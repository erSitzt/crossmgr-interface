using Xunit;

namespace CrossMgrInterface.Tests;

public class PositionCalculatorTests
{
  [Fact]
  public void SortsByLapCountBeforeElapsedTime()
  {
    var slowerButFurther = RiderBuilder.Rider("A").Laps(4, 60).Build();
    var fasterButBehind = RiderBuilder.Rider("B").Laps(3, 30).Build();

    var sorted = PositionCalculator.GetSortedRidersFromSnapshot(
      new[] { fasterButBehind, slowerButFurther });

    Assert.Equal("A", sorted[0].TagID);
    Assert.Equal("B", sorted[1].TagID);
  }

  [Fact]
  public void OnEqualLapsTheFasterRiderLeads()
  {
    var fast = RiderBuilder.Rider("fast").Laps(3, 40).Build();
    var slow = RiderBuilder.Rider("slow").Laps(3, 45).Build();

    var sorted = PositionCalculator.GetSortedRidersFromSnapshot(new[] { slow, fast });

    Assert.Equal("fast", sorted[0].TagID);
  }

  [Fact]
  public void DnfRidersSortBehindEveryoneStillRacing()
  {
    var dnfWithMostLaps = RiderBuilder.Rider("dnf").Laps(10, 40).Dnf().Build();
    var runningWithFewest = RiderBuilder.Rider("running").Laps(1, 40).Build();

    var sorted = PositionCalculator.GetSortedRidersFromSnapshot(
      new[] { dnfWithMostLaps, runningWithFewest });

    Assert.Equal("running", sorted[0].TagID);
    Assert.Equal("dnf", sorted[1].TagID);
  }

  [Fact]
  public void PositionAtLapRanksByThatLapsCrossingTime()
  {
    // Three riders cross lap 2 in the order C, A, B.
    var a = RiderBuilder.Rider("A").Lap(40).Lap(41).Build();
    var b = RiderBuilder.Rider("B").Lap(40).Lap(43).Build();
    var c = RiderBuilder.Rider("C").Lap(39).Lap(39).Build();
    var field = new List<RiderInfo> { a, b, c };

    Assert.Equal(1, PositionCalculator.CalculatePositionAtLapFromSnapshot(c, 2, field));
    Assert.Equal(2, PositionCalculator.CalculatePositionAtLapFromSnapshot(a, 2, field));
    Assert.Equal(3, PositionCalculator.CalculatePositionAtLapFromSnapshot(b, 2, field));
  }
}
