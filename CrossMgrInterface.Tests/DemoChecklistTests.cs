using Xunit;

namespace CrossMgrInterface.Tests;

public class DemoChecklistTests
{
  private static readonly DateTime Start = RiderBuilder.RaceStart;
  private static readonly TimeSpan Pace = TimeSpan.FromSeconds(40);

  private static DemoRaceView Race(double secondsIn, IEnumerable<RiderInfo>? riders = null,
    IEnumerable<string>? ignored = null, IDictionary<string, string>? aliases = null,
    IEnumerable<RejectedRead>? rejected = null) =>
    new(Start, Start.AddSeconds(secondsIn),
      (riders ?? Array.Empty<RiderInfo>()).ToDictionary(r => r.TagID),
      (ignored ?? Array.Empty<string>()).ToHashSet(),
      new Dictionary<string, string>(aliases ?? new Dictionary<string, string>()),
      (rejected ?? Array.Empty<RejectedRead>()).ToList());

  private static DemoProblem Problem(DemoProblemKind kind, double at) =>
    new(kind, TimeSpan.FromSeconds(at), "What went wrong", "What to do about it");

  private static RaceCorrectionService ServiceFor(params RiderInfo[] riders) =>
    new(riders.ToDictionary(r => r.TagID), new object(), () => Start, _ => { });

  private static DemoProblemState StateOf(DemoProblem problem, DemoRaceView race) =>
    DemoChecklist.StateOf(problem, race);

  // ---- When ----------------------------------------------------------------

  [Fact]
  public void NothingIsDueBeforeItHappensOrBeforeTheReaderHasStarted()
  {
    var problem = Problem(DemoProblemKind.StopCounting, 95) with { Tag = "M" };

    Assert.Equal(DemoProblemState.Later, StateOf(problem, Race(90)));
    Assert.Equal(DemoProblemState.Later, StateOf(problem, Race(200) with { ReaderStart = null }));
    Assert.Equal(DemoProblemState.ToFix, StateOf(problem, Race(95)));
  }

  // ---- Each kind of problem, put right ---------------------------------------

  [Fact]
  public void ASpareIsPutRightOnceItIsIdentifiedAsTheRider()
  {
    var problem = Problem(DemoProblemKind.IdentifySpare, 5) with { Tag = "SPARE", Number = "57" };
    var spare = RiderBuilder.Rider("SPARE", number: "").Laps(2, 55).Build();

    Assert.Equal(DemoProblemState.ToFix, StateOf(problem, Race(120, new[] { spare })));

    spare.RiderNumber = "57";
    Assert.Equal(DemoProblemState.Done, StateOf(problem, Race(120, new[] { spare })));
  }

  [Fact]
  public void AMarshalsBikeIsPutRightOnceItIsNoLongerCounted()
  {
    var problem = Problem(DemoProblemKind.StopCounting, 95) with { Tag = "M" };

    Assert.Equal(DemoProblemState.Done, StateOf(problem, Race(100, ignored: new[] { "M" })));
  }

  [Fact]
  public void AMissedReadIsPutRightBySplittingItButNotByKeepingIt()
  {
    // Three 40s laps, then 80s: the fourth read, at 160s, was missed.
    var problem = Problem(DemoProblemKind.SplitMissedRead, 200) with
    {
      Tag = "A", Laps = 2, Since = TimeSpan.FromSeconds(120)
    };

    var kept = RiderBuilder.Rider("A").Laps(3, 40).Lap(80).Build();
    LapAnomalyDetector.Analyze(kept, Pace);
    Assert.Equal(DemoProblemState.ToFix, StateOf(problem, Race(210, new[] { kept })));

    ServiceFor(kept).DismissSplitSuggestion("A", 4);
    Assert.Equal(DemoProblemState.ToFix, StateOf(problem, Race(210, new[] { kept })));

    var split = RiderBuilder.Rider("A").Laps(3, 40).Lap(80).Build();
    LapAnomalyDetector.Analyze(split, Pace);
    ServiceFor(split).SplitLap("A", 4, 2, split.Revision);
    LapAnomalyDetector.Analyze(split, Pace);
    Assert.Equal(DemoProblemState.Done, StateOf(problem, Race(210, new[] { split })));
  }

