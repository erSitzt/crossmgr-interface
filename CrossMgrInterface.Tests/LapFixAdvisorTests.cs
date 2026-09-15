using Xunit;

namespace CrossMgrInterface.Tests;

public class LapFixAdvisorTests
{
  private static readonly TimeSpan Pace = TimeSpan.FromSeconds(40);

  private static RaceCorrectionService ServiceFor(params RiderInfo[] riders) =>
    new(riders.ToDictionary(r => r.TagID), new object(), () => RiderBuilder.RaceStart, _ => { });

  private static RiderBuilder Adler() => RiderBuilder.Team("MSC Adler",
    RiderBuilder.Member("11", "Anna Berger", "A01"),
    RiderBuilder.Member("14", "Ben Fischer", "A02"));

  /// <summary>Five 40s laps, then one of 80s: a read missed on the sixth pass.</summary>
  private static RiderInfo MissedRead(string tag = "A", double pace = 40)
  {
    var rider = RiderBuilder.Rider(tag, "7", "Lukas Brandt").Laps(5, pace).Lap(pace * 2).Build();
    LapAnomalyDetector.Analyze(rider, Pace);
    return rider;
  }

  /// <summary>Anna laps at 40s; Ben is read 15s after her, then Anna comes round 25s later.</summary>
  private static RiderInfo TwoOnTrack()
  {
    var team = Adler().LapBy(40, "A01").LapBy(40, "A01").LapBy(40, "A01").LapBy(40, "A01")
      .LapBy(15, "A02").LapBy(25, "A01").Build();
    TwoOnTrackDetector.Analyze(team, Pace);
    return team;
  }

  // ---- What is offered ---------------------------------------------------------

  [Fact]
  public void AMissedReadIsOfferedAsASplitIntoTheLapsItLooksLike()
  {
    var fix = Assert.Single(LapFixAdvisor.For(MissedRead()));

    Assert.Equal(LapFixKind.SplitMissedRead, fix.Kind);
    Assert.Equal(6, fix.LapNumber);
    Assert.Equal(2, fix.SplitCount);
    Assert.Equal("Split lap 6 into 2 laps", fix.FixText);
    Assert.Equal("Keep lap 6 as it is", fix.KeepText);
  }

  [Fact]
  public void TwoRidersOnTrackIsOfferedAsDeletingTheLapThatIsNotReal()
  {
    var fix = Assert.Single(LapFixAdvisor.For(TwoOnTrack()));

    Assert.Equal(LapFixKind.DeleteSecondRiderRead, fix.Kind);
    Assert.Equal(5, fix.LapNumber);
    Assert.Equal("Delete lap 5", fix.FixText);

    // The operator needs to see who was read after whom to believe it.
    Assert.Contains("Ben Fischer", fix.Problem);
    Assert.Contains("Anna Berger", fix.Problem);
  }

  [Fact]
  public void TwoRidersOnTrackComesBeforeAMissedRead()
  {
    // The same team later misses a read on lap 10 as well.
    var team = Adler().LapBy(40, "A01").LapBy(40, "A01").LapBy(40, "A01").LapBy(40, "A01")
      .LapBy(15, "A02").LapBy(25, "A01").LapBy(40, "A01").LapBy(40, "A01").LapBy(40, "A01")
      .LapBy(80, "A01").Build();
    TwoOnTrackDetector.Analyze(team, Pace);
    LapAnomalyDetector.Analyze(team, Pace);

    var fixes = LapFixAdvisor.For(team);

    Assert.Equal(new[] { LapFixKind.DeleteSecondRiderRead, LapFixKind.SplitMissedRead }, fixes.Select(f => f.Kind));
  }

  [Fact]
  public void LapsWithNothingWrongOfferNothing()
  {
    Assert.Empty(LapFixAdvisor.For(RiderBuilder.Rider("A").Laps(6, 40).Build()));
  }

  // ---- Pressing the button -----------------------------------------------------

  [Fact]
  public void PressingTheSplitFixesTheLapsAndTheWarningGoes()
  {
    var rider = MissedRead();

    var result = LapFixAdvisor.For(rider)[0].Apply(ServiceFor(rider), rider.TagID, rider.Revision);
    LapAnomalyDetector.Analyze(rider, Pace);

    Assert.True(result.Ok, result.Error);
    Assert.Equal(7, rider.Laps.Count);
    Assert.All(rider.Laps, l => Assert.Equal(40, l.LapTime!.Value.TotalSeconds, 3));
    Assert.Empty(LapFixAdvisor.For(rider));
  }

  [Fact]
  public void PressingTheDeleteFixesTheLapsAndTheWarningGoes()
  {
    var team = TwoOnTrack();

    var result = LapFixAdvisor.For(team)[0].Apply(ServiceFor(team), team.TagID, team.Revision);
    TwoOnTrackDetector.Analyze(team, Pace);

    Assert.True(result.Ok, result.Error);
    Assert.Equal(5, team.Laps.Count);
    Assert.Equal(40, team.Laps[4].LapTime!.Value.TotalSeconds, 3);
    Assert.Empty(LapFixAdvisor.For(team));
  }

  [Fact]
  public void KeepingAMissedReadLapLeavesTheLapsAloneAndTheWarningStaysGone()
  {
    var rider = MissedRead();

    var result = LapFixAdvisor.For(rider)[0].Keep(ServiceFor(rider), rider.TagID, rider.Revision);
    LapAnomalyDetector.Analyze(rider, Pace);

    Assert.True(result.Ok, result.Error);
    Assert.Equal(6, rider.Laps.Count);
    Assert.Empty(LapFixAdvisor.For(rider));
  }

