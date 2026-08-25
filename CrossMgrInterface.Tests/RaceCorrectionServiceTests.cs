using Xunit;

namespace CrossMgrInterface.Tests;

public class RecomputeRiderTests
{
  private static readonly DateTime Start = RiderBuilder.RaceStart;

  [Fact]
  public void NumbersLapsSequentiallyInCrossingOrder()
  {
    var rider = RiderBuilder.Rider("A").Lap(40).Lap(41).Lap(42).Build();

    // Shuffle to prove ordering comes from the crossing times, not the list order.
    rider.Laps.Reverse();
    RaceCorrectionService.RecomputeRider(rider, Start);

    Assert.Equal(new[] { 1, 2, 3 }, rider.Laps.Select(l => l.LapNumber));
    Assert.Equal(
      new[] { 40.0, 41.0, 42.0 },
      rider.Laps.Select(l => Math.Round(l.LapTime!.Value.TotalSeconds, 3)));
  }

  [Fact]
  public void FirstLapIsMeasuredFromTheRaceStart()
  {
    var rider = RiderBuilder.Rider("A").Lap(47).Build();

    RaceCorrectionService.RecomputeRider(rider, Start);

    Assert.Equal(TimeSpan.FromSeconds(47), rider.Laps[0].LapTime);
  }

  [Fact]
  public void FirstLapHasNoTimeWhenTheRaceStartIsUnknown()
  {
    var rider = RiderBuilder.Rider("A").Lap(47).Lap(40).Build();

    RaceCorrectionService.RecomputeRider(rider, null);

    Assert.Null(rider.Laps[0].LapTime);
    Assert.Equal(TimeSpan.FromSeconds(40), rider.Laps[1].LapTime);
  }

  [Fact]
  public void DropsTombstonedLapsAndClosesTheGap()
  {
    var rider = RiderBuilder.Rider("A").Lap(40).Lap(41).Lap(42).Build();
    rider.Laps[1].IsDeleted = true;

    RaceCorrectionService.RecomputeRider(rider, Start);

    Assert.Equal(2, rider.Laps.Count);
    Assert.Equal(new[] { 1, 2 }, rider.Laps.Select(l => l.LapNumber));
    // Lap 2 now spans the removed lap: 41 + 42 seconds.
    Assert.Equal(83, Math.Round(rider.Laps[1].LapTime!.Value.TotalSeconds, 3));
  }

  [Fact]
  public void RefreshesFirstAndLastCrossing()
  {
    var rider = RiderBuilder.Rider("A").Lap(40).Lap(41).Build();
    rider.Laps[1].IsDeleted = true;

    RaceCorrectionService.RecomputeRider(rider, Start);

    Assert.Equal(Start.AddSeconds(40), rider.LastCrossing);
    Assert.Equal(Start.AddSeconds(40), rider.LastCrossingTime);
  }

  [Fact]
  public void HandlesAnEmptyLapList()
  {
    var rider = RiderBuilder.Rider("A").Build();

    RaceCorrectionService.RecomputeRider(rider, Start);

    Assert.Empty(rider.Laps);
  }

  [Fact]
  public void BumpsTheRevisionOnEveryPass()
  {
    var rider = RiderBuilder.Rider("A").Lap(40).Build();
    var before = rider.Revision;

    RaceCorrectionService.RecomputeRider(rider, Start);

    Assert.Equal(before + 1, rider.Revision);
  }
}

public class CorrectionOperationTests
{
  private static readonly DateTime Start = RiderBuilder.RaceStart;

  private static (RaceCorrectionService Service, Dictionary<string, RiderInfo> Field) NewField(
    params RiderInfo[] riders)
  {
    var field = riders.ToDictionary(r => r.TagID, r => r);
    var service = new RaceCorrectionService(field, new object(), () => Start, _ => { });
    return (service, field);
  }

  [Fact]
  public void AddingAMissedLapRenumbersTheLapsAfterIt()
  {
    var rider = RiderBuilder.Rider("A").Lap(40).Lap(80).Build();
    var (service, field) = NewField(rider);

    // Drop a lap in halfway through the long one.
    var result = service.AddLap("A", Start.AddSeconds(80), rider.Revision);

    Assert.True(result.Ok, result.Error);
    Assert.Equal(3, field["A"].Laps.Count);
    Assert.Equal(new[] { 1, 2, 3 }, field["A"].Laps.Select(l => l.LapNumber));
    Assert.Equal(LapSource.ManualInsert, field["A"].Laps[1].Source);
  }

