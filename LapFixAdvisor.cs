namespace CrossMgrInterface;

/// <summary>The kinds of lap problem that come with a fix worth offering as one button.</summary>
public enum LapFixKind
{
  /// <summary>A lap long enough to be two or more laps, with the reads between them missed.</summary>
  SplitMissedRead,

  /// <summary>
  /// A team lap that ended far too soon after a teammate's crossing: two riders
  /// were out at once, and the lap is not a real one.
  /// </summary>
  DeleteSecondRiderRead
}

/// <summary>
/// One problem with a rider's laps, in plain words, with the correction that is
/// right for it - and the other answer, that the lap was real after all.
/// </summary>
public sealed record LapFix(LapFixKind Kind, int LapNumber, int SplitCount, string Problem, string FixText, string KeepText)
{
  /// <param name="expectedRevision">The rider's revision when the operator was shown this fix.</param>
  public CorrectionResult Apply(RaceCorrectionService service, string tagId, int expectedRevision) => Kind switch
  {
    LapFixKind.SplitMissedRead => service.SplitLap(tagId, LapNumber, SplitCount, expectedRevision),
    _ => service.DeleteLap(tagId, LapNumber, expectedRevision)
  };

  public CorrectionResult Keep(RaceCorrectionService service, string tagId, int expectedRevision) => Kind switch
  {
    LapFixKind.SplitMissedRead => service.DismissSplitSuggestion(tagId, LapNumber, expectedRevision),
    _ => service.DismissOverlapWarning(tagId, LapNumber, expectedRevision)
  };
}

/// <summary>
/// Works out the fix for each warning on a rider's laps, so the operator is offered
/// the right correction as a single button rather than having to know which of the
/// Fix laps window's ten buttons answers which warning.
///
/// Only for the two warnings whose fix follows from the warning itself. A missed
/// read already comes with how many laps the long one is; two riders on track
/// already names the lap that cannot be real. A read rejected as too soon, or a
/// rider marked DNF, needs someone to know what happened on the track, so those
/// stay with the ordinary buttons.
/// </summary>
public static class LapFixAdvisor
{
  /// <summary>
  /// The fixes for one rider, most urgent first. Two riders on track comes before
  /// a missed read: it changes the team's lap count, and its short lap drags down
  /// the pace a missed read is judged against until it is sorted out.
  /// </summary>
  public static IReadOnlyList<LapFix> For(RiderInfo rider)
  {
    var fixes = new List<LapFix>();

    foreach (var lap in rider.Laps.Where(IsTwoOnTrack).OrderBy(l => l.LapNumber))
    {
      var previous = rider.Laps.FirstOrDefault(l => l.LapNumber == lap.LapNumber - 1);
      var who = rider.MemberFor(lap.CrossedBy)?.Label ?? "A rider";
      var before = rider.MemberFor(previous?.CrossedBy)?.Label ?? "the rider before";

      fixes.Add(new LapFix(
        LapFixKind.DeleteSecondRiderRead,
        lap.LapNumber,
        0,
        $"Lap {lap.LapNumber}: {who} was read only {lap.LapTime?.TotalSeconds ?? 0:F0}s after {before} crossed. " +
        "Two riders cannot both have been on track, so this lap is not a real one.",
        $"Delete lap {lap.LapNumber}",
        $"Keep lap {lap.LapNumber} - it was real"));
    }

    foreach (var lap in rider.Laps.Where(IsMissedRead).OrderBy(l => l.LapNumber))
    {
      var laps = lap.SuggestedSplitCount;
      var duration = lap.LapTime!.Value;
      var each = lap.SuggestedSplitLapTime ?? TimeSpan.FromMilliseconds(duration.TotalMilliseconds / laps);
      var missed = laps == 2 ? "a read" : $"{laps - 1} reads";

      fixes.Add(new LapFix(
        LapFixKind.SplitMissedRead,
        lap.LapNumber,
        laps,
        $"Lap {lap.LapNumber} took {duration.TotalSeconds:F1}s - that is {laps} laps of about " +
        $"{each.TotalSeconds:F0}s, with {missed} missed.",
        $"Split lap {lap.LapNumber} into {laps} laps",
        $"Keep lap {lap.LapNumber} as it is"));
    }

    return fixes;
  }

  /// <summary>
  /// The rider Fix laps (F2) should open. Anyone with two riders on track before
  /// anyone with a missed read, for the same reason <see cref="For"/> puts it
  /// first; and within each, whoever is highest in the running order, where a
  /// wrong lap count decides a result. Null when nothing needs fixing.
  /// </summary>
  /// <param name="runningOrder">The field in running order, leader first.</param>
  public static RiderInfo? MostUrgent(IEnumerable<RiderInfo> runningOrder)
  {
    var field = runningOrder.ToList();

    return field.FirstOrDefault(r => r.Laps.Any(IsTwoOnTrack))
           ?? field.FirstOrDefault(r => r.Laps.Any(IsMissedRead));
  }

  private static bool IsTwoOnTrack(RiderLap lap) => lap is { IsSuspectedOverlap: true, OverlapDismissed: false };

  private static bool IsMissedRead(RiderLap lap) =>
    lap is { IsSuggestedForSplit: true, SuggestionDismissed: false, SuggestedSplitCount: >= 2, LapTime: not null };
}
