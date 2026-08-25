namespace CrossMgrInterface;

public readonly record struct PrefetchEstimate(int TileCount, long EstimatedBytes, int[] PerZoom)
{
  public string Describe() => TileCount == 0
    ? "Nothing to download."
    : $"{TileCount:N0} tiles, about {EstimatedBytes / 1024.0 / 1024.0:F0} MB.";
}

public readonly record struct PrefetchProgress(
  int Completed, int Total, int Downloaded, int AlreadyCached, int Failed, int CurrentZoom);

public sealed record PrefetchResult(
  int Downloaded, int AlreadyCached, int Failed, bool Cancelled)
{
  public string Describe()
  {
    if (Cancelled) return $"Stopped. {Downloaded:N0} tiles downloaded before cancelling.";

    var text = $"{Downloaded:N0} tiles downloaded";
    if (AlreadyCached > 0) text += $", {AlreadyCached:N0} already on disk";
    if (Failed > 0) text += $", {Failed:N0} could not be fetched";
    return text + ".";
  }
}

/// <summary>
/// Downloads every tile covering a circuit, so the map still works on a field
/// with no usable internet - which is most of them.
///
/// This is the one operation that deliberately fetches tiles nobody has asked to
/// look at, so it is also the one that has to be careful about the tile usage
/// policy. Three things keep it on the right side of it:
///
///   - It shares TileFetcher's two-connection semaphore, so the interactive map
///     always wins the race for a slot and a background download can never
///     starve the view the operator is actually looking at.
///   - A hundred milliseconds between requests on top of that.
///   - A hard cap. A circuit-sized box over the default zooms is a couple of
///     hundred tiles - two orders of magnitude below anything the policy would
///     call bulk downloading - and the cap makes it impossible to accidentally
///     ask for a city.
/// </summary>
public sealed class MapTilePrefetcher
{
  /// <summary>Refuses outright above this. z19 across a wide box is where this would
  /// stop being a one-off and start being scraping.</summary>
  public const int HardTileLimit = 5000;

  /// <summary>
  /// z18 mostly adds building detail nobody reads at circuit scale, so it is
  /// opt-in with a visible tile count rather than on by default.
  /// </summary>
  public const int DefaultMinZoom = 14;
  public const int DefaultMaxZoom = 17;

  /// <summary>Typical OSM raster tile. Only used to quote a size before starting.</summary>
  private const int AverageTileBytes = 22_000;

  private const int SpacingMs = 100;

  private readonly TileStore _store;
  private readonly TileFetcher _fetcher;

  public MapTilePrefetcher(TileStore store, TileFetcher fetcher)
  {
    _store = store;
    _fetcher = fetcher;
  }

  /// <summary>
  /// Cheap and synchronous, so the dialog can requote on every spinner change
  /// before anything is downloaded.
  /// </summary>
  public static PrefetchEstimate Estimate(GeoBounds bounds, int minZoom, int maxZoom)
  {
    if (bounds.IsEmpty) return new PrefetchEstimate(0, 0, Array.Empty<int>());

    var perZoom = new List<int>();
    var total = 0;

    for (var z = Math.Max(TileMath.MinZoom, minZoom); z <= Math.Min(TileMath.MaxZoom, maxZoom); z++)
    {
      var count = TileMath.RangeFor(bounds, z).Count;
      perZoom.Add(count);
      total += count;
    }

    return new PrefetchEstimate(total, (long)total * AverageTileBytes, perZoom.ToArray());
  }

  /// <summary>How much of this box is already on disk. Drives the "63% cached" hint.</summary>
  public int CountCached(GeoBounds bounds, int minZoom, int maxZoom)
  {
    var cached = 0;

    for (var z = Math.Max(TileMath.MinZoom, minZoom); z <= Math.Min(TileMath.MaxZoom, maxZoom); z++)
      cached += _store.CountCached(TileMath.RangeFor(bounds, z).Tiles());

    return cached;
  }

  /// <summary>
  /// Downloads the box. Never throws for a network reason - failures are counted
  /// into the result - and a cancellation is a normal outcome, not an exception,
  /// because someone pressing Cancel has not done anything wrong.
  /// </summary>
  public async Task<PrefetchResult> DownloadAsync(
    GeoBounds bounds, int minZoom, int maxZoom,
    IProgress<PrefetchProgress>? progress, CancellationToken cancellationToken)
  {
    var estimate = Estimate(bounds, minZoom, maxZoom);

    if (estimate.TileCount > HardTileLimit)
      throw new InvalidOperationException(
        $"That area needs {estimate.TileCount:N0} tiles, more than the {HardTileLimit:N0} " +
        "this will download at once. Reduce the zoom range.");

    int downloaded = 0, cached = 0, failed = 0, completed = 0;

    for (var z = Math.Max(TileMath.MinZoom, minZoom); z <= Math.Min(TileMath.MaxZoom, maxZoom); z++)
    {
      foreach (var tile in TileMath.RangeFor(bounds, z).Tiles())
      {
        if (cancellationToken.IsCancellationRequested)
          return new PrefetchResult(downloaded, cached, failed, Cancelled: true);

        if (_store.Exists(tile))
        {
          cached++;
        }
        else
        {
          var result = await _fetcher.GetAsync(tile, allowNetwork: true, cancellationToken)
            .ConfigureAwait(false);

          switch (result.Status)
          {
            case TileFetchStatus.Downloaded: downloaded++; break;
            case TileFetchStatus.FromDisk: cached++; break;
            case TileFetchStatus.Cancelled:
              return new PrefetchResult(downloaded, cached, failed, Cancelled: true);
            default: failed++; break;
          }

          try
          {
            await Task.Delay(SpacingMs, cancellationToken).ConfigureAwait(false);
          }
          catch (OperationCanceledException)
          {
            return new PrefetchResult(downloaded, cached, failed, Cancelled: true);
          }
        }

        completed++;
        progress?.Report(new PrefetchProgress(
          completed, estimate.TileCount, downloaded, cached, failed, z));
      }
    }

    return new PrefetchResult(downloaded, cached, failed, Cancelled: false);
  }
}
