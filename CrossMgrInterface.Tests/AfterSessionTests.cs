using ClosedXML.Excel;
using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// What happens by itself after a session: what the outbox does next, how a
/// correction made after publishing is noticed, and where the sheets go.
/// </summary>
public sealed class AfterSessionTests : IDisposable
{
  private static readonly DateTime Day = new(2026, 10, 18);
  private readonly string _path = Path.Combine(Path.GetTempPath(), $"crossmgr-test-{Guid.NewGuid():N}.db");
  private readonly string _folder = Path.Combine(Path.GetTempPath(), $"crossmgr-sheets-{Guid.NewGuid():N}");
  private readonly Lazy<RaceDataService> _db;

  public AfterSessionTests() => _db = new Lazy<RaceDataService>(() => new RaceDataService(_path));

  public void Dispose()
  {
    if (_db.IsValueCreated) _db.Value.Dispose();
    try { File.Delete(_path); } catch (IOException) { }
    try { File.Delete(Path.ChangeExtension(_path, null) + "-log.db"); } catch (IOException) { }
    try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
  }

  private static DbRace Race(int id, int startHour, bool publish = true, bool save = true, int revision = 1,
    int? published = null, int? saved = null) => new()
  {
    Id = id,
    Name = $"Moto {id}",
    StartTime = Day.AddHours(startHour),
    EndTime = Day.AddHours(startHour).AddMinutes(20),
    IsFinished = true,
    AutoPublish = publish,
    AutoSaveSheets = save,
    Revision = revision,
    PublishedRevision = published,
    PublishedAt = published != null ? Day : null,
    SheetsRevision = saved
  };

  private static readonly IReadOnlySet<string> NothingParked = new HashSet<string>();

  // ---- the order ------------------------------------------------------------

  [Fact]
  public void SheetsAreSavedBeforeAnythingGoesToTheWebsite()
  {
    var races = new[] { Race(1, 10), Race(2, 11) };
    var next = OutboxPolicy.Next(races, Array.Empty<OverallCandidate>(), Day.AddHours(12), NothingParked, canPublish: true);
    Assert.Equal(new OutboxItem(OutboxWork.SaveSheets, 1, 1), next);
  }

  [Fact]
  public void WithoutTheInternetTheSheetsAreStillSaved()
  {
    var races = new[] { Race(1, 10, saved: 1), Race(2, 11) };
    var next = OutboxPolicy.Next(races, Array.Empty<OverallCandidate>(), Day.AddHours(12), NothingParked, canPublish: false);
    Assert.Equal(OutboxWork.SaveSheets, next!.Work);
    Assert.Equal(2, next.Id);

    var allSaved = new[] { Race(1, 10, saved: 1), Race(2, 11, saved: 1) };
    Assert.Null(OutboxPolicy.Next(allSaved, Array.Empty<OverallCandidate>(), Day.AddHours(12), NothingParked, false));
  }

  [Fact]
  public void ASessionIsPublishedTwoMinutesAfterItsFinishOldestFirst()
  {
    var races = new[] { Race(2, 11, saved: 1), Race(1, 10, saved: 1) };
    var finish2 = races[0].EndTime!.Value;

    var next = OutboxPolicy.Next(races, Array.Empty<OverallCandidate>(), finish2.AddMinutes(1), NothingParked, true);
    Assert.Equal(new OutboxItem(OutboxWork.PublishSession, 1, 1), next);

    var onlyTheLatest = new[] { races[0] };
    Assert.Null(OutboxPolicy.Next(onlyTheLatest, Array.Empty<OverallCandidate>(), finish2.AddMinutes(1), NothingParked, true));
    Assert.NotNull(OutboxPolicy.Next(onlyTheLatest, Array.Empty<OverallCandidate>(), finish2.AddMinutes(2), NothingParked, true));
  }

