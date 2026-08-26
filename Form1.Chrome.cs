namespace CrossMgrInterface;

/// <summary>
/// The window's menu bar and status bar.
///
/// These replace twelve loose buttons scattered across the top of the form -
/// including a TCP port box and "Start Server", which were the first things a
/// volunteer saw. Every menu item delegates to the handler that already sat
/// behind the button it replaces.
/// </summary>
public partial class Form1
{
  private MenuStrip _menu = null!;
  private StatusStrip _statusBar = null!;

  private ToolStripMenuItem _menuStartRace = null!;
  private ToolStripMenuItem _menuEndRace = null!;
  private ToolStripMenuItem _menuGatePick = null!;
  private ToolStripMenuItem _menuTransponders = null!;
  private ToolStripMenuItem _menuStartReader = null!;
  private ToolStripMenuItem _menuStopReader = null!;
  private ToolStripMenuItem _menuUndo = null!;
  private ToolStripMenuItem _menuAdvanced = null!;
  private ToolStripMenuItem _menuShowTransponders = null!;

  private ToolStripStatusLabel _statusReader = null!;
  private ToolStripStatusLabel _statusLastRead = null!;
  private ToolStripStatusLabel _statusRaceState = null!;
  private ToolStripStatusLabel _statusRiders = null!;
  private ToolStripStatusLabel _statusNotice = null!;
  private ToolStripStatusLabel _statusRaceName = null!;

  private AppSettings _settings = new();

  private void InitializeChrome()
  {
    _settings = AppSettings.Load();
    advancedMode = _settings.AdvancedMode;
    readerPort = _settings.ReaderPort;
    verboseProtocolLogging = _settings.VerboseProtocolLogging;

    // Push the remembered race setup into the controls before Form1_Load reads
    // them, so the rest of startup picks these up rather than designer defaults.
    numericUpDownRaceDuration.Value = Math.Clamp(
      _settings.RaceDurationMinutes, numericUpDownRaceDuration.Minimum, numericUpDownRaceDuration.Maximum);
    numericUpDownAdditionalLaps.Value = Math.Clamp(
      _settings.AdditionalLaps, numericUpDownAdditionalLaps.Minimum, numericUpDownAdditionalLaps.Maximum);
    numericUpDownDnfTimeout.Value = Math.Clamp(
      _settings.DnfTimeoutMinutes, numericUpDownDnfTimeout.Minimum, numericUpDownDnfTimeout.Maximum);
    dnfTimeoutMinutes = _settings.DnfTimeoutMinutes;
    sessionType = _settings.SessionType;
    radioButtonStartManual.Checked = _settings.ManualStart;
    radioButtonStartOnFirstTag.Checked = !_settings.ManualStart;

    BuildMenu();
    BuildStatusBar();

    tabControl.Dock = DockStyle.Fill;
    Controls.Add(_statusBar);
    Controls.Add(_menu);
    MainMenuStrip = _menu;

    // WinForms lays docked children out from the HIGHEST z-order index down to
    // zero, each taking what it needs from the space left over. So the Fill
    // control has to sit at index 0 - laid out last, receiving the remainder -
    // with the edge-docked menu and status bar above it.
    //
    // Getting this backwards does not throw or warn: the tab control simply
    // claims the whole client area and the menu paints over the top of it,
    // hiding the tab strip completely.
    Controls.SetChildIndex(tabControl, 0);
    Controls.SetChildIndex(_statusBar, 1);
    Controls.SetChildIndex(_menu, 2);

    ApplyTransponderColumnVisibility();

    // Layout is not final until the form is actually on screen, so verify the
    // real geometry then rather than trusting it during Load.
    Shown += (_, _) => VerifyChromeLayout();
  }

  /// <summary>Records that the reader connection is open, or closed, across restarts.</summary>
  private void RememberReaderState(bool connected)
  {
    if (_settings.ReaderConnected == connected) return;
    _settings.ReaderConnected = connected;
    _settings.Save();
  }

  /// <summary>Records the race format so it survives a restart.</summary>
  private void RememberRaceSetup()
  {
    _settings.RaceDurationMinutes = (int)raceDuration.TotalMinutes;
    _settings.AdditionalLaps = additionalLapsAfterTimeExpiry;
    _settings.ManualStart = manualStartMode;
    _settings.DnfTimeoutMinutes = dnfTimeoutMinutes;
    _settings.SessionType = sessionType;
    _settings.Save();
  }

  /// <summary>Records the rider list in use, so a restart can reload it.</summary>
  private void RememberRiderList(string path)
  {
    _settings.LastRiderListPath = path;
    _settings.Save();
  }

