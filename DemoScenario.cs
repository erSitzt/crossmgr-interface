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
  SecondRiderOut
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
