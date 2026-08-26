namespace CrossMgrInterface;

/// <summary>How a rider appears on the gate pick sheet.</summary>
public enum QualifyingStatus
{
  /// <summary>Set at least one timed lap, so they have a place on merit.</summary>
  Timed,

  /// <summary>Went out, but never completed a timed lap - only an out-lap.</summary>
  NoTime,

  /// <summary>Never crossed the timing loop at all.</summary>
  DidNotGoOut
}

/// <summary>One line of the gate pick sheet.</summary>
public sealed record QualifyingEntry
{
  /// <summary>
  /// 1-based and continuous across the whole sheet. Riders without a time still
  /// get one: they pick a gate too, they just pick last.
  /// </summary>
  public int GatePick { get; init; }

  public required RiderInfo Rider { get; init; }

  public TimeSpan? BestLapTime { get; init; }

  /// <summary>When the best lap was completed. The tie-break key.</summary>
  public DateTime? BestLapSetAt { get; init; }

  /// <summary>Which lap of their session it was. Zero when there is no time.</summary>
  public int BestLapNumber { get; init; }

  /// <summary>Laps that carry a time, so excluding the out-lap.</summary>
  public int TimedLaps { get; init; }

  public int TotalLaps { get; init; }

  /// <summary>Behind the fastest rider on the sheet. Null for pole and for no time.</summary>
  public TimeSpan? GapToPole { get; init; }

  /// <summary>Behind the rider immediately ahead. Null for pole and for no time.</summary>
  public TimeSpan? IntervalToAhead { get; init; }

  public QualifyingStatus Status { get; init; }
}

/// <summary>
/// Turns a field into the order riders pick their starting gate in.
///
/// Ranked on best lap - any lap of the session - which is a different question
/// from the one <see cref="PositionCalculator.GetSortedRidersFromSnapshot"/>
/// answers. That sorts on laps completed then elapsed time, which is right for
/// a race and wrong here: it would put a rider who circulated slowly for the
/// whole session ahead of the fastest rider on the track.
///
/// Pure and clock-free, like <see cref="RaceProgress"/>, so the tab, the Race
/// Day board and the printed sheet can all be driven from it and cannot
/// disagree with one another.
/// </summary>
public static class QualifyingRanking
{
  public static List<QualifyingEntry> Rank(IEnumerable<RiderInfo> field)
  {
    var riders = field as IReadOnlyList<RiderInfo> ?? field.ToList();

    var timed = new List<(RiderInfo Rider, RiderLap Best)>();
    var noTime = new List<RiderInfo>();
    var didNotGoOut = new List<RiderInfo>();

    foreach (var rider in riders)
    {
      var best = rider.BestLap;

      // A rider the operator has marked as not started is placed with those who
      // never went out whatever their laps say - that ruling is a statement of
      // fact about the session, and it outranks what the loop recorded.
      if (best is not null && !rider.IsDNS)
        timed.Add((rider, best));
      else if (rider.TotalLaps > 0 && !rider.IsDNS)
        noTime.Add(rider);
      else
        didNotGoOut.Add(rider);
    }

    // A rider who crashed out of qualifying keeps the time they set and keeps
    // their gate pick, so IsDNF is deliberately not consulted here. Demoting on
    // DNF is right for a race classification and wrong for a timing sheet - and
    // after the chequered flag an automatic DNF only means "was not still out
    // at the end", which is true of everyone who had already pulled in.
    var ordered = timed
      .OrderBy(t => t.Best.LapTime!.Value)
      .ThenBy(t => t.Best.CrossingTime)
      .ToList();

    var results = new List<QualifyingEntry>(riders.Count);
    var pole = ordered.Count > 0 ? ordered[0].Best.LapTime!.Value : TimeSpan.Zero;

    for (var i = 0; i < ordered.Count; i++)
    {
      var (rider, best) = ordered[i];
      var lapTime = best.LapTime!.Value;

      results.Add(new QualifyingEntry
      {
        GatePick = i + 1,
        Rider = rider,
        BestLapTime = lapTime,
        BestLapSetAt = best.CrossingTime,
        BestLapNumber = best.LapNumber,
        TimedLaps = CountTimedLaps(rider),
        TotalLaps = rider.TotalLaps,
        GapToPole = i == 0 ? null : lapTime - pole,
        IntervalToAhead = i == 0 ? null : lapTime - ordered[i - 1].Best.LapTime!.Value,
        Status = QualifyingStatus.Timed
      });
    }

    // Riders who went out and set nothing rank above riders who never appeared:
    // being on track and failing to record a time is still more than not
    // starting. Within each group, start number, so the sheet is predictable.
    AppendWithoutTime(results, noTime, QualifyingStatus.NoTime);
    AppendWithoutTime(results, didNotGoOut, QualifyingStatus.DidNotGoOut);

    return results;
  }

  private static void AppendWithoutTime(
    List<QualifyingEntry> results, List<RiderInfo> riders, QualifyingStatus status)
  {
    foreach (var rider in riders.OrderBy(r => r, StartNumberOrder.Instance))
    {
      results.Add(new QualifyingEntry
      {
        GatePick = results.Count + 1,
        Rider = rider,
        TimedLaps = CountTimedLaps(rider),
        TotalLaps = rider.TotalLaps,
        Status = status
      });
    }
  }

  /// <summary>Laps carrying a time, excluding the out-lap - what "laps" means here.</summary>
  private static int CountTimedLaps(RiderInfo rider)
  {
    var count = 0;
    for (var i = 1; i < rider.Laps.Count; i++)
      if (rider.Laps[i].LapTime.HasValue) count++;
    return count;
  }

  /// <summary>
  /// Start numbers are free text, so #7 would otherwise sort after #12. Numeric
  /// where both sides are numeric, textual otherwise, and numbers before text.
  /// </summary>
  private sealed class StartNumberOrder : IComparer<RiderInfo>
  {
    public static readonly StartNumberOrder Instance = new();

    public int Compare(RiderInfo? x, RiderInfo? y)
    {
      if (ReferenceEquals(x, y)) return 0;
      if (x is null) return -1;
      if (y is null) return 1;

      var xIsNumber = int.TryParse(x.RiderNumber, out var xn);
      var yIsNumber = int.TryParse(y.RiderNumber, out var yn);

      if (xIsNumber && yIsNumber) return xn.CompareTo(yn);
      if (xIsNumber) return -1;
      if (yIsNumber) return 1;

      var byNumber = string.Compare(x.RiderNumber, y.RiderNumber, StringComparison.OrdinalIgnoreCase);
      // Riders with no number at all still need a stable order, or the sheet
      // reshuffles between the screen and the printout.
      return byNumber != 0
        ? byNumber
        : string.Compare(x.TagID, y.TagID, StringComparison.OrdinalIgnoreCase);
    }
  }
}