  /// <summary>
  /// Puts back what was in place before the last shutdown: the rider list, and
  /// the reader connection. Runs after crash recovery so it does not fight it.
  /// </summary>
  private void RestorePreviousSession()
  {
    var path = _settings.LastRiderListPath;
    if (!string.IsNullOrEmpty(path) && _riderDataImporter.Count == 0)
    {
      try
      {
        if (File.Exists(path))
        {
          var result = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            ? _riderDataImporter.ImportFromCsvDetailed(path)
            : _riderDataImporter.ImportFromExcelDetailed(path);

          if (result.ImportedCount > 0)
          {
            ApplyImportedDataToExistingRiders();
            PopulateClassFilter();
            AddMessage($"📋 Reloaded {result.ImportedCount} riders from {Path.GetFileName(path)}");
          }
        }
        else
        {
          AddDiagnostic($"Previous rider list is no longer at {path}");
        }
      }
      catch (Exception ex)
      {
        AddDiagnostic($"Could not reload the previous rider list: {ex.Message}");
      }
    }

    if (_settings.ReaderConnected && !isListening)
    {
      AddMessage("🔌 Reconnecting to the reader (it was connected when the app last closed)");
      StartTcpListener(readerPort);
    }
  }

  /// <summary>
  /// Confirms the menu, tab strip and status bar are not overlapping. Getting the
  /// dock z-order wrong is silent - nothing throws, the tab strip just disappears
  /// behind the menu - so it is worth asserting once at startup.
  /// </summary>
  private void VerifyChromeLayout()
  {
    var overlapped = tabControl.Top < _menu.Bottom || tabControl.Bottom > _statusBar.Top;

    AddDiagnostic(
      $"Chrome layout: menu={_menu.Top}..{_menu.Bottom}, " +
      $"tabs={tabControl.Top}..{tabControl.Bottom} (rows={tabControl.RowCount}), " +
      $"status={_statusBar.Top}..{_statusBar.Bottom}, client={ClientSize.Width}x{ClientSize.Height}" +
      (overlapped ? "  *** OVERLAP - tab strip is hidden ***" : "  OK"));

    if (overlapped)
    {
      AddMessage("⚠️ The window layout is wrong - the tab strip is being covered. Please report this.");
    }
  }

