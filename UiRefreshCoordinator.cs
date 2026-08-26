namespace CrossMgrInterface;

/// <summary>
/// Identifies a refreshable view. A flags enum so a single change can mark
/// several views dirty in one call.
/// </summary>
[Flags]
public enum RaceViewKind
{
  None = 0,
  Riders = 1 << 0,
  Statistics = 1 << 1,
  LapChart = 1 << 2,
  LapProgression = 1 << 3,
  RaceDay = 1 << 4,

  /// <summary>The circuit map. Like RaceDay, it is a live operator screen, so it
  /// is part of All - anything big enough to repaint everything moves riders too.</summary>
  Track = 1 << 5,

  /// <summary>The gate pick order. Only present during a qualifying session.</summary>
  Qualifying = 1 << 6,

  /// <summary>The transponder check. Present for any timed session.</summary>
  Transponder = 1 << 7,

  /// <summary>
  /// The views that move when a lap is recorded.
  ///
  /// A named group rather than the same four flags spelled out at every call
  /// site, which is how the qualifying sheet would otherwise have been left out
  /// of one of them and quietly gone stale.
  /// </summary>
  Standings = Riders | LapChart | RaceDay | Track | Qualifying | Transponder,

  All = Riders | Statistics | LapChart | LapProgression | RaceDay | Track | Qualifying | Transponder
}

/// <summary>
/// One refreshable view. Implementations are thin adapters over the existing
/// Update*Display methods on Form1.
/// </summary>
public interface IRaceView
{
  RaceViewKind Kind { get; }

  /// <summary>Tab that must be selected for this view to be on screen. Null means always visible.</summary>
  TabPage? HostTab { get; }

  /// <summary>
  /// True when the view's content changes with the wall clock even though no
  /// data changed - race clocks, countdowns, "due in" columns, the lap-chart
  /// progress line. Those need a periodic tick as well as change notifications.
  /// </summary>
  bool NeedsHeartbeat { get; }

  /// <summary>
  /// True while this view's content changes on every frame rather than only when
  /// the data does - an animated map, where the dots move with the wall clock.
  ///
  /// Such a view keeps its dirty bit after rendering, so the pump repaints it on
  /// every tick instead of once a second. That is the difference between motion
  /// and a display that looks broken: a rider covers about twenty pixels a second
  /// at circuit zoom, so one frame a second reads as a fault rather than as speed.
  ///
  /// It costs nothing while the view is hidden, because Flush skips invisible
  /// views before it renders anything. Implementations must return false as soon
  /// as there is nothing left to animate, or a finished race would spin the pump
  /// at full rate for no reason.
  /// </summary>
  bool WantsContinuousRepaint => false;

  /// <summary>Full repaint from current data. Called when the view is dirty and visible.</summary>
  void Render();

  /// <summary>
  /// Clock-driven repaint when the underlying data has not changed. Defaults to a
  /// full render; override where a cheaper partial update will do (the riders grid
  /// only needs its two countdown columns rewritten, for instance).
  /// </summary>
  void RenderHeartbeat() => Render();
}

/// <summary>
/// Drives every view refresh in the application.
///
/// It replaces a design where a single 1s timer cleared each dirty flag *before*
/// checking whether the view was actually visible, so any change arriving while
/// a tab was hidden was discarded and never repainted - the tab kept showing
/// stale data until the next change happened to land while it was on screen.
///
/// Three rules fix that:
///   1. Invalidate() never touches a control, so it is safe to call from the
///      network thread while holding a data lock.
///   2. A dirty bit is cleared only after the view has actually rendered. A
///      hidden view keeps its bit and repaints the moment its tab is selected.
///   3. Tab activation renders anything dirty or not yet rendered at all,
///      uniformly, so no view can be forgotten.
/// </summary>
public sealed class UiRefreshCoordinator : IDisposable
{
  /// <summary>How quickly a change reaches the screen, and the coalescing window for bursts.</summary>
  private const int PumpIntervalMs = 125;
  private const int HeartbeatIntervalMs = 1000;

