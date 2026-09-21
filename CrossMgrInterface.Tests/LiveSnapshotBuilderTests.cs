using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// What the live timing website is told about a running race - and, as with
/// the results, what it is not.
/// </summary>
public class LiveSnapshotBuilderTests
{
  private static readonly DateTime Start = RiderBuilder.RaceStart;

  private static LiveSnapshot Build(RaceDayState state = RaceDayState.Running, params RiderInfo[] riders) =>
    LiveSnapshotBuilder.Build(new LiveInputs
    {
      PublicId = "b3f1c0de0000000000000000000000ff",
      Title = "Moto 1",
      State = state,
      StartedAt = Start,
      Duration = TimeSpan.FromMinutes(20),
      Remaining = TimeSpan.FromMinutes(12),
      Now = Start.AddMinutes(8),
      Seq = 7,
      Riders = riders.Select(r => LiveCapture.Of(r)).ToList(),
      ClientVersion = "test"
    });

  [Fact]
  public void TheOrderIsTheLeaderboards()
  {
    var leader = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(5, 40).Build();
    var second = RiderBuilder.Rider("B", "2", "Ben Fischer").Laps(5, 42).Build();
    var lapped = RiderBuilder.Rider("C", "3", "Carla Hoff").Laps(4, 40).Build();
    var dnf = RiderBuilder.Rider("D", "4", "David Kern").Laps(5, 30).Dnf().Build();
    var dns = RiderBuilder.Rider("E", "5", "Eva Lang").Dns().Build();

    var entries = Build(RaceDayState.Running, dns, dnf, lapped, second, leader).Entries;

    Assert.Equal(new[] { "1", "2", "3", "4", "5" }, entries.Select(e => e.Number));
    Assert.Equal(new[] { 1, 2, 3, 4, 5 }, entries.Select(e => e.Rank));
    Assert.Equal(new[] { "racing", "racing", "racing", "dnf", "dns" }, entries.Select(e => e.Status));
  }

  [Fact]
  public void AGapIsTimeOnTheSameLapAndLapsOtherwise()
  {
    var leader = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(5, 40).Build();
    var second = RiderBuilder.Rider("B", "2", "Ben Fischer").Laps(5, 42).Build();
    var lapped = RiderBuilder.Rider("C", "3", "Carla Hoff").Laps(4, 40).Build();

    var entries = Build(RaceDayState.Running, leader, second, lapped).Entries;

    Assert.Null(entries[0].GapToLeaderMs);
    Assert.Equal(10_000, entries[1].GapToLeaderMs);
    Assert.Equal(0, entries[1].LapsDown);
    Assert.Null(entries[2].GapToLeaderMs);
    Assert.Equal(1, entries[2].LapsDown);
  }

  [Fact]
  public void TheFeedIsTheNewestCrossingsAcrossTheField()
  {
    var a = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(3, 40).Build();   // crosses at 40, 80, 120
    var b = RiderBuilder.Rider("B", "2", "Ben Fischer").Laps(2, 50).Build();   // crosses at 50, 100

    var recent = Build(RaceDayState.Running, a, b).Recent;

    // 120 A, 100 B, 80 A, 50 B, 40 A.
    Assert.Equal(new[] { "1", "2", "1", "2", "1" }, recent.Select(c => c.Number));
    Assert.Equal(120_000, recent[0].AtMs);
    Assert.Equal(3, recent[0].Lap);
  }

  [Fact]
  public void NoMoreThanTwentyCrossingsAreSent()
  {
    var field = Enumerable.Range(1, 10)
      .Select(i => RiderBuilder.Rider($"T{i}", i.ToString(), $"Rider {i}").Laps(8, 40 + i).Build())
      .ToArray();

    Assert.Equal(LiveSnapshotBuilder.RecentCrossings, Build(RaceDayState.Running, field).Recent.Count);
  }

  [Fact]
  public void TheFirstCrossingIsNeverALapTime()
  {
    // The builder leaves a time on lap 1 - the run from the start - which the
    // live feed must not pass off as a lap. Seen first on a real demo, where the
    // leaderboard's "last lap" was the gap to the leader.
    var rider = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(1, 40).Build();

    var snapshot = Build(RaceDayState.Running, rider);

    Assert.Null(snapshot.Recent.Single().LapMs);
    Assert.Null(snapshot.Entries.Single().LastLapMs);
  }

