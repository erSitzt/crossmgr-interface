namespace CrossMgrInterface;

/// <summary>
/// The map camera: what part of the world is on screen, and the conversions
/// between screen pixels and geography.
///
/// Immutable. Every camera change produces a new one, which makes anchored zoom
/// trivially correct (no accumulating state to get out of step) and makes the
/// whole thing unit-testable with no Control anywhere.
///
/// The load-bearing detail is that the world-pixel origin is rounded to an
/// integer once, here in the constructor, and never adjusted again. Three things
/// fall out of that:
///
///   1. Tiles land on integer pixel boundaries, so GDI+ blits them 1:1 instead of
///      resampling every tile every frame. That is the classic reason a hand
///      written map renderer is slow, and it is closed by construction here.
///   2. Hit-test rectangles come out in the same coordinates as the mouse event,
///      with no adjustment at the call site.
///   3. There is no Graphics transform to forget to reset before drawing the
///      tooltip - the mistake the lap chart had to be fixed for.
///
/// So: do not add a TranslateTransform to the map renderer. It would reintroduce
/// all three problems.
/// </summary>
public readonly struct MapViewport
{
  public LatLon Center { get; }
  public int Zoom { get; }
  public Size ViewSize { get; }

  /// <summary>World pixel of the top-left corner, integral. See the class remarks.</summary>
  public double OriginX { get; }
  public double OriginY { get; }

  public MapViewport(LatLon center, int zoom, Size viewSize)
  {
    Zoom = Math.Clamp(zoom, TileMath.MinZoom, TileMath.MaxZoom);
    ViewSize = new Size(Math.Max(1, viewSize.Width), Math.Max(1, viewSize.Height));

    Center = new LatLon(
      Math.Clamp(center.Lat, -TileMath.MaxLatitude, TileMath.MaxLatitude),
      Math.Clamp(center.Lon, -180.0, 180.0));

    var c = TileMath.ToWorldPixel(Center, Zoom);
    OriginX = Math.Round(c.X - ViewSize.Width / 2.0);
    OriginY = Math.Round(c.Y - ViewSize.Height / 2.0);
  }

  public PointD ToScreenD(LatLon p)
  {
    var w = TileMath.ToWorldPixel(p, Zoom);
    return new PointD(w.X - OriginX, w.Y - OriginY);
  }

  public PointF ToScreen(LatLon p)
  {
    var d = ToScreenD(p);
    return new PointF((float)d.X, (float)d.Y);
  }

  public LatLon ToLatLon(PointD screen) =>
    TileMath.FromWorldPixel(new PointD(OriginX + screen.X, OriginY + screen.Y), Zoom);

  public LatLon ToLatLon(Point screen) => ToLatLon(new PointD(screen.X, screen.Y));

  public double MetresPerPixel => TileMath.MetresPerPixel(Center.Lat, Zoom);

  public GeoBounds VisibleBounds
  {
    get
    {
      var nw = ToLatLon(new PointD(0, 0));
      var se = ToLatLon(new PointD(ViewSize.Width, ViewSize.Height));
      return new GeoBounds(se.Lat, nw.Lon, nw.Lat, se.Lon);
    }
  }

  /// <summary>
  /// Tiles touching the viewport. Derived from the pixel origin rather than by
  /// projecting the corners back to degrees - exact, and no round trip.
  /// </summary>
  public TileRange VisibleTiles
  {
    get
    {
      var max = (1 << Zoom) - 1;
      var minX = (int)Math.Floor(OriginX / TileMath.TileSize);
      var minY = (int)Math.Floor(OriginY / TileMath.TileSize);
      var maxX = (int)Math.Floor((OriginX + ViewSize.Width - 1) / TileMath.TileSize);
      var maxY = (int)Math.Floor((OriginY + ViewSize.Height - 1) / TileMath.TileSize);

      return new TileRange(Zoom,
        Math.Clamp(minX, 0, max), Math.Clamp(minY, 0, max),
        Math.Clamp(maxX, 0, max), Math.Clamp(maxY, 0, max));
    }
  }

  /// <summary>
  /// Moves the camera by a pixel offset. A drag passes the negated mouse delta,
  /// because dragging right should bring the land right along with the cursor.
  /// </summary>
  public MapViewport PannedByPixels(double dx, double dy)
  {
    var centre = new PointD(
      OriginX + dx + ViewSize.Width / 2.0,
      OriginY + dy + ViewSize.Height / 2.0);

    return new MapViewport(TileMath.FromWorldPixel(centre, Zoom), Zoom, ViewSize);
  }

  /// <summary>
  /// Changes zoom while keeping whatever is under <paramref name="anchor"/> under
  /// it afterwards. This is the method people get wrong: the anchor has to be
  /// resolved to geography at the OLD zoom and re-projected at the NEW one, not
  /// scaled in pixel space.
  /// </summary>
  public MapViewport WithZoomAnchored(int newZoom, Point anchor)
  {
    newZoom = Math.Clamp(newZoom, TileMath.MinZoom, TileMath.MaxZoom);
    if (newZoom == Zoom) return this;

    var anchorLatLon = ToLatLon(anchor);
    var anchorWorld = TileMath.ToWorldPixel(anchorLatLon, newZoom);

    var centre = new PointD(
      anchorWorld.X - (anchor.X - ViewSize.Width / 2.0),
      anchorWorld.Y - (anchor.Y - ViewSize.Height / 2.0));

    return new MapViewport(TileMath.FromWorldPixel(centre, newZoom), newZoom, ViewSize);
  }

  public MapViewport WithCenter(LatLon center) => new(center, Zoom, ViewSize);

  public MapViewport WithSize(Size viewSize) => new(Center, Zoom, viewSize);

  /// <summary>Camera showing all of <paramref name="bounds"/> with a pixel margin.</summary>
  public static MapViewport Fit(GeoBounds bounds, Size viewSize, int paddingPx, int maxZoom = TileMath.MaxZoom)
  {
    if (bounds.IsEmpty) return new MapViewport(new LatLon(0, 0), TileMath.MinZoom, viewSize);

    var zoom = TileMath.ZoomToFit(bounds, viewSize, paddingPx, maxZoom);
    return new MapViewport(bounds.Center, zoom, viewSize);
  }
}
