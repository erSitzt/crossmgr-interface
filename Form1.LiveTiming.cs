namespace CrossMgrInterface;

/// <summary>
/// Sending the running race to the live timing website, every few seconds.
///
/// Shaped exactly like the periodic state save: a timer on the UI thread
/// decides whether there is anything to do, a thread-pool task takes a
/// picture of the field under the riders lock and does the network work
/// outside it. Nothing here runs on the network thread, and nothing here is
/// ever modal - a race carries on identically whether the hotspot is up or
/// down, and the only sign is the colour of a tile.
///
/// The timer polls a fingerprint of the field rather than hooking the place a
/// crossing is recorded. Corrections, DNF marking, the ignore list and team
/// joins all change the standings through paths that never meet in one
/// method - but every one of them bumps a rider's Revision, so a fingerprint
/// sees them all, and the crossing path is left exactly as it was.
/// </summary>
public partial class Form1
{
  private const int LivePushIntervalMs = 3000;

  /// <summary>Resent even when nothing changed, so the page knows the laptop is alive.</summary>
  private static readonly TimeSpan LiveHeartbeat = TimeSpan.FromSeconds(15);

  /// <summary>Failures in a row before the tile goes red rather than amber.</summary>
  private const int LiveFailuresBeforeRed = 3;

  private bool _liveOn;
  private System.Windows.Forms.Timer? _liveTimer;
  private int _livePushInFlight;
  private long _liveFingerprint = -1;
  private long _liveSeq;
  private bool _liveFinalPending;
  private DateTime _liveLastSent = DateTime.MinValue;
  private LiveStatus? _liveLastStatus;
  private int _liveFailures;
  private HttpLiveTimingPublisher? _livePublisher;
  private bool _syncingLiveMenu;

  private bool? _liveConfiguredCache;

  /// <summary>Both an address and the key. Cached: the key is a DPAPI decrypt, and this is asked every second.</summary>
  private bool LiveTimingConfigured =>
    _liveConfiguredCache ??= !string.IsNullOrWhiteSpace(_settings.LiveSiteUrl) && PublishCredentials.HasKey();

  /// <summary>
  /// A demo may send live to this computer and nowhere else: a fictional race
  /// must never reach the club's real site, but with a local address a demo
  /// is the way to see the whole thing work.
  /// </summary>
  private bool LiveTimingAllowed => !IsDemo || HttpLiveTimingPublisher.IsLoopback(_settings.LiveSiteUrl);

  private void InvalidateLiveConfiguration()
  {
    _liveConfiguredCache = null;
    _livePublisher = null;
  }

  /// <summary>
  /// Switches live timing on or off for the session on screen.
  ///
  /// Never persisted and never restored with a session: a restored session may
  /// be minutes stale and the operator must first see what came back, and one
  /// restored to look at a result should not start broadcasting it.
  /// </summary>
  private void SetLiveTiming(bool on, bool silent = false)
  {
    if (on && (!LiveTimingConfigured || !LiveTimingAllowed)) on = false;
    if (on == _liveOn && !silent) return;

    _liveOn = on;
    _liveFingerprint = -1;   // the first tick after switching on sends straight away
    _liveFailures = 0;
    _liveLastStatus = null;

    if (on)
    {
      _livePublisher ??= HttpLiveTimingPublisher.FromSettings(_settings, AddDiagnostic);
      StartLiveTimer();
      if (!silent) AddMessage($"🌐 Live timing on - sending to {_livePublisher.Host()}");
    }
    else if (!silent)
    {
      AddMessage("🌐 Live timing off");
    }

    SyncLiveMenu();
    if (_statusLive != null) _statusLive.Visible = on;
    UpdateCommandStates();
  }

  private void SyncLiveMenu()
  {
    if (_menuLiveTiming == null || _syncingLiveMenu) return;
    _syncingLiveMenu = true;
    try { _menuLiveTiming.Checked = _liveOn; }
    finally { _syncingLiveMenu = false; }
  }

  private void StartLiveTimer()
  {
    if (_liveTimer != null) return;

    _liveTimer = new System.Windows.Forms.Timer { Interval = LivePushIntervalMs };
    _liveTimer.Tick += (_, _) =>
    {
      if (!_liveOn || !raceStarted || !currentRaceId.HasValue) return;

      // One push at a time. A push still waiting on its ten seconds blocks the
      // next tick; nothing queues up behind a dead hotspot.
      if (Interlocked.CompareExchange(ref _livePushInFlight, 1, 0) != 0) return;

      Task.Run(PushLiveAsync);
    };
    _liveTimer.Start();
  }

  /// <summary>Called when the race is over: one last update saying so, then off.</summary>
  private void RequestFinalLivePush()
  {
    if (!_liveOn) return;
    _liveFinalPending = true;
  }

