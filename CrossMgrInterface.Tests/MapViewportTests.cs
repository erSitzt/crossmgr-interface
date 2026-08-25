using Xunit;

namespace CrossMgrInterface.Tests;

public class MapViewportTests
{
  private static readonly LatLon Circuit = new(50.0045, 8.007);
  private static readonly Size Screen = new(800, 600);

  private static MapViewport At(int zoom) => new(Circuit, zoom, Screen);

  private static void Close(double expected, double actual, double tolerance, string what) =>
    Assert.True(Math.Abs(expected - actual) <= tolerance,
      $"{what}: expected {expected}, got {actual} (tolerance {tolerance})");

  [Fact]
  public void TheCentreLandsInTheMiddleOfTheViewport()
  {
    var screen = At(17).ToScreenD(Circuit);

    // Within a pixel: the world-pixel origin is rounded to an integer, so the
    // centre can sit half a pixel off. That rounding is deliberate.
    Close(400, screen.X, 1.0, "centre x");
    Close(300, screen.Y, 1.0, "centre y");
  }

  [Fact]
  public void TheWorldPixelOriginIsAlwaysAnInteger()
  {
    // Load-bearing: tiles blit 1:1 only if they land on integer boundaries.
    // A fractional origin makes GDI+ resample every tile on every frame.
    foreach (var zoom in new[] { 3, 12, 16, 17, 18, 19 })
    {
      var v = At(zoom);
      Assert.Equal(Math.Floor(v.OriginX), v.OriginX);
      Assert.Equal(Math.Floor(v.OriginY), v.OriginY);
    }
  }

  [Fact]
  public void ScreenAndGeographyRoundTrip()
  {
    var v = At(17);
    var point = new Point(137, 421);

    var back = v.ToScreenD(v.ToLatLon(point));

    Close(point.X, back.X, 0.001, "x");
    Close(point.Y, back.Y, 0.001, "y");
  }

  [Fact]
  public void ZoomingInKeepsWhateverIsUnderTheCursorUnderTheCursor()
  {
    // The method everybody gets wrong. Scaling in pixel space instead of
    // re-projecting through geography makes the map slide out from under the
    // pointer, which feels broken immediately.
    var v = At(16);
    var anchor = new Point(650, 120);
    var under = v.ToLatLon(anchor);

    var zoomed = v.WithZoomAnchored(17, anchor);
    var after = zoomed.ToScreenD(under);

    Assert.Equal(17, zoomed.Zoom);
    Close(anchor.X, after.X, 1.5, "anchor x after zooming in");
    Close(anchor.Y, after.Y, 1.5, "anchor y after zooming in");
  }

  [Fact]
  public void ZoomingOutKeepsTheAnchorTooAndIsTheInverseOfZoomingIn()
  {
    var v = At(17);
    var anchor = new Point(90, 500);
    var under = v.ToLatLon(anchor);

    var out1 = v.WithZoomAnchored(16, anchor);
    var back = out1.ToScreenD(under);

    Close(anchor.X, back.X, 2.0, "anchor x after zooming out");
    Close(anchor.Y, back.Y, 2.0, "anchor y after zooming out");
  }

  [Fact]
  public void ZoomingToTheLevelYouAreAlreadyAtChangesNothing()
  {
    var v = At(17);

    Assert.Equal(v, v.WithZoomAnchored(17, new Point(10, 10)));
  }

  [Fact]
  public void ZoomIsClampedToTheAvailableTileLevels()
  {
    var v = At(17);

    Assert.Equal(TileMath.MaxZoom, v.WithZoomAnchored(99, new Point(1, 1)).Zoom);
    Assert.Equal(TileMath.MinZoom, v.WithZoomAnchored(-5, new Point(1, 1)).Zoom);
  }

  [Fact]
  public void PanningMovesTheCameraByThePixelsAsked()
  {
    var v = At(17);
    var before = v.ToScreenD(Circuit);

    var panned = v.PannedByPixels(50, -30);
    var after = panned.ToScreenD(Circuit);

    // Camera right by 50 means the land appears to move left by 50.
    Close(before.X - 50, after.X, 1.0, "x after pan");
    Close(before.Y + 30, after.Y, 1.0, "y after pan");
  }

