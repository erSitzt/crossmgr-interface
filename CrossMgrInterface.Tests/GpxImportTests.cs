using System.Globalization;
using Xunit;

namespace CrossMgrInterface.Tests;

public class GpxImportTests
{
  private const string Gpx11 = "http://www.topografix.com/GPX/1/1";
  private const string Gpx10 = "http://www.topografix.com/GPX/1/0";

  private static readonly double CosLat = GeoMath.CosLatitude(TrackBuilder.BaseLat);

  private static string Coord(double v) => v.ToString("F8", CultureInfo.InvariantCulture);

  /// <summary>A GPX document with one track, one segment.</summary>
  private static string Gpx(IEnumerable<LatLon> points, string? ns = Gpx11, string name = "Test Loop")
  {
    var xmlns = ns is null ? "" : $" xmlns=\"{ns}\"";
    var body = string.Join("\n", points.Select(p =>
      $"      <trkpt lat=\"{Coord(p.Lat)}\" lon=\"{Coord(p.Lon)}\"/>"));

    return $"<gpx version=\"1.1\"{xmlns}>\n  <trk>\n    <name>{name}</name>\n    <trkseg>\n{body}\n    </trkseg>\n  </trk>\n</gpx>";
  }

  /// <summary>A dense path all the way round the square, starting at the north-west corner.</summary>
  private static List<LatLon> DenseSquare(int perSide, int laps = 1)
  {
    var corners = new[] { TrackBuilder.Nw, TrackBuilder.Ne, TrackBuilder.Se, TrackBuilder.Sw };
    var points = new List<LatLon>();

    for (var lap = 0; lap < laps; lap++)
      for (var c = 0; c < 4; c++)
        for (var i = 0; i < perSide; i++)
          points.Add(GeoMath.Interpolate(corners[c], corners[(c + 1) % 4], (double)i / perSide));

    return points;
  }

  /// <summary>A circle sampled far more finely than any circuit needs, with a little wobble.</summary>
  private static List<LatLon> NoisyCircle(int count, double radiusMetres, double wobbleMetres)
  {
    var points = new List<LatLon>(count);

    for (var i = 0; i < count; i++)
    {
      // Deterministic pseudo-noise: a real random number would make failures
      // impossible to reproduce.
      var wobble = Math.Sin(i * 12.9898) * wobbleMetres;
      var angle = 2 * Math.PI * i / count;
      var r = radiusMetres + wobble;

      points.Add(new LatLon(
        TrackBuilder.BaseLat + r * Math.Cos(angle) / GeoMath.MetresPerDegreeLatitude,
        TrackBuilder.BaseLon + r * Math.Sin(angle) / (GeoMath.MetresPerDegreeLatitude * CosLat)));
    }

    return points;
  }

  /// <summary>
  /// A circle whose every sample alternates in and out by a fixed amount, so that
  /// every single point is a genuine corner well beyond the default tolerance.
  /// Nothing a GPS produces looks like this; it exists to force the escalation path.
  /// </summary>
  private static List<LatLon> ZigzagCircle(int count, double radiusMetres, double amplitudeMetres)
  {
    var points = new List<LatLon>(count);

    for (var i = 0; i < count; i++)
    {
      var angle = 2 * Math.PI * i / count;
      var r = radiusMetres + (i % 2 == 0 ? amplitudeMetres : -amplitudeMetres);

      points.Add(new LatLon(
        TrackBuilder.BaseLat + r * Math.Cos(angle) / GeoMath.MetresPerDegreeLatitude,
        TrackBuilder.BaseLon + r * Math.Sin(angle) / (GeoMath.MetresPerDegreeLatitude * CosLat)));
    }

    return points;
  }

  private static void HasAllFourCorners(IReadOnlyList<LatLon> points)
  {
    foreach (var (corner, name) in new[]
             {
               (TrackBuilder.Nw, "north-west"), (TrackBuilder.Ne, "north-east"),
               (TrackBuilder.Se, "south-east"), (TrackBuilder.Sw, "south-west")
             })
      Assert.True(points.Any(p => TrackBuilder.Metres(p, corner) < 1.0),
        $"the {name} corner was thinned away");
  }

  // ---- Parsing -------------------------------------------------------------

  [Fact]
  public void ReadsATrackFromGpxEleven()
  {
    var result = GpxTrackImporter.ImportXml(Gpx(DenseSquare(20)));

    Assert.True(result.Success, result.Summary);
    Assert.Equal("Test Loop", result.Track!.Name);
  }

  [Fact]
  public void ReadsATrackFromGpxTenWithItsOlderNamespace()
  {
    var result = GpxTrackImporter.ImportXml(Gpx(DenseSquare(20), Gpx10));

    Assert.True(result.Success, result.Summary);
  }

  [Fact]
  public void ReadsATrackFromAFileWithNoNamespaceAtAll()
  {
    // Plenty of exporters get the namespace wrong or omit it. Matching on local
    // name is what makes all three cases the same code path.
    var result = GpxTrackImporter.ImportXml(Gpx(DenseSquare(20), ns: null));

    Assert.True(result.Success, result.Summary);
  }

