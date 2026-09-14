namespace CrossMgrInterface;

/// <summary>
/// How hard a team event looks for trouble. Shipped values, no settings screen:
/// see <see cref="TwoOnTrackDetector"/> for why these numbers.
/// </summary>
public sealed record TeamEventSettings
{
  /// <summary>A handover lap shorter than this share of the team's pace is flagged.</summary>
  public double OverlapRatio { get; init; } = 0.6;

  /// <summary>How many recent laps make up the team's pace.</summary>
  public int PaceWindow { get; init; } = 5;

  /// <summary>Laps the team needs before its own pace is trusted over the field's.</summary>
  public int MinPriorLaps { get; init; } = 2;

  /// <summary>Rejected reads of one member within <see cref="WaitingWindow"/> that mean they are waiting near the loop.</summary>
  public int WaitingReads { get; init; } = 3;

  public TimeSpan WaitingWindow { get; init; } = TimeSpan.FromSeconds(60);

  /// <summary>How long before the same waiting member is mentioned again.</summary>
  public TimeSpan WaitingRewarn { get; init; } = TimeSpan.FromMinutes(10);

  public static TeamEventSettings Default { get; } = new();
}

/// <summary>
/// Flags a team lap that looks like two of the team's riders were out at once.
///
/// Only one member may be on track. A real handover lap - one member's read,
/// then a different member's - is at least a normal lap long, because it holds
/// the changeover as well: the rider coming in rides to the changeover zone, and
/// the one going out rides from it. When a member goes out while another is
/// still riding, the next read from the other one lands after only part of a
/// lap. So a handover lap well under the team's pace is suspicious, and a
/// violation that lasts several laps produces several of them.
///
/// The threshold is 0.6 of the pace. A fast member in a mixed-speed team rides
/// around 0.85 of a pace set by slower teammates, and still carries the
/// changeover, so a real handover does not come near it.
///
/// A warning only, like the missed-read detector it is modelled on: nothing is
/// deleted. Which of the two laps is not real is for the operator to decide.
/// </summary>
public static class TwoOnTrackDetector
{
  /// <summary>
  /// Re-derives every two-on-track warning for one team. Runs over the whole lap
  /// list, so it is right after a correction as well as after a crossing. Laps
  /// the operator has kept are left alone.
  /// </summary>
  /// <param name="fieldPace">The field's pace, used until the team has one of its own.</param>
  public static void Analyze(RiderInfo team, TimeSpan? fieldPace, TeamEventSettings? settings = null)
  {
    if (!team.IsTeam) return;
    var tuning = settings ?? TeamEventSettings.Default;

    foreach (var lap in team.Laps)
      if (!lap.OverlapDismissed) lap.IsSuspectedOverlap = false;

    var groups = TransponderGroup.Of(team.Members);

    for (var i = 1; i < team.Laps.Count; i++)
    {
      var lap = team.Laps[i];
      if (lap.OverlapDismissed || !lap.LapTime.HasValue) continue;
      if (!IsHandover(groups, team.Laps[i - 1], lap)) continue;

      // In lap order, so a lap flagged here is already out of the pace the next
      // one is measured against.
      var pace = TeamPaceBefore(team, groups, i, tuning) ?? fieldPace;
      if (pace is not { } reference || reference <= TimeSpan.Zero) continue;

      if (lap.LapTime.Value.TotalMilliseconds < reference.TotalMilliseconds * tuning.OverlapRatio)
        lap.IsSuspectedOverlap = true;
    }
  }

  /// <summary>
  /// True when two consecutive crossings were made by riders who can be told
  /// apart: both transponders known, and not shared with each other. A team on
  /// one shared transponder never hands over as far as this can tell.
  /// </summary>
  public static bool IsHandover(IReadOnlyList<TransponderGroup> groups, RiderLap previous, RiderLap lap)
  {
    var from = TransponderGroup.IndexOf(groups, previous.CrossedBy);
    var to = TransponderGroup.IndexOf(groups, lap.CrossedBy);
    return from >= 0 && to >= 0 && from != to;
  }

  /// <summary>
  /// The team's pace over its recent ordinary laps before <paramref name="index"/>:
  /// not the first lap, which runs from the start; not a handover, which carries
  /// the changeover; not one already flagged either way.
  /// </summary>
  private static TimeSpan? TeamPaceBefore(RiderInfo team, IReadOnlyList<TransponderGroup> groups, int index,
    TeamEventSettings tuning)
  {
    var window = new List<double>();

    for (var i = index - 1; i >= 1 && window.Count < tuning.PaceWindow; i--)
    {
      var lap = team.Laps[i];
      if (!lap.LapTime.HasValue || lap.IsSuspectedOverlap || lap.IsSuggestedForSplit) continue;
      if (IsHandover(groups, team.Laps[i - 1], lap)) continue;
      window.Add(lap.LapTime.Value.TotalMilliseconds);
    }

    return window.Count < tuning.MinPriorLaps ? null : TimeSpan.FromMilliseconds(window.Average());
  }
}