  private void BuildMenu()
  {
    _menu = new MenuStrip { Dock = DockStyle.Top };

    // ---- Race ----
    var race = new ToolStripMenuItem("&Race");

    var newRace = Item("New race...", Keys.Control | Keys.N, (s, e) => RunNewRaceWizard());
    var import = Item("Import riders...", Keys.Control | Keys.I, buttonImportRiders_Click);
    _menuStartRace = Item("Start race", Keys.F5, buttonStartRace_Click);
    _menuEndRace = Item("End race now...", Keys.Control | Keys.E, (s, e) => EndRaceNow());
    var results = Item("Results...", Keys.Control | Keys.P, buttonGenerateReport_Click);
    _menuGatePick = Item("Gate pick order...", Keys.None, (s, e) => ShowQualifyingReport());
    _menuTransponders = Item("Transponder check...", Keys.None, (s, e) => ShowTransponderReport());
    var summary = Item("Rider summary", Keys.None, buttonShowSummary_Click);
    var clear = Item("Delete race data...", Keys.None, buttonClearRiders_Click);
    var exit = Item("Exit", Keys.None, (s, e) => Close());

    race.DropDownItems.AddRange(new ToolStripItem[]
    {
      newRace, import, new ToolStripSeparator(),
      _menuStartRace, _menuEndRace, new ToolStripSeparator(),
      results, _menuGatePick, _menuTransponders, summary, new ToolStripSeparator(),
      clear, exit
    });

    // ---- Riders ----
    var ridersMenu = new ToolStripMenuItem("Ri&ders");

    var fixLaps = Item("Fix laps...", Keys.F2, (s, e) => OpenLapCorrectionForMostUrgentRider());
    _menuUndo = Item("Undo last change", Keys.Control | Keys.Z, (s, e) => UndoLastCorrection());
    var ignored = Item("Ignored transponders...", Keys.None, (s, e) => ShowIgnoreList());

    _menuShowTransponders = new ToolStripMenuItem("Show transponder IDs")
    {
      CheckOnClick = true,
      Checked = _settings.ShowTransponderIds
    };
    _menuShowTransponders.CheckedChanged += (_, _) =>
    {
      ApplyTransponderColumnVisibility();
      _settings.ShowTransponderIds = _menuShowTransponders.Checked;
      _settings.Save();
    };

    ridersMenu.DropDownItems.AddRange(new ToolStripItem[]
    {
      fixLaps, _menuUndo, new ToolStripSeparator(), ignored, _menuShowTransponders
    });

    // ---- Reader ----
    // Alt+E, not Alt+D: "Ri&ders" already claims D, and two menus sharing a
    // mnemonic means the key cycles between them instead of opening either.
    var reader = new ToolStripMenuItem("Read&er");

    _menuStartReader = Item("Start reader connection", Keys.None, buttonStart_Click);
    _menuStopReader = Item("Stop reader connection", Keys.None, buttonStop_Click);
    var readerSettings = Item("Connection settings...", Keys.None, (s, e) => ShowReaderSettings());
    var clearReaderLog = Item("Clear reader log", Keys.None, buttonClearTagEvents_Click);

    reader.DropDownItems.AddRange(new ToolStripItem[]
    {
      _menuStartReader, _menuStopReader, new ToolStripSeparator(), readerSettings, clearReaderLog
    });

    // ---- View ----
    var view = new ToolStripMenuItem("&View");

    var goRaceDay = Item("Race day", Keys.Control | Keys.D1, (s, e) => tabControl.SelectedTab = tabPageRaceDay);
    var goRiders = Item("Riders", Keys.Control | Keys.D2, (s, e) => tabControl.SelectedTab = tabPageRiders);
    var goTrack = Item("Track map", Keys.Control | Keys.D3, (s, e) => tabControl.SelectedTab = tabPageTrack);

    _menuAdvanced = new ToolStripMenuItem("Show advanced tabs")
    {
      CheckOnClick = true,
      Checked = advancedMode,
      ShortcutKeys = Keys.Control | Keys.Shift | Keys.A
    };
    _menuAdvanced.CheckedChanged += (_, _) =>
    {
      advancedMode = _menuAdvanced.Checked;
      RebuildTabs();
      _settings.AdvancedMode = advancedMode;
      _settings.Save();
    };

    var clearLog = Item("Clear event log", Keys.None, buttonClear_Click);

    view.DropDownItems.AddRange(new ToolStripItem[]
    {
      goRaceDay, goRiders, goTrack, _menuAdvanced, new ToolStripSeparator(), clearLog
    });

    // ---- Help ----
    var help = new ToolStripMenuItem("&Help");
    help.DropDownItems.AddRange(new ToolStripItem[]
    {
      Item("Quick start...", Keys.F1, (s, e) => ShowQuickStart()),
      Item("Open log folder", Keys.None, (s, e) => OpenLogFolder()),
      Item("About", Keys.None, (s, e) => MessageBox.Show(this,
        "CrossMgr RFID Interface\n\nTimes motocross races from transponder reads.",
        "About", MessageBoxButtons.OK, MessageBoxIcon.Information))
    });

    _menu.Items.AddRange(new ToolStripItem[] { race, ridersMenu, reader, view, help });

    // Keep enabled/disabled in step with race state each time a menu opens.
    race.DropDownOpening += (_, _) => UpdateCommandStates();
    ridersMenu.DropDownOpening += (_, _) => UpdateCommandStates();
    reader.DropDownOpening += (_, _) => UpdateCommandStates();
  }

  private static ToolStripMenuItem Item(string text, Keys shortcut, EventHandler onClick)
  {
    var item = new ToolStripMenuItem(text);
    if (shortcut != Keys.None) item.ShortcutKeys = shortcut;
    item.Click += onClick;
    return item;
  }

  private void BuildStatusBar()
  {
    _statusBar = new StatusStrip { Dock = DockStyle.Bottom, SizingGrip = false };

    _statusReader = new ToolStripStatusLabel("Reader off") { ForeColor = Color.Firebrick };
    _statusLastRead = new ToolStripStatusLabel("");
    _statusRaceState = new ToolStripStatusLabel("Not started");
    _statusRiders = new ToolStripStatusLabel("");
    _statusNotice = new ToolStripStatusLabel("") { Spring = true, TextAlign = ContentAlignment.MiddleRight };
    _statusRaceName = new ToolStripStatusLabel("") { ForeColor = Color.DimGray };

    _statusBar.Items.AddRange(new ToolStripItem[]
    {
      _statusReader, Separator(), _statusLastRead, Separator(),
      _statusRaceState, Separator(), _statusRiders,
      _statusNotice, Separator(), _statusRaceName
    });
  }

  private static ToolStripStatusLabel Separator() =>
    new("|") { ForeColor = Color.LightGray };