  [Fact]
  public void JoinsSeveralTrackSegmentsIntoOneLoop()
  {
    var points = DenseSquare(20);
    var half = points.Count / 2;

    string Seg(IEnumerable<LatLon> ps) =>
      "<trkseg>" + string.Join("", ps.Select(p =>
        $"<trkpt lat=\"{Coord(p.Lat)}\" lon=\"{Coord(p.Lon)}\"/>")) + "</trkseg>";

    var xml = $"<gpx xmlns=\"{Gpx11}\"><trk><name>Split</name>" +
              Seg(points.Take(half)) + Seg(points.Skip(half)) + "</trk></gpx>";

    var result = GpxTrackImporter.ImportXml(xml);

    Assert.True(result.Success, result.Summary);
    HasAllFourCorners(result.Track!.Points);
  }

  [Fact]
  public void AcceptsARouteWhenThereIsNoTrack()
  {
    // What you get when the loop was drawn in a mapping tool rather than ridden.
    var xml = $"<gpx xmlns=\"{Gpx11}\"><rte><name>Drawn</name>" +
              string.Join("", DenseSquare(20).Select(p =>
                $"<rtept lat=\"{Coord(p.Lat)}\" lon=\"{Coord(p.Lon)}\"/>")) +
              "</rte></gpx>";

    var result = GpxTrackImporter.ImportXml(xml);

    Assert.True(result.Success, result.Summary);
    Assert.Equal("Drawn", result.Track!.Name);
    Assert.Contains(result.Warnings, w => w.Contains("route"));
  }

  [Fact]
  public void IgnoresLooseWaypointsBecauseTheyAreMarkersNotTheLine()
  {
    var xml = $"<gpx xmlns=\"{Gpx11}\">" +
              "<wpt lat=\"0.0\" lon=\"0.0\"><name>Feed zone</name></wpt>" +
              "<wpt lat=\"10.0\" lon=\"10.0\"><name>Finish</name></wpt>" +
              "<trk><name>Real</name><trkseg>" +
              string.Join("", DenseSquare(20).Select(p =>
                $"<trkpt lat=\"{Coord(p.Lat)}\" lon=\"{Coord(p.Lon)}\"/>")) +
              "</trkseg></trk></gpx>";

    var result = GpxTrackImporter.ImportXml(xml);

    Assert.True(result.Success, result.Summary);

    // A waypoint at the origin would blow the bounding box open across half the planet.
    Assert.All(result.Track!.Points, p => Assert.InRange(p.Lat, 49.0, 51.0));
  }

  [Fact]
  public void CoordinatesAreReadWithTheInvariantCultureNotTheMachinesOwn()
  {
    // This machine runs a German locale, where double.Parse("50.123") reads as
    // fifty thousand one hundred and twenty three. Pinned explicitly so the trap
    // stays closed on an English-locale machine too.
    var original = CultureInfo.CurrentCulture;
    try
    {
      CultureInfo.CurrentCulture = new CultureInfo("de-DE");

      var result = GpxTrackImporter.ImportXml(Gpx(DenseSquare(20)));

      Assert.True(result.Success, result.Summary);
      Assert.All(result.Track!.Points, p => Assert.InRange(p.Lat, 49.0, 51.0));
    }
    finally
    {
      CultureInfo.CurrentCulture = original;
    }
  }

  [Fact]
  public void RejectsAFileWithTooFewPointsToBeALoop()
  {
    var result = GpxTrackImporter.ImportXml(Gpx(new[] { TrackBuilder.Nw, TrackBuilder.Ne }));

    Assert.False(result.Success);
    Assert.Contains(result.Warnings, w => w.Contains("at least"));
  }

  [Fact]
  public void RejectsCoordinatesOutsideThePlanet()
  {
    var xml = $"<gpx xmlns=\"{Gpx11}\"><trk><trkseg>" +
              "<trkpt lat=\"95.0\" lon=\"8.0\"/>" +
              "<trkpt lat=\"50.0\" lon=\"200.0\"/>" +
              "<trkpt lat=\"50.0\" lon=\"8.0\"/>" +
              "<trkpt lat=\"50.001\" lon=\"8.001\"/>" +
              "</trkseg></trk></gpx>";

    var result = GpxTrackImporter.ImportXml(xml);

    Assert.False(result.Success);
  }

  [Fact]
  public void SomethingThatIsNotXmlAtAllFailsWithAReadableMessage()
  {
    var result = GpxTrackImporter.ImportXml("bib,name\n27,Test Rider");

    Assert.False(result.Success);
    Assert.NotEmpty(result.Warnings);
  }

  // ---- Thinning ------------------------------------------------------------

  [Fact]
  public void ThinningKeepsTheCornersAndDropsTheStraights()
  {
    // 400 points describing a square come back as five or so: the four corners,
    // plus the final point, which Douglas-Peucker always keeps.
    var dense = DenseSquare(100);

    var thinned = GpxTrackImporter.Thin(dense, GpxTrackImporter.DefaultToleranceMetres, CosLat);

    Assert.InRange(thinned.Count, 4, 6);
    HasAllFourCorners(thinned);
  }

