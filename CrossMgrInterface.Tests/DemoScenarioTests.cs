using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// Holds each demo to what its intro card promises. A demo that quietly stops
/// producing its missed read, or stops before its race can finish, would still
/// run - it would just show a volunteer something other than it says.
/// </summary>
public class DemoScenarioTests
{
  private static readonly TimeSpan MinimumLap = TimeSpan.FromSeconds(10);

  public static TheoryData<string> Ids => new()
  {
    DemoScenarios.RaceId, DemoScenarios.QualifyingId, DemoScenarios.EnduroId, DemoScenarios.TeamsId
  };

  public static TheoryData<string> Races => new()
  {
    DemoScenarios.RaceId, DemoScenarios.EnduroId, DemoScenarios.TeamsId
  };

  [Fact]
  public void EveryDemoIsListedAndCanBeFoundByItsId()
  {
    Assert.Equal(
      new[] { DemoScenarios.RaceId, DemoScenarios.QualifyingId, DemoScenarios.EnduroId, DemoScenarios.TeamsId },
      DemoScenarios.All.Select(s => s.Id));
    Assert.All(DemoScenarios.All, s => Assert.Same(s, DemoScenarios.Find(s.Id.ToUpperInvariant())));
    Assert.Null(DemoScenarios.Find("rally"));
  }

  [Theory, MemberData(nameof(Ids))]
  public void ADemoIsTheSameEveryTime(string id)
  {
    Assert.Equal(DemoScenarios.Build(id).Roster, DemoScenarios.Build(id).Roster);
    Assert.Equal(DemoScenarios.Build(id).Crossings, DemoScenarios.Build(id).Crossings);
  }

  [Theory, MemberData(nameof(Ids))]
  public void TheRiderListImportsRowForRow(string id)
  {
    var scenario = DemoScenarios.Build(id);
    var path = Path.Combine(Path.GetTempPath(), $"demo-{id}-{Guid.NewGuid():N}.csv");

    try
    {
      File.WriteAllText(path, scenario.ToRiderCsv());
      var importer = new RiderDataImporter();
      var result = importer.ImportFromCsvDetailed(path);

      Assert.Empty(result.Skipped);
      Assert.Equal(scenario.Roster.Count, importer.Rows.Count);
      for (var i = 0; i < scenario.Roster.Count; i++)
      {
        var (row, rider) = (importer.Rows[i], scenario.Roster[i]);
        Assert.Equal(rider.Tag, row.TagID);
        Assert.Equal(rider.Number, row.RiderNumber);
        Assert.Equal(rider.Name, row.FullName);
        Assert.Equal(rider.Team, row.Team);
        Assert.Equal(rider.Class, row.Category);
      }
    }
    finally
    {
      File.Delete(path);
    }
  }

  [Theory, MemberData(nameof(Ids))]
  public void EveryTransponderReadIsOnTheRiderListExceptTheSpare(string id)
  {
    var scenario = DemoScenarios.Build(id);
    var listed = scenario.Roster.Select(r => r.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);

    Assert.All(scenario.Crossings, c => Assert.Equal(c.Kind != DemoReadKind.Unknown, listed.Contains(c.Tag)));
  }

  /// <summary>
  /// Plays every entry's reads through the application's own rule for a read
  /// too soon to be a lap: the reads meant to be refused are, and no others.
  /// </summary>
  [Theory, MemberData(nameof(Ids))]
  public void OnlyTheReadsMeantToBeRefusedComeTooSoonForALap(string id)
  {
    var scenario = DemoScenarios.Build(id);
    var start = new DateTime(2026, 9, 14, 10, 0, 0);

    var entries = scenario.Crossings
      .Where(c => c.Kind != DemoReadKind.BeforeWave)
      .GroupBy(c => EntryOf(scenario, c.Tag));

    foreach (var entry in entries)
    {
      var isTeam = entry.Key.StartsWith("TEAM:", StringComparison.Ordinal);
      DateTime? lastCounted = null;
      var lastRead = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

      foreach (var read in entry.OrderBy(c => Absolute(scenario, c)))
      {
        var at = start + Absolute(scenario, read);
        var meantToBeRefused = read.Kind is DemoReadKind.TooSoon or DemoReadKind.Waiting;
        DateTime? ownLast = lastRead.TryGetValue(read.Tag, out var own) ? own : null;

        var refused = lastCounted is { } last &&
                      ReadDebounce.Check(at, last, ownLast, MinimumLap, isTeam).Reject;

        Assert.True(refused == meantToBeRefused,
          $"{id}: the {read.Kind} read of {read.Tag} at {read.At} would be {(refused ? "refused" : "counted")}");

        if (!refused) lastCounted = at;
        lastRead[read.Tag] = at;
      }
    }
  }

