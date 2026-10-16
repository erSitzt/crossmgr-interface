using System.Text.RegularExpressions;

namespace CrossMgrInterface;

/// <summary>How a planned session's clock starts. See the wizard's Start step.</summary>
public enum PlannedStartMode
{
  /// <summary>The first rider of the session crossing the line starts it.</summary>
  FirstRider,

  /// <summary>The operator presses START RACE when the gate drops.</summary>
  Gate,

  /// <summary>The classes leave one after another, a fixed gap apart. Started by START RACE.</summary>
  Waves
}

/// <summary>
/// One session of the day, as planned the evening before. Everything the
/// New race wizard would ask, answered in advance - except the rider list,
/// which is the day's, and the reader, which is set up once.
/// </summary>
public sealed class PlannedSession
{
  public string Id { get; set; } = DayPlan.NewId();

  /// <summary>Time of day it is planned for, on the plan's date.</summary>
  public TimeSpan Time { get; set; } = TimeSpan.FromHours(9);

  /// <summary>
  /// A new time typed on the day, beside the planned one. A typed time is a
  /// promise to the riders - "Moto 2 at 11:15, whatever happens before" - so
  /// the running-late shift leaves it where it is, and the sessions after it
  /// move by as much as it moved from its plan. Never copied to another day.
  /// </summary>
  public TimeSpan? TypedTime { get; set; }

  public string Name { get; set; } = "";
  public SessionType SessionType { get; set; } = SessionType.Race;

  /// <summary>The classes it counts. Empty counts the whole rider list. See SessionClassGate.</summary>
  public List<string> Classes { get; set; } = new();

  public int DurationMinutes { get; set; } = 15;

  /// <summary>Races only; a timed session ends on the chequered flag.</summary>
  public int AdditionalLaps { get; set; } = 1;

  public PlannedStartMode Start { get; set; } = PlannedStartMode.Gate;

  /// <summary>The gap between one class leaving and the next, for <see cref="PlannedStartMode.Waves"/>.</summary>
  public int WaveGapSeconds { get; set; } = 60;

  /// <summary>Riders sharing a team name score as one. Races only, and never with classes or waves.</summary>
  public bool TeamEvent { get; set; }

  /// <summary>
  /// Sessions sharing this name are scored together as one overall - Moto 1
  /// and Moto 2 of "MX1/MX2". Null for a session scored on its own.
  /// </summary>
  public string? OverallGroup { get; set; }

  /// <summary>Not run today - rained off, or merged into another. Kept, so the timetable still shows it.</summary>
  public bool Skipped { get; set; }

  public bool IsTimed => SessionType != SessionType.Race;

  /// <summary>"MX1, MX2", or "All classes".</summary>
  public string ClassesText => Classes.Count == 0 ? "All classes" : SessionClassGate.Describe(Classes);

  /// <summary>
  /// What the session is set up with: the wizard's answers, from the plan.
  /// The rider list is the day's and already loaded, so nothing is imported.
  /// </summary>
  /// <param name="overall">An overall an earlier moto of the group started, to join; null to start one.</param>
  public NewRaceSetup ToSetup(int? overall) => new()
  {
    SessionType = SessionType,
    RaceName = Name,
    DurationMinutes = DurationMinutes,
    AdditionalLaps = IsTimed ? 0 : AdditionalLaps,
    // A wave start is a manual one: the first class goes on START RACE.
    ManualStart = Start != PlannedStartMode.FirstRider,
    Waves = !IsTimed && !TeamEvent && Start == PlannedStartMode.Waves && Classes.Count >= 2
      ? Classes.Select((c, i) => (c, i == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(WaveGapSeconds))).ToList()
      : null,
    TeamEvent = !IsTimed && TeamEvent,
    Classes = TeamEvent ? null : Classes.ToList(),
    StartReader = true,
    ImportedFile = null,
    OverallId = OverallGroup != null && !IsTimed ? overall : null,
    NewOverall = OverallGroup != null && !IsTimed && overall == null,
    OverallName = OverallGroup != null ? $"Overall - {OverallGroup}" : null
  };

  public PlannedSession Copy() => new()
  {
    Time = Time,
    Name = Name,
    SessionType = SessionType,
    Classes = Classes.ToList(),
    DurationMinutes = DurationMinutes,
    AdditionalLaps = AdditionalLaps,
    Start = Start,
    WaveGapSeconds = WaveGapSeconds,
    TeamEvent = TeamEvent,
    OverallGroup = OverallGroup
  };

  /// <summary>
  /// The next moto of this one: "Moto 1 - MX1/MX2" becomes "Moto 2 - MX1/MX2",
  /// with the same classes and length, in the same overall. The name's number
  /// is counted up, whatever the club calls a moto - Lauf 1, Heat 1, Race 1.
  /// </summary>
  public PlannedSession NextMoto()
  {
    var next = Copy();
    var match = Regex.Match(Name, @"^(\s*(?:moto|lauf|heat|race|run)\s*)(\d+)", RegexOptions.IgnoreCase);
    next.Name = match.Success
      ? match.Groups[1].Value + (int.Parse(match.Groups[2].Value) + 1) + Name[match.Length..]
      : Name + " (2)";

    // A moto that had no overall gets one now, named after what the two share:
    // the rest of the name, or the classes when the name is only "Moto 1".
    next.OverallGroup ??= GroupFromName(Name) ?? (match.Success ? ClassesText : Name.Trim());
    return next;
  }

