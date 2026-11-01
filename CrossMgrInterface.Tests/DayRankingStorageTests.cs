using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>The list of every class of a day as stored: one per day, its races whatever finished.</summary>
public sealed class DayRankingStorageTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), $"crossmgr-test-{Guid.NewGuid():N}.db");
  private readonly RaceDataService _db;

  public DayRankingStorageTests()
  {
    _db = new RaceDataService(_path);
  }

  public void Dispose()
  {
    _db.Dispose();
    try { File.Delete(_path); } catch (IOException) { }
    try { File.Delete(Path.ChangeExtension(_path, null) + "-log.db"); } catch (IOException) { }
  }

  private int Session(string name, DateTime start, SessionType type = SessionType.Race, bool finished = true)
  {
    var id = _db.StartNewRace(start, TimeSpan.FromMinutes(15), name, type);
    if (finished) _db.UpdateRace(r => r.IsFinished = true);
    return id;
  }

  [Fact]
  public void TheDayCountsItsFinishedRacesOnly()
  {
    var day = new DateTime(2026, 10, 31, 9, 0, 0);
    Session("Practice", day, SessionType.FreePractice);
    var moto1 = Session("Moto 1 - Hobby", day.AddHours(3));
    var moto2 = Session("Moto 1 - Lizenz", day.AddHours(4));
    Session("Moto 2 - Hobby", day.AddHours(5), finished: false);
    Session("Next day", day.AddDays(1));

    Assert.Equal(new[] { moto1, moto2 }, _db.RacesOfDay(day.Date).Select(r => r.Id));
  }

  [Fact]
  public void OneListPerDayKeepsItsAddress()
  {
    var day = new DateTime(2026, 10, 31);
    var first = _db.EnsureDayRanking(day, "Master of all classes", DayRankingMeasure.BestLap);
    var again = _db.EnsureDayRanking(day.AddHours(15), "Something else", DayRankingMeasure.AverageLap);

    Assert.Equal((first.Id, first.PublicId), (again.Id, again.PublicId));
    Assert.Equal(("Master of all classes", DayRankingMeasure.BestLap), (again.Name, again.Measure));
    Assert.NotEqual(first.PublicId, _db.EnsureDayRanking(day.AddDays(1), "x", DayRankingMeasure.BestLap).PublicId);

    again.Measure = DayRankingMeasure.AverageLap;
    _db.UpdateDayRanking(again);
    _db.SetDayRankingStamps(again.Id, published: "41:1|bestLap");
    Assert.Equal((DayRankingMeasure.AverageLap, "41:1|bestLap"),
      (_db.GetDayRanking(day)!.Measure, _db.GetDayRanking(day)!.PublishedStamp));
  }
}
