using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.IO;

namespace CrossMgrInterface;

public partial class Form1 : Form
{
  private TcpListener? tcpListener;
  private bool isListening = false;
  private readonly List<TcpClient> connectedClients = new();
  private readonly object clientsLock = new object();

  // Database service
  private readonly RaceDataService _raceDb;

  // Extracted manager classes
  private readonly LapChartRenderer _lapChartRenderer;
  private readonly LapProgressionManager _lapProgressionManager;
  private readonly RaceReportGenerator _raceReportGenerator;
  private readonly RiderDataImporter _riderDataImporter;

  // Rider tracking
  private readonly Dictionary<string, RiderInfo> riders = new();
  private readonly object ridersLock = new object();

  // Race tracking
  private DateTime? raceStartTime = null;
  private string lastTagID = "None";
  private DateTime lastTagTime = DateTime.MinValue;
  private int? currentRaceId = null;
  private bool manualStartMode = false;

  /// <summary>
  /// Practice, qualifying or a race. Chosen in setup; it is not race state, so
  /// ClearRiderData deliberately leaves it alone.
  /// </summary>
  private SessionType sessionType = SessionType.Race;

  /// <summary>
  /// The clock is a chequered flag rather than a laps target: at time expiry
  /// every rider finishes the lap they are on and that lap counts, with no
  /// extra laps and no wait for the leader. True for both practice formats.
  /// </summary>
  private bool IsTimedSession => sessionType != SessionType.Race;

  /// <summary>Gate pick order is derived, and the Qualifying tab is shown.</summary>
  private bool IsQualifying => sessionType == SessionType.TimedQualifying;
  private bool raceStarted = false;
  private bool raceFinished = false;
  private bool raceTimeExpired = false; // Track when race time has expired but ongoing lap not yet completed
  private bool waitingForLeaderFinish = false;
  private bool waitingForFinalLaps = false; // Track when leader finished but other riders completing their current lap
  private DateTime? finalLapsStartTime = null; // Track when final laps phase started
  private string? leaderAtTimeExpiry = null;
  private int leaderLapsAtTimeExpiry = 0; // Track leader's lap count when time expired
  private int targetLapsToFinishRace = 0; // Absolute target lap count to finish race (set when ongoing lap completes)

  // Race duration settings
  private TimeSpan raceDuration = TimeSpan.FromMinutes(20); // Default 20 minutes
  private DateTime? raceEndTime = null;
  private bool fiveMinuteWarningShown = false;
  private bool oneMinuteWarningShown = false;
  private int additionalLapsAfterTimeExpiry = 1; // Configurable number of laps after time expires
  private int dnfTimeoutMinutes = 2; // Configurable DNF timeout in minutes

  /// <summary>How hard to look for a missed read. Restored from settings on load.</summary>
  private LapAnomalySettings missedReadSettings = LapAnomalySettings.Default;

  // Lap time validation
  private TimeSpan minimumLapTime = TimeSpan.FromSeconds(10); // Ignore laps shorter than this
  private bool shortLapDetectionEnabled = true;

  // Reads that were not counted, kept so they can be reviewed and reinstated.
  private const int MaxRejectedReads = 500;
  private readonly List<RejectedRead> rejectedReads = new();

  /// <summary>TCP port the reader connects on. Persisted; edited under Reader > Connection settings.</summary>
  private int readerPort = 53135;

  // Tag filtering
  private string tagFilterPrefix = "";
  private bool tagFilterEnabled = false;
  private int filteredTagCount = 0;

  // Tag ignore list for excluding specific tags from processing.
  // Read on the network thread for every tag read, written on the UI thread.
  private readonly ThreadSafeTagSet ignoredTags = new();
  private int ignoredTagCount = 0;

  // Logging
  private string logFilePath = "";
  private StreamWriter? logWriter;
  private readonly System.Collections.Concurrent.BlockingCollection<string> logQueue =
    new(new System.Collections.Concurrent.ConcurrentQueue<string>(), boundedCapacity: 10000);
  private Task? logWriterTask;
  private bool verboseProtocolLogging = false;

  // Position tracking for race events (now backed by database)
  private Dictionary<string, int> lastKnownPositions = new();
  private readonly Dictionary<(string, string), DateTime> lastBattleAnnounced = new();
  private readonly Dictionary<(string, string), int> lapDifferences = new();

  /// <summary>
  /// Serialises the read-compare-store cycle over lastKnownPositions. Position
  /// checks run on a task per crossing, and two crossings milliseconds apart
  /// would otherwise both compare against the same stale baseline.
  /// </summary>
  private readonly object positionCheckLock = new object();
  private readonly object lapDifferencesLock = new object();
  private static readonly TimeSpan BattleAnnouncementCooldown = TimeSpan.FromSeconds(30);
  private Dictionary<string, int> lastKnownLapCounts = new();
  private DateTime lastPositionCheck = DateTime.MinValue;

  // Lap chart visualization fields removed - now handled by LapChartRenderer
  private DateTime lastProgressLineUpdate = DateTime.MinValue;

  // Lap progression tracking

  // Class filtering
  private string selectedClassFilter = "All Classes";

  // Runtime-created tab pages. Tab order is owned solely by RebuildTabs(); never
  // compare against tabControl.SelectedIndex - the indices shift whenever a tab
  // is added, removed or hidden.
  private TabPage tabPageLapProgression = null!;
  // tabPageTrack is declared in Form1.Track.cs, beside the rest of its plumbing.

  /// <summary>Whether the technical tabs are shown. Off by default.</summary>
  private bool advancedMode = false;

  public Form1()
  {
    InitializeComponent();

    // Initialize database service
    _raceDb = new RaceDataService(AppPaths.DatabaseFile);

    // Initialize extracted manager classes
    _lapChartRenderer = new LapChartRenderer();
    _lapProgressionManager = new LapProgressionManager();
    _raceReportGenerator = new RaceReportGenerator();
    _riderDataImporter = new RiderDataImporter();

    this.Load += Form1_Load;
    InitializeRidersDataGrid();
    PopulateClassFilter();

    // Add event handler for tab changes
    tabControl.SelectedIndexChanged += TabControl_SelectedIndexChanged;
  }

  private void TabControl_SelectedIndexChanged(object? sender, EventArgs e)
  {
    // Anything that changed while this tab was hidden kept its dirty bit, so it
    // repaints here. A view that has never painted gets its first paint too.
    _refresh?.OnTabChanged();
  }

  /// <summary>
  /// Single authority on which tab pages are shown and in what order.
  /// TabPages.Remove/Clear does not dispose a page or its event handlers, so
  /// pages can be taken out and put back without losing state.
  /// </summary>
  private void RebuildTabs()
  {
    var previouslySelected = tabControl.SelectedTab;

    // Simple mode is Race Day plus the riders grid; the grid stays because it is
    // where corrections are made. Everything else is for whoever wants detail.
    //
    // The track map is in simple mode too. "Where is everyone?" is the most asked
    // question of the day and the person being asked is exactly the volunteer
    // running this view; the advanced tabs are diagnostics, and this is not. It
    // comes after Riders because tab order puts what you need in a hurry first.
    var desired = new List<TabPage> { tabPageRaceDay, tabPageRiders };

    // Only during a qualifying session. In a race there is no gate pick order
    // to show, and an empty tab reading "ranked by best lap" beside a race
    // leaderboard is an invitation to read the wrong sheet.
    if (IsQualifying && tabPageQualifying != null) desired.Add(tabPageQualifying);

    // Any timed session. Practice is the session whose whole point is finding
    // bad tags, and qualifying is the last chance to fix one before the race.
    if (IsTimedSession && tabPageTransponder != null) desired.Add(tabPageTransponder);

    desired.Add(tabPageTrack);

    if (advancedMode)
    {
      desired.AddRange(new[]
      {
        tabPageLive,
        tabPageTagEvents,
        tabPageStats,
        tabPageLapChart,
        tabPageLapProgression,
        tabPageRaceSettings
      });
    }

    // With eight tabs a single row can overflow on a smaller screen, and a
    // TabControl hides the overflow behind scroll arrows rather than saying so -
    // a tab simply appears to be missing. Wrapping keeps every tab reachable.
    tabControl.Multiline = true;

    tabControl.SuspendLayout();
    tabControl.TabPages.Clear();
    foreach (var page in desired)
    {
      if (page != null)
        tabControl.TabPages.Add(page);
    }
    tabControl.ResumeLayout();

    tabControl.SelectedTab = previouslySelected != null && desired.Contains(previouslySelected)
      ? previouslySelected
      : desired[0];

    // Records both the tab set and the geometry. If the tab control's top edge
    // is not below the menu bar, the menu is covering the tab strip.
    AddDiagnostic($"Tabs (advanced={advancedMode}): " +
      string.Join(" | ", tabControl.TabPages.Cast<TabPage>().Select(t => t.Text)) +
      $"  [shown={tabControl.TabPages.Count}, tabs.Top={tabControl.Top}, " +
      $"menu.Bottom={_menu?.Bottom}, tabs.Height={tabControl.Height}, rows={tabControl.RowCount}]");
  }

  private void Form1_Load(object? sender, EventArgs e)
  {
    // Logging first of all, so the startup sequence itself is on record.
    InitializeLogging();

    // Infrastructure next. Almost everything below - UpdateConnectionCount,
    // UpdateUI, the settings handlers - now writes to the status bar or asks the
    // correction service a question, so these have to exist before any of it runs.
    tabPageLapProgression = _lapProgressionManager.CreateLapProgressionTab();
    InitializeRaceDayView();
    InitializeChrome();

    // After InitializeChrome, which is what loads _settings - the track view
    // needs LastTrackId to restore the circuit that was last on screen, and the
    // qualifying tab is only shown at all when _settings says this is a
    // qualifying session.
    InitializeTrackView();
    InitializeQualifyingView();
    InitializeTransponderView();
    InitializeRefreshCoordinator();
    InitializeCorrections();
    _lapProgressionManager.RefreshRequested += () => _refresh.RenderNow(RaceViewKind.LapProgression);

    // Applies the session type remembered from last time - and rebuilds the tabs
    // as it does, so this stands in for the plain RebuildTabs that was here.
    ApplySessionTypeToUi();

    // Volunteers land on the calm screen, not on a protocol trace.
    tabControl.SelectedTab = tabPageRaceDay;

    AddMessage("Application started. Ready to listen for RFID messages.");
    UpdateConnectionCount();

    // Initialize race duration from the numeric control
    raceDuration = TimeSpan.FromMinutes((double)numericUpDownRaceDuration.Value);

    // Initialize additional laps setting
    additionalLapsAfterTimeExpiry = (int)numericUpDownAdditionalLaps.Value;

    // Initialize short-lap rejection from its (previously inert) controls
    minimumLapTime = TimeSpan.FromSeconds((double)numericUpDownMinimumLapTime.Value);
    shortLapDetectionEnabled = checkBoxShortLapDetection.Checked;
    numericUpDownDnfTimeout.Value = dnfTimeoutMinutes;

    buttonSetShortLapSettings.Click += buttonSetShortLapSettings_Click;
    buttonSetDnfTimeout.Click += buttonSetDnfTimeout_Click;
    buttonMissedReadSettings.Click += (_, _) => ShowMissedReadSettings();

    // Initialize tag filter controls
    textBoxTagFilter.PlaceholderText = "e.g., RIDER, 1000, BIKE (comma-separated)";
    checkBoxFilterEnabled.Checked = false;
    tagFilterEnabled = false;
    AddMessage("🔍 Tag filter: Disabled (all tags will be processed)");
    AddMessage($"⚙️ DNF timeout: {dnfTimeoutMinutes} minutes after leader finishes");

    // On record at startup, because a start mode that disagreed with the radio
    // was invisible in the log until someone noticed the button was disabled.
    AddMessage(manualStartMode
      ? "⚙️ Start mode: manual - the clock starts when you press Start Race"
      : "⚙️ Start mode: automatic - the clock starts on the first transponder read");

    // Enable double buffering for the lap chart panel to reduce flickering
    typeof(Panel).InvokeMember("DoubleBuffered",
      BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
      null, panelLapChart, new object[] { true });

    // Add mouse event handlers for lap chart interaction
    panelLapChart.MouseClick += PanelLapChart_MouseClick;
    panelLapChart.MouseMove += PanelLapChart_MouseMove;
    panelLapChart.MouseLeave += PanelLapChart_MouseLeave;

    // Add context menu to tag events list
    InitializeTagEventsContextMenu();

    // Set up race start mode controls
    radioButtonSessionRace.CheckedChanged += SessionType_CheckedChanged;
    radioButtonSessionQualifying.CheckedChanged += SessionType_CheckedChanged;
    radioButtonSessionPractice.CheckedChanged += SessionType_CheckedChanged;
    radioButtonStartOnFirstTag.CheckedChanged += RaceStartMode_CheckedChanged;
    radioButtonStartManual.CheckedChanged += RaceStartMode_CheckedChanged;
    UpdateRaceStartControls();

    InitializeTooltips();

    // Crash recovery and session restore deliberately do NOT run here. Recovery
    // asks the operator a question, and a modal shown from Load blocks before the
    // main window has appeared - so the app looks hung rather than like it is
    // asking something. Both run from Shown instead; see StartupAfterShown.
    Shown += StartupAfterShown;
  }

  private readonly ToolTip toolTipMain = new() { AutoPopDelay = 15000, InitialDelay = 400, ReshowDelay = 200 };

  /// <summary>
  /// Explains the settings that are not self-evident. The application had no
  /// tooltips at all, and several of these controls change how a race is scored.
  /// </summary>
  private void InitializeTooltips()
  {
    toolTipMain.SetToolTip(numericUpDownRaceDuration,
      "How long the clock runs. Riders may still finish the lap they are on when it hits zero.");
    toolTipMain.SetToolTip(numericUpDownAdditionalLaps,
      "After the clock reaches zero the leader still rides this many more laps before the flag.");
    toolTipMain.SetToolTip(numericUpDownMinimumLapTime,
      "Anything faster than this is treated as the finish line seeing the same rider twice, not as a lap.");
    toolTipMain.SetToolTip(checkBoxShortLapDetection,
      "Turn off only if the course is genuinely short enough for real laps to fall below the limit.");
    toolTipMain.SetToolTip(numericUpDownDnfTimeout,
      "Once the leader finishes, riders get this long to complete their last lap before being scored DNF.");
    toolTipMain.SetToolTip(textBoxTagFilter,
      "Only count transponders whose ID starts with one of these. Leave empty to count everything.");
    toolTipMain.SetToolTip(checkBoxFilterEnabled,
      "Turn the transponder filter on. With it off, every transponder is counted.");
    toolTipMain.SetToolTip(radioButtonStartOnFirstTag,
      "The clock starts by itself the moment the first rider crosses the line.");
    toolTipMain.SetToolTip(radioButtonStartManual,
      "You press Start Race. Use this when the gate drop and the first crossing are not the same moment.");
    toolTipMain.SetToolTip(buttonStartRace, "Start the clock now.");
    toolTipMain.SetToolTip(comboBoxClassFilter, "Show only one class in the list below.");

    SetColumnTooltip("Status", "Anything needing attention: CHECK is a possible missed read, DNF/DNS a finish status.");
    SetColumnTooltip("ProjectedPosition", "Where this rider would be if the flagged missed read were corrected.");
    SetColumnTooltip("PredictedLap", "Expected next lap time, weighted towards their most recent laps.");
    SetColumnTooltip("NextCrossing", "Race time this rider is expected to cross next.");
    SetColumnTooltip("TimeToNext", "How long until they are due. \"Overdue\" means they have not appeared.");
    SetColumnTooltip("Gap", "Behind the leader: seconds on the same lap, or how many laps down.");

    void SetColumnTooltip(string column, string text)
    {
      var c = dataGridViewRiders.Columns[column];
      if (c != null) c.ToolTipText = text;
    }
  }

  private bool startupCompleted;

  /// <summary>
  /// Work that must happen only once the main window is on screen: reopening the
  /// reader connection and reloading the rider list, then offering to recover an
  /// interrupted race. The recovery prompt is a modal, so the operator needs to
  /// be looking at the application when it appears.
  /// </summary>
  private void StartupAfterShown(object? sender, EventArgs e)
  {
    if (startupCompleted) return;
    startupCompleted = true;

    // Reader and roster first: they never prompt, so the app is usable even if
    // the operator leaves the recovery question sitting there.
    RestorePreviousSession();

    // Say so plainly if the previous database could not be read. Silently
    // starting with an empty one would look like the race data had vanished.
    if (_raceDb.RecoveredFromUnreadableDatabase)
    {
      AddMessage("⚠️ The previous race database could not be read. A new one has been started.");
      ErrorDialog.Show(this,
        "The saved race data could not be opened.",
        "A new, empty database has been started so you can carry on timing.\n\n" +
        $"The old file has been kept as:\n{_raceDb.QuarantinedDatabasePath}");

      // Nothing to recover from a database that was created moments ago.
      return;
    }

    AttemptCrashRecovery();
  }

  private void buttonStart_Click(object? sender, EventArgs e)
  {
    StartTcpListener(readerPort);
  }

  private void buttonStop_Click(object? sender, EventArgs e)
  {
    StopTcpListener();

    // Deliberate disconnection by the operator: remember it, so the next start
    // does not reopen the port behind their back. Shutdown deliberately does not
    // do this - closing the application is not a decision to stop timing.
    RememberReaderState(false);
  }

  private void buttonClear_Click(object? sender, EventArgs e)
  {
    listBoxMessages.Items.Clear();
  }

  private void buttonClearTagEvents_Click(object? sender, EventArgs e)
  {
    listBoxTagEvents.Items.Clear();
  }

  /// <summary>
  /// Ctrl+Z undoes the last correction from anywhere in the application, so an
  /// operator who has just made a mistake does not have to hunt for a menu.
  /// </summary>
  protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
  {
    if (keyData == (Keys.Control | Keys.Z))
    {
      UndoLastCorrection();
      return true;
    }
    return base.ProcessCmdKey(ref msg, keyData);
  }

