namespace CrossMgrInterface;

/// <summary>
/// A geographic position in WGS84 degrees.
///
/// This is the shared coordinate type for the whole track-map feature: the track
/// model serialises it to tracks.json, and the renderer projects it to the screen.
/// The property names are deliberately short - a thinned circuit is a few hundred
/// of these in one JSON file.
/// </summary>
public readonly record struct LatLon(double Lat, double Lon);

/// <summary>
/// A double-precision 2D point.
///
/// System.Drawing only offers PointF, and float carries about four pixels of
/// error at zoom 18 world scale (67 million pixels across) - enough to put a
/// rider dot visibly off the track. Everything upstream of the final cast to
/// screen coordinates uses this instead.
/// </summary>
public readonly record struct PointD(double X, double Y);

/// <summary>
/// Web Mercator position on the unit square: X and Y in [0,1), Y increasing
/// southwards. Zoom-independent, so it is the natural intermediate between
/// degrees and pixels.
/// </summary>
public readonly record struct MercatorPoint(double X, double Y);

/// <summary>One raster tile in the standard slippy-map scheme.</summary>
public readonly record struct TileId(int Z, int X, int Y)
{
  /// <summary>
  /// The tile one zoom level out that contains this one. Walking up this chain
  /// is how a missing tile borrows imagery from a coarser level it already has.
  /// </summary>
  public TileId Parent => new(Z - 1, X >> 1, Y >> 1);

  public override string ToString() => $"{Z}/{X}/{Y}";
}

/// <summary>A rectangular block of tiles at one zoom level.</summary>
public readonly record struct TileRange(int Z, int MinX, int MinY, int MaxX, int MaxY)
{
  public int Width => MaxX - MinX + 1;
  public int Height => MaxY - MinY + 1;
  public int Count => Width * Height;

  public bool Contains(TileId t) =>
    t.Z == Z && t.X >= MinX && t.X <= MaxX && t.Y >= MinY && t.Y <= MaxY;

  /// <summary>
  /// Every tile in the range, nearest the centre first.
  ///
  /// The ordering is the point: it is what the fetch queue consumes, and the
  /// operator is looking at the middle of the map, so the middle should fill in
  /// first. Row-major order fills the top edge first and looks broken.
  /// </summary>
  public IEnumerable<TileId> Tiles()
  {
    var cx = (MinX + MaxX) / 2.0;
    var cy = (MinY + MaxY) / 2.0;

    var all = new List<TileId>(Count);
    for (var y = MinY; y <= MaxY; y++)
      for (var x = MinX; x <= MaxX; x++)
        all.Add(new TileId(Z, x, y));

    return all.OrderBy(t => (t.X - cx) * (t.X - cx) + (t.Y - cy) * (t.Y - cy));
  }

  /// <summary>Grows the range by <paramref name="tiles"/> on every side, clamped to the world.</summary>
  public TileRange Inflate(int tiles)
  {
    var max = (1 << Z) - 1;
    return new TileRange(
      Z,
      Math.Max(0, MinX - tiles), Math.Max(0, MinY - tiles),
      Math.Min(max, MaxX + tiles), Math.Min(max, MaxY + tiles));
  }
}

/// <summary>An axis-aligned geographic rectangle. Not antimeridian-aware; a bike circuit is not.</summary>
public readonly record struct GeoBounds(double South, double West, double North, double East)
{
  public LatLon Center => new((South + North) / 2, (West + East) / 2);
  public double LatSpan => North - South;
  public double LonSpan => East - West;
  public bool IsEmpty => North < South || East < West;

  public bool Contains(LatLon p) =>
    p.Lat >= South && p.Lat <= North && p.Lon >= West && p.Lon <= East;

  public GeoBounds Extend(LatLon p) => new(
    Math.Min(South, p.Lat), Math.Min(West, p.Lon),
    Math.Max(North, p.Lat), Math.Max(East, p.Lon));

  /// <summary>
  /// Grows the box by a margin in metres. Longitude degrees shrink with latitude,
  /// so the two axes need different conversions or the box comes out lopsided.
  /// </summary>
  public GeoBounds Pad(double metres)
  {
    if (metres <= 0) return this;

    var dLat = metres / GeoMath.MetresPerDegreeLatitude;
    var cosLat = Math.Cos(Center.Lat * Math.PI / 180);
    var dLon = metres / (GeoMath.MetresPerDegreeLatitude * Math.Max(0.01, cosLat));

    return new GeoBounds(
      Math.Max(-TileMath.MaxLatitude, South - dLat), West - dLon,
      Math.Min(TileMath.MaxLatitude, North + dLat), East + dLon);
  }

  /// <summary>Tightest box containing every point. Returns an empty box for an empty input.</summary>
  public static GeoBounds FromPoints(IEnumerable<LatLon> points)
  {
    double s = double.MaxValue, w = double.MaxValue;
    double n = double.MinValue, e = double.MinValue;
    var any = false;

    foreach (var p in points)
    {
      any = true;
      if (p.Lat < s) s = p.Lat;
      if (p.Lat > n) n = p.Lat;
      if (p.Lon < w) w = p.Lon;
      if (p.Lon > e) e = p.Lon;
    }

    return any ? new GeoBounds(s, w, n, e) : new GeoBounds(0, 0, -1, -1);
  }
}
