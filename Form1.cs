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

  // Tag filtering
  private string tagFilterPrefix = "";
  private bool tagFilterEnabled = false;
  private int filteredTagCount = 0;

  // Logging
  private string logFilePath = "";
  private readonly object logLock = new object();

  // Position tracking for race events (now backed by database)
  private Dictionary<string, int> lastKnownPositions = new();
  private Dictionary<string, int> lastKnownLapCounts = new();
  private DateTime lastPositionCheck = DateTime.MinValue;

  // Lap chart visualization
  private bool lapChartNeedsUpdate = false;
  private DateTime lastProgressLineUpdate = DateTime.MinValue;
  private string? selectedRiderId = null;
  private string? hoveredLapInfo = null;
  private readonly List<LapChartElement> lapChartElements = new();

  // Lap progression tracking
  private readonly List<LapProgressionEntry> lapProgressionHistory = new();
  private bool lapProgressionNeedsUpdate = false;

  public Form1()
  {
    InitializeComponent();

    // Initialize database service
    _raceDb = new RaceDataService("races.db");

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

    // Add Lap Progression tab programmatically
    CreateLapProgressionTab();

    // Enable double buffering for the lap chart panel to reduce flickering
    typeof(Panel).InvokeMember("DoubleBuffered",
      BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
      null, panelLapChart, new object[] { true });

    // Add mouse event handlers for lap chart interaction
    panelLapChart.MouseClick += PanelLapChart_MouseClick;
    panelLapChart.MouseMove += PanelLapChart_MouseMove;
    panelLapChart.MouseLeave += PanelLapChart_MouseLeave;

    // Set up race start mode controls
    radioButtonStartOnFirstTag.CheckedChanged += RaceStartMode_CheckedChanged;
    radioButtonStartManual.CheckedChanged += RaceStartMode_CheckedChanged;
    UpdateRaceStartControls();

    // Initialize logging
    InitializeLogging();
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
              ProcessMessage(lines[i], stream, clientEndpoint);
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
            ProcessMessage(line, stream, clientEndpoint);
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

  private async void ProcessMessage(string message, NetworkStream stream, string clientEndpoint)
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

      // Format for display
      string displayTime = DateTime.Now.ToString("HH:mm:ss.fff");
      string formattedTagID = FormatTagID(tagID);

      // Check tag filter
      if (!ShouldProcessTag(tagID))
      {
        // Log filtered tag but don't process lap tracking
        filteredTagCount++;
        string filteredMessage = $"🚫 Tag: {formattedTagID,-32} Time: {timeStr,-15} Count: {count,-8} Date: {date} [FILTERED #{filteredTagCount} - doesn't match prefix '{tagFilterPrefix}'] [{displayTime}]";
        AddTagEvent($"[{clientEndpoint}] {filteredMessage}");
        return; // Skip lap processing for filtered tags
      }

      // Process rider lap tracking
      var crossingTime = DateTime.Now; // Use current time as crossing time
      var lapInfo = ProcessRiderCrossing(tagID, crossingTime);

      string lapInfoStr = $"Lap {lapInfo.LapNumber}";
      if (lapInfo.LapTime.HasValue)
      {
        lapInfoStr += $" ({lapInfo.LapTime.Value:mm\\:ss\\.fff})";
      }

      string formattedMessage = $"🏷️  Tag: {formattedTagID,-32} Time: {timeStr,-15} Count: {count,-8} Date: {date} {lapInfoStr} [{displayTime}]";

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
          resultLap = ProcessNormalCrossingInternal(tagID, crossingTime, messagesToAdd);
        }
      }
      // If in manual start mode and race hasn't started yet, ignore tags
      else if (manualStartMode && !raceStarted)
      {
        resultLap = new RiderLap { TagID = tagID, CrossingTime = crossingTime, LapNumber = 0 };
      }
      else
      {
        resultLap = ProcessNormalCrossingInternal(tagID, crossingTime, messagesToAdd);
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

  private RiderLap ProcessNormalCrossingInternal(string tagID, DateTime crossingTime, List<(string, bool)> messagesToAdd)
  {
    // Track race start time on first crossing (only if not manual start mode)
    if (raceStartTime == null && !manualStartMode)
    {
      raceStartTime = crossingTime;
      raceEndTime = raceStartTime.Value + raceDuration;
      raceStarted = true;

      // These operations will be called later after the lock is released
      Task.Run(() => UpdateRaceStartControls());

      messagesToAdd.Add(($"🏁 Race started! Duration: {raceDuration.TotalMinutes} minutes, End time: {raceEndTime:HH:mm:ss}", true));
      messagesToAdd.Add(($"🎯 Predicted total laps will be calculated based on leader performance.", true));
    }

    // Check if race time has expired and we need to wait for leader
    if (raceStartTime.HasValue && raceEndTime.HasValue && DateTime.Now > raceEndTime.Value && !raceTimeExpired && !waitingForLeaderFinish && !raceFinished && !waitingForFinalLaps)
    {
      // Find current leader (exclude DNF riders)
      var currentLeader = riders.Values
        .Where(r => !r.IsDNF)
        .OrderByDescending(r => r.TotalLaps)
        .ThenBy(r => r.TotalTime)
        .FirstOrDefault();

      if (currentLeader != null)
      {
        leaderAtTimeExpiry = currentLeader.TagID;
        leaderLapsAtTimeExpiry = currentLeader.TotalLaps;
        raceTimeExpired = true;
        var lapsText = additionalLapsAfterTimeExpiry == 1 ? "lap" : "laps";
        messagesToAdd.Add(($"⏰ Race time expired! Leader {leaderAtTimeExpiry} currently has {leaderLapsAtTimeExpiry} laps completed.", true));
        messagesToAdd.Add(($"🏁 Race will finish after leader completes any ongoing lap plus {additionalLapsAfterTimeExpiry} additional {lapsText}.", true));
      }
    }

    // Update last tag info
    lastTagID = tagID;
    lastTagTime = crossingTime;

    if (!riders.ContainsKey(tagID))
    {
      // First time seeing this rider
      riders[tagID] = new RiderInfo
      {
        TagID = tagID,
        FirstCrossing = crossingTime,
        LastCrossing = crossingTime,
        RaceStartTime = raceStartTime
      };

      var firstLap = new RiderLap
      {
        TagID = tagID,
        CrossingTime = crossingTime,
        LapNumber = 1,
        LapTime = null
      };

      riders[tagID].Laps.Add(firstLap);

      // These operations will be called later after the lock is released
      Task.Run(() => RecordLapProgressionAfterLapCompletion(tagID, 1));

      ridersDisplayNeedsUpdate = true;
      lapChartNeedsUpdate = true;
      return firstLap;
    }
    else
    {
      // Subsequent crossing
      var rider = riders[tagID];
      var previousCrossing = rider.LastCrossing;
      var lapTime = crossingTime - previousCrossing;

      var newLap = new RiderLap
      {
        TagID = tagID,
        CrossingTime = crossingTime,
        LapNumber = rider.TotalLaps + 1,
        LapTime = lapTime
      };

      rider.Laps.Add(newLap);
      rider.LastCrossing = crossingTime;

      // These operations will be called later after the lock is released
      Task.Run(() => RecordLapProgressionAfterLapCompletion(tagID, rider.TotalLaps));

      // Handle transition from time expired to additional laps phase
      if (raceTimeExpired && !waitingForLeaderFinish && !waitingForFinalLaps && !raceFinished)
      {
        var currentLeader = riders.Values
          .Where(r => !r.IsDNF)
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
      return newLap;
    }
  }

  private void DisplayRiderSummary(string tagID)
  {
    string message;

    lock (ridersLock)
    {
      if (riders.ContainsKey(tagID))
      {
        var rider = riders[tagID];
        var bestLap = rider.BestLapTime?.ToString(@"mm\:ss\.fff") ?? "N/A";
        var lastLap = rider.LastLapTime?.ToString(@"mm\:ss\.fff") ?? "N/A";
        var totalTime = rider.TotalTime.ToString(@"mm\:ss\.fff");

        message = $"📊 Rider {tagID}: {rider.TotalLaps} laps | Best: {bestLap} | Last: {lastLap} | Total: {totalTime}";
      }
      else
      {
        message = $"📊 Rider {tagID}: Not found";
      }
    }

    // Send message outside the lock
    AddMessage(message);
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
      Invoke(new Action<string>(AddTagEvent), message);
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
      Invoke(new Action<string>(AddRaceEvent), message);
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
      Invoke(new Action(UpdateConnectionCount));
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
    dataGridViewRiders.Columns.Add("TagID", "Tag ID");
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
        case "TagID": column.Width = 100; break;
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
      riderSnapshot = riders.Values.Select(r => new RiderInfo
      {
        TagID = r.TagID,
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
      var sortedRiders = riderSnapshot
        .OrderBy(r => r.IsDNF ? 1 : 0) // Non-DNF riders first (0), DNF riders last (1)
        .ThenByDescending(r => r.TotalLaps)
        .ThenBy(r => r.TotalTime)
        .ToList();

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
        var displayTagID = rider.IsDNF ? $"{rider.TagID} (DNF)" : rider.TagID;
        dataGridViewRiders.Rows.Add(
          (i + 1).ToString(),  // Position
          displayTagID,
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

    lock (ridersLock)
    {
      raceStartSnapshot = raceStartTime;
      raceEndSnapshot = raceEndTime;
      riderCount = riders.Count;
      dnfCount = riders.Values.Count(r => r.IsDNF);
      totalLaps = riders.Values.Sum(r => r.TotalLaps);
      lastTagSnapshot = lastTagID;
      lastTagTimeSnapshot = lastTagTime;
      raceFinishedSnapshot = raceFinished;
    }

    // Update race time
    if (raceStartSnapshot.HasValue)
    {
      var elapsed = DateTime.Now - raceStartSnapshot.Value;
      labelRaceTime.Text = $"Race Time: {elapsed:hh\\:mm\\:ss}";
    }
    else
    {
      labelRaceTime.Text = "Race Time: Not Started";
    }

    // Update rider count
    if (dnfCount > 0)
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
    {
      ShowNextExpectedCrossing();

      // Update race end time
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
        else if (!raceFinishedSnapshot) // Only show this message if race isn't already finished
        {
          labelTimeRemaining.Text = "Time Remaining: Race Finished";
          labelTimeRemaining.ForeColor = Color.Red;
          AddMessage("🏁 RACE FINISHED!");
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

      // Update predicted laps
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
  }
  private void ShowNextExpectedCrossing()
  {
    // Create snapshot to avoid nested locking
    RiderInfo? nextRider = null;

    lock (ridersLock)
    {
      // Find the rider expected to cross next
      nextRider = riders.Values
        .Where(r => r.EstimatedNextCrossing.HasValue)
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
      // Find the current leader (exclude DNF riders)
      leader = riders.Values
        .Where(r => !r.IsDNF)
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
    else if (tabControl.SelectedIndex == 2) // If on Riders tab, update predictions
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

    // Update lap progression display if needed
    if (lapProgressionNeedsUpdate)
    {
      lapProgressionNeedsUpdate = false;
      if (tabControl.SelectedIndex == 5) // Only update if on Lap Progression tab
      {
        UpdateLapProgressionDisplay();
      }
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
      riderSnapshot = riders.Values.ToList();
      raceFinishedSnapshot = raceFinished;
    }

    try
    {
      var sortedRiders = riderSnapshot
        .OrderBy(r => r.IsDNF ? 1 : 0) // Non-DNF riders first (0), DNF riders last (1)
        .ThenByDescending(r => r.TotalLaps)
        .ThenBy(r => r.TotalTime)
        .ToList();

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
      // Find the current leader (exclude DNF riders)
      leader = riders.Values
        .Where(r => !r.IsDNF)
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

  private void panelLapChart_Paint(object? sender, PaintEventArgs e)
  {
    try
    {
      DrawLapChart(e.Graphics, panelLapChart.ClientRectangle);
    }
    catch (Exception ex)
    {
      // Log any errors but don't crash the app
      AddMessage($"Error drawing lap chart: {ex.Message}");
    }
  }

  private void DrawLapChart(Graphics g, Rectangle bounds)
  {
    if (bounds.Width <= 0 || bounds.Height <= 0)
      return;

    // Set graphics quality settings for better performance
    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;

    // Account for scroll position
    var scrollOffset = panelLapChart.AutoScrollPosition;
    g.TranslateTransform(scrollOffset.X, scrollOffset.Y);

    // Clear previous clickable elements
    lapChartElements.Clear();

    lock (ridersLock)
    {
      if (riders.Count == 0 || !raceStartTime.HasValue || !raceEndTime.HasValue)
      {
        // Draw "No race data" message
        var font = new Font("Arial", 16, FontStyle.Bold);
        var text = "No race data available";
        var textSize = g.MeasureString(text, font);
        var x = (bounds.Width - textSize.Width) / 2;
        var y = (bounds.Height - textSize.Height) / 2;
        g.DrawString(text, font, Brushes.Gray, x, y);
        font.Dispose();
        return;
      }

      // Calculate race duration and timing
      var raceDurationMs = raceDuration.TotalMilliseconds;

      // Calculate extended duration to show estimated finish times for all riders
      var extendedDurationMs = CalculateExtendedChartDuration(raceDurationMs);

      var raceElapsedMs = (DateTime.Now - raceStartTime.Value).TotalMilliseconds;
      var raceProgressPercent = Math.Min(raceElapsedMs / extendedDurationMs, 1.0);

      // Sort riders by position (same as leaderboard): finishing riders first, then DNF riders
      var sortedRiders = riders.Values
        .OrderBy(r => r.IsDNF ? 1 : 0) // Non-DNF riders first (0), DNF riders last (1)
        .ThenByDescending(r => r.TotalLaps)
        .ThenBy(r => r.TotalTime)
        .ToList();

      // Chart layout parameters
      const int margin = 20;
      const int riderBarHeight = 40;
      const int riderSpacing = 5;
      const int labelWidth = 120;
      var chartWidth = bounds.Width - margin * 2 - labelWidth;
      var chartHeight = sortedRiders.Count * (riderBarHeight + riderSpacing);

      // Set auto-scroll minimum size to enable scrolling when content is larger than panel
      var minContentHeight = chartHeight + margin * 2 + 50; // Extra 50px for current time indicator
      var minContentWidth = margin * 2 + labelWidth + chartWidth;
      panelLapChart.AutoScrollMinSize = new Size(minContentWidth, minContentHeight);

      // Draw title
      var titleFont = new Font("Arial", 14, FontStyle.Bold);
      var raceTimeElapsed = TimeSpan.FromMilliseconds(raceElapsedMs);
      var title = $"Lap Visualization - Race: {raceTimeElapsed:mm\\:ss} / {raceDuration:mm\\:ss}";
      g.DrawString(title, titleFont, Brushes.Black, margin, margin);
      titleFont.Dispose();

      var chartTop = margin + 60; // Increased space for current time indicator
      var barFont = new Font("Arial", 10);

      // Draw time scale at top
      DrawTimeScale(g, new Rectangle(margin + labelWidth, chartTop - 25, chartWidth, 20), extendedDurationMs, raceDurationMs);

      // Draw each rider's bar
      for (int i = 0; i < sortedRiders.Count; i++)
      {
        var rider = sortedRiders[i];
        var y = chartTop + i * (riderBarHeight + riderSpacing);
        var barRect = new Rectangle(margin + labelWidth, y, chartWidth, riderBarHeight);

        DrawRiderLapBar(g, rider, barRect, extendedDurationMs, raceDurationMs, i + 1, lapChartElements);

        // Draw rider label
        var labelRect = new Rectangle(margin, y, labelWidth - 10, riderBarHeight);
        var labelText = $"#{i + 1}: {rider.TagID}";
        var labelBrush = GetPositionBrush(i);

        // Highlight if this rider is selected
        if (selectedRiderId == rider.TagID)
        {
          var highlightRect = new Rectangle(labelRect.X - 3, labelRect.Y - 3,
            labelRect.Width + 6, labelRect.Height + 6);
          g.FillRectangle(Brushes.Yellow, highlightRect);
          g.DrawRectangle(new Pen(Color.Orange, 3), highlightRect);
        }

        g.FillRectangle(labelBrush, labelRect);
        g.DrawRectangle(Pens.Black, labelRect);

        // Add rider label as clickable element
        lapChartElements.Add(new LapChartElement
        {
          Bounds = labelRect,
          RiderId = rider.TagID,
          IsRider = true
        });

        var textBrush = i < 3 ? Brushes.Black : Brushes.White;
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(labelText, barFont, textBrush, labelRect, sf);
        sf.Dispose();
      }

      barFont.Dispose();

      // ===== DRAW LINES ON TOP OF EVERYTHING ELSE =====

      // Draw race progress line - thick and prominent
      var progressX = margin + labelWidth + (int)(chartWidth * raceProgressPercent);
      var progressPen = new Pen(Color.Red, 4) { DashStyle = System.Drawing.Drawing2D.DashStyle.Solid };

      // Draw progress line from top of time scale to bottom of chart
      g.DrawLine(progressPen, progressX, chartTop - 25, progressX, chartTop + chartHeight);

      // Add current time indicator at the top
      var currentTimeFont = new Font("Arial", 10, FontStyle.Bold);
      var elapsedTime = TimeSpan.FromMilliseconds(raceElapsedMs);
      var currentTimeText = $"NOW: {elapsedTime:mm\\:ss}";
      var timeTextSize = g.MeasureString(currentTimeText, currentTimeFont);
      var timeTextX = progressX - timeTextSize.Width / 2;
      var timeTextY = chartTop - 45;

      // Draw background for current time text
      var timeTextRect = new Rectangle((int)timeTextX - 3, (int)timeTextY - 2,
        (int)timeTextSize.Width + 6, (int)timeTextSize.Height + 4);
      g.FillRectangle(Brushes.Red, timeTextRect);
      g.DrawRectangle(Pens.Black, timeTextRect);
      g.DrawString(currentTimeText, currentTimeFont, Brushes.White, timeTextX, timeTextY);
      currentTimeFont.Dispose();

      // Add a semi-transparent overlay to the right of the progress line to show "future time"
      if (progressX < margin + labelWidth + chartWidth)
      {
        var futureRect = new Rectangle(progressX, chartTop,
          margin + labelWidth + chartWidth - progressX, chartHeight);
        var futureBrush = new SolidBrush(Color.FromArgb(30, 255, 0, 0));
        g.FillRectangle(futureBrush, futureRect);
        futureBrush.Dispose();
      }

      // Draw race end time line (when original race duration expires)
      var raceEndX = margin + labelWidth + (int)(chartWidth * (raceDurationMs / extendedDurationMs));
      if (raceEndX != progressX) // Only draw if different from current progress
      {
        var raceEndPen = new Pen(Color.Orange, 3) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        g.DrawLine(raceEndPen, raceEndX, chartTop - 25, raceEndX, chartTop + chartHeight);

        // Add race end time indicator
        var raceEndTimeFont = new Font("Arial", 9, FontStyle.Bold);
        var raceEndTimeText = $"TIME: {raceDuration:mm\\:ss}";
        var raceEndTextSize = g.MeasureString(raceEndTimeText, raceEndTimeFont);
        var raceEndTextX = raceEndX - raceEndTextSize.Width / 2;
        var raceEndTextY = chartTop - 45;

        // Offset if too close to current time indicator
        if (Math.Abs(raceEndTextX - timeTextX) < raceEndTextSize.Width)
        {
          raceEndTextY = chartTop - 25;
        }

        // Draw background for race end time text
        var raceEndTextRect = new Rectangle((int)raceEndTextX - 3, (int)raceEndTextY - 2,
          (int)raceEndTextSize.Width + 6, (int)raceEndTextSize.Height + 4);
        g.FillRectangle(Brushes.Orange, raceEndTextRect);
        g.DrawRectangle(Pens.Black, raceEndTextRect);
        g.DrawString(raceEndTimeText, raceEndTimeFont, Brushes.Black, raceEndTextX, raceEndTextY);

        raceEndPen.Dispose();
        raceEndTimeFont.Dispose();
      }

      progressPen.Dispose();

      // Draw hover tooltip if there's hovered lap info
      if (!string.IsNullOrEmpty(hoveredLapInfo))
      {
        var mousePos = panelLapChart.PointToClient(Cursor.Position);
        DrawTooltip(g, hoveredLapInfo, mousePos);
      }
    }
  }

  private void DrawTimeScale(Graphics g, Rectangle bounds, double extendedDurationMs, double raceDurationMs)
  {
    var font = new Font("Arial", 10, FontStyle.Bold);
    var pen = new Pen(Color.Black, 2);
    var lightPen = new Pen(Color.LightGray, 1);
    var extendedPen = new Pen(Color.Gray, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };

    // Draw background for better contrast
    g.FillRectangle(Brushes.White, bounds);
    g.DrawRectangle(Pens.Black, bounds);

    // Choose appropriate interval based on extended duration
    var totalMinutes = extendedDurationMs / 60000.0;
    double majorIntervalMs;
    double minorIntervalMs;

    if (totalMinutes <= 5)
    {
      majorIntervalMs = 1 * 60 * 1000; // 1 minute major, 30 second minor
      minorIntervalMs = 30 * 1000;
    }
    else if (totalMinutes <= 15)
    {
      majorIntervalMs = 2 * 60 * 1000; // 2 minute major, 1 minute minor
      minorIntervalMs = 1 * 60 * 1000;
    }
    else
    {
      majorIntervalMs = 5 * 60 * 1000; // 5 minute major, 1 minute minor
      minorIntervalMs = 1 * 60 * 1000;
    }

    // Draw major tick marks
    var majorIntervals = (int)(extendedDurationMs / majorIntervalMs) + 1;
    for (int i = 0; i <= majorIntervals; i++)
    {
      var timeMs = (double)(i * majorIntervalMs);
      if (timeMs > extendedDurationMs) timeMs = extendedDurationMs;

      var x = bounds.X + (int)(bounds.Width * (timeMs / extendedDurationMs));
      var minutes = timeMs / 60000;

      // Use different styling for time marks beyond race duration
      var isWithinRaceTime = timeMs <= raceDurationMs;
      var tickPen = isWithinRaceTime ? pen : extendedPen;
      var textBrush = isWithinRaceTime ? Brushes.Black : Brushes.Gray;

      // Draw major tick marks
      g.DrawLine(tickPen, x, bounds.Y, x, bounds.Y + bounds.Height);

      // Draw time labels with better visibility
      var timeText = minutes < 10 ? $"{minutes:F1}m" : $"{minutes:F0}m";
      var textSize = g.MeasureString(timeText, font);
      var textX = x - textSize.Width / 2;
      var textY = bounds.Y + 2;

      // Draw white background for text
      var bgBrush = isWithinRaceTime ? Brushes.White : Brushes.LightGray;
      g.FillRectangle(bgBrush, textX - 2, textY, textSize.Width + 4, textSize.Height);
      g.DrawString(timeText, font, textBrush, textX, textY);
    }

    // Draw minor tick marks
    var minorIntervals = (int)(extendedDurationMs / minorIntervalMs) + 1;
    for (int i = 0; i <= minorIntervals; i++)
    {
      var timeMs = (double)(i * minorIntervalMs);
      if (timeMs > extendedDurationMs) timeMs = extendedDurationMs;

      // Skip if this is a major tick mark
      if (timeMs % majorIntervalMs == 0) continue;

      var x = bounds.X + (int)(bounds.Width * (timeMs / extendedDurationMs));
      var isWithinRaceTime = timeMs <= raceDurationMs;
      var tickPen = isWithinRaceTime ? lightPen : extendedPen;

      g.DrawLine(tickPen, x, bounds.Y + bounds.Height - 5, x, bounds.Y + bounds.Height);
    }

    font.Dispose();
    pen.Dispose();
    lightPen.Dispose();
    extendedPen.Dispose();
  }

  private void DrawRiderLapBar(Graphics g, RiderInfo rider, Rectangle bounds, double extendedDurationMs, double raceDurationMs, int position, List<LapChartElement> elements)
  {
    // Background
    g.FillRectangle(Brushes.LightGray, bounds);
    g.DrawRectangle(Pens.Black, bounds);

    if (rider.Laps.Count == 0) return;

    var lapColors = GetLapColors();

    // Draw completed laps based on actual race timeline
    for (int i = 0; i < rider.Laps.Count; i++)
    {
      var lap = rider.Laps[i];

      // Calculate when this lap started and ended in race time
      DateTime lapStartTime;
      TimeSpan? lapDuration;

      if (i == 0)
      {
        // First lap starts at race start
        lapStartTime = raceStartTime!.Value;
        if (lap.LapTime == null)
        {
          // Calculate first lap time from race start to crossing
          lapDuration = lap.CrossingTime - raceStartTime.Value;
        }
        else
        {
          lapDuration = lap.LapTime;
        }
      }
      else
      {
        // Subsequent laps start when previous lap ended
        lapStartTime = rider.Laps[i - 1].CrossingTime;
        lapDuration = lap.LapTime;
      }

      if (!lapDuration.HasValue || lapDuration.Value.TotalMilliseconds <= 0)
        continue;

      // Calculate position in race timeline using extended duration
      var lapStartMs = (lapStartTime - raceStartTime!.Value).TotalMilliseconds;
      var lapDurationMs = lapDuration.Value.TotalMilliseconds;
      var lapEndMs = lapStartMs + lapDurationMs;

      // Don't clamp to race duration - let laps extend into the extended time area
      var lapStartX = bounds.X + (int)(bounds.Width * (lapStartMs / extendedDurationMs));
      var lapWidth = (int)(bounds.Width * (lapDurationMs / extendedDurationMs));

      var lapRect = new Rectangle(
        lapStartX,
        bounds.Y + 2,
        lapWidth,
        bounds.Height - 4
      );

      if (lapRect.Width > 0 && lapRect.X < bounds.Right && lapRect.Right > bounds.X)
      {
        var colorIndex = i % lapColors.Length;
        g.FillRectangle(new SolidBrush(lapColors[colorIndex]), lapRect);
        g.DrawRectangle(Pens.Black, lapRect);

        // Add lap rectangle as hoverable element
        elements.Add(new LapChartElement
        {
          Bounds = lapRect,
          RiderId = rider.TagID,
          LapNumber = i + 1,
          LapTime = lapDuration,
          IsRider = false
        });

        // Draw lap number if there's space
        if (lapRect.Width > 20)
        {
          var lapText = (i + 1).ToString();
          var font = new Font("Arial", 8, FontStyle.Bold);
          var textSize = g.MeasureString(lapText, font);
          var textX = lapRect.X + (lapRect.Width - textSize.Width) / 2;
          var textY = lapRect.Y + (lapRect.Height - textSize.Height) / 2;
          g.DrawString(lapText, font, Brushes.Black, textX, textY);
          font.Dispose();
        }
      }
    }

    // Draw predicted future laps
    if (rider.PredictedLapTime.HasValue && rider.Laps.Count > 0)
    {
      var lastLapEndTime = rider.Laps.Last().CrossingTime;
      var predictedLapMs = rider.PredictedLapTime.Value.TotalMilliseconds;
      var lapNumber = rider.TotalLaps + 1;
      var currentPredictedTime = lastLapEndTime;

      // Calculate maximum laps to display based on race completion rules
      int maxLapsToShow = CalculateMaxLapsForRaceCompletion(rider);

      while (currentPredictedTime < raceStartTime!.Value.AddMilliseconds(extendedDurationMs) && lapNumber <= maxLapsToShow)
      {
        var lapStartMs = (currentPredictedTime - raceStartTime.Value).TotalMilliseconds;
        var lapEndMs = lapStartMs + predictedLapMs;

        var lapStartX = bounds.X + (int)(bounds.Width * (lapStartMs / extendedDurationMs));
        var lapWidth = (int)(bounds.Width * (predictedLapMs / extendedDurationMs));

        var lapRect = new Rectangle(
          lapStartX,
          bounds.Y + 2,
          lapWidth,
          bounds.Height - 4
        );

        if (lapRect.Width > 0 && lapRect.X < bounds.Right && lapRect.Right > bounds.X)
        {
          // Use different styling for laps before and after original race time
          var baseColor = lapColors[(lapNumber - 1) % lapColors.Length];
          var isAfterRaceTime = lapStartMs > raceDurationMs;

          Color lapColor;
          if (isAfterRaceTime)
          {
            // Laps after race time - use more transparent color
            lapColor = Color.FromArgb(80, baseColor.R, baseColor.G, baseColor.B);
          }
          else
          {
            // Laps during race time - normal transparency
            lapColor = Color.FromArgb(128, baseColor.R, baseColor.G, baseColor.B);
          }

          var brush = new SolidBrush(lapColor);
          g.FillRectangle(brush, lapRect);

          // Dashed border for predicted laps
          var pen = new Pen(baseColor, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
          g.DrawRectangle(pen, lapRect);

          brush.Dispose();
          pen.Dispose();

          // Draw predicted lap number
          if (lapRect.Width > 20)
          {
            var lapText = lapNumber.ToString();
            var font = new Font("Arial", 8, FontStyle.Italic);
            var textSize = g.MeasureString(lapText, font);
            var textX = lapRect.X + (lapRect.Width - textSize.Width) / 2;
            var textY = lapRect.Y + (lapRect.Height - textSize.Height) / 2;
            g.DrawString(lapText, font, Brushes.Gray, textX, textY);
            font.Dispose();
          }
        }

        currentPredictedTime = currentPredictedTime.AddMilliseconds(predictedLapMs);
        lapNumber++;
      }
    }

    // Draw statistics text
    var statsFont = new Font("Arial", 8);
    var stats = $"Laps: {rider.TotalLaps}";
    if (rider.BestLapTime.HasValue)
      stats += $" | Best: {rider.BestLapTime.Value:mm\\:ss}";
    if (rider.PredictedLapTime.HasValue)
      stats += $" | Pred: {rider.PredictedLapTime.Value:mm\\:ss}";

    g.DrawString(stats, statsFont, Brushes.Black, bounds.X + 5, bounds.Y + bounds.Height + 2);
    statsFont.Dispose();
  }

  private Color[] GetLapColors()
  {
    return new Color[]
    {
      Color.FromArgb(70, 130, 180),   // Steel Blue
      Color.FromArgb(255, 165, 0),    // Orange
      Color.FromArgb(50, 205, 50),    // Lime Green
      Color.FromArgb(255, 69, 0),     // Red Orange
      Color.FromArgb(138, 43, 226),   // Blue Violet
      Color.FromArgb(255, 215, 0),    // Gold
      Color.FromArgb(220, 20, 60),    // Crimson
      Color.FromArgb(0, 191, 255),    // Deep Sky Blue
      Color.FromArgb(154, 205, 50),   // Yellow Green
      Color.FromArgb(255, 20, 147)    // Deep Pink
    };
  }

  private Brush GetPositionBrush(int position)
  {
    return position switch
    {
      0 => new SolidBrush(Color.Gold),
      1 => new SolidBrush(Color.Silver),
      2 => new SolidBrush(Color.FromArgb(205, 127, 50)), // Bronze
      _ => new SolidBrush(Color.DarkGray)
    };
  }

  private void PanelLapChart_MouseClick(object? sender, MouseEventArgs e)
  {
    if (e.Button != MouseButtons.Left) return;

    // Adjust mouse position for scroll offset
    var adjustedLocation = new Point(e.Location.X - panelLapChart.AutoScrollPosition.X,
                                   e.Location.Y - panelLapChart.AutoScrollPosition.Y);

    // Find which element was clicked
    var clickedElement = lapChartElements.FirstOrDefault(elem => elem.Bounds.Contains(adjustedLocation));
    if (clickedElement != null && clickedElement.IsRider)
    {
      selectedRiderId = clickedElement.RiderId;
      ShowRiderDetails(clickedElement.RiderId);
      panelLapChart.Invalidate(); // Redraw to show selection
    }
  }

  private void PanelLapChart_MouseMove(object? sender, MouseEventArgs e)
  {
    // Adjust mouse position for scroll offset
    var adjustedLocation = new Point(e.Location.X - panelLapChart.AutoScrollPosition.X,
                                   e.Location.Y - panelLapChart.AutoScrollPosition.Y);

    // Find which element is being hovered
    var hoveredElement = lapChartElements.FirstOrDefault(elem => elem.Bounds.Contains(adjustedLocation));

    string? newHoverInfo = null;
    if (hoveredElement != null && !hoveredElement.IsRider && hoveredElement.LapTime.HasValue)
    {
      newHoverInfo = $"Lap {hoveredElement.LapNumber}: {hoveredElement.LapTime.Value:mm\\:ss\\.fff}";
    }

    if (newHoverInfo != hoveredLapInfo)
    {
      hoveredLapInfo = newHoverInfo;
      panelLapChart.Invalidate(); // Redraw to show/hide tooltip
    }

    // Change cursor when hovering over clickable elements
    panelLapChart.Cursor = hoveredElement != null ? Cursors.Hand : Cursors.Default;
  }

  private void PanelLapChart_MouseLeave(object? sender, EventArgs e)
  {
    if (hoveredLapInfo != null)
    {
      hoveredLapInfo = null;
      panelLapChart.Invalidate(); // Hide tooltip
    }
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
        details.AppendLine($"Total Time: {rider.TotalTime:hh\\:mm\\:ss}");
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
      if (otherRider.TagID == riderId) continue;

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

  private int CalculatePositionAtLap(string riderId, int lapNumber, List<RiderInfo> riderSnapshot)
  {
    // Find all riders who had completed at least 'lapNumber' laps
    // and determine this rider's position among them based on when they completed that lap

    var targetRider = riderSnapshot.FirstOrDefault(r => r.TagID == riderId);
    if (targetRider == null) return 999;

    var riderLap = targetRider.Laps.FirstOrDefault(l => l.LapNumber == lapNumber);
    if (riderLap == null) return 999; // Should not happen

    var ridersAtThisLap = new List<(string Id, DateTime CompletionTime, int TotalLapsAtTime)>();

    foreach (var otherRider in riderSnapshot)
    {
      var otherRiderLap = otherRider.Laps.FirstOrDefault(l => l.LapNumber == lapNumber);

      if (otherRiderLap != null)
      {
        // Count how many laps this rider had when they completed this lap
        var lapsAtTime = otherRider.Laps.Count(l => l.CrossingTime <= otherRiderLap.CrossingTime);
        ridersAtThisLap.Add((otherRider.TagID, otherRiderLap.CrossingTime, lapsAtTime));
      }
    }

    // Sort by laps completed (desc) then by completion time (asc)
    ridersAtThisLap.Sort((a, b) =>
    {
      var lapComparison = b.TotalLapsAtTime.CompareTo(a.TotalLapsAtTime);
      if (lapComparison != 0) return lapComparison;
      return a.CompletionTime.CompareTo(b.CompletionTime);
    });

    // Find position
    for (int i = 0; i < ridersAtThisLap.Count; i++)
    {
      if (ridersAtThisLap[i].Id == riderId)
      {
        return i + 1; // 1-based position
      }
    }

    return 999; // Fallback
  }

  private void DrawTooltip(Graphics g, string text, Point mousePosition)
  {
    if (string.IsNullOrEmpty(text)) return;

    var font = new Font("Arial", 10, FontStyle.Bold);
    var textSize = g.MeasureString(text, font);

    // Position tooltip near mouse but ensure it stays within bounds
    var tooltipX = mousePosition.X + 10;
    var tooltipY = mousePosition.Y - 30;

    if (tooltipX + (int)textSize.Width > panelLapChart.Width)
      tooltipX = mousePosition.X - (int)textSize.Width - 10;
    if (tooltipY < 0)
      tooltipY = mousePosition.Y + 20;

    var tooltipRect = new Rectangle(
      tooltipX - 5,
      tooltipY - 3,
      (int)textSize.Width + 10,
      (int)textSize.Height + 6);

    // Draw tooltip background with shadow
    var shadowRect = new Rectangle(tooltipRect.X + 2, tooltipRect.Y + 2,
      tooltipRect.Width, tooltipRect.Height);
    g.FillRectangle(Brushes.Gray, shadowRect);

    g.FillRectangle(Brushes.LightYellow, tooltipRect);
    g.DrawRectangle(Pens.Black, tooltipRect);

    // Draw text
    g.DrawString(text, font, Brushes.Black, tooltipX, tooltipY);

    font.Dispose();
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
      Invoke(new Action(UpdateRaceStartControls));
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
    var actualRaceDuration = actualRaceFinishTime - raceStartTime!.Value;

    // Count DNF riders
    var dnfRiders = riders.Values.Where(r => r.IsDNF).ToList();
    var finishedRiders = riders.Values.Where(r => !r.IsDNF).Count();

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
  /// Calculates the extended duration for the chart to show rider predictions beyond the race end time
  /// </summary>
  private double CalculateExtendedChartDuration(double raceDurationMs)
  {
    // Extend the chart duration beyond race time to show predicted finishes
    // Add approximately 25% more time or at least 5 minutes, whichever is greater
    var extensionMs = Math.Max(raceDurationMs * 0.25, 5 * 60 * 1000); // 25% or 5 minutes minimum
    return raceDurationMs + extensionMs;
  }

  /// <summary>
  /// Calculates the maximum number of laps a rider should complete for race completion display
  /// </summary>
  private int CalculateMaxLapsForRaceCompletion(RiderInfo rider)
  {
    // If race has finished and this rider has a final allowed lap, use that
    if (rider.FinalAllowedLap != int.MaxValue)
    {
      return rider.FinalAllowedLap;
    }

    // If race is still ongoing or no specific limit set, allow reasonable prediction
    // Limit to current laps + a reasonable number of additional laps (e.g., 10)
    return rider.TotalLaps + 10;
  }

  /// <summary>
  /// Event handler for the Set Additional Laps button
  /// </summary>
  private void buttonSetAdditionalLaps_Click(object sender, EventArgs e)
  {
    additionalLapsAfterTimeExpiry = (int)numericUpDownAdditionalLaps.Value;
    AddMessage($"⚙️ Additional laps after time expiry set to: {additionalLapsAfterTimeExpiry}");

    // If race has already finished in time mode, update the target
    if (raceTimeExpired && targetLapsToFinishRace > 0)
    {        // Recalculate target laps based on new setting (exclude DNF riders from leader calculation)
      var currentLeader = riders.Values
        .Where(r => !r.IsDNF)
        .OrderByDescending(r => r.TotalLaps)
        .ThenBy(r => r.TotalTime)
        .FirstOrDefault();

      if (currentLeader != null && leaderLapsAtTimeExpiry > 0)
      {
        // Calculate target: leader's current lap (in progress when time expired) + additional laps
        var leaderCurrentLapWhenTimeExpired = leaderLapsAtTimeExpiry + 1;
        targetLapsToFinishRace = leaderCurrentLapWhenTimeExpired + additionalLapsAfterTimeExpiry;
        var lapsText = additionalLapsAfterTimeExpiry == 1 ? "lap" : "laps";
        AddMessage($"🏁 Updated race finish target to {targetLapsToFinishRace} laps (leader was on lap {leaderCurrentLapWhenTimeExpired} when time expired + {additionalLapsAfterTimeExpiry} additional {lapsText})");
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

  #region Lap Progression Tab

  private DataGridView? dataGridViewLapProgression;
  private Button? buttonRefreshProgression;

  private void CreateLapProgressionTab()
  {
    // Create the Lap Progression tab page
    var tabPage = new TabPage("Lap Progression");

    // Create the DataGridView for showing lap progression
    dataGridViewLapProgression = new DataGridView
    {
      Dock = DockStyle.Fill,
      ReadOnly = true,
      AllowUserToAddRows = false,
      AllowUserToDeleteRows = false,
      AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None, // Changed to None for manual control
      SelectionMode = DataGridViewSelectionMode.FullRowSelect,
      MultiSelect = false,
      ScrollBars = ScrollBars.Both, // Ensure scrollbars are available
      AllowUserToResizeColumns = true
    };

    // Create refresh button
    buttonRefreshProgression = new Button
    {
      Text = "Refresh Progression",
      Size = new Size(150, 30),
      Location = new Point(10, 10)
    };
    buttonRefreshProgression.Click += ButtonRefreshProgression_Click;

    // Create panel to hold the button
    var topPanel = new Panel
    {
      Height = 50,
      Dock = DockStyle.Top
    };
    topPanel.Controls.Add(buttonRefreshProgression);

    // Add controls to tab page
    tabPage.Controls.Add(dataGridViewLapProgression);
    tabPage.Controls.Add(topPanel);

    // Add tab to the tab control (insert after Lap Chart tab - index 4)
    tabControl.TabPages.Insert(5, tabPage);

    // Initialize the DataGridView columns
    InitializeLapProgressionGrid();
  }

  private void InitializeLapProgressionGrid()
  {
    if (dataGridViewLapProgression == null) return;

    dataGridViewLapProgression.Columns.Clear();
    dataGridViewLapProgression.Columns.Add("RiderId", "Rider");
    dataGridViewLapProgression.Columns.Add("Lap1", "Lap 1");
    dataGridViewLapProgression.Columns.Add("Lap2", "Lap 2");
    dataGridViewLapProgression.Columns.Add("Lap3", "Lap 3");
    dataGridViewLapProgression.Columns.Add("Lap4", "Lap 4");
    dataGridViewLapProgression.Columns.Add("Lap5", "Lap 5");
    dataGridViewLapProgression.Columns.Add("Status", "Status");

    // Set column properties
    var riderIdColumn = dataGridViewLapProgression.Columns["RiderId"];
    if (riderIdColumn != null)
    {
      riderIdColumn.Width = 120; // Increased width for better visibility
      riderIdColumn.Frozen = true; // Keep TagID column always visible when scrolling
      riderIdColumn.Resizable = DataGridViewTriState.False; // Prevent user from resizing
      riderIdColumn.MinimumWidth = 120; // Ensure minimum width
    }

    var statusColumn = dataGridViewLapProgression.Columns["Status"];
    if (statusColumn != null)
    {
      statusColumn.Width = 100;
      statusColumn.Resizable = DataGridViewTriState.True;
    }

    // Set lap columns to have consistent width and allow scrolling
    for (int i = 1; i <= 5; i++)
    {
      var lapColumn = dataGridViewLapProgression.Columns[$"Lap{i}"];
      if (lapColumn != null)
      {
        lapColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        lapColumn.Width = 150; // Fixed width for lap columns
        lapColumn.Resizable = DataGridViewTriState.True;
      }
    }
  }

  private void ButtonRefreshProgression_Click(object? sender, EventArgs e)
  {
    UpdateLapProgressionDisplay();
  }

  private void UpdateLapProgressionDisplay()
  {
    if (dataGridViewLapProgression == null) return;

    if (InvokeRequired)
    {
      Invoke(new Action(UpdateLapProgressionDisplay));
      return;
    }

    // Create a snapshot of rider data outside of UI operations to avoid deadlocks
    List<RiderInfo> riderSnapshot;
    bool raceFinishedSnapshot;
    bool waitingForFinalLapsSnapshot;

    lock (ridersLock)
    {
      if (riders.Count == 0) return;

      // Create a quick snapshot of the data we need
      riderSnapshot = riders.Values.ToList();
      raceFinishedSnapshot = raceFinished;
      waitingForFinalLapsSnapshot = waitingForFinalLaps;
    }

    try
    {
      dataGridViewLapProgression.SuspendLayout();
      dataGridViewLapProgression.Rows.Clear();

      // Determine maximum laps to show
      var maxLaps = riderSnapshot.Max(r => r.TotalLaps);
      maxLaps = Math.Max(maxLaps, 5); // Show at least 5 laps

      // Update columns if needed
      EnsureLapProgressionColumns(maxLaps);

      // Sort riders by their final position (finishing riders first, then DNF)
      var sortedRiders = riderSnapshot
        .OrderBy(r => r.IsDNF ? 1 : 0) // Non-DNF first
        .ThenByDescending(r => r.TotalLaps)
        .ThenBy(r => r.TotalTime)
        .ToList();

      foreach (var rider in sortedRiders)
      {
        var row = new List<object> { rider.TagID };

        // Add position for each completed lap
        for (int lap = 1; lap <= maxLaps; lap++)
        {
          if (lap <= rider.TotalLaps)
          {
            // Calculate what position this rider was in when they completed this lap
            var position = CalculatePositionAtLapFromSnapshot(rider, lap, riderSnapshot);
            var lapTime = GetLapTimeFromRider(rider, lap);

            // Calculate position change from previous lap
            string positionChangeArrow = "";
            string lapTimeChangeArrow = "";
            Color cellBackColor = Color.LightBlue; // Default neutral color for maintained position

            if (lap > 1)
            {
              var previousPosition = CalculatePositionAtLapFromSnapshot(rider, lap - 1, riderSnapshot);
              int positionChange = previousPosition - position; // Positive = improved (lower position number)

              // Check lap time improvement
              var previousLapTime = GetLapTimeFromRider(rider, lap - 1);
              bool lapTimeImproved = false;
              bool lapTimeWorsened = false;

              if (lapTime.HasValue && previousLapTime.HasValue)
              {
                var timeDifference = lapTime.Value.TotalMilliseconds - previousLapTime.Value.TotalMilliseconds;
                if (timeDifference < 0) // Any improvement, even 1ms faster
                {
                  lapTimeImproved = true;
                  lapTimeChangeArrow = "⚡"; // Fast lap indicator
                }
                else if (timeDifference > 0) // Any degradation, even 1ms slower
                {
                  lapTimeWorsened = true;
                  lapTimeChangeArrow = "🐌"; // Slow lap indicator
                }
              }

              // Determine cell color based on position AND lap time changes
              if (positionChange > 0)
              {
                // Moved up in positions
                positionChangeArrow = " ↑"; // Improved position
                cellBackColor = Color.LightGreen;
              }
              else if (positionChange < 0)
              {
                // Moved down in positions
                positionChangeArrow = " ↓"; // Lost position
                cellBackColor = Color.LightPink;
              }
              else // Position maintained
              {
                // Use lap time performance for color when position unchanged
                if (lapTimeImproved)
                {
                  cellBackColor = Color.LightCyan; // Light cyan for faster lap time
                }
                else if (lapTimeWorsened)
                {
                  cellBackColor = Color.MistyRose; // Light pink for slower lap time
                }
                else
                {
                  cellBackColor = Color.LightBlue; // Neutral for similar lap time
                }
              }
            }

            string cellValue = $"P{position}{positionChangeArrow}{lapTimeChangeArrow}";
            if (lapTime.HasValue)
            {
              cellValue += $"\n{lapTime.Value:mm\\:ss\\.fff}";
            }

            row.Add(new { Value = cellValue, BackColor = cellBackColor });
          }
          else
          {
            row.Add(new { Value = "", BackColor = Color.White }); // No lap completed
          }
        }

        // Add status - determine if this specific rider has finished
        string status;
        if (rider.IsDNF)
        {
          status = "DNF";
        }
        else if (raceFinishedSnapshot)
        {
          status = "Finished";
        }
        else if (waitingForFinalLapsSnapshot)
        {
          // Check if this rider has completed their final allowed lap
          if (rider.FinalAllowedLap > 0 && rider.TotalLaps >= rider.FinalAllowedLap)
          {
            status = "Finished";
          }
          else
          {
            status = "Final Lap";
          }
        }
        else
        {
          status = "Racing";
        }

        row.Add(new { Value = status, BackColor = Color.White });

        // Create the row with just the values
        var rowValues = new object[row.Count];
        for (int i = 0; i < row.Count; i++)
        {
          if (row[i] is string str)
          {
            rowValues[i] = str; // Rider ID
          }
          else if (row[i] != null && row[i].GetType().GetProperty("Value") != null)
          {
            rowValues[i] = row[i].GetType().GetProperty("Value")?.GetValue(row[i]) ?? "";
          }
          else
          {
            rowValues[i] = row[i] ?? "";
          }
        }

        dataGridViewLapProgression.Rows.Add(rowValues);

        // Apply cell formatting
        var currentGridRow = dataGridViewLapProgression.Rows[dataGridViewLapProgression.Rows.Count - 1];

        // Apply individual cell background colors for position changes
        for (int i = 1; i < row.Count - 1; i++) // Skip rider ID (0) and status (last)
        {
          if (row[i] != null && row[i].GetType().GetProperty("BackColor") != null)
          {
            var backColor = (Color)(row[i].GetType().GetProperty("BackColor")?.GetValue(row[i]) ?? Color.White);
            if (i < currentGridRow.Cells.Count)
            {
              currentGridRow.Cells[i].Style.BackColor = backColor;
            }
          }
        }

        // Make position text bold in each lap cell
        for (int i = 1; i < currentGridRow.Cells.Count - 1; i++) // Skip rider ID and status
        {
          if (!string.IsNullOrEmpty(currentGridRow.Cells[i].Value?.ToString()))
          {
            currentGridRow.Cells[i].Style.Font = new Font(currentGridRow.DefaultCellStyle.Font ?? dataGridViewLapProgression.DefaultCellStyle.Font, FontStyle.Bold);
          }
        }

        // Color code specific columns based on overall position (only rider ID and status columns)
        if (rider.IsDNF)
        {
          // Apply DNF styling to rider ID and status columns only
          currentGridRow.Cells[0].Style.BackColor = Color.LightGray; // Rider ID
          currentGridRow.Cells[0].Style.ForeColor = Color.DarkRed;
          currentGridRow.Cells[currentGridRow.Cells.Count - 1].Style.BackColor = Color.LightGray; // Status
          currentGridRow.Cells[currentGridRow.Cells.Count - 1].Style.ForeColor = Color.DarkRed;
        }
        else if (sortedRiders.IndexOf(rider) == 0)
        {
          // Leader styling to rider ID and status columns only
          currentGridRow.Cells[0].Style.BackColor = Color.Gold;
          currentGridRow.Cells[currentGridRow.Cells.Count - 1].Style.BackColor = Color.Gold;
        }
        else if (sortedRiders.IndexOf(rider) == 1)
        {
          // 2nd place styling to rider ID and status columns only
          currentGridRow.Cells[0].Style.BackColor = Color.Silver;
          currentGridRow.Cells[currentGridRow.Cells.Count - 1].Style.BackColor = Color.Silver;
        }
        else if (sortedRiders.IndexOf(rider) == 2)
        {
          // 3rd place styling to rider ID and status columns only
          currentGridRow.Cells[0].Style.BackColor = Color.FromArgb(205, 127, 50);
          currentGridRow.Cells[currentGridRow.Cells.Count - 1].Style.BackColor = Color.FromArgb(205, 127, 50);
        }
      }
    }
    catch (Exception ex)
    {
      AddMessage($"Error updating lap progression display: {ex.Message}");
    }
    finally
    {
      dataGridViewLapProgression.ResumeLayout();
    }
  }

  private void EnsureLapProgressionColumns(int maxLaps)
  {
    if (dataGridViewLapProgression == null) return;

    // Remove existing lap columns (keep RiderId and Status)
    var columnsToRemove = dataGridViewLapProgression.Columns.Cast<DataGridViewColumn>()
      .Where(c => c.Name.StartsWith("Lap") && c.Name != "Status")
      .ToList();

    foreach (var col in columnsToRemove)
    {
      dataGridViewLapProgression.Columns.Remove(col);
    }

    // Add lap columns
    for (int i = 1; i <= maxLaps; i++)
    {
      var lapColumn = new DataGridViewTextBoxColumn
      {
        Name = $"Lap{i}",
        HeaderText = $"Lap {i}",
        DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter },
        Width = 150, // Fixed width for lap columns
        Resizable = DataGridViewTriState.True
      };

      // Insert before Status column
      var statusColumn = dataGridViewLapProgression.Columns["Status"];
      if (statusColumn != null)
      {
        var statusColumnIndex = statusColumn.Index;
        dataGridViewLapProgression.Columns.Insert(statusColumnIndex, lapColumn);
      }
      else
      {
        dataGridViewLapProgression.Columns.Add(lapColumn);
      }
    }

    // Ensure RiderID column properties are maintained after column changes
    var riderIdColumn = dataGridViewLapProgression.Columns["RiderId"];
    if (riderIdColumn != null)
    {
      riderIdColumn.Width = 120;
      riderIdColumn.Frozen = true; // Keep TagID column always visible when scrolling
      riderIdColumn.Resizable = DataGridViewTriState.False;
      riderIdColumn.MinimumWidth = 120;
    }

    // Ensure Status column properties are maintained
    var statusCol = dataGridViewLapProgression.Columns["Status"];
    if (statusCol != null)
    {
      statusCol.Width = 100;
      statusCol.Resizable = DataGridViewTriState.True;
    }
  }

  private int CalculatePositionAtLap(string riderId, int lapNumber)
  {
    // Find all riders who had completed at least 'lapNumber' laps
    // and determine this rider's position among them based on when they completed that lap

    var riderLap = riders[riderId].Laps.FirstOrDefault(l => l.LapNumber == lapNumber);
    if (riderLap == null) return 999; // Should not happen

    var ridersAtThisLap = new List<(string Id, DateTime CompletionTime, int TotalLapsAtTime)>();

    foreach (var kvp in riders)
    {
      var otherRider = kvp.Value;
      var otherRiderLap = otherRider.Laps.FirstOrDefault(l => l.LapNumber == lapNumber);

      if (otherRiderLap != null)
      {
        // Count how many laps this rider had when they completed this lap
        var lapsAtTime = otherRider.Laps.Count(l => l.CrossingTime <= otherRiderLap.CrossingTime);
        ridersAtThisLap.Add((kvp.Key, otherRiderLap.CrossingTime, lapsAtTime));
      }
    }

    // Sort by laps completed (desc) then by completion time (asc)
    ridersAtThisLap.Sort((a, b) =>
    {
      var lapComparison = b.TotalLapsAtTime.CompareTo(a.TotalLapsAtTime);
      if (lapComparison != 0) return lapComparison;
      return a.CompletionTime.CompareTo(b.CompletionTime);
    });

    // Find position
    for (int i = 0; i < ridersAtThisLap.Count; i++)
    {
      if (ridersAtThisLap[i].Id == riderId)
      {
        return i + 1; // 1-based position
      }
    }

    return 999; // Fallback
  }

  private TimeSpan? GetLapTime(RiderInfo rider, int lapNumber)
  {
    var lap = rider.Laps.FirstOrDefault(l => l.LapNumber == lapNumber);
    return lap?.LapTime;
  }

  #endregion

  /// <summary>
  /// Calculate position at lap using snapshot data (no locking needed)
  /// </summary>
  private int CalculatePositionAtLapFromSnapshot(RiderInfo targetRider, int lapNumber, List<RiderInfo> riderSnapshot)
  {
    var riderLap = targetRider.Laps.FirstOrDefault(l => l.LapNumber == lapNumber);
    if (riderLap == null) return 999; // Should not happen

    var ridersAtThisLap = new List<(string Id, DateTime CompletionTime, int TotalLapsAtTime)>();

    foreach (var otherRider in riderSnapshot)
    {
      var otherRiderLap = otherRider.Laps.FirstOrDefault(l => l.LapNumber == lapNumber);

      if (otherRiderLap != null)
      {
        // Count how many laps this rider had when they completed this lap
        var lapsAtTime = otherRider.Laps.Count(l => l.CrossingTime <= otherRiderLap.CrossingTime);
        ridersAtThisLap.Add((otherRider.TagID, otherRiderLap.CrossingTime, lapsAtTime));
      }
    }

    // Sort by laps completed (desc) then by completion time (asc)
    ridersAtThisLap.Sort((a, b) =>
    {
      var lapComparison = b.TotalLapsAtTime.CompareTo(a.TotalLapsAtTime);
      if (lapComparison != 0) return lapComparison;
      return a.CompletionTime.CompareTo(b.CompletionTime);
    });

    // Find position
    for (int i = 0; i < ridersAtThisLap.Count; i++)
    {
      if (ridersAtThisLap[i].Id == targetRider.TagID)
      {
        return i + 1; // 1-based position
      }
    }

    return 999; // Fallback
  }

  /// <summary>
  /// Get lap time from rider data directly (no dictionary access needed)
  /// </summary>
  private TimeSpan? GetLapTimeFromRider(RiderInfo rider, int lapNumber)
  {
    var lap = rider.Laps.FirstOrDefault(l => l.LapNumber == lapNumber);
    return lap?.LapTime;
  }

  private void RecordLapProgression(string riderId, int lapNumber, int position, TimeSpan raceTime)
  {
    var entry = new LapProgressionEntry
    {
      RiderId = riderId,
      LapNumber = lapNumber,
      Position = position,
      RaceTime = raceTime,
      CrossingTime = DateTime.Now,
      LapTime = riders.ContainsKey(riderId) ? riders[riderId].LastLapTime : null,
      IsDNF = riders.ContainsKey(riderId) && riders[riderId].IsDNF
    };

    lapProgressionHistory.Add(entry);
    lapProgressionNeedsUpdate = true;
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
    if (!riders.ContainsKey(riderId)) return 999;

    var targetRider = riders[riderId];
    var sortedRiders = riders.Values
      .Where(r => !r.IsDNF)
      .OrderByDescending(r => r.TotalLaps)
      .ThenBy(r => r.TotalTime)
      .ToList();

    for (int i = 0; i < sortedRiders.Count; i++)
    {
      if (sortedRiders[i].TagID == riderId)
      {
        return i + 1; // 1-based position
      }
    }

    return 999; // Not found or DNF
  }

}
