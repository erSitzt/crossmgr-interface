namespace CrossMgrInterface;

public enum MapHitKind { RiderDot, RiderCluster, TrackVertex, SectorHandle, StartFinish }

/// <summary>
/// One clickable thing on the map, in screen coordinates, rebuilt on every paint.
///
/// The same idea as LapChartElement, with two deliberate differences. Hit-testing
/// walks the list BACKWARDS, so the topmost drawn element wins - the lap chart's
/// FirstOrDefault returns the bottom-most, which is wrong here because rider dots
/// overlap constantly. And every point-like element is inflated to at least
/// <see cref="MinimumPickSize"/>, because a six-pixel dot that demands six pixels
/// of accuracy is unusable with a trackpad.
/// </summary>
public sealed class MapHitElement
{
  public const int MinimumPickSize = 20;

  public Rectangle Bounds { get; init; }
  public MapHitKind Kind { get; init; }
  public string TagId { get; init; } = "";
  public IReadOnlyList<string> ClusterTagIds { get; init; } = Array.Empty<string>();
  public int VertexIndex { get; init; } = -1;
  public int SectorIndex { get; init; } = -1;
  public LatLon Location { get; init; }

  public static Rectangle Around(PointF centre, float radius)
  {
    var size = Math.Max(MinimumPickSize, (int)Math.Ceiling(radius * 2));
    return new Rectangle(
      (int)Math.Round(centre.X - size / 2.0),
      (int)Math.Round(centre.Y - size / 2.0),
      size, size);
  }
}

/// <summary>
/// What to write beside a rider dot. A flags enum rather than a list of modes,
/// because the useful combinations are not a sequence: some operators want the
/// running order, some want start numbers, a commentator wants names, and plenty
/// want two of the three.
/// </summary>
[Flags]
public enum MapLabelParts
{
  None = 0,
  Position = 1 << 0,
  Number = 1 << 1,
  Name = 1 << 2
}

/// <summary>A rider, ready to draw. Built by the view from a <see cref="TrackPosition"/>.</summary>
public readonly record struct MapRiderMarker(
  string TagId,

  /// <summary>Start number, or the transponder id when no rider list has been loaded.</summary>
  string RiderNumber,

  string Label,

  /// <summary>Surname where there is one - a full name is unreadable at map scale.</summary>
  string ShortName,

  string Category,
  LatLon Position,
  double HeadingDegrees,
  int Rank,
  TrackPositionState State,
  double Fraction,
  string? Badge,
  bool Highlighted);

/// <summary>
/// How many riders are in one sector right now, and who leads it.
///
/// This is what keeps a big field readable. Dots degrade as the field grows -
/// 250 of them on a 1.4km loop is a chain of blobs - but "58 in the Back
/// Straight, led by 27" reads the same whether there are twenty riders or three
/// hundred.
/// </summary>
public readonly record struct MapSectorInfo(
  int Index, string Name, Color Color, int RiderCount, string? LeaderNumber);

/// <summary>One line of the running order shown beside the map under a top-N filter.</summary>
public readonly record struct TrackLeaderRow(
  int Position, string TagId, string Number, string Name, int Laps, string State);

/// <summary>A stretch of the loop drawn in its own colour.</summary>
public readonly record struct MapSectorSpan(
  string Name, Color Color, double StartFraction, double EndFraction);

public sealed class MapClickEventArgs : EventArgs
{
  public required LatLon Location { get; init; }
  public required Point Screen { get; init; }
  public required bool CtrlHeld { get; init; }
}

public sealed class MapPickEventArgs : EventArgs
{
  public required MapHitElement Element { get; init; }
  public required Point Screen { get; init; }
  public required bool DoubleClick { get; init; }
}

public sealed class MapVertexDragEventArgs : EventArgs
{
  public required int Index { get; init; }
  public required LatLon Location { get; init; }
  public required bool Finished { get; init; }
}
