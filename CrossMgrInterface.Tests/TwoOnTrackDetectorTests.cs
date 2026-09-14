using Xunit;

namespace CrossMgrInterface.Tests;

public class TwoOnTrackDetectorTests
{
  private static readonly TimeSpan FieldPace = TimeSpan.FromSeconds(40);

  private static RiderBuilder Adler() => RiderBuilder.Team("MSC Adler",
    RiderBuilder.Member("11", "Anna Berger", "A01"),
    RiderBuilder.Member("14", "Ben Fischer", "A02"));

  private static IEnumerable<int> Flagged(RiderInfo team) =>
    team.Laps.Where(l => l.IsSuspectedOverlap).Select(l => l.LapNumber);

  [Fact]
  public void TheThresholdIsSixTenthsOfThePace()
  {
    // Pinned: a real handover carries the changeover and never comes near this.
    Assert.Equal(0.6, TeamEventSettings.Default.OverlapRatio);
  }

  [Fact]
  public void FlagsAShortLapByADifferentMember()
  {
    // Anna laps at 40s; Ben's read 15s after hers means he was already out.
    var team = Adler().LapBy(40, "A01").LapBy(40, "A01").LapBy(40, "A01").LapBy(40, "A01")
      .LapBy(15, "A02").Build();

    TwoOnTrackDetector.Analyze(team, FieldPace);

    Assert.Equal(new[] { 5 }, Flagged(team));
    Assert.True(team.HasAnomalies);
  }

  [Fact]
  public void LeavesARealHandoverAlone()
  {
    // Slightly longer than a lap: the changeover.
    var team = Adler().LapBy(40, "A01").LapBy(40, "A01").LapBy(40, "A01").LapBy(40, "A01")
      .LapBy(46, "A02").Build();

    TwoOnTrackDetector.Analyze(team, FieldPace);

    Assert.Empty(Flagged(team));
  }

  [Fact]
  public void AShortLapByTheSameMemberIsNotTwoOnTrack()
  {
    var team = Adler().LapBy(40, "A01").LapBy(40, "A01").LapBy(40, "A01").LapBy(15, "A01").Build();

    TwoOnTrackDetector.Analyze(team, FieldPace);

    Assert.Empty(Flagged(team));
  }

  [Fact]
  public void ATeamOnOneSharedTransponderIsNeverFlagged()
  {
    var team = RiderBuilder.Team("RC Falke",
        RiderBuilder.Member("21", "Carla Hoff", "B00"),
        RiderBuilder.Member("22", "David Kern", "B00"))
      .LapBy(40, "B00").LapBy(40, "B00").LapBy(40, "B00").LapBy(15, "B00").Build();

    TwoOnTrackDetector.Analyze(team, FieldPace);

    Assert.Empty(Flagged(team));
  }

  [Fact]
  public void AMemberWithTheirOwnTransponderIsJudgedInAPartlySharedTeam()
  {
    var team = RiderBuilder.Team("RC Falke",
        RiderBuilder.Member("21", "Carla Hoff", "B00"),
        RiderBuilder.Member("22", "David Kern", "B00"),
        RiderBuilder.Member("23", "Eva Lorenz", "B03"))
      .LapBy(40, "B00").LapBy(40, "B00").LapBy(40, "B00").LapBy(15, "B03").Build();

    TwoOnTrackDetector.Analyze(team, FieldPace);

    Assert.Equal(new[] { 4 }, Flagged(team));
  }

  [Fact]
  public void ALapWithNoRecordedRiderIsNotJudged()
  {
    var team = Adler().LapBy(40, "A01").LapBy(40, "A01").LapBy(40, "A01").LapBy(15, null).Build();

    TwoOnTrackDetector.Analyze(team, FieldPace);

    Assert.Empty(Flagged(team));
  }

  [Fact]
  public void AKeptLapIsNotFlaggedAgain()
  {
    var team = Adler().LapBy(40, "A01").LapBy(40, "A01").LapBy(40, "A01").LapBy(15, "A02").Build();
    TwoOnTrackDetector.Analyze(team, FieldPace);

    team.Laps[3].IsSuspectedOverlap = false;
    team.Laps[3].OverlapDismissed = true;
    TwoOnTrackDetector.Analyze(team, FieldPace);

    Assert.Empty(Flagged(team));
  }

  [Fact]
  public void TheWarningClearsOnceTheLapThatWasNotRealIsDeleted()
  {
    // Anna's 4 laps, Ben's read at 175s, Anna's at 200s.
    var team = Adler().LapBy(40, "A01").LapBy(40, "A01").LapBy(40, "A01").LapBy(40, "A01")
      .LapBy(15, "A02").LapBy(25, "A01").Build();
    var field = new Dictionary<string, RiderInfo> { [team.TagID] = team };
    var service = new RaceCorrectionService(field, new object(), () => RiderBuilder.RaceStart, _ => { });

    TwoOnTrackDetector.Analyze(team, FieldPace);
    Assert.Equal(new[] { 5 }, Flagged(team));

    Assert.True(service.DeleteLap(team.TagID, 5, team.Revision).Ok);
    TwoOnTrackDetector.Analyze(team, FieldPace);

    Assert.Empty(Flagged(team));
  }

  [Fact]
  public void UsesTheFieldsPaceBeforeTheTeamHasOne()
  {
    var team = Adler().LapBy(40, "A01").LapBy(15, "A02").Build();

    TwoOnTrackDetector.Analyze(team, FieldPace);

    Assert.Equal(new[] { 2 }, Flagged(team));
  }

  [Fact]
  public void JudgesNothingWithoutAnyPace()
  {
    var team = Adler().LapBy(40, "A01").LapBy(15, "A02").Build();

    TwoOnTrackDetector.Analyze(team, fieldPace: null);

    Assert.Empty(Flagged(team));
  }

  [Fact]
  public void FlaggedLapsDoNotDragTheTeamsPaceDown()
  {
    // Both short laps are measured against 40s, not against a pace the first one lowered.
    var team = Adler().LapBy(40, "A01").LapBy(40, "A01").LapBy(40, "A01").LapBy(40, "A01")
      .LapBy(15, "A02").LapBy(20, "A01").LapBy(40, "A01").Build();

    TwoOnTrackDetector.Analyze(team, FieldPace);

    Assert.Equal(new[] { 5, 6 }, Flagged(team));
  }

  [Fact]
  public void HandoverLapsAreNotPartOfTheTeamsPace()
  {
    // Laps 2, 3, 5 and 6 are 40s. Lap 4 is a 100s handover; counted in the pace it
    // would lift the threshold above Anna's 30s return and flag it.
    var team = Adler().LapBy(40, "A01").LapBy(40, "A01").LapBy(40, "A01")
      .LapBy(100, "A02").LapBy(40, "A02").LapBy(40, "A02")
      .LapBy(30, "A01").Build();

    TwoOnTrackDetector.Analyze(team, FieldPace);

    Assert.Empty(Flagged(team));
  }

  [Fact]
  public void ASoloRiderIsNeverJudged()
  {
    var rider = RiderBuilder.Rider("A01").Lap(40).Lap(40).Lap(15).Build();
    rider.Laps.ForEach(l => l.CrossedBy = "A01");

    TwoOnTrackDetector.Analyze(rider, FieldPace);

    Assert.Empty(Flagged(rider));
  }
}