  [Fact]
  public void DeletingThenReAddingALapRestoresTheOriginalStandings()
  {
    var rider = RiderBuilder.Rider("A").Lap(40).Lap(41).Lap(42).Build();
    var (service, field) = NewField(rider);

    var originalCrossings = field["A"].Laps.Select(l => l.CrossingTime).ToList();
    var deletedCrossing = field["A"].Laps[1].CrossingTime;

    service.DeleteLap("A", 2, field["A"].Revision);
    service.AddLap("A", deletedCrossing, field["A"].Revision);

    Assert.Equal(originalCrossings, field["A"].Laps.Select(l => l.CrossingTime));
    Assert.Equal(new[] { 1, 2, 3 }, field["A"].Laps.Select(l => l.LapNumber));
  }

  [Fact]
  public void EditingALapTimeShiftsTheFollowingLapByTheInverse()
  {
    var rider = RiderBuilder.Rider("A").Lap(40).Lap(40).Build();
    var (service, field) = NewField(rider);

    // Move the first crossing five seconds later.
    service.EditLapTime("A", 1, Start.AddSeconds(45), field["A"].Revision);

    Assert.Equal(45, field["A"].Laps[0].LapTime!.Value.TotalSeconds);
    Assert.Equal(35, field["A"].Laps[1].LapTime!.Value.TotalSeconds);
    Assert.Equal(Start.AddSeconds(40), field["A"].Laps[0].OriginalCrossingTime);
  }

  [Fact]
  public void SplittingALapProducesEqualSegmentsEndingOnTheRealRead()
  {
    var rider = RiderBuilder.Rider("A").Lap(40).Lap(120).Build();
    var (service, field) = NewField(rider);
    var realCrossing = field["A"].Laps[1].CrossingTime;

    var result = service.SplitLap("A", 2, 3, field["A"].Revision);

    Assert.True(result.Ok, result.Error);
    Assert.Equal(4, field["A"].Laps.Count);
    // Three 40-second segments in place of the 120-second lap.
    Assert.Equal(
      new[] { 40.0, 40.0, 40.0, 40.0 },
      field["A"].Laps.Select(l => Math.Round(l.LapTime!.Value.TotalSeconds, 3)));
    // The final segment must land on the crossing that was actually read.
    Assert.Equal(realCrossing, field["A"].Laps[^1].CrossingTime);
    Assert.True(field["A"].Laps[^1].IsSplitLap);
  }

  [Fact]
  public void SplittingRefusesAnythingBelowTwo()
  {
    var rider = RiderBuilder.Rider("A").Lap(40).Lap(120).Build();
    var (service, _) = NewField(rider);

    Assert.False(service.SplitLap("A", 2, 1, rider.Revision).Ok);
  }

  [Fact]
  public void AStaleRevisionIsRejectedRatherThanApplied()
  {
    var rider = RiderBuilder.Rider("A", "12", "Max Mustermann").Lap(40).Lap(41).Build();
    var (service, field) = NewField(rider);

    var staleRevision = field["A"].Revision;
    // Simulate a crossing landing while the operator had the dialog open.
    service.AddLap("A", Start.AddSeconds(120), staleRevision);

    var result = service.DeleteLap("A", 1, staleRevision);

    Assert.False(result.Ok);
    Assert.Contains("crossed the line while this window was open", result.Error);
    // And nothing was changed.
    Assert.Equal(3, field["A"].Laps.Count);
  }

  [Fact]
  public void DismissingASuggestionStopsItBeingReFlagged()
  {
    var rider = RiderBuilder.Rider("A").Lap(40).Lap(120).Build();
    rider.Laps[1].IsSuggestedForSplit = true;
    rider.Laps[1].SuggestedSplitCount = 3;
    var (service, field) = NewField(rider);

    service.DismissSplitSuggestion("A", 2);

    Assert.False(field["A"].Laps[1].IsSuggestedForSplit);
    Assert.True(field["A"].Laps[1].SuggestionDismissed);
  }

  [Fact]
  public void ManualStatusIsFlaggedSoTheAutomaticTimeoutCannotOverrideIt()
  {
    var rider = RiderBuilder.Rider("A").Lap(40).Build();
    var (service, field) = NewField(rider);

    service.SetRiderStatus("A", RiderStatus.DNF, "retired at the gate");

    Assert.True(field["A"].IsDNF);
    Assert.True(field["A"].StatusSetByOperator);
    Assert.Equal("DNF", field["A"].StatusText);

    // Putting them back in the race hands control back to the timeout.
    service.SetRiderStatus("A", RiderStatus.Racing);
    Assert.False(field["A"].IsDNF);
    Assert.False(field["A"].StatusSetByOperator);
  }

