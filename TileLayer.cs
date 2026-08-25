namespace CrossMgrInterface;

/// <summary>
/// The only part of the tile machinery the renderer talks to.
///
/// THREADING - one rule, and everything else follows from it:
///
///   1. The memory cache, the in-flight set and the queue are touched ONLY on the
///      UI thread. A Paint is synchronous, so no eviction can interleave with a
///      DrawImage, and the check-then-act in the queue pump is genuinely atomic.
///      No locks here, and none should be added.
///   2. TileStore and TileFetcher are touched only on background threads.
///   3. Peek does no I/O, never blocks and cannot throw for a network reason, so
///      it is safe to call from a paint handler. Everything else is not.
///   4. Every fetch continuation marshals back to the UI thread before touching
///      anything in (1).
///
/// Completions do not call Invalidate directly. They start a 60ms timer that
/// fires TilesChanged once, so a burst of fifty arriving tiles produces one
/// repaint instead of fifty - the same coalescing idea as UiRefreshCoordinator's
/// 125ms pump.
/// </summary>
public sealed class TileLayer : IDisposable
{
  /// <summary>Coalescing window for arriving tiles.</summary>
  private const int RepaintCoalesceMs = 60;

  /// <summary>
  /// Requests outstanding at once. The fetcher's semaphore caps actual connections
  /// at two; this caps how much work is queued behind them, so a pan does not
  /// leave hundreds of stale requests draining.
  /// </summary>
  private const int MaxInFlight = 8;

  private readonly Control _host;
  private readonly TileFetcher _fetcher;
  private readonly TileMemoryCache _memory;
  /// <summary>
  /// Guarded, unlike everything else here, because a fetch completing before the
  /// window has a handle has to give its slot back from a background thread. It is
  /// touched twice per tile, not per paint, so the lock costs nothing.
  /// </summary>
  private readonly HashSet<TileId> _inFlight = new();

  private readonly object _flightGate = new();
  private readonly Queue<TileId> _queue = new();
  private readonly HashSet<TileId> _queued = new();
  private readonly CancellationTokenSource _cts = new();
  private readonly System.Windows.Forms.Timer _repaintTimer;

  private bool _disposed;

  /// <summary>Raised on the UI thread when newly arrived tiles are worth a repaint.</summary>
  public event EventHandler? TilesChanged;

  /// <summary>Cleared during an offline pre-cache so the two do not fight for slots.</summary>
  public bool AllowNetwork { get; set; } = true;

  public TileLayer(Control host, TileFetcher fetcher, int memoryCapacity = TileMemoryCache.DefaultCapacity)
  {
    _host = host;
    _fetcher = fetcher;
    _memory = new TileMemoryCache(memoryCapacity);

    _repaintTimer = new System.Windows.Forms.Timer { Interval = RepaintCoalesceMs };
    _repaintTimer.Tick += (_, _) =>
    {
      _repaintTimer.Stop();
      TilesChanged?.Invoke(this, EventArgs.Empty);
    };
  }

  public int PendingCount
  {
    get { lock (_flightGate) return _queue.Count + _inFlight.Count; }
  }

  /// <summary>
  /// A resident tile, or null. PAINT-SAFE: a dictionary lookup and nothing else.
  ///
  /// The returned bitmap is for drawing immediately. Do not retain it - the next
  /// Put may evict and dispose it.
  /// </summary>
  public Bitmap? Peek(TileId id) => _memory.TryGet(id, out var bitmap) ? bitmap : null;

  public bool IsPermanentlyUnavailable(TileId id) => _fetcher.IsPermanentlyMissing(id);

