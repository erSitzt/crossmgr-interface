using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// What survives in the database between a session ending and its sheet being
/// reprinted: the finished flag, every rider's status, the riders who never
/// went out, and the ability to list, rename and delete what is stored.
/// </summary>
public sealed class RaceDataServiceTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), $"crossmgr-test-{Guid.NewGuid():N}.db");
  private readonly RaceDataService _db;

  public RaceDataServiceTests()
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

  private static readonly RaceRules Rules = new()
  {
    SessionType = SessionType.Race,
    Duration = TimeSpan.FromMinutes(20),
    AdditionalLaps = 2,
    DnfTimeoutMinutes = 2,
    MinimumLapSeconds = 10,
    ManualStart = false
  };

  private int StartRace(string name = "Moto 1", SessionType type = SessionType.Race) =>
    _db.StartNewRace(Start, TimeSpan.FromMinutes(20), name, type, Rules);

  /// <summary>Saves the field as the periodic timer and the finish both do.</summary>
  private void Save(IEnumerable<RiderInfo> riders, bool finished, IReadOnlyCollection<string>? ignored = null)
  {
    _db.SaveRaceState(riders.ToDictionary(r => r.TagID, r => r),
      Start, finished ? Start.AddMinutes(22) : null, TimeSpan.FromMinutes(20),
      raceFinished: finished, raceTimeExpired: finished, waitingForLeaderFinish: false,
      waitingForFinalLaps: false, finalLapsStartTime: finished ? Start.AddMinutes(21) : null,
      leaderAtTimeExpiry: null, leaderLapsAtTimeExpiry: 0, targetLapsToFinishRace: 0,
      fiveMinuteWarningShown: true, ignoredTags: ignored);
  }

  [Fact]
  public void AFinishedRaceIsNoLongerOfferedForRecovery()
  {
    // The bug this file exists for: a race that had been finished and printed
    // was still "unfinished" on disk, so every restart offered to restore it.
    var id = StartRace();
    var rider = RiderBuilder.Rider("A", "1").Laps(3, 60).Build();
    _db.UpsertRider(rider);

    Assert.NotNull(_db.GetLatestUnfinishedRace());

    Save(new[] { rider }, finished: true);

    Assert.Null(_db.GetLatestUnfinishedRace());

    var stored = _db.GetRace(id)!;
    Assert.True(stored.IsFinished);
    Assert.Equal(Start.AddMinutes(22), stored.EndTime);
    Assert.NotNull(stored.LastSavedAt);
    Assert.Equal(2, stored.AdditionalLaps);
  }

  [Fact]
  public void TheRulesAreStoredWithTheRaceAndFollowAMidRaceChange()
  {
    // The sheet prints what the race was scored under, so it must be on the
    // row - and it must be the value that applied at the end, because the
    // length and the DNF timeout can be changed while the clock is running.
    var id = StartRace();

    var stored = RaceRules.FromRace(_db.GetRace(id)!);
    Assert.Equal((2, 2, 10.0, false), (stored.AdditionalLaps, stored.DnfTimeoutMinutes, stored.MinimumLapSeconds, stored.ManualStart));
    Assert.Equal(TimeSpan.FromMinutes(20), stored.Duration);

    var changed = new RaceRules
    {
      SessionType = SessionType.Race,
      Duration = TimeSpan.FromMinutes(25),
      AdditionalLaps = 1,
      DnfTimeoutMinutes = 3,
      MinimumLapSeconds = 0,
      ManualStart = false
    };
    _db.SaveRaceState(new(), Start, null, changed.Duration, false, false, false, false, null, null, 0, 0, false,
      ignoredTags: null, rules: changed);

    stored = RaceRules.FromRace(_db.GetRace(id)!);
    Assert.Equal(TimeSpan.FromMinutes(25), stored.Duration);
    Assert.Equal((1, 3, 0.0), (stored.AdditionalLaps, stored.DnfTimeoutMinutes, stored.MinimumLapSeconds));
  }

  [Fact]
  public void TheWaveScheduleIsStoredWithItsActualStarts()
  {
    // A crash between two gates must come back knowing which classes are
    // away, or the remaining ones would never be started.
    var waves = WaveSchedule.Build(new[] { "MX1", "MX2" }, TimeSpan.FromMinutes(2));
    var rules = new RaceRules { SessionType = SessionType.Race, Duration = TimeSpan.FromMinutes(20), Waves = waves };
    var id = _db.StartNewRace(Start, TimeSpan.FromMinutes(20), "Enduro", SessionType.Race, rules);

    waves.Start(waves.First, Start);
    _db.SaveRaceState(new(), Start, null, TimeSpan.FromMinutes(20), false, false, false, false, null, null, 0, 0, false,
      ignoredTags: null, rules: rules);

    var stored = RaceRules.FromRace(_db.GetRace(id)!).Waves!;
    Assert.Equal("MX1 at the gate, MX2 +2:00", stored.Describe());
    Assert.Equal(Start, stored.First.StartedAt);
    Assert.Equal("MX2", stored.Next!.Class);

    // A race with one start has no schedule at all.
    var plain = StartRace("Moto");
    Assert.Null(RaceRules.FromRace(_db.GetRace(plain)!).Waves);
  }

  [Fact]
  public void ARaceStoredBeforeTheRulesExistedReadsBackAsNotRecorded()
  {
    var id = _db.StartNewRace(Start, TimeSpan.FromMinutes(20), "Old");

    var stored = RaceRules.FromRace(_db.GetRace(id)!);

    Assert.Null(stored.AdditionalLaps);
    Assert.Null(stored.DnfTimeoutMinutes);
    Assert.Null(stored.MinimumLapSeconds);
    Assert.Null(stored.ManualStart);
    Assert.Contains(stored.Describe(), l => l.Value == "not recorded");
  }

  [Fact]
  public void OperatorRulingsComeBackWithTheRiders()
  {
    // A DNS and its reason are decisions a person made on the day. Losing them
    // on restore printed a different sheet from the one handed out.
    var id = StartRace();
    var dns = RiderBuilder.Rider("A", "1").Dns().Build();
    dns.StatusSetByOperator = true;
    dns.StatusReason = "Withdrew before the start";
    var finisher = RiderBuilder.Rider("B", "2").Laps(4, 60).Build();

    Save(new[] { dns, finisher }, finished: true);

    var restored = _db.RestoreRiderData(id);

    Assert.True(restored["A"].IsDNS);
    Assert.True(restored["A"].StatusSetByOperator);
    Assert.Equal("Withdrew before the start", restored["A"].StatusReason);
    Assert.False(restored["B"].IsDNS);
    Assert.False(restored["B"].StatusSetByOperator);
    Assert.Null(restored["B"].StatusReason);
  }

  [Fact]
  public void LapsAreRestoredWithTheirRiders()
  {
    var id = StartRace();
    var rider = RiderBuilder.Rider("A", "1").Laps(3, 60).Build();
    _db.UpsertRider(rider);
    foreach (var lap in rider.Laps) _db.AddLap("A", lap, 1);

    var restored = _db.RestoreRiderData(id);

    Assert.Equal(3, restored["A"].TotalLaps);
    Assert.Equal(TimeSpan.FromSeconds(60), restored["A"].Laps[1].LapTime);
  }

  [Fact]
  public void IgnoredTranspondersAreRememberedAsIgnored()
  {
    // The ignore list lives beside the riders in memory. It used to be
    // dropped entirely on save, so a stopped rider came back into the standings.
    var id = StartRace();
    var kept = RiderBuilder.Rider("A", "1").Laps(3, 60).Build();
    var stopped = RiderBuilder.Rider("STRAY", "").Laps(2, 60).Build();

    Save(new[] { kept, stopped }, finished: false, ignored: new[] { "STRAY" });

    Assert.Equal(new[] { "STRAY" }, _db.GetIgnoredTags(id));
    Assert.Equal(1, _db.ListSessions().Single().Riders);
  }

  [Fact]
  public void RidersWhoNeverWentOutAreKeptForTheGatePickOrderOnly()
  {
    // The gate pick order lists them last; a race classification must not
    // list them at all, and neither must a live race being recovered.
    var id = StartRace("Qualifying", SessionType.TimedQualifying);
    var timed = RiderBuilder.Rider("A", "1").Laps(3, 60).Build();
    _db.UpsertRider(timed);

    var stayedHome = RiderBuilder.Rider("B", "2").Build();
    _db.SaveRosterOnlyRiders(new[] { stayedHome, timed });

    var forTheSheet = _db.RestoreRiderData(id, includeRosterOnly: true);
    var forTheRace = _db.RestoreRiderData(id);

    Assert.Equal(new[] { "A", "B" }, forTheSheet.Keys.OrderBy(k => k));
    Assert.Equal(0, forTheSheet["B"].TotalLaps);
    Assert.Equal(new[] { "A" }, forTheRace.Keys);

    // Not a rider who took part, so not counted as one.
    Assert.Equal(1, _db.ListSessions().Single().Riders);
  }

  [Fact]
  public void SavingTheRosterTwiceDoesNotDuplicateAnyone()
  {
    var id = StartRace("Qualifying", SessionType.TimedQualifying);
    var stayedHome = RiderBuilder.Rider("B", "2").Build();

    _db.SaveRosterOnlyRiders(new[] { stayedHome });
    _db.SaveRosterOnlyRiders(new[] { stayedHome });

    Assert.Single(_db.RestoreRiderData(id, includeRosterOnly: true));
  }

  [Fact]
  public void SessionsAreListedNewestFirstWithTheirCounts()
  {
    var first = _db.StartNewRace(Start, TimeSpan.FromMinutes(20), "Moto 1");
    _db.UpsertRider(RiderBuilder.Rider("A", "1").Laps(3, 60).Build());
    _db.AddLap("A", new RiderLap { TagID = "A", LapNumber = 1, CrossingTime = Start.AddSeconds(60) }, 1);
    _db.AddLap("A", new RiderLap { TagID = "A", LapNumber = 2, CrossingTime = Start.AddSeconds(120) }, 1);

    var second = _db.StartNewRace(Start.AddHours(1), TimeSpan.FromMinutes(10), "Moto 2");
    _db.UpsertRider(RiderBuilder.Rider("A", "1").Laps(1, 60).Build());
    _db.UpsertRider(RiderBuilder.Rider("B", "2").Laps(1, 60).Build());

    var sessions = _db.ListSessions();

    Assert.Equal(new[] { second, first }, sessions.Select(s => s.Race.Id));
    Assert.Equal(("Moto 2", 2, 0), (sessions[0].Race.Name, sessions[0].Riders, sessions[0].Laps));
    Assert.Equal(("Moto 1", 1, 2), (sessions[1].Race.Name, sessions[1].Riders, sessions[1].Laps));
  }

  [Fact]
  public void DeletingASessionRemovesEverythingUnderIt()
  {
    // The old clear-out deleted the children and left the race row behind,
    // which is how the list would have filled with empty sessions.
    var doomed = StartRace("Doomed");
    _db.UpsertRider(RiderBuilder.Rider("A", "1").Laps(2, 60).Build());
    _db.AddLap("A", new RiderLap { TagID = "A", LapNumber = 1, CrossingTime = Start.AddSeconds(60) }, 1);
    _db.AddRaceEvent("RACE_EVENT", "A", "started");
    _db.SavePositionSnapshot(new() { ["A"] = 1 }, new() { ["A"] = 1 });
    _db.StoreLapDifference("A", "B", 1);

    var survivor = StartRace("Survivor");
    _db.UpsertRider(RiderBuilder.Rider("C", "3").Laps(1, 60).Build());

    _db.DeleteRace(doomed);

    Assert.Null(_db.GetRace(doomed));
    Assert.Empty(_db.RestoreRiderData(doomed));
    Assert.Equal(new[] { survivor }, _db.ListSessions().Select(s => s.Race.Id));
    Assert.Single(_db.RestoreRiderData(survivor));

    // Deleting the current race also stops it being current.
    _db.DeleteRace(survivor);
    Assert.Equal(0, _db.CurrentRaceId);
  }

  [Fact]
  public void DeletingARiderTakesTheirLapsWithThem()
  {
    var id = StartRace();
    _db.UpsertRider(RiderBuilder.Rider("A", "1").Laps(2, 60).Build());
    _db.AddLap("A", new RiderLap { TagID = "A", LapNumber = 1, CrossingTime = Start.AddSeconds(60) }, 1);
    _db.UpsertRider(RiderBuilder.Rider("B", "2").Laps(1, 60).Build());

    _db.DeleteRider("A");

    Assert.Equal(new[] { "B" }, _db.RestoreRiderData(id).Keys);
    Assert.Empty(_db.GetRiderLaps("A"));
  }

  [Fact]
  public void RenamingChangesOnlyTheName()
  {
    var id = StartRace("Moto 1");

    _db.RenameRace(id, "Moto 1 - 250cc");

    var race = _db.GetRace(id)!;
    Assert.Equal("Moto 1 - 250cc", race.Name);
    Assert.Equal(SessionType.Race, race.SessionType);
    Assert.Equal(Start, race.StartTime);
  }

  [Fact]
  public void ClosingTheCurrentRaceStopsFurtherWrites()
  {
    var id = StartRace();
    _db.CloseCurrentRace();

    _db.UpsertRider(RiderBuilder.Rider("A", "1").Laps(2, 60).Build());

    Assert.Equal(0, _db.CurrentRaceId);
    Assert.Empty(_db.RestoreRiderData(id));
  }
}