  [Fact]
  public void TwoRidersOnTrackIsPutRightByDeletingTheReadThatWasNotALap()
  {
    var team = RiderBuilder.Team("MSC Adler",
        RiderBuilder.Member("11", "Anna Berger", "A01"),
        RiderBuilder.Member("12", "Ben Fischer", "A02"))
      .LapBy(40, "A01").LapBy(40, "A01").LapBy(40, "A01").LapBy(40, "A01").LapBy(15, "A02").LapBy(25, "A01")
      .Build();
    var problem = Problem(DemoProblemKind.DeleteSecondRider, 175) with { Tag = team.TagID, OtherTag = "A02" };

    Assert.Equal(DemoProblemState.ToFix, StateOf(problem, Race(200, new[] { team })));

    ServiceFor(team).DeleteLap(team.TagID, 5, team.Revision);
    Assert.Equal(DemoProblemState.Done, StateOf(problem, Race(200, new[] { team })));
  }

  [Fact]
  public void ASwappedTransponderIsPutRightOnceItsLapsAreMergedIntoTheRider()
  {
    var problem = Problem(DemoProblemKind.MergeSpare, 400) with { Tag = "MAX", OtherTag = "SPARE2" };
    var max = RiderBuilder.Rider("MAX").Laps(5, 53).Build();
    var spare = RiderBuilder.Rider("SPARE2", number: "").Laps(2, 53).Build();

    Assert.Equal(DemoProblemState.ToFix, StateOf(problem, Race(500, new[] { max, spare })));
    Assert.Equal(DemoProblemState.Done,
      StateOf(problem, Race(500, new[] { max }, aliases: new Dictionary<string, string> { ["SPARE2"] = "MAX" })));
  }

  [Fact]
  public void MarkingARiderDnfIsMissedOnceHeIsAlreadyBack()
  {
    var problem = Problem(DemoProblemKind.MarkDnf, 445) with { Tag = "PAUL", Deadline = TimeSpan.FromSeconds(516) };
    var paul = RiderBuilder.Rider("PAUL").Laps(7, 56).Build();

    Assert.Equal(DemoProblemState.ToFix, StateOf(problem, Race(450, new[] { paul })));
    Assert.Equal(DemoProblemState.Missed, StateOf(problem, Race(520, new[] { paul })));

    paul.IsDNF = true;
    Assert.Equal(DemoProblemState.Done, StateOf(problem, Race(520, new[] { paul })));
  }

  [Fact]
  public void ARiderBackInTheRaceIsPutRightOnceTheReadsWhileDnfAreCounted()
  {
    var problem = Problem(DemoProblemKind.BackInTheRace, 516) with { Tag = "PAUL" };
    var paul = RiderBuilder.Rider("PAUL").Laps(7, 56).Build();
    paul.IsDNF = true;
    var read = new RejectedRead
    {
      TagID = "PAUL", CrossingTime = Start.AddSeconds(516), Reason = "the rider was marked DNF", WhileDnf = true
    };

    Assert.Equal(DemoProblemState.ToFix, StateOf(problem, Race(520, new[] { paul }, rejected: new[] { read })));

    paul.IsDNF = false;
    Assert.Equal(DemoProblemState.ToFix, StateOf(problem, Race(520, new[] { paul }, rejected: new[] { read })));

    ServiceFor(paul).RestoreRejectedRead("PAUL", read.CrossingTime);
    Assert.Equal(DemoProblemState.Done, StateOf(problem, Race(520, new[] { paul }, rejected: new[] { read })));
  }

