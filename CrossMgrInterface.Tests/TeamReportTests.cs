using Xunit;

namespace CrossMgrInterface.Tests;

public class TeamReportTests
{
  private static RiderResult Solo(string position) => new() { Position = position, RiderName = "Greta Lang" };

  private static RiderResult Team(string position, RiderInfo team) => new()
  {
    Position = position,
    RiderName = team.FirstName,
    IsTeam = true,
    MemberBreakdown = TeamMemberStats.For(team)
  };

  [Fact]
  public void ASheetWithoutTeamsIsJustItsResults()
  {
    var lines = RaceReportGenerator.BuildPrintLines(new[] { Solo("1"), Solo("2") });

    Assert.All(lines, l => Assert.Equal(ReportLineKind.Result, l.Kind));
    Assert.Equal(2, lines.Count);
  }

  [Fact]
  public void EachTeamsRidersFollowTheResultsInFinishingOrder()
  {
    var adler = RiderBuilder.Team("MSC Adler",
        RiderBuilder.Member("11", "Anna Berger", "A01"),
        RiderBuilder.Member("14", "Ben Fischer", "A02"))
      .LapBy(40, "A01").LapBy(40, "A01").LapBy(46, "A02").Build();
    var falke = RiderBuilder.Team("RC Falke",
        RiderBuilder.Member("21", "Carla Hoff", "B00"),
        RiderBuilder.Member("22", "David Kern", "B00"))
      .LapBy(40, "B00").Build();

    var lines = RaceReportGenerator.BuildPrintLines(new[] { Team("1", adler), Solo("2"), Team("3", falke) });

    Assert.Equal(new[]
    {
      ReportLineKind.Result, ReportLineKind.Result, ReportLineKind.Result,
      ReportLineKind.TeamsHeading,
      ReportLineKind.TeamHeading, ReportLineKind.Member, ReportLineKind.Member,
      ReportLineKind.TeamHeading, ReportLineKind.Member
    }, lines.Select(l => l.Kind));

    Assert.Equal("MSC Adler", lines[4].Result!.RiderName);
    Assert.Equal("#14 Ben Fischer", lines[6].Member!.Label);
    Assert.True(lines[8].Member!.SharedTransponder);
  }
}
