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
  /// treated as off the track.
  ///
  /// The configured DNF timeout defaults to two minutes, which is shorter than
  /// a motocross lap - so on the configured value alone a rider riding a
  /// perfectly good flag lap is written off while they are still on it. That
  /// matters more in a timed session than in a race, because the write-off also
  /// makes the app discard their next crossing: the lap that would have set
  /// their gate pick is thrown away.
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