  private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
  {
    // Cancels tile fetches and disposes the decoded-tile cache, which is
    // unmanaged memory the GC will not reclaim in time on its own.
    _trackTab?.Dispose();

    StopTcpListener();

    // Write final log entry
    WriteToLogFile("SYSTEM", $"=== CrossMgr Interface Log Ended at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
    ShutdownLogging();
    _refresh?.Dispose();
  }

  private void StartTcpListener(int port)
  {
    try
    {
      tcpListener = new TcpListener(IPAddress.Any, port);
      tcpListener.Start();
      isListening = true;

      UpdateUI();
      RememberReaderState(true);
      AddDiagnostic($"TCP server started on port {port}. Waiting for connections...");

      // Start accepting connections
      _ = Task.Run(AcceptConnectionsAsync);
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(this,
        "The reader connection could not be started.",
        $"Another program may already be using port {port}. Close it, or choose a " +
        "different port and try again.", ex);
      UpdateUI();
    }
  }

  private void StopTcpListener()
  {
    isListening = false;

    try
    {
      tcpListener?.Stop();

      lock (clientsLock)
      {
        foreach (var client in connectedClients.ToList())
        {
          client?.Close();
        }
        connectedClients.Clear();
      }

      AddDiagnostic("TCP server stopped.");
    }
    catch (Exception ex)
    {
      AddDiagnostic($"Error stopping the reader connection: {ex.Message}");
    }

    UpdateUI();
    UpdateConnectionCount();
  }

  private async Task AcceptConnectionsAsync()
  {
    while (isListening && tcpListener != null)
    {
      try
      {
        var tcpClient = await tcpListener.AcceptTcpClientAsync();

        lock (clientsLock)
        {
          connectedClients.Add(tcpClient);
        }

        var clientEndpoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";
        AddTagEvent($"Client connected from: {clientEndpoint}");
        UpdateConnectionCount();

        // Handle client in separate task
        _ = Task.Run(() => HandleClientAsync(tcpClient));
      }
      catch (Exception ex) when (isListening)
      {
        AddMessage($"Error accepting connection: {ex.Message}");
      }
    }
  }

  private async Task HandleClientAsync(TcpClient client)
  {
    var clientEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
    var buffer = new byte[1024];
    var stringBuilder = new StringBuilder();

    try
    {
      var stream = client.GetStream();

      while (client.Connected && isListening)
      {
        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

        if (bytesRead == 0)
          break; // Client disconnected

        string receivedData = Encoding.ASCII.GetString(buffer, 0, bytesRead);
        stringBuilder.Append(receivedData);

        // Process complete messages (assuming messages end with CR or LF)
        string allData = stringBuilder.ToString();

        // Raw byte-level trace. Off by default: building the hex string costs a
        // byte array, a LINQ projection, one string per byte and a join, on every
        // single socket read.
        if (verboseProtocolLogging && allData.Length > 0 && allData.Length < 200)
        {
          AddTagEvent($"[{clientEndpoint}] RAW: '{allData}' (hex: {Convert.ToHexString(Encoding.ASCII.GetBytes(allData))})");
        }

        string[] lines = allData.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        // Keep the incomplete message in the buffer
        if (allData.EndsWith('\r') || allData.EndsWith('\n'))
        {
          stringBuilder.Clear();
        }
        else
        {
          // Last line might be incomplete, keep it
          stringBuilder.Clear();
          if (lines.Length > 0)
          {
            for (int i = 0; i < lines.Length - 1; i++)
            {
              await ProcessMessage(lines[i], stream, clientEndpoint);
            }
            stringBuilder.Append(lines[lines.Length - 1]);
          }
          continue;
        }

        // Process all complete lines
        foreach (string line in lines)
        {
          if (!string.IsNullOrWhiteSpace(line))
          {
            // Reduced verbosity - don't log processing details for every line
            await ProcessMessage(line, stream, clientEndpoint);
          }
        }
      }
    }
    catch (Exception ex)
    {
      AddTagEvent($"Error handling client {clientEndpoint}: {ex.Message}");
    }
    finally
    {
      lock (clientsLock)
      {
        connectedClients.Remove(client);
      }

      client.Close();
      AddTagEvent($"Client disconnected: {clientEndpoint}");
      UpdateConnectionCount();
    }
  }

  private async Task ProcessMessage(string message, NetworkStream stream, string clientEndpoint)
  {
    try
    {
      AddTagEvent($"[{clientEndpoint}] Received: '{message}' (Length: {message.Length})");

      if (message.StartsWith("GT"))
      {
        if (message.Length > 2 && message.Contains("date="))
        {
          // This is a GT response from the reader with time/date info
          ParseGTResponse(message, clientEndpoint);
        }
        else
        {
          // GetTime request - respond with current time
          var now = DateTime.Now;
          var response = $"GT{now:HHmmssfff} date={now:yyyyMMdd}\r"; // CrossMgr uses CR only

          byte[] responseBytes = Encoding.ASCII.GetBytes(response);
          await stream.WriteAsync(responseBytes, 0, responseBytes.Length);

          AddTagEvent($"[{clientEndpoint}] Sent: {response.TrimEnd()}");
        }
      }
      else if (message.StartsWith("S0000"))
      {
        // Setup command - acknowledge
        AddTagEvent($"[{clientEndpoint}] Setup command received");
        // CrossMgr typically doesn't need a response to S0000
      }
      else if (message.StartsWith("DA"))
      {
        // Tag read message - parse and display nicely
        ParseAndDisplayTagRead(message, clientEndpoint);
      }
      else if (message.StartsWith("N") && message.Length > 5)
      {
        // Name/ID message format: N{4digits}{hostname-suffix}
        // Example: N0000DESKTOP-V48S27K-24428
        // Extract the reader name (skip N and first 4 digits)
        string readerName = message.Length > 5 ? message.Substring(5) : message;
        AddTagEvent($"[{clientEndpoint}] 📋 Reader identification: {readerName} (full: {message})");

        // According to CrossMgr protocol (based on Impinj2JChip.py), the client sends identifier
        // and then waits for the server to send GT command. Adding realistic delay to match
        // the expected timing where the reader waits for the server to respond.
        // Note: Client has 2-second socket timeout, so delay must be well under 2 seconds.
        AddTagEvent($"[{clientEndpoint}] ⏳ Waiting 500ms before sending GT command (protocol timing)...");

        // Use Task.Delay to avoid blocking the UI thread
        _ = Task.Run(async () =>
        {
          AddDiagnostic($"[{clientEndpoint}] ⏳ Starting 500ms delay timer...");
          await Task.Delay(500); // 500ms delay - well under the 2-second client timeout
          AddTagEvent($"[{clientEndpoint}] ⏰ Delay complete, sending GT command now...");

          try
          {
            // Find the client connection for this endpoint  
            TcpClient? targetClient = null;
            lock (clientsLock)
            {
              targetClient = connectedClients.FirstOrDefault(client =>
                client.Client.RemoteEndPoint?.ToString() == clientEndpoint);
            }

            if (targetClient?.Connected == true)
            {
              var clientStream = targetClient.GetStream();
              var gtCommand = "GT\r"; // CrossMgr protocol uses CR only, not CRLF
              byte[] gtBytes = Encoding.ASCII.GetBytes(gtCommand);
              await clientStream.WriteAsync(gtBytes, 0, gtBytes.Length);

              AddTagEvent($"[{clientEndpoint}] 📤 Sent GT command to initialize reader (after delay)");
            }
            else
            {
              AddTagEvent($"[{clientEndpoint}] ❌ Cannot send GT - client disconnected during delay");
            }
          }
          catch (Exception ex)
          {
            AddTagEvent($"[{clientEndpoint}] Error sending delayed GT command: {ex.Message}");
          }
        });
      }
      else
      {
        // Unknown message type
        AddDiagnostic($"[{clientEndpoint}] Unknown message: {message}");
      }
    }
    catch (Exception ex)
    {
      AddMessage($"Error processing message: {ex.Message}");
    }
  }

  private void ParseAndDisplayTagRead(string message, string clientEndpoint)
  {
    try
    {
      // DA format: DA{tagID} {time} 10 {count} C7 date={date}
      // Example: DA10000001 17:50:37.786398 10  00006      C7 date=20250709

      if (message.Length < 10)
      {
        AddDiagnostic($"[{clientEndpoint}] Invalid DA message (too short): {message}");
        return;
      }

      // Skip "DA" prefix
      string content = message.Substring(2);

      // Find the space that separates tagID from time
      int firstSpace = content.IndexOf(' ');
      if (firstSpace == -1)
      {
        AddDiagnostic($"[{clientEndpoint}] Invalid DA message format: {message}");
        return;
      }

      string tagID = content.Substring(0, firstSpace);
      string remainder = content.Substring(firstSpace + 1);

      // Parse time (should be next)
      int nextSpace = remainder.IndexOf(' ');
      if (nextSpace == -1)
      {
        AddDiagnostic($"[{clientEndpoint}] Invalid DA message format: {message}");
        return;
      }

      string timeStr = remainder.Substring(0, nextSpace);
      remainder = remainder.Substring(nextSpace + 1);

      // Extract count (hex number after "10")
      var parts = remainder.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
      string count = parts.Length > 1 ? parts[1].Trim() : "?";

      // Extract date if present
      string date = "";
      int dateIndex = remainder.IndexOf("date=");
      if (dateIndex >= 0)
      {
        date = remainder.Substring(dateIndex + 5, 8);
      }

      // Parse the actual crossing time from the message
      DateTime crossingTime;
      if (!string.IsNullOrEmpty(date) && date.Length == 8)
      {
        // Try to parse the date and time from the message
        try
        {
          // Parse date: YYYYMMDD
          string year = date.Substring(0, 4);
          string month = date.Substring(4, 2);
          string day = date.Substring(6, 2);

          // Parse time: HH:mm:ss.ffffff (or similar format)
          var timeParts = timeStr.Split(':');
          if (timeParts.Length >= 3)
          {
            var seconds = timeParts[2].Split('.');
            int hour = int.Parse(timeParts[0]);
            int minute = int.Parse(timeParts[1]);
            int second = int.Parse(seconds[0]);
            int microsecond = seconds.Length > 1 ? int.Parse(seconds[1].PadRight(6, '0').Substring(0, 6)) : 0;
            int millisecond = microsecond / 1000;

            crossingTime = new DateTime(int.Parse(year), int.Parse(month), int.Parse(day),
                                      hour, minute, second, millisecond);
          }
          else
          {
            // Fallback to current time if parsing fails
            crossingTime = DateTime.Now;
          }
        }
        catch
        {
          // Fallback to current time if parsing fails
          crossingTime = DateTime.Now;
        }
      }
      else
      {
        // No date provided, use current time
        crossingTime = DateTime.Now;
      }

      // Format for display
      string displayTime = DateTime.Now.ToString("HH:mm:ss.fff");
      string formattedTagID = FormatTagID(tagID);

      // Check tag filter
      if (!ShouldProcessTag(tagID))
      {
        // Check if it's ignored vs filtered
        if (ignoredTags.Contains(tagID))
        {
          // Completely ignore - don't log or process anything for ignored tags
          ignoredTagCount++;
          return; // Exit early, don't log ignored tags anywhere
        }
        else
        {
          // Log filtered tag but don't process lap tracking
          filteredTagCount++;
          string filteredMessage = $"🚫 Tag: {formattedTagID,-32} Time: {timeStr,-15} Count: {count,-8} Date: {date} [FILTERED #{filteredTagCount} - doesn't match prefix '{tagFilterPrefix}'] [{displayTime}]";
          AddTagEvent($"[{clientEndpoint}] {filteredMessage}", tagID);
        }
        return; // Skip lap processing for filtered/ignored tags
      }

      // Process rider lap tracking using the parsed crossing time
      var lapInfo = ProcessRiderCrossing(tagID, crossingTime);

      string lapInfoStr = $"Lap {lapInfo.LapNumber}";
      if (lapInfo.LapTime.HasValue)
      {
        lapInfoStr += $" ({lapInfo.LapTime.Value:mm\\:ss\\.fff})";
      }

      string formattedMessage = $"🏷️  Tag: {formattedTagID,-32} Time: {timeStr,-15} Count: {count,-8} Date: {date} {lapInfoStr} [Parsed: {crossingTime:HH:mm:ss.fff}]";

      AddTagEvent($"[{clientEndpoint}] {formattedMessage}", tagID);

      // Display rider summary after each crossing - simplified since we have the GUI
      // DisplayRiderSummary(tagID); // Commented out to reduce log noise
    }
    catch (Exception ex)
    {
      AddDiagnostic($"[{clientEndpoint}] Error parsing DA message '{message}': {ex.Message}");
    }
  }

  private string FormatTagID(string tagID)
  {
    // Return tag ID as-is without formatting
    return tagID;
  }

  /// <summary>
  /// Formats rider display text showing number and name if available, otherwise tag ID.
  /// </summary>
  private string GetRiderDisplayText(RiderInfo rider) => rider.Label;

  /// <summary>
  /// Formats rider display text by looking up rider info from tagID
  /// </summary>
  private string GetRiderDisplayText(string tagID)
  {
    // Called from both the network and UI threads, and sometimes from inside
    // ridersLock already - Monitor is reentrant, so taking it is safe either way.
    lock (ridersLock)
    {
      return riders.TryGetValue(tagID, out var rider) ? rider.Label : tagID;
    }
  }

  private RiderLap ProcessRiderCrossing(string tagID, DateTime crossingTime)
  {
    // Collect messages to send after lock is released
    var messagesToAdd = new List<(string message, bool isRaceEvent)>();
    RiderLap resultLap;

    lock (ridersLock)
    {
      // A transponder the operator has merged onto a rider counts as that rider
      // from here on, rather than spawning a fresh unknown entry every lap.
      if (tagAliases.TryGetValue(tagID, out var canonicalTag))
        tagID = canonicalTag;

      // If race is finished, still record crossings but note they are post-race
      if (raceFinished)
      {
        messagesToAdd.Add(($"🏁 Post-race crossing: {GetRiderDisplayText(tagID)} at {crossingTime:HH:mm:ss.fff} (recorded but not counted in final results)", true));
        messagesToAdd.Add(($"Post-race crossing: {GetRiderDisplayText(tagID)}", false));
        resultLap = new RiderLap { TagID = tagID, CrossingTime = crossingTime, LapNumber = 0 };
      }
      // Check if this rider is already marked as DNF
      else if (riders.ContainsKey(tagID) && riders[tagID].IsDNF)
      {
        messagesToAdd.Add(($"🚫 Tag read ignored: {GetRiderDisplayText(tagID)} is marked as DNF (Did Not Finish) - crossing at {crossingTime:HH:mm:ss.fff}", true));
        messagesToAdd.Add(($"DNF rider crossing ignored: {GetRiderDisplayText(tagID)}", false));
        resultLap = new RiderLap { TagID = tagID, CrossingTime = crossingTime, LapNumber = 0 };
      }
      // Check if we're in final laps phase and this rider has exceeded their allowed laps
      else if (waitingForFinalLaps && riders.ContainsKey(tagID))
      {
        var existingRider = riders[tagID];
        var nextLapNumber = existingRider.TotalLaps + 1;

        if (nextLapNumber > existingRider.FinalAllowedLap)
        {
          messagesToAdd.Add(($"🚫 Tag read ignored: {GetRiderDisplayText(tagID)} has already completed their final allowed lap (lap {existingRider.FinalAllowedLap})", true));
          messagesToAdd.Add(($"Final lap exceeded: {GetRiderDisplayText(tagID)}", false));
          resultLap = new RiderLap { TagID = tagID, CrossingTime = crossingTime, LapNumber = 0 };
        }
        else
        {
          var processedLap = ProcessNormalCrossingInternal(tagID, crossingTime, messagesToAdd);
          resultLap = processedLap ?? new RiderLap { TagID = tagID, CrossingTime = crossingTime, LapNumber = 0 };
        }
      }
      // If in manual start mode and race hasn't started yet, ignore tags
      else if (manualStartMode && !raceStarted)
      {
        resultLap = new RiderLap { TagID = tagID, CrossingTime = crossingTime, LapNumber = 0 };
      }
      else
      {
        var processedLap = ProcessNormalCrossingInternal(tagID, crossingTime, messagesToAdd);
        resultLap = processedLap ?? new RiderLap { TagID = tagID, CrossingTime = crossingTime, LapNumber = 0 };
      }
    }

    // Process all messages outside the lock
    foreach (var (message, isRaceEvent) in messagesToAdd)
    {
      if (isRaceEvent)
        AddRaceEvent(message);
      else
        AddTagEvent(message);
    }

    return resultLap;
  }

  private RiderLap? ProcessNormalCrossingInternal(string tagID, DateTime crossingTime, List<(string, bool)> messagesToAdd)
  {
    // Track race start time on first crossing (only if not manual start mode)
    if (raceStartTime == null && !manualStartMode)
    {
      raceStartTime = crossingTime;
      raceEndTime = raceStartTime.Value + raceDuration;
      raceStarted = true;

      // Create new race in database
      currentRaceId = _raceDb.StartNewRace(raceStartTime.Value, raceDuration, raceName, sessionType);

      // These operations will be called later after the lock is released
      Task.Run(() => UpdateRaceStartControls());

      messagesToAdd.Add(($"🏁 Race started! Duration: {raceDuration.TotalMinutes} minutes, End time: {raceEndTime:HH:mm:ss}", true));
      messagesToAdd.Add(($"🎯 Predicted total laps will be calculated based on leader performance.", true));
    }

    // Check if race time has expired and we need to wait for leader
    if (raceStartTime.HasValue && raceEndTime.HasValue && DateTime.Now > raceEndTime.Value && !raceTimeExpired && !waitingForLeaderFinish && !raceFinished && !waitingForFinalLaps)
    {
      // Find current leader (exclude DNF riders and ignored riders)
      var currentLeader = riders.Values
        .Where(r => !r.IsDNF && !ignoredTags.Contains(r.TagID))
        .OrderByDescending(r => r.TotalLaps)
        .ThenBy(r => r.TotalTime)
        .FirstOrDefault();

      if (currentLeader != null)
      {
        leaderAtTimeExpiry = currentLeader.TagID;
        leaderLapsAtTimeExpiry = currentLeader.TotalLaps;
        raceTimeExpired = true;

        if (IsTimedSession)
        {
          messagesToAdd.Add(("⏰ Session time expired.", true));
        }
        else
        {
          var leaderDisplay = GetRiderDisplayText(currentLeader);
          messagesToAdd.Add(($"⏰ Race time expired! Leader {leaderDisplay} currently has {leaderLapsAtTimeExpiry} laps completed.", true));
        }

        // A timed session ends on the flag, not on a laps target, so it always
        // takes this branch: no extra laps, no waiting for the leader. Setting
        // waitingForFinalLaps here also clears raceTimeExpired again, which is
        // what makes the extra-laps transition further down unreachable.
        if (IsTimedSession || additionalLapsAfterTimeExpiry == 0)
        {
          BeginFinalLapPhase(messagesToAdd);
        }
        else
        {
          var lapsText = additionalLapsAfterTimeExpiry == 1 ? "lap" : "laps";
          messagesToAdd.Add(($"🏁 Race will finish after leader completes any ongoing lap plus {additionalLapsAfterTimeExpiry} additional {lapsText}.", true));
        }
      }
    }

    // Update last tag info
    lastTagID = tagID;
    lastTagTime = crossingTime;

    if (!riders.ContainsKey(tagID))
    {
      // Get imported rider data if available
      var importedData = _riderDataImporter.GetRiderData(tagID);

      // First time seeing this rider
      riders[tagID] = new RiderInfo
      {
        TagID = tagID,
        RiderNumber = importedData?.RiderNumber ?? "",
        FirstName = importedData?.FirstName ?? "",
        LastName = importedData?.LastName ?? "",
        Team = importedData?.Team ?? "",
        Category = importedData?.Category ?? "",
        Machine = importedData?.Machine ?? "",
        LastCrossingTime = crossingTime,
        FirstCrossing = crossingTime,
        LastCrossing = crossingTime,
        RaceStartTime = raceStartTime
      };

      var firstLap = new RiderLap
      {
        TagID = tagID,
        CrossingTime = crossingTime,
        LapNumber = 1,
        LapTime = raceStartTime.HasValue ? crossingTime - raceStartTime.Value : (TimeSpan?)null
      };

      riders[tagID].Laps.Add(firstLap);

      // Save to database for crash recovery
      if (currentRaceId.HasValue)
      {
        Task.Run(() =>
        {
          _raceDb.UpsertRider(riders[tagID]);
          _raceDb.AddLap(tagID, firstLap, 1); // Position 1 for first rider
        });
      }

      _refresh.Invalidate(RaceViewKind.Standings | RaceViewKind.LapProgression);

      return firstLap;
    }
    else
    {
      // Subsequent crossing
      var rider = riders[tagID];
      var previousCrossing = rider.LastCrossing;
      var lapTime = crossingTime - previousCrossing;

      // Check for minimum lap time - ignore unrealistically short laps (likely RFID errors)
      if (shortLapDetectionEnabled && lapTime < minimumLapTime)
      {
        // Keep the read so the operator can review it and put it back - on a
        // short course a "too soon" read is sometimes a real lap.
        rejectedReads.Add(new RejectedRead
        {
          TagID = tagID,
          CrossingTime = crossingTime,
          GapToPrevious = lapTime,
          Reason = $"Only {lapTime.TotalSeconds:F1}s after the previous read"
        });
        if (rejectedReads.Count > MaxRejectedReads)
          rejectedReads.RemoveAt(0);

        var logMessage = $"IGNORED SHORT LAP: {GetRiderDisplayText(tagID)} - {lapTime.TotalSeconds:F3}s " +
          $"(minimum {minimumLapTime.TotalSeconds:F0}s) - review it under \"Fix laps\"";
        messagesToAdd.Add((logMessage, false));

        // Return null to indicate no lap was processed
        return null;
      }

      var newLap = new RiderLap
      {
        TagID = tagID,
        CrossingTime = crossingTime,
        LapNumber = rider.TotalLaps + 1,
        LapTime = lapTime
      };

      rider.Laps.Add(newLap);
      rider.LastCrossing = crossingTime;

      // Check for missed reads and mark for potential splitting BEFORE saving to database
      DetectAndMarkPotentialSplits(tagID, messagesToAdd);

      // Save to database for crash recovery - but only if the lap wasn't split
      // If it was split, the split detection would have already saved the split laps,
      // but now we only mark potential splits, so we always save the original lap
      if (currentRaceId.HasValue)
      {
        Task.Run(() =>
        {
          _raceDb.UpsertRider(rider);

          // Since we only mark potential splits now, always save the original lap
          var position = CalculateCurrentPosition(tagID);
          _raceDb.AddLap(tagID, newLap, position);
        });
      }

      _refresh.Invalidate(RaceViewKind.LapProgression);

      // Handle transition from time expired to additional laps phase
      if (raceTimeExpired && !waitingForLeaderFinish && !waitingForFinalLaps && !raceFinished)
      {
        var currentLeader = riders.Values
          .Where(r => !r.IsDNF && !ignoredTags.Contains(r.TagID))
          .OrderByDescending(r => r.TotalLaps)
          .ThenBy(r => r.TotalTime)
          .FirstOrDefault();

        if (currentLeader != null && tagID == currentLeader.TagID)
        {
          var leaderCurrentLapWhenTimeExpired = leaderLapsAtTimeExpiry + 1;
          targetLapsToFinishRace = leaderCurrentLapWhenTimeExpired + additionalLapsAfterTimeExpiry;
          waitingForLeaderFinish = true;
          raceTimeExpired = false;

          var originalLeader = leaderAtTimeExpiry;
          leaderAtTimeExpiry = tagID;

          if (additionalLapsAfterTimeExpiry == 0)
          {
            if (tagID == originalLeader)
            {
              var currentRiderDisplay = GetRiderDisplayText(tagID);
              messagesToAdd.Add(($"🏁 LEADER {currentRiderDisplay} crossed after time expiry! Race will finish when leader completes {targetLapsToFinishRace} total laps (no additional laps).", true));
            }
            else
            {
              var currentRiderDisplay = GetRiderDisplayText(tagID);
              var originalLeaderDisplay = string.IsNullOrEmpty(originalLeader) ? "Unknown" : GetRiderDisplayText(originalLeader);
              messagesToAdd.Add(($"🏁 NEW LEADER {currentRiderDisplay} crossed after time expiry (was {originalLeaderDisplay})! Race will finish when new leader completes {targetLapsToFinishRace} total laps (no additional laps).", true));
            }
          }
          else
          {
            var lapsText = additionalLapsAfterTimeExpiry == 1 ? "lap" : "laps";
            if (tagID == originalLeader)
            {
              var currentRiderDisplay = GetRiderDisplayText(tagID);
              messagesToAdd.Add(($"🏁 LEADER {currentRiderDisplay} crossed after time expiry! Shown {additionalLapsAfterTimeExpiry} additional {lapsText} sign. Race will finish when leader completes {targetLapsToFinishRace} total laps.", true));
            }
            else
            {
              var currentRiderDisplay = GetRiderDisplayText(tagID);
              var originalLeaderDisplay = string.IsNullOrEmpty(originalLeader) ? "Unknown" : GetRiderDisplayText(originalLeader);
              messagesToAdd.Add(($"🏁 NEW LEADER {currentRiderDisplay} crossed after time expiry (was {originalLeaderDisplay})! Shown {additionalLapsAfterTimeExpiry} additional {lapsText} sign. Race will finish when leader completes {targetLapsToFinishRace} total laps.", true));
            }
          }
        }
        else
        {
          var currentLeaderTag = currentLeader?.TagID ?? "Unknown";
          var currentRiderDisplay = GetRiderDisplayText(tagID);
          var currentLeaderDisplay = GetRiderDisplayText(currentLeaderTag);
          messagesToAdd.Add(($"⏰ {currentRiderDisplay} crossed after time expiry, but waiting for current LEADER {currentLeaderDisplay} to cross and receive additional laps sign...", true));
        }
      }

      // Check if race should finish during additional laps phase
      if (waitingForLeaderFinish)
      {
        if (tagID == leaderAtTimeExpiry && rider.TotalLaps >= targetLapsToFinishRace)
        {
          // The leader has completed their additional laps - race is finished
          Task.Run(() => FinishRace());
        }
        else if (rider.TotalLaps >= targetLapsToFinishRace)
        {
          messagesToAdd.Add(($"🏁 {GetRiderDisplayText(tagID)} completed {targetLapsToFinishRace} laps, but race will finish when LEADER {GetRiderDisplayText(leaderAtTimeExpiry ?? "")} reaches this target.", true));
        }
      }

      // Check if we're in final laps phase and all riders have completed their final laps
      if (waitingForFinalLaps)
      {
        Task.Run(() => CheckIfAllFinalLapsCompleted());
      }

      // Check for position changes and lapping events
      Task.Run(() => CheckForPositionChangesAndLapping(tagID));

      _refresh.Invalidate(RaceViewKind.Standings);


      return newLap;
    }
  }

  /// <summary>
  /// Re-derives the missed-read warnings for a rider after they complete a lap.
  /// Delegates to the shared detector so the live path and the re-scan that runs
  /// after a correction can never disagree.
  /// </summary>
  private void DetectAndMarkPotentialSplits(string tagID, List<(string, bool)> messagesToAdd)
  {
    if (!riders.TryGetValue(tagID, out var rider)) return;

    var before = rider.Laps
      .Where(l => l.IsSuggestedForSplit)
      .Select(l => l.LapNumber)
      .ToHashSet();

    LapAnomalyDetector.Analyze(rider, CalculateGlobalAverageLapTime(), missedReadSettings);

    var newlyFlagged = rider.Laps
      .Where(l => l.IsSuggestedForSplit && !before.Contains(l.LapNumber))
      .ToList();

    foreach (var lap in newlyFlagged)
    {
      messagesToAdd.Add((
        $"🔄 POSSIBLE MISSED READ: {GetRiderDisplayText(tagID)} - lap {lap.LapNumber} took " +
        $"{lap.LapTime?.TotalSeconds:F1}s, which looks like {lap.SuggestedSplitCount} laps of " +
        $"about {lap.SuggestedSplitLapTime?.TotalSeconds:F1}s. Right-click the rider to fix it.",
        true));
    }

    if (newlyFlagged.Count > 0)
      RaiseNotice(NoticeLevel.Warning, $"Possible missed read - {GetRiderDisplayText(tagID)}");
  }

  /// <summary>
  /// Calculate the global average lap time from all riders (excluding first laps)
  /// </summary>
  private TimeSpan? CalculateGlobalAverageLapTime()
  {
    var allLapTimes = new List<TimeSpan>();

    foreach (var rider in riders.Values)
    {
      // Skip first lap for each rider and collect lap times
      var lapTimes = rider.Laps.Skip(1) // Skip first lap
          .Where(l => l.LapTime.HasValue)
          .Select(l => l.LapTime!.Value)
          .ToList();

      allLapTimes.AddRange(lapTimes);
    }

    if (allLapTimes.Count == 0) return null;

    var avgMilliseconds = allLapTimes.Average(t => t.TotalMilliseconds);
    return TimeSpan.FromMilliseconds(avgMilliseconds);
  }

  private void DisplayAllRidersSummary()
  {
    var messages = new List<string>();

    lock (ridersLock)
    {
      if (riders.Count == 0)
      {
        messages.Add("📊 No riders tracked yet.");
      }
      else
      {
        messages.Add("📊 === RIDERS SUMMARY ===");

        var sortedRiders = riders.Values
          .Where(r => !ignoredTags.Contains(r.TagID)) // Exclude ignored riders
          .OrderBy(r => r.IsDNF ? 1 : 0) // Non-DNF riders first (0), DNF riders last (1)
          .ThenByDescending(r => r.TotalLaps)
          .ThenBy(r => r.TotalTime)
          .ToList();

        int position = 1;
        foreach (var rider in sortedRiders)
        {
          var bestLap = rider.BestLapTime?.ToString(@"mm\:ss\.fff") ?? "N/A";
          var totalTime = rider.TotalTime.ToString(@"mm\:ss\.fff");

          var avgLapStr = rider.AverageLapTime?.ToString(@"mm\:ss\.fff") ?? "N/A";

          var statusStr = rider.IsDNF ? " (DNF)" : "";
          messages.Add($"📊 P{position}: {rider.Label} | {rider.TotalLaps} laps | Best: {bestLap} | Avg: {avgLapStr} | Total: {totalTime}{statusStr}");
          position++;
        }

        messages.Add("📊 ==================");
      }
    }

    // Send all messages outside the lock
    foreach (var message in messages)
    {
      AddMessage(message);
    }
  }

  private void ClearRiderData()
  {
    lock (ridersLock)
    {
      // sessionType is deliberately not reset. It is setup, not race state:
      // an operator clearing data to re-run qualifying is still in qualifying.
      riders.Clear();
      raceStartTime = null;
      raceEndTime = null;
      raceStarted = false;
      raceFinished = false;
      raceTimeExpired = false;
      waitingForLeaderFinish = false;
      waitingForFinalLaps = false;
      finalLapsStartTime = null;
      leaderAtTimeExpiry = null;
      leaderLapsAtTimeExpiry = 0;
      targetLapsToFinishRace = 0;
      lastTagID = "None";
      lastTagTime = DateTime.MinValue;
      _refresh.Invalidate(RaceViewKind.Standings);
      currentRaceId = null;

      // Reset position tracking
      lastKnownPositions.Clear();
      lastKnownLapCounts.Clear();
      lock (lapDifferencesLock) lapDifferences.Clear();
      lastBattleAnnounced.Clear();
      lastPositionCheck = DateTime.MinValue;

      // Clear race data from database if we have a current race
      if (_raceDb.CurrentRaceId > 0)
      {
        _raceDb.ClearCurrentRaceData();
      }

      // Reset warning flags
      fiveMinuteWarningShown = false;
      oneMinuteWarningShown = false;

      // Reset race start controls
      UpdateRaceStartControls();

      // Reset filter counter
      filteredTagCount = 0;

      AddMessage("🗑️ All rider data cleared. Race reset.");
      AddMessage($"⚙️ DNF timeout set to {dnfTimeoutMinutes} minutes after leader finishes.");
    }
  }

  private async void ParseGTResponse(string message, string clientEndpoint)
  {
    try
    {
      // GT response format: GT{HHmmssfff} date={YYYYMMDD}
      // Example: GT0175013116038 date=20250709

      if (message.Length < 10)
      {
        AddDiagnostic($"[{clientEndpoint}] Invalid GT response (too short): {message}");
        return;
      }

      // Extract time part (after "GT")
      int dateIndex = message.IndexOf(" date=");
      if (dateIndex == -1)
      {
        AddDiagnostic($"[{clientEndpoint}] Invalid GT response format (no date): {message}");
        return;
      }

      string timeStr = message.Substring(2, dateIndex - 2); // Skip "GT" prefix
      string dateStr = message.Substring(dateIndex + 6); // Skip " date="

      // Parse time: HHmmssfff
      if (timeStr.Length >= 9)
      {
        string hours = timeStr.Substring(0, 2);
        string minutes = timeStr.Substring(2, 2);
        string seconds = timeStr.Substring(4, 2);
        string milliseconds = timeStr.Substring(6);

        string formattedTime = $"{hours}:{minutes}:{seconds}.{milliseconds}";

        // Parse date: YYYYMMDD
        if (dateStr.Length >= 8)
        {
          string year = dateStr.Substring(0, 4);
          string month = dateStr.Substring(4, 2);
          string day = dateStr.Substring(6, 2);

          string formattedDate = $"{year}-{month}-{day}";

          AddDiagnostic($"[{clientEndpoint}] ⏰ Reader Time Sync: {formattedTime} on {formattedDate}");

          // Show time difference if significant
          try
          {
            var readerDateTime = DateTime.ParseExact($"{formattedDate} {formattedTime}",
                                                   "yyyy-MM-dd HH:mm:ss.fff", null);
            var timeDiff = DateTime.Now - readerDateTime;

            if (Math.Abs(timeDiff.TotalSeconds) > 1)
            {
              AddDiagnostic($"[{clientEndpoint}] ⚠️  Time difference: {timeDiff.TotalSeconds:F2} seconds");
            }
          }
          catch
          {
            // Ignore parsing errors for time comparison
          }
        }
        else
        {
          AddDiagnostic($"[{clientEndpoint}] ⏰ Reader Time Sync: {formattedTime} (invalid date format)");
        }
      }
      else
      {
        AddDiagnostic($"[{clientEndpoint}] ⏰ Reader Time Sync: {timeStr} date={dateStr} (raw format)");
      }

      // After successful time sync, send S0000 to start tag reading
      await SendS0000Command(clientEndpoint);
    }
    catch (Exception ex)
    {
      AddDiagnostic($"[{clientEndpoint}] Error parsing GT response '{message}': {ex.Message}");
    }
  }

  private async Task SendS0000Command(string clientEndpoint)
  {
    try
    {
      // Find the client connection for this endpoint
      TcpClient? targetClient = null;
      lock (clientsLock)
      {
        targetClient = connectedClients.FirstOrDefault(client =>
          client.Client.RemoteEndPoint?.ToString() == clientEndpoint);
      }

      if (targetClient?.Connected == true)
      {
        var stream = targetClient.GetStream();

        // Send S0000 command to start tag reading
        var s0000Command = "S0000\r"; // CrossMgr uses CR only, not CRLF
        byte[] s0000Bytes = Encoding.ASCII.GetBytes(s0000Command);
        await stream.WriteAsync(s0000Bytes, 0, s0000Bytes.Length);

        AddDiagnostic($"[{clientEndpoint}] 📡 Sent S0000 command to start tag reading");
      }
      else
      {
        AddDiagnostic($"[{clientEndpoint}] ❌ Cannot send S0000 - client not found or disconnected");
      }
    }
    catch (Exception ex)
    {
      AddDiagnostic($"[{clientEndpoint}] Error sending S0000 command: {ex.Message}");
    }
  }

  private void AddMessage(string message)
  {
    // Redirect to race events by default
    AddRaceEvent(message);
  }

  /// <summary>
  /// Internal/plumbing messages. These go to the Tag Events feed so the Race
  /// Events feed stays readable as race commentary.
  /// </summary>
  private void AddDiagnostic(string message) => AddTagEvent(message);

  private void AddTagEvent(string message) => AddTagEvent(message, null);

  /// <summary>
  /// Appends to the Tag Events feed. Pass <paramref name="tagId"/> when the line
  /// refers to a specific transponder, so the context menu can act on it without
  /// parsing the rendered text back apart.
  /// </summary>
  private void AddTagEvent(string message, string? tagId)
  {
    if (InvokeRequired)
    {
      BeginInvoke(new Action<string, string?>(AddTagEvent), message, tagId);
      return;
    }

    var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
    var formattedMessage = $"[{timestamp}] {message}";

    // Add to UI
    listBoxTagEvents.Items.Add(new TagEventItem(formattedMessage, tagId));

    // Write to log file
    WriteToLogFile("TAG", message);

    TrimEventList(listBoxTagEvents);

    // Auto-scroll to bottom
    listBoxTagEvents.TopIndex = listBoxTagEvents.Items.Count - 1;
  }

  private const int MaxEventListItems = 5000;
  private const int EventListTrimBlock = 1000;

  /// <summary>
  /// Drops the oldest block of messages once the cap is reached. Removing one
  /// item at a time shifted the entire backing array and forced a repaint for
  /// every subsequent message.
  /// </summary>
  private static void TrimEventList(ListBox list)
  {
    if (list.Items.Count <= MaxEventListItems) return;

    list.BeginUpdate();
    try
    {
      for (var i = 0; i < EventListTrimBlock; i++)
        list.Items.RemoveAt(0);
    }
    finally
    {
      list.EndUpdate();
    }
  }

  private void AddRaceEvent(string message)
  {
    if (InvokeRequired)
    {
      BeginInvoke(new Action<string>(AddRaceEvent), message);
      return;
    }

    var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
    var formattedMessage = $"[{timestamp}] {message}";

    // Add to UI
    listBoxMessages.Items.Add(formattedMessage);

    // Write to log file
    WriteToLogFile("RACE", message);

    // Persist off the UI thread; a 45 MB LiteDB file plus engine-lock contention
    // made this a visible stall at tag-read rate.
    if (currentRaceId != null)
    {
      var toPersist = message;
      Task.Run(() =>
      {
        try { _raceDb.AddRaceEvent("SYSTEM", "", toPersist); }
        catch (Exception) { /* logging must never take the race down */ }
      });
    }

    TrimEventList(listBoxMessages);

    // Auto-scroll to bottom
    listBoxMessages.TopIndex = listBoxMessages.Items.Count - 1;
  }

  private void UpdateConnectionCount()
  {
    if (InvokeRequired)
    {
      BeginInvoke(new Action(UpdateConnectionCount));
      return;
    }

    lock (clientsLock)
    {
      UpdateStatusBar();
    }
  }

  private void UpdateUI()
  {
    if (InvokeRequired)
    {
      Invoke(new Action(UpdateUI));
      return;
    }

    UpdateStatusBar();
    UpdateCommandStates();
  }

  private void buttonShowSummary_Click(object? sender, EventArgs e)
  {
    DisplayAllRidersSummary();
  }

  private void buttonClearRiders_Click(object? sender, EventArgs e)
  {
    int riderCount;
    int lapCount;
    lock (ridersLock)
    {
      riderCount = riders.Count;
      lapCount = riders.Values.Sum(r => r.TotalLaps);
    }

    if (riderCount > 0)
    {
      var answer = MessageBox.Show(
        $"Delete this race?\n\n{riderCount} rider(s) and {lapCount} recorded lap(s) " +
        "will be permanently deleted. This cannot be undone.",
        "Delete race",
        MessageBoxButtons.YesNo,
        MessageBoxIcon.Warning,
        MessageBoxDefaultButton.Button2);

      if (answer != DialogResult.Yes) return;
    }

    ClearRiderData();
  }

  private void buttonGenerateReport_Click(object? sender, EventArgs e)
  {
    try
    {
      // Create a snapshot of rider data to avoid locking issues
      Dictionary<string, RiderInfo> riderSnapshot;
      DateTime? raceStartSnapshot;
      DateTime? raceEndSnapshot;
      TimeSpan raceDurationSnapshot;
      bool raceFinishedSnapshot;
      DateTime? additionalLapsSignShown;
      DateTime? raceActuallyEnded;
      int additionalLapsCount;

      lock (ridersLock)
      {
        riderSnapshot = riders
          .Where(kvp => !ignoredTags.Contains(kvp.Key))
          .ToDictionary(kvp => kvp.Key, kvp => CloneRiderForDisplay(kvp.Value));
        raceStartSnapshot = raceStartTime;
        raceDurationSnapshot = raceDuration;
        raceFinishedSnapshot = raceFinished;

        // Additional timing information. These come straight from Form1's own
        // fields: raceEndTime is overwritten with the true finish time in
        // CompletelyFinishRace, and finalLapsStartTime is set in FinishRace.
        additionalLapsSignShown = finalLapsStartTime;
        raceActuallyEnded = raceFinished ? raceEndTime : null;
        additionalLapsCount = additionalLapsAfterTimeExpiry;

        // Use actual race end time if available, otherwise use calculated end time
        if (raceActuallyEnded.HasValue)
        {
          raceEndSnapshot = raceActuallyEnded.Value;
        }
        else
        {
          raceEndSnapshot = raceEndTime; // Fallback to calculated end time
        }
      }

      if (riderSnapshot.Count == 0)
      {
        MessageBox.Show(this,
          "There are no laps recorded yet, so there is nothing to report.",
          "No results yet", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
      }

      // Show report options dialog
      using var reportDialog = new ReportOptionsDialog(raceName);
      if (reportDialog.ShowDialog() == DialogResult.OK)
      {
        var raceTitle = reportDialog.RaceTitle;

        switch (reportDialog.SelectedAction)
        {
          case ReportAction.Preview:
            _raceReportGenerator.ShowClassBasedPrintPreview(riderSnapshot, raceStartSnapshot,
              raceEndSnapshot, raceDurationSnapshot, raceFinishedSnapshot, raceTitle,
              additionalLapsSignShown, raceActuallyEnded, additionalLapsCount);
            break;

          case ReportAction.Print:
            _raceReportGenerator.PrintReport(riderSnapshot, raceStartSnapshot,
              raceEndSnapshot, raceDurationSnapshot, raceFinishedSnapshot, raceTitle,
              additionalLapsSignShown, raceActuallyEnded, additionalLapsCount);
            break;

          case ReportAction.Export:
            _raceReportGenerator.ExportToFile(riderSnapshot, raceStartSnapshot,
              raceEndSnapshot, raceDurationSnapshot, raceFinishedSnapshot, raceTitle,
              additionalLapsSignShown, raceActuallyEnded, additionalLapsCount);
            break;
        }
      }
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(this,
        "The results could not be produced.",
        "Nothing has been lost - the race data is still recorded. Try again, or " +
        "export to a file instead of printing.", ex);
    }
  }

  private void buttonImportRiders_Click(object? sender, EventArgs e)
  {
    try
    {
      using (var openFileDialog = new OpenFileDialog())
      {
        openFileDialog.Title = "Import Rider Data";
        openFileDialog.Filter = "Excel files (*.xlsx)|*.xlsx|CSV files (*.csv)|*.csv|All files (*.*)|*.*";
        openFileDialog.FilterIndex = 1;

        if (openFileDialog.ShowDialog() == DialogResult.OK)
        {
          ImportResult importResult;
          string fileName = openFileDialog.FileName;
          string extension = Path.GetExtension(fileName).ToLower();

          // Import based on file type
          if (extension == ".xlsx" || extension == ".xls")
          {
            importResult = _riderDataImporter.ImportFromExcelDetailed(fileName);
          }
          else if (extension == ".csv")
          {
            importResult = _riderDataImporter.ImportFromCsvDetailed(fileName);
          }
          else
          {
            ErrorDialog.Show(this,
              "That file type can't be read.",
              "Choose an Excel file (.xlsx) or a CSV file (.csv).");
            return;
          }

          var importedCount = importResult.ImportedCount;

          // Update UI to show import status
          if (importedCount > 0)
          {
            SetStatusNotice(
              importResult.Skipped.Count > 0 ? NoticeLevel.Warning : NoticeLevel.Info,
              importResult.Skipped.Count > 0
                ? $"{importedCount} riders imported, {importResult.Skipped.Count} row(s) skipped"
                : $"{importedCount} riders imported");

            AddMessage($"📋 Imported rider data for {importedCount} riders from {Path.GetFileName(fileName)}");

            // Rows that failed to parse used to disappear into Console.WriteLine,
            // so a partly-broken roster reported a clean success.
            if (importResult.Skipped.Count > 0)
            {
              foreach (var (row, reason) in importResult.Skipped.Take(20))
                AddMessage($"⚠️ Row {row} skipped: {reason}");

              ErrorDialog.Show(this,
                $"{importedCount} riders imported, {importResult.Skipped.Count} row(s) skipped.",
                "The skipped rows are listed in the Race Events tab. Riders on those " +
                "rows will show as UNKNOWN when they cross the line.",
                null);
            }

            // Apply imported data to any existing riders
            ApplyImportedDataToExistingRiders();

            // Update class filter options
            PopulateClassFilter();

            RememberRiderList(fileName);
          }
          else
          {
            SetStatusNotice(NoticeLevel.Warning, "No riders imported");

            var columns = importResult.DetectedColumns.Count > 0
              ? string.Join(", ", importResult.DetectedColumns)
              : "none";

            ErrorDialog.Show(this,
              "No riders were found in that file.",
              importResult.HasTagColumn
                ? $"The file has a transponder column but no usable rows. Columns found: {columns}."
                : $"The file needs a column called 'tagid'. Columns found: {columns}.");
          }
        }
      }
    }
    catch (Exception ex)
    {
      SetStatusNotice(NoticeLevel.Critical, "Import failed");
      ErrorDialog.Show(this,
        "The rider list could not be read.",
        "Check that the file is not open in another program, then try again.", ex);
    }
  }

  /// <summary>
  /// Apply imported rider data to existing riders that don't have names/teams
  /// </summary>
  private void ApplyImportedDataToExistingRiders()
  {
    lock (ridersLock)
    {
      int updatedCount = 0;

      foreach (var rider in riders.Values)
      {
        var importedData = _riderDataImporter.GetRiderData(rider.TagID);
        if (importedData != null)
        {
          // Update rider information if not already set
          if (string.IsNullOrEmpty(rider.FirstName) && !string.IsNullOrEmpty(importedData.FirstName))
            rider.FirstName = importedData.FirstName;

          if (string.IsNullOrEmpty(rider.LastName) && !string.IsNullOrEmpty(importedData.LastName))
            rider.LastName = importedData.LastName;

          if (string.IsNullOrEmpty(rider.Team) && !string.IsNullOrEmpty(importedData.Team))
            rider.Team = importedData.Team;

          if (string.IsNullOrEmpty(rider.RiderNumber) && !string.IsNullOrEmpty(importedData.RiderNumber))
            rider.RiderNumber = importedData.RiderNumber;

          if (string.IsNullOrEmpty(rider.Category) && !string.IsNullOrEmpty(importedData.Category))
            rider.Category = importedData.Category;

          if (string.IsNullOrEmpty(rider.Machine) && !string.IsNullOrEmpty(importedData.Machine))
            rider.Machine = importedData.Machine;

          updatedCount++;

          // Update database with new rider information
          if (currentRaceId.HasValue)
          {
            _raceDb.UpsertRider(rider);
          }
        }
      }

      if (updatedCount > 0)
      {
        AddMessage($"📋 Updated {updatedCount} existing riders with imported data");

        // Refresh displays to show updated rider information
        _refresh.Invalidate(RaceViewKind.Standings);
      }
    }
  }

  private void buttonSetDuration_Click(object? sender, EventArgs e)
  {
    var minutes = (int)numericUpDownRaceDuration.Value;
    raceDuration = TimeSpan.FromMinutes(minutes);

    // Reset warning flags when duration changes
    fiveMinuteWarningShown = false;
    oneMinuteWarningShown = false;

    // If race is already started, update the end time
    if (raceStartTime.HasValue)
    {
      raceEndTime = raceStartTime.Value + raceDuration;
      AddMessage($"⏰ Race duration updated to {minutes} minutes. New end time: {raceEndTime:HH:mm:ss}");

      // Immediately update display to show new end time and predictions
      UpdateStatisticsDisplay();
    }
    else
    {
      AddMessage($"⏰ Race duration set to {minutes} minutes. Will be applied when race starts.");
    }
  
    RememberRaceSetup();
  }

  private void InitializeRidersDataGrid()
  {
    // Same trick already used for the lap chart panel: DataGridView exposes
    // DoubleBuffered only as a protected property.
    typeof(DataGridView).InvokeMember("DoubleBuffered",
      BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
      null, dataGridViewRiders, new object[] { true });

    // Virtual mode: the control holds no row data, it asks for what it paints.
    dataGridViewRiders.VirtualMode = true;
    dataGridViewRiders.CellValueNeeded += DataGridViewRiders_CellValueNeeded;
    dataGridViewRiders.CellFormatting += DataGridViewRiders_CellFormatting;
    dataGridViewRiders.CellToolTipTextNeeded += DataGridViewRiders_CellToolTipTextNeeded;

    // Set up the DataGridView columns
    dataGridViewRiders.Columns.Clear();
    dataGridViewRiders.Columns.Add("Position", "Pos");
    dataGridViewRiders.Columns.Add("Status", "Status");
    dataGridViewRiders.Columns.Add("ProjectedPosition", "If fixed");
    dataGridViewRiders.Columns.Add("RiderNumber", "Number");
    dataGridViewRiders.Columns.Add("TagID", "Transponder");
    dataGridViewRiders.Columns.Add("RiderName", "Rider Name");
    dataGridViewRiders.Columns.Add("Team", "Team");
    dataGridViewRiders.Columns.Add("Category", "Class");
    dataGridViewRiders.Columns.Add("Laps", "Laps");
    dataGridViewRiders.Columns.Add("LastLap", "Last Lap");
    dataGridViewRiders.Columns.Add("BestLap", "Best Lap");
    dataGridViewRiders.Columns.Add("AvgLap", "Avg Lap");
    dataGridViewRiders.Columns.Add("PredictedLap", "Typical lap");
    dataGridViewRiders.Columns.Add("NextCrossing", "Next lap at");
    dataGridViewRiders.Columns.Add("TimeToNext", "Due in");
    dataGridViewRiders.Columns.Add("TotalTime", "Total Time");
    dataGridViewRiders.Columns.Add("Gap", "Gap");

    // Set column widths
    foreach (DataGridViewColumn column in dataGridViewRiders.Columns)
    {
      switch (column.Name)
      {
        case "Position": column.Width = 40; break;
        case "Status": column.Width = 90; break;
      case "RiderNumber": column.Width = 60; break;
        case "TagID": column.Width = 200; break; // Increased to accommodate up to 32-character tag IDs
        case "RiderName": column.Width = 150; break;
        case "Team": column.Width = 120; break;
        case "Category": column.Width = 100; break;
        case "Laps": column.Width = 50; break;
        case "LastLap": column.Width = 85; break;
        case "BestLap": column.Width = 85; break;
        case "AvgLap": column.Width = 85; break;
        case "PredictedLap": column.Width = 85; break;
        case "NextCrossing": column.Width = 80; break;
        case "TimeToNext": column.Width = 90; break;
        case "TotalTime": column.Width = 85; break;
        case "Gap": column.Width = 80; break;
      }
    }

    // Add context menu for tag operations
    var contextMenu = new ContextMenuStrip();

    // Deliberately no keyboard shortcut: this was bound to Delete, so resting a
    // hand on the keyboard with a row selected wiped that rider's race.
    var fixLapsItem = new ToolStripMenuItem("Fix laps...")
    {
      Font = new Font(dataGridViewRiders.Font, FontStyle.Bold)
    };
    fixLapsItem.Click += (s, e) => OpenLapCorrection(SelectedRiderTag());

    var assignTagItem = new ToolStripMenuItem("Identify this transponder...");
    assignTagItem.Click += (s, e) => OpenAssignTag(SelectedRiderTag());

    var addToIgnoreItem = new ToolStripMenuItem("Stop counting this rider...");
    addToIgnoreItem.Click += (s, e) => HandleAddTagToIgnoreList();

    var removeFromIgnoreItem = new ToolStripMenuItem("Count this rider again");
    removeFromIgnoreItem.Click += (s, e) => HandleRemoveTagFromIgnoreList();

    var showIgnoreListItem = new ToolStripMenuItem("Show ignored transponders...");
    showIgnoreListItem.Click += (s, e) => ShowIgnoreList();

    var clearIgnoreListItem = new ToolStripMenuItem("Count all ignored transponders again");
    clearIgnoreListItem.Click += (s, e) => ClearIgnoreList();

    var undoItem = new ToolStripMenuItem("Undo last change");
    undoItem.Click += (s, e) => UndoLastCorrection();

    contextMenu.Items.AddRange(new ToolStripItem[]
    {
      fixLapsItem,
      assignTagItem,
      undoItem,
      new ToolStripSeparator(),
      addToIgnoreItem,
      removeFromIgnoreItem,
      new ToolStripSeparator(),
      showIgnoreListItem,
      clearIgnoreListItem
    });

    // Label the items with the rider they will act on, and only enable what
    // actually applies to the current selection.
    contextMenu.Opening += (s, e) =>
    {
      var tagId = SelectedRiderTag();
      var hasSelection = !string.IsNullOrEmpty(tagId);
      var isIgnored = hasSelection && ignoredTags.Contains(tagId!);
      var who = hasSelection ? GetRiderDisplayText(tagId!) : null;

      fixLapsItem.Enabled = hasSelection;
      fixLapsItem.Text = who != null ? $"Fix laps for {who}..." : "Fix laps...";

      // Only meaningful while the transponder has no rider attached; when it is
      // the likely thing to do, make it the default item.
      var unidentified = hasSelection && who == tagId;
      assignTagItem.Enabled = unidentified;
      assignTagItem.Visible = unidentified;
      if (unidentified)
        assignTagItem.Font = new Font(dataGridViewRiders.Font, FontStyle.Bold);

      undoItem.Enabled = _corrections.History.CanUndo;
      undoItem.Text = _corrections.History.CanUndo
        ? $"Undo: {_corrections.History.NextUndoDescription}"
        : "Nothing to undo";

      addToIgnoreItem.Enabled = hasSelection && !isIgnored;
      addToIgnoreItem.Text = who != null ? $"Stop counting {who}..." : "Stop counting this rider...";

      removeFromIgnoreItem.Enabled = hasSelection && isIgnored;
      removeFromIgnoreItem.Text = who != null ? $"Count {who} again" : "Count this rider again";

      clearIgnoreListItem.Enabled = ignoredTags.Count > 0;
    };

    dataGridViewRiders.ContextMenuStrip = contextMenu;

    // Double-click a rider to fix their laps - the same gesture that used to
    // pop up an unreadable text dump.
    dataGridViewRiders.CellDoubleClick += (s, e) =>
    {
      if (e.RowIndex >= 0) OpenLapCorrection(SelectedRiderTag());
    };
  }

  /// <summary>Reverses the most recent correction. Also bound to Ctrl+Z.</summary>
  private void UndoLastCorrection()
  {
    var result = _corrections.Undo();
    if (!result.Ok)
    {
      MessageBox.Show(this, result.Error, "Nothing to undo",
        MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
  }

  private void InitializeTagEventsContextMenu()
  {
    // Create context menu for tag events list
    var contextMenu = new ContextMenuStrip();

    var addToIgnoreItem = new ToolStripMenuItem("Add Tag to Ignore List");
    addToIgnoreItem.Click += (s, e) => HandleTagEventsAddToIgnoreList();

    var removeFromIgnoreItem = new ToolStripMenuItem("Remove Tag from Ignore List");
    removeFromIgnoreItem.Click += (s, e) => HandleTagEventsRemoveFromIgnoreList();

    var showIgnoreListItem = new ToolStripMenuItem("Show Ignore List");
    showIgnoreListItem.Click += (s, e) => ShowIgnoreList();

    var clearIgnoreListItem = new ToolStripMenuItem("Clear Ignore List");
    clearIgnoreListItem.Click += (s, e) => ClearIgnoreList();

    contextMenu.Items.AddRange(new ToolStripItem[]
    {
      addToIgnoreItem,
      removeFromIgnoreItem,
      new ToolStripSeparator(),
      showIgnoreListItem,
      clearIgnoreListItem
    });

    // Update context menu items based on current selection
    contextMenu.Opening += (s, e) =>
    {
      string? tagId = ExtractTagFromSelectedEvent();
      bool hasSelection = !string.IsNullOrEmpty(tagId);
      bool isIgnored = hasSelection && tagId != null && ignoredTags.Contains(tagId);

      var who = hasSelection ? GetRiderDisplayText(tagId!) : null;

      addToIgnoreItem.Enabled = hasSelection && !isIgnored;
      addToIgnoreItem.Text = who != null ? $"Stop counting {who}..." : "Stop counting this rider...";

      removeFromIgnoreItem.Enabled = hasSelection && isIgnored;
      removeFromIgnoreItem.Text = who != null ? $"Count {who} again" : "Count this rider again";

      clearIgnoreListItem.Enabled = ignoredTags.Count > 0;
    };

    listBoxTagEvents.ContextMenuStrip = contextMenu;
  }

  private Font? _ridersGridBoldFont;

  /// <summary>
  /// One bold font shared by every cell that needs it. These used to be
  /// allocated per cell per refresh and never disposed, so a long race steadily
  /// consumed the process GDI handle quota.
  /// </summary>
  private Font GetRidersGridBoldFont()
  {
    var baseFont = dataGridViewRiders.Font;
    if (_ridersGridBoldFont == null || _ridersGridBoldFont.FontFamily != baseFont.FontFamily ||
        Math.Abs(_ridersGridBoldFont.Size - baseFont.Size) > 0.01f)
    {
      _ridersGridBoldFont?.Dispose();
      _ridersGridBoldFont = new Font(baseFont, FontStyle.Bold);
    }
    return _ridersGridBoldFont;
  }

  private void UpdateRidersDisplay()
  {
    if (InvokeRequired)
    {
      Invoke(new Action(UpdateRidersDisplay));
      return;
    }

    // Create snapshot of rider data to avoid holding lock during UI operations
    List<RiderInfo> riderSnapshot;
    DateTime? raceStartSnapshot;
    bool raceFinishedSnapshot;
    Dictionary<string, int>? projectedPositions;

    lock (ridersLock)
    {
      if (riders.Count == 0)
      {
        _riderRows = new List<RiderRowData>();
        dataGridViewRiders.RowCount = 0;
        return;
      }

      // Create deep copies of rider data to avoid references to locked objects
      // Filter out ignored riders and filter by selected class
      riderSnapshot = riders.Values
        .Where(r => !ignoredTags.Contains(r.TagID))
        .Where(r => selectedClassFilter == "All Classes" || r.Category == selectedClassFilter)
        .Select(r => new RiderInfo
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
          FinalAllowedLap = r.FinalAllowedLap,
          Laps = r.Laps.ToList() // Create copy of laps list
                                 // Note: EstimatedNextCrossing and PredictedLapTime are computed properties
        }).ToList();

      raceStartSnapshot = raceStartTime;
      raceFinishedSnapshot = raceFinished;

      // Projected standings are only meaningful when something is flagged for a
      // split, and building them costs a copy of the whole field - so do it once
      // here rather than once per row.
      projectedPositions = riders.Values.Any(r => r.Laps.Any(l => l.IsSuggestedForSplit))
        ? PositionCalculator.CalculateProjectedPositionsWithSplits(riders)
        : null;
    }

    // Remember the rider the operator had selected, not the row index - the
    // leaderboard reorders constantly, and following the index would make the
    // selection jump to whoever happens to be in that position next.
    var selectedTag = SelectedRiderTag();
    var firstVisibleRow = dataGridViewRiders.FirstDisplayedScrollingRowIndex;

    try
    {
      // Suspend layout to improve performance during bulk updates
      dataGridViewRiders.SuspendLayout();

      // Sort riders: Finishing riders first (by laps desc, then time asc), then DNF riders (by laps desc, then time asc)
      var sortedRiders = PositionCalculator.GetSortedRidersFromSnapshot(riderSnapshot);

      var rows = new List<RiderRowData>(sortedRiders.Count);

      // Get leader info for gap calculations
      var leader = sortedRiders.FirstOrDefault();

      for (int i = 0; i < sortedRiders.Count; i++)
      {
        var rider = sortedRiders[i];

        // Calculate gap to leader
        string gap = "";
        if (i > 0 && leader != null)
        {
          if (rider.TotalLaps < leader.TotalLaps)
          {
            gap = $"-{leader.TotalLaps - rider.TotalLaps} lap{(leader.TotalLaps - rider.TotalLaps > 1 ? "s" : "")}";
          }
          else
          {
            var timeDiff = rider.TotalTime - leader.TotalTime;
            gap = $"+{timeDiff:mm\\:ss\\.fff}";
          }
        }
        else if (i == 0)
        {
          gap = "Leader";
        }

        // Average of the completed laps. RiderInfo.AverageLapTime excludes the
        // first, which is the run from the race start rather than a lap.
        var avgLapStr = rider.AverageLapTime?.ToString(@"mm\:ss\.fff") ?? "N/A";

        // Predicted lap time
        var predictedLapStr = rider.PredictedLapTime?.ToString(@"mm\:ss\.fff") ?? "N/A";

        // Next crossing prediction
        var nextCrossingStr = "N/A";
        var timeToNextStr = "N/A";

        if (rider.IsDNF)
        {
          nextCrossingStr = "DNF";
          timeToNextStr = "DNF";
        }
        else if (raceFinishedSnapshot)
        {
          nextCrossingStr = "Race Finished";
          timeToNextStr = "Race Finished";
        }
        else if (rider.EstimatedNextCrossing.HasValue && raceStartSnapshot.HasValue)
        {
          var nextTime = rider.EstimatedNextCrossing.Value;

          // Convert to race time instead of wall clock time
          var raceTimeAtCrossing = nextTime - raceStartSnapshot.Value;
          nextCrossingStr = raceTimeAtCrossing.ToString(@"mm\:ss");

          var timeToNext = nextTime - DateTime.Now;
          if (timeToNext > TimeSpan.Zero)
          {
            if (timeToNext.TotalMinutes < 1)
              timeToNextStr = $"{timeToNext.TotalSeconds:F0}s";
            else
              timeToNextStr = $"{timeToNext:mm\\:ss}";
          }
          else
          {
            timeToNextStr = "Overdue";
          }
        }

        // Add row to grid
        var hasSplitLaps = rider.Laps.Any(l => l.IsSplitLap);
        var hasSuggestedSplits = rider.Laps.Any(l => l.IsSuggestedForSplit);

        // Status lives in its own column. It used to be appended to the
        // transponder cell as " (DNF)" / " *" / " ?" and parsed back off again.
        var statusText = "";
        var statusTooltip = "";
        if (rider.IsDNF)
        {
          statusText = "DNF";
          statusTooltip = "Did not finish - timed out after the leader finished";
        }
        else if (hasSuggestedSplits)
        {
          statusText = "CHECK";
          statusTooltip = "A lap looks long enough to be a missed read. Right-click to review it.";
        }
        else if (hasSplitLaps)
        {
          statusText = "FIXED";
          statusTooltip = "A missed read was corrected by splitting a lap";
        }
        else if (string.IsNullOrEmpty(rider.RiderNumber) && rider.DisplayName == rider.TagID)
        {
          statusText = "UNKNOWN";
          statusTooltip = "This transponder is not in the imported rider list";
        }

        var displayTagID = rider.TagID;

        var riderName = rider.DisplayName != rider.TagID ? rider.DisplayName : "";
        var teamName = rider.Team;
        var categoryName = rider.Category;

        // Calculate projected position if splits were applied
        string projectedPositionStr = "";
        if (hasSuggestedSplits && projectedPositions != null)
        {
          {
            var projectedPosition = projectedPositions.TryGetValue(rider.TagID, out var p) ? p : i + 1;
            var currentPosition = i + 1;

            if (projectedPosition != currentPosition)
            {
              var change = currentPosition - projectedPosition;
              if (change > 0)
              {
                projectedPositionStr = $"{projectedPosition} (+{change})"; // Would improve position
              }
              else
              {
                projectedPositionStr = $"{projectedPosition} ({change})"; // Would lose position
              }
            }
            else
            {
              projectedPositionStr = "Same"; // No change
            }
          }
        }
        else
        {
          projectedPositionStr = ""; // No suggestions
        }

        // Build the row rather than writing it into the grid. The control is
        // in virtual mode and will ask for whatever it needs to paint.
        var cells = new string[RiderRowData.ColumnCount];
        cells[RiderRowData.ColPosition] = (i + 1).ToString();
        cells[RiderRowData.ColStatus] = statusText;
        cells[RiderRowData.ColProjectedPosition] = projectedPositionStr;
        cells[RiderRowData.ColRiderNumber] = rider.RiderNumber;
        cells[RiderRowData.ColTagID] = displayTagID;
        cells[RiderRowData.ColRiderName] = riderName;
        cells[RiderRowData.ColTeam] = teamName;
        cells[RiderRowData.ColCategory] = categoryName;
        cells[RiderRowData.ColLaps] = rider.TotalLaps.ToString();
        cells[RiderRowData.ColLastLap] = rider.LastLapTime?.ToString(@"mm\:ss\.fff") ?? "N/A";
        cells[RiderRowData.ColBestLap] = rider.BestLapTime?.ToString(@"mm\:ss\.fff") ?? "N/A";
        cells[RiderRowData.ColAvgLap] = avgLapStr;
        cells[RiderRowData.ColPredictedLap] = predictedLapStr;
        cells[RiderRowData.ColNextCrossing] = nextCrossingStr;
        cells[RiderRowData.ColTimeToNext] = timeToNextStr;
        cells[RiderRowData.ColTotalTime] = rider.TotalTime.ToString(@"mm\:ss\.fff");
        cells[RiderRowData.ColGap] = gap;

        var rowBack = Color.Empty;
        var rowFore = Color.Empty;
        if (rider.IsDNF)
        {
          rowBack = Color.LightGray;
          rowFore = Color.DarkRed;
        }
        else if (i == 0) rowBack = Color.Gold;
        else if (i == 1) rowBack = Color.Silver;
        else if (i == 2) rowBack = Color.FromArgb(205, 127, 50);

        rows.Add(new RiderRowData
        {
          TagID = rider.TagID,
          Cells = cells,
          StatusText = statusText,
          StatusTooltip = statusTooltip,
          RowBackColor = rowBack,
          RowForeColor = rowFore,
          IsDnf = rider.IsDNF,
          IsOverdue = timeToNextStr == "Overdue" && !rider.IsDNF,
          ProjectedImproves = hasSuggestedSplits && !rider.IsDNF && projectedPositionStr.Contains("(+"),
          ProjectedDeclines = hasSuggestedSplits && !rider.IsDNF && projectedPositionStr.Contains("(-")
        });
      }
      _riderRows = rows;

      // Virtual mode: setting RowCount is all the control needs. It will ask for
      // the fifteen or so rows actually on screen, whatever the field size.
      if (dataGridViewRiders.RowCount != rows.Count)
        dataGridViewRiders.RowCount = rows.Count;

      dataGridViewRiders.Invalidate();
    }
    catch (Exception ex)
    {
      // Log any errors but don't crash the app
      AddMessage($"Error updating riders display: {ex.Message}");
    }
    finally
    {
      // Always resume layout
      dataGridViewRiders.ResumeLayout();
    }

    RestoreRidersGridView(selectedTag, firstVisibleRow);
  }

  private List<RiderRowData> _riderRows = new();

  /// <summary>
  /// Supplies cell text on demand. In virtual mode the grid only asks about the
  /// rows it is actually painting, so a 250-rider field costs the same as a
  /// 20-rider one - writing every row into the control took close to a second at
  /// that size, which made the grid feel sluggish and jump under the operator.
  /// </summary>
  private void DataGridViewRiders_CellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
  {
    if (e.RowIndex < 0 || e.RowIndex >= _riderRows.Count) return;

    var row = _riderRows[e.RowIndex];
    e.Value = e.ColumnIndex >= 0 && e.ColumnIndex < row.Cells.Length
      ? row.Cells[e.ColumnIndex] ?? ""
      : "";
  }

  /// <summary>Colours the visible cells. Same rules as before, applied at paint time.</summary>
  private void DataGridViewRiders_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
  {
    if (e.RowIndex < 0 || e.RowIndex >= _riderRows.Count) return;

    var row = _riderRows[e.RowIndex];

    if (!row.RowBackColor.IsEmpty) e.CellStyle.BackColor = row.RowBackColor;
    if (!row.RowForeColor.IsEmpty) e.CellStyle.ForeColor = row.RowForeColor;

    switch (e.ColumnIndex)
    {
      case RiderRowData.ColStatus when row.StatusText.Length > 0:
        e.CellStyle.ForeColor = row.StatusText switch
        {
          "CHECK" => Color.DarkOrange,
          "FIXED" => Color.Red,
          "UNKNOWN" => Color.DarkOrange,
          _ => Color.DarkRed
        };
        e.CellStyle.Font = GetRidersGridBoldFont();
        break;

      case RiderRowData.ColProjectedPosition when row.ProjectedImproves:
        e.CellStyle.ForeColor = Color.Green;
        e.CellStyle.Font = GetRidersGridBoldFont();
        break;

      case RiderRowData.ColProjectedPosition when row.ProjectedDeclines:
        e.CellStyle.ForeColor = Color.Red;
        e.CellStyle.Font = GetRidersGridBoldFont();
        break;

      case RiderRowData.ColTimeToNext when row.IsOverdue:
        e.CellStyle.ForeColor = Color.Red;
        e.CellStyle.Font = GetRidersGridBoldFont();
        break;

      case RiderRowData.ColNextCrossing when row.IsDnf:
      case RiderRowData.ColTimeToNext when row.IsDnf:
        e.CellStyle.Font = GetRidersGridBoldFont();
        break;
    }
  }

  /// <summary>Explains the Status column on hover, for the visible cell only.</summary>
  private void DataGridViewRiders_CellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
  {
    if (e.RowIndex < 0 || e.RowIndex >= _riderRows.Count) return;
    if (e.ColumnIndex == RiderRowData.ColStatus)
      e.ToolTipText = _riderRows[e.RowIndex].StatusTooltip;
  }

  /// <summary>
  /// Puts the operator back where they were: on the same rider, at the same
  /// scroll offset - even though that rider may have changed position.
  /// </summary>
  private void RestoreRidersGridView(string? selectedTag, int firstVisibleRow)
  {
    if (selectedTag != null)
    {
      var index = _riderRows.FindIndex(r => r.TagID == selectedTag);
      if (index >= 0 && index < dataGridViewRiders.RowCount)
        dataGridViewRiders.CurrentCell = dataGridViewRiders.Rows[index].Cells[0];
    }

    // After CurrentCell, which scrolls the selection into view on its own.
    if (firstVisibleRow >= 0 && firstVisibleRow < dataGridViewRiders.RowCount)
      dataGridViewRiders.FirstDisplayedScrollingRowIndex = firstVisibleRow;
  }

  private void UpdateStatisticsDisplay()
  {
    if (InvokeRequired)
    {
      Invoke(new Action(UpdateStatisticsDisplay));
      return;
    }

    // Create snapshot of data to avoid holding lock during UI operations
    DateTime? raceStartSnapshot;
    DateTime? raceEndSnapshot;
    int riderCount;
    int dnfCount;
    int totalLaps;
    string lastTagSnapshot;
    DateTime lastTagTimeSnapshot;
    bool raceFinishedSnapshot;
    DateTime? additionalLapsSignShown;
    DateTime? raceActuallyEnded;
    int additionalLapsCount;

    lock (ridersLock)
    {
      raceStartSnapshot = raceStartTime;
      raceEndSnapshot = raceEndTime;

      // Filter out ignored riders from statistics
      var activeRiders = riders.Values.Where(r => !ignoredTags.Contains(r.TagID));
      riderCount = activeRiders.Count();
      dnfCount = activeRiders.Count(r => r.IsDNF);
      totalLaps = activeRiders.Sum(r => r.TotalLaps);
      lastTagSnapshot = lastTagID;
      lastTagTimeSnapshot = lastTagTime;
      raceFinishedSnapshot = raceFinished;

      // Additional timing information. These come straight from Form1's own
      // fields: raceEndTime is overwritten with the true finish time in
      // CompletelyFinishRace, and finalLapsStartTime is set in FinishRace.
      additionalLapsSignShown = finalLapsStartTime;
      raceActuallyEnded = raceFinished ? raceEndTime : null;
      additionalLapsCount = additionalLapsAfterTimeExpiry;
    }

    // Update race time - stop updating when race is finished
    if (raceStartSnapshot.HasValue)
    {
      if (raceFinishedSnapshot && raceActuallyEnded.HasValue)
      {
        // Race is finished - show final race time based on when it actually ended
        var finalElapsed = raceActuallyEnded.Value - raceStartSnapshot.Value;
        labelRaceTime.Text = $"Race Time: {finalElapsed:hh\\:mm\\:ss} (Final)";
      }
      else
      {
        // Race is ongoing - show current elapsed time
        var elapsed = DateTime.Now - raceStartSnapshot.Value;
        labelRaceTime.Text = $"Race Time: {elapsed:hh\\:mm\\:ss}";
      }
    }
    else
    {
      labelRaceTime.Text = "Race Time: Not Started";
    }

    // Update rider count - only show DNF count after race is completely finished
    if (raceFinishedSnapshot && dnfCount > 0)
    {
      labelTotalRiders.Text = $"Total Riders: {riderCount} ({dnfCount} DNF)";
    }
    else
    {
      labelTotalRiders.Text = $"Total Riders: {riderCount}";
    }

    // Update total laps
    labelTotalLaps.Text = $"Total Laps: {totalLaps}";

    // Update last tag info
    if (lastTagSnapshot != "None" && lastTagTimeSnapshot != DateTime.MinValue)
    {
      var timeSince = DateTime.Now - lastTagTimeSnapshot;
      labelLastTag.Text = $"Last read: {GetRiderDisplayText(lastTagSnapshot)}, {timeSince.TotalSeconds:F0}s ago";
    }
    else
    {
      labelLastTag.Text = "Last read: none yet";
    }

    {
      // Show additional timing information for race progression or next expected crossing
      if (additionalLapsSignShown.HasValue && raceStartSnapshot.HasValue)
      {
        var raceTimeWhenSignShown = additionalLapsSignShown.Value - raceStartSnapshot.Value;
        var timingText = $"🏁 Additional Laps Board: {raceTimeWhenSignShown:mm\\:ss}";

        if (raceActuallyEnded.HasValue)
        {
          var actualRaceTime = raceActuallyEnded.Value - raceStartSnapshot.Value;
          timingText += $" | Final: {actualRaceTime:mm\\:ss} (+{additionalLapsCount} laps)";
        }

        // Show additional timing info when race is in final/finished stages
        labelNextCrossing.Text = timingText;
      }
      else
      {
        // Show normal next crossing info when no additional timing to display
        ShowNextExpectedCrossing();
      }

      // Update race end time - only update if race isn't actually finished
      if (!raceFinishedSnapshot)
      {
        if (raceEndSnapshot.HasValue)
        {
          labelRaceEndTime.Text = $"Race End: {raceEndSnapshot:HH:mm:ss}";

          // Update time remaining
          var timeRemaining = GetTimeRemaining();
          if (timeRemaining > TimeSpan.Zero)
          {
            labelTimeRemaining.Text = $"Time Remaining: {timeRemaining:mm\\:ss}";
            labelTimeRemaining.ForeColor = timeRemaining.TotalMinutes <= 5 ? Color.Red : Color.DarkRed;

          }
          else
          {
            labelTimeRemaining.Text = "Time Remaining: Race Finished";
            labelTimeRemaining.ForeColor = Color.Red;
          }
        }
        else
        {
          labelRaceEndTime.Text = "Race End: Not Set";
          labelTimeRemaining.Text = "Time Remaining: N/A";
        }

        // Update predicted laps - only update if race isn't finished
        var predictedLaps = CalculatePredictedLaps();
        var leaderInfo = GetLeaderPredictionInfo();
        if (predictedLaps > 0)
        {
          labelPredictedLaps.Text = $"Predicted Laps (Leader): {predictedLaps}{leaderInfo}";
        }
        else
        {
          labelPredictedLaps.Text = "Predicted Laps (Leader): Calculating...";
        }
      }
      else
      {
        // Race is finished - show final state with actual race end time
        labelTimeRemaining.Text = "Time Remaining: Race Finished";
        labelTimeRemaining.ForeColor = Color.Red;

        if (raceActuallyEnded.HasValue)
        {
          // Show actual race end time when leader finished
          labelRaceEndTime.Text = $"Race End: {raceActuallyEnded.Value:HH:mm:ss}";
        }
        else if (raceEndSnapshot.HasValue)
        {
          labelRaceEndTime.Text = $"Race End: {raceEndSnapshot:HH:mm:ss}";
        }

        // Show final race time if available
        if (raceActuallyEnded.HasValue && raceStartSnapshot.HasValue)
        {
          var finalRaceTime = raceActuallyEnded.Value - raceStartSnapshot.Value;
          labelPredictedLaps.Text = $"Final Race Time: {finalRaceTime:mm\\:ss}";
        }
      }
    }
  }
  /// <summary>
  /// Fires the countdown warnings. Driven purely by the clock and deliberately
  /// independent of which tab is on screen: these used to live inside the
  /// Race Statistics paint path, so a warning was missed entirely unless the
  /// operator happened to be looking at that tab as the clock crossed.
  /// </summary>
  private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);
  private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

  private void CheckRaceClockMilestones()
  {
    if (!raceStarted || raceFinished || !raceEndTime.HasValue) return;

    var timeRemaining = GetTimeRemaining();
    if (timeRemaining <= TimeSpan.Zero) return;

    if (RaceClockMilestones.ShouldAnnounce(
          timeRemaining, raceDuration, OneMinute, oneMinuteWarningShown))
    {
      AddMessage("⚠️ 1 MINUTE REMAINING!");
      RaiseNotice(NoticeLevel.Critical, "1 minute left");
      oneMinuteWarningShown = true;
      return;
    }

    if (RaceClockMilestones.ShouldAnnounce(
          timeRemaining, raceDuration, FiveMinutes, fiveMinuteWarningShown))
    {
      AddMessage("⚠️ 5 MINUTES REMAINING!");
      RaiseNotice(NoticeLevel.Warning, "5 minutes left");
      fiveMinuteWarningShown = true;
    }
  }

  private void ShowNextExpectedCrossing()
  {
    // Create snapshot to avoid nested locking
    RiderInfo? nextRider = null;

    lock (ridersLock)
    {
      // Find the rider expected to cross next (exclude ignored riders)
      nextRider = riders.Values
        .Where(r => r.EstimatedNextCrossing.HasValue && !ignoredTags.Contains(r.TagID))
        .OrderBy(r => r.EstimatedNextCrossing!.Value)
        .FirstOrDefault();
    }

    string nextCrossingInfo = "Next Expected: None";

    if (nextRider != null && nextRider.EstimatedNextCrossing.HasValue)
    {
      var nextTime = nextRider.EstimatedNextCrossing.Value;
      var timeToNext = nextTime - DateTime.Now;
      var riderDisplay = GetRiderDisplayText(nextRider);

      if (timeToNext > TimeSpan.Zero)
      {
        if (timeToNext.TotalMinutes < 1)
          nextCrossingInfo = $"Next Expected: {riderDisplay} in {timeToNext.TotalSeconds:F0}s";
        else
          nextCrossingInfo = $"Next Expected: {riderDisplay} in {timeToNext:mm\\:ss}";
      }
      else
      {
        nextCrossingInfo = $"Overdue: {riderDisplay} (expected {Math.Abs(timeToNext.TotalSeconds):F0}s ago)";
      }
    }

    // Update the label
    labelNextCrossing.Text = nextCrossingInfo;
  }

  private int CalculatePredictedLaps()
  {
    if (!raceStartTime.HasValue || !raceEndTime.HasValue)
      return 0;

    // Create snapshot to avoid holding lock during calculations
    RiderInfo? leader = null;

    lock (ridersLock)
    {
      // Find the current leader (exclude DNF riders and ignored riders)
      leader = riders.Values
        .Where(r => !r.IsDNF && !ignoredTags.Contains(r.TagID))
        .OrderByDescending(r => r.TotalLaps)
        .ThenBy(r => r.TotalTime)
        .FirstOrDefault();
    }

    if (leader == null)
      return 0;

    // If leader has no completed laps with times yet, estimate based on race progress
    if (leader.PredictedLapTime == null)
    {
      // Use elapsed time per lap as rough estimate
      var raceElapsed = DateTime.Now - raceStartTime.Value;
      if (leader.TotalLaps > 0 && raceElapsed.TotalMinutes > 0.5) // At least 30 seconds elapsed
      {
        var avgTimePerLap = raceElapsed.TotalMilliseconds / leader.TotalLaps;
        var totalRaceTime = raceDuration.TotalMilliseconds;
        return (int)(totalRaceTime / avgTimePerLap);
      }
      return 0;
    }

    // Calculate how much time is left in the race
    var timeRemaining = raceEndTime.Value - DateTime.Now;
    if (timeRemaining <= TimeSpan.Zero)
      return leader.TotalLaps; // Race is over

    // Estimate how many more laps the leader can complete
    var additionalLaps = Math.Floor(timeRemaining.TotalMilliseconds / leader.PredictedLapTime.Value.TotalMilliseconds);

    // Add a small buffer for partial laps
    var totalPredictedLaps = leader.TotalLaps + (int)additionalLaps;

    // If the leader is close to completing another lap, include it
    if (leader.EstimatedNextCrossing.HasValue)
    {
      var timeToNextCrossing = leader.EstimatedNextCrossing.Value - DateTime.Now;
      if (timeToNextCrossing <= timeRemaining && timeToNextCrossing > TimeSpan.Zero)
      {
        // Leader will likely complete at least one more lap
        var remainingAfterNextLap = timeRemaining - timeToNextCrossing;
        if (remainingAfterNextLap > leader.PredictedLapTime.Value.Multiply(0.5)) // At least half a lap time remaining
        {
          totalPredictedLaps += 1;
        }
      }
    }

    return Math.Max(totalPredictedLaps, leader.TotalLaps); // Never predict fewer laps than already completed
  }

  private TimeSpan GetTimeRemaining()
  {
    if (!raceEndTime.HasValue)
      return TimeSpan.Zero;

    var remaining = raceEndTime.Value - DateTime.Now;
    return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
  }

  /// <summary>
  /// Race-logic heartbeat. All view repainting is driven by
  /// <see cref="UiRefreshCoordinator"/>; only work that must happen regardless
  /// of which tab is on screen belongs here.
  /// </summary>
  private void timerUpdate_Tick(object? sender, EventArgs e)
  {
    CheckRaceClockMilestones();
    CheckReaderHealth();
    UpdateStatusBar();

    // In a timed session the clock raises the flag. A race waits for the next
    // crossing instead, because it needs the leader's lap count to set a target.
    if (IsTimedSession)
    {
      CheckTimedSessionExpiry();
    }

    // Check for DNF timeouts if we're in final laps phase
    if (waitingForFinalLaps)
    {
      CheckIfAllFinalLapsCompleted();
    }
  }

  private void UpdateRiderPredictions()
  {
    if (InvokeRequired)
    {
      Invoke(new Action(UpdateRiderPredictions));
      return;
    }

    // Don't update predictions if race is completely finished
    if (raceFinished)
      return;

    if (_riderRows.Count == 0)
      return;

    // Create snapshot of data to avoid holding lock during UI updates
    List<RiderInfo> riderSnapshot;
    bool raceFinishedSnapshot;

    lock (ridersLock)
    {
      riderSnapshot = riders.Values.Where(r => !ignoredTags.Contains(r.TagID)).ToList();
      raceFinishedSnapshot = raceFinished;
    }

    try
    {
      // Only the two countdown columns move between crossings. Update them in
      // the backing rows and repaint - in virtual mode there are no cells to write.
      var byTag = new Dictionary<string, RiderInfo>(riderSnapshot.Count);
      foreach (var r in riderSnapshot) byTag[r.TagID] = r;

      var changed = false;

      for (var i = 0; i < _riderRows.Count; i++)
      {
        var row = _riderRows[i];
        if (!byTag.TryGetValue(row.TagID, out var rider)) continue;

        var nextCrossingStr = "N/A";
        var timeToNextStr = "N/A";

        if (rider.IsDNF)
        {
          nextCrossingStr = "DNF";
          timeToNextStr = "DNF";
        }
        else if (raceFinishedSnapshot)
        {
          nextCrossingStr = "Race Finished";
          timeToNextStr = "Race Finished";
        }
        else if (rider.EstimatedNextCrossing.HasValue)
        {
          var nextTime = rider.EstimatedNextCrossing.Value;
          nextCrossingStr = nextTime.ToString("HH:mm:ss");

          var timeToNext = nextTime - DateTime.Now;
          if (timeToNext > TimeSpan.Zero)
          {
            timeToNextStr = timeToNext.TotalMinutes < 1
              ? $"{timeToNext.TotalSeconds:F0}s"
              : $"{timeToNext:mm\\:ss}";
          }
          else
          {
            timeToNextStr = "Overdue";
          }
        }

        if (row.Cells[RiderRowData.ColNextCrossing] != nextCrossingStr ||
            row.Cells[RiderRowData.ColTimeToNext] != timeToNextStr)
        {
          row.Cells[RiderRowData.ColNextCrossing] = nextCrossingStr;
          row.Cells[RiderRowData.ColTimeToNext] = timeToNextStr;
          changed = true;
        }
      }

      // Repaint just the two columns that moved, and only where they are visible.
      if (changed)
      {
        dataGridViewRiders.InvalidateColumn(RiderRowData.ColNextCrossing);
        dataGridViewRiders.InvalidateColumn(RiderRowData.ColTimeToNext);
      }
    }
    catch (Exception ex)
    {
      // Log any errors but don't crash the app
      AddMessage($"Error updating rider predictions: {ex.Message}");
    }
  }

  private string GetLeaderPredictionInfo()
  {
    if (!raceStartTime.HasValue || !raceEndTime.HasValue)
      return "";

    // Create snapshot to avoid holding lock during calculations
    RiderInfo? leader = null;

    lock (ridersLock)
    {
      // Find the current leader (exclude DNF riders and ignored riders)
      leader = riders.Values
        .Where(r => !r.IsDNF && !ignoredTags.Contains(r.TagID))
        .OrderByDescending(r => r.TotalLaps)
        .ThenBy(r => r.TotalTime)
        .FirstOrDefault();
    }

    if (leader == null)
      return "";

    var timeRemaining = GetTimeRemaining();
    if (timeRemaining <= TimeSpan.Zero)
      return " (Race Finished)";

    if (leader.PredictedLapTime.HasValue)
    {
      return $" (Avg: {leader.PredictedLapTime.Value:mm\\:ss\\.fff})";
    }
    else if (leader.TotalLaps > 0)
    {
      var raceElapsed = DateTime.Now - raceStartTime.Value;
      var avgTimePerLap = TimeSpan.FromMilliseconds(raceElapsed.TotalMilliseconds / leader.TotalLaps);
      return $" (Est. Avg: {avgTimePerLap:mm\\:ss\\.fff})";
    }

    return " (Calculating...)";
  }

  private void buttonSetFilter_Click(object? sender, EventArgs e)
  {
    tagFilterPrefix = textBoxTagFilter.Text.Trim();

    if (string.IsNullOrEmpty(tagFilterPrefix))
    {
      checkBoxFilterEnabled.Checked = false;
      tagFilterEnabled = false;
      AddMessage("🔍 Tag filter cleared - all tags will be processed.");
    }
    else
    {
      // Show info about multiple prefixes
      var prefixes = tagFilterPrefix.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(p => p.Trim())
                                    .Where(p => !string.IsNullOrEmpty(p))
                                    .ToList();

      if (prefixes.Count > 1)
      {
        AddMessage($"🔍 Tag filter set to prefixes: {string.Join(", ", prefixes.Select(p => $"'{p}'"))} (Filter enabled: {checkBoxFilterEnabled.Checked})");
      }
      else
      {
        AddMessage($"🔍 Tag filter set to prefix: '{tagFilterPrefix}' (Filter enabled: {checkBoxFilterEnabled.Checked})");
      }

      tagFilterEnabled = checkBoxFilterEnabled.Checked;
    }
  }

  private void ComboBoxClassFilter_SelectedIndexChanged(object? sender, EventArgs e)
  {
    if (comboBoxClassFilter.SelectedItem == null) return;

    selectedClassFilter = comboBoxClassFilter.SelectedItem.ToString() ?? "All Classes";

    // PopulateClassFilter runs from the constructor and sets SelectedItem, which
    // fires this handler before Form1_Load has built the refresh coordinator.
    // There is nothing to repaint that early - the first render comes from
    // InitializeRefreshCoordinator.
    _refresh?.RenderNow(RaceViewKind.Riders);
  }

  /// <summary>
  /// Every class in the meeting, from riders seen so far and from the imported
  /// list. Shared by the riders grid's filter and the track map's, which keep
  /// their own SELECTED value: the grid filter is a working tool for the
  /// operator, the map filter is a display for everyone else, and narrowing one
  /// must not silently empty the other.
  /// </summary>
  private List<string> AvailableClasses()
  {
    List<string> classes;

    lock (ridersLock)
    {
      classes = riders.Values
        .Where(r => !string.IsNullOrEmpty(r.Category))
        .Select(r => r.Category)
        .Distinct()
        .OrderBy(c => c)
        .ToList();
    }

    classes.AddRange(_riderDataImporter.GetAllRiderData().Values
      .Where(r => !string.IsNullOrEmpty(r.Category))
      .Select(r => r.Category)
      .Distinct()
      .Where(c => !classes.Contains(c))
      .OrderBy(c => c));

    return classes;
  }

  private void PopulateClassFilter()
  {
    if (comboBoxClassFilter == null) return;

    // Always include "All Classes" as the first option
    var classOptions = new List<string> { "All Classes" };
    classOptions.AddRange(AvailableClasses());

    // Update ComboBox
    var currentSelection = comboBoxClassFilter.SelectedItem?.ToString();
    comboBoxClassFilter.Items.Clear();
    comboBoxClassFilter.Items.AddRange(classOptions.ToArray());

    // Restore selection or default to "All Classes"
    if (!string.IsNullOrEmpty(currentSelection) && classOptions.Contains(currentSelection))
    {
      comboBoxClassFilter.SelectedItem = currentSelection;
      selectedClassFilter = currentSelection;
    }
    else
    {
      comboBoxClassFilter.SelectedItem = "All Classes";
      selectedClassFilter = "All Classes";
    }
  }

  private void checkBoxFilterEnabled_CheckedChanged(object? sender, EventArgs e)
  {
    tagFilterEnabled = checkBoxFilterEnabled.Checked;

    if (tagFilterEnabled && !string.IsNullOrEmpty(tagFilterPrefix))
    {
      AddMessage($"🔍 Tag filter enabled for prefix: '{tagFilterPrefix}'");
    }
    else
    {
      AddMessage("🔍 Tag filter disabled - all tags will be processed.");
    }
  }

  private bool ShouldProcessTag(string tagID)
  {
    // First check if tag is in the ignore list
    if (ignoredTags.Contains(tagID))
      return false;

    if (!tagFilterEnabled || string.IsNullOrEmpty(tagFilterPrefix))
      return true;

    // Support multiple prefixes separated by commas
    var prefixes = tagFilterPrefix.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                  .Select(p => p.Trim())
                                  .Where(p => !string.IsNullOrEmpty(p));

    foreach (var prefix in prefixes)
    {
      if (tagID.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        return true;
    }

    return false;
  }

  #region Tag Ignore List Management

  /// <summary>
  /// Stops counting a transponder.
  ///
  /// This used to delete the rider and every recorded lap outright, from memory
  /// and from the database, with no confirmation and no way back - and it was
  /// bound to the Delete key. Ignoring now defaults to affecting future reads
  /// only; discarding the laps already recorded is a separate, explicit choice.
  /// </summary>
  private void AddTagToIgnoreList(string tagID)
  {
    if (string.IsNullOrWhiteSpace(tagID))
      return;

    if (ignoredTags.Contains(tagID))
    {
      AddMessage($"⚠️ {GetRiderDisplayText(tagID)} is already being ignored.");
      return;
    }

    // Snapshot what is at stake before asking, and hold no lock across the dialog.
    int recordedLaps;
    bool hasExistingData;
    lock (ridersLock)
    {
      hasExistingData = riders.TryGetValue(tagID, out var existing);
      recordedLaps = hasExistingData ? existing!.TotalLaps : 0;
    }

    var discardRecordedLaps = false;

    if (hasExistingData && recordedLaps > 0)
    {
      var answer = MessageBox.Show(
        $"Stop counting {GetRiderDisplayText(tagID)}?\n\n" +
        $"Yes - ignore future reads and delete the {recordedLaps} lap(s) already recorded.\n" +
        "No - ignore future reads but keep the laps already recorded.\n" +
        "Cancel - do nothing.",
        "Ignore transponder",
        MessageBoxButtons.YesNoCancel,
        MessageBoxIcon.Warning,
        MessageBoxDefaultButton.Button2);

      if (answer == DialogResult.Cancel) return;
      discardRecordedLaps = answer == DialogResult.Yes;
    }

    if (!ignoredTags.Add(tagID))
      return;

    AddMessage($"⛔ Now ignoring {GetRiderDisplayText(tagID)}. Ignored transponders: {ignoredTags.Count}");

    if (!discardRecordedLaps)
    {
      // Laps stay on record; the rider simply drops out of the standings.
      _refresh.Invalidate(RaceViewKind.All);
      return;
    }

    lock (ridersLock)
    {
      if (riders.Remove(tagID))
      {
        lastKnownPositions.Remove(tagID);
        lastKnownLapCounts.Remove(tagID);
        AddMessage($"🗑️ Deleted {recordedLaps} recorded lap(s) for the ignored transponder.");
      }
    }

    _refresh.Invalidate(RaceViewKind.All);

    if (currentRaceId.HasValue)
    {
      Task.Run(() =>
      {
        try
        {
          foreach (var lap in _raceDb.GetRiderLaps(tagID))
            _raceDb.DeleteLap(tagID, lap.LapNumber);
        }
        catch (Exception ex)
        {
          AddDiagnostic($"Could not delete stored laps for {tagID}: {ex.Message}");
        }
      });
    }
  }

  /// <summary>
  /// Removes a tag from the ignore list
  /// </summary>
  private void RemoveTagFromIgnoreList(string tagID)
  {
    if (ignoredTags.Remove(tagID))
    {
      AddMessage($"✅ Removed tag '{tagID}' from ignore list. Total ignored tags: {ignoredTags.Count}");
    }
    else
    {
      AddMessage($"⚠️ Tag '{tagID}' was not found in the ignore list.");
    }
  }

  /// <summary>
  /// Clears all tags from the ignore list
  /// </summary>
  private void ClearIgnoreList()
  {
    var count = ignoredTags.Count;
    ignoredTags.Clear();
    ignoredTagCount = 0;
    AddMessage($"🗑️ Cleared ignore list. Removed {count} ignored tags.");
  }

  /// <summary>
  /// Shows the current ignore list
  /// </summary>
  private void ShowIgnoreList()
  {
    if (ignoredTags.Count == 0)
    {
      AddMessage("📋 Ignore list is empty.");
      return;
    }

    AddMessage($"📋 Current ignore list ({ignoredTags.Count} tags):");
    foreach (var tag in ignoredTags.OrderBy(t => t))
    {
      AddMessage($"   ⛔ {tag}");
    }
  }

  #endregion

  #region Context Menu Handlers

  /// <summary>
  /// Handles adding the selected tag to the ignore list from the riders grid context menu
  /// </summary>
  private void HandleAddTagToIgnoreList()
  {
    if (dataGridViewRiders.SelectedRows.Count > 0)
    {
      var tagID = SelectedRiderTag();

      if (!string.IsNullOrEmpty(tagID))
      {
        AddTagToIgnoreList(tagID);
      }
    }
    else if (dataGridViewRiders.CurrentCell != null)
    {
      var currentRow = dataGridViewRiders.CurrentCell.OwningRow;
      if (currentRow != null)
      {
        var tagIDCell = currentRow.Cells["TagID"];
        var tagID = tagIDCell?.Value?.ToString();

        if (!string.IsNullOrEmpty(tagID))
        {
          AddTagToIgnoreList(tagID);
        }
      }
    }
    else
    {
      AddMessage("⚠️ No rider selected. Please select a rider first.");
    }
  }

  /// <summary>
  /// Handles removing the selected tag from the ignore list from the riders grid context menu
  /// </summary>
  private void HandleRemoveTagFromIgnoreList()
  {
    if (dataGridViewRiders.SelectedRows.Count > 0)
    {
      var tagID = SelectedRiderTag();

      if (!string.IsNullOrEmpty(tagID))
      {
        RemoveTagFromIgnoreList(tagID);
      }
    }
    else if (dataGridViewRiders.CurrentCell != null)
    {
      var currentRow = dataGridViewRiders.CurrentCell.OwningRow;
      if (currentRow != null)
      {
        var tagIDCell = currentRow.Cells["TagID"];
        var tagID = tagIDCell?.Value?.ToString();

        if (!string.IsNullOrEmpty(tagID))
        {
          RemoveTagFromIgnoreList(tagID);
        }
      }
    }
    else
    {
      AddMessage("⚠️ No rider selected. Please select a rider first.");
    }
  }

  /// <summary>
  /// Handles adding the selected tag to the ignore list from the tag events list context menu
  /// </summary>
  private void HandleTagEventsAddToIgnoreList()
  {
    string? tagId = ExtractTagFromSelectedEvent();
    if (!string.IsNullOrEmpty(tagId))
    {
      AddTagToIgnoreList(tagId);
    }
    else
    {
      AddMessage("⚠️ No tag event selected or tag ID could not be extracted.");
    }
  }

  /// <summary>
  /// Handles removing the selected tag from the ignore list from the tag events list context menu
  /// </summary>
  private void HandleTagEventsRemoveFromIgnoreList()
  {
    string? tagId = ExtractTagFromSelectedEvent();
    if (!string.IsNullOrEmpty(tagId))
    {
      RemoveTagFromIgnoreList(tagId);
    }
    else
    {
      AddMessage("⚠️ No tag event selected or tag ID could not be extracted.");
    }
  }

  /// <summary>
  /// The transponder of the currently selected rider row, taken from the row's
  /// Tag. Cell text carries status decorations and must never be parsed back.
  /// </summary>
  private string? SelectedRiderTag()
  {
    var index = dataGridViewRiders.CurrentRow?.Index ?? -1;
    return index >= 0 && index < _riderRows.Count ? _riderRows[index].TagID : null;
  }

  /// <summary>
  /// The transponder referred to by the selected Tag Events line, if any.
  /// </summary>
  private string? ExtractTagFromSelectedEvent()
    => (listBoxTagEvents.SelectedItem as TagEventItem)?.TagId;

  #endregion

  private void panelLapChart_Paint(object? sender, PaintEventArgs e)
  {
    try
    {
      // Delegate to the lap chart renderer using the actual race state from Form1
      // _lapChartSnapshot is a private copy taken under ridersLock by
      // RefreshLapChart. Painting straight from `riders` raced with the network
      // thread and threw "Collection was modified" mid-draw.
      _lapChartRenderer.DrawLapChart(e.Graphics, panelLapChart.ClientRectangle, _lapChartSnapshot,
        raceStartTime, raceEndTime, raceDuration, panelLapChart);
    }
    catch (Exception ex)
    {
      // Log any errors but don't crash the app
      AddMessage($"Error drawing lap chart: {ex.Message}");
    }
  }

  // DrawLapChart method removed - functionality moved to LapChartRenderer



  private void PanelLapChart_MouseClick(object? sender, MouseEventArgs e)
  {
    // Adjust mouse position for scroll offset
    var adjustedLocation = new Point(e.Location.X - panelLapChart.AutoScrollPosition.X,
                                   e.Location.Y - panelLapChart.AutoScrollPosition.Y);

    if (e.Button == MouseButtons.Left)
    {
      // Delegate to the lap chart renderer for left clicks
      _lapChartRenderer.HandleMouseClick(adjustedLocation, OpenLapCorrection, () => panelLapChart.Invalidate());
    }
    else if (e.Button == MouseButtons.Right)
    {
      // Handle right-click for context menu
      string? tagId = _lapChartRenderer.HandleRightClick(adjustedLocation);
      if (!string.IsNullOrEmpty(tagId))
      {
        ShowTagContextMenu(tagId, e.Location);
      }
    }
  }

  private void PanelLapChart_MouseMove(object? sender, MouseEventArgs e)
  {
    // Adjust mouse position for scroll offset
    var adjustedLocation = new Point(e.Location.X - panelLapChart.AutoScrollPosition.X,
                                   e.Location.Y - panelLapChart.AutoScrollPosition.Y);

    // Delegate to the lap chart renderer
    bool hasHoveredElement = _lapChartRenderer.HandleMouseMove(adjustedLocation, () => panelLapChart.Invalidate(), _lapChartSnapshot);

    // Change cursor when hovering over clickable elements
    panelLapChart.Cursor = hasHoveredElement ? Cursors.Hand : Cursors.Default;
  }

  private void PanelLapChart_MouseLeave(object? sender, EventArgs e)
  {
    // Delegate to the lap chart renderer
    _lapChartRenderer.HandleMouseLeave(() => panelLapChart.Invalidate());
    panelLapChart.Cursor = Cursors.Default;
  }

  private void ShowTagContextMenu(string tagId, Point location)
  {
    var contextMenu = new ContextMenuStrip();

    bool isIgnored = ignoredTags.Contains(tagId);

    if (isIgnored)
    {
      var removeItem = new ToolStripMenuItem($"Count {GetRiderDisplayText(tagId)} again");
      removeItem.Click += (s, e) => RemoveTagFromIgnoreList(tagId);
      contextMenu.Items.Add(removeItem);
    }
    else
    {
      var addItem = new ToolStripMenuItem($"Stop counting {GetRiderDisplayText(tagId)}...");
      addItem.Click += (s, e) => AddTagToIgnoreList(tagId);
      contextMenu.Items.Add(addItem);
    }

    contextMenu.Items.Add(new ToolStripSeparator());

    var showListItem = new ToolStripMenuItem("Show ignore list...");
    showListItem.Click += (s, e) => ShowIgnoreList();
    contextMenu.Items.Add(showListItem);

    var clearListItem = new ToolStripMenuItem("Clear ignore list");
    clearListItem.Click += (s, e) => ClearIgnoreList();
    clearListItem.Enabled = ignoredTags.Count > 0;
    contextMenu.Items.Add(clearListItem);

    contextMenu.Show(panelLapChart, location);
  }





  private void RaceStartMode_CheckedChanged(object? sender, EventArgs e)
  {
    manualStartMode = radioButtonStartManual.Checked;
    UpdateRaceStartControls();
  
    RememberRaceSetup();
  }

  private void UpdateRaceStartControls()
  {
    if (InvokeRequired)
    {
      BeginInvoke(new Action(UpdateRaceStartControls));
      return;
    }

    buttonStartRace.Enabled = manualStartMode && !raceStarted && !raceFinished;
    UpdateSessionTypeLock();

    if (raceFinished)
    {
      labelRaceStatus.Text = IsTimedSession ? "Session: OVER" : "Race: FINISHED";
      labelRaceStatus.ForeColor = Color.Blue;
    }
    else if (waitingForFinalLaps)
    {
      // Count how many riders are still eligible to complete their final lap
      var ridersStillActive = riders.Values.Count(r => r.TotalLaps < r.FinalAllowedLap &&
                                                       (r.PredictedLapTime.HasValue ||
                                                        (DateTime.Now - r.LastCrossing).TotalMinutes < 2));

      // In a timed session no leader has finished - the clock ran out. Saying
      // otherwise is the one place this label states something untrue.
      labelRaceStatus.Text = IsTimedSession
        ? $"Session: CHEQUERED FLAG - {ridersStillActive} riders finishing their lap"
        : $"Race: LEADER FINISHED - {ridersStillActive} riders completing final lap";
      labelRaceStatus.ForeColor = Color.DarkBlue;
    }
    else if (raceTimeExpired)
    {
      labelRaceStatus.Text = $"Race: TIME EXPIRED - Waiting for ongoing lap to complete";
      labelRaceStatus.ForeColor = Color.Orange;
    }
    else if (waitingForLeaderFinish)
    {
      // AMA Motocross regulations: Show status for the leader who was leading when time expired
      if (!string.IsNullOrEmpty(leaderAtTimeExpiry) && riders.ContainsKey(leaderAtTimeExpiry))
      {
        var leaderRider = riders[leaderAtTimeExpiry];
        var remainingLaps = targetLapsToFinishRace - leaderRider.TotalLaps;
        var lapsText = remainingLaps == 1 ? "lap" : "laps";
        labelRaceStatus.Text = $"Race: LEADER {GetRiderDisplayText(leaderAtTimeExpiry ?? "")} - {remainingLaps} {lapsText} to go (target: {targetLapsToFinishRace})";
      }
      else
      {
        labelRaceStatus.Text = $"Race: Waiting for Leader {GetRiderDisplayText(leaderAtTimeExpiry ?? "")} to complete additional laps";
      }
      labelRaceStatus.ForeColor = Color.Purple;
    }
    else if (raceStarted)
    {
      labelRaceStatus.Text = "Race: Started";
      labelRaceStatus.ForeColor = Color.Green;
    }
    else if (manualStartMode)
    {
      labelRaceStatus.Text = "Race: Ready to Start";
      labelRaceStatus.ForeColor = Color.Orange;
    }
    else
    {
      labelRaceStatus.Text = "Race: Waiting for First Tag";
      labelRaceStatus.ForeColor = Color.DarkRed;
    }
  }

  private void buttonStartRace_Click(object? sender, EventArgs e)
  {
    if (manualStartMode && !raceStarted)
    {
      raceStartTime = DateTime.Now;
      raceEndTime = raceStartTime.Value + raceDuration;
      raceStarted = true;

      // Create new race in database
      currentRaceId = _raceDb.StartNewRace(raceStartTime.Value, raceDuration, raceName, sessionType);

      // Update race start time for all existing riders
      lock (ridersLock)
      {
        foreach (var rider in riders.Values)
        {
          rider.RaceStartTime = raceStartTime;
        }
      }

      UpdateRaceStartControls();
      AddMessage($"🏁 Race started manually at {raceStartTime.Value:HH:mm:ss}");
    RaiseNotice(NoticeLevel.Info, "Race started");

      // Reset warnings
      fiveMinuteWarningShown = false;
      oneMinuteWarningShown = false;

      // Update displays to reflect new total times
      _refresh.Invalidate(RaceViewKind.Standings);
    }
  }

  /// <summary>
  /// Chequered flag. Every rider still on track finishes the lap they are on
  /// and that lap counts; nothing after it does. This is what a timed session
  /// does at time expiry, and what a race does when it is configured with no
  /// extra laps.
  ///
  /// The caller must hold <see cref="ridersLock"/>.
  /// </summary>
  private void BeginFinalLapPhase(List<(string, bool)> messagesToAdd)
  {
    // Counted from crossing times rather than from TotalLaps so the allowance
    // is exact whichever of the clock timer and the network thread reaches the
    // lock first. The lock serialises the two but does not order them, so a
    // rider whose crossing lands in the same second as expiry would otherwise
    // be granted a whole extra lap.
    var flagAt = raceEndTime ?? DateTime.Now;

    foreach (var rider in riders.Values.Where(r => !r.IsDNF && !ignoredTags.Contains(r.TagID)))
    {
      rider.FinalAllowedLap = rider.LapsCompletedBy(flagAt) + 1;
    }

    waitingForFinalLaps = true;
    finalLapsStartTime = DateTime.Now;
    raceTimeExpired = false;

    messagesToAdd.Add((IsTimedSession
      ? "🏁 Chequered flag - every rider finishes the lap they are on, and it counts."
      : "🏁 Final laps phase started - each rider must finish their current lap only.", true));
  }

  /// <summary>
  /// Raises the flag from the clock in a timed session.
  ///
  /// The expiry check inside ProcessNormalCrossingInternal only runs when a
  /// crossing arrives, and is nested inside a leader lookup. In a race that is
  /// invisible. In a timed session it hangs: if the last rider on track crosses
  /// a second before expiry and everyone then pulls in, no further crossing
  /// arrives, the final-lap phase never starts, the grace never begins, and the
  /// Race Day tile sits on a running clock at 00:00 forever.
  ///
  /// Deliberately does not require a leader - a session where nobody has
  /// crossed at all must still be able to end.
  /// </summary>
  private void CheckTimedSessionExpiry()
  {
    if (!raceStarted || raceFinished || waitingForFinalLaps) return;
    if (!raceEndTime.HasValue || DateTime.Now <= raceEndTime.Value) return;

    var messagesToAdd = new List<(string, bool)>();

    lock (ridersLock)
    {
      // Re-check under the lock: a crossing may have raised the flag already.
      if (waitingForFinalLaps || raceFinished) return;
      messagesToAdd.Add(("⏰ Session time expired.", true));
      BeginFinalLapPhase(messagesToAdd);
    }

    // Same convention as the crossing path: emitted outside the lock.
    foreach (var (message, isRaceEvent) in messagesToAdd)
    {
      if (isRaceEvent)
        AddRaceEvent(message);
      else
        AddTagEvent(message);
    }

    RaiseNotice(NoticeLevel.Critical, "Chequered flag - finish the lap you are on");
    UpdateRaceStartControls();
    _refresh.Invalidate(RaceViewKind.All);
  }

  private void FinishRace()
  {
    // Reaching this in a timed session means the gate in the expiry block
    // leaked and the race finishing rules are running over a practice session.
    // Without this line the only symptom would be a wrong sheet.
    if (IsTimedSession)
      AddDiagnostic("FinishRace reached in a timed session - the extra-laps gate leaked.");

    // Don't immediately finish - allow other riders to complete their current lap
    waitingForLeaderFinish = false;
    waitingForFinalLaps = true;
    finalLapsStartTime = DateTime.Now; // Track when final laps phase started

    // Calculate actual race finish time
    var actualRaceFinishTime = DateTime.Now;

    // Set the actual race end time to when the leader finished
    raceEndTime = actualRaceFinishTime;

    var actualRaceDuration = actualRaceFinishTime - raceStartTime!.Value;

    // Find the rider who just completed the target lap count
    var finishingRider = riders.Values
      .FirstOrDefault(r => r.TotalLaps >= targetLapsToFinishRace);

    var finishingRiderTag = finishingRider?.Label ?? "The leader";

    AddMessage($"🏁 RACE TARGET REACHED! {finishingRiderTag} completed {targetLapsToFinishRace} laps in {actualRaceDuration:mm\\:ss}.");
    RaiseNotice(NoticeLevel.Critical, "Leader has finished - everyone else completes their current lap");
    AddMessage($"🏁 All other riders must complete only their current lap, then no more laps will be counted.");

    // Store the current lap numbers for all riders at race finish
    lock (ridersLock)
    {
      foreach (var rider in riders.Values)
      {
        if (rider.TotalLaps >= targetLapsToFinishRace)
        {
          // Riders who reached the target are NOT allowed to complete another lap
          rider.FinalAllowedLap = rider.TotalLaps;
          AddMessage($"📋 Rider {rider.Label}: Reached target with {rider.TotalLaps} laps, RACE FINISHED - no more laps allowed");
        }
        else
        {
          // All other riders are allowed to complete exactly one more lap (their current lap)
          rider.FinalAllowedLap = rider.TotalLaps + 1;
          AddMessage($"📋 Rider {rider.Label}: Currently has {rider.TotalLaps} laps, allowed to complete lap {rider.FinalAllowedLap}");
        }
      }
    }

    // Update race status
    UpdateRaceStartControls();

    // Force final update of displays
    _refresh.Invalidate(RaceViewKind.Standings);
  }

  private void CheckIfAllFinalLapsCompleted()
  {
    // Check if all riders have either completed their final allowed lap or have timed out
    bool allRidersFinished = true;

    List<RiderInfo> field;
    lock (ridersLock)
    {
      // Called from a background task as well as the timer; take a copy of the
      // collection rather than enumerating it while crossings may be arriving.
      field = riders.Values.ToList();
    }

    // One deadline for the whole pass. A timed session stretches it to cover a
    // flag lap - see ChequeredFlag.Grace for why the configured value alone is
    // not safe there.
    var fieldPace = RaceProgress.MedianPace(field);
    var grace = TimeSpan.FromMinutes(dnfTimeoutMinutes);
    if (IsTimedSession) grace = ChequeredFlag.Grace(grace, fieldPace);

    // When the flag actually fell, for telling a rider who was still out from
    // one who had already pulled in.
    var flagTime = finalLapsStartTime ?? DateTime.Now;

    foreach (var rider in field)
    {
      // Skip riders already marked as DNF or DNS
      if (rider.IsDNF || rider.IsDNS)
        continue;

      // An operator who has already ruled on this rider outranks the timeout.
      if (rider.StatusSetByOperator)
        continue;

      // If rider hasn't reached their final allowed lap yet
      if (rider.TotalLaps < rider.FinalAllowedLap)
      {
        // Check if too much time has passed since leader finished (timeout)
        var timeSinceLeaderFinished = finalLapsStartTime.HasValue ?
          DateTime.Now - finalLapsStartTime.Value : TimeSpan.Zero;

        // A race waits out the grace for everyone: the leader has finished and
        // any rider still classified may yet complete their final lap. A timed
        // session can tell the two apart, because the flag falls on the clock
        // rather than on somebody finishing - so a rider whose last crossing is
        // already more than a lap old had pulled in before it and is not coming
        // round. There is nothing to wait for.
        var pace = TrackPositionSolver.UsablePace(rider.RacingPace) ?? fieldPace;
        var wasCirculating = !IsTimedSession
          || ChequeredFlag.WasCirculatingAtFlag(flagTime - rider.LastCrossing, pace);

        // Still inside the grace - the rider may yet come round.
        if (wasCirculating && timeSinceLeaderFinished < grace)
        {
          allRidersFinished = false;
          // Don't break - continue checking other riders for DNF timeout
        }
        else
        {
          // Rider has timed out. In a timed session this means "no longer on
          // track", not "did not finish": the qualifying order ignores IsDNF
          // entirely and ranks on the best lap they had already set.
          rider.IsDNF = true;
          rider.DNFTime = DateTime.Now;
          if (IsTimedSession)
          {
            // Only the rider who was actually out on a lap when the flag fell
            // is worth announcing. Everyone who had already pulled in is marked
            // for the same reason - they never cross again - and saying so for
            // each of them fills the feed with warnings that mean "finished
            // their session normally".
            if (wasCirculating)
            {
              rider.StatusReason = "Did not complete the lap after the flag";
              AddMessage($"🏁 {rider.Label} did not complete the lap after the flag - {timeSinceLeaderFinished.TotalMinutes:F1} min since the flag. Any time already set still counts.");
              AddRaceEvent($"Off track: {rider.Label} - did not complete the lap after the flag");
            }
            else
            {
              rider.StatusReason = "Was not on track when the flag fell";
            }
          }
          else
          {
            AddMessage($"🚫 Rider {rider.Label} marked as DNF (Did Not Finish) - {timeSinceLeaderFinished.TotalMinutes:F1} min since leader finished, failed to complete final lap");
            AddRaceEvent($"DNF: {rider.Label} - Timeout after {timeSinceLeaderFinished.TotalMinutes:F1} minutes");
          }

          // Update displays to show DNF status
          _refresh.Invalidate(RaceViewKind.Standings);
        }
      }
    }

    if (allRidersFinished)
    {
      CompletelyFinishRace();
    }
  }

  private void CompletelyFinishRace()
  {
    raceFinished = true;
    waitingForFinalLaps = false;
    finalLapsStartTime = null; // Reset final laps tracking

    var actualRaceFinishTime = DateTime.Now;

    // Set the actual race end time (only if not already set from leader finish)
    if (raceEndTime == null || raceEndTime < actualRaceFinishTime)
    {
      raceEndTime = actualRaceFinishTime;
    }

    var actualRaceDuration = actualRaceFinishTime - raceStartTime!.Value;

    // Count DNF riders (exclude ignored riders)
    var dnfRiders = riders.Values.Where(r => r.IsDNF && !ignoredTags.Contains(r.TagID)).ToList();
    var finishedRiders = riders.Values.Where(r => !r.IsDNF && !ignoredTags.Contains(r.TagID)).Count();

    if (IsTimedSession)
    {
      // Deliberately not a DNF summary. Everyone who was not still circulating
      // when the grace closed is marked IsDNF, which here is nearly the whole
      // field and means only "had already pulled in" - reporting seven of eight
      // riders as Did Not Finish describes a disaster that did not happen.
      var wentOut = riders.Values.Where(r => !ignoredTags.Contains(r.TagID)).ToList();
      var withATime = wentOut.Count(r => r.BestLap != null);

      AddMessage("🏁 SESSION OVER. Every rider has finished the lap they were on.");
      RaiseNotice(NoticeLevel.Critical, IsQualifying
        ? "Session over - the gate pick order is final"
        : "Session over");
      AddMessage($"🏁 Session length: {actualRaceDuration:mm\\:ss}");
      AddMessage($"⏱️ {withATime} of {wentOut.Count} riders who went out set a time.");

      if (IsQualifying)
        AddMessage("🏁 The gate pick order is final. Print it from Race > Gate pick order...");
    }
    else
    {
      AddMessage($"🏁 RACE COMPLETELY FINISHED! All riders have completed their final laps or timed out.");
      RaiseNotice(NoticeLevel.Critical, "Race finished - results are final");
      AddMessage($"🏁 Final race duration: {actualRaceDuration:mm\\:ss}");

      if (dnfRiders.Any())
      {
        AddMessage($"🚫 DNF Summary: {dnfRiders.Count} rider(s) marked as Did Not Finish:");
        foreach (var dnfRider in dnfRiders)
        {
          var raceLeaderFinishTime = dnfRider.DNFTime?.AddMinutes(-dnfTimeoutMinutes) ?? DateTime.Now;
          var timeAtDNF = dnfRider.DNFTime.HasValue ?
            (dnfRider.DNFTime.Value - raceLeaderFinishTime).TotalMinutes : 0;
          AddMessage($"   • {dnfRider.Label}: {dnfRider.TotalLaps} laps completed, DNF after {timeAtDNF:F1} min timeout");
        }
        AddMessage($"✅ {finishedRiders} rider(s) completed the race successfully.");
      }
      else
      {
        AddMessage($"✅ All {finishedRiders} riders completed the race successfully - no DNF!");
      }

      AddMessage($"🏁 Race results are now final. Additional tag reads will be ignored.");
    }

    // Update race status
    UpdateRaceStartControls();

    // Force final update of displays
    _refresh.Invalidate(RaceViewKind.Standings);
  }

  private void InitializeLogging()
  {
    try
    {
      var logsDir = AppPaths.LogsFolder;

      // Create log file with timestamp
      var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
      logFilePath = Path.Combine(logsDir, $"CrossMgrInterface_{timestamp}.log");

      // One long-lived handle drained by a background writer, rather than an
      // open/write/flush/close on the UI thread for every message.
      logWriter = new StreamWriter(logFilePath, append: true, Encoding.UTF8) { AutoFlush = false };
      logWriterTask = Task.Run(RunLogWriter);

      // Write initial log header
      var header = $"=== CrossMgr Interface Log Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===";
      WriteToLogFile("SYSTEM", header);

      AddDiagnostic($"Logging to {logFilePath}");
    }
    catch (Exception ex)
    {
      // Don't crash if logging fails, just show a message
      AddDiagnostic($"Logging could not be started: {ex.Message}");
    }
  }

  /// <summary>
  /// Queues a line for the background log writer. Never blocks and never throws:
  /// if the queue is full the line is dropped rather than stalling a tag read.
  /// </summary>
  private void WriteToLogFile(string category, string message)
  {
    if (logWriter == null)
      return;

    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
    try
    {
      logQueue.TryAdd($"[{timestamp}] [{category}] {message}");
    }
    catch (InvalidOperationException)
    {
      // Queue already completed during shutdown.
    }
  }

  /// <summary>
  /// Drains the log queue on a background thread, batching flushes so a burst of
  /// tag reads costs one disk write rather than one per line.
  /// </summary>
  private void RunLogWriter()
  {
    var sinceFlush = 0;
    try
    {
      while (!logQueue.IsCompleted)
      {
        // Time-bounded take rather than a blocking enumerate, so a quiet log
        // still gets flushed. Buffering purely by line count meant a crash
        // shortly after startup left an empty log file - exactly when the log
        // is most needed.
        if (logQueue.TryTake(out var line, millisecondsTimeout: 500))
        {
          try
          {
            logWriter!.WriteLine(line);
            sinceFlush++;
          }
          catch (Exception)
          {
            // A failing log must never take the race down.
          }
        }

        if (sinceFlush == 0) continue;

        try { logWriter!.Flush(); sinceFlush = 0; }
        catch (Exception) { }
      }
    }
    catch (Exception)
    {
      // Queue completed or disposed during shutdown.
    }
    finally
    {
      try { logWriter?.Flush(); } catch (Exception) { }
    }
  }

  private void ShutdownLogging()
  {
    try
    {
      logQueue.CompleteAdding();
      logWriterTask?.Wait(TimeSpan.FromSeconds(2));
    }
    catch (Exception)
    {
      // Nothing useful to do while closing.
    }
    finally
    {
      try { logWriter?.Flush(); logWriter?.Dispose(); } catch (Exception) { }
      logWriter = null;
    }
  }


  /// <summary>
  /// Applies the short-lap rejection settings. Ten seconds is right for some
  /// tracks and badly wrong for others - a tight supercross lap can be under it,
  /// and a marshal carrying a spare transponder past the loop trips it.
  /// </summary>
  private void buttonSetShortLapSettings_Click(object? sender, EventArgs e)
  {
    RememberRaceSetup();
    minimumLapTime = TimeSpan.FromSeconds((double)numericUpDownMinimumLapTime.Value);
    shortLapDetectionEnabled = checkBoxShortLapDetection.Checked;

    AddMessage(shortLapDetectionEnabled
      ? $"⚙️ Laps faster than {minimumLapTime.TotalSeconds:F0}s will be treated as double reads and ignored"
      : "⚙️ Short-lap rejection is off - every read counts as a lap");
  }

  /// <summary>
  /// Applies the DNF timeout. Previously hard-coded, with no way for an operator
  /// to shorten it to close out a race that is waiting on a rider who has retired.
  /// </summary>
  private void buttonSetDnfTimeout_Click(object? sender, EventArgs e)
  {
    dnfTimeoutMinutes = (int)numericUpDownDnfTimeout.Value;
    RememberRaceSetup();
    AddMessage($"⚙️ Riders have {dnfTimeoutMinutes} minute(s) after the leader finishes before they are scored DNF");
  }

  /// <summary>
  /// Event handler for the Set Additional Laps button
  /// </summary>
  private void buttonSetAdditionalLaps_Click(object sender, EventArgs e)
  {
    additionalLapsAfterTimeExpiry = (int)numericUpDownAdditionalLaps.Value;

    if (additionalLapsAfterTimeExpiry == 0)
    {
      AddMessage($"⚙️ Additional laps after time expiry set to: 0 (race finishes when all riders complete their current lap)");
    }
    else
    {
      AddMessage($"⚙️ Additional laps after time expiry set to: {additionalLapsAfterTimeExpiry}");
    }

    // If race has already finished in time mode, update the target
    if (raceTimeExpired && targetLapsToFinishRace > 0)
    {        // Recalculate target laps based on new setting (exclude DNF riders from leader calculation)
      var currentLeader = riders.Values
        .Where(r => !r.IsDNF && !ignoredTags.Contains(r.TagID))
        .OrderByDescending(r => r.TotalLaps)
        .ThenBy(r => r.TotalTime)
        .FirstOrDefault();

      if (currentLeader != null && leaderLapsAtTimeExpiry > 0)
      {
        // Calculate target: leader's current lap (in progress when time expired) + additional laps
        var leaderCurrentLapWhenTimeExpired = leaderLapsAtTimeExpiry + 1;
        targetLapsToFinishRace = leaderCurrentLapWhenTimeExpired + additionalLapsAfterTimeExpiry;

        if (additionalLapsAfterTimeExpiry == 0)
        {
          AddMessage($"🏁 Updated race finish target to {targetLapsToFinishRace} laps (leader was on lap {leaderCurrentLapWhenTimeExpired} when time expired + 0 additional laps)");
        }
        else
        {
          var lapsText = additionalLapsAfterTimeExpiry == 1 ? "lap" : "laps";
          AddMessage($"🏁 Updated race finish target to {targetLapsToFinishRace} laps (leader was on lap {leaderCurrentLapWhenTimeExpired} when time expired + {additionalLapsAfterTimeExpiry} additional {lapsText})");
        }
      }
    }
  
    RememberRaceSetup();
  }

  /// <summary>
  /// Check for position changes and lapping events after a rider crossing
  /// </summary>
  private void CheckForPositionChangesAndLapping(string crossingRiderTagID)
  {
    // Don't check for position changes if race hasn't started or is finished
    if (!raceStarted || raceFinished)
      return;

    // Track position means nothing in a timed session - riders leave the gate
    // when they please and are scored on their best lap, so "PASSED" and
    // "LAPPED" would be pure noise in the event feed.
    if (IsTimedSession)
      return;

    // The snapshot has to be taken *inside* positionCheckLock, not before it.
    // Two riders crossing together spawn two of these tasks; if each snapshots
    // first and then queues for the lock, the second compares its older snapshot
    // against the baseline the first has just stored - and announces the mirror
    // image of the pass that was reported a millisecond earlier.
    //
    // Lock order is always positionCheckLock then ridersLock, never the reverse.
    lock (positionCheckLock)
    {
      List<RiderInfo> currentStandings;
      lock (ridersLock)
      {
        currentStandings = PositionCalculator.GetSortedRidersFromSnapshot(
          riders.Values.Select(CloneRiderForDisplay).ToList());
      }

      if (currentStandings.Count < 2)
        return; // Need at least 2 riders for position changes

      // Check for passing and lapping events (only if we have previous data)
      if (lastKnownPositions.Count > 0 && lastKnownLapCounts.Count > 0)
      {
        CheckForPassingAndLappingEvents(currentStandings, crossingRiderTagID);
      }

      // Check for position changes (only if enough time has passed to avoid spam)
      if (lastKnownPositions.Count > 0 &&
          (DateTime.Now - lastPositionCheck).TotalSeconds >= 5)
      {
        var crossingRiderPosition = currentStandings.FindIndex(r => r.TagID == crossingRiderTagID) + 1;
        CheckForPositionChanges(currentStandings, crossingRiderTagID, crossingRiderPosition);
      }

      // Store current standings for future comparisons
      StoreCurrentStandings(currentStandings);
    }
  }

  /// <summary>
  /// Check for passing and lapping events using lap difference analysis
  /// </summary>
  /// <summary>
  /// At most three riders are named before the announcement becomes a count.
  /// A leader lapping forty backmarkers is forty true statements and one
  /// unreadable event log.
  /// </summary>
  private const int MaxNamedLappedRiders = 3;

  /// <summary>
  /// Only lapping by a rider this high up is announced.
  ///
  /// The remaining events after the lap-count bug was fixed were all true, but
  /// most were not worth reading: in a 250-rider field the natural spread means
  /// the midfield laps the tail constantly, and P150 lapping P200 changes
  /// nothing anybody acts on. Lapping matters for race management when it is the
  /// front of the race catching backmarkers - blue flags, and who is on the lead
  /// lap at the finish.
  /// </summary>
  private const int LappingAnnouncementPositions = 10;

  private void CheckForPassingAndLappingEvents(List<RiderInfo> currentStandings, string crossingRiderTagID)
  {
    // Built once. CheckPassingEvent used to FindIndex twice for every comparison,
    // so a 250-rider field cost about sixty thousand list scans per crossing.
    var order = new Dictionary<string, int>(currentStandings.Count);
    for (var i = 0; i < currentStandings.Count; i++) order[currentStandings[i].TagID] = i;

    if (!order.TryGetValue(crossingRiderTagID, out var crossingIndex)) return;

    // Everything below works from the standings snapshot. The previous version
    // indexed the live riders dictionary here, outside ridersLock, while the
    // network thread was writing to it.
    var crossingRider = currentStandings[crossingIndex];

    var now = DateTime.Now;
    var medianPace = RaceProgress.MedianPace(currentStandings);
    var crossingProgress = RaceProgress.Of(crossingRider, now, medianPace);
    var crossingPosition = crossingIndex + 1;

    var lapped = new List<RiderInfo>();
    var lappedBy = new List<RiderInfo>();

    foreach (var otherRider in currentStandings)
    {
      if (otherRider.TagID == crossingRiderTagID) continue;

      if (otherRider.TotalLaps == crossingRider.TotalLaps)
      {
        CheckPassingEvent(crossingRiderTagID, otherRider.TagID, currentStandings, order);
        continue;
      }

      var otherProgress = RaceProgress.Of(otherRider, now, medianPace);
      var otherPosition = order[otherRider.TagID] + 1;

      // Both directions, and the second one is the one that actually fires.
      //
      // A rider who has just crossed sits at exactly a whole lap of progress, so
      // their lead over a backmarker computes as one lap MINUS however far round
      // that backmarker is - always just under a lap, never over it. Checking
      // only "did the crossing rider lap anybody" would therefore almost never
      // report anything. The lapping becomes visible at the BACKMARKER's next
      // crossing, when it is their progress that is the whole number.
      // The state is updated either way, so a lapping that goes unannounced still
      // counts against the next one - the filter suppresses the message, not the
      // bookkeeping.
      if (HasJustLapped(crossingRider, crossingProgress, otherRider, otherProgress))
      {
        if (crossingPosition <= LappingAnnouncementPositions) lapped.Add(otherRider);
      }
      else if (HasJustLapped(otherRider, otherProgress, crossingRider, crossingProgress))
      {
        if (otherPosition <= LappingAnnouncementPositions) lappedBy.Add(otherRider);
      }
    }

    AnnounceLapping(crossingRider, lapped, lappedBy);
  }

  /// <summary>
  /// True the first time the crossing rider reaches a new whole-lap lead over
  /// this rider.
  ///
  /// Measured in track progress rather than lap count, which is the entire fix:
  /// a rider who has just crossed has one more lap recorded than everybody still
  /// approaching the line, and reporting that as lapping produced roughly nine
  /// false events per crossing.
  /// </summary>
  private bool HasJustLapped(RiderInfo leader, double leaderProgress, RiderInfo other, double otherProgress)
  {
    var lead = RaceProgress.WholeLapLead(leaderProgress, otherProgress);
    var previous = GetPreviousLapDifference(leader.TagID, other.TagID, 0);

    // A HIGH-WATER MARK, never allowed to fall.
    //
    // Progress depends on a pace estimate that shifts every lap, so a pair sitting
    // near the one-lap boundary drifts back and forth across it - and storing the
    // current value would re-announce the same lapping every time it wobbled back.
    // Having been lapped does not become untrue, so the recorded lead only ever
    // rises, and the next announcement needs a genuine second lap.
    //
    // Stored per ordered pair, so "A leads B" and "B leads A" keep separate
    // history and neither direction can suppress the other.
    StoreLapDifference(leader.TagID, other.TagID, Math.Max(lead, previous));

    return lead >= 1 && lead > previous;
  }

  private void AnnounceLapping(RiderInfo crossingRider, List<RiderInfo> lapped, List<RiderInfo> lappedBy)
  {
    var who = GetRiderDisplayText(crossingRider);

    if (lapped.Count > 0)
      AddRaceEvent($"🔄 {who} has LAPPED {Describe(lapped)}");

    if (lappedBy.Count > 0)
      AddRaceEvent($"🔄 {who} has been LAPPED by {Describe(lappedBy)}");
  }

  /// <summary>Names a few riders, or counts them once naming stops being readable.</summary>
  private string Describe(List<RiderInfo> riders) =>
    riders.Count <= MaxNamedLappedRiders
      ? string.Join(", ", riders.Select(GetRiderDisplayText))
      : $"{riders.Count} riders";

  /// <summary>
  /// Check for a lapping event between two specific riders
  /// </summary>

  /// <summary>
  /// Check for a passing event between two specific riders (same lap only)
  /// </summary>
  private void CheckPassingEvent(
    string crossingRiderTagID, string otherRiderTagID,
    List<RiderInfo> currentStandings, Dictionary<string, int> order)
  {
    if (!order.TryGetValue(crossingRiderTagID, out var crossingIndex)) return;
    if (!order.TryGetValue(otherRiderTagID, out var otherIndex)) return;

    // Only riders on the same lap can pass one another.
    //
    // Standings are ordered by lap count first, so a rider who has just crossed
    // sits ahead of everyone still on the previous lap - including riders only a
    // few seconds behind on the track. Without this check that registered as a
    // pass, and the reverse was reported as soon as the other rider crossed:
    // two announcements a second apart claiming opposite things, when in truth
    // nobody had overtaken anybody. The doc comment on this method always
    // claimed "same lap only"; the code never actually enforced it.
    if (currentStandings[crossingIndex].TotalLaps != currentStandings[otherIndex].TotalLaps)
      return;

    // Get previous positions (if we have history)
    if (!lastKnownPositions.ContainsKey(crossingRiderTagID) || !lastKnownPositions.ContainsKey(otherRiderTagID))
      return; // No previous position data to compare

    int currentPosCrossing = crossingIndex + 1;
    int currentPosOther = otherIndex + 1;
    int previousPosCrossing = lastKnownPositions[crossingRiderTagID];
    int previousPosOther = lastKnownPositions[otherRiderTagID];

    // Check if crossing rider passed the other rider (was behind but now ahead)
    if (previousPosCrossing > previousPosOther && currentPosCrossing < currentPosOther)
    {
      AddRaceEvent($"⚡ {GetRiderDisplayText(crossingRiderTagID)} PASSES {GetRiderDisplayText(otherRiderTagID)} for position {currentPosCrossing}!");
    }
  }

  /// <summary>
  /// Previous lap gap between two riders, used to detect the moment one laps the
  /// other. Purely per-race scratch state - nothing reads it back after a
  /// restart - so it lives in memory rather than in LiteDB, where looking it up
  /// meant an unindexed collection scan per rider pair per crossing.
  /// </summary>
  private int GetPreviousLapDifference(string riderA, string riderB, int defaultValue)
  {
    lock (lapDifferencesLock)
    {
      return lapDifferences.TryGetValue((riderA, riderB), out var diff) ? diff : defaultValue;
    }
  }

  private void StoreLapDifference(string riderA, string riderB, int lapDifference)
  {
    lock (lapDifferencesLock)
    {
      lapDifferences[(riderA, riderB)] = lapDifference;
    }
  }

  /// <summary>
  /// Check for position changes since last check
  /// </summary>
  private void CheckForPositionChanges(List<RiderInfo> currentStandings, string crossingRiderTagID, int currentPosition)
  {
    // Check if the crossing rider's position changed significantly
    if (lastKnownPositions.ContainsKey(crossingRiderTagID))
    {
      int previousPosition = lastKnownPositions[crossingRiderTagID];
      int positionChange = previousPosition - currentPosition; // Positive = moved up, Negative = moved down

      if (Math.Abs(positionChange) >= 1) // Position changed by at least 1 place
      {
        if (positionChange > 0)
        {
          // Moved up in positions
          if (currentPosition == 1)
          {
            AddRaceEvent($"🥇 NEW LEADER! {GetRiderDisplayText(crossingRiderTagID)} takes the lead! (was P{previousPosition})");
          }
          else if (currentPosition <= 3 && previousPosition > 3)
          {
            AddRaceEvent($"🏆 {GetRiderDisplayText(crossingRiderTagID)} moves into podium position {currentPosition}! (was P{previousPosition})");
          }
          else if (positionChange >= 3)
          {
            AddRaceEvent($"⬆️ {GetRiderDisplayText(crossingRiderTagID)} surges up {positionChange} positions to P{currentPosition}! (was P{previousPosition})");
          }
          else
          {
            AddRaceEvent($"⬆️ {GetRiderDisplayText(crossingRiderTagID)} moves up to P{currentPosition} (was P{previousPosition})");
          }
        }
        else
        {
          // Moved down in positions
          if (previousPosition == 1)
          {
            var newLeader = currentStandings.FirstOrDefault();
            AddRaceEvent($"🔄 LEADER CHANGE! {(newLeader != null ? newLeader.Label : "the leader")} takes over from {GetRiderDisplayText(crossingRiderTagID)} who drops to P{currentPosition}");
          }
          else if (Math.Abs(positionChange) >= 3)
          {
            AddRaceEvent($"⬇️ {GetRiderDisplayText(crossingRiderTagID)} drops {Math.Abs(positionChange)} positions to P{currentPosition} (was P{previousPosition})");
          }
        }
      }
    }

    // Check for other significant position battles in top 5
    CheckForTopPositionBattles(currentStandings);
  }

  /// <summary>
  /// Check for position battles in the top positions
  /// </summary>
  private void CheckForTopPositionBattles(List<RiderInfo> currentStandings)
  {
    // Look for close battles in top 5 positions
    for (int i = 0; i < Math.Min(5, currentStandings.Count - 1); i++)
    {
      var rider1 = currentStandings[i];
      var rider2 = currentStandings[i + 1];

      // Check if riders are on the same lap
      if (rider1.TotalLaps == rider2.TotalLaps)
      {
        var timeDifference = rider2.TotalTime - rider1.TotalTime;

        // If the gap is very close (less than 5 seconds), announce close battle
        if (timeDifference.TotalSeconds < 5 && timeDifference.TotalSeconds > 0)
        {
          // Only announce occasionally to avoid spam
          if (ShouldAnnounceBattle(rider1.TagID, rider2.TagID))
          {
            AddRaceEvent($"🔥 CLOSE BATTLE! P{i + 1} {rider1.Label} leads P{i + 2} {rider2.Label} by only {timeDifference.TotalSeconds:F1} seconds!");
          }
        }
      }
    }
  }

  /// <summary>
  /// Check if a battle between two riders should be announced (to avoid spam)
  /// </summary>
  private bool ShouldAnnounceBattle(string rider1, string rider2)
  {
    // Rate-limit per pair of riders. The previous test was
    // `DateTime.Now.Second % 30 == 0`, which is true for 2 seconds in every 60
    // no matter what the riders are doing - so it dropped almost every battle
    // and occasionally let an unrelated one through.
    var pair = string.CompareOrdinal(rider1, rider2) <= 0
      ? (rider1, rider2)
      : (rider2, rider1);

    if (lastBattleAnnounced.TryGetValue(pair, out var last) &&
        (DateTime.Now - last) < BattleAnnouncementCooldown)
    {
      return false;
    }

    lastBattleAnnounced[pair] = DateTime.Now;
    return true;
  }

  /// <summary>
  /// Store current standings to track position changes over time
  /// </summary>
  private void StoreCurrentStandings(List<RiderInfo> currentStandings)
  {
    // Update position tracking
    lastKnownPositions.Clear();
    lastKnownLapCounts.Clear();

    for (int i = 0; i < currentStandings.Count; i++)
    {
      var rider = currentStandings[i];
      lastKnownPositions[rider.TagID] = i + 1; // 1-based position
      lastKnownLapCounts[rider.TagID] = rider.TotalLaps;
    }
    lastPositionCheck = DateTime.Now;
  }





  private int CalculateCurrentPosition(string riderId)
  {
    // This method should only be called when ridersLock is already held
    // Filter out ignored riders for position calculations
    var activeRiders = riders.Where(kvp => !ignoredTags.Contains(kvp.Key)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    return PositionCalculator.CalculateCurrentPosition(riderId, activeRiders);
  }

  #region Crash Recovery

  /// <summary>
  /// Attempts to recover from a previous crash by restoring race state
  /// </summary>
  private void AttemptCrashRecovery()
  {
    try
    {
      var latestRace = _raceDb.GetLatestUnfinishedRace();
      if (latestRace == null) return;

      // Check if the race was recently active (within last 24 hours)
      var timeSinceLastSave = DateTime.Now - (latestRace.LastSavedAt ?? latestRace.StartTime);
      if (timeSinceLastSave.TotalHours > 24)
      {
        // Too old, don't auto-recover
        return;
      }

      var result = MessageBox.Show(
        $"Found an unfinished race from {latestRace.StartTime:yyyy-MM-dd HH:mm:ss}.\n\n" +
        $"Would you like to restore this race?\n\n" +
        $"Race Duration: {latestRace.Duration:mm\\:ss}\n" +
        $"Last Saved: {latestRace.LastSavedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown"}",
        "Crash Recovery",
        MessageBoxButtons.YesNo,
        MessageBoxIcon.Question);

      if (result == DialogResult.Yes)
      {
        RestoreRaceState(latestRace);
      }
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(this,
        "The previous race could not be restored.",
        "You can still start a new race. Nothing has been deleted.", ex);
    }
  }

  /// <summary>
  /// Restores complete race state from database
  /// </summary>
  private void RestoreRaceState(DbRace raceToRestore)
  {
    try
    {
      // Set current race in database
      _raceDb.SetCurrentRace(raceToRestore.Id);
      currentRaceId = raceToRestore.Id;

      // Restore race variables
      raceName = raceToRestore.Name;
      Text = string.IsNullOrEmpty(raceName)
        ? "CrossMgr RFID Interface"
        : $"CrossMgr - {raceName}";
      raceStartTime = raceToRestore.StartTime;
      raceEndTime = raceToRestore.EndTime;
      raceDuration = raceToRestore.Duration;
      // Races recorded before session types existed read back as Race, which
      // is what they were.
      sessionType = raceToRestore.SessionType;
      raceFinished = raceToRestore.IsFinished;
      raceTimeExpired = raceToRestore.IsTimeExpired;
      waitingForLeaderFinish = raceToRestore.WaitingForLeaderFinish;
      waitingForFinalLaps = raceToRestore.WaitingForFinalLaps;
      finalLapsStartTime = raceToRestore.FinalLapsStartTime;
      leaderAtTimeExpiry = raceToRestore.LeaderAtTimeExpiry;
      leaderLapsAtTimeExpiry = raceToRestore.LeaderLapsAtTimeExpiry;
      targetLapsToFinishRace = raceToRestore.TargetLapsToFinishRace;
      fiveMinuteWarningShown = raceToRestore.FiveMinuteWarningShown;

      // Mark race as started if it was in progress
      if (raceStartTime.HasValue && !raceFinished)
      {
        raceStarted = true;
        manualStartMode = true; // Assume manual start mode for recovered races
      }

      // Restore rider data
      lock (ridersLock)
      {
        riders.Clear();
        var restoredRiders = _raceDb.RestoreRiderData(raceToRestore.Id);

        // Debug: Log restoration details
        var totalLapsRestored = 0;
        foreach (var kvp in restoredRiders)
        {
          riders[kvp.Key] = kvp.Value;
          totalLapsRestored += kvp.Value.Laps.Count;
        }

        // Add a race event to show what was restored
        AddRaceEvent($"Restored {restoredRiders.Count} riders with {totalLapsRestored} total laps");
      }

      // Restore position tracking
      lastKnownPositions = _raceDb.GetLastKnownPositions();
      lastKnownLapCounts = _raceDb.GetLastKnownLapCounts();

      // Before the repaint: this rebuilds the tabs, which is what brings the
      // Qualifying tab back for a recovered qualifying session. Nothing else on
      // this path calls RebuildTabs.
      ApplySessionTypeToUi();

      _refresh.Invalidate(RaceViewKind.All);

      // Auto-start TCP server if race was in progress
      if (raceStarted && !raceFinished)
      {
        StartTcpListener(readerPort);
      }

      // Start periodic state saving
      StartPeriodicStateSaving();

      // Create snapshot for UI update (exclude ignored riders)
      var raceFinishedSnapshot = raceFinished;
      var riderCount = riders.Values.Where(r => !ignoredTags.Contains(r.TagID)).Count();
      var totalLaps = riders.Values.Where(r => !ignoredTags.Contains(r.TagID)).Sum(r => r.TotalLaps);

      // Update UI to reflect restored state
      BeginInvoke(new Action(() =>
      {
        UpdateUI();

        // Repaint everything. Views that are not on screen keep their dirty bit
        // and repaint the moment their tab is selected.
        _refresh.RenderNow(RaceViewKind.All);

        // The recovery result used to be written into two labels on the Race
        // Statistics tab, which a volunteer in the simple view never sees.
        RaiseNotice(NoticeLevel.Info,
          $"Restored the previous race: {riderCount} riders, {totalLaps} laps");
      }));

      // Add race event outside the UI thread
      AddRaceEvent($"Race state recovered: {riderCount} riders, {totalLaps} total laps");

    }
    catch (Exception ex)
    {
      ErrorDialog.Show(this,
        "The previous race could not be restored.",
        "You can still start a new race. Nothing has been deleted.", ex);
    }
  }

  /// <summary>
  /// Starts periodic saving of race state to prevent data loss
  /// </summary>
  private void StartPeriodicStateSaving()
  {
    var saveTimer = new System.Windows.Forms.Timer();
    saveTimer.Interval = 30000; // Save every 30 seconds
    saveTimer.Tick += (sender, e) =>
    {
      if (raceStarted && !raceFinished && currentRaceId.HasValue)
      {
        Task.Run(() => SaveCurrentRaceState());
      }
    };
    saveTimer.Start();
  }

  /// <summary>
  /// Saves current race state to database for crash recovery
  /// </summary>
  private void SaveCurrentRaceState()
  {
    try
    {
      Dictionary<string, RiderInfo> riderSnapshot;
      lock (ridersLock)
      {
        riderSnapshot = riders
          .Where(kvp => !ignoredTags.Contains(kvp.Key))
          .ToDictionary(kvp => kvp.Key, kvp => CloneRiderForDisplay(kvp.Value));
      }

      _raceDb.SaveRaceState(
        riderSnapshot,
        raceStartTime,
        raceEndTime,
        raceDuration,
        raceFinished,
        raceTimeExpired,
        waitingForLeaderFinish,
        waitingForFinalLaps,
        finalLapsStartTime,
        leaderAtTimeExpiry,
        leaderLapsAtTimeExpiry,
        targetLapsToFinishRace,
        fiveMinuteWarningShown
      );
    }
    catch (Exception ex)
    {
      // Log error but don't show to user (background operation)
      Console.WriteLine($"Error saving race state: {ex.Message}");
    }
  }

  #endregion
}
