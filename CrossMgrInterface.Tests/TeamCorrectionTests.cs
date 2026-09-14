using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// Corrections on a team must not lose which member rode each lap: the
/// per-rider breakdown and the two-on-track warning are built on it.
/// </summary>
public class TeamCorrectionTests
{
  private static readonly DateTime Start = RiderBuilder.RaceStart;

  private static (RaceCorrectionService Service, Dictionary<string, RiderInfo> Field, RiderInfo Team) Adler()
  {
    // Anna 40s, Anna 80s (a missed read), Ben 45s.
    var team = RiderBuilder.Team("MSC Adler",
        RiderBuilder.Member("11", "Anna Berger", "A01"),
        RiderBuilder.Member("14", "Ben Fischer", "A02"))
      .LapBy(40, "A01").LapBy(80, "A01").LapBy(45, "A02")
      .Build();

    var field = new Dictionary<string, RiderInfo> { [team.TagID] = team };
    return (new RaceCorrectionService(field, new object(), () => Start, _ => { }), field, team);
  }

  [Fact]
  public void RecomputingKeepsWhoRodeEachLap()
  {
    var (_, _, team) = Adler();
    team.Laps.Reverse();

    RaceCorrectionService.RecomputeRider(team, Start);

    Assert.Equal(new[] { "A01", "A01", "A02" }, team.Laps.Select(l => l.CrossedBy));
  }

  [Fact]
  public void SplitLapsBelongToTheRiderWhoseReadEndedTheLongLap()
  {
    var (service, _, team) = Adler();

    Assert.True(service.SplitLap(team.TagID, 2, 2, team.Revision).Ok);

    Assert.Equal(new[] { "A01", "A01", "A01", "A02" }, team.Laps.Select(l => l.CrossedBy));
  }

  [Fact]
  public void ARestoredReadKeepsTheTransponderThatWasRead()
  {
    var (service, _, team) = Adler();

    Assert.True(service.RestoreRejectedRead(team.TagID, Start.AddSeconds(125), "A02").Ok);

    Assert.Equal("A02", team.Laps.Single(l => l.CrossingTime == Start.AddSeconds(125)).CrossedBy);
  }

  [Fact]
  public void ALapEnteredByHandHasNoRiderUnlessTheOperatorSaysWho()
  {
    var (service, _, team) = Adler();

    Assert.True(service.AddLap(team.TagID, Start.AddSeconds(90), team.Revision).Ok);
    Assert.True(service.AddLap(team.TagID, Start.AddSeconds(200), team.Revision, crossedBy: "A02").Ok);

    Assert.Null(team.Laps.Single(l => l.CrossingTime == Start.AddSeconds(90)).CrossedBy);
    Assert.Equal("A02", team.Laps.Single(l => l.CrossingTime == Start.AddSeconds(200)).CrossedBy);
  }

  [Fact]
  public void KeepingALapFlaggedTwoOnTrackStopsItBeingFlaggedAgainAndCanBeUndone()
  {
    var (service, field, team) = Adler();
    team.Laps[2].IsSuspectedOverlap = true;

    Assert.True(service.DismissOverlapWarning(team.TagID, 3).Ok);
    TwoOnTrackDetector.Analyze(team, TimeSpan.FromSeconds(200));

    Assert.False(team.Laps[2].IsSuspectedOverlap);
    Assert.True(team.Laps[2].OverlapDismissed);

    Assert.True(service.Undo().Ok);
    Assert.True(field[team.TagID].Laps[2].IsSuspectedOverlap);
    Assert.False(field[team.TagID].Laps[2].OverlapDismissed);
  }

  [Fact]
  public void UndoBringsBackTheMembersAndWhoRodeEachLap()
  {
    var (service, field, team) = Adler();
    var members = team.Members;

    Assert.True(service.DeleteLap(team.TagID, 3, team.Revision).Ok);
    Assert.True(service.Undo().Ok);

    var restored = field[team.TagID];
    Assert.Same(members, restored.Members);
    Assert.Equal(new[] { "A01", "A01", "A02" }, restored.Laps.Select(l => l.CrossedBy));
  }
}
