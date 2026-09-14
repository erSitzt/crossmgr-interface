using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// What a team event needs to survive a restart or a reprint: the flag on the
/// race, each team's riders, and which rider's transponder ended each lap.
/// </summary>
public sealed class TeamPersistenceTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), $"crossmgr-team-test-{Guid.NewGuid():N}.db");
  private readonly RaceDataService _db;

  public TeamPersistenceTests()
  {
    _db = new RaceDataService(_path);
  }

  public void Dispose()
  {
    _db.Dispose();
    try { File.Delete(_path); } catch (IOException) { }
    try { File.Delete(Path.ChangeExtension(_path, null) + "-log.db"); } catch (IOException) { }
  }

  private static RaceRules Rules(bool teamEvent) => new()
  {
    SessionType = SessionType.Race,
    Duration = TimeSpan.FromMinutes(20),
    AdditionalLaps = 1,
    DnfTimeoutMinutes = 2,
    MinimumLapSeconds = 10,
    ManualStart = true,
    TeamEvent = teamEvent
  };

  private int StartRace(bool teamEvent = true) =>
    _db.StartNewRace(RiderBuilder.RaceStart, TimeSpan.FromMinutes(20), "Team race", SessionType.Race, Rules(teamEvent));

  private static RiderInfo Adler() => RiderBuilder.Team("MSC Adler",
      RiderBuilder.Member("11", "Anna Berger", "A01", "SPARE"),
      RiderBuilder.Member("14", "Ben Fischer", "A02"))
    .Category("MX1")
    .LapBy(40, "A01").LapBy(40, "A01").LapBy(12, "A02").LapBy(45, "A02")
    .Build();

  [Fact]
  public void TheTeamEventFlagIsStoredWithTheRace()
  {
    var id = StartRace();

    Assert.True(RaceRules.FromRace(_db.GetRace(id)!).TeamEvent);
  }

  [Fact]
  public void ARaceStoredBeforeTeamsExistedIsNotATeamEvent()
  {
    var id = StartRace(teamEvent: false);
    var race = _db.GetRace(id)!;
    race.TeamEvent = null;

    Assert.False(RaceRules.FromRace(race).TeamEvent);
  }

  [Fact]
  public void ATeamEntryComesBackWithItsRiders()
  {
    var id = StartRace();
    var team = Adler();
    _db.UpsertRider(team);

    var restored = _db.RestoreRiderData(id)[team.TagID];

    Assert.True(restored.IsTeam);
    Assert.Equal("#11/14 MSC Adler", restored.Label);
    Assert.Equal(2, restored.Members!.Count);
    Assert.Equal(new[] { "A01", "SPARE" }, restored.Members[0].Transponders);
    Assert.Equal("Ben", restored.Members[1].FirstName);
  }

  [Fact]
  public void ASoloRiderComesBackWithoutMembers()
  {
    var id = StartRace();
    _db.UpsertRider(RiderBuilder.Rider("S01", "41", "Greta Lang").Lap(40).Build());

    Assert.False(_db.RestoreRiderData(id)["S01"].IsTeam);
  }

  [Fact]
  public void LapsAddedOneByOneComeBackWithWhoRodeThemAndTheirWarnings()
  {
    var id = StartRace();
    var team = Adler();
    team.Laps[2].IsSuspectedOverlap = true;
    team.Laps[3].IsSuspectedOverlap = true;
    team.Laps[3].OverlapDismissed = true;

    _db.UpsertRider(team);
    foreach (var lap in team.Laps)
      _db.AddLap(team.TagID, lap, 1);

    AssertLapsMatch(team, _db.RestoreRiderData(id)[team.TagID]);
  }

  [Fact]
  public void LapsReplacedByACorrectionComeBackWithWhoRodeThemAndTheirWarnings()
  {
    var id = StartRace();
    var team = Adler();
    team.Laps[2].IsSuspectedOverlap = true;

    _db.UpsertRider(team);
    _db.ReplaceRiderLaps(team.TagID, team.Laps, _ => 1);

    AssertLapsMatch(team, _db.RestoreRiderData(id)[team.TagID]);
  }

  private static void AssertLapsMatch(RiderInfo expected, RiderInfo actual)
  {
    Assert.Equal(expected.Laps.Count, actual.Laps.Count);
    for (var i = 0; i < expected.Laps.Count; i++)
    {
      Assert.Equal(expected.Laps[i].CrossedBy, actual.Laps[i].CrossedBy);
      Assert.Equal(expected.Laps[i].IsSuspectedOverlap, actual.Laps[i].IsSuspectedOverlap);
      Assert.Equal(expected.Laps[i].OverlapDismissed, actual.Laps[i].OverlapDismissed);
    }
  }
}
