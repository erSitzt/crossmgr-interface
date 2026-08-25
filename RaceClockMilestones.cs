namespace CrossMgrInterface;

/// <summary>
/// Decides when the countdown warnings are due.
///
/// Pulled out of the form as a pure function because the rule has a corner that
/// is easy to get wrong: a "5 minutes remaining" warning is meaningless on a race
/// that is only 4 minutes long, and firing it the instant the flag drops is worse
/// than not firing it at all - it teaches the operator to ignore warnings.
/// </summary>
public static class RaceClockMilestones
{
  /// <summary>
  /// True when a warning for <paramref name="milestone"/> is due now.
  /// </summary>
  /// <param name="remaining">Time left on the clock.</param>
  /// <param name="duration">Total race length.</param>
  /// <param name="milestone">The milestone being tested, e.g. five minutes.</param>
  /// <param name="alreadyShown">Whether this milestone has already been announced.</param>
  public static bool ShouldAnnounce(
    TimeSpan remaining, TimeSpan duration, TimeSpan milestone, bool alreadyShown)
  {
    if (alreadyShown) return false;

    // Nothing to count down to.
    if (remaining <= TimeSpan.Zero) return false;

    // A race no longer than the milestone would trigger it at the start line.
    if (duration <= milestone) return false;

    return remaining <= milestone;
  }
}