  /// <summary>
  /// A race only notices its time is up on the next crossing; then the leader
  /// rides the lap in progress plus the extra laps, and everyone else finishes
  /// theirs. Stop sending any sooner and the demo never finishes.
  /// </summary>
  [Theory, MemberData(nameof(Races))]
  public void RidersKeepComingRoundUntilTheRaceCanFinish(string id)
  {
    var scenario = DemoScenarios.Build(id);
    var flag = FlagAt(scenario);
    var retired = new List<string>();

    var entries = scenario.Crossings
      .Where(c => c.Kind is DemoReadKind.Lap or DemoReadKind.Unknown)
      .GroupBy(c => EntryOf(scenario, c.Tag));

    foreach (var entry in entries)
    {
      var afterFlag = entry.Count(c => Absolute(scenario, c) > flag);
      if (afterFlag == 0)
      {
        retired.Add(entry.Key);
        continue;
      }

      Assert.True(afterFlag >= scenario.AdditionalLaps + 2,
        $"{id}: {entry.Key} is read only {afterFlag} times after the flag");
    }

    Assert.True(retired.Count <= 1, $"{id}: {string.Join(", ", retired)} stop before the flag");
  }

  [Fact]
  public void QualifyingProducesTheGatePickOrderItsCardDescribes()
  {
    var scenario = DemoScenarios.Build(DemoScenarios.QualifyingId);
    var flag = FlagAt(scenario);

    var picks = scenario.Roster
      .Select(rider =>
      {
        var times = scenario.Crossings
          .Where(c => c.Tag == rider.Tag && c.Kind == DemoReadKind.Lap)
          .Select(c => c.At)
          .OrderBy(t => t)
          .ToList();

        // The lap in progress at the flag counts; nothing after it does.
        var firstAfter = times.FindIndex(t => t > flag);
        if (firstAfter >= 0) times.RemoveRange(firstAfter + 1, times.Count - firstAfter - 1);

        var best = times.Skip(1)
          .Select((t, i) => (Lap: t - times[i], SetAt: t))
          .OrderBy(l => l.Lap).ThenBy(l => l.SetAt)
          .Select(l => ((TimeSpan Lap, TimeSpan SetAt)?)l)
          .FirstOrDefault();

        return (rider.Number, Best: best);
      })
      .Where(p => p.Best.HasValue)
      .OrderBy(p => p.Best!.Value.Lap).ThenBy(p => p.Best!.Value.SetAt)
      .Select(p => p.Number);

    Assert.Equal(new[] { "66", "22", "55", "33", "44", "11", "88" }, picks);
  }

  [Fact]
  public void InTheEnduroNobodyIsReadBeforeTheirClassHasGoneExceptTheRiderOnTheWayToTheGate()
  {
    var scenario = DemoScenarios.Build(DemoScenarios.EnduroId);
    var classOf = scenario.Roster.ToDictionary(r => r.Tag, r => r.Class);

    Assert.Equal(new[] { "MX1", "MX2", "Youth" }, scenario.Classes);
    Assert.All(scenario.Crossings.Where(c => c.Kind == DemoReadKind.Lap), c =>
    {
      Assert.Equal(classOf[c.Tag], c.AfterWaveOf);
      Assert.True(c.At > TimeSpan.Zero);
    });

    var early = Assert.Single(scenario.Crossings, c => c.Kind == DemoReadKind.BeforeWave);
    Assert.NotEqual(scenario.Classes[0], classOf[early.Tag]);
    Assert.True(Absolute(scenario, early) < WaveOffset(scenario, classOf[early.Tag]));
  }

  [Fact]
  public void TheEnduroIsSetUpAsWavesAMinuteApart()
  {
    var setup = DemoScenarios.Build(DemoScenarios.EnduroId).ToSetup(@"C:\demo\riders.csv");

    Assert.True(setup.ManualStart);
    Assert.Equal(
      new[] { ("MX1", TimeSpan.Zero), ("MX2", TimeSpan.FromMinutes(1)), ("Youth", TimeSpan.FromMinutes(1)) },
      setup.Waves!);
    Assert.StartsWith("Demo: ", setup.RaceName);
    Assert.Equal(@"C:\demo\riders.csv", setup.ImportedFile);
  }