  [Fact]
  public void ACorrectionAfterPublishingIsPublishedAgain()
  {
    var corrected = Race(1, 10, revision: 2, published: 1, saved: 2);
    var next = OutboxPolicy.Next(new[] { corrected }, Array.Empty<OverallCandidate>(), Day.AddHours(12), NothingParked, true);
    Assert.Equal(new OutboxItem(OutboxWork.PublishSession, 1, 2), next);
  }

  [Fact]
  public void AnOverallWaitsUntilAllItsMotosAreUpInTheirLatestForm()
  {
    var moto1 = Race(1, 10, published: 1, saved: 1);
    var moto2 = Race(2, 11, revision: 2, published: 1, saved: 2);
    var overall = new OverallCandidate(7, new[] { moto1, moto2 }, Complete: true, "stamp", null, "stamp");
    var now = Day.AddHours(13);

    Assert.Equal(OutboxWork.PublishSession, OutboxPolicy.Next(new[] { moto1, moto2 }, new[] { overall }, now, NothingParked, true)!.Work);

    moto2.PublishedRevision = 2;
    Assert.Equal(new OutboxItem(OutboxWork.PublishOverall, 7, 0, "stamp"),
      OutboxPolicy.Next(new[] { moto1, moto2 }, new[] { overall }, now, NothingParked, true));

    var notComplete = overall with { Complete = false };
    Assert.Null(OutboxPolicy.Next(new[] { moto1, moto2 }, new[] { notComplete }, now, NothingParked, true));

    var alreadyUp = overall with { PublishedStamp = "stamp" };
    Assert.Null(OutboxPolicy.Next(new[] { moto1, moto2 }, new[] { alreadyUp }, now, NothingParked, true));
  }

  [Fact]
  public void SomethingTheWebsiteRefusedIsParkedUntilTheSessionChanges()
  {
    var race = Race(1, 10, saved: 1);
    var parked = new HashSet<string> { new OutboxItem(OutboxWork.PublishSession, 1, 1).Key };
    Assert.Null(OutboxPolicy.Next(new[] { race }, Array.Empty<OverallCandidate>(), Day.AddHours(12), parked, true));

    race.Revision = 2;
    Assert.NotNull(OutboxPolicy.Next(new[] { race }, Array.Empty<OverallCandidate>(), Day.AddHours(12), parked, true));
  }

  [Fact]
  public void TheWaitAfterNoAnswerGrowsToFiveMinutes()
  {
    Assert.Equal(TimeSpan.FromSeconds(30), OutboxPolicy.Backoff(1));
    Assert.Equal(TimeSpan.FromSeconds(60), OutboxPolicy.Backoff(2));
    Assert.Equal(TimeSpan.FromMinutes(5), OutboxPolicy.Backoff(9));
  }

  // ---- the race row ---------------------------------------------------------

  [Fact]
  public void TheRaceRowRecordsWhatIsToDoAndWhatIsDone()
  {
    var db = _db.Value;
    var id = db.StartNewRace(Day.AddHours(10), TimeSpan.FromMinutes(20), "Moto 1");
    var old = db.StartNewRace(Day.AddHours(9), TimeSpan.FromMinutes(20), "Before the switch");
    foreach (var raceId in new[] { id, old })
    {
      db.SetCurrentRace(raceId);
      db.UpdateRace(r => r.IsFinished = true);
    }

    // Changed before the finish is recorded: nothing to publish yet.
    Assert.Empty(db.RacesWithWorkToDo());

    db.MarkFinished(id, publish: true, saveSheets: true, pdf: false, folder: @"C:\x");
    Assert.Equal(new[] { id }, db.RacesWithWorkToDo().Select(r => r.Id));

    db.MarkSheetsSaved(id, 1);
    db.MarkPublished(id, Day, "https://x/races/moto-1", revision: 1);
    Assert.Empty(db.RacesWithWorkToDo());

    // A correction made while the upload was on its way still counts.
    db.MarkChanged(id);
    var race = db.GetRace(id)!;
    Assert.Equal(2, race.Revision);
    Assert.Equal(1, race.PublishedRevision);
    Assert.Single(db.RacesWithWorkToDo());
    Assert.Equal(@"C:\x", race.SheetsFolder);
  }

