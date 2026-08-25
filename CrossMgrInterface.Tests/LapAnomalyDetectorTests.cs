using Xunit;

namespace CrossMgrInterface.Tests;

public class LapAnomalyDetectorTests
{
  private static readonly TimeSpan FieldAverage = TimeSpan.FromSeconds(40);

  [Fact]
  public void FlagsALapThatIsRoughlyTwiceTheRidersPace()
  {
    // Five normal laps, then one that took two laps' worth of time.
    var rider = RiderBuilder.Rider("A").Laps(5, 40).Lap(80).Build();

    LapAnomalyDetector.Analyze(rider, FieldAverage);

    var flagged = rider.Laps.Single(l => l.IsSuggestedForSplit);
    Assert.Equal(6, flagged.LapNumber);
    Assert.Equal(2, flagged.SuggestedSplitCount);
    Assert.Equal(40, flagged.SuggestedSplitLapTime!.Value.TotalSeconds);
  }

  [Fact]
  public void LeavesNormalLapsAlone()
  {
    var rider = RiderBuilder.Rider("A").Laps(8, 40).Build();

    LapAnomalyDetector.Analyze(rider, FieldAverage);

    Assert.DoesNotContain(rider.Laps, l => l.IsSuggestedForSplit);
  }

  [Fact]
  public void IgnoresALapTooLongToBeAMissedRead()
  {
    // Eight times the pace is a rider who stopped, not a missed read.
    var rider = RiderBuilder.Rider("A").Laps(5, 40).Lap(320).Build();

    LapAnomalyDetector.Analyze(rider, FieldAverage);

    Assert.DoesNotContain(rider.Laps, l => l.IsSuggestedForSplit);
  }

  [Fact]
  public void RefusesASplitThatWouldProduceImplausiblyQuickLaps()
  {
    // This rider is lapping at 10s against a field average of 40s, so splitting
    // their 20s lap in two would imply 10s laps - far quicker than anyone rides.
    var rider = RiderBuilder.Rider("A").Laps(5, 10).Lap(20).Build();

    LapAnomalyDetector.Analyze(rider, FieldAverage);

    Assert.DoesNotContain(rider.Laps, l => l.IsSuggestedForSplit);
  }

  [Fact]
  public void ADismissedWarningIsNotResurrectedByARescan()
  {
    var rider = RiderBuilder.Rider("A").Laps(5, 40).Lap(80).Build();

    LapAnomalyDetector.Analyze(rider, FieldAverage);
    var flagged = rider.Laps.Single(l => l.IsSuggestedForSplit);

    // The operator decides the lap is genuine.
    flagged.IsSuggestedForSplit = false;
    flagged.SuggestionDismissed = true;

    LapAnomalyDetector.Analyze(rider, FieldAverage);

    Assert.DoesNotContain(rider.Laps, l => l.IsSuggestedForSplit);
  }

  [Fact]
  public void ClearsAWarningOnceTheLapNoLongerLooksLong()
  {
    var rider = RiderBuilder.Rider("A").Laps(5, 40).Lap(80).Build();
    LapAnomalyDetector.Analyze(rider, FieldAverage);
    Assert.Contains(rider.Laps, l => l.IsSuggestedForSplit);

    // The operator splits it; the resulting laps are ordinary.
    var service = new RaceCorrectionService(
      new Dictionary<string, RiderInfo> { ["A"] = rider },
      new object(), () => RiderBuilder.RaceStart, _ => { });
    service.SplitLap("A", 6, 2, rider.Revision);

    LapAnomalyDetector.Analyze(rider, FieldAverage);

    Assert.DoesNotContain(rider.Laps, l => l.IsSuggestedForSplit);
  }

  [Fact]
  public void NeedsEnoughHistoryBeforeItJudgesAnything()
  {
    // Two laps is not enough to establish a pace to compare against.
    var rider = RiderBuilder.Rider("A").Lap(40).Lap(200).Build();

    LapAnomalyDetector.Analyze(rider, FieldAverage);

    Assert.DoesNotContain(rider.Laps, l => l.IsSuggestedForSplit);
  }

  [Fact]
  public void NeverJudgesTheFirstLapWhichRunsFromTheRaceStart()
  {
    // A rider who starts at the back has a long first lap by definition.
    var rider = RiderBuilder.Rider("A").Lap(120).Laps(5, 40).Build();

    LapAnomalyDetector.Analyze(rider, FieldAverage);

    Assert.DoesNotContain(rider.Laps, l => l.LapNumber == 1 && l.IsSuggestedForSplit);
  }
}
