using System.Drawing.Imaging;
using Xunit;

namespace CrossMgrInterface.Tests;

public class TileMemoryCacheTests
{
  private static Bitmap Tile() => new(8, 8, PixelFormat.Format32bppPArgb);

  private static bool IsDisposed(Bitmap b)
  {
    try
    {
      _ = b.Width;
      return false;
    }
    catch (Exception)
    {
      return true;
    }
  }

  [Fact]
  public void ATileGoesInAndComesBackOut()
  {
    using var cache = new TileMemoryCache(16);
    var bitmap = Tile();

    cache.Put(new TileId(17, 1, 1), bitmap);

    Assert.True(cache.TryGet(new TileId(17, 1, 1), out var found));
    Assert.Same(bitmap, found);
  }

  [Fact]
  public void ATileThatWasNeverCachedIsSimplyAbsent()
  {
    using var cache = new TileMemoryCache(16);

    Assert.False(cache.TryGet(new TileId(17, 9, 9), out _));
  }

  [Fact]
  public void TheCacheNeverGrowsPastItsCapacity()
  {
    using var cache = new TileMemoryCache(16);

    for (var i = 0; i < 200; i++) cache.Put(new TileId(17, i, 0), Tile());

    Assert.Equal(16, cache.Count);
  }

  [Fact]
  public void EvictingATileDisposesIt()
  {
    // Load-bearing. A 256x256 32bpp bitmap is 256 KiB of UNMANAGED memory behind a
    // 24-byte wrapper, so the GC feels no pressure from it and will not collect in
    // time. Without this, panning reaches gigabytes within minutes.
    using var cache = new TileMemoryCache(8);
    var first = Tile();

    cache.Put(new TileId(17, 0, 0), first);
    for (var i = 1; i <= 8; i++) cache.Put(new TileId(17, i, 0), Tile());

    Assert.False(cache.Contains(new TileId(17, 0, 0)));
    Assert.True(IsDisposed(first), "the evicted bitmap was leaked");
  }

  [Fact]
  public void ReadingATileKeepsItAliveLongerThanOneThatIsIgnored()
  {
    // Least-recently-USED, not least-recently-added: the tile under the operator's
    // cursor must not be evicted just because it was fetched first.
    using var cache = new TileMemoryCache(8);
    var keepMe = new TileId(17, 0, 0);
    var ignored = new TileId(17, 1, 0);

    // Fill it exactly, oldest first.
    for (var i = 0; i < cache.Capacity; i++) cache.Put(new TileId(17, i, 0), Tile());

    // Touch the oldest, then push one more in. The tile that has not been read
    // since it arrived is the one that should go.
    cache.TryGet(keepMe, out _);
    cache.Put(new TileId(17, cache.Capacity, 0), Tile());

    Assert.True(cache.Contains(keepMe), "the recently read tile was evicted");
    Assert.False(cache.Contains(ignored), "the least recently used tile survived");
  }

  [Fact]
  public void ReplacingATileDisposesTheCopyBeingDroppedAndNotTheNewOne()
  {
    using var cache = new TileMemoryCache(8);
    var id = new TileId(17, 5, 5);
    var stale = Tile();
    var fresh = Tile();

    cache.Put(id, stale);
    cache.Put(id, fresh);

    Assert.True(IsDisposed(stale), "the replaced bitmap was leaked");
    Assert.False(IsDisposed(fresh), "the new bitmap was disposed by mistake");
    Assert.Equal(1, cache.Count);
  }

  [Fact]
  public void DisposingTheCacheDisposesEverythingInIt()
  {
    var cache = new TileMemoryCache(8);
    var bitmap = Tile();
    cache.Put(new TileId(17, 1, 1), bitmap);

    cache.Dispose();

    Assert.True(IsDisposed(bitmap));
    Assert.Equal(0, cache.Count);
  }
}

public class TileStoreTests : IDisposable
{
  private readonly string _root;

