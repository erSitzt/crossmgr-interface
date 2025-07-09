using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace CrossMgrInterface;

// Class to track rider lap information
public class RiderLap
{
  public string TagID { get; set; } = "";
  public DateTime CrossingTime { get; set; }
  public int LapNumber { get; set; }
  public TimeSpan? LapTime { get; set; } // Time for this lap (null for first lap)
}

public class RiderInfo
{
  public string TagID { get; set; } = "";
  public List<RiderLap> Laps { get; set; } = new List<RiderLap>();
  public DateTime FirstCrossing { get; set; }
  public DateTime LastCrossing { get; set; }
  public int TotalLaps => Laps.Count;
  public TimeSpan? BestLapTime => Laps.Where(l => l.LapTime.HasValue).Min(l => l.LapTime);
  public TimeSpan? LastLapTime => Laps.LastOrDefault()?.LapTime;
  public TimeSpan TotalTime => LastCrossing - FirstCrossing;

  // Predicted next lap time based on recent performance
  public TimeSpan? PredictedLapTime
  {
    get
    {
      var recentLaps = Laps.Where(l => l.LapTime.HasValue).TakeLast(3).ToList();
      if (recentLaps.Count == 0) return null;

      // Use weighted average of recent laps (more weight to recent laps)
      double totalWeight = 0;
      double weightedSum = 0;

      for (int i = 0; i < recentLaps.Count; i++)
      {
        double weight = i + 1; // More recent laps get higher weight
        weightedSum += recentLaps[i].LapTime!.Value.TotalMilliseconds * weight;
        totalWeight += weight;
      }

      return TimeSpan.FromMilliseconds(weightedSum / totalWeight);
    }
  }

  // Estimated time for next finish line crossing
  public DateTime? EstimatedNextCrossing
  {
    get
    {
      if (PredictedLapTime == null) return null;
      return LastCrossing + PredictedLapTime.Value;
    }
  }
}

public partial class Form1 : Form
{
  private TcpListener? tcpListener;
  private bool isListening = false;
  private readonly List<TcpClient> connectedClients = new();
  private readonly object clientsLock = new object();

  // Rider tracking
  private readonly Dictionary<string, RiderInfo> riders = new();
  private readonly object ridersLock = new object();

  // Race tracking
  private DateTime? raceStartTime = null;
  private string lastTagID = "None";
  private DateTime lastTagTime = DateTime.MinValue;
  private bool ridersDisplayNeedsUpdate = false;
  private bool manualStartMode = false;
  private bool raceStarted = false;

  // Race duration settings
  private TimeSpan raceDuration = TimeSpan.FromMinutes(20); // Default 20 minutes
  private DateTime? raceEndTime = null;
  private bool fiveMinuteWarningShown = false;
  private bool oneMinuteWarningShown = false;

  // Tag filtering
  private string tagFilterPrefix = "";
  private bool tagFilterEnabled = false;
  private int filteredTagCount = 0;

  // Lap chart visualization
  private bool lapChartNeedsUpdate = false;
  private DateTime lastProgressLineUpdate = DateTime.MinValue;
  private string? selectedRiderId = null;
  private string? hoveredLapInfo = null;
  private readonly List<LapChartElement> lapChartElements = new();

  // Helper class to track clickable areas in the lap chart
  private class LapChartElement
  {
    public Rectangle Bounds { get; set; }
    public string RiderId { get; set; } = "";
    public int LapNumber { get; set; }
    public TimeSpan? LapTime { get; set; }
    public bool IsRider { get; set; } // true for rider label, false for individual lap
  }

  public Form1()
  {
    InitializeComponent();
    this.Load += Form1_Load;
    InitializeRidersDataGrid();

    // Add event handler for tab changes
    tabControl.SelectedIndexChanged += TabControl_SelectedIndexChanged;
  }

