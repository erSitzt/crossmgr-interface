namespace CrossMgrInterface;

/// <summary>
/// Web Mercator (EPSG:3857) projection and slippy-map tile indexing.
///
/// Pure and side-effect free, so all of it is unit-testable without a control,
/// a network, or a Graphics. Everything the map draws ultimately comes through
/// here, so the conventions are worth stating once:
///
///   - "world pixel" means the pixel coordinate on the whole world map at a
///     given zoom, origin top-left, so it runs 0..256*2^zoom on both axes.
///   - Zoom is always an integer. Tiles then blit 1:1 with no resampling, which
///     keeps a 1px track line and two-digit rider numbers crisp on a laptop
///     screen in daylight. Smoothness during a zoom comes from stretching the
///     parent tile while the new level loads, not from fractional scaling.
/// </summary>
public static class TileMath
{
  public const int TileSize = 256;

  /// <summary>
  /// The latitude where Mercator's y runs off to infinity, atan(sinh(pi)) in
  /// degrees. Every projection here clamps to it; without the clamp a stray
  /// coordinate produces an infinite world pixel and GDI+ throws from Paint.
  /// </summary>
  public const double MaxLatitude = 85.05112877980659;

  public const int MinZoom = 0;

  /// <summary>The standard OSM raster layer has no tiles past 19.</summary>
  public const int MaxZoom = 19;

  /// <summary>Metres per pixel at the equator, zoom 0: earth circumference / 256.</summary>
  private const double EquatorMetresPerPixel = 156543.03392804097;

  public static MercatorPoint ToMercator(LatLon p)
  {
    var lat = Math.Clamp(p.Lat, -MaxLatitude, MaxLatitude) * Math.PI / 180;
    var x = (p.Lon + 180.0) / 360.0;
    var y = (1.0 - Math.Log(Math.Tan(lat) + 1.0 / Math.Cos(lat)) / Math.PI) / 2.0;
    return new MercatorPoint(x, y);
  }

  public static LatLon ToLatLon(MercatorPoint m)
  {
    var lon = m.X * 360.0 - 180.0;
    var lat = Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * m.Y))) * 180.0 / Math.PI;
    return new LatLon(lat, lon);
  }

  /// <summary>Width and height of the whole world in pixels at this zoom.</summary>
  public static double MapSizePixels(int zoom) => (double)TileSize * (1L << zoom);

  public static PointD ToWorldPixel(LatLon p, int zoom)
  {
    var m = ToMercator(p);
    var size = MapSizePixels(zoom);
    return new PointD(m.X * size, m.Y * size);
  }

  public static LatLon FromWorldPixel(PointD world, int zoom)
  {
    var size = MapSizePixels(zoom);
    return ToLatLon(new MercatorPoint(world.X / size, world.Y / size));
  }

  public static TileId TileAt(LatLon p, int zoom) => TileAtWorldPixel(ToWorldPixel(p, zoom), zoom);

  public static TileId TileAtWorldPixel(PointD world, int zoom)
  {
    var max = (1 << zoom) - 1;
    var x = (int)Math.Floor(world.X / TileSize);
    var y = (int)Math.Floor(world.Y / TileSize);
    return new TileId(zoom, Math.Clamp(x, 0, max), Math.Clamp(y, 0, max));
  }

  public static PointD TileOriginWorldPixel(TileId t) =>
    new((double)t.X * TileSize, (double)t.Y * TileSize);

  public static GeoBounds TileBounds(TileId t)
  {
    var nw = FromWorldPixel(TileOriginWorldPixel(t), t.Z);
    var se = FromWorldPixel(new PointD((t.X + 1.0) * TileSize, (t.Y + 1.0) * TileSize), t.Z);
    return new GeoBounds(se.Lat, nw.Lon, nw.Lat, se.Lon);
  }

  public static bool IsValid(TileId t)
  {
    if (t.Z < MinZoom || t.Z > MaxZoom) return false;
    var max = (1 << t.Z) - 1;
    return t.X >= 0 && t.X <= max && t.Y >= 0 && t.Y <= max;
  }

  /// <summary>
  /// Ground resolution at a latitude. Drives the scale bar, and the decision of
  /// which zoom levels are worth pre-caching for a given circuit.
  /// </summary>
  public static double MetresPerPixel(double latitude, int zoom)
  {
    var lat = Math.Clamp(latitude, -MaxLatitude, MaxLatitude) * Math.PI / 180;
    return EquatorMetresPerPixel * Math.Cos(lat) / (1L << zoom);
  }

  /// <summary>
  /// The largest integer zoom at which <paramref name="bounds"/> fits inside the
  /// viewport with a margin. Returns MinZoom for a degenerate box rather than
  /// clamping to MaxZoom, so an empty track does not open zoomed into a street.
  /// </summary>
  public static int ZoomToFit(GeoBounds bounds, Size viewport, int paddingPx, int maxZoom = MaxZoom)
  {
    var usableW = Math.Max(1, viewport.Width - 2 * paddingPx);
    var usableH = Math.Max(1, viewport.Height - 2 * paddingPx);

    var nw = ToMercator(new LatLon(bounds.North, bounds.West));
    var se = ToMercator(new LatLon(bounds.South, bounds.East));
    var spanX = Math.Abs(se.X - nw.X);
    var spanY = Math.Abs(se.Y - nw.Y);

    if (spanX <= 0 && spanY <= 0) return MinZoom;

    for (var z = Math.Min(maxZoom, MaxZoom); z >= MinZoom; z--)
    {
      var size = MapSizePixels(z);
      if (spanX * size <= usableW && spanY * size <= usableH) return z;
    }

    return MinZoom;
  }

  public static TileRange RangeFor(GeoBounds bounds, int zoom)
  {
    var nw = TileAt(new LatLon(bounds.North, bounds.West), zoom);
    var se = TileAt(new LatLon(bounds.South, bounds.East), zoom);
    return new TileRange(zoom,
      Math.Min(nw.X, se.X), Math.Min(nw.Y, se.Y),
      Math.Max(nw.X, se.X), Math.Max(nw.Y, se.Y));
  }

  /// <summary>Total tiles across an inclusive zoom range. What the pre-cache dialog quotes.</summary>
  public static int TileCount(GeoBounds bounds, int minZoom, int maxZoom)
  {
    var total = 0;
    for (var z = Math.Max(MinZoom, minZoom); z <= Math.Min(MaxZoom, maxZoom); z++)
      total += RangeFor(bounds, z).Count;
    return total;
  }
}
