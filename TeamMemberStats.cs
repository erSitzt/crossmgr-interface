namespace CrossMgrInterface;

/// <summary>
/// One line of a team's breakdown: a rider, or riders sharing a transponder, or
/// the laps nobody can be credited with.
/// </summary>
public sealed class TeamMemberLine
{
  /// <summary>The riders this line is about; null for the laps with no recorded rider.</summary>
  public TransponderGroup? Group { get; init; }

  public int LapsRidden { get; init; }

  /// <summary>Laps that count as this rider's own time: see <see cref="TeamMemberStats"/>.</summary>
  public int TimedLaps { get; init; }

  public TimeSpan? BestLap { get; init; }
  public TimeSpan? AverageLap { get; init; }

  public bool IsUnattributed => Group == null;

  /// <summary>Several riders on one transponder: their laps count, but cannot be timed per rider.</summary>
  public bool SharedTransponder => Group?.IsShared == true;

  public string Label => Group?.Label ?? "Laps with no recorded rider";

  public IReadOnlyList<string> Transponders => Group?.Transponders ?? Array.Empty<string>();
}

/// <summary>
/// Who rode a team's laps, and how fast each of them went.
///
/// A lap is credited to the rider whose transponder ended it. Only some laps are
/// that rider's own time, though:
/// - not the first lap, which runs from the start of the race;
/// - not a handover, which starts with the previous rider riding in and holds
///   the changeover - it would make whoever takes over look slow;
/// - not a lap suspected of being two riders out, or one flagged as a missed
///   read, whose times are not a lap;
/// - not a lap on a shared transponder, which cannot say who rode it.
/// </summary>
public static class TeamMemberStats
{
  public static IReadOnlyList<TeamMemberLine> For(RiderInfo team)
  {
    if (!team.IsTeam) return Array.Empty<TeamMemberLine>();

    var groups = TransponderGroup.Of(team.Members);
    var laps = team.Laps.Where(l => !l.IsDeleted).ToList();

    var ridden = new int[groups.Count];
    var timed = groups.Select(_ => new List<double>()).ToArray();
    var unattributed = 0;

    for (var i = 0; i < laps.Count; i++)
    {
      var lap = laps[i];
      var group = TransponderGroup.IndexOf(groups, lap.CrossedBy);
      if (group < 0)
      {
        unattributed++;
        continue;
      }

      ridden[group]++;

      if (i == 0 || !lap.LapTime.HasValue || groups[group].IsShared) continue;
      if (lap.IsSuspectedOverlap || lap.IsSuggestedForSplit) continue;
      if (TransponderGroup.IndexOf(groups, laps[i - 1].CrossedBy) != group) continue;

      timed[group].Add(lap.LapTime.Value.TotalMilliseconds);
    }

    var lines = new List<TeamMemberLine>(groups.Count + 1);
    for (var g = 0; g < groups.Count; g++)
    {
      lines.Add(new TeamMemberLine
      {
        Group = groups[g],
        LapsRidden = ridden[g],
        TimedLaps = timed[g].Count,
        BestLap = timed[g].Count > 0 ? TimeSpan.FromMilliseconds(timed[g].Min()) : null,
        AverageLap = timed[g].Count > 0 ? TimeSpan.FromMilliseconds(timed[g].Average()) : null
      });
    }

    if (unattributed > 0)
      lines.Add(new TeamMemberLine { LapsRidden = unattributed });

    return lines;
  }
}