  public TileStoreTests()
  {
    _root = Path.Combine(Path.GetTempPath(), "CrossMgrTileStoreTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_root);
  }

  public void Dispose()
  {
    try { Directory.Delete(_root, recursive: true); }
    catch (IOException) { /* a locked temp file is not a test failure */ }
  }

  private TileStore Store(string? template = null) =>
    new(template ?? TileProvider.OpenStreetMap.UrlTemplate, _root);

  private static byte[] SmallPng()
  {
    using var bitmap = new Bitmap(4, 4, PixelFormat.Format32bppArgb);
    using var stream = new MemoryStream();
    bitmap.Save(stream, ImageFormat.Png);
    return stream.ToArray();
  }

  [Fact]
  public void TilesAreLaidOutByZoomThenColumnThenRow()
  {
    var path = Store().PathFor(new TileId(17, 68424, 44324));

    Assert.Equal(Path.Combine("17", "68424", "44324.png"), path[(path.IndexOf("17" + Path.DirectorySeparatorChar, StringComparison.Ordinal))..]);
  }

  [Fact]
  public void EachTileServerGetsItsOwnFolder()
  {
    // Without this, switching provider silently serves the previous provider's
    // imagery from disk forever.
    var osm = Store();
    var other = Store("https://tiles.example.org/{z}/{x}/{y}.png");

    Assert.NotEqual(osm.Root, other.Root);
    Assert.Contains("tile_openstreetmap_org", osm.Root);
  }

  [Fact]
  public void AMalformedTemplateStillProducesAUsableFolder()
  {
    var store = Store("not a url at all");

    Assert.False(string.IsNullOrWhiteSpace(store.Root));
    Assert.True(Directory.Exists(store.Root));
  }

  [Fact]
  public void EveryBasemapHasAUsableTemplateAndItsOwnAttribution()
  {
    // Attribution is a condition of using each of these services, not decoration,
    // so a provider without one is a bug rather than a cosmetic omission.
    Assert.NotEmpty(TileProvider.All);

    foreach (var provider in TileProvider.All)
    {
      Assert.False(string.IsNullOrWhiteSpace(provider.Attribution), $"{provider.Id} has no attribution");
      Assert.Contains("{z}", provider.UrlTemplate);
      Assert.Contains("{x}", provider.UrlTemplate);
      Assert.Contains("{y}", provider.UrlTemplate);
      Assert.InRange(provider.MaxZoom, 10, TileMath.MaxZoom);
    }

    Assert.Equal(TileProvider.All.Count, TileProvider.All.Select(p => p.Id).Distinct().Count());
  }

  [Fact]
  public void EachBasemapCachesIntoItsOwnFolder()
  {
    // Without this, switching basemap serves the previous provider's imagery from
    // disk forever - and a satellite tile drawn as a street map is not obviously wrong.
    var roots = TileProvider.All.Select(p => new TileStore(p.UrlTemplate, _root).Root).ToList();

    Assert.Equal(roots.Count, roots.Distinct().Count());
  }

  [Fact]
  public void AnUnknownBasemapIdFallsBackToTheStreetMap()
  {
    Assert.Equal(TileProvider.OpenStreetMap, TileProvider.ById(null));
    Assert.Equal(TileProvider.OpenStreetMap, TileProvider.ById("a provider that was removed"));
  }

  [Fact]
  public void TheSatelliteLayerPutsItsTileCoordinatesInTheOrderEsriExpects()
  {
    // Esri's imagery service is {z}/{y}/{x}, not the usual {z}/{x}/{y}. Token
    // replacement handles either, but getting it backwards silently shows the
    // wrong part of the world - which looks like a projection bug, not a typo.
    var template = TileProvider.Satellite.UrlTemplate;

    Assert.True(template.IndexOf("{y}", StringComparison.Ordinal) <
                template.IndexOf("{x}", StringComparison.Ordinal),
      "the satellite template must be {z}/{y}/{x}");
  }

  [Fact]
  public async Task ATileWrittenIsATileReadBack()
  {
    var store = Store();
    var id = new TileId(16, 3, 4);
    var png = SmallPng();

    await store.WriteAsync(id, png, CancellationToken.None);

    Assert.True(store.Exists(id));
    Assert.Equal(png, await store.ReadAsync(id, CancellationToken.None));
  }

  [Fact]
  public async Task WritingLeavesNoTemporaryFileBehind()
  {
    var store = Store();
    var id = new TileId(16, 3, 4);

    await store.WriteAsync(id, SmallPng(), CancellationToken.None);
    await store.WriteAsync(id, SmallPng(), CancellationToken.None);

    Assert.False(File.Exists(store.PathFor(id) + ".tmp"));
  }

  [Fact]
  public async Task AMissingTileReadsAsNothingRatherThanThrowing()
  {
    Assert.Null(await Store().ReadAsync(new TileId(16, 99, 99), CancellationToken.None));
  }

  [Fact]
  public async Task ATruncatedTileIsTreatedAsAbsentRatherThanPoisoningEveryPaint()
  {
    var store = Store();
    var id = new TileId(16, 7, 7);
    var path = store.PathFor(id);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, Array.Empty<byte>());

    Assert.Null(await store.ReadAsync(id, CancellationToken.None));
  }

  [Fact]
  public void DecodingProducesAPremultipliedBitmapThatOwnsItsOwnPixels()
  {
    // Pre-converting removes a pixel format conversion from every DrawImage, and
    // around fifty tiles are blitted per frame.
    using var decoded = TileStore.Decode(SmallPng());

    Assert.Equal(4, decoded.Width);
    Assert.Equal(PixelFormat.Format32bppPArgb, decoded.PixelFormat);
  }

  [Fact]
  public async Task DecodingDoesNotLockTheFileTheTileCameFrom()
  {
    // Image.FromFile holds the file for the lifetime of the Image, which would
    // leave the cache unable to overwrite or clear its own tiles. Decoding from
    // bytes is what avoids it, so this pins the consequence.
    var store = Store();
    var id = new TileId(16, 2, 2);
    await store.WriteAsync(id, SmallPng(), CancellationToken.None);

    var bytes = await store.ReadAsync(id, CancellationToken.None);
    using var decoded = TileStore.Decode(bytes!);

    store.Clear();

    Assert.False(store.Exists(id));
    Assert.Equal(4, decoded.Width);
  }

  [Fact]
  public async Task TheCacheCanReportHowMuchOfATrackIsAlreadyDownloaded()
  {
    var store = Store();
    var wanted = new[] { new TileId(16, 1, 1), new TileId(16, 1, 2), new TileId(16, 1, 3) };

    await store.WriteAsync(wanted[0], SmallPng(), CancellationToken.None);

    Assert.Equal(1, store.CountCached(wanted));
    Assert.True(store.GetCacheSizeBytes() > 0);
  }
}
