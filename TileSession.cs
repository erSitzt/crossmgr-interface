namespace CrossMgrInterface;

/// <summary>
/// Everything needed to draw one basemap on one control: the disk cache, the
/// fetcher, and the paint-safe layer over them.
///
/// Bundled because switching provider means replacing all three at once - the
/// cache folder is per-host, the fetcher holds per-host backoff state, and the
/// layer holds decoded tiles from the old imagery that must not be drawn over the
/// new. Swapping them individually is how you end up with a half-changed map.
/// </summary>
public sealed class TileSession : IDisposable
{
  public TileProvider Provider { get; }
  public TileStore Store { get; }
  public TileFetcher Fetcher { get; }
  public TileLayer Layer { get; }

  public TileSession(Control host, TileProvider provider, Action<string>? log = null, TileSessionOptions? options = null)
  {
    Provider = provider;
    Store = new TileStore(provider.UrlTemplate, options?.CacheRoot);
    Fetcher = new TileFetcher(Store, provider.UrlTemplate, log);
    Layer = new TileLayer(host, Fetcher) { AllowNetwork = options?.AllowNetwork ?? true };
  }

  public void Dispose()
  {
    Layer.Dispose();
    Fetcher.Dispose();
  }
}

/// <summary>
/// Where a tile session keeps its tiles, and whether it may download more. Left
/// out, a session uses the application's own cache and goes online as needed.
/// The help screenshots use it so a CI run renders the map without asking the
/// tile servers for anything.
/// </summary>
public sealed record TileSessionOptions(string? CacheRoot = null, bool AllowNetwork = true);