  [Fact]
  public void TheTeamDemoMakesSixTeamsOneOnASharedTransponderAndTwoSoloRiders()
  {
    var scenario = DemoScenarios.Build(DemoScenarios.TeamsId);
    var roster = TeamRoster.Build(ImportRows(scenario));

    Assert.True(scenario.TeamEvent);
    Assert.Equal(6, roster.Teams.Count);
    Assert.Equal(2, roster.Solos.Count);
    var shared = Assert.Single(roster.Teams, t => TransponderGroup.Of(t.Members).Any(g => g.IsShared));
    Assert.Equal("RC Falke", shared.Name);
  }

  /// <summary>
  /// Every team's laps through the application's own two-on-track check: the
  /// rider sent out early is caught, and no ordinary handover is.
  /// </summary>
  [Fact]
  public void InTheTeamDemoOnlyTheRiderSentOutEarlyLooksLikeTwoOnTrack()
  {
    var scenario = DemoScenarios.Build(DemoScenarios.TeamsId);
    var roster = TeamRoster.Build(ImportRows(scenario));

    foreach (var team in roster.Teams)
    {
      var tags = team.Members.SelectMany(m => m.Transponders).ToHashSet(StringComparer.OrdinalIgnoreCase);
      var reads = scenario.Crossings
        .Where(c => tags.Contains(c.Tag) && c.Kind is DemoReadKind.Lap or DemoReadKind.SecondRiderOut)
        .OrderBy(c => c.At)
        .ToList();

      var builder = RiderBuilder.Team(team.Name, team.Members.ToArray());
      var previous = TimeSpan.Zero;
      foreach (var read in reads)
      {
        builder.LapBy((read.At - previous).TotalSeconds, read.Tag);
        previous = read.At;
      }

      var entry = builder.Build();
      TwoOnTrackDetector.Analyze(entry, fieldPace: null);

      var flagged = reads.Where((_, i) => entry.Laps[i].IsSuspectedOverlap).ToList();
      var early = reads.Where(r => r.Kind == DemoReadKind.SecondRiderOut).ToList();

      if (early.Count == 0)
        Assert.True(flagged.Count == 0, $"{team.Name}: an ordinary handover looks like two on track");
      else
        Assert.Contains(early[0], flagged);
    }

    Assert.Single(scenario.Crossings, c => c.Kind == DemoReadKind.SecondRiderOut);
  }

  // ---- Helpers ---------------------------------------------------------------

  private static RiderDataImporter.RiderImportData[] ImportRows(DemoScenario scenario) =>
    scenario.Roster.Select(r =>
    {
      var parts = r.Name.Split(' ', 2);
      return new RiderDataImporter.RiderImportData
      {
        TagID = r.Tag,
        RiderNumber = r.Number,
        FirstName = parts[0],
        LastName = parts.Length > 1 ? parts[1] : "",
        Team = r.Team,
        Category = r.Class
      };
    }).ToArray();

  /// <summary>What a read scores for: its team in a team event, otherwise its transponder.</summary>
  private static string EntryOf(DemoScenario scenario, string tag)
  {
    if (!scenario.TeamEvent) return tag;

    var team = scenario.Roster.FirstOrDefault(r => r.Tag == tag)?.Team ?? "";
    var members = scenario.Roster.Count(r => team.Length > 0 && string.Equals(r.Team, team, StringComparison.OrdinalIgnoreCase));
    return members > 1 ? "TEAM:" + team : tag;
  }

  /// <summary>When a read happens, counted from the first start, with every wave going on time.</summary>
  private static TimeSpan Absolute(DemoScenario scenario, DemoCrossing read) =>
    read.AfterWaveOf == null ? read.At : read.At + WaveOffset(scenario, read.AfterWaveOf);

  private static TimeSpan WaveOffset(DemoScenario scenario, string className) =>
    scenario.WaveGap!.Value * scenario.Classes.ToList()
      .FindIndex(c => string.Equals(c, className, StringComparison.OrdinalIgnoreCase));

  /// <summary>When the clock runs out: from the first class's start, or from the first crossing.</summary>
  private static TimeSpan FlagAt(DemoScenario scenario)
  {
    var clockStart = scenario.WaveGap.HasValue
      ? TimeSpan.Zero
      : scenario.Crossings.Where(c => c.Kind != DemoReadKind.BeforeWave).Min(c => c.At);
    return clockStart + TimeSpan.FromMinutes(scenario.DurationMinutes);
  }
}
