namespace CrossMgrInterface;

/// <summary>
/// How long the plan waits for things. A record so the race-day demo can run
/// the same rules on a shorter day.
/// </summary>
public sealed record PlanRunnerTimings
{
  /// <summary>
  /// How long before it is due a session with a gate start is set up. Reads
  /// before START RACE are not counted anyway, so it can be on screen early.
  /// </summary>
  public TimeSpan GateLead { get; init; } = TimeSpan.FromMinutes(15);

  /// <summary>
  /// The same for a session that starts on the first rider. Short on purpose:
  /// once it is set up, any of its riders rolling over the loop starts it.
  /// </summary>
  public TimeSpan FirstRiderLead { get; init; } = TimeSpan.FromMinutes(5);

  /// <summary>The least time the finished session stays on screen, for the last look at its laps.</summary>
  public TimeSpan AfterFinishGate { get; init; } = TimeSpan.FromMinutes(2);

  /// <summary>The same, before a first-rider session: the last riders are still rolling back over the loop.</summary>
  public TimeSpan AfterFinishFirstRider { get; init; } = TimeSpan.FromMinutes(3);

  /// <summary>Somebody fixed a lap this recently: they are probably not done.</summary>
  public TimeSpan CorrectionQuiet { get; init; } = TimeSpan.FromMinutes(2);

  /// <summary>How long the strip counts down before setting up, with Not yet to stop it.</summary>
  public TimeSpan Countdown { get; init; } = TimeSpan.FromSeconds(30);

  /// <summary>What Not yet puts it off by.</summary>
  public TimeSpan Snooze { get; init; } = TimeSpan.FromMinutes(5);

  /// <summary>The first reminder, this long before a session is due.</summary>
  public TimeSpan RemindBefore { get; init; } = TimeSpan.FromMinutes(5);

  /// <summary>Due this long and not started: one more, firmer reminder.</summary>
  public TimeSpan OverdueAfter { get; init; } = TimeSpan.FromMinutes(10);

  public static PlanRunnerTimings Default { get; } = new();
}

/// <summary>What is on screen, as far as the plan is concerned.</summary>
/// <param name="EntryId">The plan's session on screen - set up, running or finished - or null.</param>
/// <param name="FromPlan">False when something else is on screen: a session reopened from Past sessions, or one set up by hand.</param>
/// <param name="Empty">Nothing is set up at all.</param>
public sealed record PlanScreen(string? EntryId, bool FromPlan, bool Empty, bool Running, bool Finished, DateTime? FinishedAt)
{
  public static PlanScreen Nothing { get; } = new(null, true, true, false, false, null);
}

/// <summary>Why the next session is not being set up yet. Shown on the strip, so the operator is never left guessing.</summary>
public enum PlanWait
{
  /// <summary>Nothing in the way: set it up.</summary>
  None,
  NothingLeft,
  AlreadySetUp,
  SessionRunning,
  NotFromPlan,
  NotYetDue,
  WindowOpen,
  Correcting,
  Snoozed
}

public enum PlanReminder { None, Soon, Due, Overdue }

/// <summary>What the plan wants now: the session it is waiting for, when that is set up, and what holds it.</summary>
public sealed record PlanDecision(ForecastEntry? Next, DateTime? SetUpAt, PlanWait Waiting)
{
  public bool SetUpNow => Waiting == PlanWait.None;
}

/// <summary>
/// The plan's judgement, once a second: is it time to put the next session on
/// screen, and should anybody be reminded of it.
///
/// Pure, so every rule can be held to in a test. The one thing it never does
/// is set a session up behind the operator's back while they are working: an
/// open window, a lap fixed in the last two minutes, a session they reopened
/// from Past sessions, or Not yet, all hold it. Setting up clears the screen
/// and the undo history, and doing that under someone's Fix laps would lose
/// their work.
/// </summary>
public static class PlanRunnerPolicy
{
  public static PlanDecision Decide(IReadOnlyList<ForecastEntry> forecast, PlanScreen screen, DateTime now,
    bool windowOpen, DateTime? lastCorrectionAt, DateTime? snoozedUntil, PlanRunnerTimings? timings = null)
  {
    var t = timings ?? PlanRunnerTimings.Default;
    var next = PlanForecast.Next(forecast);

    if (next == null) return new PlanDecision(null, null, PlanWait.NothingLeft);
    if (next.Status == PlanEntryStatus.Ready) return new PlanDecision(next, null, PlanWait.AlreadySetUp);
    if (screen.Running) return new PlanDecision(next, null, PlanWait.SessionRunning);
    if (!screen.Empty && !screen.FromPlan) return new PlanDecision(next, null, PlanWait.NotFromPlan);

    var setUpAt = SetUpAt(next, screen, t);
    if (now < setUpAt) return new PlanDecision(next, setUpAt, PlanWait.NotYetDue);

    return new PlanDecision(next, setUpAt, Blocked(now, windowOpen, lastCorrectionAt, snoozedUntil, t));
  }

  /// <summary>
  /// "Set up now": the operator's word skips the timing, but not what is
  /// unsafe - a session still running, or a window open over the screen.
  /// </summary>
  public static PlanWait CanSetUpByHand(IReadOnlyList<ForecastEntry> forecast, PlanScreen screen, bool windowOpen)
  {
    var next = PlanForecast.Next(forecast);
    if (next == null) return PlanWait.NothingLeft;
    if (next.Status == PlanEntryStatus.Ready) return PlanWait.AlreadySetUp;
    if (screen.Running) return PlanWait.SessionRunning;
    return windowOpen ? PlanWait.WindowOpen : PlanWait.None;
  }

  public static DateTime SetUpAt(ForecastEntry next, PlanScreen screen, PlanRunnerTimings t)
  {
    var firstRider = next.Session.Start == PlannedStartMode.FirstRider;
    var at = next.ExpectedStart - (firstRider ? t.FirstRiderLead : t.GateLead);

    if (screen.Finished && screen.FinishedAt is { } finished)
    {
      var settled = finished + (firstRider ? t.AfterFinishFirstRider : t.AfterFinishGate);
      if (settled > at) at = settled;
    }

    return at;
  }

  private static PlanWait Blocked(DateTime now, bool windowOpen, DateTime? lastCorrectionAt, DateTime? snoozedUntil,
    PlanRunnerTimings t)
  {
    if (windowOpen) return PlanWait.WindowOpen;
    if (lastCorrectionAt is { } corrected && now - corrected < t.CorrectionQuiet) return PlanWait.Correcting;
    if (snoozedUntil is { } until && now < until) return PlanWait.Snoozed;
    return PlanWait.None;
  }

  /// <summary>
  /// Which reminder is due for the session the day is waiting for. Measured
  /// against when it is due, not when it is expected: "expected" never falls
  /// behind the clock, and a session never started would never be overdue.
  /// </summary>
  public static PlanReminder ReminderFor(ForecastEntry? next, DateTime now, PlanRunnerTimings? timings = null)
  {
    var t = timings ?? PlanRunnerTimings.Default;
    if (next is not { IsPending: true }) return PlanReminder.None;

    if (now >= next.DueAt + t.OverdueAfter) return PlanReminder.Overdue;
    if (now >= next.DueAt) return PlanReminder.Due;
    if (now >= next.DueAt - t.RemindBefore) return PlanReminder.Soon;
    return PlanReminder.None;
  }
}
