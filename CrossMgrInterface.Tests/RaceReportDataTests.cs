using Xunit;

namespace CrossMgrInterface.Tests;

public class RaceReportDataTests
{
  private static RaceReportData Prepare(RaceRules rules, params RiderInfo[] riders) =>
    new RaceReportGenerator().PrepareReportData(
      riders.ToDictionary(r => r.TagID, r => r),
      RiderBuilder.RaceStart, RiderBuilder.RaceStart.AddMinutes(20), TimeSpan.FromMinutes(20),
      true, "Test", rules: rules);

  [Fact]
  public void ARiderMarkedDnsIsListedAsDnsAfterTheDnfRiders()
  {
    var winner = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(5, 40).Build();
    var dns = RiderBuilder.Rider("B", "2", "Ben Fischer").Dns().Build();
    var dnf = RiderBuilder.Rider("C", "3", "Carla Hoff").Laps(3, 40).Dnf().Build();

    var data = Prepare(new RaceRules(), dns, dnf, winner);

    Assert.Equal(new[] { "1", "DNF", "DNS" }, data.RiderResults.Select(r => r.Position));
    var marked = data.RiderResults[2];
    Assert.Equal("DNS", marked.Status);
    Assert.Null(marked.GapToLeader);
    Assert.Equal(0, marked.LapGapToLeader);
    Assert.Equal(1, data.RaceStatistics!.DNSRiders);
    Assert.Equal(1, data.RaceStatistics.FinishedRiders);
  }

  [Fact]
  public void ARaceSheetScoresARiderWhoTimedOutAsDnf()
  {
    var timedOut = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(6, 40).Dnf().Build();
    var finished = RiderBuilder.Rider("B", "2", "Ben Fischer").Laps(4, 40).Build();

    var data = Prepare(new RaceRules(), timedOut, finished);

    Assert.Equal(new[] { "B", "A" }, data.RiderResults.Select(r => r.TagID));
    Assert.Equal(new[] { "1", "DNF" }, data.RiderResults.Select(r => r.Position));
  }

  [Fact]
  public void APracticeSheetMarksNobodyDnf()
  {
    // Pulled in before the flag: the timeout marks the rider, but only as off track.
    var pulledIn = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(6, 40).Dnf().Build();
    var stillOut = RiderBuilder.Rider("B", "2", "Ben Fischer").Laps(4, 40).Build();

    var data = Prepare(new RaceRules { SessionType = SessionType.FreePractice }, stillOut, pulledIn);

    Assert.Equal(new[] { "A", "B" }, data.RiderResults.Select(r => r.TagID));
    Assert.Equal(new[] { "1", "2" }, data.RiderResults.Select(r => r.Position));
    Assert.All(data.RiderResults, r => Assert.Equal("Finished", r.Status));
    Assert.Equal(0, data.RaceStatistics!.DNFRiders);
    Assert.Equal(2, data.RaceStatistics.FinishedRiders);
  }

  [Fact]
  public void APracticeSheetStillMarksADns()
  {
    var rode = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(6, 40).Build();
    var dns = RiderBuilder.Rider("B", "2", "Ben Fischer").Dns().Build();

    var data = Prepare(new RaceRules { SessionType = SessionType.FreePractice }, dns, rode);

    Assert.Equal(new[] { "1", "DNS" }, data.RiderResults.Select(r => r.Position));
  }
}
