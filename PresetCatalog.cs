namespace CrossMgrInterface;

/// <summary>A day plan to start from: one of the application's own, or one the club saved.</summary>
public sealed record PlanPreset(string Name, string Description, DayPlan Plan, bool BuiltIn)
{
  /// <summary>Its code: the plan itself, written out. See DayPlanCode.</summary>
  public string Code => DayPlanCode.Encode(Plan);
}

/// <summary>
/// The day plans the application comes with: the shapes most club days take.
///
/// Classes are not known in advance - one club has MX1 and MX2, the next
/// "Jugend 85" and "Senioren" - so the presets use stand-ins, Group A and
/// Group B, and the volunteer says which of the rider list's classes each one
/// is when a plan is made from the preset (see ClassMapping). Times and
/// lengths are a sensible start, there to be changed.
///
/// Their codes are held to the byte by the tests: a club may have written one
/// down, and it must still mean the same day.
/// </summary>
public static class PresetCatalog
{
  public const string GroupA = "Group A";
  public const string GroupB = "Group B";

  public static IReadOnlyList<PlanPreset> All { get; } = new[]
  {
    new PlanPreset("MX club day",
      "Two groups of classes. Practice and timed qualifying for each, then Moto 1 and Moto 2 for each, " +
      "added up into an overall per group.",
      MxClubDay(), BuiltIn: true),
    new PlanPreset("Training day",
      "Free practice for two groups of classes, taking turns every half hour. Times only, no ranking.",
      TrainingDay(), BuiltIn: true),
    new PlanPreset("Enduro",
      "Two 2-hour races with no extra laps: one group in the morning, all starting together; the other in " +
      "the afternoon, its classes leaving a minute apart.",
      Enduro(), BuiltIn: true),
    new PlanPreset("Team race",
      "A practice for everyone, then a one-hour team race: riders sharing a team name score as one.",
      TeamRace(), BuiltIn: true)
  };

  /// <summary>
  /// A new plan from a preset - or from nothing - for a day: new identities,
  /// the day's name and rider list, and every time moved so the first session
  /// is at <paramref name="firstSession"/>. Null when that would push the
  /// last one past midnight.
  /// </summary>
  public static DayPlan? NewPlan(DayPlan? source, DateTime day, string name, TimeSpan firstSession, string? riderList)
  {
    var plan = source == null ? new DayPlan { After = AfterSession.ForNewPlan() } : source.CopyFor(day.Date);
    plan.Date = day.Date;
    plan.Name = name.Trim().Length > 0 ? name.Trim() : source?.Name is { Length: > 0 } n ? n : "Race day";
    plan.RiderListPath = riderList;
    plan.After.Folder = null;

    if (plan.Sessions.Count > 0)
    {
      var shift = TimeSpan.FromMinutes(Math.Round((firstSession - plan.Sessions.Min(s => s.Time)).TotalMinutes));
      if (plan.Sessions.Max(s => s.Time) + shift >= TimeSpan.FromDays(1)) return null;
      foreach (var s in plan.Sessions) s.Time += shift;
    }

    return plan;
  }

  private static PlannedSession Session(int hour, int minute, string label, SessionType type, PlannedStartMode start,
    int minutes, int extraLaps, string? group, params string[] classes) => new()
  {
    Time = new TimeSpan(hour, minute, 0),
    Name = classes.Length > 0 ? $"{label} - {DayPlanCode.ClassesSuffix(classes)}" : label,
    SessionType = type,
    Start = start,
    DurationMinutes = minutes,
    AdditionalLaps = extraLaps,
    OverallGroup = group,
    Classes = classes.ToList()
  };

  private static DayPlan Plan(string name, int changeover, int lapSeconds, params PlannedSession[] sessions)
  {
    var plan = new DayPlan
    {
      Name = name,
      ChangeoverMinutes = changeover,
      TypicalLapSeconds = lapSeconds,
      After = AfterSession.ForNewPlan()
    };
    plan.Sessions.AddRange(sessions);
    return DayPlanCode.Canonical(plan);
  }

  private static DayPlan MxClubDay()
  {
    const SessionType practice = SessionType.FreePractice, qualifying = SessionType.TimedQualifying, race = SessionType.Race;
    const PlannedStartMode first = PlannedStartMode.FirstRider, gate = PlannedStartMode.Gate;
    return Plan("MX club day", 5, 120,
      Session(8, 30, "Practice", practice, first, 15, 0, null, GroupA),
      Session(8, 50, "Practice", practice, first, 15, 0, null, GroupB),
      Session(9, 15, "Qualifying", qualifying, first, 15, 0, null, GroupA),
      Session(9, 35, "Qualifying", qualifying, first, 15, 0, null, GroupB),
      Session(10, 30, "Moto 1", race, gate, 20, 1, GroupA, GroupA),
      Session(11, 0, "Moto 1", race, gate, 15, 1, GroupB, GroupB),
      Session(13, 0, "Moto 2", race, gate, 20, 1, GroupA, GroupA),
      Session(13, 30, "Moto 2", race, gate, 15, 1, GroupB, GroupB));
  }

  private static DayPlan TrainingDay()
  {
    var sessions = Enumerable.Range(0, 6)
      .Select(i => Session(9 + i / 2, i % 2 * 30, "Practice", SessionType.FreePractice, PlannedStartMode.FirstRider,
        20, 0, null, i % 2 == 0 ? GroupA : GroupB))
      .ToArray();
    return Plan("Training day", 5, 120, sessions);
  }

  /// <summary>As the club ran 20 September 2026: Lauf 1 in the morning, Lauf 2 in waves after lunch.</summary>
  private static DayPlan Enduro()
  {
    var race2 = Session(12, 45, "Race 2", SessionType.Race, PlannedStartMode.Waves, 120, 0, null, GroupB);
    return Plan("Enduro", 15, 600,
      Session(9, 0, "Race 1", SessionType.Race, PlannedStartMode.Gate, 120, 0, null, GroupA),
      race2);
  }

  private static DayPlan TeamRace() => Plan("Team race", 10, 120,
    Session(10, 0, "Practice", SessionType.FreePractice, PlannedStartMode.FirstRider, 15, 0, null),
    new PlannedSession
    {
      Time = new TimeSpan(11, 0, 0),
      Name = "Team race",
      SessionType = SessionType.Race,
      Start = PlannedStartMode.Gate,
      DurationMinutes = 60,
      AdditionalLaps = 1,
      TeamEvent = true
    });
}
