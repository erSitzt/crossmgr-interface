using Xunit;

namespace CrossMgrInterface.Tests;

public class TeamMemberStatsTests
{
  private static RiderBuilder Adler() => RiderBuilder.Team("MSC Adler",
    RiderBuilder.Member("11", "Anna Berger", "A01"),
    RiderBuilder.Member("14", "Ben Fischer", "A02"));

  [Fact]
  public void EachRiderIsCreditedWithTheLapsTheirTransponderEnded()
  {
    var team = Adler().LapBy(40, "A01").LapBy(40, "A01").LapBy(40, "A01")
      .LapBy(46, "A02").LapBy(42, "A02").LapBy(42, "A02").Build();

    var lines = TeamMemberStats.For(team);

    Assert.Equal(2, lines.Count);
    Assert.Equal(("#11 Anna Berger", 3), (lines[0].Label, lines[0].LapsRidden));
    Assert.Equal(("#14 Ben Fischer", 3), (lines[1].Label, lines[1].LapsRidden));
  }

  [Fact]
  public void TimesLeaveOutTheFirstLapAndTheHandover()
  {
    // Anna's first lap runs from the start; Ben's 46s holds the changeover.
    var team = Adler().LapBy(30, "A01").LapBy(40, "A01").LapBy(41, "A01")
      .LapBy(46, "A02").LapBy(42, "A02").LapBy(44, "A02").Build();

    var lines = TeamMemberStats.For(team);

    Assert.Equal(2, lines[0].TimedLaps);
    Assert.Equal(TimeSpan.FromSeconds(40), lines[0].BestLap);
    Assert.Equal(TimeSpan.FromSeconds(40.5), lines[0].AverageLap);

    Assert.Equal(2, lines[1].TimedLaps);
    Assert.Equal(TimeSpan.FromSeconds(42), lines[1].BestLap);
    Assert.Equal(TimeSpan.FromSeconds(43), lines[1].AverageLap);
  }

  [Fact]
  public void ATeamOnOneTransponderGetsOneLineWithLapsButNoTimes()
  {
    var team = RiderBuilder.Team("RC Falke",
        RiderBuilder.Member("21", "Carla Hoff", "B00"),
        RiderBuilder.Member("22", "David Kern", "B00"))
      .LapBy(40, "B00").LapBy(40, "B00").LapBy(46, "B00").LapBy(40, "B00").Build();

    var line = Assert.Single(TeamMemberStats.For(team));

    Assert.True(line.SharedTransponder);
    Assert.Equal("#21 Carla Hoff / #22 David Kern", line.Label);
    Assert.Equal(4, line.LapsRidden);
    Assert.Null(line.BestLap);
  }

  [Fact]
  public void APartlySharedTeamIsTimedOnlyWhereRidersCanBeToldApart()
  {
    var team = RiderBuilder.Team("RC Falke",
        RiderBuilder.Member("21", "Carla Hoff", "B00"),
        RiderBuilder.Member("22", "David Kern", "B00"),
        RiderBuilder.Member("23", "Eva Lorenz", "B03"))
      .LapBy(40, "B00").LapBy(40, "B00").LapBy(45, "B03").LapBy(41, "B03").Build();

    var lines = TeamMemberStats.For(team);

    Assert.Equal((2, true, (TimeSpan?)null), (lines[0].LapsRidden, lines[0].SharedTransponder, lines[0].BestLap));
    Assert.Equal((2, TimeSpan.FromSeconds(41)), (lines[1].LapsRidden, lines[1].BestLap!.Value));
  }

  [Fact]
  public void ASpareTransponderCountsForItsRider()
  {
    var team = RiderBuilder.Team("MSC Adler",
        RiderBuilder.Member("11", "Anna Berger", "A01", "SPARE"),
        RiderBuilder.Member("14", "Ben Fischer", "A02"))
      .LapBy(40, "A01").LapBy(40, "SPARE").LapBy(38, "SPARE").Build();

    var anna = TeamMemberStats.For(team)[0];

    Assert.Equal(3, anna.LapsRidden);
    Assert.Equal(TimeSpan.FromSeconds(38), anna.BestLap);
    Assert.Equal(TimeSpan.FromSeconds(39), anna.AverageLap);
  }

  [Fact]
  public void LapsWithNoRecordedRiderAreCountedApart()
  {
    // The lap after an unknown one cannot be timed either: nobody knows whether
    // it was a handover.
    var team = Adler().LapBy(40, "A01").LapBy(40, null).LapBy(40, "A01").Build();

    var lines = TeamMemberStats.For(team);

    Assert.Equal((2, 0), (lines[0].LapsRidden, lines[0].TimedLaps));
    Assert.True(lines[2].IsUnattributed);
    Assert.Equal(1, lines[2].LapsRidden);
  }

  [Fact]
  public void AFlaggedLapIsNotARidersTime()
  {
    var team = Adler().LapBy(40, "A01").LapBy(40, "A01").LapBy(80, "A01").Build();
    team.Laps[2].IsSuggestedForSplit = true;

    var anna = TeamMemberStats.For(team)[0];

    Assert.Equal(3, anna.LapsRidden);
    Assert.Equal(TimeSpan.FromSeconds(40), anna.AverageLap);
  }

  [Fact]
  public void ASoloRiderHasNoBreakdown()
  {
    Assert.Empty(TeamMemberStats.For(RiderBuilder.Rider("S01").Lap(40).Build()));
  }
}