  /// <summary>
  /// Asks for everything in view, plus a one-tile margin so a small pan is already
  /// covered. Call this from a camera change, never from Draw - requesting tiles
  /// inside a paint handler builds a request/repaint feedback loop.
  /// </summary>
  public void EnsureVisible(TileRange range)
  {
    if (_disposed) return;

    // Rebuild rather than append: anything scrolled off screen should stop being
    // wanted, and the centre-out ordering has to be recomputed for the new view.
    _queue.Clear();
    _queued.Clear();

    foreach (var id in range.Inflate(1).Tiles())
    {
      if (!TileMath.IsValid(id)) continue;
      bool alreadyFetching;
      lock (_flightGate) alreadyFetching = _inFlight.Contains(id);

      if (_memory.Contains(id) || alreadyFetching) continue;
      if (_fetcher.IsPermanentlyMissing(id)) continue;

      if (_queued.Add(id)) _queue.Enqueue(id);
    }

    Pump();
  }

  /// <summary>Drops decoded tiles but keeps them on disk. Used when the zoom changes wholesale.</summary>
  public void ForgetTransientFailures() => _fetcher.ResetTransientFailures();

  /// <summary>One line for the corner of the map, or null when there is nothing to say.</summary>
  public string? StatusText
  {
    get
    {
      if (!TileFetcher.IsConfigured) return "Map tiles unavailable";
      if (_fetcher.IsRateLimited) return "Map server busy - showing cached tiles";
      if (!AllowNetwork && PendingCount > 0) return "Downloading map for offline use...";

      var pending = PendingCount;
      return pending > 0 ? $"{pending} map tile{(pending == 1 ? "" : "s")} loading..." : null;
    }
  }

  private void Pump()
  {
    while (_queue.Count > 0)
    {
      lock (_flightGate) if (_inFlight.Count >= MaxInFlight) return;

      var id = _queue.Dequeue();
      _queued.Remove(id);
      if (_memory.Contains(id)) continue;

      bool started;
      lock (_flightGate) started = _inFlight.Add(id);
      if (!started) continue;

      Start(id);
    }
  }

  private void Start(TileId id)
  {
    var allowNetwork = AllowNetwork;

    _ = Task.Run(async () =>
    {
      var result = await _fetcher.GetAsync(id, allowNetwork, _cts.Token).ConfigureAwait(false);
      Complete(id, result);
    }, _cts.Token);
  }

  /// <summary>
  /// Hands a finished fetch back to the UI thread, which owns every collection here.
  ///
  /// The slot MUST be released on every path out of this method. Tiles are
  /// routinely requested before the window has a handle - the first zoom-to-fit
  /// happens while the tab is still being built - and if those completions
  /// returned without releasing, the in-flight set would fill to MaxInFlight and
  /// the pump would wedge for the rest of the session: no error, no exception,
  /// just a map that never loads another tile.
  /// </summary>
  private void Complete(TileId id, TileFetchResult result)
  {
    if (!_disposed)
    {
      try
      {
        if (_host.IsHandleCreated && !_host.IsDisposed)
        {
          _host.BeginInvoke(new Action(() => Deliver(id, result)));
          return;
        }
      }
      catch (Exception)
      {
        // The form can close between the IsDisposed check and the BeginInvoke.
      }
    }

    // Could not reach the UI thread. The bytes are already on disk, so the next
    // EnsureVisible will pick this tile straight back up from the cache.
    Release(id);
  }

  /// <summary>Runs on the UI thread, which owns the memory cache and the queue.</summary>
  private void Deliver(TileId id, TileFetchResult result)
  {
    Release(id);
    if (_disposed) return;

    if (result.HasImage)
    {
      try
      {
        _memory.Put(id, TileStore.Decode(result.Png!));
        if (!_repaintTimer.Enabled) _repaintTimer.Start();
      }
      catch (Exception)
      {
        // A tile that will not decode is a corrupt cache entry, not a crash. It
        // stays off the map and is re-fetched next time the view changes.
      }
    }

    Pump();
  }

  private void Release(TileId id)
  {
    lock (_flightGate) _inFlight.Remove(id);
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;

    _cts.Cancel();
    _repaintTimer.Stop();
    _repaintTimer.Dispose();
    _memory.Dispose();
    _cts.Dispose();
  }
}