  [Fact]
  public void CorrectingAnUnknownRiderFailsCleanly()
  {
    var (service, _) = NewField();

    var result = service.AddLap("nobody", Start, -1);

    Assert.False(result.Ok);
    Assert.NotNull(result.Error);
  }
}

public class CorrectionUndoTests
{
  private static readonly DateTime Start = RiderBuilder.RaceStart;

  private static (RaceCorrectionService Service, Dictionary<string, RiderInfo> Field) NewField(
    params RiderInfo[] riders)
  {
    var field = riders.ToDictionary(r => r.TagID, r => r);
    var service = new RaceCorrectionService(field, new object(), () => Start, _ => { });
    return (service, field);
  }

  private static string Fingerprint(RiderInfo rider) =>
    string.Join("|", rider.Laps.Select(l =>
      $"{l.LapNumber}@{l.CrossingTime:O}/{l.LapTime}/{l.Source}/{l.IsSplitLap}"))
    + $"::DNF={rider.IsDNF},DNS={rider.IsDNS}";

  [Fact]
  public void UndoRestoresTheExactStateBeforeEachOperation()
  {
    var rider = RiderBuilder.Rider("A").Laps(6, 40).Build();
    var (service, field) = NewField(rider);
    var original = Fingerprint(field["A"]);

    service.DeleteLap("A", 3, field["A"].Revision);
    Assert.NotEqual(original, Fingerprint(field["A"]));

    service.Undo();
    Assert.Equal(original, Fingerprint(field["A"]));
  }

  [Fact]
  public void UndoingToEmptyReproducesTheStartingStateAcrossMixedOperations()
  {
    var rider = RiderBuilder.Rider("A").Laps(8, 40).Build();
    var (service, field) = NewField(rider);
    var original = Fingerprint(field["A"]);

    service.DeleteLap("A", 2, field["A"].Revision);
    service.AddLap("A", Start.AddSeconds(500), field["A"].Revision);
    service.EditLapTime("A", 4, Start.AddSeconds(175), field["A"].Revision);
    service.SplitLap("A", 6, 2, field["A"].Revision);
    service.SetRiderStatus("A", RiderStatus.DNF);

    while (service.History.CanUndo)
      service.Undo();

    Assert.Equal(original, Fingerprint(field["A"]));
  }

  [Fact]
  public void ApplyUndoRedoUndoLandsBackOnTheOriginal()
  {
    var rider = RiderBuilder.Rider("A").Laps(5, 40).Build();
    var (service, field) = NewField(rider);
    var original = Fingerprint(field["A"]);

    service.DeleteLap("A", 3, field["A"].Revision);
    var afterDelete = Fingerprint(field["A"]);

    service.Undo();
    Assert.Equal(original, Fingerprint(field["A"]));

    service.Redo();
    Assert.Equal(afterDelete, Fingerprint(field["A"]));

    service.Undo();
    Assert.Equal(original, Fingerprint(field["A"]));
  }

  [Fact]
  public void ANewCorrectionDiscardsTheRedoBranch()
  {
    var rider = RiderBuilder.Rider("A").Laps(5, 40).Build();
    var (service, field) = NewField(rider);

    service.DeleteLap("A", 3, field["A"].Revision);
    service.Undo();
    Assert.True(service.History.CanRedo);

    service.DeleteLap("A", 2, field["A"].Revision);
    Assert.False(service.History.CanRedo);
  }

  [Fact]
  public void UndoIsBoundedSoLongRacesDoNotGrowWithoutLimit()
  {
    var rider = RiderBuilder.Rider("A").Laps(3, 40).Build();
    var (service, field) = NewField(rider);

    for (var i = 0; i < CorrectionHistory.MaxDepth + 10; i++)
      service.SetRiderStatus("A", i % 2 == 0 ? RiderStatus.DNF : RiderStatus.Racing);

    Assert.Equal(CorrectionHistory.MaxDepth, service.History.Commands.Count);
  }

  [Fact]
  public void UndoReportsWhatItWouldReverse()
  {
    var rider = RiderBuilder.Rider("A", "12", "Max Mustermann").Laps(3, 40).Build();
    var (service, field) = NewField(rider);

    service.DeleteLap("A", 2, field["A"].Revision);

    Assert.Contains("Deleted lap 2", service.History.NextUndoDescription);
    Assert.Contains("#12 Max Mustermann", service.History.NextUndoDescription);
  }

  [Fact]
  public void UndoWithNothingToUndoFailsRatherThanThrowing()
  {
    var (service, _) = NewField(RiderBuilder.Rider("A").Lap(40).Build());

    Assert.False(service.Undo().Ok);
    Assert.False(service.Redo().Ok);
  }
}
