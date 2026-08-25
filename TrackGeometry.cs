namespace CrossMgrInterface;

/// <summary>A position on the track: where it is, and which way the rider is facing.</summary>
public readonly record struct TrackPoint(LatLon Location, double HeadingDegrees);

/// <summary>
/// The measured form of a circuit: cumulative arc length along a closed polyline,
/// and the lookups built on it.
///
/// Immutable and built once per edit, because the hot path runs the other way: a
/// 250-rider field at 8 frames a second is 2000 PointAtFraction calls a second,
/// and each one has to be a binary search plus a lerp, not a walk of the polyline.
///
/// The ring is OPEN - the last point is not a repeat of the first, and the closing
/// segment back to point 0 is implicit. GPX exporters disagree about whether to
/// repeat the first point, so imports normalise to this and nothing downstream has
/// to ask whether the loop is closed.
///
/// Lengths are measured in a local tangent plane about the track's centroid
/// latitude rather than by haversine. That is deliberate: the renderer draws
/// straight lines between vertices and interpolates linearly, so measuring with
/// the same approximation is what keeps a rider dot exactly on the drawn line.
/// See GeoMath for the error bound.
/// </summary>
public sealed class TrackGeometry
{
  private readonly Segment[] _segments;

  /// <summary>
  /// Distance to the start of each segment, with a final entry holding the total.
  /// The sentinel is what lets the binary search run without a modulo in the loop.
  /// </summary>
  private readonly double[] _cumulative;

  public IReadOnlyList<LatLon> Points { get; }
  public double TotalLengthMetres { get; }

  /// <summary>Longitude scale factor for the local plane. Shared with anything measuring against this track.</summary>
  public double CosLat0 { get; }

  /// <summary>False for a track too short or too sparse to place a rider on.</summary>
  public bool IsUsable => Points.Count >= 3 && TotalLengthMetres > 0;

  public static readonly TrackGeometry Empty = Build(Array.Empty<LatLon>());

  private readonly record struct Segment(
    LatLon Start, LatLon End, double LengthMetres, double HeadingDegrees);

  private TrackGeometry(IReadOnlyList<LatLon> points, Segment[] segments, double[] cumulative, double cosLat0)
  {
    Points = points;
    _segments = segments;
    _cumulative = cumulative;
    CosLat0 = cosLat0;
    TotalLengthMetres = cumulative.Length > 0 ? cumulative[^1] : 0;
  }

  public static TrackGeometry Build(IReadOnlyList<LatLon> points)
  {
    var copy = points.ToArray();

    if (copy.Length < 2)
      return new TrackGeometry(copy, Array.Empty<Segment>(), new[] { 0.0 }, 1.0);

    var cosLat0 = GeoMath.CosLatitude(GeoMath.CentroidLatitude(copy));

    var segments = new Segment[copy.Length];
    var cumulative = new double[copy.Length + 1];

    for (var i = 0; i < copy.Length; i++)
    {
      var a = copy[i];
      var b = copy[(i + 1) % copy.Length];

      var length = GeoMath.PlanarDistanceMetres(a, b, cosLat0);
      segments[i] = new Segment(a, b, length, GeoMath.PlanarHeadingDegrees(a, b, cosLat0));
      cumulative[i + 1] = cumulative[i] + length;
    }

    return new TrackGeometry(copy, segments, cumulative, cosLat0);
  }

  /// <summary>Wraps any fraction into [0,1). 1.25, -0.75 and 0.25 all mean the same place.</summary>
  public static double NormaliseFraction(double fraction)
  {
    if (double.IsNaN(fraction) || double.IsInfinity(fraction)) return 0;
    var n = fraction % 1.0;
    return n < 0 ? n + 1.0 : n;
  }

  public LatLon LocationAtFraction(double fraction) => PointAtFraction(fraction).Location;

  /// <summary>
  /// The point that far round the loop by ARC LENGTH, not by vertex count. The
  /// distinction is the whole feature: a rider halfway through their lap has
  /// covered half the distance, not passed half the polyline vertices.
  /// </summary>
  public TrackPoint PointAtFraction(double fraction)
  {
    if (_segments.Length == 0)
      return new TrackPoint(Points.Count > 0 ? Points[0] : default, 0);

    if (TotalLengthMetres <= 0)
      return new TrackPoint(Points[0], 0);

    var target = NormaliseFraction(fraction) * TotalLengthMetres;
    var i = SegmentIndexAt(target);
    var segment = _segments[i];

    var t = segment.LengthMetres > 0
      ? (target - _cumulative[i]) / segment.LengthMetres
      : 0;

    return new TrackPoint(
      GeoMath.Interpolate(segment.Start, segment.End, t),
      segment.HeadingDegrees);
  }

