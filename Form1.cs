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
  private readonly RaceEventManager _raceEventManager;
  private readonly LapChartRenderer _lapChartRenderer;
  private readonly LapProgressionManager _lapProgressionManager;
  private readonly RaceStateManager _raceStateManager;
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
  private bool ridersDisplayNeedsUpdate = false;
  private bool manualStartMode = false;
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

  // Lap time validation
  private readonly TimeSpan minimumLapTime = TimeSpan.FromSeconds(10); // Ignore laps shorter than 10 seconds

  // Tag filtering
  private string tagFilterPrefix = "";
  private bool tagFilterEnabled = false;
  private int filteredTagCount = 0;

  // Tag ignore list for excluding specific tags from processing
  private readonly HashSet<string> ignoredTags = new();
  private int ignoredTagCount = 0;

  // Logging
  private string logFilePath = "";
  private readonly object logLock = new object();

  // Position tracking for race events (now backed by database)
  private Dictionary<string, int> lastKnownPositions = new();
  private Dictionary<string, int> lastKnownLapCounts = new();
  private DateTime lastPositionCheck = DateTime.MinValue;

  // Lap chart visualization fields removed - now handled by LapChartRenderer
  private bool lapChartNeedsUpdate = false;
  private DateTime lastProgressLineUpdate = DateTime.MinValue;

  // Lap progression tracking
  private readonly List<LapProgressionEntry> lapProgressionHistory = new();
  private bool lapProgressionNeedsUpdate = false;

  public Form1()
  {
    InitializeComponent();

    // Initialize database service
    _raceDb = new RaceDataService("races.db");

    // Initialize extracted manager classes
    _raceStateManager = new RaceStateManager();
    _raceEventManager = new RaceEventManager(_raceDb, AddRaceEvent);
    _lapChartRenderer = new LapChartRenderer();
    _lapProgressionManager = new LapProgressionManager();
    _raceReportGenerator = new RaceReportGenerator();
    _riderDataImporter = new RiderDataImporter();

    this.Load += Form1_Load;
    InitializeRidersDataGrid();

    // Add event handler for tab changes
    tabControl.SelectedIndexChanged += TabControl_SelectedIndexChanged;
  }

  private void TabControl_SelectedIndexChanged(object? sender, EventArgs e)
  {
    // If switching to Riders tab, always update if we have rider data (to fix empty table issue)
    if (tabControl.SelectedIndex == 2)
    {
      bool shouldUpdate = false;
      lock (ridersLock)
      {
        shouldUpdate = riders.Count > 0 && (ridersDisplayNeedsUpdate || dataGridViewRiders.Rows.Count == 0);
        if (shouldUpdate)
          ridersDisplayNeedsUpdate = false;
      }

      if (shouldUpdate)
      {
        UpdateRidersDisplay();
      }
    }
    // If switching to Lap Chart tab and we need an update, do it now
    else if (tabControl.SelectedIndex == 4 && lapChartNeedsUpdate)
    {
      lapChartNeedsUpdate = false;
      panelLapChart.Invalidate(); // Trigger repaint
    }
    // If switching to Lap Progression tab, always update if we have data or if update is needed
    else if (tabControl.SelectedIndex == 5)
    {
      bool shouldUpdate = false;
      List<RiderInfo> riderSnapshot = new();
      bool raceFinishedSnapshot = false;
      bool waitingForFinalLapsSnapshot = false;

      lock (ridersLock)
      {
        shouldUpdate = riders.Count > 0; // Always update when switching to lap progression tab
        if (shouldUpdate)
        {
          riderSnapshot = riders.Values.Where(r => !ignoredTags.Contains(r.TagID)).ToList();
          raceFinishedSnapshot = raceFinished;
          waitingForFinalLapsSnapshot = waitingForFinalLaps;
        }
        lapProgressionNeedsUpdate = false; // Clear the flag since we're updating now
      }

      if (shouldUpdate)
      {
        _lapProgressionManager.UpdateLapProgressionDisplay(riderSnapshot, raceFinishedSnapshot, waitingForFinalLapsSnapshot, this);
      }
    }
  }

  private void Form1_Load(object? sender, EventArgs e)
  {
    AddMessage("Application started. Ready to listen for RFID messages.");
    UpdateConnectionCount();

    // Initialize race duration from the numeric control
    raceDuration = TimeSpan.FromMinutes((double)numericUpDownRaceDuration.Value);

    // Initialize additional laps setting
    additionalLapsAfterTimeExpiry = (int)numericUpDownAdditionalLaps.Value;

    // Initialize tag filter controls
    textBoxTagFilter.PlaceholderText = "e.g., RIDER, 1000, BIKE (comma-separated)";
    checkBoxFilterEnabled.Checked = false;
    tagFilterEnabled = false;
    AddMessage("🔍 Tag filter: Disabled (all tags will be processed)");
    AddMessage($"⚙️ DNF timeout: {dnfTimeoutMinutes} minutes after leader finishes");

    // Add Lap Progression tab programmatically using LapProgressionManager
    var lapProgressionTab = _lapProgressionManager.CreateLapProgressionTab();
    tabControl.TabPages.Insert(5, lapProgressionTab);

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
    radioButtonStartOnFirstTag.CheckedChanged += RaceStartMode_CheckedChanged;
    radioButtonStartManual.CheckedChanged += RaceStartMode_CheckedChanged;
    UpdateRaceStartControls();

    // Initialize logging
    InitializeLogging();

    // Perform crash recovery after the form is fully loaded and window handle is created
    AttemptCrashRecovery();
  }

  private void buttonStart_Click(object? sender, EventArgs e)
  {
    if (!int.TryParse(textBoxPort.Text, out int port) || port < 1 || port > 65535)
    {
      MessageBox.Show("Please enter a valid port number (1-65535).", "Invalid Port",
          MessageBoxButtons.OK, MessageBoxIcon.Warning);
      return;
    }

    StartTcpListener(port);
  }

  private void buttonStop_Click(object? sender, EventArgs e)
  {
    StopTcpListener();
  }

  private void buttonClear_Click(object? sender, EventArgs e)
  {
    listBoxMessages.Items.Clear();
  }

  private void buttonClearTagEvents_Click(object? sender, EventArgs e)
  {
    listBoxTagEvents.Items.Clear();
  }

  private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
  {
    StopTcpListener();

    // Write final log entry
    WriteToLogFile("SYSTEM", $"=== CrossMgr Interface Log Ended at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
  }

  private void StartTcpListener(int port)
  {
    try
    {
      tcpListener = new TcpListener(IPAddress.Any, port);
      tcpListener.Start();
      isListening = true;

      UpdateUI();
      AddMessage($"TCP server started on port {port}. Waiting for connections...");

      // Start accepting connections
      _ = Task.Run(AcceptConnectionsAsync);
    }
    catch (Exception ex)
    {
      MessageBox.Show($"Failed to start TCP server: {ex.Message}", "Error",
          MessageBoxButtons.OK, MessageBoxIcon.Error);
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

      AddMessage("TCP server stopped.");
    }
    catch (Exception ex)
    {
      AddMessage($"Error stopping server: {ex.Message}");
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

        // Debug: Log raw received data (only if not too verbose)
        if (allData.Length > 0 && allData.Length < 200)
        {
          AddTagEvent($"[{clientEndpoint}] RAW: '{allData}' (hex: {string.Join("", Encoding.ASCII.GetBytes(allData).Select(b => b.ToString("X2")))})");
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
          AddMessage($"[{clientEndpoint}] ⏳ Starting 500ms delay timer...");
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
        AddMessage($"[{clientEndpoint}] Unknown message: {message}");
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
        AddMessage($"[{clientEndpoint}] Invalid DA message (too short): {message}");
        return;
      }

      // Skip "DA" prefix
      string content = message.Substring(2);

      // Find the space that separates tagID from time
      int firstSpace = content.IndexOf(' ');
      if (firstSpace == -1)
      {
        AddMessage($"[{clientEndpoint}] Invalid DA message format: {message}");
        return;
      }

      string tagID = content.Substring(0, firstSpace);
      string remainder = content.Substring(firstSpace + 1);

      // Parse time (should be next)
      int nextSpace = remainder.IndexOf(' ');
      if (nextSpace == -1)
      {
        AddMessage($"[{clientEndpoint}] Invalid DA message format: {message}");
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
          AddTagEvent($"[{clientEndpoint}] {filteredMessage}");
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

      AddTagEvent($"[{clientEndpoint}] {formattedMessage}");

      // Display rider summary after each crossing - simplified since we have the GUI
      // DisplayRiderSummary(tagID); // Commented out to reduce log noise
    }
    catch (Exception ex)
    {
      AddMessage($"[{clientEndpoint}] Error parsing DA message '{message}': {ex.Message}");
    }
  }

  private string FormatTagID(string tagID)
  {
    // Return tag ID as-is without formatting
    return tagID;
  }

  private RiderLap ProcessRiderCrossing(string tagID, DateTime crossingTime)
  {
    // Collect messages to send after lock is released
    var messagesToAdd = new List<(string message, bool isRaceEvent)>();
    RiderLap resultLap;

    lock (ridersLock)
    {
      // If race is finished, still record crossings but note they are post-race
      if (raceFinished)
      {
        messagesToAdd.Add(($"🏁 Post-race crossing: {tagID} at {crossingTime:HH:mm:ss.fff} (recorded but not counted in final results)", true));
        messagesToAdd.Add(($"Post-race crossing: {tagID}", false));
        resultLap = new RiderLap { TagID = tagID, CrossingTime = crossingTime, LapNumber = 0 };
      }
      // Check if this rider is already marked as DNF
      else if (riders.ContainsKey(tagID) && riders[tagID].IsDNF)
      {
        messagesToAdd.Add(($"🚫 Tag read ignored: {tagID} is marked as DNF (Did Not Finish) - crossing at {crossingTime:HH:mm:ss.fff}", true));
        messagesToAdd.Add(($"DNF rider crossing ignored: {tagID}", false));
        resultLap = new RiderLap { TagID = tagID, CrossingTime = crossingTime, LapNumber = 0 };
      }
      // Check if we're in final laps phase and this rider has exceeded their allowed laps
      else if (waitingForFinalLaps && riders.ContainsKey(tagID))
      {
        var existingRider = riders[tagID];
        var nextLapNumber = existingRider.TotalLaps + 1;

        if (nextLapNumber > existingRider.FinalAllowedLap)
        {
          messagesToAdd.Add(($"🚫 Tag read ignored: {tagID} has already completed their final allowed lap (lap {existingRider.FinalAllowedLap})", true));
          messagesToAdd.Add(($"Final lap exceeded: {tagID}", false));
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
      currentRaceId = _raceDb.StartNewRace(raceStartTime.Value, raceDuration);

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

        messagesToAdd.Add(($"⏰ Race time expired! Leader {leaderAtTimeExpiry} currently has {leaderLapsAtTimeExpiry} laps completed.", true));

        if (additionalLapsAfterTimeExpiry == 0)
        {
          // When no additional laps are configured, each rider must only complete their current lap
          messagesToAdd.Add(($"🏁 Race will finish when all riders complete their current lap (no additional laps).", true));

          // Immediately set final allowed laps for all riders to their current lap + 1
          // This way each rider can only complete the lap they are currently on
          foreach (var rider in riders.Values.Where(r => !r.IsDNF && !ignoredTags.Contains(r.TagID)))
          {
            rider.FinalAllowedLap = rider.TotalLaps + 1;
          }

          // Transition directly to final laps phase
          waitingForFinalLaps = true;
          finalLapsStartTime = DateTime.Now;
          raceTimeExpired = false;

          messagesToAdd.Add(($"🏁 Final laps phase started - each rider must finish their current lap only.", true));
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

      // These operations will be called later after the lock is released
      Task.Run(() => RecordLapProgressionAfterLapCompletion(tagID, 1));

      ridersDisplayNeedsUpdate = true;
      lapChartNeedsUpdate = true;

      // If user is currently on riders tab, update immediately
      if (tabControl.SelectedIndex == 2)
      {
        BeginInvoke(new Action(UpdateRidersDisplay));
      }
      return firstLap;
    }
    else
    {
      // Subsequent crossing
      var rider = riders[tagID];
      var previousCrossing = rider.LastCrossing;
      var lapTime = crossingTime - previousCrossing;

      // Check for minimum lap time - ignore unrealistically short laps (likely RFID errors)
      if (lapTime < minimumLapTime)
      {
        // Log the ignored short lap for debugging
        var logMessage = $"IGNORED SHORT LAP: {tagID} - {lapTime.TotalSeconds:F3}s (minimum: {minimumLapTime.TotalSeconds}s)";
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

      // Check for missed reads and split if necessary BEFORE saving to database
      DetectAndSplitMissedReads(tagID, messagesToAdd);

      // Save to database for crash recovery - but only if the lap wasn't split
      // If it was split, the split detection already saved the split laps
      if (currentRaceId.HasValue)
      {
        Task.Run(() =>
        {
          _raceDb.UpsertRider(rider);

          // Check if the last lap is still the same lap we created (not split)
          if (rider.Laps.LastOrDefault()?.CrossingTime == newLap.CrossingTime &&
              rider.Laps.LastOrDefault()?.LapTime == newLap.LapTime &&
              !rider.Laps.LastOrDefault()?.IsSplitLap == true)
          {
            // The lap wasn't split, so save it normally
            var position = CalculateCurrentPosition(tagID);
            _raceDb.AddLap(tagID, newLap, position);
          }
          // If the lap was split, the DetectAndSplitMissedReads method already saved the split laps
        });
      }

      // These operations will be called later after the lock is released
      Task.Run(() => RecordLapProgressionAfterLapCompletion(tagID, rider.TotalLaps));

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
              messagesToAdd.Add(($"🏁 LEADER {tagID} crossed after time expiry! Race will finish when leader completes {targetLapsToFinishRace} total laps (no additional laps).", true));
            }
            else
            {
              messagesToAdd.Add(($"🏁 NEW LEADER {tagID} crossed after time expiry (was {originalLeader})! Race will finish when new leader completes {targetLapsToFinishRace} total laps (no additional laps).", true));
            }
          }
          else
          {
            var lapsText = additionalLapsAfterTimeExpiry == 1 ? "lap" : "laps";
            if (tagID == originalLeader)
            {
              messagesToAdd.Add(($"🏁 LEADER {tagID} crossed after time expiry! Shown {additionalLapsAfterTimeExpiry} additional {lapsText} sign. Race will finish when leader completes {targetLapsToFinishRace} total laps.", true));
            }
            else
            {
              messagesToAdd.Add(($"🏁 NEW LEADER {tagID} crossed after time expiry (was {originalLeader})! Shown {additionalLapsAfterTimeExpiry} additional {lapsText} sign. Race will finish when new leader completes {targetLapsToFinishRace} total laps.", true));
            }
          }
        }
        else
        {
          var currentLeaderTag = currentLeader?.TagID ?? "Unknown";
          messagesToAdd.Add(($"⏰ {tagID} crossed after time expiry, but waiting for current LEADER {currentLeaderTag} to cross and receive additional laps sign...", true));
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
          messagesToAdd.Add(($"🏁 {tagID} completed {targetLapsToFinishRace} laps, but race will finish when LEADER {leaderAtTimeExpiry} reaches this target.", true));
        }
      }

      // Check if we're in final laps phase and all riders have completed their final laps
      if (waitingForFinalLaps)
      {
        Task.Run(() => CheckIfAllFinalLapsCompleted());
      }

      // Check for position changes and lapping events
      Task.Run(() => CheckForPositionChangesAndLapping(tagID));

      ridersDisplayNeedsUpdate = true;
      lapChartNeedsUpdate = true;

      // If user is currently on riders tab, update immediately
      if (tabControl.SelectedIndex == 2)
      {
        BeginInvoke(new Action(UpdateRidersDisplay));
      }

      return newLap;
    }
  }

  /// <summary>
  /// Detect and split laps that may represent multiple missed RFID reads
  /// If a lap is very long but close to a multiple of the rider's average lap time,
  /// split it into multiple equal-length laps
  /// </summary>
  private void DetectAndSplitMissedReads(string tagID, List<(string, bool)> messagesToAdd)
  {
    var rider = riders[tagID];
    if (rider.Laps.Count < 3) return; // Need at least 3 laps to analyze (skip first lap)

    var lastLap = rider.Laps.Last();
    if (!lastLap.LapTime.HasValue) return;

    var lastLapTime = lastLap.LapTime.Value;

    // Calculate recent average lap time (excluding the last lap and the first lap)
    var recentLaps = rider.Laps.Skip(1) // Skip the first lap
        .Take(rider.Laps.Count - 2) // Exclude the last lap as well
        .Where(l => l.LapTime.HasValue)
        .TakeLast(5) // Use last 5 laps for average
        .ToList();

    if (recentLaps.Count < 2) return; // Need at least 2 previous laps (excluding first)

    var avgLapTime = TimeSpan.FromMilliseconds(
        recentLaps.Average(l => l.LapTime!.Value.TotalMilliseconds));

    // Check if the last lap is 2-5 times the average lap time
    var ratio = lastLapTime.TotalMilliseconds / avgLapTime.TotalMilliseconds;

    if (ratio >= 1.8 && ratio <= 5.5) // Allow some tolerance
    {
      // Determine how many laps this represents
      int missedLaps = (int)Math.Round(ratio);

      if (missedLaps >= 2 && missedLaps <= 5)
      {
        // Calculate equal split lap time
        var splitLapTime = TimeSpan.FromMilliseconds(lastLapTime.TotalMilliseconds / missedLaps);

        // Calculate global average lap time from all riders to validate split lap time
        var globalAvgLapTime = CalculateGlobalAverageLapTime();
        if (globalAvgLapTime.HasValue)
        {
          // Check if split laps would be too short compared to global average
          var splitToGlobalRatio = splitLapTime.TotalMilliseconds / globalAvgLapTime.Value.TotalMilliseconds;
          if (splitToGlobalRatio < 0.5) // Split laps are less than 50% of global average
          {
            messagesToAdd.Add(($"⚠️ MISSED READS NOT SPLIT: {tagID} - Split laps would be too short ({splitLapTime.TotalSeconds:F1}s vs global avg {globalAvgLapTime.Value.TotalSeconds:F1}s)", true));
            return; // Don't split if it would create unrealistically short laps
          }
        }

        // Store the original lap number before removing it
        var originalLapNumber = lastLap.LapNumber;

        // Remove the original long lap
        rider.Laps.RemoveAt(rider.Laps.Count - 1);

        // Also remove the original lap from the database and any subsequent laps that might conflict
        if (currentRaceId.HasValue)
        {
          // Delete the original lap and any laps with higher numbers (to handle edge cases)
          for (int lapToDelete = originalLapNumber; lapToDelete <= originalLapNumber + missedLaps; lapToDelete++)
          {
            _raceDb.DeleteLap(tagID, lapToDelete);
          }
        }

        // Add the split laps - they will replace the original lap with the same starting lap number
        var baseCrossingTime = lastLap.CrossingTime - lastLapTime;

        for (int i = 1; i <= missedLaps; i++)
        {
          var splitCrossingTime = baseCrossingTime + TimeSpan.FromMilliseconds(splitLapTime.TotalMilliseconds * i);

          var splitLap = new RiderLap
          {
            TagID = tagID,
            CrossingTime = splitCrossingTime,
            LapNumber = originalLapNumber + i - 1, // Start from original lap number
            LapTime = splitLapTime,
            IsSplitLap = true // Mark this as a split lap
          };

          rider.Laps.Add(splitLap);

          // Update database for each split lap - do this synchronously to maintain order
          if (currentRaceId.HasValue)
          {
            var position = CalculateCurrentPosition(tagID);
            _raceDb.AddLap(tagID, splitLap, position);
          }
        }

        // Update rider's last crossing time
        rider.LastCrossing = lastLap.CrossingTime;

        messagesToAdd.Add(($"🔄 MISSED READS DETECTED: {tagID} - Split {lastLapTime.TotalSeconds:F1}s lap into {missedLaps} laps of {splitLapTime.TotalSeconds:F1}s each", true));
      }
    }
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

          // Calculate average lap time, but only if there are lap times available
          var lapTimesWithValues = rider.Laps.Where(l => l.LapTime.HasValue).ToList();
          var avgLapStr = "N/A";
          if (lapTimesWithValues.Any())
          {
            var avgLapTime = lapTimesWithValues.Average(l => l.LapTime!.Value.TotalMilliseconds);
            avgLapStr = TimeSpan.FromMilliseconds(avgLapTime).ToString(@"mm\:ss\.fff");
          }

          var statusStr = rider.IsDNF ? " (DNF)" : "";
          messages.Add($"📊 #{position}: Tag {rider.TagID} | {rider.TotalLaps} laps | Best: {bestLap} | Avg: {avgLapStr} | Total: {totalTime}{statusStr}");
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
      ridersDisplayNeedsUpdate = true;
      lapChartNeedsUpdate = true;
      currentRaceId = null;

      // Reset position tracking
      lastKnownPositions.Clear();
      lastKnownLapCounts.Clear();
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
        AddMessage($"[{clientEndpoint}] Invalid GT response (too short): {message}");
        return;
      }

      // Extract time part (after "GT")
      int dateIndex = message.IndexOf(" date=");
      if (dateIndex == -1)
      {
        AddMessage($"[{clientEndpoint}] Invalid GT response format (no date): {message}");
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

          AddMessage($"[{clientEndpoint}] ⏰ Reader Time Sync: {formattedTime} on {formattedDate}");

          // Show time difference if significant
          try
          {
            var readerDateTime = DateTime.ParseExact($"{formattedDate} {formattedTime}",
                                                   "yyyy-MM-dd HH:mm:ss.fff", null);
            var timeDiff = DateTime.Now - readerDateTime;

            if (Math.Abs(timeDiff.TotalSeconds) > 1)
            {
              AddMessage($"[{clientEndpoint}] ⚠️  Time difference: {timeDiff.TotalSeconds:F2} seconds");
            }
          }
          catch
          {
            // Ignore parsing errors for time comparison
          }
        }
        else
        {
          AddMessage($"[{clientEndpoint}] ⏰ Reader Time Sync: {formattedTime} (invalid date format)");
        }
      }
      else
      {
        AddMessage($"[{clientEndpoint}] ⏰ Reader Time Sync: {timeStr} date={dateStr} (raw format)");
      }

      // After successful time sync, send S0000 to start tag reading
      await SendS0000Command(clientEndpoint);
    }
    catch (Exception ex)
    {
      AddMessage($"[{clientEndpoint}] Error parsing GT response '{message}': {ex.Message}");
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

        AddMessage($"[{clientEndpoint}] 📡 Sent S0000 command to start tag reading");
      }
      else
      {
        AddMessage($"[{clientEndpoint}] ❌ Cannot send S0000 - client not found or disconnected");
      }
    }
    catch (Exception ex)
    {
      AddMessage($"[{clientEndpoint}] Error sending S0000 command: {ex.Message}");
    }
  }

  private void AddMessage(string message)
  {
    // Redirect to race events by default
    AddRaceEvent(message);
  }

  private void AddTagEvent(string message)
  {
    if (InvokeRequired)
    {
      BeginInvoke(new Action<string>(AddTagEvent), message);
      return;
    }

    var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
    var formattedMessage = $"[{timestamp}] {message}";

    // Add to UI
    listBoxTagEvents.Items.Add(formattedMessage);

    // Write to log file
    WriteToLogFile("TAG", message);

    // Auto-scroll to bottom
    listBoxTagEvents.TopIndex = listBoxTagEvents.Items.Count - 1;

    // Limit items to prevent memory issues
    while (listBoxTagEvents.Items.Count > 10000)
    {
      listBoxTagEvents.Items.RemoveAt(0);
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

    // Store in database
    if (currentRaceId != null)
    {
      _raceDb.AddRaceEvent("SYSTEM", "", message);
    }

    // Auto-scroll to bottom
    listBoxMessages.TopIndex = listBoxMessages.Items.Count - 1;

    // Limit items to prevent memory issues
    while (listBoxMessages.Items.Count > 10000)
    {
      listBoxMessages.Items.RemoveAt(0);
    }
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
      labelConnections.Text = $"Connections: {connectedClients.Count}";
    }
  }

  private void UpdateUI()
  {
    if (InvokeRequired)
    {
      Invoke(new Action(UpdateUI));
      return;
    }

    buttonStart.Enabled = !isListening;
    buttonStop.Enabled = isListening;
    textBoxPort.Enabled = !isListening;

    labelStatus.Text = isListening ? "Listening" : "Stopped";
    labelStatus.ForeColor = isListening ? Color.Green : Color.Red;
  }

  private void buttonShowSummary_Click(object? sender, EventArgs e)
  {
    DisplayAllRidersSummary();
  }

  private void buttonClearRiders_Click(object? sender, EventArgs e)
  {
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
          .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        raceStartSnapshot = raceStartTime;
        raceDurationSnapshot = raceDuration;
        raceFinishedSnapshot = raceFinished;

        // Get additional timing information from race state manager
        additionalLapsSignShown = _raceStateManager.FinalLapsStartTime;
        raceActuallyEnded = _raceStateManager.RaceEndTime;
        additionalLapsCount = _raceStateManager.AdditionalLapsAfterTimeExpiry;

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
        MessageBox.Show("No race data available to generate a report.", "No Data",
          MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
      }

      // Show report options dialog
      using var reportDialog = new ReportOptionsDialog();
      if (reportDialog.ShowDialog() == DialogResult.OK)
      {
        var raceTitle = reportDialog.RaceTitle;

        switch (reportDialog.SelectedAction)
        {
          case ReportAction.Preview:
            _raceReportGenerator.ShowPrintPreview(riderSnapshot, raceStartSnapshot,
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
      MessageBox.Show($"Error generating race report: {ex.Message}", "Error",
        MessageBoxButtons.OK, MessageBoxIcon.Error);
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
          int importedCount = 0;
          string fileName = openFileDialog.FileName;
          string extension = Path.GetExtension(fileName).ToLower();

          // Import based on file type
          if (extension == ".xlsx" || extension == ".xls")
          {
            importedCount = _riderDataImporter.ImportFromExcel(fileName);
          }
          else if (extension == ".csv")
          {
            importedCount = _riderDataImporter.ImportFromCsv(fileName);
          }
          else
          {
            MessageBox.Show("Unsupported file format. Please select an Excel (.xlsx) or CSV (.csv) file.",
              "Invalid File Type", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
          }

          // Update UI to show import status
          if (importedCount > 0)
          {
            labelImportStatus.Text = $"✓ {importedCount} riders imported";
            labelImportStatus.ForeColor = Color.DarkGreen;

            AddMessage($"📋 Imported rider data for {importedCount} riders from {Path.GetFileName(fileName)}");

            // Apply imported data to any existing riders
            ApplyImportedDataToExistingRiders();
          }
          else
          {
            labelImportStatus.Text = "⚠ No riders imported";
            labelImportStatus.ForeColor = Color.Orange;
            MessageBox.Show("No rider data was imported. Please check the file format and content.",
              "Import Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
          }
        }
      }
    }
    catch (Exception ex)
    {
      labelImportStatus.Text = "✗ Import failed";
      labelImportStatus.ForeColor = Color.Red;
      MessageBox.Show($"Error importing rider data: {ex.Message}", "Import Error",
        MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        ridersDisplayNeedsUpdate = true;
        lapChartNeedsUpdate = true;
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
  }

  private void InitializeRidersDataGrid()
  {
    // Set up the DataGridView columns
    dataGridViewRiders.Columns.Clear();
    dataGridViewRiders.Columns.Add("Position", "Pos");
    dataGridViewRiders.Columns.Add("RiderNumber", "Number");
    dataGridViewRiders.Columns.Add("TagID", "Tag ID");
    dataGridViewRiders.Columns.Add("RiderName", "Rider Name");
    dataGridViewRiders.Columns.Add("Team", "Team");
    dataGridViewRiders.Columns.Add("Laps", "Laps");
    dataGridViewRiders.Columns.Add("LastLap", "Last Lap");
    dataGridViewRiders.Columns.Add("BestLap", "Best Lap");
    dataGridViewRiders.Columns.Add("AvgLap", "Avg Lap");
    dataGridViewRiders.Columns.Add("PredictedLap", "Predicted");
    dataGridViewRiders.Columns.Add("NextCrossing", "Next Est.");
    dataGridViewRiders.Columns.Add("TimeToNext", "Time To Next");
    dataGridViewRiders.Columns.Add("TotalTime", "Total Time");
    dataGridViewRiders.Columns.Add("Gap", "Gap");

    // Set column widths
    foreach (DataGridViewColumn column in dataGridViewRiders.Columns)
    {
      switch (column.Name)
      {
        case "Position": column.Width = 40; break;
        case "RiderNumber": column.Width = 60; break;
        case "TagID": column.Width = 200; break; // Increased to accommodate up to 32-character tag IDs
        case "RiderName": column.Width = 150; break;
        case "Team": column.Width = 120; break;
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

    var addToIgnoreItem = new ToolStripMenuItem("Add Tag to Ignore List")
    {
      ShortcutKeys = Keys.Delete
    };
    addToIgnoreItem.Click += (s, e) => HandleAddTagToIgnoreList();

    var removeFromIgnoreItem = new ToolStripMenuItem("Remove Tag from Ignore List");
    removeFromIgnoreItem.Click += (s, e) => HandleRemoveTagFromIgnoreList();

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

    dataGridViewRiders.ContextMenuStrip = contextMenu;
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

      addToIgnoreItem.Enabled = hasSelection && !isIgnored;
      addToIgnoreItem.Text = hasSelection ? $"Add {tagId} to Ignore List" : "Add Tag to Ignore List";

      removeFromIgnoreItem.Enabled = hasSelection && isIgnored;
      removeFromIgnoreItem.Text = hasSelection ? $"Remove {tagId} from Ignore List" : "Remove Tag from Ignore List";

      clearIgnoreListItem.Enabled = ignoredTags.Count > 0;
    };

    listBoxTagEvents.ContextMenuStrip = contextMenu;
  }

  private void UpdateRidersDisplay()
  {
    if (InvokeRequired)
    {
      Invoke(new Action(UpdateRidersDisplay));
      return;
    }

    // Only update if we're on the Riders tab to improve performance
    if (tabControl.SelectedIndex != 2) // Riders tab is index 2
      return;

    // Create snapshot of rider data to avoid holding lock during UI operations
    List<RiderInfo> riderSnapshot;
    DateTime? raceStartSnapshot;
    bool raceFinishedSnapshot;

    lock (ridersLock)
    {
      if (riders.Count == 0)
        return;

      // Create deep copies of rider data to avoid references to locked objects
      // Filter out ignored riders
      riderSnapshot = riders.Values
        .Where(r => !ignoredTags.Contains(r.TagID))
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
    }

    try
    {
      // Suspend layout to improve performance during bulk updates
      dataGridViewRiders.SuspendLayout();

      // Clear existing rows
      dataGridViewRiders.Rows.Clear();

      // Sort riders: Finishing riders first (by laps desc, then time asc), then DNF riders (by laps desc, then time asc)
      var sortedRiders = PositionCalculator.GetSortedRidersFromSnapshot(riderSnapshot);

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

        // Calculate average lap time
        var lapsWithTimes = rider.Laps.Where(l => l.LapTime.HasValue).ToList();
        var avgLapStr = "N/A";
        if (lapsWithTimes.Any())
        {
          var avgLapTime = lapsWithTimes.Average(l => l.LapTime!.Value.TotalMilliseconds);
          avgLapStr = TimeSpan.FromMilliseconds(avgLapTime).ToString(@"mm\:ss\.fff");
        }

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
        var displayTagID = rider.IsDNF ? $"{rider.TagID} (DNF)" : rider.TagID;
        if (hasSplitLaps && !rider.IsDNF)
        {
          displayTagID = $"{rider.TagID} *"; // Add asterisk to indicate split laps
        }

        var riderName = rider.DisplayName != rider.TagID ? rider.DisplayName : "";
        var teamName = rider.Team;

        dataGridViewRiders.Rows.Add(
          (i + 1).ToString(),  // Position
          string.IsNullOrEmpty(rider.RiderNumber) ? "" : rider.RiderNumber,  // Rider Number
          displayTagID,        // Tag ID
          riderName,          // Rider Name
          teamName,           // Team
          rider.TotalLaps.ToString(),
          rider.LastLapTime?.ToString(@"mm\:ss\.fff") ?? "N/A",
          rider.BestLapTime?.ToString(@"mm\:ss\.fff") ?? "N/A",
          avgLapStr,
          predictedLapStr,
          nextCrossingStr,
          timeToNextStr,
          rider.TotalTime.ToString(@"mm\:ss\.fff"),
          gap
        );

        // Color coding for positions and status
        var row = dataGridViewRiders.Rows[dataGridViewRiders.Rows.Count - 1];

        if (rider.IsDNF)
        {
          // DNF riders get a gray background
          row.DefaultCellStyle.BackColor = Color.LightGray;
          row.DefaultCellStyle.ForeColor = Color.DarkRed;
          row.Cells["NextCrossing"].Style.Font = new Font(dataGridViewRiders.Font, FontStyle.Bold);
          row.Cells["TimeToNext"].Style.Font = new Font(dataGridViewRiders.Font, FontStyle.Bold);
        }
        else if (i == 0)
          row.DefaultCellStyle.BackColor = Color.Gold;  // 1st place
        else if (i == 1)
          row.DefaultCellStyle.BackColor = Color.Silver;  // 2nd place
        else if (i == 2)
          row.DefaultCellStyle.BackColor = Color.FromArgb(205, 127, 50);  // 3rd place (bronze)

        // Mark riders with split laps
        if (hasSplitLaps && !rider.IsDNF)
        {
          row.Cells["TagID"].Style.ForeColor = Color.Red;
          row.Cells["TagID"].Style.Font = new Font(dataGridViewRiders.Font, FontStyle.Bold);
        }

        // Highlight overdue riders (but not if they're already DNF)
        if (timeToNextStr == "Overdue" && !rider.IsDNF)
        {
          row.Cells["TimeToNext"].Style.ForeColor = Color.Red;
          row.Cells["TimeToNext"].Style.Font = new Font(dataGridViewRiders.Font, FontStyle.Bold);
        }
      }
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

      // Get additional timing information from race state manager
      additionalLapsSignShown = _raceStateManager.FinalLapsStartTime;
      raceActuallyEnded = _raceStateManager.RaceEndTime;
      additionalLapsCount = _raceStateManager.AdditionalLapsAfterTimeExpiry;
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
      labelLastTag.Text = $"Last Tag: {lastTagSnapshot} ({timeSince.TotalSeconds:F0}s ago)";
    }
    else
    {
      labelLastTag.Text = "Last Tag: None";
    }

    // Show next expected crossing (only if on Race Statistics tab)
    if (tabControl.SelectedIndex == 3) // Race Statistics tab
    {      // Show additional timing information for race progression or next expected crossing
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

            // Show warnings as race nears end
            if (timeRemaining.TotalMinutes <= 5 && timeRemaining.TotalMinutes > 1 && !fiveMinuteWarningShown)
            {
              AddMessage("⚠️ 5 MINUTES REMAINING!");
              fiveMinuteWarningShown = true;
            }
            else if (timeRemaining.TotalMinutes <= 1 && !oneMinuteWarningShown)
            {
              AddMessage("⚠️ 1 MINUTE REMAINING!");
              oneMinuteWarningShown = true;
            }
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

      if (timeToNext > TimeSpan.Zero)
      {
        if (timeToNext.TotalMinutes < 1)
          nextCrossingInfo = $"Next Expected: {nextRider.TagID} in {timeToNext.TotalSeconds:F0}s";
        else
          nextCrossingInfo = $"Next Expected: {nextRider.TagID} in {timeToNext:mm\\:ss}";
      }
      else
      {
        nextCrossingInfo = $"Overdue: {nextRider.TagID} (expected {Math.Abs(timeToNext.TotalSeconds):F0}s ago)";
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

  private void timerUpdate_Tick(object? sender, EventArgs e)
  {
    UpdateStatisticsDisplay();

    // Update riders display if needed - always update immediately to avoid empty table
    if (ridersDisplayNeedsUpdate)
    {
      ridersDisplayNeedsUpdate = false;
      UpdateRidersDisplay();
    }
    else if (tabControl.SelectedIndex == 2) // If on Riders tab, update predictions periodically
    {
      // Update only the time-sensitive columns to keep predictions current (but not if race is finished)
      if (!raceFinished)
      {
        UpdateRiderPredictions();
      }
    }

    // Update lap chart if needed or every 5 seconds to keep progress line current
    if (lapChartNeedsUpdate)
    {
      lapChartNeedsUpdate = false;
      if (tabControl.SelectedIndex == 4) // Only update if on Lap Chart tab
      {
        panelLapChart.Invalidate();
        lastProgressLineUpdate = DateTime.Now;
      }
    }
    else if (tabControl.SelectedIndex == 4) // If on Lap Chart tab, refresh every 5 seconds
    {
      var timeSinceLastUpdate = DateTime.Now - lastProgressLineUpdate;
      if (timeSinceLastUpdate.TotalSeconds >= 5)
      {
        panelLapChart.Invalidate(); // Refresh to update progress line position
        lastProgressLineUpdate = DateTime.Now;
      }
    }

    // Update lap progression display if needed - be more aggressive about updates
    if (lapProgressionNeedsUpdate)
    {
      lapProgressionNeedsUpdate = false;

      // Create snapshot for lap progression update
      List<RiderInfo> riderSnapshot;
      bool raceFinishedSnapshot;
      bool waitingForFinalLapsSnapshot;

      lock (ridersLock)
      {
        riderSnapshot = riders.Values.Where(r => !ignoredTags.Contains(r.TagID)).ToList();
        raceFinishedSnapshot = raceFinished;
        waitingForFinalLapsSnapshot = waitingForFinalLaps;
      }

      // Update regardless of which tab is active - the manager will handle efficiency
      _lapProgressionManager.UpdateLapProgressionDisplay(riderSnapshot, raceFinishedSnapshot, waitingForFinalLapsSnapshot, this);
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

    // Only update if we're on the Riders tab and have data
    if (tabControl.SelectedIndex != 2 || dataGridViewRiders.Rows.Count == 0)
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
      var sortedRiders = PositionCalculator.GetSortedRidersFromSnapshot(riderSnapshot);

      for (int i = 0; i < Math.Min(sortedRiders.Count, dataGridViewRiders.Rows.Count); i++)
      {
        var rider = sortedRiders[i];
        var row = dataGridViewRiders.Rows[i];

        // Update next crossing prediction
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

        // Update the cells
        row.Cells["NextCrossing"].Value = nextCrossingStr;
        row.Cells["TimeToNext"].Value = timeToNextStr;

        // Update styling for overdue riders (but not DNF or finished race)
        if (timeToNextStr == "Overdue" && !rider.IsDNF && !raceFinishedSnapshot)
        {
          row.Cells["TimeToNext"].Style.ForeColor = Color.Red;
          row.Cells["TimeToNext"].Style.Font = new Font(dataGridViewRiders.Font, FontStyle.Bold);
        }
        else
        {
          row.Cells["TimeToNext"].Style.ForeColor = dataGridViewRiders.DefaultCellStyle.ForeColor;
          row.Cells["TimeToNext"].Style.Font = dataGridViewRiders.Font;
        }
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
  /// Adds a tag to the ignore list and removes any existing rider data
  /// </summary>
  private void AddTagToIgnoreList(string tagID)
  {
    if (string.IsNullOrWhiteSpace(tagID))
      return;

    if (ignoredTags.Add(tagID))
    {
      AddMessage($"⛔ Added tag '{tagID}' to ignore list. Total ignored tags: {ignoredTags.Count}");

      // Remove any existing rider data for this tag
      bool hadExistingData = false;
      lock (ridersLock)
      {
        if (riders.ContainsKey(tagID))
        {
          var rider = riders[tagID];
          hadExistingData = true;

          // Remove from riders dictionary
          riders.Remove(tagID);

          // Remove from position tracking
          lastKnownPositions.Remove(tagID);
          lastKnownLapCounts.Remove(tagID);

          AddMessage($"🗑️ Removed existing race data for ignored tag '{tagID}' ({rider.TotalLaps} laps)");

          // Mark displays for update
          ridersDisplayNeedsUpdate = true;
          lapChartNeedsUpdate = true;
          lapProgressionNeedsUpdate = true;
        }
      }

      // Remove from database if exists
      if (hadExistingData && currentRaceId.HasValue)
      {
        Task.Run(() =>
        {
          // Delete all laps for this rider
          var riderLaps = _raceDb.GetRiderLaps(tagID);
          foreach (var lap in riderLaps)
          {
            _raceDb.DeleteLap(tagID, lap.LapNumber);
          }

          // Remove rider from database by trying to get all riders and filtering out this one
          // Since there's no direct DeleteRider method, we rely on the rider being excluded from future queries
          AddMessage($"🗄️ Removed all lap data for tag '{tagID}' from database");
        });
      }

      // Update displays if user is viewing them
      if (tabControl.SelectedIndex == 2) // Riders tab
      {
        BeginInvoke(new Action(UpdateRidersDisplay));
      }
    }
    else
    {
      AddMessage($"⚠️ Tag '{tagID}' is already in the ignore list.");
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
      var selectedRow = dataGridViewRiders.SelectedRows[0];
      var tagID = selectedRow.Cells["TagID"].Value?.ToString();

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
      var selectedRow = dataGridViewRiders.SelectedRows[0];
      var tagID = selectedRow.Cells["TagID"].Value?.ToString();

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
  /// Extracts the tag ID from the currently selected item in the tag events list
  /// </summary>
  private string? ExtractTagFromSelectedEvent()
  {
    if (listBoxTagEvents.SelectedItem == null)
      return null;

    string eventText = listBoxTagEvents.SelectedItem.ToString() ?? "";

    // Tag events format: "HH:mm:ss.fff - Tag: TAGID (status message)"
    // Extract the tag ID between "Tag: " and " ("
    int tagStart = eventText.IndexOf("Tag: ");
    if (tagStart == -1) return null;

    tagStart += 5; // Skip "Tag: "
    int tagEnd = eventText.IndexOf(" (", tagStart);
    if (tagEnd == -1) tagEnd = eventText.Length;

    string tagId = eventText.Substring(tagStart, tagEnd - tagStart).Trim();
    return string.IsNullOrEmpty(tagId) ? null : tagId;
  }

  #endregion

  private void panelLapChart_Paint(object? sender, PaintEventArgs e)
  {
    try
    {
      // Delegate to the lap chart renderer using the actual race state from Form1
      _lapChartRenderer.DrawLapChart(e.Graphics, panelLapChart.ClientRectangle, riders,
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
      _lapChartRenderer.HandleMouseClick(adjustedLocation, ShowRiderDetails, () => panelLapChart.Invalidate());
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
    bool hasHoveredElement = _lapChartRenderer.HandleMouseMove(adjustedLocation, () => panelLapChart.Invalidate(), riders);

    // Change cursor when hovering over clickable elements
    panelLapChart.Cursor = hasHoveredElement ? Cursors.Hand : Cursors.Default;
  }

  private void PanelLapChart_MouseLeave(object? sender, EventArgs e)
  {
    // Delegate to the lap chart renderer
    _lapChartRenderer.HandleMouseLeave(() => panelLapChart.Invalidate());
    panelLapChart.Cursor = Cursors.Default;
  }

  private void ShowRiderDetails(string riderId)
  {
    lock (ridersLock)
    {
      if (riders.TryGetValue(riderId, out var rider))
      {
        var details = new StringBuilder();
        details.AppendLine($"Rider: {riderId}");
        details.AppendLine($"Total Laps: {rider.TotalLaps}");
        details.AppendLine($"Total Time: {rider.TotalTime:hh\\:mm\\:ss\\.fff}");
        if (rider.BestLapTime.HasValue)
          details.AppendLine($"Best Lap: {rider.BestLapTime.Value:mm\\:ss\\.fff}");
        details.AppendLine();
        details.AppendLine("Lap Times with Positions:");

        for (int i = 0; i < rider.Laps.Count; i++)
        {
          var lap = rider.Laps[i];
          TimeSpan? displayLapTime = lap.LapTime;

          // Calculate first lap time from race start if it's null
          if (i == 0 && lap.LapTime == null && raceStartTime.HasValue)
          {
            displayLapTime = lap.CrossingTime - raceStartTime.Value;
          }

          var lapTimeStr = displayLapTime?.ToString(@"mm\:ss\.fff") ?? "N/A";

          // Calculate position for this lap by comparing with other riders at this lap completion time
          int position = CalculatePositionAtTime(riderId, lap.CrossingTime, i + 1);

          details.AppendLine($"  Lap {i + 1}: P{position} - {lapTimeStr} ({lap.CrossingTime:HH:mm:ss})");
        }

        MessageBox.Show(details.ToString(), $"Lap Details - {riderId}",
          MessageBoxButtons.OK, MessageBoxIcon.Information);
      }
    }
  }

  /// <summary>
  /// Calculate what position a rider was in when they completed a specific lap
  /// </summary>
  private int CalculatePositionAtTime(string riderId, DateTime lapCompletionTime, int riderLapCount)
  {
    // Count how many riders had completed more laps at this time, or same laps but faster total time
    int ridersAhead = 0;

    foreach (var otherRider in riders.Values)
    {
      if (otherRider.TagID == riderId || ignoredTags.Contains(otherRider.TagID)) continue;

      // Count laps completed by this other rider at the time of the target rider's lap completion
      int otherRiderLapsAtTime = 0;
      DateTime? otherRiderTimeAtSameLaps = null;

      foreach (var otherLap in otherRider.Laps)
      {
        if (otherLap.CrossingTime <= lapCompletionTime)
        {
          otherRiderLapsAtTime++;
          if (otherRiderLapsAtTime == riderLapCount)
          {
            otherRiderTimeAtSameLaps = otherLap.CrossingTime;
          }
        }
        else
        {
          break; // No need to check further laps
        }
      }

      // Determine if this other rider was ahead
      if (otherRiderLapsAtTime > riderLapCount)
      {
        // Other rider had more laps completed - they were ahead
        ridersAhead++;
      }
      else if (otherRiderLapsAtTime == riderLapCount && otherRiderTimeAtSameLaps.HasValue)
      {
        // Same number of laps - compare completion times
        if (otherRiderTimeAtSameLaps.Value < lapCompletionTime)
        {
          // Other rider completed the same lap faster - they were ahead
          ridersAhead++;
        }
      }
    }

    return ridersAhead + 1; // Position is number of riders ahead + 1
  }

  /// <summary>
  /// Show context menu for tag operations
  /// </summary>
  private void ShowTagContextMenu(string tagId, Point location)
  {
    var contextMenu = new ContextMenuStrip();

    bool isIgnored = ignoredTags.Contains(tagId);

    if (isIgnored)
    {
      var removeItem = new ToolStripMenuItem($"Remove {tagId} from ignore list");
      removeItem.Click += (s, e) => RemoveTagFromIgnoreList(tagId);
      contextMenu.Items.Add(removeItem);
    }
    else
    {
      var addItem = new ToolStripMenuItem($"Add {tagId} to ignore list");
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
  }

  private void UpdateRaceStartControls()
  {
    if (InvokeRequired)
    {
      BeginInvoke(new Action(UpdateRaceStartControls));
      return;
    }

    buttonStartRace.Enabled = manualStartMode && !raceStarted && !raceFinished;

    if (raceFinished)
    {
      labelRaceStatus.Text = "Race: FINISHED";
      labelRaceStatus.ForeColor = Color.Blue;
    }
    else if (waitingForFinalLaps)
    {
      // Count how many riders are still eligible to complete their final lap
      var ridersStillActive = riders.Values.Count(r => r.TotalLaps < r.FinalAllowedLap &&
                                                       (r.PredictedLapTime.HasValue ||
                                                        (DateTime.Now - r.LastCrossing).TotalMinutes < 2));

      labelRaceStatus.Text = $"Race: LEADER FINISHED - {ridersStillActive} riders completing final lap";
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
        labelRaceStatus.Text = $"Race: LEADER {leaderAtTimeExpiry} - {remainingLaps} {lapsText} to go (target: {targetLapsToFinishRace})";
      }
      else
      {
        labelRaceStatus.Text = $"Race: Waiting for Leader {leaderAtTimeExpiry} to complete additional laps";
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
      currentRaceId = _raceDb.StartNewRace(raceStartTime.Value, raceDuration);

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

      // Reset warnings
      fiveMinuteWarningShown = false;
      oneMinuteWarningShown = false;

      // Update displays to reflect new total times
      ridersDisplayNeedsUpdate = true;
      lapChartNeedsUpdate = true;
    }
  }

  private void FinishRace()
  {
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

    var finishingRiderTag = finishingRider?.TagID ?? "Unknown";

    AddMessage($"🏁 RACE TARGET REACHED! {finishingRiderTag} completed {targetLapsToFinishRace} laps in {actualRaceDuration:mm\\:ss}.");
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
          AddMessage($"📋 Rider {rider.TagID}: Reached target with {rider.TotalLaps} laps, RACE FINISHED - no more laps allowed");
        }
        else
        {
          // All other riders are allowed to complete exactly one more lap (their current lap)
          rider.FinalAllowedLap = rider.TotalLaps + 1;
          AddMessage($"📋 Rider {rider.TagID}: Currently has {rider.TotalLaps} laps, allowed to complete lap {rider.FinalAllowedLap}");
        }
      }
    }

    // Update race status
    UpdateRaceStartControls();

    // Force final update of displays
    ridersDisplayNeedsUpdate = true;
    lapChartNeedsUpdate = true;
  }

  private void CheckIfAllFinalLapsCompleted()
  {
    // Check if all riders have either completed their final allowed lap or have timed out
    bool allRidersFinished = true;

    foreach (var rider in riders.Values)
    {
      // Skip riders already marked as DNF
      if (rider.IsDNF)
        continue;

      // If rider hasn't reached their final allowed lap yet
      if (rider.TotalLaps < rider.FinalAllowedLap)
      {
        // Check if too much time has passed since leader finished (timeout)
        var timeSinceLeaderFinished = finalLapsStartTime.HasValue ?
          DateTime.Now - finalLapsStartTime.Value : TimeSpan.Zero;

        // If less than timeout period since leader finished, rider might still finish their lap
        if (timeSinceLeaderFinished.TotalMinutes < dnfTimeoutMinutes)
        {
          allRidersFinished = false;
          // Don't break - continue checking other riders for DNF timeout
        }
        else
        {
          // Rider has timed out - mark as DNF
          rider.IsDNF = true;
          rider.DNFTime = DateTime.Now;
          AddMessage($"🚫 Rider {rider.TagID} marked as DNF (Did Not Finish) - {timeSinceLeaderFinished.TotalMinutes:F1} min since leader finished, failed to complete final lap");
          AddRaceEvent($"DNF: {rider.TagID} - Timeout after {timeSinceLeaderFinished.TotalMinutes:F1} minutes");

          // Update displays to show DNF status
          ridersDisplayNeedsUpdate = true;
          lapChartNeedsUpdate = true;
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

    AddMessage($"🏁 RACE COMPLETELY FINISHED! All riders have completed their final laps or timed out.");
    AddMessage($"🏁 Final race duration: {actualRaceDuration:mm\\:ss}");

    if (dnfRiders.Any())
    {
      AddMessage($"🚫 DNF Summary: {dnfRiders.Count} rider(s) marked as Did Not Finish:");
      foreach (var dnfRider in dnfRiders)
      {
        var raceLeaderFinishTime = dnfRider.DNFTime?.AddMinutes(-dnfTimeoutMinutes) ?? DateTime.Now;
        var timeAtDNF = dnfRider.DNFTime.HasValue ?
          (dnfRider.DNFTime.Value - raceLeaderFinishTime).TotalMinutes : 0;
        AddMessage($"   • {dnfRider.TagID}: {dnfRider.TotalLaps} laps completed, DNF after {timeAtDNF:F1} min timeout");
      }
      AddMessage($"✅ {finishedRiders} rider(s) completed the race successfully.");
    }
    else
    {
      AddMessage($"✅ All {finishedRiders} riders completed the race successfully - no DNF!");
    }

    AddMessage($"🏁 Race results are now final. Additional tag reads will be ignored.");

    // Update race status
    UpdateRaceStartControls();

    // Force final update of displays
    ridersDisplayNeedsUpdate = true;
    lapChartNeedsUpdate = true;
  }

  private void InitializeLogging()
  {
    try
    {
      // Create logs directory if it doesn't exist
      var logsDir = Path.Combine(Application.StartupPath, "logs");
      Directory.CreateDirectory(logsDir);

      // Create log file with timestamp
      var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
      logFilePath = Path.Combine(logsDir, $"CrossMgrInterface_{timestamp}.log");

      // Write initial log header
      var header = $"=== CrossMgr Interface Log Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===";
      WriteToLogFile("SYSTEM", header);

      AddMessage($"📝 Logging initialized: {logFilePath}");
    }
    catch (Exception ex)
    {
      // Don't crash if logging fails, just show a message
      MessageBox.Show($"Warning: Could not initialize logging: {ex.Message}",
        "Logging Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
  }

  private void WriteToLogFile(string category, string message)
  {
    if (string.IsNullOrEmpty(logFilePath))
      return;

    try
    {
      lock (logLock)
      {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logEntry = $"[{timestamp}] [{category}] {message}{Environment.NewLine}";

        File.AppendAllText(logFilePath, logEntry, Encoding.UTF8);
      }
    }
    catch (Exception)
    {
      // Silently ignore logging errors to prevent infinite loops
      // and avoid disrupting the main application
    }
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
  }

  /// <summary>
  /// Check for position changes and lapping events after a rider crossing
  /// </summary>
  private void CheckForPositionChangesAndLapping(string crossingRiderTagID)
  {
    // Don't check for position changes if race hasn't started or is finished
    if (!raceStarted || raceFinished)
      return;

    // Get current standings sorted by position (DNF riders last)
    var currentStandings = riders.Values
      .OrderBy(r => r.IsDNF ? 1 : 0) // Non-DNF riders first (0), DNF riders last (1)
      .ThenByDescending(r => r.TotalLaps)
      .ThenBy(r => r.TotalTime)
      .ToList();

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

  /// <summary>
  /// Check for passing and lapping events using lap difference analysis
  /// </summary>
  private void CheckForPassingAndLappingEvents(List<RiderInfo> currentStandings, string crossingRiderTagID)
  {
    // For each other rider, check if there's a passing or lapping event involving the crossing rider
    foreach (var otherRider in currentStandings)
    {
      if (otherRider.TagID == crossingRiderTagID) continue;

      var crossingRider = riders[crossingRiderTagID];
      var otherRiderInfo = riders[otherRider.TagID];

      // Determine if riders are on the same lap
      bool sameCurrentLap = crossingRider.TotalLaps == otherRiderInfo.TotalLaps;

      if (sameCurrentLap)
      {
        // Same lap = check for passing events only
        CheckPassingEvent(crossingRiderTagID, otherRider.TagID, currentStandings);
      }
      else
      {
        // Different laps = check for lapping events only
        CheckLappingEvent(crossingRiderTagID, otherRider.TagID, currentStandings);
      }
    }
  }

  /// <summary>
  /// Check for a lapping event between two specific riders
  /// </summary>
  private void CheckLappingEvent(string crossingRiderTagID, string otherRiderTagID, List<RiderInfo> currentStandings)
  {
    var crossingRider = riders[crossingRiderTagID];
    var otherRider = riders[otherRiderTagID];

    // Calculate current lap difference (crossing rider - other rider)
    int currentLapDiff = crossingRider.TotalLaps - otherRider.TotalLaps;

    // Don't check lapping if riders are on the same lap - that's a passing event, not lapping
    if (currentLapDiff == 0)
    {
      // Store the lap difference and return - passing logic will handle same-lap events
      StoreLapDifference(crossingRiderTagID, otherRiderTagID, currentLapDiff);
      return;
    }

    // Get previous lap difference
    int previousLapDiff = GetPreviousLapDifference(crossingRiderTagID, otherRiderTagID, currentLapDiff);

    // Lapping occurs when crossing rider gains a lap advantage (goes from same/behind to ahead)
    // The crossing rider must have MORE laps than the other rider to lap them
    if (currentLapDiff >= 1 && previousLapDiff < currentLapDiff)
    {
      // Lapping event detected - crossing rider has gained a lap advantage
      if (currentLapDiff == 1)
      {
        AddRaceEvent($"🔄 {crossingRiderTagID} has LAPPED {otherRiderTagID}!");
      }
      else if (currentLapDiff > 1)
      {
        AddRaceEvent($"🔄 {crossingRiderTagID} has LAPPED {otherRiderTagID} (now {currentLapDiff} laps ahead)!");
      }
    }

    // Store the current lap difference for next comparison
    StoreLapDifference(crossingRiderTagID, otherRiderTagID, currentLapDiff);
  }

  /// <summary>
  /// Check for a passing event between two specific riders (same lap only)
  /// </summary>
  private void CheckPassingEvent(string crossingRiderTagID, string otherRiderTagID, List<RiderInfo> currentStandings)
  {
    // Get current positions
    int currentPosCrossing = currentStandings.FindIndex(r => r.TagID == crossingRiderTagID) + 1;
    int currentPosOther = currentStandings.FindIndex(r => r.TagID == otherRiderTagID) + 1;

    // Get previous positions (if we have history)
    if (!lastKnownPositions.ContainsKey(crossingRiderTagID) || !lastKnownPositions.ContainsKey(otherRiderTagID))
      return; // No previous position data to compare

    int previousPosCrossing = lastKnownPositions[crossingRiderTagID];
    int previousPosOther = lastKnownPositions[otherRiderTagID];

    // Check if crossing rider passed the other rider (was behind but now ahead)
    if (previousPosCrossing > previousPosOther && currentPosCrossing < currentPosOther)
    {
      AddRaceEvent($"⚡ {crossingRiderTagID} PASSES {otherRiderTagID} for position {currentPosCrossing}!");
    }
  }

  /// <summary>
  /// Get the previous lap difference between two riders
  /// </summary>
  private int GetPreviousLapDifference(string riderA, string riderB, int defaultValue)
  {
    return _raceDb.GetPreviousLapDifference(riderA, riderB, defaultValue);
  }

  /// <summary>
  /// Store the lap difference between two riders
  /// </summary>
  private void StoreLapDifference(string riderA, string riderB, int lapDifference)
  {
    _raceDb.StoreLapDifference(riderA, riderB, lapDifference);
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
            AddRaceEvent($"🥇 NEW LEADER! {crossingRiderTagID} takes the lead! (was P{previousPosition})");
          }
          else if (currentPosition <= 3 && previousPosition > 3)
          {
            AddRaceEvent($"🏆 {crossingRiderTagID} moves into podium position {currentPosition}! (was P{previousPosition})");
          }
          else if (positionChange >= 3)
          {
            AddRaceEvent($"⬆️ {crossingRiderTagID} surges up {positionChange} positions to P{currentPosition}! (was P{previousPosition})");
          }
          else
          {
            AddRaceEvent($"⬆️ {crossingRiderTagID} moves up to P{currentPosition} (was P{previousPosition})");
          }
        }
        else
        {
          // Moved down in positions
          if (previousPosition == 1)
          {
            var newLeader = currentStandings.FirstOrDefault();
            AddRaceEvent($"🔄 LEADER CHANGE! {newLeader?.TagID} takes over from {crossingRiderTagID} who drops to P{currentPosition}");
          }
          else if (Math.Abs(positionChange) >= 3)
          {
            AddRaceEvent($"⬇️ {crossingRiderTagID} drops {Math.Abs(positionChange)} positions to P{currentPosition} (was P{previousPosition})");
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
            AddRaceEvent($"🔥 CLOSE BATTLE! P{i + 1} {rider1.TagID} leads P{i + 2} {rider2.TagID} by only {timeDifference.TotalSeconds:F1} seconds!");
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
    // For now, limit battle announcements to avoid spam
    // Could implement more sophisticated logic later
    return DateTime.Now.Second % 30 == 0; // Announce battles every 30 seconds max
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





  private void RecordLapProgression(string riderId, int lapNumber, int position, TimeSpan raceTime)
  {
    // Delegate to the lap progression manager
    _lapProgressionManager.RecordLapProgression(riderId, lapNumber, position, raceTime, riders);

    // Set update flag in a thread-safe way using BeginInvoke to avoid deadlocks
    if (InvokeRequired)
    {
      BeginInvoke(new Action(() => lapProgressionNeedsUpdate = true));
    }
    else
    {
      lapProgressionNeedsUpdate = true;
    }
  }

  private void RecordLapProgressionAfterLapCompletion(string riderId, int lapNumber)
  {
    if (!riders.ContainsKey(riderId)) return;

    // Calculate current position based on completed laps and total time
    var position = CalculateCurrentPosition(riderId);
    var raceTime = riders[riderId].TotalTime;

    RecordLapProgression(riderId, lapNumber, position, raceTime);
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
      MessageBox.Show($"Error during crash recovery: {ex.Message}", "Recovery Error",
        MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
      raceStartTime = raceToRestore.StartTime;
      raceEndTime = raceToRestore.EndTime;
      raceDuration = raceToRestore.Duration;
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

      // Update displays
      ridersDisplayNeedsUpdate = true;
      lapChartNeedsUpdate = true;
      _lapProgressionManager.NeedsUpdate = true;

      // Auto-start TCP server if race was in progress
      if (raceStarted && !raceFinished)
      {
        // Parse the port from the current UI (default to 53135 if not valid)
        if (!int.TryParse(textBoxPort.Text, out int port) || port < 1 || port > 65535)
        {
          port = 53135; // Default port
        }
        StartTcpListener(port);
      }

      // Start periodic state saving
      StartPeriodicStateSaving();

      // Create snapshot for UI update (exclude ignored riders)
      var riderSnapshot = riders.Values.Where(r => !ignoredTags.Contains(r.TagID)).ToList();
      var raceFinishedSnapshot = raceFinished;
      var waitingForFinalLapsSnapshot = waitingForFinalLaps;
      var riderCount = riders.Values.Where(r => !ignoredTags.Contains(r.TagID)).Count();
      var totalLaps = riders.Values.Where(r => !ignoredTags.Contains(r.TagID)).Sum(r => r.TotalLaps);

      // Update UI to reflect restored state
      BeginInvoke(new Action(() =>
      {
        // Update race status labels
        if (labelRaceTime != null)
        {
          if (raceFinishedSnapshot)
          {
            labelRaceTime.Text = "Race: FINISHED";
            labelRaceTime.BackColor = Color.LightGreen;
          }
          else if (raceStarted)
          {
            labelRaceTime.Text = raceTimeExpired ? "Race: TIME EXPIRED" : "Race: IN PROGRESS";
            labelRaceTime.BackColor = raceTimeExpired ? Color.Orange : Color.LightBlue;
          }
        }

        // Update connection status
        UpdateUI(); // This will update the start/stop button states

        // Update displays
        ridersDisplayNeedsUpdate = true;
        lapChartNeedsUpdate = true;
        UpdateRidersDisplay();

        // Force lap chart refresh if user is currently on lap chart tab
        if (tabControl.SelectedIndex == 4)
        {
          lapChartNeedsUpdate = false;
          panelLapChart.Invalidate();
          panelLapChart.Refresh(); // Force immediate repaint
        }
        else
        {
          // Ensure it will update when user switches to lap chart tab
          lapChartNeedsUpdate = true;
        }

        _lapProgressionManager.UpdateLapProgressionDisplay(riderSnapshot, raceFinishedSnapshot, waitingForFinalLapsSnapshot, this);

        // Show recovery success message
        if (labelLastTag != null)
        {
          labelLastTag.Text = $"RECOVERED: {riderCount} riders, {totalLaps} total laps";
          labelLastTag.BackColor = Color.LightGreen;
        }
      }));

      // Add race event outside the UI thread
      AddRaceEvent($"Race state recovered: {riderCount} riders, {totalLaps} total laps");

    }
    catch (Exception ex)
    {
      MessageBox.Show($"Error restoring race state: {ex.Message}", "Restore Error",
        MessageBoxButtons.OK, MessageBoxIcon.Error);
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
          .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
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
