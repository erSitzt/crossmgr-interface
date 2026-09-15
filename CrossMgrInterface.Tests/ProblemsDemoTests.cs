using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// Holds the problems demo to its checklist: every problem it plants happens the
/// way the checklist says, judged by the application's own rules - and nothing
/// else goes wrong that the checklist does not mention.
/// </summary>
public class ProblemsDemoTests
{
  private static readonly DemoScenario Demo = DemoScenarios.Build(DemoScenarios.ProblemsId);
  private static readonly DateTime Start = RiderBuilder.RaceStart;
  private static readonly TimeSpan FieldPace = TimeSpan.FromSeconds(52);

  private static IEnumerable<DemoProblem> All(DemoProblemKind kind) => Demo.Problems.Where(p => p.Kind == kind);

  private static DemoProblem Only(DemoProblemKind kind) => Assert.Single(All(kind));

  /// <summary>
  /// Every entry as the application scores it, from the reads up to <paramref name="upTo"/>:
  /// a team's riders together, a rider's spare transponder with the rider, reads too soon left out.
  /// </summary>
  private static List<(string Key, RiderInfo Entry)> Field(TimeSpan upTo)
  {
    var field = new List<(string, RiderInfo)>();
    var teams = Demo.Roster.GroupBy(r => r.Team).Where(g => g.Key.Length > 0 && g.Count() > 1).ToList();

    foreach (var team in teams)
    {
      var members = team.Select(r => RiderBuilder.Member(r.Number, r.Name, r.Tag)).ToArray();
      field.Add((TeamRoster.KeyFor(team.Key), Laps(RiderBuilder.Team(team.Key, members), team.Select(r => r.Tag), upTo)));
    }

    foreach (var rider in Demo.Roster.Where(r => teams.All(t => t.Key != r.Team)))
    {
      var tags = new List<string> { rider.Tag };
      tags.AddRange(All(DemoProblemKind.IdentifySpare).Where(p => p.Number == rider.Number).Select(p => p.Tag));
      tags.AddRange(All(DemoProblemKind.MergeSpare).Where(p => p.Tag == rider.Tag).Select(p => p.OtherTag!));
      field.Add((rider.Tag, Laps(RiderBuilder.Rider(rider.Tag, rider.Number, rider.Name), tags, upTo)));
    }

    return field;
  }

  /// <summary>
  /// Laps at exactly the moments the reads are planned for. Not through the
  /// builder's seconds: adding those up as doubles drifts a few ticks from the
  /// planned millisecond, and the times are compared exactly.
  /// </summary>
  private static RiderInfo Laps(RiderBuilder builder, IEnumerable<string> tags, TimeSpan upTo)
  {
    var rider = builder.Build();
    var wanted = tags.ToHashSet();
    var previous = TimeSpan.Zero;

    foreach (var read in Demo.Crossings
               .Where(c => wanted.Contains(c.Tag) && c.At <= upTo &&
                           c.Kind is not (DemoReadKind.TooSoon or DemoReadKind.Stray))
               .OrderBy(c => c.At))
    {
      rider.Laps.Add(new RiderLap
      {
        TagID = rider.TagID,
        CrossingTime = Start + read.At,
        LapNumber = rider.Laps.Count + 1,
        LapTime = read.At - previous,
        CrossedBy = read.Tag
      });
      previous = read.At;
    }

    if (rider.Laps.Count > 0)
      rider.LastCrossing = rider.LastCrossingTime = rider.Laps[^1].CrossingTime;

    return rider;
  }

  /// <summary>Both warnings, in the order the application runs them.</summary>
  private static void Judge(RiderInfo entry)
  {
    if (entry.IsTeam) TwoOnTrackDetector.Analyze(entry, FieldPace);
    LapAnomalyDetector.Analyze(entry, FieldPace);
  }

  // ---- The plan ----------------------------------------------------------------

  [Fact]
  public void EveryKindOfProblemIsPlantedAndTheMissedReadsAreOneAndTwo()
  {
    var expected = new[]
    {
      DemoProblemKind.IdentifySpare, DemoProblemKind.StopCounting, DemoProblemKind.ReadTwice,
      DemoProblemKind.SplitMissedRead, DemoProblemKind.SplitMissedRead, DemoProblemKind.DeleteSecondRider,
      DemoProblemKind.MergeSpare, DemoProblemKind.MarkDnf, DemoProblemKind.BackInTheRace,
      DemoProblemKind.ReaderOutage, DemoProblemKind.SplitAfterOutage, DemoProblemKind.Retires
    };

    Assert.True(Demo.TeamEvent);
    Assert.Equal(expected.OrderBy(k => k), Demo.Problems.Select(p => p.Kind).OrderBy(k => k));
    Assert.Equal(new[] { 2, 3 }, All(DemoProblemKind.SplitMissedRead).Select(p => p.Laps).OrderBy(n => n));
  }