  [Fact]
  public void ThinningNeverMovesTheLineFurtherThanTheTolerance()
  {
    // The correctness property of Douglas-Peucker, and what justifies choosing 4m:
    // a circuit's rideable line is four to eight metres wide, so the thinned
    // polyline stays on the track surface and a dot never appears in a field.
    var dense = NoisyCircle(2000, 240, 1.0);
    const double tolerance = GpxTrackImporter.DefaultToleranceMetres;

    var thinned = GpxTrackImporter.Thin(dense, tolerance, CosLat);
    var geometry = TrackGeometry.Build(thinned);

    foreach (var dropped in dense)
    {
      geometry.NearestFraction(dropped, out var offset);
      Assert.True(offset <= tolerance + 0.01,
        $"a dropped point ended up {offset:F2}m from the thinned line (tolerance {tolerance}m)");
    }
  }

  [Fact]
  public void AFiveThousandPointTraceThinsToSomethingDrawable()
  {
    var trace = NoisyCircle(5000, 240, 0.5);

    var result = GpxTrackImporter.ImportXml(Gpx(trace));

    Assert.True(result.Success, result.Summary);
    Assert.Equal(5000, result.RawPointCount);
    Assert.InRange(result.KeptPointCount, 8, GpxTrackImporter.MaxPoints);
  }

  [Fact]
  public void TheToleranceIsRaisedUntilThePointCapIsMet()
  {
    // A trace whose every sample is a genuine corner at 4m. Rather than truncate -
    // which would cut a corner off the circuit - the tolerance backs off.
    var jagged = ZigzagCircle(1500, 300, 6.0);

    var result = GpxTrackImporter.ImportXml(Gpx(jagged));

    Assert.True(result.Success, result.Summary);
    Assert.True(result.KeptPointCount <= GpxTrackImporter.MaxPoints,
      $"kept {result.KeptPointCount} points, over the cap");
    Assert.True(result.ToleranceMetres > GpxTrackImporter.DefaultToleranceMetres,
      "the tolerance should have been raised, or this test proves nothing");
    Assert.Contains(result.Warnings, w => w.Contains("dense"));
  }

  // ---- Shaping -------------------------------------------------------------

  [Fact]
  public void AThreeLapTraceIsTrimmedToOneLap()
  {
    // The most common real-world GPX. Untrimmed it draws as overlapping spaghetti
    // and the operator has no idea why.
    var result = GpxTrackImporter.ImportXml(Gpx(DenseSquare(100, laps: 3)));

    Assert.True(result.Success, result.Summary);
    Assert.Equal(3, result.LapsDetected);
    Assert.InRange(result.Track!.LengthMetres, 950, 1050);
    Assert.Contains(result.Warnings, w => w.Contains("3 laps"));
  }

  [Fact]
  public void ASingleLapTraceIsLeftAlone()
  {
    var result = GpxTrackImporter.ImportXml(Gpx(DenseSquare(100)));

    Assert.True(result.Success, result.Summary);
    Assert.Equal(1, result.LapsDetected);
    Assert.DoesNotContain(result.Warnings, w => w.Contains("laps"));
  }

  [Fact]
  public void ATrailingPointOnTopOfTheFirstIsDropped()
  {
    var points = DenseSquare(100);
    points.Add(points[0]);

    var result = GpxTrackImporter.ImportXml(Gpx(points));

    Assert.True(result.Success, result.Summary);
    Assert.True(TrackBuilder.Metres(result.Track!.Points[0], result.Track.Points[^1]) > 1.0,
      "the repeated first point should have been removed");
  }

  [Fact]
  public void StandingStillAtTheStartCollapsesToOnePoint()
  {
    var points = new List<LatLon>();
    for (var i = 0; i < 50; i++) points.Add(TrackBuilder.North(TrackBuilder.Nw, i * 0.02));
    points.AddRange(DenseSquare(100));

    var result = GpxTrackImporter.ImportXml(Gpx(points));

    Assert.True(result.Success, result.Summary);
    Assert.InRange(result.Track!.Points.Count, 4, 8);
  }

  [Fact]
  public void ImportLeavesTheStartFinishFlaggedForSomeoneToPlace()
  {
    // The trace's first point is where the rider pressed "go", which is essentially
    // never the start line. Defaulting silently would be quietly wrong in the one
    // way that ruins every rider position on the map.
    var result = GpxTrackImporter.ImportXml(Gpx(DenseSquare(100)));

    Assert.True(result.Track!.StartFinish.NeedsReview);
    Assert.Contains(result.Warnings, w => w.Contains("chequered"));
  }

  [Fact]
  public void TheImportedTrackIsImmediatelyUsable()
  {
    var result = GpxTrackImporter.ImportXml(Gpx(DenseSquare(100)));

    Assert.True(result.Track!.IsUsable);
    Assert.InRange(result.Track.LengthMetres, 950, 1050);
  }
}