  [Fact]
  public void FromTheSecondCrossingOnALapHasATime()
  {
    var rider = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(2, 40).Build();

    var snapshot = Build(RaceDayState.Running, rider);

    Assert.Equal(40_000, snapshot.Recent.First().LapMs);
    Assert.Equal(40_000, snapshot.Entries.Single().LastLapMs);
  }

  [Fact]
  public void ATeamIsNamedAsATeamWithItsRiders()
  {
    var adler = RiderBuilder.Team("MSC Adler",
        RiderBuilder.Member("11", "Anna Berger", "A01"),
        RiderBuilder.Member("14", "Ben Fischer", "A02"))
      .LapBy(40, "A01").LapBy(40, "A02").Build();

    var entry = Build(RaceDayState.Running, adler).Entries.Single();

    Assert.Equal("MSC Adler", entry.Name);
    Assert.Equal(new[] { "#11 Anna Berger", "#14 Ben Fischer" }, entry.Members);
  }

  [Theory]
  [InlineData(RaceDayState.WaitingForFirstRider, "waiting")]
  [InlineData(RaceDayState.ReadyToStart, "waiting")]
  [InlineData(RaceDayState.Running, "running")]
  [InlineData(RaceDayState.LastLaps, "lastLaps")]
  [InlineData(RaceDayState.Finishing, "finishing")]
  [InlineData(RaceDayState.Finished, "finished")]
  public void TheStateIsTheRaceDayScreens(RaceDayState state, string expected)
  {
    Assert.Equal(expected, Build(state).State);
  }

  [Fact]
  public void TheClockSaysHowLongHasGoneAndHowLongIsLeft()
  {
    var snapshot = Build(RaceDayState.Running, RiderBuilder.Rider("A", "1", "Anna Berger").Laps(2, 40).Build());

    Assert.Equal(8 * 60_000, snapshot.Clock.ElapsedMs);
    Assert.Equal(12 * 60_000, snapshot.Clock.RemainingMs);
    Assert.Equal(20 * 60_000, snapshot.Clock.ScheduledMs);
    Assert.Equal(7, snapshot.Seq);
  }

  [Fact]
  public void NoTransponderCodeReachesTheWebsite()
  {
    var adler = RiderBuilder.Team("MSC Adler",
        RiderBuilder.Member("11", "Anna Berger", "TAG-SECRET-1"),
        RiderBuilder.Member("14", "Ben Fischer", "TAG-SECRET-2"))
      .LapBy(40, "TAG-SECRET-1").Build();
    var solo = RiderBuilder.Rider("TAG-SECRET-3", "7", "Carla Hoff").Laps(2, 40).Build();

    var json = LiveSnapshotBuilder.Serialise(Build(RaceDayState.Running, adler, solo));

    Assert.DoesNotContain("TAG-SECRET", json);
  }

  [Fact]
  public void ARiderNobodyIdentifiedIsNamedRatherThanBlank()
  {
    var unknown = RiderBuilder.Rider("20269990", "", "").Laps(2, 40).Build();

    var entry = Build(RaceDayState.Running, unknown).Entries.Single();

    Assert.Equal("Unidentified rider", entry.Name);
    Assert.DoesNotContain("20269990", LiveSnapshotBuilder.Serialise(Build(RaceDayState.Running, unknown)));
  }
  [Fact]
  public void ADemoSaysSoInAFieldTheWebsiteCanTrust()
  {
    var snapshot = LiveSnapshotBuilder.Build(new LiveInputs
    {
      PublicId = "b3f1c0de0000000000000000000000ff", Title = "DEMO · A short race",
      State = RaceDayState.Running, StartedAt = Start, Duration = TimeSpan.FromMinutes(6),
      Now = Start.AddMinutes(1), Demo = true,
      Riders = new[] { LiveCapture.Of(RiderBuilder.Rider("A", "1", "Anna Berger").Laps(2, 40).Build()) },
      ClientVersion = "test"
    });

    Assert.True(snapshot.Demo);
    Assert.Contains("\"demo\":true", LiveSnapshotBuilder.Serialise(snapshot));
    // A real race is not one, whatever it is called.
    Assert.False(Build(RaceDayState.Running).Demo);
  }
}
