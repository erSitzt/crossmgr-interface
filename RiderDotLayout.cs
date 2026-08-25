namespace CrossMgrInterface;

public sealed record PlacedDot(
  string TagId,
  PointF Centre,
  float Radius,
  TrackPositionState State,

  /// <summary>True lap progress, so the renderer can tell the amber overdue band from the red one.</summary>
  double Fraction,

  string? Label,
  string? Badge,
  PointF LabelAnchor,
  bool NeedsLeaderLine,
  bool Highlighted);

public sealed record PlacedCluster(
  PointF Centre,
  float Radius,
  int Count,
  IReadOnlyList<string> TagIds,

  /// <summary>
  /// The best-placed rider in the group. A bare count says "twelve riders
  /// somewhere here"; a count with a leader says "27 leads a group of twelve",
  /// which is the sentence a commentator actually wants.
  /// </summary>
  string? LeaderNumber);

public sealed record DotLayout(
  IReadOnlyList<PlacedDot> Dots, IReadOnlyList<PlacedCluster> Clusters);

/// <summary>
/// Arranges rider dots so a two-hundred-and-fifty rider field stays readable.
///
/// A pure function - no Graphics, no Control - so the logic that decides what the
/// operator can actually see is testable, unlike the drawing around it.
///
/// Three passes:
///   1. Riders in a bunch collapse into one counted cluster marker.
///   2. Cells holding one or two riders fan apart perpendicular to the track.
///   3. Labels are rationed, because 250 of them is a solid block of text.
///
/// Overdue riders are handled separately from all of that: they are all parked on
/// the start/finish line by the solver, so clustering them would hide exactly the
/// riders the operator most needs to pick out. They fan backwards along the track
/// instead, one dot apart, most overdue furthest back.
/// </summary>
public static class RiderDotLayout
{
  /// <summary>Cluster cell, in world pixels. About two dot diameters.</summary>
  public const int ClusterCellPx = 28;

  /// <summary>Riders in one cell before it becomes a cluster rather than dots.</summary>
  public const int ClusterThreshold = 3;

  /// <summary>Labels drawn at once. Each one is a halo, which is five DrawString calls.</summary>
  public const int MaxLabels = 20;

  public const float DotRadius = 6f;
  public const float HighlightRadius = 9f;
  private const float PairOffsetPx = 6f;
  private const float OverdueSpacingPx = 13f;
  private const int CullMarginPx = 48;

