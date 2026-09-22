using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// The two demos that replay a real race day. They are not planned, so there
/// is no card to hold them to; what they must be is faithful - the day's own
/// setup, and reads that score to the day's own sheet - and free of names.
/// </summary>
public class RealRaceDemoTests
{
  public static TheoryData<string> Ids => new() { DemoScenarios.Lauf1Id, DemoScenarios.Lauf2Id };

  /// <summary>
  /// The files are checked in, so this is the guard against a real name ever
  /// being committed: every part of every name is three letters and stars,
  /// the shape NamePrivacy gives a hidden name on the website.
  /// </summary>
  [Theory, MemberData(nameof(Ids))]
  public void EveryNameIsHidden(string id)
  {
    var scenario = DemoScenarios.Build(id);

    Assert.All(scenario.Roster, rider =>
    {
      Assert.False(string.IsNullOrWhiteSpace(rider.Name), $"#{rider.Number} has no name at all");
      Assert.All(rider.Name.Split(' '), part =>
        Assert.True(part.Length <= NamePrivacy.StarredKeeps || part[NamePrivacy.StarredKeeps..].All(c => c == '*'),
          $"#{rider.Number}: \"{part}\" is not a hidden name"));
    });
  }

  [Fact]
  public void Lauf1WasTwoHoursWithNoExtraLapsAndOneStartPressedByTheOperator()
  {
    var scenario = DemoScenarios.Build(DemoScenarios.Lauf1Id);
    var setup = scenario.ToSetup(@"C:\demo\riders.csv");

    Assert.Equal(SessionType.Race, scenario.SessionType);
    Assert.Equal(120, setup.DurationMinutes);
    Assert.Equal(0, setup.AdditionalLaps);
    Assert.True(setup.ManualStart);
    Assert.Null(setup.Waves);
    Assert.Equal(81, scenario.Roster.Count);
    Assert.Equal(5, scenario.Classes.Count);

    // One start, pressed by hand: every read counts from that press, none from a class's gate.
    Assert.All(scenario.Crossings, c => Assert.Null(c.AfterWaveOf));
  }

  [Fact]
  public void Lauf2WasTheSameInThreeWavesAMinuteApart()
  {
    var scenario = DemoScenarios.Build(DemoScenarios.Lauf2Id);
    var setup = scenario.ToSetup(@"C:\demo\riders.csv");

    Assert.Equal(120, setup.DurationMinutes);
    Assert.Equal(0, setup.AdditionalLaps);
    Assert.True(setup.ManualStart);
    Assert.Equal(
      new[] { ("1_expert", TimeSpan.Zero), ("2_racer", TimeSpan.FromMinutes(1)), ("3_senior1", TimeSpan.FromMinutes(1)) },
      setup.Waves!);
    Assert.Equal(119, scenario.Roster.Count);

    var classOf = scenario.Roster.ToDictionary(r => r.Tag, r => r.Class);
    Assert.All(scenario.Crossings, c => Assert.Equal(classOf[c.Tag], c.AfterWaveOf));
  }

  /// <summary>
  /// Plays the reads through the race's own finishing rule - the clock runs
  /// out, the leader finishes the lap they are on, everyone else finishes
  /// theirs - and expects every rider to end on the laps the club's sheet gave
  /// them. A read dropped or shifted in the file would show up here.
  /// </summary>
  [Theory, MemberData(nameof(Ids))]
  public void ReplayedThroughTheFlagRuleTheReadsScoreToTheDaysSheet(string id)
  {
    var scenario = DemoScenarios.Build(id);
    var sheet = DemoScenarios.ReadRealRace(id).LapsOnTheSheet;
    var clock = TimeSpan.FromMinutes(scenario.DurationMinutes);

    // Every read on the clock's own timeline, which runs from the first gate.
    var crossings = scenario.Roster.ToDictionary(r => r.Tag, r => scenario.Crossings
      .Where(c => c.Tag == r.Tag)
      .Select(c => c.At + WaveOffset(scenario, c.AfterWaveOf))
      .OrderBy(t => t)
      .ToList());

    // The leader when the clock ran out: most laps, and of those the first to have completed them.
    var leader = crossings
      .Where(p => p.Value.Count > 0)
      .Select(p => (Tag: p.Key, Laps: p.Value.Count(t => t <= clock), Last: p.Value.LastOrDefault(t => t <= clock)))
      .OrderByDescending(l => l.Laps).ThenBy(l => l.Last)
      .First();
    var target = ChequeredFlag.TargetLaps(leader.Laps, scenario.AdditionalLaps);
    var flag = crossings[leader.Tag].First(t => t > clock);

    foreach (var rider in scenario.Roster)
    {
      var times = crossings[rider.Tag];
      var atFlag = times.Count(t => t <= flag);
      var allowed = ChequeredFlag.AllowedLap(atFlag, target);
      var scored = Math.Min(times.Count, allowed);

      Assert.True(scored == sheet[rider.Tag],
        $"{id}: #{rider.Number} {rider.Name} scores {scored} laps replayed, {sheet[rider.Tag]} on the sheet");
    }
  }

  private static TimeSpan WaveOffset(DemoScenario scenario, string? className) =>
    className == null
      ? TimeSpan.Zero
      : scenario.WaveGap!.Value * scenario.Classes.ToList()
        .FindIndex(c => string.Equals(c, className, StringComparison.OrdinalIgnoreCase));
}
