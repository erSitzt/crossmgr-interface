using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// RacingPace and PredictedLapTime deliberately disagree. These tests pin both
/// sides of that so nobody later "unifies" them and reintroduces the orbiting
/// leader on the track map.
/// </summary>
public class RacingPaceTests
{
  private static void Close(double expected, double actual, double tolerance, string what) =>
    Assert.True(Math.Abs(expected - actual) <= tolerance,
      $"{what}: expected {expected}, got {actual} (tolerance {tolerance})");

  [Fact]
  public void RacingPaceIgnoresTheRunFromTheStartLine()
  {
    // Laps 0.001, 40, 40, 40. Including lap 1 gives 33.3s; excluding it, 40s.
    var rider = RiderBuilder.Rider("A").Lap(0.001).Lap(40).Lap(40).Lap(40).Build();

    Close(40, rider.RacingPace!.Value.TotalSeconds, 0.001, "racing pace");
  }

  [Fact]
  public void PredictedLapTimeStillIncludesTheFirstLap()
  {
    // The deliberate divergence, pinned. PredictedLapTime is persisted to the race
    // database and drives the riders grid's countdown columns, so it keeps its own
    // behaviour - the map is what had to change, not this.
    var rider = RiderBuilder.Rider("A").Lap(0.001).Lap(40).Lap(40).Lap(40).Build();

    Close(40, rider.PredictedLapTime!.Value.TotalSeconds, 0.001,
      "with four laps, lap 1 has already fallen out of the three-lap window");

    var earlier = RiderBuilder.Rider("B").Lap(0.001).Lap(40).Lap(40).Build();

    // (0.001*1 + 40*2 + 40*3) / 6 = 33.33s - seventeen percent fast, which is
    // exactly the error the map could not live with.
    Close(33.33, earlier.PredictedLapTime!.Value.TotalSeconds, 0.01, "prediction three laps in");
    Close(40, earlier.RacingPace!.Value.TotalSeconds, 0.001, "racing pace three laps in");
  }

  [Fact]
  public void RacingPaceIsNullWithOnlyTheStartLineRun()
  {
    var rider = RiderBuilder.Rider("A").Lap(0.001).Build();

    Assert.Null(rider.RacingPace);
    Assert.NotNull(rider.PredictedLapTime);
  }

  [Fact]
  public void RacingPaceIsNullBeforeAnyLapAtAll()
  {
    var rider = RiderBuilder.Rider("A").Build();

    Assert.Null(rider.RacingPace);
  }

  [Fact]
  public void RacingPaceWeightsTheLastThreeRealLapsThreeTwoOne()
  {
    // Laps after the first: 60, 60, 30. (60*1 + 60*2 + 30*3) / 6 = 45s.
    var rider = RiderBuilder.Rider("A").Lap(0.001).Lap(60).Lap(60).Lap(30).Build();

    Close(45, rider.RacingPace!.Value.TotalSeconds, 0.001, "racing pace");
  }

  [Fact]
  public void RacingPaceIgnoresLapsBeforeTheLastThree()
  {
    var rider = RiderBuilder.Rider("A").Lap(0.001).Laps(5, 90).Lap(40).Lap(40).Lap(40).Build();

    Close(40, rider.RacingPace!.Value.TotalSeconds, 0.001, "racing pace");
  }

  [Fact]
  public void ARiderWithExactlyTwoLapsHasTheirSecondLapAsTheirPace()
  {
    var rider = RiderBuilder.Rider("A").Lap(0.001).Lap(50).Build();

    Close(50, rider.RacingPace!.Value.TotalSeconds, 0.001, "racing pace");
  }
}
