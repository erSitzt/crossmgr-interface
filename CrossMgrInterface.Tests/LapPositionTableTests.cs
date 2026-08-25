using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// Pins the claim that lets the lap progression grid stop recomputing positions
/// per cell: <see cref="PositionCalculator.BuildLapPositionTable"/> must agree
/// with <see cref="PositionCalculator.CalculatePositionAtLapFromSnapshot"/> for
/// every rider and every lap.
///
/// If a future change makes lap numbering independent of crossing order, these
/// tests fail rather than the grid quietly showing wrong positions.
/// </summary>
public class LapPositionTableTests
{
  [Fact]
  public void MatchesThePerCellCalculationAcrossASimulatedRace()
  {
    var field = SimulateRace(riders: 20, laps: 15, seed: 20250806);
    var maxLaps = field.Max(r => r.Laps.Count);

    var table = PositionCalculator.BuildLapPositionTable(field, maxLaps);

    foreach (var rider in field)
    {
      for (var lap = 1; lap <= rider.Laps.Count; lap++)
      {
        var expected = PositionCalculator.CalculatePositionAtLapFromSnapshot(rider, lap, field);
        var actual = PositionCalculator.PositionAtLap(table, rider.TagID, lap);

        Assert.True(expected == actual,
          $"lap {lap}, rider {rider.TagID}: per-cell said P{expected}, table said P{actual}");
      }
    }
  }

  [Fact]
  public void MatchesWhenRidersRetireAtDifferentLaps()
  {
    // Uneven lap counts are the interesting case: at lap N only the riders who
    // reached it are ranked.
    var field = SimulateRace(riders: 12, laps: 10, seed: 7, retireSome: true);
    var maxLaps = field.Max(r => r.Laps.Count);

    var table = PositionCalculator.BuildLapPositionTable(field, maxLaps);

    foreach (var rider in field)
    {
      for (var lap = 1; lap <= rider.Laps.Count; lap++)
      {
        Assert.Equal(
          PositionCalculator.CalculatePositionAtLapFromSnapshot(rider, lap, field),
          PositionCalculator.PositionAtLap(table, rider.TagID, lap));
      }
    }
  }

  [Fact]
  public void ReportsTheSameSentinelForALapTheRiderNeverCompleted()
  {
    var field = SimulateRace(riders: 3, laps: 4, seed: 1);
    var table = PositionCalculator.BuildLapPositionTable(field, 10);

    Assert.Equal(999, PositionCalculator.PositionAtLap(table, field[0].TagID, 9));
    Assert.Equal(999, PositionCalculator.PositionAtLap(table, "not-a-tag", 1));
  }

  [Fact]
  public void RanksLapOneByCrossingTime()
  {
    var slow = RiderBuilder.Rider("slow").Lap(50).Build();
    var quick = RiderBuilder.Rider("quick").Lap(38).Build();
    var middle = RiderBuilder.Rider("middle").Lap(44).Build();

    var table = PositionCalculator.BuildLapPositionTable(
      new[] { slow, quick, middle }, maxLaps: 1);

    Assert.Equal(1, PositionCalculator.PositionAtLap(table, "quick", 1));
    Assert.Equal(2, PositionCalculator.PositionAtLap(table, "middle", 1));
    Assert.Equal(3, PositionCalculator.PositionAtLap(table, "slow", 1));
  }

  /// <summary>
  /// Builds a field with varied and drifting lap times, so positions actually
  /// change hands during the race rather than staying in starting order.
  /// </summary>
  private static List<RiderInfo> SimulateRace(int riders, int laps, int seed, bool retireSome = false)
  {
    var random = new Random(seed);
    var field = new List<RiderInfo>();

    for (var i = 0; i < riders; i++)
    {
      var builder = RiderBuilder.Rider($"RIDER{i:D3}", number: (i + 1).ToString(), name: $"Rider {i}");

      // Riders retire at assorted points so lap counts differ across the field.
      var lapsForThisRider = retireSome && i % 4 == 0
        ? Math.Max(1, laps - random.Next(1, laps))
        : laps;

      var pace = 38.0 + random.NextDouble() * 8.0;
      for (var lap = 0; lap < lapsForThisRider; lap++)
      {
        // Drift the pace so the order genuinely churns between laps.
        pace += (random.NextDouble() - 0.45) * 3.0;
        builder.Lap(Math.Round(pace, 3));
      }

      field.Add(builder.Build());
    }

    return field;
  }
}
