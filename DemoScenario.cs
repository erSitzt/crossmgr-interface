using System.Text;

namespace CrossMgrInterface;

/// <summary>What a demo read stands for. Everything but <see cref="Lap"/> is a problem put in on purpose.</summary>
public enum DemoReadKind
{
  /// <summary>A rider crossing the line.</summary>
  Lap,

  /// <summary>The same pass read a second time a few seconds later. Not counted.</summary>
  TooSoon,

  /// <summary>A rider on a transponder that is not on the rider list.</summary>
  Unknown,

  /// <summary>A rider read before their class has left the gate. Ignored.</summary>
  BeforeWave,

  /// <summary>A team rider standing near the loop, read again and again. Not counted.</summary>
  Waiting,

  /// <summary>A team rider going out while their teammate is still on track.</summary>
  SecondRiderOut,

  /// <summary>A transponder that is nobody's on the rider list and not a rider at all - a marshal's bike over the loop.</summary>
  Stray
}

/// <summary>One row of a demo's rider list.</summary>
public sealed record DemoRider(string Tag, string Number, string Name, string Team, string Class);

/// <summary>
/// One read the demo reader sends. <see cref="At"/> counts from the moment the
/// reader starts, or - when <see cref="AfterWaveOf"/> names a class - from the
/// moment that class actually left the gate, so a wave the operator starts
/// early or late takes its riders with it.
/// </summary>
public sealed record DemoCrossing(TimeSpan At, string Tag, DemoReadKind Kind = DemoReadKind.Lap,
  string? AfterWaveOf = null);

/// <summary>
/// A whole session planned in advance: the rider list, how the session is set
/// up, and every read the reader will send. Pure data, so a test can hold a
/// demo to what its intro card promises without opening a window.
/// </summary>
public sealed class DemoScenario
{
  public required string Id { get; init; }
  public required string Title { get; init; }

  /// <summary>How long it takes, in words: "About 10 minutes".</summary>
  public required string Length { get; init; }

  public required string Summary { get; init; }
  public required IReadOnlyList<string> WhatHappens { get; init; }
  public required IReadOnlyList<string> WhatToTry { get; init; }

  public SessionType SessionType { get; init; } = SessionType.Race;
  public required int DurationMinutes { get; init; }
  public int AdditionalLaps { get; init; }

  /// <summary>The gap between classes leaving the gate, in rider-list order. Null for one start.</summary>
  public TimeSpan? WaveGap { get; init; }

  public bool TeamEvent { get; init; }

  public required IReadOnlyList<DemoRider> Roster { get; init; }

  /// <summary>Every read, in the order the reader plans to send them.</summary>
  public required IReadOnlyList<DemoCrossing> Crossings { get; init; }

  /// <summary>The problems planted for the operator to put right, in the order they happen. Empty for most demos.</summary>
  public IReadOnlyList<DemoProblem> Problems { get; init; } = Array.Empty<DemoProblem>();

  /// <summary>A wave start is always a manual one: the operator sends the first class.</summary>
  public bool ManualStart => WaveGap.HasValue;

  /// <summary>The classes on the rider list, in the order it has them - which is the start order.</summary>
  public IReadOnlyList<string> Classes =>
    Roster.Select(r => r.Class).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

  /// <summary>The rider list as the CSV the importer reads. Names never hold a comma: it splits on plain commas.</summary>
  public string ToRiderCsv()
  {
    var csv = new StringBuilder("tagid,number,name,team,class\n");
    foreach (var rider in Roster)
      csv.Append($"{rider.Tag},{rider.Number},{rider.Name},{rider.Team},{rider.Class}\n");
    return csv.ToString();
  }

  /// <summary>The setup the wizard would have produced, for the rider list saved at <paramref name="riderListPath"/>.</summary>
  public NewRaceSetup ToSetup(string riderListPath) => new()
  {
    SessionType = SessionType,
    // On the printed sheet as well, so a demo result never passes for a real one.
    RaceName = $"Demo: {Title}",
    DurationMinutes = DurationMinutes,
    AdditionalLaps = AdditionalLaps,
    ManualStart = ManualStart,
    Waves = WaveGap is { } gap
      ? Classes.Select((c, i) => (c, i == 0 ? TimeSpan.Zero : gap)).ToList()
      : null,
    TeamEvent = TeamEvent,
    StartReader = true,
    ImportedFile = riderListPath
  };
}

/// <summary>What goes wrong in a problem the problems demo plants - each has its own test of being put right.</summary>
public enum DemoProblemKind
{
  /// <summary>A rider on a spare transponder that is not on the rider list: identify it as them.</summary>
  IdentifySpare,

  /// <summary>A transponder that is nobody's - a marshal's bike: stop counting it.</summary>
  StopCounting,

  /// <summary>A pass read twice: nothing to do.</summary>
  ReadTwice,

  /// <summary>A lap long enough to be two or three: split it.</summary>
  SplitMissedRead,

  /// <summary>A team rider read while a teammate was still out: delete that lap.</summary>
  DeleteSecondRider,

  /// <summary>A rider who carries on with a spare transponder: merge the spare into them.</summary>
  MergeSpare,

  /// <summary>Race control reports a rider retired: mark them DNF.</summary>
  MarkDnf,

  /// <summary>That rider crosses the line again: back in the race, and count the reads.</summary>
  BackInTheRace,

  /// <summary>The reader sends nothing for a while: nothing to do but notice.</summary>
  ReaderOutage,

  /// <summary>The long laps an outage leaves: split every one.</summary>
  SplitAfterOutage,

  /// <summary>A rider who really does retire: nothing to do, the finish makes them DNF.</summary>
  Retires
}

/// <summary>
/// One problem a demo plants: when it happens, what it is, and what the operator
/// should do about it - for the demo's checklist, see <see cref="DemoChecklist"/>.
/// <see cref="At"/> counts from the demo reader's start, as a read's does.
/// </summary>
public sealed record DemoProblem(DemoProblemKind Kind, TimeSpan At, string Title, string HowTo)
{
  /// <summary>The transponder, or a team's entry, the problem is about.</summary>
  public string Tag { get; init; } = "";

  /// <summary>A second transponder: the spare merged in, or the team rider read too early.</summary>
  public string? OtherTag { get; init; }

  /// <summary>The start number a spare should be identified as.</summary>
  public string? Number { get; init; }

  /// <summary>How many laps a long lap should be split into.</summary>
  public int Laps { get; init; }

  /// <summary>When the stretch of track the problem is about began - a long lap's start, an outage's.</summary>
  public TimeSpan Since { get; init; }

  /// <summary>When an outage ends.</summary>
  public TimeSpan Until { get; init; }

  /// <summary>After this there is no longer any point doing it: the rider marked DNF is already back.</summary>
  public TimeSpan? Deadline { get; init; }

  /// <summary>Every entry an outage left a long lap on.</summary>
  public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

  /// <summary>Said in a banner the moment it happens, as race control would say it over the radio.</summary>
  public string? Announce { get; init; }

  /// <summary>Something to see rather than to put right, so not counted among the fixes.</summary>
  public bool JustWatch => Kind is DemoProblemKind.ReadTwice or DemoProblemKind.ReaderOutage or DemoProblemKind.Retires;
}