  private readonly List<IRaceView> _views = new();
  private readonly object _gate = new();
  private readonly TabControl _tabs;
  private readonly System.Windows.Forms.Timer _pump;
  private readonly Action<string>? _log;

  /// <summary>Per-view render cost, so "is the UI keeping up?" is measurable.</summary>
  private sealed class ViewStats
  {
    public int Renders;
    public long TotalMs;
    public long MaxMs;
    public int Skipped;
  }

  private readonly Dictionary<RaceViewKind, ViewStats> _stats = new();
  private DateTime _lastStatsReport = DateTime.MinValue;

  /// <summary>A single render slower than this is worth naming in the log.</summary>
  private const int SlowRenderMs = 400;
  private const int StatsIntervalMs = 30000;

  private RaceViewKind _dirty = RaceViewKind.None;
  private RaceViewKind _everRendered = RaceViewKind.None;
  private int _cooldownTicks;
  private DateTime _lastHeartbeat = DateTime.MinValue;
  private bool _disposed;

  public UiRefreshCoordinator(TabControl tabs, Action<string>? log = null)
  {
    _tabs = tabs;
    _log = log;

    _pump = new System.Windows.Forms.Timer { Interval = PumpIntervalMs };
    _pump.Tick += Pump_Tick;
  }

  public void Register(IRaceView view)
  {
    _views.Add(view);
    // A freshly registered view has never painted, so make sure it does.
    Invalidate(view.Kind);
  }

  public void Start() => _pump.Start();

  /// <summary>
  /// Marks views as needing a repaint. Thread-safe and non-blocking: it takes a
  /// short lock over an integer and returns. Deliberately reads no control
  /// property - TabControl.SelectedIndex/SelectedTab issue a synchronous
  /// SendMessage into the UI thread's message pump, which is disastrous to call
  /// from the network thread while holding the riders lock.
  /// </summary>
  public void Invalidate(RaceViewKind kinds)
  {
    if (kinds == RaceViewKind.None) return;
    lock (_gate) _dirty |= kinds;
  }

  /// <summary>
  /// Renders the given views immediately if they are visible, ignoring the
  /// coalescing delay. For explicit user actions such as a Refresh button.
  /// UI thread only.
  /// </summary>
  public void RenderNow(RaceViewKind kinds)
  {
    Invalidate(kinds);
    Flush(force: true);
  }

  /// <summary>Call from TabControl.SelectedIndexChanged.</summary>
  public void OnTabChanged()
  {
    // Anything that changed while this tab was hidden still has its bit set, and
    // a view that has never painted needs its first paint even if nothing is dirty.
    foreach (var view in _views)
    {
      if (!IsVisible(view)) continue;

      bool pending;
      lock (_gate) pending = (_dirty & view.Kind) != 0;
      if (pending || (_everRendered & view.Kind) == 0)
        RenderOne(view);
    }
  }

  private void Pump_Tick(object? sender, EventArgs e)
  {
    // Back off after an expensive render so the UI thread is never monopolised.
    if (_cooldownTicks > 0)
    {
      _cooldownTicks--;
      _skippedTicks++;
      return;
    }

    Flush(force: false);

    if ((DateTime.Now - _lastHeartbeat).TotalMilliseconds >= HeartbeatIntervalMs)
    {
      _lastHeartbeat = DateTime.Now;
      Heartbeat();
    }

    if ((DateTime.Now - _lastStatsReport).TotalMilliseconds >= StatsIntervalMs)
    {
      if (_lastStatsReport != DateTime.MinValue) ReportStats();
      _lastStatsReport = DateTime.Now;
    }
  }

  private void Flush(bool force)
  {
    RaceViewKind pending;
    lock (_gate) pending = _dirty;
    if (pending == RaceViewKind.None) return;

    var started = Environment.TickCount64;

    foreach (var view in _views)
    {
      if ((pending & view.Kind) == 0) continue;

      // A hidden view keeps its dirty bit so it repaints on tab activation.
      if (!IsVisible(view)) continue;

      RenderOne(view);
    }

    if (!force)
    {
      // If that render took longer than one pump interval, skip proportionally
      // many ticks. Keeps the UI thread at roughly 50% render load at worst,
      // whatever the field size.
      var elapsed = Environment.TickCount64 - started;
      _cooldownTicks = (int)Math.Min(elapsed / PumpIntervalMs, 16);
    }
  }

