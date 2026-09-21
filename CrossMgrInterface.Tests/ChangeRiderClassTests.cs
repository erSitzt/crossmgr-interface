using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// Moving a rider the rider list put in the wrong class.
///
/// The promise the operator is given is narrow and worth pinning: it changes
/// which sheet the rider appears on and nothing else. In particular it must not
/// move a single lap time, because in a staggered race lap 1 is measured from
/// the rider's own gate - and the rider left the gate they left, whatever the
/// list said about them.
/// </summary>
public class ChangeRiderClassTests
{
  private static readonly DateTime Start = RiderBuilder.RaceStart;

  private static (RaceCorrectionService Service, Dictionary<string, RiderInfo> Field) NewField(
    params RiderInfo[] riders)
  {
    var field = riders.ToDictionary(r => r.TagID, r => r);
    var service = new RaceCorrectionService(field, new object(), () => Start, _ => { });
    return (service, field);
  }

  private static string LapFingerprint(RiderInfo rider) =>
    string.Join("|", rider.Laps.Select(l => $"{l.LapNumber}@{l.CrossingTime:O}/{l.LapTime}"));

  [Fact]
  public void TheClassChangesAndNothingElseDoes()
  {
    var rider = RiderBuilder.Rider("A").Category("MX2").Laps(5, 40).Build();
    var (service, field) = NewField(rider);

    var lapsBefore = LapFingerprint(rider);
    var totalBefore = rider.TotalTime;

    Assert.True(service.SetRiderClass("A", "MX1").Ok);

    Assert.Equal("MX1", field["A"].Category);
    Assert.Equal(lapsBefore, LapFingerprint(field["A"]));
    Assert.Equal(totalBefore, field["A"].TotalTime);
  }

  [Fact]
  public void AStaggeredRidersStartTimeIsLeftAlone()
  {
    // The rider went off with the second wave, two minutes after the first.
    // Scoring them in another class must not pretend they left with it - their
    // first lap is measured from the gate they actually used.
    var gate = Start.AddMinutes(2);
    var rider = RiderBuilder.Rider("A").Category("MX2").Laps(3, 40).Build();
    rider.RaceStartTime = gate;
    RaceCorrectionService.RecomputeRider(rider, Start);

    var firstLap = rider.Laps[0].LapTime;
    var (service, field) = NewField(rider);

    Assert.True(service.SetRiderClass("A", "MX1").Ok);

    Assert.Equal(gate, field["A"].RaceStartTime);
    Assert.Equal(firstLap, field["A"].Laps[0].LapTime);
  }

  [Fact]
  public void UndoPutsTheRiderBackAndRedoMovesThemAgain()
  {
    var rider = RiderBuilder.Rider("A").Category("MX2").Laps(4, 40).Build();
    var (service, field) = NewField(rider);

    service.SetRiderClass("A", "MX1");
    Assert.Equal("MX1", field["A"].Category);

    Assert.True(service.Undo().Ok);
    Assert.Equal("MX2", field["A"].Category);

    Assert.True(service.Redo().Ok);
    Assert.Equal("MX1", field["A"].Category);
  }

  [Fact]
  public void ARiderWhoHadNoClassCanBeGivenOne()
  {
    // A transponder that was never on the rider list has no class at all, and
    // "from no class" reads better in the undo menu than "from ".
    var rider = RiderBuilder.Rider("A").Laps(2, 40).Build();
    var (service, field) = NewField(rider);

    var result = service.SetRiderClass("A", "MX1");

    Assert.True(result.Ok);
    Assert.Equal("MX1", field["A"].Category);
    Assert.Contains("from no class to MX1", result.Command!.Description);
  }

  [Fact]
  public void TheDescriptionNamesTheRiderAndBothClasses()
  {
    // It is what the Undo menu offers the operator, so it has to say enough to
    // decide by without opening anything.
    var rider = RiderBuilder.Rider("A", "77").Category("MX2").Laps(2, 40).Build();
    var (service, _) = NewField(rider);

    var description = service.SetRiderClass("A", "MX1").Command!.Description;

    Assert.Contains("77", description);
    Assert.Contains("MX2", description);
    Assert.Contains("MX1", description);
  }

  [Fact]
  public void ARiderWhoIsNoLongerInTheRaceFailsCleanly()
  {
    var (service, _) = NewField(RiderBuilder.Rider("A").Laps(2, 40).Build());

    var result = service.SetRiderClass("GONE", "MX1");

    Assert.False(result.Ok);
    Assert.False(service.History.CanUndo);
  }
}