  private void TabControl_SelectedIndexChanged(object? sender, EventArgs e)
  {
    // If switching to Riders tab and we need an update, do it now
    if (tabControl.SelectedIndex == 1 && ridersDisplayNeedsUpdate)
    {
      ridersDisplayNeedsUpdate = false;
      UpdateRidersDisplay();
    }
    // If switching to Lap Chart tab and we need an update, do it now
    else if (tabControl.SelectedIndex == 3 && lapChartNeedsUpdate)
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

    // Initialize tag filter controls
    textBoxTagFilter.PlaceholderText = "e.g., RIDER, 1000, BIKE (comma-separated)";
    checkBoxFilterEnabled.Checked = false;
    tagFilterEnabled = false;
    AddMessage("🔍 Tag filter: Disabled (all tags will be processed)");

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

  private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
  {
    StopTcpListener();
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
        AddMessage($"Client connected from: {clientEndpoint}");
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
          AddMessage($"[{clientEndpoint}] RAW: '{allData}' (hex: {string.Join("", Encoding.ASCII.GetBytes(allData).Select(b => b.ToString("X2")))})");
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
      AddMessage($"Error handling client {clientEndpoint}: {ex.Message}");
    }
    finally
    {
      lock (clientsLock)
      {
        connectedClients.Remove(client);
      }

      client.Close();
      AddMessage($"Client disconnected: {clientEndpoint}");
      UpdateConnectionCount();
    }
  }

  private async void ProcessMessage(string message, NetworkStream stream, string clientEndpoint)
  {
    try
    {
      AddMessage($"[{clientEndpoint}] Received: '{message}' (Length: {message.Length})");

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

          AddMessage($"[{clientEndpoint}] Sent: {response.TrimEnd()}");
        }
      }
      else if (message.StartsWith("S0000"))
      {
        // Setup command - acknowledge
        AddMessage($"[{clientEndpoint}] Setup command received");
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
        AddMessage($"[{clientEndpoint}] 📋 Reader identification: {readerName} (full: {message})");

        // According to CrossMgr protocol (based on Impinj2JChip.py), the client sends identifier
        // and then waits for the server to send GT command. Adding realistic delay to match
        // the expected timing where the reader waits for the server to respond.
        // Note: Client has 2-second socket timeout, so delay must be well under 2 seconds.
        AddMessage($"[{clientEndpoint}] ⏳ Waiting 500ms before sending GT command (protocol timing)...");

        // Use Task.Delay to avoid blocking the UI thread
        _ = Task.Run(async () =>
        {
          AddMessage($"[{clientEndpoint}] ⏳ Starting 500ms delay timer...");
          await Task.Delay(500); // 500ms delay - well under the 2-second client timeout
          AddMessage($"[{clientEndpoint}] ⏰ Delay complete, sending GT command now...");

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

              AddMessage($"[{clientEndpoint}] 📤 Sent GT command to initialize reader (after delay)");
            }
            else
            {
              AddMessage($"[{clientEndpoint}] ❌ Cannot send GT - client disconnected during delay");
            }
          }
          catch (Exception ex)
          {
            AddMessage($"[{clientEndpoint}] Error sending delayed GT command: {ex.Message}");
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
        AddMessage($"[{clientEndpoint}] {filteredMessage}");
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

      AddMessage($"[{clientEndpoint}] {formattedMessage}");

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
    lock (ridersLock)
    {
      // If in manual start mode and race hasn't started yet, ignore tags
      if (manualStartMode && !raceStarted)
      {
        // Create a dummy lap that won't be processed
        return new RiderLap { TagID = tagID, CrossingTime = crossingTime, LapNumber = 0 };
      }

      // Track race start time on first crossing (only if not manual start mode)
      if (raceStartTime == null && !manualStartMode)
      {
        raceStartTime = crossingTime;
        raceEndTime = raceStartTime.Value + raceDuration;
        raceStarted = true;
        UpdateRaceStartControls();
        AddMessage($"🏁 Race started! Duration: {raceDuration.TotalMinutes} minutes, End time: {raceEndTime:HH:mm:ss}");
        AddMessage($"🎯 Predicted total laps will be calculated based on leader performance.");
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
          LastCrossing = crossingTime
        };

        var firstLap = new RiderLap
        {
          TagID = tagID,
          CrossingTime = crossingTime,
          LapNumber = 1,
          LapTime = null // No lap time for first crossing
        };

        riders[tagID].Laps.Add(firstLap);

        // Mark that riders display needs update (don't update immediately to avoid freezing)
        ridersDisplayNeedsUpdate = true;
        lapChartNeedsUpdate = true;
        return firstLap;
      }
      else
      {
        // Subsequent crossing - calculate lap time
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

        // Mark that riders display needs update (don't update immediately to avoid freezing)
        ridersDisplayNeedsUpdate = true;
        lapChartNeedsUpdate = true;
        return newLap;
      }
    }
  }

