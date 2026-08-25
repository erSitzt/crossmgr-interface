using Xunit;

namespace CrossMgrInterface.Tests;

public class TrackStoreTests : IDisposable
{
  private readonly string _folder;
  private readonly string _path;

  public TrackStoreTests()
  {
    _folder = Path.Combine(Path.GetTempPath(), "CrossMgrTrackStoreTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_folder);
    _path = Path.Combine(_folder, "tracks.json");
  }

  public void Dispose()
  {
    try { Directory.Delete(_folder, recursive: true); }
    catch (IOException) { /* a locked temp file is not a test failure */ }
  }

  [Fact]
  public void ATrackSurvivesTheRoundTripThroughJson()
  {
    var track = TrackBuilder.SquareWithFinishOnTheSouthSide();
    track.Name = "Steinbergpark";
    track.Notes = "Gravel through sector 2";
    track.AddSector("The Climb", TrackBuilder.Ne);
    track.AddSector("Back Straight", TrackBuilder.Sw);

    var store = TrackStore.Load(_path);
    store.AddOrUpdate(track);
    store.Save();

    var reloaded = TrackStore.Load(_path).Find(track.Id);

    Assert.NotNull(reloaded);
    Assert.Equal("Steinbergpark", reloaded!.Name);
    Assert.Equal("Gravel through sector 2", reloaded.Notes);
    Assert.Equal(track.Points.Count, reloaded.Points.Count);
    Assert.Equal(track.StartFinish.Fraction, reloaded.StartFinish.Fraction, 9);
    Assert.Equal(2, reloaded.Sectors.Count);
    Assert.Equal(new[] { "The Climb", "Back Straight" }, reloaded.Sectors.Select(s => s.Name));
    Assert.Equal(track.Sectors[0].ColorArgb, reloaded.Sectors[0].ColorArgb);
  }

  [Fact]
  public void TheCoordinatesThemselvesSurviveToTheMillimetre()
  {
    // The whole file is a few hundred of these. A round trip that quietly loses
    // precision would move every rider dot.
    var track = TrackBuilder.Square();

    var store = TrackStore.Load(_path);
    store.AddOrUpdate(track);
    store.Save();

    var reloaded = TrackStore.Load(_path).Find(track.Id)!;

    for (var i = 0; i < track.Points.Count; i++)
      Assert.True(TrackBuilder.Metres(track.Points[i], reloaded.Points[i]) < 0.001,
        $"point {i} moved on the round trip");
  }

  [Fact]
  public void ReloadingRebuildsTheGeometryRatherThanSerialisingIt()
  {
    var track = TrackBuilder.Square();
    var store = TrackStore.Load(_path);
    store.AddOrUpdate(track);
    store.Save();

    var json = File.ReadAllText(_path);
    Assert.DoesNotContain("Geometry", json);
    Assert.DoesNotContain("TotalLengthMetres", json);

    var reloaded = TrackStore.Load(_path).Find(track.Id)!;
    Assert.True(reloaded.IsUsable);
    Assert.Equal(track.LengthMetres, reloaded.LengthMetres, 3);
  }

  [Fact]
  public void AnUnreadableFileIsSetAsideRatherThanCrashingTheApplication()
  {
    // Matches AppSettings.Load's contract: the application must start regardless.
    File.WriteAllText(_path, "{ this is not json");

    var store = TrackStore.Load(_path);

    Assert.Empty(store.Tracks);
    Assert.NotNull(store.RecoveredFrom);
    Assert.True(File.Exists(store.RecoveredFrom!),
      "the damaged file must be kept - it is the only copy of work someone did");
    Assert.False(File.Exists(_path));
  }

  [Fact]
  public void AMissingFileIsSimplyAnEmptyStore()
  {
    var store = TrackStore.Load(Path.Combine(_folder, "never-written.json"));

    Assert.Empty(store.Tracks);
    Assert.Null(store.RecoveredFrom);
  }

  [Fact]
  public void SavingLeavesNoTemporaryFileBehind()
  {
    var store = TrackStore.Load(_path);
    store.AddOrUpdate(TrackBuilder.Square());
    store.Save();
    store.Save();

    Assert.False(File.Exists(_path + ".tmp"));
    Assert.Single(Directory.GetFiles(_folder));
  }

  [Fact]
  public void SavingTheSameTrackTwiceUpdatesItRatherThanDuplicatingIt()
  {
    var track = TrackBuilder.Square();
    var store = TrackStore.Load(_path);

    store.AddOrUpdate(track);
    track.Name = "Renamed";
    store.AddOrUpdate(track);
    store.Save();

    var reloaded = TrackStore.Load(_path);
    Assert.Single(reloaded.Tracks);
    Assert.Equal("Renamed", reloaded.Tracks[0].Name);
  }

  [Fact]
  public void ATrackCanBeRemoved()
  {
    var track = TrackBuilder.Square();
    var store = TrackStore.Load(_path);
    store.AddOrUpdate(track);

    Assert.True(store.Remove(track.Id));
    Assert.False(store.Remove(track.Id));
    Assert.Empty(store.Tracks);
  }

  [Fact]
  public void TheOnlyTrackIsOfferedAutomaticallyButAChoiceOfTwoIsNot()
  {
    var store = TrackStore.Load(_path);
    Assert.Null(store.Only);

    store.AddOrUpdate(TrackBuilder.Square());
    Assert.NotNull(store.Only);

    var second = TrackBuilder.Square("Another");
    second.Id = Guid.NewGuid().ToString("N");
    store.AddOrUpdate(second);
    Assert.Null(store.Only);
  }

  [Fact]
  public void ASingleTrackCanBeExportedAndImportedOnAnotherMachine()
  {
    var track = TrackBuilder.SquareWithFinishOnTheSouthSide();
    track.Name = "Shared circuit";
    track.AddSector("The Climb", TrackBuilder.Ne);

    var imported = TrackStore.ImportJson(TrackStore.ExportJson(track));

    Assert.NotNull(imported);
    Assert.Equal("Shared circuit", imported!.Name);
    Assert.Equal(track.Id, imported.Id);
    Assert.Equal(track.Points.Count, imported.Points.Count);
    Assert.Single(imported.Sectors);
    Assert.True(imported.IsUsable);
  }

  [Fact]
  public void ImportingRubbishReturnsNothingRatherThanThrowing()
  {
    Assert.Null(TrackStore.ImportJson("not json at all"));
  }
}