  [Fact]
  public void TheProblemsComeOneAfterAnotherWithTimeToFixEach()
  {
    var times = Demo.Problems.Select(p => p.At).ToList();
    Assert.Equal(times.OrderBy(t => t), times);

    var fixes = Demo.Problems
      .Where(p => !p.JustWatch && p.Kind != DemoProblemKind.SplitAfterOutage)
      .Select(p => p.At)
      .ToList();

    for (var i = 1; i < fixes.Count; i++)
      Assert.True(fixes[i] - fixes[i - 1] >= TimeSpan.FromSeconds(20),
        $"the problems at {fixes[i - 1]} and {fixes[i]} are too close together to fix one before the next");
  }

  // ---- Each problem, as the application sees it ---------------------------------

  [Fact]
  public void TheMissedReadsLookLikeTwoAndThreeLapsToTheApplication()
  {
    foreach (var problem in All(DemoProblemKind.SplitMissedRead))
    {
      var rider = Field(problem.At).Single(f => f.Key == problem.Tag).Entry;
      Judge(rider);

      var lap = Assert.Single(rider.Laps, l => l.IsSuggestedForSplit);
      Assert.Equal(Start + problem.At, lap.CrossingTime);
      Assert.Equal(Start + problem.Since, lap.CrossingTime - lap.LapTime!.Value);
      Assert.Equal(problem.Laps, lap.SuggestedSplitCount);
    }
  }

  [Fact]
  public void NothingElseLooksLikeAMissedReadBeforeTheReaderGoesQuiet()
  {
    // Not the stop for the spare, not the stop to fix a chain, not a handover.
    var missed = All(DemoProblemKind.SplitMissedRead).Select(p => Start + p.At).ToList();

    foreach (var (key, entry) in Field(Only(DemoProblemKind.ReaderOutage).At))
    {
      Judge(entry);
      Assert.All(entry.Laps.Where(l => l.IsSuggestedForSplit),
        l => Assert.True(missed.Contains(l.CrossingTime), $"{key}: lap {l.LapNumber} shows CHECK"));
    }
  }

  [Fact]
  public void OnlyTheRiderSentOutEarlyLooksLikeTwoOnTrack()
  {
    var problem = Only(DemoProblemKind.DeleteSecondRider);

    foreach (var (key, entry) in Field(TimeSpan.FromHours(1)).Where(f => f.Entry.IsTeam))
    {
      Judge(entry);
      var flagged = entry.Laps.Where(l => l.IsSuspectedOverlap).Select(l => l.CrossingTime).ToList();

      if (key == problem.Tag)
        Assert.Equal(new[] { Start + problem.At }, flagged);
      else
        Assert.Empty(flagged);
    }
  }

  [Fact]
  public void TheOutageLeavesEveryRiderStillRacingOneLongLapToSplit()
  {
    var outage = Only(DemoProblemKind.SplitAfterOutage);
    var cutShort = new List<string>();

    foreach (var (key, entry) in Field(outage.Until + TimeSpan.FromSeconds(90)))
    {
      var after = entry.Laps.FirstOrDefault(l => l.CrossingTime > Start + outage.Until);
      if (after == null || !entry.Laps.Any(l => l.CrossingTime < Start + outage.Since)) continue;

      Assert.DoesNotContain(entry.Laps,
        l => l.CrossingTime > Start + outage.Since && l.CrossingTime < Start + outage.Until);

      Judge(entry);
      Assert.True(after.IsSuggestedForSplit, $"{key}: the lap across the outage does not show CHECK");
      Assert.InRange(after.SuggestedSplitCount, 2, 3);

      cutShort.Add(entry.IsTeam ? key : after.CrossedBy!);
    }

    Assert.Equal(cutShort.OrderBy(t => t, StringComparer.Ordinal), outage.Tags.OrderBy(t => t, StringComparer.Ordinal));
  }

  [Fact]
  public void TheReaderGoingQuietSetsOffTheNoReadsAlarm()
  {
    var outage = Only(DemoProblemKind.ReaderOutage);
    var lastRead = Start + Demo.Crossings.Where(c => c.At < outage.At).Max(c => c.At);
    var now = Start + outage.At + TimeSpan.FromSeconds(80);

    var stillRacing = Field(outage.At)
      .Where(f => f.Entry.Laps.Count >= 2)
      .Select(f => new ReaderQuietRider(f.Key, f.Entry.LastCrossing, FieldPace))
      .ToList();

    var verdict = ReaderQuietCheck.Evaluate(now, lastRead, stillRacing, FieldPace, fromLapTimes: true,
      fixedAfter: TimeSpan.FromSeconds(60));

    Assert.Equal(ReaderQuietLevel.Silent, verdict.Level);
    Assert.True(now < Start + outage.Until, "the alarm must go off while the reader is still quiet");
  }

