using System.Globalization;
using System.Xml.Linq;
using Xunit;

namespace CrossMgrInterface.Tests;

public class TrackExportTests
{
  private static TrackDefinition Sample()
  {
    var track = TrackBuilder.SquareWithFinishOnTheSouthSide();
    track.Name = "Steinbergpark";
    track.AddSector("The Climb", TrackBuilder.Ne);
    track.AddSector("Back Straight", TrackBuilder.Sw);
    return track;
  }

  // ---- GPX ------------------------------------------------------------------

  [Fact]
  public void TheExportedFileIsGpxAnyToolCanRead()
  {
    var doc = XDocument.Parse(TrackGpxExporter.ToGpx(Sample()));

    Assert.Equal("gpx", doc.Root!.Name.LocalName);
    Assert.Equal("1.1", doc.Root.Attribute("version")!.Value);
    Assert.Equal("http://www.topografix.com/GPX/1/1", doc.Root.Name.NamespaceName);
    Assert.NotEmpty(doc.Descendants().Where(e => e.Name.LocalName == "trkpt"));
  }

  [Fact]
  public void CoordinatesAreWrittenWithTheInvariantCulture()
  {
    // On this machine's German locale the default would emit "52,1508" - a comma,
    // which makes the attribute unreadable by every GPX parser including ours.
    var original = CultureInfo.CurrentCulture;
    try
    {
      CultureInfo.CurrentCulture = new CultureInfo("de-DE");

      var gpx = TrackGpxExporter.ToGpx(Sample());

      Assert.DoesNotContain("lat=\"50,", gpx);
      Assert.Contains("lat=\"50.", gpx);
    }
    finally
    {
      CultureInfo.CurrentCulture = original;
    }
  }

  [Fact]
  public void TheLoopIsClosedSoOtherToolsDrawItAsALoop()
  {
    var track = Sample();
    var doc = XDocument.Parse(TrackGpxExporter.ToGpx(track));

    var points = doc.Descendants().Where(e => e.Name.LocalName == "trkpt").ToList();

    Assert.Equal(track.Points.Count + 1, points.Count);
    Assert.Equal(points[0].Attribute("lat")!.Value, points[^1].Attribute("lat")!.Value);
    Assert.Equal(points[0].Attribute("lon")!.Value, points[^1].Attribute("lon")!.Value);
  }

  [Fact]
  public void TheStartFinishAndSectorsAreWrittenAsWaypointsForAHumanToSee()
  {
    // GPX has no way to express either as structure, so this is the best that can
    // be done: somebody opening the file can at least see where they were.
    var doc = XDocument.Parse(TrackGpxExporter.ToGpx(Sample()));

    var names = doc.Descendants()
      .Where(e => e.Name.LocalName == "wpt")
      .Select(w => w.Elements().First(e => e.Name.LocalName == "name").Value)
      .ToList();

    Assert.Contains("Start/Finish", names);
    Assert.Contains("The Climb", names);
    Assert.Contains("Back Straight", names);
  }

  [Fact]
  public void ExportedGpxComesBackInWithTheSameLoop()
  {
    var track = Sample();

    var result = GpxTrackImporter.ImportXml(TrackGpxExporter.ToGpx(track));

    Assert.True(result.Success, result.Summary);
    Assert.Equal(track.Points.Count, result.Track!.Points.Count);
    Assert.Equal(track.LengthMetres, result.Track.LengthMetres, 1);

    for (var i = 0; i < track.Points.Count; i++)
      Assert.True(TrackBuilder.Metres(track.Points[i], result.Track.Points[i]) < 0.1,
        $"point {i} moved on the round trip");
  }

  [Fact]
  public void AGpxRoundTripLosesTheStartFinishAndSaysSo()
  {
    // The honest limitation of the format, pinned so nobody assumes otherwise:
    // waypoints are not read back as structure, so the line needs placing again.
    var track = Sample();

    var result = GpxTrackImporter.ImportXml(TrackGpxExporter.ToGpx(track));

    Assert.True(result.Track!.StartFinish.NeedsReview);
    Assert.Empty(result.Track.Sectors);
  }

  [Fact]
  public void ATrackWithNoGeometryStillProducesAValidFile()
  {
    var empty = new TrackDefinition { Name = "Nothing drawn yet" };

    var doc = XDocument.Parse(TrackGpxExporter.ToGpx(empty));

    Assert.Equal("gpx", doc.Root!.Name.LocalName);
    Assert.Empty(doc.Descendants().Where(e => e.Name.LocalName == "wpt"));
  }

  // ---- Circuit file ---------------------------------------------------------

  [Fact]
  public void ACircuitFileRoundTripKeepsEverythingGpxCannot()
  {
    var track = Sample();

    var back = TrackStore.ImportJson(TrackStore.ExportJson(track))!;

    Assert.Equal(track.Name, back.Name);
    Assert.Equal(track.Points.Count, back.Points.Count);
    Assert.Equal(track.LengthMetres, back.LengthMetres, 3);

    Assert.False(back.StartFinish.NeedsReview);
    Assert.Equal(track.StartFinish.Fraction, back.StartFinish.Fraction, 9);

    Assert.Equal(new[] { "The Climb", "Back Straight" }, back.Sectors.Select(s => s.Name));
    Assert.Equal(track.Sectors[0].ColorArgb, back.Sectors[0].ColorArgb);
    Assert.Equal(track.Sectors[0].Start.Fraction, back.Sectors[0].Start.Fraction, 9);
  }
}