  [Fact]
  public void PublishingByHandCountsToo()
  {
    var db = _db.Value;
    var id = db.StartNewRace(Day.AddHours(10), TimeSpan.FromMinutes(20), "Moto 1");
    db.SetCurrentRace(id);
    db.UpdateRace(r => r.IsFinished = true);
    db.MarkFinished(id, publish: true, saveSheets: false, pdf: false, folder: null);

    db.MarkPublished(id, Day, "https://x");
    Assert.Empty(db.RacesWithWorkToDo());
  }

  // ---- the sheets ------------------------------------------------------------

  [Fact]
  public void TheSheetsGoIntoAFolderPerDayAndPerSession()
  {
    var day = SheetSaver.DayFolder(@"D:\Results", Day, "Clubrennen");
    Assert.Equal(@"D:\Results\2026-10-18 Clubrennen", day);
    Assert.Equal(@"D:\Results\2026-10-18 Clubrennen\03 Moto 1 - MX1 _ MX2",
      SheetSaver.SessionFolder(day, 3, "Moto 1 - MX1 / MX2"));
    Assert.EndsWith(@"CrossMgr Results\2026-10-18", SheetSaver.DayFolder(null, Day, null));
  }

  [Fact]
  public void AFileOpenElsewhereIsNotOverwrittenButSavedBesideIt()
  {
    Directory.CreateDirectory(_folder);
    var first = Path.Combine(_folder, "Results.xlsx");
    File.WriteAllText(first, "old");

    Assert.Equal(first, SheetSaver.WritablePath(_folder, "Results.xlsx"));

    using (File.Open(first, FileMode.Open, FileAccess.Read, FileShare.Read))
      Assert.Equal(Path.Combine(_folder, "Results (2).xlsx"), SheetSaver.WritablePath(_folder, "Results.xlsx"));
  }

  [Fact]
  public void ASessionsSheetsAreSavedWithoutAQuestionOnePerClass()
  {
    var riders = new[]
    {
      RiderBuilder.Rider("A", "1", "Anna Berger").Laps(5, 40).Category("MX1").Build(),
      RiderBuilder.Rider("B", "2", "Ben Fischer").Laps(5, 41).Category("MX2").Build()
    }.ToDictionary(r => r.TagID);

    var generator = new RaceReportGenerator();
    try
    {
      var files = generator.SaveSheets(riders, RiderBuilder.RaceStart, RiderBuilder.RaceStart.AddMinutes(20),
        TimeSpan.FromMinutes(20), true, "Moto 1", null, null, 1, new RaceRules(), _folder, pdf: false);

      Assert.Equal(new[] { "Class MX1.xlsx", "Class MX2.xlsx", "Results.xlsx" },
        files.Select(Path.GetFileName).OrderBy(n => n));
      using var workbook = new XLWorkbook(files.First(f => f.EndsWith("Results.xlsx")));
      Assert.Contains(workbook.Worksheets.First().CellsUsed(), c => c.GetString() == "Anna Berger");
    }
    finally
    {
      generator.Dispose();
    }
  }

  [Fact]
  public void APlanSwitchesEverythingOnAndACopyKeepsItsSwitches()
  {
    var plan = new DayPlan { Name = "Clubrennen", Date = Day };
    Assert.True(plan.After.Publish && plan.After.SaveSheets && plan.After.Pdf && plan.After.LiveTiming);

    plan.After.LiveTiming = false;
    plan.After.Folder = @"D:\Results";
    var copy = plan.CopyFor(Day.AddDays(7));
    Assert.False(copy.After.LiveTiming);
    Assert.Equal(@"D:\Results", copy.After.Folder);
    Assert.NotSame(plan.After, copy.After);

    Assert.False(new AppSettings().AfterSession.AnyOn);
  }
}
