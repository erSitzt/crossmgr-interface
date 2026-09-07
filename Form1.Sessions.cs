namespace CrossMgrInterface;

/// <summary>
/// Sessions as things that outlive the clock: printing a sheet from whatever
/// field is to hand - live or loaded from the database - and the Past
/// sessions window that lists, reopens, renames and deletes what is stored.
///
/// Its own partial file for the usual reason: Form1.cs is very large and the
/// designer rewrites Form1.Designer.cs wholesale.
/// </summary>
public partial class Form1
{
  /// <summary>
  /// Preview, print or export a race classification.
  ///
  /// The single tail behind the Results button and the Past sessions window.
  /// Both used to own a copy of the options dialog and the three-way switch,
  /// and the second copy would have been the third.
  /// </summary>
  /// <param name="flagAt">When the leader finished and the final-lap phase began, if it did.</param>
  /// <param name="actuallyEnded">When the race was called, if it was.</param>
  private void RunResultsReport(Dictionary<string, RiderInfo> field, DateTime? start, DateTime? end,
    TimeSpan duration, bool finished, DateTime? flagAt, DateTime? actuallyEnded, int extraLaps,
    string? defaultTitle)
  {
    using var options = new ReportOptionsDialog(defaultTitle);
    if (options.ShowDialog(this) != DialogResult.OK) return;

    var title = options.RaceTitle;

    switch (options.SelectedAction)
    {
      case ReportAction.Preview:
        _raceReportGenerator.ShowClassBasedPrintPreview(field, start, end, duration, finished, title,
          flagAt, actuallyEnded, extraLaps);
        break;

      case ReportAction.Print:
        _raceReportGenerator.PrintReport(field, start, end, duration, finished, title,
          flagAt, actuallyEnded, extraLaps);
        break;

      case ReportAction.Export:
        _raceReportGenerator.ExportToFile(field, start, end, duration, finished, title,
          flagAt, actuallyEnded, extraLaps);
        break;
    }
  }

  /// <summary>Preview, print or export a gate pick order. See <see cref="RunResultsReport"/>.</summary>
  private void RunGatePickReport(Dictionary<string, RiderInfo> field, string defaultTitle,
    DateTime? start, DateTime? end, TimeSpan duration, bool finished)
  {
    using var options = new ReportOptionsDialog(defaultTitle);
    if (options.ShowDialog(this) != DialogResult.OK) return;

    var title = options.RaceTitle;

    switch (options.SelectedAction)
    {
      case ReportAction.Preview:
        _qualifyingReportGenerator.ShowClassBasedPrintPreview(field, title, start, end, duration, finished);
        break;

      case ReportAction.Print:
        _qualifyingReportGenerator.PrintReport(field, title, start, end, duration, finished);
        break;

      case ReportAction.Export:
        _qualifyingReportGenerator.ExportToFile(field, title, start, end, duration, finished);
        break;
    }
  }

  private static string GatePickTitle(string? sessionName) =>
    string.IsNullOrWhiteSpace(sessionName)
      ? $"Gate Pick Order - {DateTime.Now:yyyy-MM-dd HH:mm}"
      : $"{sessionName} - Gate Pick Order";

  // ---- Past sessions -------------------------------------------------------

  private void ShowSessionManager()
  {
    using var dialog = new SessionManagerDialog(new SessionManagerHost(this));
    dialog.ShowDialog(this);
  }

