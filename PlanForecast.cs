namespace CrossMgrInterface;

/// <summary>Where a planned session stands. Worked out from the race database, never stored. See DayPlan.</summary>
public enum PlanEntryStatus
{
  /// <summary>Not run yet, and not set up.</summary>
  Planned,

  /// <summary>Set up on screen, waiting for its clock to start.</summary>
  Ready,

  /// <summary>Its clock is running.</summary>
  Running,

  /// <summary>Over: its results are final.</summary>
  Finished,

  /// <summary>
  /// Started, never finished, and not on screen - the laptop restarted and
  /// the session was not brought back. Someone has to say what to do with it.
  /// </summary>
  Interrupted,

  Skipped
}

/// <summary>What the race database says about one planned session: the race that ran it, if one has.</summary>
public sealed record PlanRun(int RaceId, DateTime Start, DateTime? End, bool Finished);

/// <summary>One planned session with its times worked out.</summary>
public sealed record ForecastEntry(
  PlannedSession Session,
  PlanEntryStatus Status,
  DateTime Planned,
  DateTime DueAt,
  DateTime ExpectedStart,
  DateTime ExpectedEnd,
  int? RaceId)
{
  /// <summary>How far behind its plan it is due. Zero when on time; started sessions say how late they started.</summary>
  public TimeSpan Late => (Status is PlanEntryStatus.Planned or PlanEntryStatus.Ready ? DueAt : ExpectedStart) - Planned;

  public bool IsDone => Status is PlanEntryStatus.Finished or PlanEntryStatus.Skipped;
  public bool IsPending => Status is PlanEntryStatus.Planned or PlanEntryStatus.Ready;
}

/// <summary>
/// When each session of a plan will really start, as the day goes.
///
/// The rule a volunteer can say out loud: every session moves by as much as
/// the last one started late - so the planned breaks stay as they were - and
/// none starts until the one before has finished and the changeover has
/// passed. A session never moves earlier than planned: riders were told a
/// time. A time typed on the day (<see cref="PlannedSession.TypedTime"/>) does
/// not move with the others, and the sessions after it move as much as it did.
///
/// <see cref="ForecastEntry.DueAt"/> is when a session should start by those
/// rules; <see cref="ForecastEntry.ExpectedStart"/> is the same, but never in
/// the past - a session that is due and has not started will start now at
/// the earliest. The difference is what "overdue" means, and it pushes the
/// rest of the day back as it grows.
/// </summary>
public static class PlanForecast
{
  /// <param name="runs">The race that ran each entry, by entry id.</param>
  /// <param name="readyEntryId">The entry set up on screen and waiting, if any.</param>
  /// <param name="runningEntryId">The entry whose clock is running on screen, if any.</param>
  /// <param name="runningLap">The running session's own pace, when it has one.</param>
  public static IReadOnlyList<ForecastEntry> Forecast(DayPlan plan, IReadOnlyDictionary<string, PlanRun> runs,
    string? readyEntryId, string? runningEntryId, DateTime now, TimeSpan? runningLap = null)
  {
    var entries = new List<ForecastEntry>(plan.Sessions.Count);
    var changeover = TimeSpan.FromMinutes(Math.Max(0, plan.ChangeoverMinutes));
    var shift = TimeSpan.Zero;
    DateTime? previousEnd = null;
    var firstPending = true;

    foreach (var session in plan.Sessions)
    {
      var planned = plan.PlannedStart(session);
      runs.TryGetValue(session.Id, out var run);

      if (run is { Finished: true })
      {
        var end = run.End ?? run.Start + plan.PlannedLength(session);
        entries.Add(new ForecastEntry(session, PlanEntryStatus.Finished, planned, run.Start, run.Start, end, run.RaceId));
        shift = Max(TimeSpan.Zero, run.Start - planned);
        previousEnd = end;
        continue;
      }

      var running = run != null && session.Id == runningEntryId;

      // Skipped wins over an interrupted run: skipping one is how the operator
      // says what should become of it.
      if (session.Skipped && !running)
      {
        // Shown where it was planned; it takes no time out of the day.
        entries.Add(new ForecastEntry(session, PlanEntryStatus.Skipped, planned, planned, planned, planned, run?.RaceId));
        continue;
      }

      if (run != null)
      {
        var lap = running ? runningLap : null;
        var end = Max(run.Start + plan.PlannedLength(session, lap), now);
        entries.Add(new ForecastEntry(session, running ? PlanEntryStatus.Running : PlanEntryStatus.Interrupted,
          planned, run.Start, run.Start, end, run.RaceId));
        shift = Max(TimeSpan.Zero, run.Start - planned);
        previousEnd = end;
        continue;
      }

      var due = session.TypedTime is { } typed ? plan.Date.Date + typed : planned + shift;
      if (previousEnd is { } end0 && end0 + changeover > due) due = end0 + changeover;

      var expected = firstPending ? Max(due, now) : due;
      firstPending = false;

      // Later sessions keep their planned distance from this one, however late
      // it is - and a typed time moves them by as much as it moved.
      shift = Max(TimeSpan.Zero, expected - planned);

      var expectedEnd = expected + plan.PlannedLength(session);
      entries.Add(new ForecastEntry(session,
        session.Id == readyEntryId ? PlanEntryStatus.Ready : PlanEntryStatus.Planned,
        planned, due, expected, expectedEnd, null));
      previousEnd = expectedEnd;
    }

    return entries;
  }

  /// <summary>The session the day is waiting for next: set up on screen, or the first still to come.</summary>
  public static ForecastEntry? Next(IReadOnlyList<ForecastEntry> forecast) =>
    forecast.FirstOrDefault(e => e.Status == PlanEntryStatus.Ready) ??
    forecast.FirstOrDefault(e => e.Status == PlanEntryStatus.Planned);

  private static T Max<T>(T a, T b) where T : IComparable<T> => a.CompareTo(b) >= 0 ? a : b;
}
