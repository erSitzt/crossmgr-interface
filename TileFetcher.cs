using System.Net;

namespace CrossMgrInterface;

public enum TileFetchStatus
{
  FromDisk,
  Downloaded,

  /// <summary>The server says this tile does not exist. Permanent for the session.</summary>
  NotFound,

  /// <summary>We are being asked to slow down, or stop. Applies to every tile, not just this one.</summary>
  RateLimited,

  NetworkError,
  Cancelled,

  /// <summary>No usable User-Agent, so no request was made. See the remarks on TileFetcher.</summary>
  NotConfigured
}

public readonly record struct TileFetchResult(TileFetchStatus Status, byte[]? Png)
{
  public bool HasImage => Png is { Length: > 0 };
}

/// <summary>
/// Fetches map tiles: disk first, then the network. Background threads only.
///
/// The OSM tile usage policy is enforced here rather than documented and hoped
/// for, because the penalty for breaking it is the source IP being blocked - a
/// failure that would appear on race day, on someone else's laptop:
///
///   - A real, identifying User-Agent is mandatory. Requests without one are
///     refused, and a faked browser UA gets the IP banned. It is a compile-time
///     constant and deliberately NOT an AppSettings property, because a
///     user-editable field is exactly how it ends up empty.
///   - At most two connections at a time, enforced twice over: the handler
///     queues, and the semaphore lets queued work be cancelled cheaply. The
///     offline pre-fetcher shares that same semaphore, so a background download
///     can never starve the map the operator is actually looking at.
///   - No Cache-Control or Pragma headers are ever sent.
///   - A 429 or 503 stops EVERY request, not just the one that saw it. The server
///     is saying stop, not "slow down on that tile".
/// </summary>
public sealed class TileFetcher : IDisposable
{
  /// <summary>
  /// Identifies this application to the tile server. Must stay a real, contactable
  /// identity - see the class remarks.
  /// </summary>
  public const string UserAgent =
    "CrossMgrInterface/1.0 (+https://github.com/erSitzt/crossmgr-interface)";

  private const int MaxConcurrentRequests = 2;
  private const int MaxAttemptsPerTile = 3;
  private const int FirstBackoffSeconds = 5;
  private const int FirstCooldownSeconds = 30;
  private const int MaxCooldownSeconds = 300;
  private const int MaxRememberedFailures = 2000;

  private static readonly HttpClient Http = CreateClient();

  private readonly TileStore _store;
  private readonly string _urlTemplate;
  private readonly Action<string>? _log;

  /// <summary>Tiles the server says do not exist. Never requested again this session.</summary>
  private readonly HashSet<TileId> _permanentlyMissing = new();

  private readonly Dictionary<TileId, (int Attempts, DateTime NotBefore)> _failed = new();
  private readonly object _gate = new();

  private DateTime _globalRetryAfterUtc = DateTime.MinValue;

  /// <summary>Shared with MapTilePrefetcher so interactive fetches always win the race for a slot.</summary>
  public SemaphoreSlim Gate { get; } = new(MaxConcurrentRequests, MaxConcurrentRequests);

  public TileFetcher(TileStore store, string? urlTemplate = null, Action<string>? log = null)
  {
    _store = store;
    _urlTemplate = urlTemplate ?? TileProvider.OpenStreetMap.UrlTemplate;
    _log = log;
  }

  /// <summary>False when no request will ever be issued. Fails loudly in development
  /// rather than quietly getting the address blocked in the field.</summary>
  public static bool IsConfigured => !string.IsNullOrWhiteSpace(UserAgent);

  /// <summary>True while a rate limit is in force. The status line says so.</summary>
  public bool IsRateLimited
  {
    get { lock (_gate) return DateTime.UtcNow < _globalRetryAfterUtc; }
  }

  public bool IsPermanentlyMissing(TileId t)
  {
    lock (_gate) return _permanentlyMissing.Contains(t);
  }

  /// <summary>Forgets per-tile backoff. Called on a zoom change, so the map recovers
  /// from a transient outage without a restart.</summary>
  public void ResetTransientFailures()
  {
    lock (_gate) _failed.Clear();
  }

  /// <summary>Disk first, then the network. Never throws.</summary>
  public async Task<TileFetchResult> GetAsync(TileId t, bool allowNetwork, CancellationToken ct)
  {
    if (!TileMath.IsValid(t)) return new TileFetchResult(TileFetchStatus.NotFound, null);

    var cached = await _store.ReadAsync(t, ct).ConfigureAwait(false);
    if (cached is not null) return new TileFetchResult(TileFetchStatus.FromDisk, cached);

    if (!allowNetwork) return new TileFetchResult(TileFetchStatus.NetworkError, null);
    if (!IsConfigured) return new TileFetchResult(TileFetchStatus.NotConfigured, null);

    if (!MayRequest(t, out var status)) return new TileFetchResult(status, null);

    try
    {
      await Gate.WaitAsync(ct).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      return new TileFetchResult(TileFetchStatus.Cancelled, null);
    }

    try
    {
      return await DownloadAsync(t, ct).ConfigureAwait(false);
    }
    finally
    {
      Gate.Release();
    }
  }