  [Fact]
  public void AnOutageIsGoingOnUntilTheReaderComesBack()
  {
    var problem = Problem(DemoProblemKind.ReaderOutage, 560) with { Until = TimeSpan.FromSeconds(645) };

    Assert.Equal(DemoProblemState.ToFix, StateOf(problem, Race(600)));
    Assert.Equal(DemoProblemState.Done, StateOf(problem, Race(650)));
  }

  [Fact]
  public void TheOutageIsPutRightOnceEveryRiderItCutShortIsSplit()
  {
    // Both lose the read at 200s.
    var problem = Problem(DemoProblemKind.SplitAfterOutage, 240) with
    {
      Since = TimeSpan.FromSeconds(160), Until = TimeSpan.FromSeconds(240), Tags = new[] { "A", "B" }
    };
    var a = RiderBuilder.Rider("A").Laps(3, 50).Lap(100).Build();
    var b = RiderBuilder.Rider("B").Laps(3, 50).Lap(100).Build();
    LapAnomalyDetector.Analyze(a, Pace);
    LapAnomalyDetector.Analyze(b, Pace);

    var race = Race(260, new[] { a, b });
    Assert.Equal(DemoProblemState.ToFix, StateOf(problem, race));
    Assert.Equal(2, DemoChecklist.RidersLeft(problem, race));

    ServiceFor(a).SplitLap("A", 4, 2, a.Revision);
    LapAnomalyDetector.Analyze(a, Pace);
    Assert.Equal(1, DemoChecklist.RidersLeft(problem, Race(260, new[] { a, b })));

    ServiceFor(b).SplitLap("B", 4, 2, b.Revision);
    LapAnomalyDetector.Analyze(b, Pace);
    Assert.Equal(DemoProblemState.Done, StateOf(problem, Race(260, new[] { a, b })));
  }

  [Fact]
  public void ARetirementIsSeenOnceTheRiderIsDnf()
  {
    var problem = Problem(DemoProblemKind.Retires, 700) with { Tag = "F" };
    var felix = RiderBuilder.Rider("F").Laps(12, 54).Build();

    Assert.Equal(DemoProblemState.ToFix, StateOf(problem, Race(710, new[] { felix })));

    felix.IsDNF = true;
    Assert.Equal(DemoProblemState.Done, StateOf(problem, Race(710, new[] { felix })));
  }

  // ---- Over the whole demo ---------------------------------------------------

  [Fact]
  public void AProblemPutRightStaysTickedOff()
  {
    // A missed read split early on still shows CHECK again once a later outage
    // leaves a long lap; the problem that was fixed stays fixed.
    var problem = Problem(DemoProblemKind.StopCounting, 95) with { Tag = "M" };
    var tracker = new DemoChecklistTracker(new[] { problem });

    tracker.Update(Race(100, ignored: new[] { "M" }));
    tracker.Update(Race(101));

    Assert.Equal(DemoProblemState.Done, tracker.StateOf(problem));
  }

  [Fact]
  public void AnAnnouncementIsMadeOnceWhenItsProblemHappens()
  {
    var problem = Problem(DemoProblemKind.MarkDnf, 445) with { Tag = "PAUL", Announce = "Race control: #68 is out" };
    var tracker = new DemoChecklistTracker(new[] { problem });

    Assert.Empty(tracker.Update(Race(400)));
    Assert.Same(problem, Assert.Single(tracker.Update(Race(446))));
    Assert.Empty(tracker.Update(Race(447)));
  }

  [Fact]
  public void TheCountOfFixesLeavesOutWhatThereIsNothingToDoAbout()
  {
    var tracker = new DemoChecklistTracker(new[]
    {
      Problem(DemoProblemKind.ReadTwice, 10) with { Tag = "A" },
      Problem(DemoProblemKind.StopCounting, 10) with { Tag = "M" },
      Problem(DemoProblemKind.IdentifySpare, 500) with { Tag = "S", Number = "57" }
    });

    tracker.Update(Race(20));

    Assert.Equal(2, tracker.Fixable);
    Assert.Equal(1, tracker.LeftToFix);
    Assert.Equal(0, tracker.Fixed);
  }
}