  private void DisplayRiderSummary(string tagID)
  {
    lock (ridersLock)
    {
      if (riders.ContainsKey(tagID))
      {
        var rider = riders[tagID];
        var bestLap = rider.BestLapTime?.ToString(@"mm\:ss\.fff") ?? "N/A";
        var lastLap = rider.LastLapTime?.ToString(@"mm\:ss\.fff") ?? "N/A";
        var totalTime = rider.TotalTime.ToString(@"mm\:ss\.fff");

        AddMessage($"📊 Rider {tagID}: {rider.TotalLaps} laps | Best: {bestLap} | Last: {lastLap} | Total: {totalTime}");
      }
    }
  }

  private void DisplayAllRidersSummary()
  {
    lock (ridersLock)
    {
      if (riders.Count == 0)
      {
        AddMessage("📊 No riders tracked yet.");
        return;
      }

      AddMessage("📊 === RIDERS SUMMARY ===");

      var sortedRiders = riders.Values
        .OrderByDescending(r => r.TotalLaps)
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

        AddMessage($"📊 #{position}: Tag {rider.TagID} | {rider.TotalLaps} laps | Best: {bestLap} | Avg: {avgLapStr} | Total: {totalTime}");
        position++;
      }

      AddMessage("📊 ==================");
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
      lastTagID = "None";
      lastTagTime = DateTime.MinValue;
      ridersDisplayNeedsUpdate = true;
      lapChartNeedsUpdate = true;

      // Reset warning flags
      fiveMinuteWarningShown = false;
      oneMinuteWarningShown = false;

      // Reset race start controls
      UpdateRaceStartControls();

      // Reset filter counter
      filteredTagCount = 0;

      AddMessage("🗑️ All rider data cleared. Race reset.");
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
    if (InvokeRequired)
    {
      Invoke(new Action<string>(AddMessage), message);
      return;
    }

    string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
    listBoxMessages.Items.Add($"[{timestamp}] {message}");

    // Auto-scroll to bottom
    listBoxMessages.TopIndex = Math.Max(0, listBoxMessages.Items.Count - 1);

    // Limit number of items to prevent memory issues
    if (listBoxMessages.Items.Count > 1000)
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
    if (tabControl.SelectedIndex != 1) // Riders tab is index 1
      return;

    lock (ridersLock)
    {
      try
      {
        // Suspend layout to improve performance during bulk updates
        dataGridViewRiders.SuspendLayout();

        // Clear existing rows
        dataGridViewRiders.Rows.Clear();

        if (riders.Count == 0)
          return;

        // Sort riders by laps (descending) then by total time (ascending)
        var sortedRiders = riders.Values
          .OrderByDescending(r => r.TotalLaps)
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
          if (rider.EstimatedNextCrossing.HasValue)
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

          // Add row to grid
          dataGridViewRiders.Rows.Add(
            (i + 1).ToString(),  // Position
            rider.TagID,
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

          // Color coding for positions
          var row = dataGridViewRiders.Rows[dataGridViewRiders.Rows.Count - 1];
          if (i == 0)
            row.DefaultCellStyle.BackColor = Color.Gold;  // 1st place
          else if (i == 1)
            row.DefaultCellStyle.BackColor = Color.Silver;  // 2nd place
          else if (i == 2)
            row.DefaultCellStyle.BackColor = Color.FromArgb(205, 127, 50);  // 3rd place (bronze)

          // Highlight overdue riders
          if (timeToNextStr == "Overdue")
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
  }

  private void UpdateStatisticsDisplay()
  {
    if (InvokeRequired)
    {
      Invoke(new Action(UpdateStatisticsDisplay));
      return;
    }

    lock (ridersLock)
    {
      // Update race time
      if (raceStartTime.HasValue)
      {
        var elapsed = DateTime.Now - raceStartTime.Value;
        labelRaceTime.Text = $"Race Time: {elapsed:hh\\:mm\\:ss}";
      }
      else
      {
        labelRaceTime.Text = "Race Time: Not Started";
      }

      // Update rider count
      labelTotalRiders.Text = $"Total Riders: {riders.Count}";

      // Update total laps
      var totalLaps = riders.Values.Sum(r => r.TotalLaps);
      labelTotalLaps.Text = $"Total Laps: {totalLaps}";

      // Update last tag info
      if (lastTagID != "None" && lastTagTime != DateTime.MinValue)
      {
        var timeSince = DateTime.Now - lastTagTime;
        labelLastTag.Text = $"Last Tag: {lastTagID} ({timeSince.TotalSeconds:F0}s ago)";
      }
      else
      {
        labelLastTag.Text = "Last Tag: None";
      }

      // Show next expected crossing (only if on Race Statistics tab)
      if (tabControl.SelectedIndex == 2) // Race Statistics tab
      {
        ShowNextExpectedCrossing();

        // Update race end time
        if (raceEndTime.HasValue)
        {
          labelRaceEndTime.Text = $"Race End: {raceEndTime:HH:mm:ss}";

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
            AddMessage("🏁 RACE FINISHED!");
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
  }
  private void ShowNextExpectedCrossing()
  {
    lock (ridersLock)
    {
      // Find the rider expected to cross next
      var nextRider = riders.Values
        .Where(r => r.EstimatedNextCrossing.HasValue)
        .OrderBy(r => r.EstimatedNextCrossing!.Value)
        .FirstOrDefault();

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
  }

  private int CalculatePredictedLaps()
  {
    if (!raceStartTime.HasValue || !raceEndTime.HasValue)
      return 0;

    // Find the current leader
    var leader = riders.Values
      .OrderByDescending(r => r.TotalLaps)
      .ThenBy(r => r.TotalTime)
      .FirstOrDefault();

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

    // Update riders display if needed (but not more than once per second to avoid freezing)
    if (ridersDisplayNeedsUpdate)
    {
      ridersDisplayNeedsUpdate = false;
      UpdateRidersDisplay();
    }
    else if (tabControl.SelectedIndex == 1) // If on Riders tab, update predictions
    {
      // Update only the time-sensitive columns to keep predictions current
      UpdateRiderPredictions();
    }

    // Update lap chart if needed (but not more than once per second to avoid freezing)
    if (lapChartNeedsUpdate)
    {
      lapChartNeedsUpdate = false;
      if (tabControl.SelectedIndex == 3) // Only update if on Lap Chart tab
      {
        panelLapChart.Invalidate();
        lastProgressLineUpdate = DateTime.Now;
      }
    }
    else if (tabControl.SelectedIndex == 3) // If on Lap Chart tab, update progress line every 5 seconds
    {
      var timeSinceLastUpdate = DateTime.Now - lastProgressLineUpdate;
      if (timeSinceLastUpdate.TotalSeconds >= 5)
      {
        panelLapChart.Invalidate();
        lastProgressLineUpdate = DateTime.Now;
      }
    }
  }

  private void UpdateRiderPredictions()
  {
    if (InvokeRequired)
    {
      Invoke(new Action(UpdateRiderPredictions));
      return;
    }

    // Only update if we're on the Riders tab and have data
    if (tabControl.SelectedIndex != 1 || dataGridViewRiders.Rows.Count == 0)
      return;

    lock (ridersLock)
    {
      try
      {
        var sortedRiders = riders.Values
          .OrderByDescending(r => r.TotalLaps)
          .ThenBy(r => r.TotalTime)
          .ToList();

        for (int i = 0; i < Math.Min(sortedRiders.Count, dataGridViewRiders.Rows.Count); i++)
        {
          var rider = sortedRiders[i];
          var row = dataGridViewRiders.Rows[i];

          // Update next crossing prediction
          var nextCrossingStr = "N/A";
          var timeToNextStr = "N/A";
          if (rider.EstimatedNextCrossing.HasValue)
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

          // Update styling for overdue riders
          if (timeToNextStr == "Overdue")
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
  }

  private string GetLeaderPredictionInfo()
  {
    if (!raceStartTime.HasValue || !raceEndTime.HasValue)
      return "";

    // Find the current leader
    var leader = riders.Values
      .OrderByDescending(r => r.TotalLaps)
      .ThenBy(r => r.TotalTime)
      .FirstOrDefault();

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
      var raceElapsedMs = (DateTime.Now - raceStartTime.Value).TotalMilliseconds;
      var raceProgressPercent = Math.Min(raceElapsedMs / raceDurationMs, 1.0);

      // Sort riders by position (same as leaderboard)
      var sortedRiders = riders.Values
        .OrderByDescending(r => r.TotalLaps)
        .ThenBy(r => r.TotalTime)
        .ToList();

      // Chart layout parameters
      const int margin = 20;
      const int riderBarHeight = 40;
      const int riderSpacing = 5;
      const int labelWidth = 120;
      var chartWidth = bounds.Width - margin * 2 - labelWidth;
      var chartHeight = sortedRiders.Count * (riderBarHeight + riderSpacing);

      // Ensure panel is tall enough (add extra space for current time indicator)
      var minPanelHeight = chartHeight + margin * 2 + 50; // Extra 50px for current time indicator
      if (panelLapChart.Height < minPanelHeight)
      {
        panelLapChart.Height = minPanelHeight;
      }

      // Draw title
      var titleFont = new Font("Arial", 14, FontStyle.Bold);
      var title = $"Lap Visualization - Race: {raceElapsedMs / 60000:F1}min / {raceDuration.TotalMinutes:F0}min";
      g.DrawString(title, titleFont, Brushes.Black, margin, margin);
      titleFont.Dispose();

      var chartTop = margin + 60; // Increased space for current time indicator
      var barFont = new Font("Arial", 10);

      // Draw time scale at top
      DrawTimeScale(g, new Rectangle(margin + labelWidth, chartTop - 25, chartWidth, 20), raceDurationMs);

      // Draw race progress line - thick and prominent
      var progressX = margin + labelWidth + (int)(chartWidth * raceProgressPercent);
      var progressPen = new Pen(Color.Red, 4) { DashStyle = System.Drawing.Drawing2D.DashStyle.Solid };

      // Draw progress line from top of time scale to bottom of chart
      g.DrawLine(progressPen, progressX, chartTop - 25, progressX, chartTop + chartHeight);

      // Add current time indicator at the top
      var currentTimeFont = new Font("Arial", 10, FontStyle.Bold);
      var currentTimeText = $"NOW: {DateTime.Now:HH:mm:ss}";
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

      progressPen.Dispose();

      // Draw each rider's bar
      for (int i = 0; i < sortedRiders.Count; i++)
      {
        var rider = sortedRiders[i];
        var y = chartTop + i * (riderBarHeight + riderSpacing);
        var barRect = new Rectangle(margin + labelWidth, y, chartWidth, riderBarHeight);

        DrawRiderLapBar(g, rider, barRect, raceDurationMs, i + 1, lapChartElements);

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

      // Draw hover tooltip if there's hovered lap info
      if (!string.IsNullOrEmpty(hoveredLapInfo))
      {
        var mousePos = panelLapChart.PointToClient(Cursor.Position);
        DrawTooltip(g, hoveredLapInfo, mousePos);
      }
    }
  }

  private void DrawTimeScale(Graphics g, Rectangle bounds, double raceDurationMs)
  {
    var font = new Font("Arial", 10, FontStyle.Bold);
    var pen = new Pen(Color.Black, 2);
    var lightPen = new Pen(Color.LightGray, 1);

    // Draw background for better contrast
    g.FillRectangle(Brushes.White, bounds);
    g.DrawRectangle(Pens.Black, bounds);

    // Draw scale marks every 5 minutes
    var intervalMs = 5 * 60 * 1000; // 5 minutes
    var intervals = (int)(raceDurationMs / intervalMs) + 1;

    for (int i = 0; i <= intervals; i++)
    {
      var timeMs = (double)(i * intervalMs);
      if (timeMs > raceDurationMs) timeMs = raceDurationMs;

      var x = bounds.X + (int)(bounds.Width * (timeMs / raceDurationMs));
      var minutes = timeMs / 60000;

      // Draw major tick marks
      g.DrawLine(pen, x, bounds.Y, x, bounds.Y + bounds.Height);

      // Draw time labels with better visibility
      var timeText = $"{minutes:F0}m";
      var textSize = g.MeasureString(timeText, font);
      var textX = x - textSize.Width / 2;
      var textY = bounds.Y + 2;

      // Draw white background for text
      g.FillRectangle(Brushes.White, textX - 2, textY, textSize.Width + 4, textSize.Height);
      g.DrawString(timeText, font, Brushes.Black, textX, textY);
    }

    // Draw minor tick marks (every minute)
    var minorIntervalMs = 1 * 60 * 1000; // 1 minute
    var minorIntervals = (int)(raceDurationMs / minorIntervalMs) + 1;

    for (int i = 0; i <= minorIntervals; i++)
    {
      var timeMs = (double)(i * minorIntervalMs);
      if (timeMs > raceDurationMs) timeMs = raceDurationMs;

      // Skip if this is a major tick mark
      if (timeMs % (5 * 60 * 1000) == 0) continue;

      var x = bounds.X + (int)(bounds.Width * (timeMs / raceDurationMs));
      g.DrawLine(lightPen, x, bounds.Y + bounds.Height - 5, x, bounds.Y + bounds.Height);
    }

    font.Dispose();
    pen.Dispose();
    lightPen.Dispose();
  }

  private void DrawRiderLapBar(Graphics g, RiderInfo rider, Rectangle bounds, double raceDurationMs, int position, List<LapChartElement> elements)
  {
    // Background
    g.FillRectangle(Brushes.LightGray, bounds);
    g.DrawRectangle(Pens.Black, bounds);

    if (rider.Laps.Count == 0) return;

    var currentTime = 0.0;
    var lapColors = GetLapColors();

    // Draw completed laps
    for (int i = 0; i < rider.Laps.Count; i++)
    {
      var lap = rider.Laps[i];
      var lapDuration = lap.LapTime?.TotalMilliseconds ?? 0;

      if (i == 0 && lap.LapTime == null)
      {
        // First lap - use time from race start to first crossing
        lapDuration = (lap.CrossingTime - raceStartTime!.Value).TotalMilliseconds;
      }

      var lapWidth = (int)(bounds.Width * (lapDuration / raceDurationMs));
      var lapRect = new Rectangle(
        bounds.X + (int)(bounds.Width * (currentTime / raceDurationMs)),
        bounds.Y + 2,
        lapWidth,
        bounds.Height - 4
      );

      if (lapRect.Width > 0 && lapRect.X < bounds.Right)
      {
        var colorIndex = i % lapColors.Length;
        g.FillRectangle(new SolidBrush(lapColors[colorIndex]), lapRect);
        g.DrawRectangle(Pens.Black, lapRect);

        // Add lap rectangle as hoverable element
        var actualLapTime = i == 0 && lap.LapTime == null
          ? TimeSpan.FromMilliseconds(lapDuration)
          : lap.LapTime;

        if (actualLapTime.HasValue)
        {
          elements.Add(new LapChartElement
          {
            Bounds = lapRect,
            RiderId = rider.TagID,
            LapNumber = i + 1,
            LapTime = actualLapTime,
            IsRider = false
          });
        }

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

      currentTime += lapDuration;
    }

    // Draw predicted future laps
    if (rider.PredictedLapTime.HasValue && currentTime < raceDurationMs)
    {
      var predictedLapMs = rider.PredictedLapTime.Value.TotalMilliseconds;
      var lapNumber = rider.TotalLaps + 1;

      while (currentTime < raceDurationMs)
      {
        var remainingTime = raceDurationMs - currentTime;
        var lapDuration = Math.Min(predictedLapMs, remainingTime);

        var lapWidth = (int)(bounds.Width * (lapDuration / raceDurationMs));
        var lapRect = new Rectangle(
          bounds.X + (int)(bounds.Width * (currentTime / raceDurationMs)),
          bounds.Y + 2,
          lapWidth,
          bounds.Height - 4
        );

        if (lapRect.Width > 0 && lapRect.X < bounds.Right)
        {
          // Use a faded color for predicted laps
          var baseColor = lapColors[(lapNumber - 1) % lapColors.Length];
          var predictedColor = Color.FromArgb(128, baseColor.R, baseColor.G, baseColor.B);
          var brush = new SolidBrush(predictedColor);
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

        currentTime += lapDuration;
        lapNumber++;

        // Safety check to prevent infinite loop
        if (lapNumber > 1000) break;
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

    // Find which element was clicked
    var clickedElement = lapChartElements.FirstOrDefault(elem => elem.Bounds.Contains(e.Location));
    if (clickedElement != null && clickedElement.IsRider)
    {
      selectedRiderId = clickedElement.RiderId;
      ShowRiderDetails(clickedElement.RiderId);
      panelLapChart.Invalidate(); // Redraw to show selection
    }
  }

  private void PanelLapChart_MouseMove(object? sender, MouseEventArgs e)
  {
    // Find which element is being hovered
    var hoveredElement = lapChartElements.FirstOrDefault(elem => elem.Bounds.Contains(e.Location));

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
        details.AppendLine("Lap Times:");

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
          details.AppendLine($"  Lap {i + 1}: {lapTimeStr} ({lap.CrossingTime:HH:mm:ss})");
        }

        MessageBox.Show(details.ToString(), $"Lap Details - {riderId}",
          MessageBoxButtons.OK, MessageBoxIcon.Information);
      }
    }
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
    buttonStartRace.Enabled = manualStartMode && !raceStarted;

    if (raceStarted)
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
      UpdateRaceStartControls();
      AddMessage($"🏁 Race started manually at {raceStartTime.Value:HH:mm:ss}");

      // Reset warnings
      fiveMinuteWarningShown = false;
      oneMinuteWarningShown = false;
    }
  }
}