  public static DotLayout Build(
    IReadOnlyList<MapRiderMarker> riders,
    MapViewport viewport,
    string? selectedTagId,
    string? hoveredTagId,
    MapLabelParts labelParts = MapLabelParts.Number,
    int labelTopN = 3)
  {
    var dots = new List<PlacedDot>(riders.Count);
    var clusters = new List<PlacedCluster>();

    // Two groups skip clustering entirely.
    //
    // Overdue riders are all parked on the line by the solver, so collapsing them
    // would hide exactly the riders the operator most needs to pick out.
    //
    // Emphasised riders - selected, hovered, or typed into the highlight box -
    // must never disappear into a bunch either. "Where is 27?" is the whole
    // reason the highlight box exists, and answering it with a marker reading
    // "14" is no answer at all.
    var overdue = new List<MapRiderMarker>();
    var emphasised = new List<MapRiderMarker>();
    var circulating = new List<MapRiderMarker>(riders.Count);

    for (var i = 0; i < riders.Count; i++)
    {
      var rider = riders[i];

      if (rider.Highlighted || rider.TagId == selectedTagId || rider.TagId == hoveredTagId)
        emphasised.Add(rider);
      else if (rider.State is TrackPositionState.Overdue or TrackPositionState.LongOverdue)
        overdue.Add(rider);
      else
        circulating.Add(rider);
    }

    // Cells are indexed in WORLD pixels, not screen pixels. Screen cells shift
    // with the pan origin, so panning by a single pixel would reshuffle cluster
    // membership and the whole map would visibly boil. World cells are
    // pan-invariant, so a cluster changes only when riders or the zoom do.
    var cells = new Dictionary<(int X, int Y), List<MapRiderMarker>>();

    foreach (var rider in circulating)
    {
      var world = TileMath.ToWorldPixel(rider.Position, viewport.Zoom);
      var key = ((int)Math.Floor(world.X / ClusterCellPx), (int)Math.Floor(world.Y / ClusterCellPx));

      if (!cells.TryGetValue(key, out var bucket)) cells[key] = bucket = new List<MapRiderMarker>();
      bucket.Add(rider);
    }

    var bounds = new RectangleF(
      -CullMarginPx, -CullMarginPx,
      viewport.ViewSize.Width + 2 * CullMarginPx,
      viewport.ViewSize.Height + 2 * CullMarginPx);

    foreach (var (_, bucket) in cells.OrderBy(c => c.Key.Y).ThenBy(c => c.Key.X))
    {
      // Sorting inside the cell keeps the pair offset stable frame to frame; an
      // unstable order makes two dots swap sides every repaint.
      bucket.Sort((a, b) => string.CompareOrdinal(a.TagId, b.TagId));

      if (bucket.Count >= ClusterThreshold)
      {
        var centre = Average(bucket, viewport);
        if (!bounds.Contains(centre)) continue;

        var leader = bucket
          .Where(r => r.Rank > 0)
          .OrderBy(r => r.Rank)
          .Select(r => r.RiderNumber)
          .FirstOrDefault();

        clusters.Add(new PlacedCluster(
          centre,
          Math.Min(20f, 9f + 3f * (float)Math.Log2(bucket.Count)),
          bucket.Count,
          bucket.Select(r => r.TagId).ToList(),
          leader));
        continue;
      }

      for (var i = 0; i < bucket.Count; i++)
      {
        var rider = bucket[i];
        var centre = viewport.ToScreen(rider.Position);

        // Two riders in one cell are side by side in the bunch, so offset them
        // across the track rather than along it: both dots stay on the road, and
        // it reads as what it is.
        if (bucket.Count == 2)
        {
          var (dx, dy) = Perpendicular(rider.HeadingDegrees);
          var sign = i == 0 ? 1f : -1f;
          centre = new PointF(centre.X + dx * PairOffsetPx * sign, centre.Y + dy * PairOffsetPx * sign);
        }

        if (!bounds.Contains(centre)) continue;
        dots.Add(Dot(rider, centre, selectedTagId, hoveredTagId));
      }
    }

    // Least overdue first, so it sits ON the line - the one about to cross - and
    // the most overdue ends up furthest back down the queue.
    overdue.Sort((a, b) => a.Fraction.CompareTo(b.Fraction));

    for (var i = 0; i < overdue.Count; i++)
    {
      var rider = overdue[i];
      var centre = viewport.ToScreen(rider.Position);
      var (fx, fy) = Forward(rider.HeadingDegrees);

      // Backwards along the direction of travel: they have not reached the line,
      // so behind it is the only side they can honestly be shown on.
      centre = new PointF(centre.X - fx * OverdueSpacingPx * i, centre.Y - fy * OverdueSpacingPx * i);

      if (!bounds.Contains(centre)) continue;
      dots.Add(Dot(rider, centre, selectedTagId, hoveredTagId));
    }

    // Drawn where they actually are, with no offset: these are the dots whose
    // position the operator is reading off the map.
    foreach (var rider in emphasised)
    {
      var centre = viewport.ToScreen(rider.Position);
      if (!bounds.Contains(centre)) continue;

      dots.Add(Dot(rider, centre, selectedTagId, hoveredTagId));
    }

    ApplyLabels(dots, riders, viewport, selectedTagId, hoveredTagId, labelParts, labelTopN);

    return new DotLayout(dots, clusters);
  }

