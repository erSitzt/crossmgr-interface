namespace CrossMgrInterface;

/// <summary>
/// The two rules a timed session ends by, pulled out of the form because both
/// have a corner that is easy to get wrong and impossible to see from the UI.
/// </summary>
public static class ChequeredFlag
{
  /// <summary>
  /// A flag lap is worth at least this much of a lap before a rider who has not
  /// come round is written off as no longer on track.
  /// </summary>
  private const double LapsOfGrace = 1.5;

  /// <summary>
  /// How long to wait after the flag before a rider who has not crossed is
  /// treated as off the track. Used for races as well as timed sessions.
  ///
  /// The configured DNF timeout defaults to two minutes, which is shorter than
  /// a motocross lap - so on the configured value alone a rider riding a
  /// perfectly good last lap is written off while they are still on it. In a
  /// timed session the write-off also discards their next crossing, the lap
  /// that would have set their gate pick; in an enduro with twenty-minute laps
  /// it would have scored the whole field DNF.
  ///
  /// Only ever extends the operator's setting, never shortens it.
  /// </summary>
  /// <param name="configured">The operator's DNF timeout.</param>
  /// <param name="medianPace">Typical lap time for the field, or null if unknown.</param>
  public static TimeSpan Grace(TimeSpan configured, TimeSpan? medianPace)
  {
    if (!medianPace.HasValue) return configured;

    var lapBased = medianPace.Value * LapsOfGrace;
    return lapBased > configured ? lapBased : configured;
  }

  /// <summary>
  /// How long a race waits for a leader who has not come round before it gives
  /// up and flags the field from the clock instead.
  ///
  /// The wait has to cover the laps the leader still legitimately owes - the
  /// one in progress plus the extra laps - or a race run with extra laps would
  /// flag its own leader off part way round. <see cref="Grace"/> already allows
  /// a lap and a half, so only the extra laps are added on top.
  /// </summary>
  /// <param name="configured">The operator's DNF timeout.</param>
  /// <param name="medianPace">The field's typical lap time, or null if unknown.</param>
  /// <param name="additionalLaps">The race's extra-laps setting.</param>
  public static TimeSpan LeaderWait(TimeSpan configured, TimeSpan? medianPace, int additionalLaps)
  {
    var grace = Grace(configured, medianPace);
    if (!medianPace.HasValue || additionalLaps <= 0) return grace;
    return grace + additionalLaps * medianPace.Value;
  }

  /// <summary>
  /// The lap the leader must reach before the flag comes out: the lap they were
  /// on when the clock ran out, plus whatever extra laps the race is run under.
  ///
  /// Zero extra laps still means one more lap - the one in progress. It does
  /// not mean the clock itself ends the race: a race always waits for the
  /// leader to come round, and everyone else is flagged from that moment. A
  /// race that flagged the whole field on the clock instead ended the day for
  /// every rider who happened to be a few seconds past the loop, while the
  /// leader - a few seconds short of it - rode a whole further lap.
  /// </summary>
  /// <param name="leaderLapsAtExpiry">Laps the leader had completed when the clock ran out.</param>
  /// <param name="additionalLaps">The race's extra-laps setting. Zero is normal.</param>
  public static int TargetLaps(int leaderLapsAtExpiry, int additionalLaps) =>
    leaderLapsAtExpiry + 1 + additionalLaps;

  /// <summary>
  /// The last lap a rider may complete once the flag is out, from the laps they
  /// had completed when it came out: the lap they are on - or none at all for a
  /// rider already at the race's laps target when the leader finished.
  ///
  /// Worked out again from the flag moment after every correction, never from
  /// the laps a rider has now. A lap split in two before the flag moves the
  /// allowance with it; a lap added after the flag is the one they were on, not
  /// a licence to start another.
  /// </summary>
  /// <param name="lapsCompletedAtFlag">RiderInfo.LapsCompletedBy at the flag moment.</param>
  /// <param name="targetLaps">The race's laps target when the leader's finish ended it, or 0 when the clock did.</param>
  public static int AllowedLap(int lapsCompletedAtFlag, int targetLaps) =>
    targetLaps > 0 && lapsCompletedAtFlag >= targetLaps ? lapsCompletedAtFlag : lapsCompletedAtFlag + 1;

  /// <summary>
  /// Whether a rider was still out on a lap when the flag fell, as opposed to
  /// having pulled in earlier and finished their session normally.
  ///
  /// Both end up marked off-track by the grace, because neither crosses the
  /// loop again - but only the first is worth telling the operator about. In a
  /// practice session most of the field comes in before the clock runs out, so
  /// announcing every one of them buries the one rider who is genuinely still
  /// out under a page of warnings that mean "finished normally".
  ///
  /// A rider still circulating crossed within about the last lap. Anyone whose
  /// last crossing is older than that was already overdue when the flag fell,
  /// which in practice means they had pulled in.
  /// </summary>
  /// <param name="sinceLastCrossing">Flag time minus the rider's last crossing.</param>
  /// <param name="pace">The rider's lap time, or the field's, or null if unknown.</param>
  public static bool WasCirculatingAtFlag(TimeSpan sinceLastCrossing, TimeSpan? pace)
  {
    // With no pace to judge by, assume they were out. Over-reporting is a line
    // in the feed; under-reporting hides a rider who never came back.
    if (!pace.HasValue) return true;

    return sinceLastCrossing <= pace.Value * LapsOfGrace;
  }
}
