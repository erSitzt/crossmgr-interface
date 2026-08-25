using Xunit;

namespace CrossMgrInterface.Tests;

public class TrackGeometryTests
{
  private static void Close(double expected, double actual, double tolerance, string what) =>
    Assert.True(Math.Abs(expected - actual) <= tolerance,
      $"{what}: expected {expected}, got {actual} (tolerance {tolerance})");

  private static void AtSamePlace(LatLon expected, LatLon actual, double toleranceMetres, string what) =>
    Assert.True(TrackBuilder.Metres(expected, actual) <= toleranceMetres,
      $"{what}: expected {expected}, got {actual} " +
      $"({TrackBuilder.Metres(expected, actual):F2}m apart, tolerance {toleranceMetres}m)");

  // ---- Arc length ----------------------------------------------------------

  [Fact]
  public void TheLengthOfASquareLoopIsTheSumOfItsSides()
  {
    var g = TrackBuilder.SquareGeometry();

    Close(TrackBuilder.PerimeterMetres, g.TotalLengthMetres, 1.0, "perimeter");
  }

  [Fact]
  public void TheClosingSegmentCountsTowardsTheLength()
  {
    // The classic off-by-one. Four points describe four sides, not three: the
    // segment from the last point back to the first is implicit, so an
    // implementation that walks pairs and stops gives 750m instead of 1000m.
    var g = TrackBuilder.SquareGeometry();

    Assert.True(g.TotalLengthMetres > 900,
      $"only {g.TotalLengthMetres:F0}m - the closing segment is missing");
  }

  [Fact]
  public void ARepeatedFinalPointDoesNotChangeTheLength()
  {
    // GPX exporters disagree about whether to repeat the first point at the end.
    // Either way the loop is the same length; the extra segment is zero long.
    var points = TrackBuilder.SquarePoints();
    points.Add(points[0]);

    var withRepeat = TrackGeometry.Build(points);

    Close(TrackBuilder.SquareGeometry().TotalLengthMetres, withRepeat.TotalLengthMetres, 0.01,
      "perimeter with a repeated final point");
  }

  [Fact]
  public void ArcLengthIsMonotonicAllTheWayRound()
  {
    // Property test over the whole loop. Catches binary-search boundary bugs at
    // segment joins, which otherwise show up as a dot flickering at one corner.
    var g = TrackGeometry.Build(TrackBuilder.CirclePoints(200, 24));
    var previous = g.PointAtFraction(0).Location;
    double travelled = 0;

    for (var i = 1; i <= 5000; i++)
    {
      var here = g.PointAtFraction(i / 5000.0).Location;
      var step = TrackBuilder.Metres(previous, here);

      Assert.True(step >= 0, $"went backwards at sample {i}");
      travelled += step;
      previous = here;
    }

    Close(g.TotalLengthMetres, travelled, g.TotalLengthMetres * 0.01, "distance walked round the loop");
  }

  // ---- PointAtFraction -----------------------------------------------------

  [Fact]
  public void FractionZeroIsTheFirstPoint()
  {
    AtSamePlace(TrackBuilder.Nw, TrackBuilder.SquareGeometry().LocationAtFraction(0), 0.01, "fraction 0");
  }

  [Fact]
  public void FractionOneWrapsBackToFractionZero()
  {
    var g = TrackBuilder.SquareGeometry();

    AtSamePlace(g.LocationAtFraction(0), g.LocationAtFraction(1.0), 0.01, "fraction 1");
  }

  [Fact]
  public void FractionsAboveOneAndBelowZeroWrapModuloOne()
  {
    var g = TrackBuilder.SquareGeometry();
    var quarter = g.LocationAtFraction(0.25);

    AtSamePlace(quarter, g.LocationAtFraction(1.25), 0.01, "fraction 1.25");
    AtSamePlace(quarter, g.LocationAtFraction(-0.75), 0.01, "fraction -0.75");
    AtSamePlace(quarter, g.LocationAtFraction(3.25), 0.01, "fraction 3.25");
  }

  [Fact]
  public void QuarterFractionsLandOnTheCornersOfTheSquare()
  {
    // The load-bearing arc-length assertion: a quarter of the way round by
    // DISTANCE, which for a square is exactly one corner along.
    var g = TrackBuilder.SquareGeometry();

    AtSamePlace(TrackBuilder.Nw, g.LocationAtFraction(0.00), 0.05, "fraction 0.00");
    AtSamePlace(TrackBuilder.Ne, g.LocationAtFraction(0.25), 0.05, "fraction 0.25");
    AtSamePlace(TrackBuilder.Se, g.LocationAtFraction(0.50), 0.05, "fraction 0.50");
    AtSamePlace(TrackBuilder.Sw, g.LocationAtFraction(0.75), 0.05, "fraction 0.75");
  }

  [Fact]
  public void PointAtFractionInterpolatesWithinASegment()
  {
    // An eighth of the way round is half of the first side.
    var g = TrackBuilder.SquareGeometry();

    AtSamePlace(TrackBuilder.NorthMid, g.LocationAtFraction(0.125), 0.05, "fraction 0.125");
  }