  /// <summary>Prints whichever sheet the stored session produces.</summary>
  private void PrintStoredSession(DbRace race)
  {
    try
    {
      var gatePick = race.SessionType == SessionType.TimedQualifying;

      // The gate pick order lists the riders who never went out; a race
      // classification must not, or they would be scored as having started.
      var field = _raceDb.RestoreRiderData(race.Id, includeRosterOnly: gatePick);
      foreach (var tag in _raceDb.GetIgnoredTags(race.Id))
        field.Remove(tag);

      if (field.Count == 0)
      {
        MessageBox.Show(this,
          "Nothing was recorded in that session, so there is nothing to print.",
          "Nothing to print", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
      }

      if (gatePick)
      {
        RunGatePickReport(field, GatePickTitle(race.Name),
          race.StartTime, race.EndTime, race.Duration, race.IsFinished);
        return;
      }

      // The same values the live Results button passes: the calculated end
      // until the race is called, the true one after.
      RunResultsReport(field,
        race.StartTime,
        race.EndTime ?? race.StartTime + race.Duration,
        race.Duration,
        race.IsFinished,
        race.FinalLapsStartTime,
        race.IsFinished ? race.EndTime : null,
        race.AdditionalLaps ?? 0,
        race.Name);
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(this, "The results could not be produced.",
        "Nothing has been changed. The stored session is unaffected, so you can try again.", ex);
    }
  }

  /// <summary>
  /// Loads a stored session as the current one, through the same path crash
  /// recovery uses. Refused while a session is scoring.
  /// </summary>
  private bool OpenStoredSession(DbRace race)
  {
    if (raceStarted && !raceFinished)
    {
      var what = string.IsNullOrEmpty(raceName) ? "The current session" : $"'{raceName}'";
      MessageBox.Show(this,
        $"{what} is still running. End it first, then open a past session.",
        "Session running", MessageBoxButtons.OK, MessageBoxIcon.Information);
      return false;
    }

    if (currentRaceId == race.Id) return true;

    ResetForNextSession();
    RestoreRaceState(race);

    if (currentRaceId != race.Id) return false;

    AddMessage(string.IsNullOrEmpty(race.Name)
      ? $"📂 Opened the session from {race.StartTime:dd.MM.yyyy HH:mm}."
      : $"📂 Opened '{race.Name}' from past sessions.");
    tabControl.SelectedTab = tabPageRaceDay;
    return true;
  }

  private void RenameStoredSession(DbRace race, string name)
  {
    _raceDb.RenameRace(race.Id, name);

    if (currentRaceId == race.Id)
    {
      raceName = name;
      Text = $"CrossMgr - {raceName}";
      UpdateStatusBar();
    }

    AddMessage($"✏️ Session renamed to '{name}'.");
  }

  /// <summary>Asks, then deletes. Refused while the session is scoring.</summary>
  private bool DeleteStoredSession(IWin32Window owner, SessionSummary session)
  {
    var race = session.Race;
    var isCurrent = currentRaceId == race.Id;

    if (isCurrent && raceStarted && !raceFinished)
    {
      MessageBox.Show(owner,
        "That session is still running. End it first.",
        "Session running", MessageBoxButtons.OK, MessageBoxIcon.Information);
      return false;
    }

    var what = string.IsNullOrEmpty(race.Name) ? "this session" : $"'{race.Name}'";
    var answer = MessageBox.Show(owner,
      $"Delete {what}?\n\n{session.Riders} rider(s) and {session.Laps} recorded lap(s) " +
      "will be permanently deleted. This cannot be undone.",
      "Delete session", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
      MessageBoxDefaultButton.Button2);

    if (answer != DialogResult.Yes) return false;

    // Off the screen before it goes from the disk, or the finished session
    // would still be showing after its row was gone.
    if (isCurrent) ResetForNextSession();

    _raceDb.DeleteRace(race.Id);
    AddMessage($"🗑️ Deleted {what} from past sessions.");
    return true;
  }

  /// <summary>
  /// What the Past sessions window is allowed to ask of the form. An adapter
  /// rather than the form itself, as the refresh views do, so the dialog can
  /// only reach these seven things.
  /// </summary>
  private sealed class SessionManagerHost : ISessionManagerHost
  {
    private readonly Form1 _form;
    public SessionManagerHost(Form1 form) => _form = form;

    public IReadOnlyList<SessionSummary> ListSessions() => _form._raceDb.ListSessions();
    public int? CurrentSessionId => _form.currentRaceId;
    public bool SessionRunning => _form.raceStarted && !_form.raceFinished;
    public void PrintResults(SessionSummary session) => _form.PrintStoredSession(session.Race);
    public bool OpenSession(SessionSummary session) => _form.OpenStoredSession(session.Race);
    public void RenameSession(SessionSummary session, string name) => _form.RenameStoredSession(session.Race, name);
    public bool DeleteSession(IWin32Window owner, SessionSummary session) => _form.DeleteStoredSession(owner, session);
  }
}
