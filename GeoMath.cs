namespace CrossMgrInterface;

/// <summary>
/// Distances, bearings and the local tangent plane the track model measures in.
///
/// Deliberately free of any tile or rendering dependency, so the track domain can
/// use it without dragging the map substrate along.
///
/// Two families of function live here and they are not interchangeable:
///
///   - The haversine pair (<see cref="DistanceMetres"/>, <see cref="BearingDegrees"/>)
///     is correct anywhere on the globe. Use it for bounding boxes, pre-cache
///     estimates, and deciding whether a GPX trace closes on itself.
///
///   - The Planar* family projects onto a flat plane tangent at one reference
///     latitude. Use it for everything inside a circuit. It is what
///     TrackGeometry measures arc length with, and the choice matters: the
///     renderer interpolates linearly in lat/lon between two polyline vertices,
///     so measuring with the same linear approximation is what keeps a rider dot
///     exactly on the line that was drawn. Over 10km the two disagree by less
///     than 0.1%, which is far below the error in the pace estimate anyway.
/// </summary>
public static class GeoMath
{
  /// <summary>IUGG mean earth radius.</summary>
  public const double EarthRadiusMetres = 6371008.8;

  /// <summary>Metres per degree of latitude - constant, unlike longitude.</summary>
  public const double MetresPerDegreeLatitude = EarthRadiusMetres * Math.PI / 180.0;

  private const double Deg2Rad = Math.PI / 180.0;
  private const double Rad2Deg = 180.0 / Math.PI;

  public static double DistanceMetres(LatLon a, LatLon b)
  {
    var dLat = (b.Lat - a.Lat) * Deg2Rad;
    var dLon = (b.Lon - a.Lon) * Deg2Rad;
    var lat1 = a.Lat * Deg2Rad;
    var lat2 = b.Lat * Deg2Rad;

    var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
            Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

    return 2 * EarthRadiusMetres * Math.Asin(Math.Min(1.0, Math.Sqrt(h)));
  }

  /// <summary>Initial great-circle bearing, degrees clockwise from north, 0..360.</summary>
  public static double BearingDegrees(LatLon from, LatLon to)
  {
    var lat1 = from.Lat * Deg2Rad;
    var lat2 = to.Lat * Deg2Rad;
    var dLon = (to.Lon - from.Lon) * Deg2Rad;

    var y = Math.Sin(dLon) * Math.Cos(lat2);
    var x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);

    return Normalise360(Math.Atan2(y, x) * Rad2Deg);
  }

  /// <summary>
  /// Linear interpolation in degrees. Wrong near the poles and across the
  /// antimeridian; a bike circuit is neither, and this is the interpolation the
  /// renderer draws with, so the track model must match it.
  /// </summary>
  public static LatLon Interpolate(LatLon a, LatLon b, double t) =>
    new(a.Lat + (b.Lat - a.Lat) * t, a.Lon + (b.Lon - a.Lon) * t);

  /// <summary>
  /// The longitude scale factor for a local plane. Every Planar* call takes this
  /// rather than recomputing a cosine per segment - TrackGeometry builds it once
  /// per track and the inner loops run per rider per frame.
  /// </summary>
  public static double CosLatitude(double latitudeDegrees) =>
    Math.Cos(Math.Clamp(latitudeDegrees, -89.9, 89.9) * Deg2Rad);

  public static double CentroidLatitude(IReadOnlyList<LatLon> points)
  {
    if (points.Count == 0) return 0;
    double sum = 0;
    for (var i = 0; i < points.Count; i++) sum += points[i].Lat;
    return sum / points.Count;
  }

  /// <summary>Offset from <paramref name="origin"/> in metres: X east, Y north.</summary>
  public static PointD PlanarDeltaMetres(LatLon origin, LatLon p, double cosLat0) => new(
    (p.Lon - origin.Lon) * MetresPerDegreeLatitude * cosLat0,
    (p.Lat - origin.Lat) * MetresPerDegreeLatitude);

  public static double PlanarDistanceMetres(LatLon a, LatLon b, double cosLat0)
  {
    var dx = (b.Lon - a.Lon) * MetresPerDegreeLatitude * cosLat0;
    var dy = (b.Lat - a.Lat) * MetresPerDegreeLatitude;
    return Math.Sqrt(dx * dx + dy * dy);
  }

  /// <summary>
  /// Heading along a short segment, degrees clockwise from north, 0..360.
  ///
  /// Clockwise from north is the convention throughout this feature, because it
  /// is what a rotated arrowhead glyph needs directly. A silent 90-degree
  /// mismatch here is invisible in code review and obvious on screen, so it is
  /// pinned by test.
  /// </summary>
  public static double PlanarHeadingDegrees(LatLon from, LatLon to, double cosLat0)
  {
    var dx = (to.Lon - from.Lon) * MetresPerDegreeLatitude * cosLat0;
    var dy = (to.Lat - from.Lat) * MetresPerDegreeLatitude;
    if (dx == 0 && dy == 0) return 0;
    return Normalise360(Math.Atan2(dx, dy) * Rad2Deg);
  }

  /// <summary>
  /// Where a probe falls on the segment a->b, as a parameter in [0,1], together
  /// with how far off the segment it was. Used to place the start/finish line at
  /// an arbitrary point along the track rather than snapping it to a vertex.
  /// </summary>
  public static double ProjectOntoSegment(
    LatLon a, LatLon b, LatLon probe, double cosLat0, out double offsetMetres)
  {
    var ab = PlanarDeltaMetres(a, b, cosLat0);
    var ap = PlanarDeltaMetres(a, probe, cosLat0);

    var lenSq = ab.X * ab.X + ab.Y * ab.Y;
    if (lenSq <= 0)
    {
      offsetMetres = Math.Sqrt(ap.X * ap.X + ap.Y * ap.Y);
      return 0;
    }

    var t = Math.Clamp((ap.X * ab.X + ap.Y * ab.Y) / lenSq, 0.0, 1.0);
    var cx = ap.X - ab.X * t;
    var cy = ap.Y - ab.Y * t;
    offsetMetres = Math.Sqrt(cx * cx + cy * cy);
    return t;
  }

  /// <summary>Perpendicular distance from a probe to the segment a-b, in metres.</summary>
  public static double DistanceToSegmentMetres(LatLon a, LatLon b, LatLon probe, double cosLat0)
  {
    ProjectOntoSegment(a, b, probe, cosLat0, out var offset);
    return offset;
  }

  public static double Normalise360(double degrees)
  {
    var d = degrees % 360.0;
    return d < 0 ? d + 360.0 : d;
  }
}