  private async Task PushLiveAsync()
  {
    try
    {
      var publisher = _livePublisher;
      var raceId = currentRaceId;
      if (publisher == null || raceId == null) return;

      List<LiveCapture> captured;
      long fingerprint;
      RaceDayState state;
      DateTime? startedAt;
      TimeSpan duration;
      TimeSpan? remaining;
      var final = _liveFinalPending;
      var now = DateTime.Now;

      lock (ridersLock)
      {
        state = LiveState();
        fingerprint = LiveFingerprint(state);

        var due = fingerprint != _liveFingerprint || final || now - _liveLastSent > LiveHeartbeat;
        if (!due) return;

        captured = new List<LiveCapture>(riders.Count);
        foreach (var r in riders.Values)
          if (!ignoredTags.Contains(r.TagID))
            captured.Add(LiveCapture.Of(r, _settings.PublishNamesByDefault, _settings.HiddenNameStyle));

        startedAt = raceStartTime;
        duration = raceDuration;
        remaining = raceEndTime.HasValue && !raceFinished ? raceEndTime.Value - now : null;
      }

      var publicId = _raceDb.EnsurePublicId(raceId.Value);
      if (publicId == null) return;

      var snapshot = LiveSnapshotBuilder.Build(new LiveInputs
      {
        PublicId = publicId,
        Title = string.IsNullOrWhiteSpace(raceName) ? $"Session {startedAt:yyyy-MM-dd HH:mm}" : raceName,
        SessionType = sessionType,
        State = final ? RaceDayState.Finished : state,
        TeamEvent = LiveRules().TeamEvent,
        StartedAt = startedAt,
        Duration = duration,
        Remaining = remaining,
        Now = now,
        Seq = Interlocked.Increment(ref _liveSeq),
        Riders = captured
      });

      var outcome = await publisher.PushAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
      RecordLiveOutcome(outcome, fingerprint, final, now, publisher);
    }
    catch (Exception ex)
    {
      // Never let live timing take the application down. Once in the log is enough.
      AddDiagnostic($"🌐 live push failed: {ex.GetType().Name}: {ex.Message}");
    }
    finally
    {
      Interlocked.Exchange(ref _livePushInFlight, 0);
    }
  }

  private void RecordLiveOutcome(LiveOutcome outcome, long fingerprint, bool final, DateTime at,
                                 HttpLiveTimingPublisher publisher)
  {
    var changed = outcome.Status != _liveLastStatus;
    _liveLastStatus = outcome.Status;

    if (outcome.Ok)
    {
      _liveFingerprint = fingerprint;
      _liveLastSent = at;
      _liveFailures = 0;
      if (changed) AddDiagnostic($"🌐 live {publisher.Host()} -> sending");
      if (final) BeginInvoke(new Action(() => { _liveFinalPending = false; SetLiveTiming(false, silent: true); }));
      return;
    }

    _liveFailures++;
    if (changed) AddDiagnostic($"🌐 live {publisher.Host()} -> {outcome.Status}{(outcome.HttpCode is { } c ? $" ({c})" : "")}");

    // A wrong key will be just as wrong in three seconds, and again after that.
    if (outcome.Status == LiveStatus.Unauthorized)
      BeginInvoke(new Action(() => SetLiveTiming(false, silent: true)));
    else if (final && _liveFailures >= LiveFailuresBeforeRed)
      BeginInvoke(new Action(() => { _liveFinalPending = false; SetLiveTiming(false, silent: true); }));
  }

  /// <summary>The state the Race Day screen would show, from the same flags, without the sentence.</summary>
  private RaceDayState LiveState()
  {
    if (raceFinished) return RaceDayState.Finished;
    if (!raceStarted) return RaceDayState.WaitingForFirstRider;
    if (waitingForFinalLaps) return RaceDayState.Finishing;
    if (raceTimeExpired || waitingForLeaderFinish) return RaceDayState.LastLaps;
    return RaceDayState.Running;
  }

  /// <summary>
  /// Cheap and complete: anything that moves a rider on the leaderboard moves
  /// this number. Call under the riders lock.
  /// </summary>
  private long LiveFingerprint(RaceDayState state)
  {
    long hash = (long)state * 1_000_003 + ignoredTags.Count * 7919;
    foreach (var r in riders.Values)
      hash = unchecked(hash * 31 + r.Laps.Count * 8 + PositionCalculator.StatusRank(r) + r.Revision * 131);
    return hash;
  }

  /// <summary>What the LIVE tile should say right now. Called from the 1 s heartbeat.</summary>
  private (LiveTileState State, string Value, string Sub) LiveTileNow()
  {
    if (!LiveTimingAllowed) return (LiveTileState.NotSetUp, "", "");
    if (!LiveTimingConfigured) return (LiveTileState.NotSetUp, "Not set up", "Race > Results website...");
    if (!_liveOn) return (LiveTileState.Off, "Off", raceFinished ? "" : "Switch on to send the race");

    var host = _livePublisher?.Host() ?? "";
    var since = _liveLastSent == DateTime.MinValue ? "" : $"sent {Ago(DateTime.Now - _liveLastSent)}";

    if (_liveLastStatus == null) return (LiveTileState.Sending, "Sending", "starting...");
    if (_liveLastStatus == LiveStatus.Sent) return (LiveTileState.Sending, "Sending", since);

    var what = HttpLiveTimingPublisher.Describe(_liveLastStatus.Value, host);
    return _liveFailures >= LiveFailuresBeforeRed
      ? (LiveTileState.Failed, what, since.Length > 0 ? $"last {since}" : "nothing sent yet")
      : (LiveTileState.Trouble, "Retrying", since.Length > 0 ? $"{what} · last {since}" : what);
  }

  private static string Ago(TimeSpan t) =>
    t < TimeSpan.FromMinutes(1) ? $"{(int)t.TotalSeconds} s ago" : $"{(int)t.TotalMinutes} min ago";
}