  /// <summary>Enables only the commands that make sense in the current state.</summary>
  private void UpdateCommandStates()
  {
    _menuStartRace.Enabled = manualStartMode && !raceStarted && !raceFinished;
    _menuStartRace.Text = IsTimedSession ? "Start session" : "Start race";
    _menuEndRace.Enabled = raceStarted && !raceFinished;
    _menuEndRace.Text = IsTimedSession ? "End session now..." : "End race now...";

    // Shown only for a qualifying session: after a race there is no gate pick
    // order, and offering one beside Results... invites printing the wrong sheet.
    _menuGatePick.Visible = IsQualifying;

    // Practice and qualifying both: a tag can still be re-fitted before the race.
    _menuTransponders.Visible = IsTimedSession;
    _menuStartReader.Enabled = !isListening;
    _menuStopReader.Enabled = isListening;

    _menuUndo.Enabled = _corrections.History.CanUndo;
    _menuUndo.Text = _corrections.History.CanUndo
      ? $"Undo: {_corrections.History.NextUndoDescription}"
      : "Nothing to undo";
  }

  /// <summary>Refreshes the status bar. Driven by the same heartbeat as the views.</summary>
  private void UpdateStatusBar()
  {
    var connections = ConnectedClientCount();

    if (!isListening)
    {
      _statusReader.Text = "Reader off";
      _statusReader.ForeColor = Color.Firebrick;
    }
    else if (connections == 0)
    {
      _statusReader.Text = "Waiting for reader";
      _statusReader.ForeColor = Color.DarkOrange;
    }
    else
    {
      _statusReader.Text = connections == 1 ? "Reader connected" : $"{connections} readers connected";
      _statusReader.ForeColor = Color.DarkGreen;
    }

    _statusLastRead.Text = lastTagTime == DateTime.MinValue
      ? "No reads yet"
      : $"Last read {(DateTime.Now - lastTagTime).TotalSeconds:F0}s ago";

    if (raceFinished) _statusRaceState.Text = "Finished";
    else if (!raceStarted) _statusRaceState.Text = manualStartMode ? "Ready to start" : "Waiting for first rider";
    else _statusRaceState.Text = $"Running - {GetTimeRemaining():mm\\:ss} left";

    int riderCount, lapCount;
    lock (ridersLock)
    {
      var active = riders.Values.Where(r => !ignoredTags.Contains(r.TagID)).ToList();
      riderCount = active.Count;
      lapCount = active.Sum(r => r.TotalLaps);
    }
    _statusRiders.Text = $"{riderCount} riders · {lapCount} laps";

    _statusRaceName.Text = raceName;
  }

  /// <summary>Mirrors the Race Day banner so a warning is visible on every tab.</summary>
  private void SetStatusNotice(NoticeLevel level, string message)
  {
    _statusNotice.Text = message;
    _statusNotice.ForeColor = level switch
    {
      NoticeLevel.Critical => Color.Firebrick,
      NoticeLevel.Warning => Color.DarkOrange,
      _ => Color.DimGray
    };
  }

  private void ClearStatusNotice() => _statusNotice.Text = "";

  private void ApplyTransponderColumnVisibility()
  {
    var column = dataGridViewRiders.Columns["TagID"];
    if (column != null) column.Visible = _menuShowTransponders.Checked;
  }

  private void ShowReaderSettings()
  {
    using var dialog = new ReaderSettingsDialog(readerPort, isListening);
    if (dialog.ShowDialog(this) != DialogResult.OK) return;

    readerPort = dialog.Port;
    _settings.ReaderPort = readerPort;
    _settings.VerboseProtocolLogging = dialog.VerboseLogging;
    _settings.Save();

    verboseProtocolLogging = dialog.VerboseLogging;
    AddMessage($"⚙️ Reader will connect on port {readerPort}");
  }

  private void OpenLogFolder()
  {
    try
    {
      System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
      {
        FileName = AppPaths.LogsFolder,
        UseShellExecute = true
      });
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(this, "The log folder could not be opened.",
        $"You can find it at:\n{AppPaths.LogsFolder}", ex);
    }
  }

  private void ShowQuickStart()
  {
    MessageBox.Show(this,
      "Running a race\n\n" +
      "1. Reader > Start reader connection.\n" +
      "2. Race > Import riders... and choose the rider list.\n" +
      "3. Set the race length under Race Settings (advanced tabs).\n" +
      "4. Either press START RACE, or let the clock start on the first rider.\n" +
      "5. Watch the Race Day screen. The reader light turns red if reads stop.\n" +
      "6. If a lap looks wrong, right-click the rider and choose Fix laps.\n" +
      "   Every change can be undone with Ctrl+Z.\n" +
      "7. When the race is over, press Results...",
      "Quick start", MessageBoxButtons.OK, MessageBoxIcon.Information);
  }
}