  [Fact]
  public void KeepingATwoOnTrackLapLeavesTheLapsAloneAndTheWarningStaysGone()
  {
    var team = TwoOnTrack();

    var result = LapFixAdvisor.For(team)[0].Keep(ServiceFor(team), team.TagID, team.Revision);
    TwoOnTrackDetector.Analyze(team, Pace);

    Assert.True(result.Ok, result.Error);
    Assert.Equal(6, team.Laps.Count);
    Assert.Empty(LapFixAdvisor.For(team));
  }

  [Fact]
  public void AFixForLapsThatHaveChangedSinceItWasShownIsRefused()
  {
    // The rider came round while the operator was reading the suggestion.
    var rider = MissedRead();
    var shown = rider.Revision;
    var fix = LapFixAdvisor.For(rider)[0];

    rider.AddReadLap(new RiderLap { TagID = rider.TagID, CrossingTime = rider.LastCrossing.AddSeconds(40) });

    var service = ServiceFor(rider);
    Assert.False(fix.Apply(service, rider.TagID, shown).Ok);
    Assert.False(fix.Keep(service, rider.TagID, shown).Ok);
    Assert.Equal(7, rider.Laps.Count);
  }

  // ---- Which rider F2 opens ----------------------------------------------------

  [Fact]
  public void FixLapsOpensTwoRidersOnTrackBeforeAMissedReadWhateverTheOrder()
  {
    var leader = MissedRead();
    var team = TwoOnTrack();

    Assert.Same(team, LapFixAdvisor.MostUrgent(new[] { leader, team }));
  }

  [Fact]
  public void FixLapsOpensTheHighestPlacedOfRidersWithTheSameProblem()
  {
    var first = MissedRead("A");
    var second = MissedRead("B", pace: 41);

    Assert.Same(first, LapFixAdvisor.MostUrgent(new[] { first, second }));
    Assert.Same(second, LapFixAdvisor.MostUrgent(new[] { second, first }));
  }

  [Fact]
  public void FixLapsFindsNobodyWhenNothingIsWrong()
  {
    var kept = MissedRead();
    kept.Laps[5].SuggestionDismissed = true;

    Assert.Null(LapFixAdvisor.MostUrgent(new[] { RiderBuilder.Rider("B").Laps(6, 40).Build(), kept }));
  }
}

/// <summary>Corrections made while riders are still crossing the line.</summary>
public class CorrectionsWhileTheRaceRunsTests
{
  private static (RaceCorrectionService Service, RiderInfo Rider) Field(RiderInfo rider) =>
    (new RaceCorrectionService(new Dictionary<string, RiderInfo> { [rider.TagID] = rider }, new object(),
      () => RiderBuilder.RaceStart, _ => { }), rider);

  private static void ComesRound(RiderInfo rider, double seconds) =>
    rider.AddReadLap(new RiderLap { TagID = rider.TagID, CrossingTime = rider.LastCrossing.AddSeconds(seconds) });

  private static double[] LapTimes(RiderInfo rider) =>
    rider.Laps.Select(l => Math.Round(l.LapTime!.Value.TotalSeconds, 3)).ToArray();

  [Fact]
  public void ALiveReadMovesTheRevisionOnLikeACorrectionDoes()
  {
    var rider = RiderBuilder.Rider("A").Laps(3, 40).Build();
    var before = rider.Revision;

    ComesRound(rider, 40);

    Assert.Equal(before + 1, rider.Revision);
    Assert.Equal(RiderBuilder.RaceStart.AddSeconds(160), rider.LastCrossing);
  }

  [Fact]
  public void UndoingACorrectionKeepsTheLapsReadSinceIt()
  {
    // Split the long lap, the rider comes round again, then the split is undone:
    // the lap they have just ridden must survive the undo.
    var (service, rider) = Field(RiderBuilder.Rider("A").Laps(3, 40).Lap(80).Build());
    service.SplitLap("A", 4, 2, rider.Revision);
    ComesRound(rider, 40);

    Assert.True(service.Undo().Ok);

    Assert.Equal(new[] { 40.0, 40, 40, 80, 40 }, LapTimes(rider));
  }

  [Fact]
  public void RedoingACorrectionKeepsTheLapsReadSinceTheUndo()
  {
    var (service, rider) = Field(RiderBuilder.Rider("A").Laps(3, 40).Lap(80).Build());
    service.SplitLap("A", 4, 2, rider.Revision);
    service.Undo();
    ComesRound(rider, 40);

    Assert.True(service.Redo().Ok);

    Assert.Equal(new[] { 40.0, 40, 40, 40, 40, 40 }, LapTimes(rider));
  }

  [Fact]
  public void AnUndoNeverTakesTheRevisionBackwards()
  {
    // A Fix laps window compares revisions to know its list is out of date. Going
    // back to an old number would make a changed list look unchanged.
    var (service, rider) = Field(RiderBuilder.Rider("A").Laps(3, 40).Lap(80).Build());
    service.SplitLap("A", 4, 2, rider.Revision);
    var afterSplit = rider.Revision;

    service.Undo();

    Assert.True(rider.Revision > afterSplit);
  }

  [Fact]
  public void AReadCountedByHandIsNotCountedAgainOnceThatIsUndone()
  {
    var (service, rider) = Field(RiderBuilder.Rider("A").Laps(3, 40).Build());
    var read = new RejectedRead
    {
      TagID = "A",
      CrossingTime = rider.LastCrossing.AddSeconds(4),
      GapToPrevious = TimeSpan.FromSeconds(4),
      Reason = "Only 4.0s after the previous read"
    };

    service.RestoreRejectedRead("A", read.CrossingTime);
    Assert.True(read.IsCountedIn(rider));

    service.Undo();
    Assert.False(read.IsCountedIn(rider));
  }
}