  [Fact]
  public void HeadingIsConstantAlongASideAndTurnsAtEachCorner()
  {
    var g = TrackBuilder.SquareGeometry();

    // The square runs clockwise from the north-west corner: east, south, west, north.
    Close(90, g.PointAtFraction(0.05).HeadingDegrees, 0.01, "along the north side");
    Close(90, g.PointAtFraction(0.20).HeadingDegrees, 0.01, "still along the north side");
    Close(180, g.PointAtFraction(0.30).HeadingDegrees, 0.01, "down the east side");
    Close(270, g.PointAtFraction(0.55).HeadingDegrees, 0.01, "along the south side");
    Close(0, g.PointAtFraction(0.80).HeadingDegrees, 0.01, "up the west side");
  }

  [Fact]
  public void HeadingIsReportedClockwiseFromNorth()
  {
    // Pinned separately from the values above because a rotated arrowhead glyph
    // consumes this directly: a silent convention flip would point every rider
    // arrow across the track instead of along it, and nothing would fail to build.
    var g = TrackBuilder.SquareGeometry();

    var eastwards = g.PointAtFraction(0.1).HeadingDegrees;

    Close(90, eastwards, 0.01, "travelling east must read as 90 degrees, not -90 or 270");
  }

  // ---- NearestFraction -----------------------------------------------------

  [Fact]
  public void NearestFractionProjectsOntoTheSegmentRatherThanSnappingToAVertex()
  {
    // THE test that justifies storing the start/finish as an offset along the loop
    // instead of as a vertex index. The probe is beside the middle of the north
    // side; the answer must be 0.125, not the 0.0 or 0.25 a vertex snap would give.
    var g = TrackBuilder.SquareGeometry();
    var probe = TrackBuilder.North(TrackBuilder.NorthMid, 10);

    var fraction = g.NearestFraction(probe, out _);

    Close(0.125, fraction, 0.001, "fraction of a probe beside the middle of a side");
  }

  [Fact]
  public void NearestFractionReportsHowFarOffTheTrackTheProbeWas()
  {
    var g = TrackBuilder.SquareGeometry();
    var probe = TrackBuilder.North(TrackBuilder.NorthMid, 10);

    g.NearestFraction(probe, out var offset);

    Close(10, offset, 0.05, "off-track distance");
  }

  [Fact]
  public void APointOnTheTrackReportsNoOffset()
  {
    var g = TrackBuilder.SquareGeometry();

    g.NearestFraction(g.LocationAtFraction(0.4), out var offset);

    Close(0, offset, 0.05, "off-track distance for a point on the line");
  }

  [Fact]
  public void NearestFractionRoundTripsThroughPointAtFraction()
  {
    var g = TrackBuilder.SquareGeometry();

    foreach (var f in new[] { 0.0, 0.05, 0.125, 0.33, 0.5, 0.625, 0.9, 0.99 })
    {
      var back = g.NearestFraction(g.LocationAtFraction(f), out _);
      Close(f, back, 0.001, $"round trip through fraction {f}");
    }
  }

  // ---- ForwardDistance -----------------------------------------------------

  [Fact]
  public void ForwardDistanceWrapsRoundTheLoopRatherThanGoingNegative()
  {
    var g = TrackBuilder.SquareGeometry();

    // 0.9 to 0.1 is a short hop across the line, not 800m backwards.
    Close(200, g.ForwardDistance(0.9, 0.1), 1.0, "0.9 to 0.1");
    Close(250, g.ForwardDistance(0.0, 0.25), 1.0, "0.0 to 0.25");
    Close(0, g.ForwardDistance(0.4, 0.4), 0.01, "no distance at all");
  }

  // ---- Degenerate input ----------------------------------------------------

  [Fact]
  public void AnEmptyGeometryAnswersEveryQuestionWithoutThrowing()
  {
    var g = TrackGeometry.Empty;

    Assert.False(g.IsUsable);
    Assert.Equal(0, g.TotalLengthMetres);
    Assert.Equal(0, g.NearestFraction(new LatLon(50, 8), out _));

    var point = g.PointAtFraction(0.5);
    Assert.Equal(0, point.HeadingDegrees);
  }

  [Fact]
  public void ATwoPointTrackIsNotUsableButDoesNotThrow()
  {
    var g = TrackGeometry.Build(new List<LatLon> { TrackBuilder.Nw, TrackBuilder.Ne });

    Assert.False(g.IsUsable);
    Assert.True(double.IsFinite(g.PointAtFraction(0.3).Location.Lat));
  }

  [Fact]
  public void NotANumberIsTreatedAsTheStartLineRatherThanPropagating()
  {
    var g = TrackBuilder.SquareGeometry();

    AtSamePlace(TrackBuilder.Nw, g.LocationAtFraction(double.NaN), 0.01, "NaN fraction");
    AtSamePlace(TrackBuilder.Nw, g.LocationAtFraction(double.PositiveInfinity), 0.01, "infinite fraction");
  }
}
