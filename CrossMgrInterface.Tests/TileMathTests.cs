using Xunit;

namespace CrossMgrInterface.Tests;

public class TileMathTests
{
  private static void Close(double expected, double actual, double tolerance, string what) =>
    Assert.True(Math.Abs(expected - actual) <= tolerance,
      $"{what}: expected {expected}, got {actual} (tolerance {tolerance})");

  [Fact]
  public void TheOriginProjectsToTheMiddleOfTheWorld()
  {
    var m = TileMath.ToMercator(new LatLon(0, 0));

    Close(0.5, m.X, 1e-12, "mercator x");
    Close(0.5, m.Y, 1e-12, "mercator y");
  }

  [Fact]
  public void MercatorRoundTripsBackToDegrees()
  {
    var original = new LatLon(50.1234, 8.5678);

    var back = TileMath.ToLatLon(TileMath.ToMercator(original));

    Close(original.Lat, back.Lat, 1e-9, "latitude");
    Close(original.Lon, back.Lon, 1e-9, "longitude");
  }

  [Fact]
  public void LatitudesBeyondTheMercatorLimitAreClampedRatherThanRunningToInfinity()
  {
    // Without the clamp this produces an infinite world pixel and GDI+ throws
    // out of the paint handler.
    var m = TileMath.ToMercator(new LatLon(89.9, 0));

    Assert.True(double.IsFinite(m.Y));
    Assert.InRange(m.Y, 0.0, 1.0);
  }

  [Fact]
  public void TheWholeWorldIsOneTileAtZoomZero()
  {
    Assert.Equal(256.0, TileMath.MapSizePixels(0));
    Assert.Equal(new TileId(0, 0, 0), TileMath.TileAt(new LatLon(50, 8), 0));
  }

  [Fact]
  public void ZoomOneSplitsTheWorldIntoFourQuadrants()
  {
    // Northern hemisphere is y=0, western is x=0.
    Assert.Equal(new TileId(1, 0, 0), TileMath.TileAt(new LatLon(45, -90), 1));
    Assert.Equal(new TileId(1, 1, 0), TileMath.TileAt(new LatLon(45, 90), 1));
    Assert.Equal(new TileId(1, 0, 1), TileMath.TileAt(new LatLon(-45, -90), 1));
    Assert.Equal(new TileId(1, 1, 1), TileMath.TileAt(new LatLon(-45, 90), 1));
  }

  [Fact]
  public void ATileContainsItsOwnCentre()
  {
    var tile = new TileId(16, 34212, 22162);
    var bounds = TileMath.TileBounds(tile);

    Assert.True(bounds.Contains(bounds.Center));
    Assert.Equal(tile, TileMath.TileAt(bounds.Center, 16));
  }

  [Fact]
  public void AParentTileCoversItsChild()
  {
    var child = new TileId(17, 68424, 44324);
    var parent = child.Parent;

    Assert.Equal(16, parent.Z);
    Assert.Equal(new TileId(16, 34212, 22162), parent);
    Assert.True(TileMath.TileBounds(parent).Contains(TileMath.TileBounds(child).Center));
  }

  [Fact]
  public void GroundResolutionHalvesWithEachZoomLevel()
  {
    var z14 = TileMath.MetresPerPixel(0, 14);
    var z15 = TileMath.MetresPerPixel(0, 15);

    Close(z14 / 2, z15, 1e-9, "metres per pixel");
  }

  [Fact]
  public void GroundResolutionShrinksWithTheCosineOfLatitude()
  {
    // 50N: cos = 0.6428. This is what the pre-cache tile estimate is built on.
    var equator = TileMath.MetresPerPixel(0, 17);
    var atFifty = TileMath.MetresPerPixel(50, 17);

    Close(equator * Math.Cos(50 * Math.PI / 180), atFifty, 1e-9, "metres per pixel");
    Close(0.77, atFifty, 0.02, "zoom 17 at 50N is about 0.77 m/px");
  }

  [Fact]
  public void ZoomToFitPicksTheLargestZoomThatStillShowsTheWholeBox()
  {
    // Roughly 1km square at 50N.
    var bounds = new GeoBounds(50.0, 8.0, 50.009, 8.014);
    var viewport = new Size(800, 600);

    var zoom = TileMath.ZoomToFit(bounds, viewport, 32);

    // It must fit...
    var fitted = new MapViewport(bounds.Center, zoom, viewport);
    Assert.True(fitted.VisibleBounds.Contains(new LatLon(bounds.North, bounds.West)));
    Assert.True(fitted.VisibleBounds.Contains(new LatLon(bounds.South, bounds.East)));

    // ...and one level closer must not.
    var tooClose = new MapViewport(bounds.Center, zoom + 1, viewport);
    Assert.False(tooClose.VisibleBounds.Contains(new LatLon(bounds.North, bounds.West)));
  }

  [Fact]
  public void ZoomToFitOnADegenerateBoxZoomsOutRatherThanIntoAStreet()
  {
    var empty = new GeoBounds(50, 8, 50, 8);

    Assert.Equal(TileMath.MinZoom, TileMath.ZoomToFit(empty, new Size(800, 600), 32));
  }

  [Fact]
  public void ATypicalCircuitCostsAFewHundredTilesToCacheOffline()
  {
    // The number the pre-cache dialog quotes. A 1km square at 50N over z14-18
    // should land in the low hundreds - well under anything the OSM tile usage
    // policy would call bulk downloading. If this ever jumps by an order of
    // magnitude, the default zoom range is wrong.
    var circuit = new GeoBounds(50.0, 8.0, 50.009, 8.014);

    var count = TileMath.TileCount(circuit, 14, 18);

    Assert.InRange(count, 20, 400);
  }

  [Fact]
  public void TilesInARangeComeOutNearestTheCentreFirst()
  {
    // The fetch queue drains in this order, and the operator is looking at the
    // middle of the map. Row-major order fills the top edge first, which reads
    // as broken.
    var range = new TileRange(16, 100, 200, 104, 204);
    var ordered = range.Tiles().ToList();

    Assert.Equal(25, ordered.Count);
    Assert.Equal(new TileId(16, 102, 202), ordered[0]);
    Assert.Equal(25, ordered.Distinct().Count());
  }

  [Fact]
  public void InflatingARangeStopsAtTheEdgeOfTheWorld()
  {
    var corner = new TileRange(2, 0, 0, 0, 0);

    var grown = corner.Inflate(1);

    Assert.Equal(new TileRange(2, 0, 0, 1, 1), grown);
  }

  [Fact]
  public void TileValidityRejectsCoordinatesOutsideTheZoomLevel()
  {
    Assert.True(TileMath.IsValid(new TileId(1, 1, 1)));
    Assert.False(TileMath.IsValid(new TileId(1, 2, 0)));
    Assert.False(TileMath.IsValid(new TileId(1, 0, -1)));
    Assert.False(TileMath.IsValid(new TileId(TileMath.MaxZoom + 1, 0, 0)));
  }
}
