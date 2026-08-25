using Xunit;

namespace CrossMgrInterface.Tests;

public class GeoMathTests
{
  private const double Fifty = 50.0;
  private static readonly double CosFifty = GeoMath.CosLatitude(Fifty);

  private static void Close(double expected, double actual, double tolerance, string what) =>
    Assert.True(Math.Abs(expected - actual) <= tolerance,
      $"{what}: expected {expected}, got {actual} (tolerance {tolerance})");

  [Fact]
  public void OneDegreeOfLatitudeIsAboutOneHundredAndElevenKilometres()
  {
    var d = GeoMath.DistanceMetres(new LatLon(50, 8), new LatLon(51, 8));

    Close(111195, d, 50, "one degree of latitude");
  }

  [Fact]
  public void ADegreeOfLongitudeShrinksWithLatitude()
  {
    var atEquator = GeoMath.DistanceMetres(new LatLon(0, 8), new LatLon(0, 9));
    var atFifty = GeoMath.DistanceMetres(new LatLon(50, 8), new LatLon(50, 9));

    Close(atEquator * CosFifty, atFifty, 100, "one degree of longitude at 50N");
  }

  [Fact]
  public void DistanceToSelfIsZero()
  {
    Assert.Equal(0, GeoMath.DistanceMetres(new LatLon(50, 8), new LatLon(50, 8)));
  }

  [Fact]
  public void BearingIsReportedClockwiseFromNorth()
  {
    var from = new LatLon(50, 8);

    Close(0, GeoMath.BearingDegrees(from, new LatLon(50.01, 8)), 0.01, "due north");
    Close(90, GeoMath.BearingDegrees(from, new LatLon(50, 8.01)), 0.01, "due east");
    Close(180, GeoMath.BearingDegrees(from, new LatLon(49.99, 8)), 0.01, "due south");
    Close(270, GeoMath.BearingDegrees(from, new LatLon(50, 7.99)), 0.01, "due west");
  }

  [Fact]
  public void PlanarHeadingUsesTheSameClockwiseFromNorthConvention()
  {
    // Pinned deliberately. A rotated arrowhead glyph consumes this directly, so
    // a silent 90-degree convention flip is invisible in review and obvious on
    // screen - every rider arrow would point across the track instead of along it.
    var from = new LatLon(Fifty, 8);

    Close(0, GeoMath.PlanarHeadingDegrees(from, new LatLon(50.01, 8), CosFifty), 1e-9, "north");
    Close(90, GeoMath.PlanarHeadingDegrees(from, new LatLon(50, 8.01), CosFifty), 1e-9, "east");
    Close(180, GeoMath.PlanarHeadingDegrees(from, new LatLon(49.99, 8), CosFifty), 1e-9, "south");
    Close(270, GeoMath.PlanarHeadingDegrees(from, new LatLon(50, 7.99), CosFifty), 1e-9, "west");
  }

  [Fact]
  public void PlanarHeadingOfAZeroLengthStepIsNorthRatherThanNotANumber()
  {
    var p = new LatLon(Fifty, 8);

    Assert.Equal(0, GeoMath.PlanarHeadingDegrees(p, p, CosFifty));
  }

  [Fact]
  public void ThePlanarApproximationAgreesWithHaversineAcrossACircuit()
  {
    // The two are used for different jobs and must not disagree materially at
    // circuit scale, or the arc length the solver divides by would not match the
    // line the renderer draws. About 1.4km diagonal here.
    var a = new LatLon(Fifty, 8.0);
    var b = new LatLon(50.009, 8.014);

    var haversine = GeoMath.DistanceMetres(a, b);
    var planar = GeoMath.PlanarDistanceMetres(a, b, CosFifty);

    Assert.True(Math.Abs(haversine - planar) / haversine < 0.001,
      $"planar {planar:F2}m vs haversine {haversine:F2}m differ by more than 0.1%");
  }

  [Fact]
  public void PlanarDeltaPutsEastOnXAndNorthOnY()
  {
    var origin = new LatLon(Fifty, 8);

    var north = GeoMath.PlanarDeltaMetres(origin, new LatLon(50.001, 8), CosFifty);
    var east = GeoMath.PlanarDeltaMetres(origin, new LatLon(Fifty, 8.001), CosFifty);

    Close(0, north.X, 1e-9, "due north has no east component");
    Assert.True(north.Y > 0, "north is positive Y");
    Close(0, east.Y, 1e-9, "due east has no north component");
    Assert.True(east.X > 0, "east is positive X");
  }

  [Fact]
  public void InterpolatingHalfwayLandsOnTheMidpoint()
  {
    var a = new LatLon(50, 8);
    var b = new LatLon(51, 9);

    var mid = GeoMath.Interpolate(a, b, 0.5);

    Close(50.5, mid.Lat, 1e-12, "latitude");
    Close(8.5, mid.Lon, 1e-12, "longitude");
  }

  [Fact]
  public void ProjectingOntoASegmentFindsTheFootOfThePerpendicular()
  {
    // The probe sits beside the midpoint of a north-south segment. The answer
    // must be the midpoint parameter, not the nearer endpoint - this is the whole
    // reason the start/finish line is an offset along the loop and not a vertex.
    var a = new LatLon(Fifty, 8);
    var b = new LatLon(50.002, 8);
    var probe = new LatLon(50.001, 8.0005);

    var t = GeoMath.ProjectOntoSegment(a, b, probe, CosFifty, out var offset);

    Close(0.5, t, 1e-6, "segment parameter");
    Close(GeoMath.PlanarDistanceMetres(new LatLon(50.001, 8), probe, CosFifty), offset, 0.01,
      "perpendicular offset");
  }

  [Fact]
  public void ProjectionClampsToTheSegmentRatherThanRunningOffTheEnd()
  {
    var a = new LatLon(Fifty, 8);
    var b = new LatLon(50.002, 8);

    Assert.Equal(0, GeoMath.ProjectOntoSegment(a, b, new LatLon(49.99, 8), CosFifty, out _));
    Assert.Equal(1, GeoMath.ProjectOntoSegment(a, b, new LatLon(50.02, 8), CosFifty, out _));
  }

  [Fact]
  public void ProjectingOntoADegenerateSegmentReturnsTheDistanceToIt()
  {
    var p = new LatLon(Fifty, 8);
    var probe = new LatLon(50.001, 8);

    var t = GeoMath.ProjectOntoSegment(p, p, probe, CosFifty, out var offset);

    Assert.Equal(0, t);
    Close(GeoMath.PlanarDistanceMetres(p, probe, CosFifty), offset, 1e-6, "offset");
  }

  [Fact]
  public void AnglesNormaliseIntoZeroToThreeSixty()
  {
    Assert.Equal(350, GeoMath.Normalise360(-10));
    Assert.Equal(10, GeoMath.Normalise360(370));
    Assert.Equal(0, GeoMath.Normalise360(360));
    Assert.Equal(180, GeoMath.Normalise360(-180));
  }
}
