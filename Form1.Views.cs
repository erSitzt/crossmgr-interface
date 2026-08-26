namespace CrossMgrInterface;

/// <summary>
/// Refresh plumbing for Form1: the adapters that expose each tab to
/// <see cref="UiRefreshCoordinator"/>, and the render entry points they call.
///
/// Kept in its own partial file because the WinForms designer rewrites
/// Form1.Designer.cs wholesale and Form1.cs is already very large.
/// </summary>
public partial class Form1
{
  private UiRefreshCoordinator _refresh = null!;

  /// <summary>
  /// Private snapshot the lap chart paints from.
  ///
  /// The paint handler used to receive the live riders dictionary with no lock
  /// while the network thread appended laps to it, which threw
  /// "Collection was modified" mid-paint and left the chart half drawn. Locking
  /// during paint is not an option either - it would stall the network thread for
  /// the whole redraw - so the data is copied once per refresh instead.
  /// </summary>
  private Dictionary<string, RiderInfo> _lapChartSnapshot = new();

  private void InitializeRefreshCoordinator()
  {
    _refresh = new UiRefreshCoordinator(tabControl, AddDiagnostic);

    _refresh.Register(new RaceDayViewAdapter(this));
    _refresh.Register(new RidersView(this));
    _refresh.Register(new StatisticsView(this));
    _refresh.Register(new LapChartView(this));
    _refresh.Register(new LapProgressionView(this));
    _refresh.Register(new TrackMapViewAdapter(this));
    _refresh.Register(new QualifyingViewAdapter(this));

    _refresh.Start();
  }

  /// <summary>Takes a consistent copy of the field for the lap chart, then repaints.</summary>
  private void RefreshLapChart()
  {
    lock (ridersLock)
    {
      _lapChartSnapshot = riders.Values
        .Where(r => !ignoredTags.Contains(r.TagID))
        .Select(CloneRiderForDisplay)
        .ToDictionary(r => r.TagID, r => r);
    }

    panelLapChart.Invalidate();
    lastProgressLineUpdate = DateTime.Now;
  }

  /// <summary>Snapshots the field and hands it to the lap progression grid.</summary>
  private void RenderLapProgression()
  {
    List<RiderInfo> riderSnapshot;
    bool raceFinishedSnapshot;
    bool waitingForFinalLapsSnapshot;

    lock (ridersLock)
    {
      riderSnapshot = riders.Values.Where(r => !ignoredTags.Contains(r.TagID)).ToList();
      raceFinishedSnapshot = raceFinished;
      waitingForFinalLapsSnapshot = waitingForFinalLaps;
    }

    _lapProgressionManager.UpdateLapProgressionDisplay(
      riderSnapshot, raceFinishedSnapshot, waitingForFinalLapsSnapshot, this);
  }

  /// <summary>
  /// Deep copy for handing rider data to renderers and reports. The laps list
  /// must be copied too: consumers iterate it outside the lock.
  ///
  /// Every status field has to come across. This used to copy IsDNF but not
  /// IsDNS, which made every DNS branch in RaceDayView unreachable - a rider
  /// marked as not started was scored on the leaderboard as though they were
  /// still out on the circuit.
  /// </summary>
  private static RiderInfo CloneRiderForDisplay(RiderInfo r) => new()
  {
    TagID = r.TagID,
    RiderNumber = r.RiderNumber,
    FirstName = r.FirstName,
    LastName = r.LastName,
    Team = r.Team,
    Category = r.Category,
    Machine = r.Machine,
    LastCrossingTime = r.LastCrossingTime,
    FirstCrossing = r.FirstCrossing,
    LastCrossing = r.LastCrossing,
    RaceStartTime = r.RaceStartTime,
    IsDNF = r.IsDNF,
    DNFTime = r.DNFTime,
    IsDNS = r.IsDNS,
    StatusSetByOperator = r.StatusSetByOperator,
    StatusReason = r.StatusReason,
    Revision = r.Revision,
    FinalAllowedLap = r.FinalAllowedLap,
    Laps = r.Laps.ToList()
  };

  // ---- View adapters -------------------------------------------------------

  private sealed class RidersView : IRaceView
  {
    private readonly Form1 _form;
    public RidersView(Form1 form) => _form = form;

    public RaceViewKind Kind => RaceViewKind.Riders;
    public TabPage? HostTab => _form.tabPageRiders;
    public bool NeedsHeartbeat => true;

    public void Render() => _form.UpdateRidersDisplay();

    // Between laps only the "Next Est." / "Time To Next" columns move, so the
    // clock tick rewrites those two rather than rebuilding the whole grid.
    public void RenderHeartbeat()
    {
      if (_form.raceFinished) return;
      _form.UpdateRiderPredictions();
    }
  }

  private sealed class StatisticsView : IRaceView
  {
    private readonly Form1 _form;
    public StatisticsView(Form1 form) => _form = form;

    public RaceViewKind Kind => RaceViewKind.Statistics;
    public TabPage? HostTab => _form.tabPageStats;
    public bool NeedsHeartbeat => true;

    public void Render() => _form.UpdateStatisticsDisplay();
  }

  private sealed class LapChartView : IRaceView
  {
    private readonly Form1 _form;
    public LapChartView(Form1 form) => _form = form;

    public RaceViewKind Kind => RaceViewKind.LapChart;
    public TabPage? HostTab => _form.tabPageLapChart;
    public bool NeedsHeartbeat => true;

    public void Render() => _form.RefreshLapChart();

    // Nothing changed but the clock; the only moving part is the progress line,
    // which does not need a redraw every second.
    public void RenderHeartbeat()
    {
      if ((DateTime.Now - _form.lastProgressLineUpdate).TotalSeconds < 5) return;
      _form.RefreshLapChart();
    }
  }

  private sealed class QualifyingViewAdapter : IRaceView
  {
    private readonly Form1 _form;
    public QualifyingViewAdapter(Form1 form) => _form = form;

    public RaceViewKind Kind => RaceViewKind.Qualifying;
    public TabPage? HostTab => _form.tabPageQualifying;

    // A best lap that has been set does not change with the clock; only a new
    // lap moves this sheet, and that already marks it dirty.
    public bool NeedsHeartbeat => false;

    public void Render() => _form.RenderQualifying();
  }

  private sealed class LapProgressionView : IRaceView
  {
    private readonly Form1 _form;
    public LapProgressionView(Form1 form) => _form = form;

    public RaceViewKind Kind => RaceViewKind.LapProgression;
    public TabPage? HostTab => _form.tabPageLapProgression;

    // Purely historical - a lap that has been ridden does not change with time.
    public bool NeedsHeartbeat => false;

    public void Render() => _form.RenderLapProgression();
  }
}
