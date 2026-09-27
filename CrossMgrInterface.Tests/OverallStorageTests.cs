using System.Text.Json;
using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// An overall as stored, and as the website is told it: which motos count
/// towards it, what happens when one of them goes, and what is sent.
/// </summary>
public sealed class OverallStorageTests : IDisposable
{
  private readonly string _path = Path.Combine(Path.GetTempPath(), $"crossmgr-test-{Guid.NewGuid():N}.db");
  private readonly RaceDataService _db;

  public OverallStorageTests()
  {
    _db = new RaceDataService(_path);
  }

  public void Dispose()
  {
    _db.Dispose();
    try { File.Delete(_path); } catch (IOException) { }
    try { File.Delete(Path.ChangeExtension(_path, null) + "-log.db"); } catch (IOException) { }
  }

  private int StartRace(string name) =>
    _db.StartNewRace(RiderBuilder.RaceStart, TimeSpan.FromMinutes(20), name);

  [Fact]
  public void AnOverallKeepsItsMotosInOrderAndPastSessionsNamesIt()
  {
    var moto1 = StartRace("Moto 1");
    var moto2 = StartRace("Moto 2");
    var practice = StartRace("Practice");

    var overall = _db.CreateOverall("Overall - MX2", new[] { moto1 });
    _db.AddRaceToOverall(overall.Id, moto2);

    Assert.Equal(new[] { moto1, moto2 }, _db.GetOverall(overall.Id)!.RaceIds);
    Assert.Equal(32, overall.PublicId.Length);
    Assert.Equal(overall.Id, _db.OverallFor(moto2)!.Id);
    Assert.Null(_db.OverallFor(practice));

    var sessions = _db.ListSessions();
    Assert.Equal("Overall - MX2", sessions.Single(s => s.Race.Id == moto1).Overall);
    Assert.Null(sessions.Single(s => s.Race.Id == practice).Overall);
  }

  [Fact]
  public void AMotoCountsTowardsOneOverallOnly()
  {
    var moto1 = StartRace("Moto 1");
    var first = _db.CreateOverall("First", new[] { moto1 });
    var second = _db.CreateOverall("Second", Array.Empty<int>());

    _db.AddRaceToOverall(second.Id, moto1);

    Assert.Empty(_db.GetOverall(first.Id)!.RaceIds);
    Assert.Equal(second.Id, _db.OverallFor(moto1)!.Id);
  }

  [Fact]
  public void DeletingAMotoTakesItOutOfItsOverallAndAnEmptyOverallGoes()
  {
    var moto1 = StartRace("Moto 1");
    var moto2 = StartRace("Moto 2");
    var overall = _db.CreateOverall("Overall", new[] { moto1, moto2 });

    _db.DeleteRace(moto1);
    Assert.Equal(new[] { moto2 }, _db.GetOverall(overall.Id)!.RaceIds);

    _db.DeleteRace(moto2);
    Assert.Null(_db.GetOverall(overall.Id));
  }

  [Fact]
  public void DeletingAnOverallLeavesItsMotos()
  {
    var moto1 = StartRace("Moto 1");
    var overall = _db.CreateOverall("Overall", new[] { moto1 });

    _db.DeleteOverall(overall.Id);

    Assert.NotNull(_db.GetRace(moto1));
    Assert.Null(_db.OverallFor(moto1));
  }

  [Fact]
  public void TheClubsPointsTableIsKeptAndTheDefaultIsFim()
  {
    var overall = _db.CreateOverall("Overall", Array.Empty<int>());
    Assert.True(OverallRules.FromOverall(overall).IsFim);

    overall.PointsTable = new List<int> { 20, 17, 15 };
    _db.UpdateOverall(overall);

    var stored = OverallRules.FromOverall(_db.GetOverall(overall.Id)!);
    Assert.Equal(new[] { 20, 17, 15 }, stored.Points);
    Assert.Equal(15, stored.PointsFor(3));
    Assert.Equal(0, stored.PointsFor(4));
  }

