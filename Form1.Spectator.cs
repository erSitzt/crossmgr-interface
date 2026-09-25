namespace CrossMgrInterface;

/// <summary>
/// The spectator screen: the live leaderboard for the crowd, on a second
/// monitor. The window draws; this builds its board every second from the same
/// snapshot the Race Day board uses.
/// </summary>
public partial class Form1
{
  private SpectatorWindow? _spectator;

  /// <summary>Opens the spectator screen, or brings it forward.</summary>
  private void ShowSpectatorScreen()
  {
    if (_spectator is { IsDisposed: false })
    {
      if (_spectator.WindowState == FormWindowState.Minimized) _spectator.WindowState = FormWindowState.Normal;
      _spectator.Activate();
      return;
    }

    var window = new SpectatorWindow
    {
      RowLimit = _settings.SpectatorRows > 0 ? _settings.SpectatorRows : null
    };

    // Not owned: an owned window always sits in front of its owner, which on a
    // laptop with one screen would bury the operator's own view under it.
    var screen = SpectatorScreenToUse(out var secondScreen);
    window.PlaceOn(screen, fullScreen: secondScreen && _settings.SpectatorFullScreen);
    window.PreferencesChanged += (_, _) => RememberSpectatorScreen(window);
    window.FormClosed += (_, _) =>
    {
      if (ReferenceEquals(_spectator, window)) _spectator = null;
    };

    _spectator = window;
    RenderSpectator();
    window.Show();

    AddMessage(secondScreen
      ? $"📺 Spectator screen open on {Describe(screen)}. Right-click it for Top 10 / Top 20 / All; F11 switches full screen."
      : "📺 Spectator screen open. Connect a second screen (TV or projector), then press Ctrl+Right in the spectator " +
        "window to move it there and F11 for full screen.");
  }

  /// <summary>
  /// The screen it was on last time if that is still connected; otherwise the
  /// first one that is not showing the main window.
  /// </summary>
  private Screen SpectatorScreenToUse(out bool secondScreen)
  {
    var own = Screen.FromControl(this);
    var screens = Screen.AllScreens;

    var remembered = screens.FirstOrDefault(s => s.DeviceName == _settings.SpectatorScreen);
    var chosen = remembered != null && remembered.DeviceName != own.DeviceName
      ? remembered
      : screens.FirstOrDefault(s => s.DeviceName != own.DeviceName);

    secondScreen = chosen != null;
    return chosen ?? own;
  }

  private void RememberSpectatorScreen(SpectatorWindow window)
  {
    _settings.SpectatorRows = window.RowLimit ?? 0;
    _settings.SpectatorScreen = window.ScreenName;
    _settings.SpectatorFullScreen = window.IsFullScreen;
    _settings.Save();
  }

  private void CloseSpectatorScreen()
  {
    var window = _spectator;
    if (window is not { IsDisposed: false }) return;

    if (window.Visible && Screen.AllScreens.Length > 1) RememberSpectatorScreen(window);
    window.Close();
  }

  private static string Describe(Screen screen) =>
    $"{(screen.Primary ? "the main screen" : "the second screen")} ({screen.Bounds.Width}×{screen.Bounds.Height})";

  /// <summary>Builds the board from a snapshot of the field and hands it to the window.</summary>
  private void RenderSpectator()
  {
    var window = _spectator;
    if (window is not { IsDisposed: false }) return;

    var started = raceStarted;
    List<RiderInfo> field;
    int? lapsToGo = null;

    if (!started || IsTimedSession)
    {
      // Before the start, who is entered; in a timed session everyone entered
      // has a place on the sheet, those without a time at the bottom.
      field = BuildSessionField();
    }
    else
    {
      lock (ridersLock)
      {
        field = PositionCalculator.GetSortedRidersFromSnapshot(
          riders.Values.Where(r => !ignoredTags.Contains(r.TagID)).Select(CloneRiderForDisplay).ToList());
      }
    }

    // Only the state is taken, not the operator's sentence that goes with it.
    var (state, _) = DescribeRaceState(field, field.Count);

    if ((raceTimeExpired || waitingForLeaderFinish) && targetLapsToFinishRace > 0 &&
        field.FirstOrDefault(r => !r.IsDNF && !r.IsDNS) is { } leader)
      lapsToGo = Math.Max(0, targetLapsToFinishRace - leader.TotalLaps);

    var board = SpectatorBoardBuilder.Build(new SpectatorInputs
    {
      Field = field,
      SessionType = sessionType,
      TeamEvent = teamEvent,
      Waves = waves != null,
      Title = raceName,
      State = state,
      LeaderLapsToGo = lapsToGo,
      Remaining = started && !raceFinished ? GetTimeRemaining() : null,
      FinalElapsed = raceFinished && raceStartTime.HasValue && raceEndTime.HasValue
        ? raceEndTime.Value - raceStartTime.Value
        : null,
      Duration = raceDuration,
      RowLimit = window.RowLimit
    });

    window.ShowBoard(board);
  }

  private sealed class SpectatorViewAdapter : IRaceView
  {
    private readonly Form1 _form;
    public SpectatorViewAdapter(Form1 form) => _form = form;

    public RaceViewKind Kind => RaceViewKind.Spectator;

    // A window of its own, so no tab decides whether it is on screen. Render
    // returns at once while it is closed.
    public TabPage? HostTab => null;

    // The clock moves every second even when nobody crosses the line.
    public bool NeedsHeartbeat => true;

    public void Render() => _form.RenderSpectator();
  }
}
