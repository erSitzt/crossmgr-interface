namespace CrossMgrInterface;

/// <summary>How urgently the plan strip wants to be looked at.</summary>
public enum PlanStripTone { Calm, Soon, Overdue, Attention }

/// <summary>One session on the plan strip.</summary>
public sealed record PlanChip(string Text, PlanEntryStatus Status, bool Next);

/// <summary>Everything the Race Day plan strip shows, worked out away from the window.</summary>
public sealed record PlanStripModel(
  string Title,
  string Timing,
  IReadOnlyList<PlanChip> Chips,
  string NextLine,
  PlanStripTone Tone,
  bool CanSetUpNow,
  bool ShowNotYet,
  bool CanSkip,
  string? SetUpButtonText);

/// <summary>
/// The words on the plan strip. Pure, so what a volunteer reads in each
/// situation is fixed by a test rather than discovered on race day.
///
/// The strip always says what happens next and, when the plan is waiting,
/// why - a set-up that silently does not happen looks exactly like one that
/// is broken.
/// </summary>
public static class PlanStripText
{
  public static PlanStripModel Build(DayPlan plan, IReadOnlyList<ForecastEntry> forecast, PlanDecision decision,
    PlanReminder reminder, TimeSpan? countdownLeft, DateTime now)
  {
    var next = decision.Next;
    var late = next?.Late ?? TimeSpan.Zero;
    var timing = next == null ? "" : late >= TimeSpan.FromMinutes(1) ? $"running {Minutes(late)} late" : "on time";

    var chips = Chips(forecast, next);

    var interrupted = forecast.FirstOrDefault(e => e.Status == PlanEntryStatus.Interrupted);
    var nextLine = countdownLeft is { } left && next != null
      ? $"Setting up {next.Session.Name} in {Math.Max(1, (int)Math.Ceiling(left.TotalSeconds))} s"
      : NextLine(decision, now);

    if (interrupted != null && decision.Waiting != PlanWait.SessionRunning)
      nextLine = $"{interrupted.Session.Name} was interrupted. Open it from Race > Past sessions... to finish it, " +
                 "or Skip it. " + nextLine;

    var tone = decision.Waiting == PlanWait.NotFromPlan || interrupted != null ? PlanStripTone.Attention
      : reminder == PlanReminder.Overdue ? PlanStripTone.Overdue
      : reminder is PlanReminder.Soon or PlanReminder.Due || countdownLeft != null ? PlanStripTone.Soon
      : PlanStripTone.Calm;

    var canSetUp = next != null && next.Status == PlanEntryStatus.Planned &&
                   decision.Waiting != PlanWait.SessionRunning;

    return new PlanStripModel(
      $"TODAY'S PLAN · {plan.Name}",
      timing,
      chips,
      nextLine,
      tone,
      canSetUp,
      countdownLeft != null,
      next != null || interrupted != null,
      canSetUp ? $"SET UP {next!.Session.Name.ToUpperInvariant()}" : null);
  }

  /// <summary>Sessions either side of now, at most this many: the whole day is in the plan window.</summary>
  public const int ChipWindow = 4;

  /// <summary>
  /// The last session done, what is on, and what comes next - not the whole
  /// day. A club day of twelve sessions over three lines pushed the board off
  /// the screen, and nobody running the day needs this morning's practice there.
  /// </summary>
  private static List<PlanChip> Chips(IReadOnlyList<ForecastEntry> forecast, ForecastEntry? next)
  {
    var focus = forecast.ToList().FindIndex(e => !e.IsDone);
    if (focus < 0) focus = forecast.Count - 1;

    var start = Math.Max(0, focus - 1);
    var end = Math.Min(forecast.Count, start + ChipWindow);
    var chips = new List<PlanChip>();

    if (start > 0) chips.Add(new PlanChip($"✓ {start} earlier", PlanEntryStatus.Finished, false));
    for (var i = start; i < end; i++)
      chips.Add(new PlanChip(ChipText(forecast[i]), forecast[i].Status, ReferenceEquals(forecast[i], next)));
    if (end < forecast.Count) chips.Add(new PlanChip($"+{forecast.Count - end} more", PlanEntryStatus.Planned, false));

    return chips;
  }

  private static string NextLine(PlanDecision decision, DateTime now)
  {
    var next = decision.Next;
    if (next == null) return "Every session of the plan has run.";

    var name = next.Session.Name;
    var when = $"~{next.ExpectedStart:HH:mm}{In(next.ExpectedStart, now)}";

    return decision.Waiting switch
    {
      PlanWait.AlreadySetUp => $"{name} is set up - due {when}. " + StartHint(next.Session),
      PlanWait.SessionRunning => $"Next: {name} {when}",
      PlanWait.NotFromPlan => $"Paused: the session on screen is not from the plan. Next is {name} {when} - " +
                              "Set up now goes back to the plan.",
      PlanWait.NotYetDue when decision.SetUpAt is { } at => $"Next: {name} {when} - set up by itself at {at:HH:mm}",
      PlanWait.WindowOpen => $"Next: {name} {when} - set up as soon as the open window is closed",
      PlanWait.Correcting => $"Next: {name} {when} - waiting while laps are being fixed",
      PlanWait.Snoozed => $"Next: {name} {when} - put off for a few minutes (Not yet)",
      _ => $"Next: {name} {when}"
    };
  }

  private static string StartHint(PlannedSession session) => session.Start switch
  {
    PlannedStartMode.FirstRider => "The clock starts on the first rider.",
    PlannedStartMode.Waves => "Press START RACE when the first class goes.",
    _ => session.IsTimed ? "Press START SESSION when it opens." : "Press START RACE when the gate drops."
  };

  private static string ChipText(ForecastEntry entry) => entry.Status switch
  {
    PlanEntryStatus.Finished => $"✓ {entry.ExpectedStart:HH:mm} {entry.Session.Name}",
    PlanEntryStatus.Running => $"▶ {entry.ExpectedStart:HH:mm} {entry.Session.Name}",
    PlanEntryStatus.Interrupted => $"⚠ {entry.Session.Name} - interrupted",
    PlanEntryStatus.Skipped => $"{entry.Session.Name} - skipped",
    _ => entry.Late >= TimeSpan.FromMinutes(1)
      ? $"~{entry.ExpectedStart:HH:mm} {entry.Session.Name}"
      : $"{entry.ExpectedStart:HH:mm} {entry.Session.Name}"
  };

  /// <summary>" (in 23 min)", " (now)", or nothing more than an hour ahead.</summary>
  private static string In(DateTime at, DateTime now)
  {
    var wait = at - now;
    if (wait <= TimeSpan.FromSeconds(30)) return " (now)";
    if (wait > TimeSpan.FromHours(1)) return "";
    return $" (in {Minutes(wait)})";
  }

  private static string Minutes(TimeSpan span)
  {
    var minutes = Math.Max(1, (int)Math.Round(span.TotalMinutes));
    return minutes == 1 ? "1 min" : $"{minutes} min";
  }

  /// <summary>What the banner says when a reminder comes due. Once each, so it can be blunt.</summary>
  public static string? ReminderText(ForecastEntry next, PlanReminder reminder) => reminder switch
  {
    PlanReminder.Soon => $"{next.Session.Name} is due in 5 minutes ({next.DueAt:HH:mm}) - " +
                         $"{next.Session.ClassesText} to the gate.",
    PlanReminder.Due => $"{next.Session.Name} is due now.",
    PlanReminder.Overdue => $"{next.Session.Name} was due at {next.DueAt:HH:mm}. Start it, or change the plan's times.",
    _ => null
  };
}