  /// <summary>"Moto 1 - MX1/MX2" → "MX1/MX2". Null when the name says nothing more than "Moto 1".</summary>
  internal static string? GroupFromName(string name)
  {
    var rest = Regex.Replace(name, @"^\s*(moto|lauf|heat|race|run)\s*\d+\s*[-–:]?\s*", "", RegexOptions.IgnoreCase).Trim();
    return rest.Length > 0 && rest != name.Trim() ? rest : null;
  }
}

/// <summary>
/// A whole day, planned in advance: the timetable, and what is shared by every
/// session in it - the rider list and the circuit.
///
/// What has happened is never stored here. Whether a session has run, and
/// when, is read from the race database (DbRace.PlanEntryId) every time it is
/// needed, so a crash, a deleted false start or a session reopened from Past
/// sessions can never leave the plan believing something the database does
/// not. The plan holds only decisions: the order, the times, what to skip.
/// </summary>
public sealed class DayPlan
{
  public string Id { get; set; } = NewId();

  /// <summary>Identity on the websites, so the programme can be sent again and replace itself.</summary>
  public string PublicId { get; set; } = NewId();

  public string Name { get; set; } = "";

  /// <summary>The day it is for. Only the date matters.</summary>
  public DateTime Date { get; set; } = DateTime.Today;

  public string? TrackId { get; set; }
  public string? RiderListPath { get; set; }

  /// <summary>
  /// The least time between one session's end and the next one's start - the
  /// gate cleared, the next group lined up. What keeps a late day from
  /// planning two sessions on top of each other.
  /// </summary>
  public int ChangeoverMinutes { get; set; } = 5;

  /// <summary>A lap of the circuit, for guessing when a race ends: its length plus the extra laps.</summary>
  public int TypicalLapSeconds { get; set; } = 150;

  public List<PlannedSession> Sessions { get; set; } = new();

  /// <summary>What happens by itself around each session of the day. All on for a new plan.</summary>
  public AfterSession After { get; set; } = AfterSession.ForNewPlan();

  public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

  public static string NewId() => Guid.NewGuid().ToString("N");

  public PlannedSession? Find(string? entryId) =>
    entryId == null ? null : Sessions.FirstOrDefault(s => s.Id == entryId);

  /// <summary>When a session is planned to start, on the plan's date.</summary>
  public DateTime PlannedStart(PlannedSession session) => Date.Date + session.Time;

  /// <summary>
  /// How long a session takes, by the plan: its length, then for a race the
  /// lap in progress and the extra laps; for a timed session the lap the flag
  /// finds riders on, and the grace after it.
  /// </summary>
  public TimeSpan PlannedLength(PlannedSession session, TimeSpan? lap = null)
  {
    var lapTime = lap ?? TimeSpan.FromSeconds(TypicalLapSeconds);
    var tail = session.IsTimed ? 1.5 : session.AdditionalLaps + 1;
    return TimeSpan.FromMinutes(session.DurationMinutes) + lapTime * tail;
  }

  /// <summary>
  /// A copy for another day: the same timetable, new identities - so the two
  /// days' sessions can never be mistaken for each other in the database or
  /// on the website - and nothing typed on the day.
  /// </summary>
  public DayPlan CopyFor(DateTime date, string? name = null) => new()
  {
    Name = name ?? Name,
    Date = date.Date,
    TrackId = TrackId,
    RiderListPath = RiderListPath,
    ChangeoverMinutes = ChangeoverMinutes,
    TypicalLapSeconds = TypicalLapSeconds,
    Sessions = Sessions.Select(s => s.Copy()).ToList(),
    After = After.Copy()
  };

  /// <summary>Things worth fixing before the day; empty when the plan is fine.</summary>
  public List<string> Problems(IReadOnlyCollection<string> listClasses)
  {
    var problems = new List<string>();

    foreach (var session in Sessions)
    {
      var name = session.Name.Length > 0 ? session.Name : $"The session at {session.Time:hh\\:mm}";
      if (session.Name.Trim().Length == 0) problems.Add($"{name} has no name.");

      var unknown = session.Classes
        .Where(c => !listClasses.Contains(c, StringComparer.OrdinalIgnoreCase))
        .ToList();
      if (listClasses.Count > 0 && unknown.Count > 0)
        problems.Add($"{name}: {string.Join(", ", unknown)} is not a class on the rider list - nobody would be counted for it.");

      if (session.Start == PlannedStartMode.Waves && session.Classes.Count < 2)
        problems.Add($"{name} starts in waves, which needs at least two classes.");
    }

    foreach (var group in Sessions.Where(s => s.OverallGroup != null).GroupBy(s => s.OverallGroup!, StringComparer.OrdinalIgnoreCase))
    {
      if (group.Any(s => s.IsTimed))
        problems.Add($"Overall {group.Key}: only races count towards an overall.");
      else if (group.Select(s => string.Join("|", s.Classes.OrderBy(c => c, StringComparer.OrdinalIgnoreCase)))
               .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        problems.Add($"Overall {group.Key}: its motos are for different classes.");
    }

    var ordered = Sessions.Where(s => !s.Skipped).OrderBy(s => s.Time).ToList();
    if (!ordered.Select(s => s.Id).SequenceEqual(Sessions.Where(s => !s.Skipped).Select(s => s.Id)))
      problems.Add("The sessions are not in the order of their times. Move them, or change a time.");

    return problems;
  }
}