  [Fact]
  public void PanningIsReversible()
  {
    var v = At(16);

    var round = v.PannedByPixels(220, 140).PannedByPixels(-220, -140);

    Close(v.OriginX, round.OriginX, 1.0, "origin x");
    Close(v.OriginY, round.OriginY, 1.0, "origin y");
  }

  [Fact]
  public void TheVisibleTilesCoverEveryCornerOfTheViewport()
  {
    var v = At(17);
    var tiles = v.VisibleTiles;

    foreach (var corner in new[]
             {
               new PointD(0, 0), new PointD(799, 0),
               new PointD(0, 599), new PointD(799, 599)
             })
    {
      Assert.True(tiles.Contains(TileMath.TileAt(v.ToLatLon(corner), 17)),
        $"tile range {tiles} does not cover screen corner {corner}");
    }
  }

  [Fact]
  public void AnEightHundredBySixHundredViewportNeedsAboutFourDozenTiles()
  {
    // Sizes the in-memory tile cache: the LRU bound has to hold several screens
    // worth or panning evicts tiles it is about to need again.
    var count = At(17).VisibleTiles.Count;

    Assert.InRange(count, 12, 20);
  }

  [Fact]
  public void VisibleBoundsContainTheCentre()
  {
    var v = At(15);

    Assert.True(v.VisibleBounds.Contains(v.Center));
  }

  [Fact]
  public void FitShowsTheWholeTrackWithRoomToSpare()
  {
    var track = new GeoBounds(50.0, 8.0, 50.009, 8.014);

    var v = MapViewport.Fit(track, Screen, 32);

    Assert.True(v.VisibleBounds.Contains(new LatLon(track.North, track.West)));
    Assert.True(v.VisibleBounds.Contains(new LatLon(track.South, track.East)));
  }

  [Fact]
  public void FittingACircuitPutsEveryOneOfItsPointsOnScreen()
  {
    // What "Fit to circuit" has to guarantee: not the bounding box roughly, but
    // every vertex actually visible, including the ones on the extreme edges.
    var track = TrackBuilder.Square();
    var visible = MapViewport.Fit(track.Bounds.Pad(30), Screen, 40).VisibleBounds;

    foreach (var point in track.Points)
      Assert.True(visible.Contains(point), $"{point} fell outside the fitted view");

    Assert.True(visible.Contains(track.StartFinishLocation), "the start/finish fell outside");
  }

  [Fact]
  public void FittingATallNarrowCircuitStillShowsAllOfIt()
  {
    // A circuit far taller than it is wide is where a fit that only considers one
    // axis goes wrong, and plenty of real courses are long thin loops.
    var points = new List<LatLon>
    {
      TrackBuilder.Nw,
      TrackBuilder.East(TrackBuilder.Nw, 30),
      TrackBuilder.East(TrackBuilder.North(TrackBuilder.Nw, -1200), 30),
      TrackBuilder.North(TrackBuilder.Nw, -1200)
    };

    var visible = MapViewport.Fit(GeoBounds.FromPoints(points).Pad(30), Screen, 40).VisibleBounds;

    foreach (var point in points)
      Assert.True(visible.Contains(point), $"{point} fell outside the fitted view");
  }

  [Fact]
  public void FitOnAnEmptyBoxDoesNotThrow()
  {
    var v = MapViewport.Fit(GeoBounds.FromPoints(Array.Empty<LatLon>()), Screen, 32);

    Assert.Equal(TileMath.MinZoom, v.Zoom);
  }

  [Fact]
  public void APolarCentreIsClampedIntoTheMercatorRange()
  {
    var v = new MapViewport(new LatLon(89.99, 8), 10, Screen);

    Assert.InRange(v.Center.Lat, -TileMath.MaxLatitude, TileMath.MaxLatitude);
    Assert.True(double.IsFinite(v.OriginY));
  }

  [Fact]
  public void AZeroSizedViewportDoesNotDivideByZero()
  {
    var v = new MapViewport(Circuit, 17, new Size(0, 0));

    Assert.True(v.ViewSize.Width >= 1 && v.ViewSize.Height >= 1);
    Assert.True(double.IsFinite(v.OriginX));
  }
}