  private static PlacedDot Dot(
    MapRiderMarker rider, PointF centre, string? selectedTagId, string? hoveredTagId)
  {
    var emphasised = rider.Highlighted || rider.TagId == selectedTagId || rider.TagId == hoveredTagId;

    return new PlacedDot(
      rider.TagId,
      centre,
      emphasised ? HighlightRadius : DotRadius,
      rider.State,
      rider.Fraction,
      Label: null,
      Badge: rider.Badge,
      LabelAnchor: centre,
      NeedsLeaderLine: false,
      Highlighted: emphasised);
  }

  /// <summary>
  /// Decides which dots get text. Numbers only, never names: a name at map scale
  /// is noise, and the leaderboard is right there on the other tab.
  /// </summary>
  private static void ApplyLabels(
    List<PlacedDot> dots,
    IReadOnlyList<MapRiderMarker> riders,
    MapViewport viewport,
    string? selectedTagId,
    string? hoveredTagId,
    MapLabelParts labelParts,
    int labelTopN)
  {
    if (labelParts == MapLabelParts.None) return;

    var byTag = new Dictionary<string, MapRiderMarker>(riders.Count);
    foreach (var rider in riders) byTag[rider.TagId] = rider;

    var budget = MaxLabels;

    // Emphasised dots first, so a hard cap never spends itself on the pack and
    // leaves the rider the operator actually asked about unlabelled.
    var order = dots
      .Select((dot, index) => (dot, index))
      .OrderByDescending(x => x.dot.Highlighted)
      .ThenBy(x => byTag.TryGetValue(x.dot.TagId, out var r) ? r.Rank : int.MaxValue);

    foreach (var (dot, index) in order)
    {
      if (budget <= 0) break;
      if (!byTag.TryGetValue(dot.TagId, out var rider)) continue;

      var wanted = dot.Highlighted
                   || rider.TagId == selectedTagId
                   || rider.TagId == hoveredTagId
                   || (rider.Rank > 0 && rider.Rank <= labelTopN)
                   || viewport.Zoom >= 17;

      if (!wanted) continue;

      var text = Compose(rider, labelParts);
      if (text.Length == 0) continue;

      var anchor = new PointF(dot.Centre.X, dot.Centre.Y - dot.Radius - 8f);

      dots[index] = dot with { Label = text, LabelAnchor = anchor, NeedsLeaderLine = false };
      budget--;
    }
  }

  /// <summary>
  /// Builds the label text. Position is written "P3" rather than as a bare
  /// number, which beside a start number would be indistinguishable from one.
  /// </summary>
  public static string Compose(MapRiderMarker rider, MapLabelParts parts)
  {
    var pieces = new List<string>(3);

    if (parts.HasFlag(MapLabelParts.Position) && rider.Rank > 0) pieces.Add($"P{rider.Rank}");
    if (parts.HasFlag(MapLabelParts.Number) && rider.RiderNumber.Length > 0) pieces.Add(rider.RiderNumber);
    if (parts.HasFlag(MapLabelParts.Name) && rider.ShortName.Length > 0) pieces.Add(rider.ShortName);

    return string.Join(" ", pieces);
  }

  private static PointF Average(List<MapRiderMarker> bucket, MapViewport viewport)
  {
    float x = 0, y = 0;

    foreach (var rider in bucket)
    {
      var p = viewport.ToScreen(rider.Position);
      x += p.X;
      y += p.Y;
    }

    return new PointF(x / bucket.Count, y / bucket.Count);
  }

  /// <summary>Unit vector along the direction of travel, in screen space (y points down).</summary>
  public static (float X, float Y) Forward(double headingDegrees)
  {
    var radians = headingDegrees * Math.PI / 180.0;
    return ((float)Math.Sin(radians), (float)-Math.Cos(radians));
  }

  /// <summary>Unit vector across the track, to the rider's right.</summary>
  public static (float X, float Y) Perpendicular(double headingDegrees)
  {
    var (x, y) = Forward(headingDegrees);
    return (-y, x);
  }
}