  /// <summary>
  /// Where a point on the ground falls along the loop, and how far off it was.
  ///
  /// Projects onto the polyline, NOT to the nearest vertex. That is what lets the
  /// start/finish line sit at the painted line rather than at whichever vertex the
  /// GPS happened to sample nearest to it - an error of up to half the local point
  /// spacing, applied permanently to every rider's position.
  /// </summary>
  public double NearestFraction(LatLon probe, out double offTrackMetres)
  {
    offTrackMetres = double.MaxValue;

    if (_segments.Length == 0 || TotalLengthMetres <= 0)
    {
      offTrackMetres = 0;
      return 0;
    }

    var bestFraction = 0.0;

    for (var i = 0; i < _segments.Length; i++)
    {
      var segment = _segments[i];
      var t = GeoMath.ProjectOntoSegment(segment.Start, segment.End, probe, CosLat0, out var offset);

      if (offset >= offTrackMetres) continue;

      offTrackMetres = offset;
      bestFraction = (_cumulative[i] + t * segment.LengthMetres) / TotalLengthMetres;
    }

    return NormaliseFraction(bestFraction);
  }

  /// <summary>
  /// The polyline from one fraction forward to another: both interpolated ends
  /// plus every real vertex in between.
  ///
  /// Used to paint a sector in its own colour along the exact line the track
  /// follows. Sampling at fixed intervals instead would cut the corners, which is
  /// visible precisely where a sector boundary usually sits.
  ///
  /// A span of zero means the whole loop, since that is the only reading that
  /// makes sense for a single sector covering everything.
  /// </summary>
  public List<LatLon> PointsBetween(double fromFraction, double toFraction)
  {
    var result = new List<LatLon>();
    if (_segments.Length == 0 || TotalLengthMetres <= 0) return result;

    var from = NormaliseFraction(fromFraction);
    var span = NormaliseFraction(toFraction - fromFraction);
    if (span <= 0) span = 1.0;

    result.Add(PointAtFraction(from).Location);

    var startDistance = from * TotalLengthMetres;
    var target = span * TotalLengthMetres;

    var index = SegmentIndexAt(startDistance);
    var covered = _cumulative[index + 1] - startDistance;

    for (var guard = 0; covered < target && guard <= _segments.Length; guard++)
    {
      result.Add(_segments[index].End);
      index = (index + 1) % _segments.Length;
      covered += _segments[index].LengthMetres;
    }

    result.Add(PointAtFraction(from + span).Location);
    return result;
  }

  /// <summary>
  /// Distance travelled going forwards round the loop from one fraction to
  /// another. 0.9 to 0.1 is a short hop across the line, not a lap backwards.
  /// </summary>
  public double ForwardDistance(double fromFraction, double toFraction) =>
    NormaliseFraction(toFraction - fromFraction) * TotalLengthMetres;

  /// <summary>
  /// Which sector a fraction falls in, or -1 when the track has none.
  ///
  /// Sectors are defined by their start only, so sector i runs up to the start of
  /// sector i+1 and the last one wraps round to the first. A fraction before the
  /// first boundary therefore belongs to the LAST sector - the wrap case, and the
  /// one this always gets wrong if it is not written down.
  ///
  /// Boundaries are half-open: a fraction exactly on a boundary belongs to the
  /// sector it starts.
  /// </summary>
  public static int SectorIndexAt(double fraction, IReadOnlyList<TrackSector> sectors)
  {
    if (sectors.Count == 0) return -1;

    var f = NormaliseFraction(fraction);
    var index = -1;

    for (var i = 0; i < sectors.Count; i++)
    {
      if (sectors[i].Start.Fraction <= f) index = i;
      else break;
    }

    // Before the first boundary means inside the sector that wraps past the end.
    return index >= 0 ? index : sectors.Count - 1;
  }

  private int SegmentIndexAt(double distanceMetres)
  {
    // _cumulative is ascending with a total sentinel at the end, so a plain
    // binary search lands either on a segment start or between two of them.
    var found = Array.BinarySearch(_cumulative, distanceMetres);
    var index = found >= 0 ? found : ~found - 1;

    return Math.Clamp(index, 0, _segments.Length - 1);
  }
}