  private async Task<TileFetchResult> DownloadAsync(TileId t, CancellationToken ct)
  {
    var url = _urlTemplate
      .Replace("{z}", t.Z.ToString())
      .Replace("{x}", t.X.ToString())
      .Replace("{y}", t.Y.ToString());

    try
    {
      using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct)
        .ConfigureAwait(false);

      if (response.StatusCode == HttpStatusCode.NotFound)
      {
        lock (_gate) _permanentlyMissing.Add(t);
        return new TileFetchResult(TileFetchStatus.NotFound, null);
      }

      if (response.StatusCode is HttpStatusCode.TooManyRequests or
          HttpStatusCode.ServiceUnavailable or HttpStatusCode.Forbidden)
      {
        StartCooldown(response.Headers.RetryAfter?.Delta);
        return new TileFetchResult(TileFetchStatus.RateLimited, null);
      }

      if (!response.IsSuccessStatusCode)
      {
        NoteFailure(t);
        return new TileFetchResult(TileFetchStatus.NetworkError, null);
      }

      var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
      if (bytes.Length == 0)
      {
        NoteFailure(t);
        return new TileFetchResult(TileFetchStatus.NetworkError, null);
      }

      await _store.WriteAsync(t, bytes, ct).ConfigureAwait(false);
      ClearFailure(t);

      return new TileFetchResult(TileFetchStatus.Downloaded, bytes);
    }
    catch (OperationCanceledException)
    {
      return new TileFetchResult(TileFetchStatus.Cancelled, null);
    }
    catch (Exception ex)
    {
      NoteFailure(t);
      _log?.Invoke($"Map tile {t} could not be fetched: {ex.Message}");
      return new TileFetchResult(TileFetchStatus.NetworkError, null);
    }
  }

  private bool MayRequest(TileId t, out TileFetchStatus status)
  {
    lock (_gate)
    {
      if (_permanentlyMissing.Contains(t))
      {
        status = TileFetchStatus.NotFound;
        return false;
      }

      if (DateTime.UtcNow < _globalRetryAfterUtc)
      {
        status = TileFetchStatus.RateLimited;
        return false;
      }

      if (_failed.TryGetValue(t, out var failure))
      {
        if (failure.Attempts >= MaxAttemptsPerTile || DateTime.UtcNow < failure.NotBefore)
        {
          status = TileFetchStatus.NetworkError;
          return false;
        }
      }
    }

    status = TileFetchStatus.Downloaded;
    return true;
  }

  private void StartCooldown(TimeSpan? retryAfter)
  {
    lock (_gate)
    {
      var seconds = retryAfter?.TotalSeconds ?? 0;

      if (seconds <= 0)
      {
        // No Retry-After: start at 30s and double, capped at five minutes.
        var current = Math.Max(0, (_globalRetryAfterUtc - DateTime.UtcNow).TotalSeconds);
        seconds = current <= 0 ? FirstCooldownSeconds : Math.Min(current * 2, MaxCooldownSeconds);
      }

      seconds = Math.Min(seconds, MaxCooldownSeconds);
      _globalRetryAfterUtc = DateTime.UtcNow.AddSeconds(seconds);
    }

    _log?.Invoke($"Map tile server asked us to back off; pausing tile downloads.");
  }

  private void NoteFailure(TileId t)
  {
    lock (_gate)
    {
      // Bounded, so a long session with a flaky link cannot grow this forever.
      if (_failed.Count > MaxRememberedFailures) _failed.Clear();

      var attempts = _failed.TryGetValue(t, out var previous) ? previous.Attempts + 1 : 1;
      var wait = FirstBackoffSeconds * Math.Pow(2, attempts - 1);
      _failed[t] = (attempts, DateTime.UtcNow.AddSeconds(wait));
    }
  }

  private void ClearFailure(TileId t)
  {
    lock (_gate) _failed.Remove(t);
  }

  private static HttpClient CreateClient()
  {
    // One client per process. A new HttpClient per request exhausts sockets.
    var handler = new SocketsHttpHandler
    {
      MaxConnectionsPerServer = MaxConcurrentRequests,
      AutomaticDecompression = DecompressionMethods.All,
      PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    };

    var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    return client;
  }

  public void Dispose() => Gate.Dispose();
}
