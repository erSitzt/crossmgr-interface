namespace CrossMgrInterface.Tests;

/// <summary>
/// Circuits for tests to measure against.
///
/// The workhorse is a 1000m square: four vertices, 250m a side, at 50N. A square
/// is the right fixture because every answer is checkable by arithmetic rather
/// than by trusting the code under test - quarter fractions land exactly on
/// corners, headings are exactly 0/90/180/270, and half a side is exactly 125m.
///
/// The loop runs CLOCKWISE from the north-west corner: NW -> NE -> SE -> SW.
/// </summary>
internal static class TrackBuilder
{
  internal const double BaseLat = 50.0;
  internal const double BaseLon = 8.0;
  internal const double SideMetres = 250.0;
  internal const double PerimeterMetres = 1000.0;

  internal static readonly double DeltaLat = SideMetres / GeoMath.MetresPerDegreeLatitude;

  internal static readonly double DeltaLon =
    SideMetres / (GeoMath.MetresPerDegreeLatitude * GeoMath.CosLatitude(BaseLat));

  internal static LatLon Nw => new(BaseLat + DeltaLat, BaseLon);
  internal static LatLon Ne => new(BaseLat + DeltaLat, BaseLon + DeltaLon);
  internal static LatLon Se => new(BaseLat, BaseLon + DeltaLon);
  internal static LatLon Sw => new(BaseLat, BaseLon);

  /// <summary>Midpoint of the south side - where several tests park the finish line.</summary>
  internal static LatLon SouthMid => new(BaseLat, BaseLon + DeltaLon / 2);

  internal static LatLon NorthMid => new(BaseLat + DeltaLat, BaseLon + DeltaLon / 2);

  internal static List<LatLon> SquarePoints() => new() { Nw, Ne, Se, Sw };

  internal static TrackGeometry SquareGeometry() => TrackGeometry.Build(SquarePoints());

  /// <summary>The square with its start/finish on the north-west corner, at fraction 0.</summary>
  internal static TrackDefinition Square(string name = "Test Circuit")
  {
    var track = new TrackDefinition { Name = name };
    track.SetPoints(SquarePoints());
    track.StartFinish.PlaceAt(track.Geometry, Nw);
    return track;
  }

  /// <summary>The square with its start/finish half way along the south side, at fraction 0.625.</summary>
  internal static TrackDefinition SquareWithFinishOnTheSouthSide()
  {
    var track = Square();
    track.StartFinish.PlaceAt(track.Geometry, SouthMid);
    return track;
  }

  /// <summary>A regular polygon approximating a circle, for arc-length work.</summary>
  internal static List<LatLon> CirclePoints(double radiusMetres, int segments)
  {
    var cosLat = GeoMath.CosLatitude(BaseLat);
    var points = new List<LatLon>(segments);

    for (var i = 0; i < segments; i++)
    {
      var angle = 2 * Math.PI * i / segments;
      var north = radiusMetres * Math.Cos(angle);
      var east = radiusMetres * Math.Sin(angle);

      points.Add(new LatLon(
        BaseLat + north / GeoMath.MetresPerDegreeLatitude,
        BaseLon + east / (GeoMath.MetresPerDegreeLatitude * cosLat)));
    }

    return points;
  }

  /// <summary>Ground distance between two positions, in the square's local plane.</summary>
  internal static double Metres(LatLon a, LatLon b) =>
    GeoMath.PlanarDistanceMetres(a, b, GeoMath.CosLatitude(BaseLat));

  /// <summary>A point <paramref name="metres"/> due north of another.</summary>
  internal static LatLon North(LatLon from, double metres) =>
    new(from.Lat + metres / GeoMath.MetresPerDegreeLatitude, from.Lon);

  /// <summary>A point <paramref name="metres"/> due east of another.</summary>
  internal static LatLon East(LatLon from, double metres) =>
    new(from.Lat, from.Lon + metres / (GeoMath.MetresPerDegreeLatitude * GeoMath.CosLatitude(BaseLat)));

  // ---- Riders and race state ----------------------------------------------

  /// <summary>A fixed instant to hang timings off. There is no injectable clock.</summary>
  internal static readonly DateTime Noon = new(2025, 8, 6, 12, 0, 0);

  internal static RiderMapDatum Datum(
    string tag = "A",
    DateTime? lastCrossing = null,
    int laps = 3,
    double? paceSeconds = 40,
    bool dnf = false,
    bool dns = false,
    DateTime? dnfTime = null,
    int finalAllowedLap = int.MaxValue,
    string number = "27",
    string category = "Elite") => new(
      tag,
      $"#{number} Test Rider",
      number,
      "Surname",
      category,
      lastCrossing ?? Noon,
      laps,
      paceSeconds is { } p ? TimeSpan.FromSeconds(p) : null,
      dnf,
      dns,
      dnfTime,
      finalAllowedLap);

  internal static RaceTiming Racing(double? medianPaceSeconds = null) => new(
    RaceStarted: true,
    RaceFinished: false,
    FieldMedianLapTime: medianPaceSeconds is { } m ? TimeSpan.FromSeconds(m) : null);

  internal static RaceTiming NotStarted => new(false, false, null);

  internal static RaceTiming Finished => new(true, true, null);

  internal static TrackSector Sector(string name, double fraction) => new()
  {
    Name = name,
    Start = new TrackAnchor { Fraction = fraction }
  };
}
