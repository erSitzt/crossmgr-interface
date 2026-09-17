using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// What a session needs before it can be put on the results website: an
/// identity that means something off this computer, the circuit it was run on,
/// and the position each lap was completed in.
/// </summary>
public sealed class PublicSessionIdTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), $"crossmgr-test-{Guid.NewGuid():N}.db");
  private RaceDataService _db;

  public PublicSessionIdTests()
  {
    _db = new RaceDataService(_path);
  }

  public void Dispose()
  {
    _db.Dispose();
    try { File.Delete(_path); } catch (IOException) { }
    try { File.Delete(Path.ChangeExtension(_path, null) + "-log.db"); } catch (IOException) { }
  }

  private static readonly DateTime Start = RiderBuilder.RaceStart;

  private int StartRace(string name = "Moto 1", string? trackId = null) =>
    _db.StartNewRace(Start, TimeSpan.FromMinutes(20), name, SessionType.Race, null, trackId);

  /// <summary>Closes and reopens the database, so start-up work runs again.</summary>
  private void Reopen()
  {
    _db.Dispose();
    _db = new RaceDataService(_path);
  }

  [Fact]
  public void AStartedRaceGetsAnIdentityThatMeansSomethingElsewhere()
  {
    var race = _db.GetRace(StartRace());

    Assert.NotNull(race);
    Assert.False(string.IsNullOrEmpty(race!.PublicId));
  }

  [Fact]
  public void TwoRacesNeverShareOne()
  {
    var first = _db.GetRace(StartRace("Moto 1"))!.PublicId;
    var second = _db.GetRace(StartRace("Moto 2"))!.PublicId;

    Assert.NotEqual(first, second);
  }

  [Fact]
  public void ARaceStoredBeforeIdentitiesExistedGetsOneAtStartUp()
  {
    var raceId = StartRace();

    // What a race recorded by an older version looks like: the field is simply
    // not there, which reads back as null.
    _db.UpdateRace(r => r.PublicId = null);
    Assert.True(string.IsNullOrEmpty(_db.GetRace(raceId)!.PublicId));

    Reopen();

    Assert.False(string.IsNullOrEmpty(_db.GetRace(raceId)!.PublicId));
  }

  [Fact]
  public void AnIdentityAlreadyGivenIsNeverChanged()
  {
    var raceId = StartRace();
    var original = _db.GetRace(raceId)!.PublicId;

    Reopen();
    Reopen();

    Assert.Equal(original, _db.GetRace(raceId)!.PublicId);
  }

  [Fact]
  public void AskingForTheIdentityOfARaceThatHasNoneMintsIt()
  {
    var raceId = StartRace();
    _db.UpdateRace(r => r.PublicId = null);

    var minted = _db.EnsurePublicId(raceId);

    Assert.False(string.IsNullOrEmpty(minted));
    Assert.Equal(minted, _db.GetRace(raceId)!.PublicId);
  }

  [Fact]
  public void AskingForTheIdentityOfARaceThatIsNotThereSaysSo()
  {
    Assert.Null(_db.EnsurePublicId(9999));
  }

  [Fact]
  public void TheCircuitTheRaceWasRunOnIsRecorded()
  {
    var race = _db.GetRace(StartRace("Moto 1", "gsc-circuit"));

    Assert.Equal("gsc-circuit", race!.TrackId);
  }

  [Fact]
  public void ACircuitChosenAfterTheClockStartedStillCounts()
  {
    // What ApplyTrack does when a volunteer picks the circuit mid-session.
    var raceId = StartRace();
    Assert.Null(_db.GetRace(raceId)!.TrackId);

    _db.UpdateRace(r => r.TrackId = "gsc-circuit");

    Assert.Equal("gsc-circuit", _db.GetRace(raceId)!.TrackId);
  }

  [Fact]
  public void ARaceWithNoCircuitSaysSoRatherThanGuessing()
  {
    Assert.Null(_db.GetRace(StartRace())!.TrackId);
  }

  [Fact]
  public void PublishingIsRememberedSoTheNextAttemptCanWarn()
  {
    var raceId = StartRace();
    Assert.Null(_db.GetRace(raceId)!.PublishedAt);

    var at = Start.AddMinutes(25);
    _db.MarkPublished(raceId, at, "https://results.openlaptime.de/races/moto-1");

    var race = _db.GetRace(raceId)!;
    Assert.Equal(at, race.PublishedAt);
    Assert.Equal("https://results.openlaptime.de/races/moto-1", race.PublishedUrl);
  }

  [Fact]
  public void APositionIsKeptOnTheLapItWasReached()
  {
    var raceId = StartRace();
    var rider = RiderBuilder.Rider("127", "12", "John Smith").Laps(3, 60).Build();

    // AddLap is what a crossing does, and the position it is given is what the
    // lap chart is later drawn from.
    _db.AddLap(rider.TagID, rider.Laps[1], positionAtCompletion: 3);

    Assert.Equal(3, rider.Laps[1].PositionAtCompletion);

    _db.SaveRaceState(new Dictionary<string, RiderInfo> { [rider.TagID] = rider },
      Start, Start.AddMinutes(22), TimeSpan.FromMinutes(20),
      raceFinished: true, raceTimeExpired: true, waitingForLeaderFinish: false,
      waitingForFinalLaps: false, finalLapsStartTime: null,
      leaderAtTimeExpiry: null, leaderLapsAtTimeExpiry: 0, targetLapsToFinishRace: 0,
      fiveMinuteWarningShown: true);

    var restored = _db.RestoreRiderData(raceId);
    var lap = restored[rider.TagID].Laps.Single(l => l.LapNumber == rider.Laps[1].LapNumber);

    Assert.Equal(rider.Laps[1].PositionAtCompletion, lap.PositionAtCompletion);
  }

  [Fact]
  public void ALapStoredBeforePositionsWereReadBackSaysNothingRatherThanFirst()
  {
    // 0 means "nobody recorded it". It must not be mistaken for the lead.
    Assert.Equal(0, new RiderLap().PositionAtCompletion);
  }
}
