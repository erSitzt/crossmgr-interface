namespace CrossMgrInterface;

/// <summary>
/// The handful of things a volunteer must not miss.
///
/// These used to be indistinguishable from every other line in a log that
/// scrolls past at tag-read rate. They now raise a banner on the Race Day view
/// and a mirror in the status bar, so a warning is visible whichever tab is open.
///
/// Notices are raised at the point the event actually happens rather than by
/// pattern-matching the log text, so rewording a message cannot silently switch
/// a warning off.
/// </summary>
public partial class Form1
{
  private System.Windows.Forms.Timer? _noticeTimer;
  private NoticeLevel _currentNoticeLevel = NoticeLevel.Info;
  private bool _readerQuietNoticeShown;

  /// <summary>The last read when the no-reads alarm went up, to tell reads resuming from riders no longer being due.</summary>
  private DateTime _readerQuietAlarmLastRead;

  /// <summary>What the reader check made of the silence at the last clock tick. Null outside a running session.</summary>
  private ReaderQuietVerdict? _readerQuiet;

  /// <summary>Reader > Connection settings: judge the silence from lap times, or after a fixed time.</summary>
  private bool readerQuietFromLapTimes = true;
  private int readerQuietSeconds = 60;

  /// <summary>
  /// Shows a notice. Never a modal: a modal would freeze the clock and the
  /// leaderboard, which is exactly the wrong thing to do mid-race.
  /// </summary>
  private void RaiseNotice(NoticeLevel level, string message)
  {
    if (InvokeRequired)
    {
      BeginInvoke(new Action<NoticeLevel, string>(RaiseNotice), level, message);
      return;
    }

    // A critical notice already on screen is not displaced by something milder.
    if (_currentNoticeLevel == NoticeLevel.Critical && level != NoticeLevel.Critical)
      return;

    _currentNoticeLevel = level;
    _raceDayView.ShowBanner(level, message);
    SetStatusNotice(level, message);

    if (level == NoticeLevel.Critical)
    {
      try { System.Media.SystemSounds.Exclamation.Play(); }
      catch (Exception) { /* no audio device is not a problem worth reporting */ }
    }

    ScheduleNoticeDismissal(level);
  }

  /// <summary>
  /// Info and warnings clear themselves; a critical notice stays until the
  /// operator acknowledges it.
  /// </summary>
  private void ScheduleNoticeDismissal(NoticeLevel level)
  {
    _noticeTimer?.Stop();

    if (level == NoticeLevel.Critical) return;

    _noticeTimer ??= new System.Windows.Forms.Timer();
    _noticeTimer.Interval = level == NoticeLevel.Warning ? 20000 : 8000;
    _noticeTimer.Tick -= NoticeTimer_Tick;
    _noticeTimer.Tick += NoticeTimer_Tick;
    _noticeTimer.Start();
  }

  private void NoticeTimer_Tick(object? sender, EventArgs e)
  {
    _noticeTimer?.Stop();
    ClearNotice();
  }

  private void ClearNotice()
  {
    _currentNoticeLevel = NoticeLevel.Info;
    _raceDayView.ClearBanner();
    ClearStatusNotice();
  }

  /// <summary>
  /// Watches for the reader going quiet - the failure that costs a race, and the
  /// one nothing in the application used to notice. What counts as quiet is
  /// <see cref="ReaderQuietCheck"/>'s call; the READER tile shows the same verdict.
  /// </summary>
  private void CheckReaderHealth()
  {
    if (!raceStarted || raceFinished || !isListening)
    {
      _readerQuiet = null;
      _readerQuietNoticeShown = false;
      return;
    }

    if (lastTagTime == DateTime.MinValue)
    {
      _readerQuiet = null;
      return;
    }

    List<ReaderQuietRider> stillRacing;
    TimeSpan? fieldPace;
    lock (ridersLock)
    {
      var field = riders.Values.Where(r => !ignoredTags.Contains(r.TagID)).ToList();
      fieldPace = RaceProgress.MedianPace(field);

      // Not a rider who has ridden their final lap, or been scored DNF: after the
      // flag the finish can wait minutes for a rider who retired, and nobody
      // crossing then is nothing to do with the reader.
      stillRacing = field
        .Where(r => !r.IsDNF && !r.IsDNS && r.TotalLaps < r.FinalAllowedLap)
        .Select(r => new ReaderQuietRider(r.Label, r.LastCrossing, TrackPositionSolver.UsablePace(r.RacingPace)))
        .ToList();
    }

    var verdict = ReaderQuietCheck.Evaluate(DateTime.Now, lastTagTime, stillRacing, fieldPace,
      readerQuietFromLapTimes, TimeSpan.FromSeconds(readerQuietSeconds));
    _readerQuiet = verdict;

    if (verdict.Level == ReaderQuietLevel.Silent)
    {
      if (_readerQuietNoticeShown) return;
      _readerQuietNoticeShown = true;
      _readerQuietAlarmLastRead = lastTagTime;
      RaiseNotice(NoticeLevel.Critical, verdict.Notice);
      AddMessage($"⚠️ {verdict.LogLine}");
    }
    else if (_readerQuietNoticeShown)
    {
      _readerQuietNoticeShown = false;
      ClearNotice();
      AddMessage(lastTagTime > _readerQuietAlarmLastRead
        ? "✅ Transponder reads have resumed"
        : "✅ Nobody is still expected at the line - the no-reads warning is cleared");
    }
  }
}