  [Fact]
  public void TheFinishDoesNotSoundTheNoReadsAlarmForTheRiderWhoRetired()
  {
    // Found by running the demo: once the field has finished, nothing more is
    // read. A rider who had stopped only a lap or so before the flag still looked
    // due at the line, and the finish told the operator to check a reader that was
    // working perfectly.
    var retires = Only(DemoProblemKind.Retires);
    var rider = Field(TimeSpan.FromHours(1)).Single(f => f.Key == retires.Tag).Entry;
    var flag = Start + Demo.Crossings.Min(c => c.At) + TimeSpan.FromMinutes(Demo.DurationMinutes);

    // Everyone else finishes the lap they are on, and then the loop goes quiet.
    var lastRead = flag + TimeSpan.FromSeconds(60);
    var stillExpected = new[] { new ReaderQuietRider(retires.Tag, rider.LastCrossing, TimeSpan.FromSeconds(54)) };

    var verdict = ReaderQuietCheck.Evaluate(lastRead + TimeSpan.FromSeconds(90), lastRead, stillExpected,
      FieldPace, fromLapTimes: true, fixedAfter: TimeSpan.FromSeconds(60));

    Assert.Equal(ReaderQuietLevel.Ok, verdict.Level);
  }

  [Fact]
  public void RaceControlCallsPaulInWhileHeIsStoppedAndHeRidesOnBeforeTheOutage()
  {
    var mark = Only(DemoProblemKind.MarkDnf);
    var back = Only(DemoProblemKind.BackInTheRace);
    var paul = Demo.Crossings.Where(c => c.Tag == mark.Tag).Select(c => c.At).OrderBy(t => t).ToList();

    Assert.Equal(mark.Tag, back.Tag);
    Assert.NotNull(mark.Announce);
    Assert.Equal(back.At, mark.Deadline);
    Assert.Equal(back.At, paul.First(t => t > mark.At));
    Assert.True(back.At < Only(DemoProblemKind.ReaderOutage).At);

    // Stopping to fix the chain is not long enough to look like a missed read.
    var lastBefore = paul.Last(t => t < mark.At);
    var pace = paul.Zip(paul.Skip(1), (a, b) => (b - a).TotalSeconds).Take(5).Average();
    Assert.True((back.At - lastBefore).TotalSeconds / pace < 1.8);
  }

  [Fact]
  public void MaxCarriesOnWithTheSpareWithoutALapThatLooksMissed()
  {
    var swap = Only(DemoProblemKind.MergeSpare);
    var own = Demo.Crossings.Where(c => c.Tag == swap.Tag).Select(c => c.At).ToList();
    var spare = Demo.Crossings.Where(c => c.Tag == swap.OtherTag).ToList();

    Assert.All(own, t => Assert.True(t < swap.At));
    Assert.Equal(swap.At, spare.Min(c => c.At));
    Assert.All(spare, c => Assert.Equal(DemoReadKind.Unknown, c.Kind));
    Assert.DoesNotContain(Demo.Roster, r => r.Tag == swap.OtherTag);
    Assert.True((swap.At - own.Max()).TotalSeconds < 1.8 * 53);
  }

  [Fact]
  public void LeaRidesOnTheSpareFromTheStartAndHerOwnTransponderIsNeverRead()
  {
    var spare = Only(DemoProblemKind.IdentifySpare);
    var lea = Assert.Single(Demo.Roster, r => r.Number == spare.Number);

    Assert.DoesNotContain(Demo.Crossings, c => c.Tag == lea.Tag);
    Assert.DoesNotContain(Demo.Roster, r => r.Tag == spare.Tag);
    Assert.Equal(spare.At, Demo.Crossings.Where(c => c.Tag == spare.Tag).Min(c => c.At));
  }

  [Fact]
  public void TheMarshalsBikeIsNobodyOnTheList()
  {
    var marshal = Only(DemoProblemKind.StopCounting);
    var reads = Demo.Crossings.Where(c => c.Tag == marshal.Tag).ToList();

    Assert.DoesNotContain(Demo.Roster, r => r.Tag == marshal.Tag);
    Assert.All(reads, c => Assert.Equal(DemoReadKind.Stray, c.Kind));
    Assert.Equal(marshal.At, reads.Min(c => c.At));
  }

  [Fact]
  public void FelixIsReadTwiceThenRetiresBeforeTheFlag()
  {
    var twice = Only(DemoProblemKind.ReadTwice);
    var retires = Only(DemoProblemKind.Retires);
    var flag = Demo.Crossings.Min(c => c.At) + TimeSpan.FromMinutes(Demo.DurationMinutes);
    var last = Demo.Crossings.Where(c => c.Tag == retires.Tag).Max(c => c.At);

    Assert.Contains(Demo.Crossings, c => c.Tag == twice.Tag && c.At == twice.At && c.Kind == DemoReadKind.TooSoon);
    Assert.True(last < flag, "a rider who retires must stop before the flag, or the race waits for him");
    Assert.True(retires.At > last);
  }
}
