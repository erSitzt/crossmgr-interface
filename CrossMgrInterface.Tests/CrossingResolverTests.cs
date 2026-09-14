using Xunit;

namespace CrossMgrInterface.Tests;

public class CrossingResolverTests
{
  private static readonly Dictionary<string, string> NoAliases = new();

  private static TeamRoster Adler() => TeamRoster.Build(new[]
  {
    new RiderDataImporter.RiderImportData { TagID = "A01", RiderNumber = "11", FirstName = "Anna", Team = "MSC Adler" },
    new RiderDataImporter.RiderImportData { TagID = "A02", RiderNumber = "12", FirstName = "Ben", Team = "MSC Adler" }
  });

  private static bool Nothing(string _) => false;

  [Fact]
  public void AMembersTransponderScoresForItsTeam()
  {
    var (entry, crossedBy) = CrossingResolver.Resolve("A02", NoAliases, Adler(), Nothing);

    Assert.Equal(TeamRoster.KeyFor("MSC Adler"), entry);
    Assert.Equal("A02", crossedBy);
  }

  [Fact]
  public void AnUnknownTransponderIsItsOwnEntry()
  {
    Assert.Equal(("STRAY99", "STRAY99"), CrossingResolver.Resolve("STRAY99", NoAliases, Adler(), Nothing));
  }

  [Fact]
  public void WithoutTeamsEveryTransponderIsItsOwnEntry()
  {
    // An ordinary session: the team column holds club names and must not group anyone.
    Assert.Equal(("A01", "A01"), CrossingResolver.Resolve("A01", NoAliases, TeamRoster.Empty, Nothing));
  }

  [Fact]
  public void AnAliasIsFollowedAndStillRecordsWhichTransponderWasRead()
  {
    var aliases = new Dictionary<string, string> { ["SPARE"] = "10000001" };

    Assert.Equal(("10000001", "SPARE"), CrossingResolver.Resolve("SPARE", aliases, TeamRoster.Empty, Nothing));
  }

  [Fact]
  public void TheOperatorsAliasWinsOverTheTeamList()
  {
    var aliases = new Dictionary<string, string> { ["A01"] = "10000001" };

    Assert.Equal("10000001", CrossingResolver.Resolve("A01", aliases, Adler(), Nothing).EntryKey);
  }

  [Fact]
  public void AnAliasToATeamFromAnEarlierTeamEventIsIgnored()
  {
    // Aliases last all meeting. In the next, ordinary session there is no such team.
    var aliases = new Dictionary<string, string> { ["SPARE"] = TeamRoster.KeyFor("MSC Adler") };

    Assert.Equal(("SPARE", "SPARE"), CrossingResolver.Resolve("SPARE", aliases, TeamRoster.Empty, Nothing));
  }

  [Fact]
  public void AnAliasToATeamStillBeingScoredIsFollowed()
  {
    var key = TeamRoster.KeyFor("MSC Adler");
    var aliases = new Dictionary<string, string> { ["SPARE"] = key };

    Assert.Equal(key, CrossingResolver.Resolve("SPARE", aliases, TeamRoster.Empty, k => k == key).EntryKey);
    Assert.Equal(key, CrossingResolver.Resolve("SPARE", aliases, Adler(), Nothing).EntryKey);
  }
}

public class ReadDebounceTests
{
  private static readonly DateTime T0 = RiderBuilder.RaceStart;
  private static readonly TimeSpan MinimumLap = TimeSpan.FromSeconds(10);

  [Fact]
  public void AReadSoonAfterTheLastCrossingIsRejectedForEveryone()
  {
    var solo = ReadDebounce.Check(T0.AddSeconds(4), T0, null, MinimumLap, isTeam: false);
    var team = ReadDebounce.Check(T0.AddSeconds(4), T0, null, MinimumLap, isTeam: true);

    Assert.True(solo.Reject);
    Assert.True(team.Reject);
    // Formatted the way the app formats it, in the machine's own culture.
    Assert.Equal($"Only {4.0:F1}s after the previous read", solo.Reason);
  }

  [Fact]
  public void ATeamMemberWaitingNearTheLoopIsNotCountedAgainAndAgain()
  {
    // Anna crossed at 0. Ben is waiting near the loop and was last read 3s ago,
    // so this read - 12s after Anna's - is still Ben standing there.
    var verdict = ReadDebounce.Check(T0.AddSeconds(12), T0, T0.AddSeconds(9), MinimumLap, isTeam: true);

    Assert.True(verdict.Reject);
    Assert.Equal(TimeSpan.FromSeconds(3), verdict.Gap);
  }

  [Fact]
  public void ASoloRiderKeepsTheRuleTheyAlwaysHad()
  {
    var verdict = ReadDebounce.Check(T0.AddSeconds(12), T0, T0.AddSeconds(9), MinimumLap, isTeam: false);

    Assert.False(verdict.Reject);
  }

  [Fact]
  public void AnotherMembersRealCrossingCounts()
  {
    // Ben last crossed a whole lap ago; Anna crossed 40s ago.
    var verdict = ReadDebounce.Check(T0.AddSeconds(40), T0, T0.AddSeconds(-45), MinimumLap, isTeam: true);

    Assert.False(verdict.Reject);
    Assert.Equal(TimeSpan.FromSeconds(40), verdict.Gap);
  }
}
