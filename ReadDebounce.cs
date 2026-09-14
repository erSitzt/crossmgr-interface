namespace CrossMgrInterface;

/// <summary>Why a read was not counted as a lap, or null reason when it was.</summary>
public readonly record struct DebounceVerdict(bool Reject, TimeSpan Gap, string Reason)
{
  public static DebounceVerdict Accept(TimeSpan gap) => new(false, gap, "");
}

/// <summary>
/// Whether a read comes too soon to be a lap.
///
/// For a solo rider this is the one rule the application always had: a read
/// closer than the minimum lap time to the rider's last counted crossing is the
/// same pass seen twice.
///
/// A team needs a second rule. Its entry's last crossing may belong to another
/// member, so a member waiting to take over near the loop is read over and over
/// with nothing to measure those reads against - each one lands a little more
/// than a minimum lap after the last counted crossing and would be counted,
/// giving the team a lap every few seconds and possibly swallowing the real
/// crossing of the rider coming in. So inside a team a read is also measured
/// against the previous read of the same transponder, counted or not.
/// </summary>
public static class ReadDebounce
{
  public static DebounceVerdict Check(DateTime crossingTime, DateTime entryLastCrossing,
    DateTime? transponderLastRead, TimeSpan minimumLapTime, bool isTeam)
  {
    var gap = crossingTime - entryLastCrossing;
    if (gap < minimumLapTime)
      return new DebounceVerdict(true, gap, $"Only {gap.TotalSeconds:F1}s after the previous read");

    if (isTeam && transponderLastRead.HasValue)
    {
      var ownGap = crossingTime - transponderLastRead.Value;
      if (ownGap >= TimeSpan.Zero && ownGap < minimumLapTime)
        return new DebounceVerdict(true, ownGap,
          $"Only {ownGap.TotalSeconds:F1}s after this transponder's previous read - a rider waiting near the loop?");
    }

    return DebounceVerdict.Accept(gap);
  }
}
