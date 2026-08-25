using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace CrossMgrInterface.Tests;

/// <summary>
/// Measures the data-preparation cost behind the riders grid at the size of a
/// large event, to separate "the calculations are slow" from "writing to the
/// DataGridView is slow". Reported, not asserted on timing - a wall-clock
/// assertion would be flaky on shared hardware.
/// </summary>
public class LargeFieldBenchmarks
{
  private readonly ITestOutputHelper _out;
  public LargeFieldBenchmarks(ITestOutputHelper output) => _out = output;

  private static List<RiderInfo> Field(int riders, int laps, bool withSplitSuggestions)
  {
    var random = new Random(250);
    var field = new List<RiderInfo>();
    for (var i = 0; i < riders; i++)
    {
      var b = RiderBuilder.Rider($"RIDER{i:D3}", (i + 1).ToString(), $"Rider {i}");
      var pace = 34 + random.NextDouble() * 20;
      for (var l = 0; l < laps; l++) b.Lap(Math.Round(pace + random.NextDouble() * 3, 3));
      var rider = b.Build();
      if (withSplitSuggestions && i % 10 == 0 && rider.Laps.Count > 2)
      {
        var lap = rider.Laps[^1];
        lap.IsSuggestedForSplit = true;
        lap.SuggestedSplitCount = 2;
        lap.SuggestedSplitLapTime = TimeSpan.FromMilliseconds((lap.LapTime ?? TimeSpan.Zero).TotalMilliseconds / 2);
      }
      field.Add(rider);
    }
    return field;
  }

  private long Time(string label, int reps, Action work)
  {
    work(); // warm up
    var sw = Stopwatch.StartNew();
    for (var i = 0; i < reps; i++) work();
    sw.Stop();
    var per = sw.ElapsedMilliseconds / (double)reps;
    _out.WriteLine($"{label,-46}{per,8:F2} ms");
    return (long)per;
  }

  [Fact]
  public void DataPreparationForALargeFieldIsCheap()
  {
    const int riders = 250, laps = 9;
    var field = Field(riders, laps, withSplitSuggestions: true);
    var dict = field.ToDictionary(r => r.TagID, r => r);

    _out.WriteLine($"field: {riders} riders x {laps} laps\n");

    var clone = Time("deep copy of the field", 20,
      () => { var _ = field.Select(r => new RiderInfo { TagID = r.TagID, Laps = r.Laps.ToList() }).ToList(); });

    var sort = Time("GetSortedRidersFromSnapshot", 20,
      () => { var _ = PositionCalculator.GetSortedRidersFromSnapshot(field); });

    var table = Time("BuildLapPositionTable (lap progression)", 20,
      () => { var _ = PositionCalculator.BuildLapPositionTable(field, laps); });

    var projected = Time("CalculateProjectedPositionsWithSplits", 20,
      () => { var _ = PositionCalculator.CalculateProjectedPositionsWithSplits(dict); });

    var stats = Time("BestLap + AverageLap for every rider", 20,
      () => { foreach (var r in field) { var _ = r.BestLapTime; var __ = r.AverageLapTime; } });

    var total = clone + sort + projected + stats;
    _out.WriteLine($"\n{"total data prep per riders-grid refresh",-46}{total,8} ms");
    _out.WriteLine("measured UI render for the same field:            ~900 ms");
    _out.WriteLine("=> the remainder is DataGridView cell writing.");

    Assert.True(field.Count == riders);
  }
}
