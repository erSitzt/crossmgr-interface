namespace CrossMgrInterface;

/// <summary>
/// How a leaderboard words a rider: gap, lap time, clock, status.
///
/// Shared by the operator's Race Day board and the spectator screen, so the two
/// screens beside each other can never tell the crowd and the timekeeper
/// different things about the same rider.
/// </summary>
public static class BoardText
{
  /// <summary>"-1 lap", "+4.2", "DNF". Takes riders in finishing order; <paramref name="index"/> is this one's place in it.</summary>
  public static string Gap(RiderInfo rider, RiderInfo? leader, int index, bool timedSession)
  {
    if (rider.IsDNS) return "DNS";
    if (rider.IsDNF) return timedSession ? "-" : "DNF";
    if (leader == null || index == 0) return "-";

    var lapsDown = leader.TotalLaps - rider.TotalLaps;
    if (lapsDown > 0) return lapsDown == 1 ? "-1 lap" : $"-{lapsDown} laps";

    var gap = rider.TotalTime - leader.TotalTime;
    return gap > TimeSpan.Zero ? $"+{gap.TotalSeconds:F1}" : "-";
  }

  /// <summary>"0:48.3", or "-" for no time.</summary>
  public static string LapTime(TimeSpan? lap) => lap?.ToString(@"m\:ss\.f") ?? "-";

  /// <summary>"14:38", or "1:02:05" past the hour.</summary>
  public static string Clock(TimeSpan value) =>
    value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"mm\:ss");

  /// <summary>
  /// "DNF" or "DNS" - except that in a timed session the flag's timeout only
  /// means a rider is off track, with every time they set still counting. Free
  /// practice used to end with half the board reading DNF.
  /// </summary>
  public static string Status(RiderInfo rider, bool timedSession) =>
    timedSession && rider.IsDNF && !rider.IsDNS ? "off track" : rider.StatusText;
}