  [Theory]
  [InlineData("Moto 1 - MX2", "Overall - MX2")]
  [InlineData("Lauf 2: Jugend", "Overall - Jugend")]
  [InlineData("Moto 1", "Overall - 06.08.2025")]
  [InlineData("", "Overall - 06.08.2025")]
  [InlineData("Club championship", "Overall - Club championship")]
  public void AnOverallIsNamedAfterItsFirstMoto(string moto, string expected)
  {
    Assert.Equal(expected, Form1.OverallNameFor(moto, RiderBuilder.RaceStart));
  }

  // ---- what the website is told ------------------------------------------

  private static OverallResult TwoMotos()
  {
    OverallMoto Moto(string title, params RiderInfo[] riders) =>
      new(title, riders.ToDictionary(r => r.TagID, r => r), new RaceRules { Duration = TimeSpan.FromMinutes(20) });

    var hidden = RiderBuilder.Rider("B", "2", "Ben Fischer").Laps(10, 41).Build();
    hidden.ShowName = false;

    return OverallScorer.Score("Overall - MX2", new[]
    {
      Moto("Moto 1", RiderBuilder.Rider("A", "1", "Anna Berger").Laps(10, 40).Build(), hidden),
      Moto("Moto 2", RiderBuilder.Rider("A2", "1", "Anna Berger").Laps(10, 40).Build())
    }, new OverallRules());
  }

  [Fact]
  public void TheWebsiteIsToldPlacesPointsAndMotosButNoTransponder()
  {
    var payload = PublishPayloadBuilder.BuildOverall(TwoMotos(), "0123456789abcdef0123456789abcdef",
      new[] { "moto1id000000000", "moto2id000000000" }, clientVersion: "v1.0.0");
    var json = PublishPayloadBuilder.Serialise(payload);

    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;
    Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
    Assert.Equal("0123456789abcdef0123456789abcdef", root.GetProperty("overall").GetProperty("publicId").GetString());
    Assert.True(root.GetProperty("overall").GetProperty("final").GetBoolean());
    Assert.Equal(25, root.GetProperty("overall").GetProperty("pointsTable")[0].GetInt32());
    Assert.Equal(2, root.GetProperty("motos").GetArrayLength());

    var anna = root.GetProperty("classes")[0].GetProperty("entries")[0];
    Assert.Equal(1, anna.GetProperty("rank").GetInt32());
    Assert.Equal(50, anna.GetProperty("points").GetInt32());
    Assert.Equal("1", anna.GetProperty("motos")[1].GetProperty("result").GetString());

    Assert.DoesNotContain("\"A2\"", json);
    Assert.DoesNotContain("tag", json, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void ARiderWhoAskedNotToBeNamedIsShortenedInTheOverallToo()
  {
    var payload = PublishPayloadBuilder.BuildOverall(TwoMotos(), "0123456789abcdef0123456789abcdef",
      new[] { "moto1id000000000", "moto2id000000000" }, publishNamesByDefault: true,
      hiddenNameStyle: NameStyle.FirstNameInitial);

    var ben = payload.Classes[0].Entries.Single(e => e.Number == "2");
    Assert.Equal("Ben F.", ben.Name);
    Assert.Equal(new int?[] { 2, null }, ben.Motos.Select(m => m.Position));
    Assert.Equal("-", ben.Motos[1].Result);
  }

  [Fact]
  public void AnOverallWithAMotoStillRunningIsNotFinal()
  {
    var result = OverallScorer.Score("Overall", new[]
    {
      new OverallMoto("Moto 1", new Dictionary<string, RiderInfo>(), new RaceRules(), Finished: false)
    }, new OverallRules());

    var payload = PublishPayloadBuilder.BuildOverall(result, "0123456789abcdef0123456789abcdef", new[] { "m1" });
    Assert.False(payload.Overall.Final);
  }

  [Fact]
  public void TheExcelWorkbookHasASheetPerClass()
  {
    var result = TwoMotos();
    using var workbook = OverallReportGenerator.BuildWorkbook(result, "Overall - MX2");

    var sheet = Assert.Single(workbook.Worksheets);
    Assert.Equal("No class", sheet.Name);
    Assert.Contains(sheet.CellsUsed(), c => c.GetString() == "Total");
    Assert.Contains(sheet.CellsUsed(), c => c.GetString() == "Anna Berger");
  }
}