  private void Heartbeat()
  {
    foreach (var view in _views)
    {
      if (!view.NeedsHeartbeat || !IsVisible(view)) continue;

      // Skip anything Flush is about to repaint anyway.
      bool alreadyDirty;
      lock (_gate) alreadyDirty = (_dirty & view.Kind) != 0;
      if (alreadyDirty) continue;

      // A view that has never done a full render must not start with a partial one.
      if ((_everRendered & view.Kind) == 0)
      {
        RenderOne(view);
        continue;
      }

      var started = Environment.TickCount64;
      try
      {
        view.RenderHeartbeat();
      }
      catch (Exception ex)
      {
        _log?.Invoke($"Error on {view.Kind} heartbeat: {ex.Message}");
      }
      finally
      {
        // Counted like any other render: a clock-driven repaint costs the UI
        // thread just as much as a data-driven one, and leaving it out of the
        // measurements hid most of what the application was actually doing.
        Record(view.Kind, Environment.TickCount64 - started);
      }
    }
  }

  private void RenderOne(IRaceView view)
  {
    var started = Environment.TickCount64;
    try
    {
      view.Render();
    }
    catch (Exception ex)
    {
      // One misbehaving view must not stop the others or wedge the pump.
      _log?.Invoke($"Error refreshing {view.Kind}: {ex.Message}");
    }
    finally
    {
      Record(view.Kind, Environment.TickCount64 - started);

      // Cleared whether or not Render threw - otherwise a persistently failing
      // view would spin the pump at full rate for the rest of the race.
      //
      // An animating view is the deliberate exception: it is never "clean",
      // because its content moves with the clock rather than with the data, so
      // its bit is left set and the pump paints it again next tick. Note this
      // has to be decided HERE rather than by the view calling Invalidate from
      // inside Render - that would be undone by this very line.
      lock (_gate)
      {
        if (view.WantsContinuousRepaint) _dirty |= view.Kind;
        else _dirty &= ~view.Kind;
      }

      _everRendered |= view.Kind;
    }
  }

  private void Record(RaceViewKind kind, long elapsedMs)
  {
    if (!_stats.TryGetValue(kind, out var stat))
      _stats[kind] = stat = new ViewStats();

    stat.Renders++;
    stat.TotalMs += elapsedMs;
    if (elapsedMs > stat.MaxMs) stat.MaxMs = elapsedMs;

    if (elapsedMs >= SlowRenderMs)
      _log?.Invoke($"Slow render: {kind} took {elapsedMs} ms");
  }

  /// <summary>
  /// Periodic summary of how much of the UI thread rendering is consuming.
  /// The "skipped" count is the backoff working: ticks deliberately given up
  /// because the previous render overran the pump interval.
  /// </summary>
  private void ReportStats()
  {
    if (_stats.Count == 0) return;

    var parts = _stats
      .Where(kv => kv.Value.Renders > 0)
      .OrderByDescending(kv => kv.Value.TotalMs)
      .Select(kv =>
      {
        var v = kv.Value;
        return $"{kv.Key} x{v.Renders} avg {v.TotalMs / Math.Max(1, v.Renders)}ms max {v.MaxMs}ms";
      });

    _log?.Invoke($"Render load (last {StatsIntervalMs / 1000}s): {string.Join(" | ", parts)}" +
                 $" | ticks skipped by backoff: {_skippedTicks}");

    _stats.Clear();
    _skippedTicks = 0;
  }

  private int _skippedTicks;

  private bool IsVisible(IRaceView view)
    => view.HostTab == null || ReferenceEquals(_tabs.SelectedTab, view.HostTab);

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    _pump.Stop();
    _pump.Tick -= Pump_Tick;
    _pump.Dispose();
  }
}
