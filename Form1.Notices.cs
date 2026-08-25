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
  /// one nothing in the application used to notice.
  /// </summary>
  private void CheckReaderHealth()
  {
    if (!raceStarted || raceFinished || !isListening)
    {
      _readerQuietNoticeShown = false;
      return;
    }

    if (lastTagTime == DateTime.MinValue) return;

    var since = DateTime.Now - lastTagTime;

    if (since.TotalSeconds > 60)
    {
      if (_readerQuietNoticeShown) return;
      _readerQuietNoticeShown = true;
      RaiseNotice(NoticeLevel.Critical,
        $"No transponder reads for {since.TotalSeconds:F0} seconds - check the reader");
      AddMessage($"⚠️ NO READS FOR {since.TotalSeconds:F0}s - check the reader and the loop");
    }
    else if (_readerQuietNoticeShown)
    {
      // Reads are back.
      _readerQuietNoticeShown = false;
      ClearNotice();
      AddMessage("✅ Transponder reads have resumed");
    }
  }
}
