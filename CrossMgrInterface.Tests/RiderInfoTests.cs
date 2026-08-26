using Xunit;

namespace CrossMgrInterface.Tests;

public class RiderInfoTests
{
  [Fact]
  public void PredictedLapTimeWeightsRecentLapsMoreHeavily()
  {
    // Laps 60, 60, 30. Weighted over the last three by 1, 2, 3:
    // (60*1 + 60*2 + 30*3) / 6 = 45 seconds.
    var rider = RiderBuilder.Rider("A").Lap(60).Lap(60).Lap(30).Build();

    Assert.Equal(TimeSpan.FromSeconds(45), rider.PredictedLapTime);
  }

  [Fact]
  public void PredictedLapTimeIgnoresLapsBeforeTheLastThree()
  {
    var withOldLaps = RiderBuilder.Rider("A").Laps(5, 90).Lap(40).Lap(40).Lap(40).Build();

    Assert.Equal(TimeSpan.FromSeconds(40), withOldLaps.PredictedLapTime);
  }

  [Fact]
  public void PredictedLapTimeIsNullBeforeAnyLapIsTimed()
  {
    var rider = RiderBuilder.Rider("A").Build();

    Assert.Null(rider.PredictedLapTime);
    Assert.Null(rider.EstimatedNextCrossing);
  }

  [Fact]
  public void TotalTimeRunsFromRaceStartNotFirstCrossing()
  {
    // A rider whose first crossing is late must not be credited with a short race.
    var rider = RiderBuilder.Rider("A").Lap(70).Lap(40).Build();

    Assert.Equal(TimeSpan.FromSeconds(110), rider.TotalTime);
  }

  [Fact]
  public void DisplayNameFallsBackToTagWhenNoNameIsKnown()
  {
    var unknown = new RiderInfo { TagID = "10000001" };

    Assert.Equal("10000001", unknown.DisplayName);
  }
}

public class LapStatisticsTests
{
  /// <summary>
  /// The race can start on the first transponder read, which makes every
  /// rider's "lap 1" the run from that instant to their own first crossing -
  /// 0.000s for whoever triggered the start. Publishing that as a best lap is
  /// how a results sheet ends up claiming a zero-second lap.
  /// </summary>
  [Fact]
  public void BestLapIgnoresTheRunFromTheStartLine()
  {
    var rider = RiderBuilder.Rider("A").Lap(0.001).Lap(35).Lap(34).Lap(36).Build();

    Assert.Equal(34, rider.BestLapTime!.Value.TotalSeconds, 3);
  }

  [Fact]
  public void AverageLapIgnoresTheRunFromTheStartLine()
  {
    // Laps of 35, 34, 36 average 35 - not 26.25 with the start-line run included.
    var rider = RiderBuilder.Rider("A").Lap(0.001).Lap(35).Lap(34).Lap(36).Build();

    Assert.Equal(35, rider.AverageLapTime!.Value.TotalSeconds, 3);
  }

  [Fact]
  public void ARiderWithOnlyTheFirstCrossingHasNoLapTimesYet()
  {
    var rider = RiderBuilder.Rider("A").Lap(0.5).Build();

    Assert.Null(rider.BestLapTime);
    Assert.Null(rider.AverageLapTime);
  }

  [Fact]
  public void BestLapIsTheQuickestOfTheRealLaps()
  {
    var rider = RiderBuilder.Rider("A").Lap(12).Lap(40).Lap(38).Lap(41).Build();

    Assert.Equal(38, rider.BestLapTime!.Value.TotalSeconds, 3);
  }

  [Fact]
  public void BestLapReturnsTheEarlierOfTwoEqualLaps()
  {
    // The qualifying tie-break reads "whoever set it first" straight off this
    // property, and it is one comparison operator away from being wrong.
    var rider = RiderBuilder.Rider("R", "1").Lap(30).Lap(40).Lap(45).Lap(40).Build();

    var best = rider.BestLap;

    Assert.NotNull(best);
    Assert.Equal(2, best!.LapNumber);
    Assert.Equal(TimeSpan.FromSeconds(40), best.LapTime);
  }

  [Fact]
  public void BestLapIsNullWhenOnlyTheOutLapWasRecorded()
  {
    var rider = RiderBuilder.Rider("R", "1").Lap(35).Build();

    Assert.Null(rider.BestLap);
    Assert.Null(rider.BestLapTime);
  }
}
