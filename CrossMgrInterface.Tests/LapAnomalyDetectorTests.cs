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

  [Fact]
  public void AReadMissedOnTheThirdCrossingIsFound()
  {
    // The out-lap does not count as pace, so demanding two prior laps made a
    // rider's third crossing the earliest that could ever be checked - and a
    // read missed before then was undetectable. This is that lap.
    var rider = RiderBuilder.Rider("EARLY", "1").Lap(0).Lap(41).Lap(82).Lap(41.5).Build();

    LapAnomalyDetector.Analyze(rider, FieldAverage);

    Assert.True(rider.Laps[2].IsSuggestedForSplit);
    Assert.Equal(2, rider.Laps[2].SuggestedSplitCount);
  }

  [Fact]
  public void TheOriginalSettingsStillMissThatLap()
  {
    // Pins why the default moved, so nobody "restores" it without knowing.
    var rider = RiderBuilder.Rider("EARLY", "1").Lap(0).Lap(41).Lap(82).Lap(41.5).Build();

    LapAnomalyDetector.Analyze(rider, FieldAverage, LapAnomalySettings.Original);

    Assert.False(rider.Laps[2].IsSuggestedForSplit);
  }

  [Fact]
  public void OneMissedReadDoesNotHideTheNext()
  {
    // The cascade: an unflagged long lap stays in the pace window and drags the
    // baseline up, so the second miss measures at 1.53x and slips under the
    // threshold. Flagging the first excludes it, and the second is caught.
    var rider = RiderBuilder.Rider("PAIR", "1")
      .Lap(0).Lap(41).Lap(82).Lap(41.5).Lap(84).Lap(42).Build();

    LapAnomalyDetector.Analyze(rider, FieldAverage);

    Assert.True(rider.Laps[2].IsSuggestedForSplit, "first missed read");
    Assert.True(rider.Laps[4].IsSuggestedForSplit, "second missed read, hidden by the first");
  }

  [Fact]
  public void SettingsFromAHandEditedFileCannotDisableDetection()
  {
    // The values reach the detector from a JSON file an operator can edit.
    var rider = RiderBuilder.Rider("R", "1").Lap(0).Lap(41).Lap(82).Lap(41.5).Build();

    LapAnomalyDetector.Analyze(rider, FieldAverage,
      new LapAnomalySettings { MinRatio = -5, MaxRatio = 0, MinPriorLaps = -1, PaceWindow = 0 });

    // Falls back to the shipped values, so detection still works. Squeezing
    // them to the nearest valid end instead produced a band a fraction of a lap
    // wide that flagged nothing - a silently dead detector.
    Assert.True(rider.Laps[2].IsSuggestedForSplit);
  }
}
