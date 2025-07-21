namespace CrossMgrInterface;

/// <summary>
/// Manages the Lap Progression tab functionality
/// </summary>
public class LapProgressionManager
{
  private DataGridView? _dataGridViewLapProgression;
  private Button? _buttonRefreshProgression;
  private readonly List<LapProgressionEntry> _lapProgressionHistory = new();
  private bool _lapProgressionNeedsUpdate = false;

  public bool NeedsUpdate
  {
    get => _lapProgressionNeedsUpdate;
    set => _lapProgressionNeedsUpdate = value;
  }

  /// <summary>
  /// Creates and initializes the Lap Progression tab
  /// </summary>
  public TabPage CreateLapProgressionTab()
  {
    // Create the Lap Progression tab page
    var tabPage = new TabPage("Lap Progression");

    // Create the DataGridView for showing lap progression
    _dataGridViewLapProgression = new DataGridView
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
    _buttonRefreshProgression = new Button
    {
      Text = "Refresh Progression",
      Size = new Size(150, 30),
      Location = new Point(10, 10)
    };
    _buttonRefreshProgression.Click += ButtonRefreshProgression_Click;

    // Create panel to hold the button
    var topPanel = new Panel
    {
      Height = 50,
      Dock = DockStyle.Top
    };
    topPanel.Controls.Add(_buttonRefreshProgression);

    // Add controls to tab page
    tabPage.Controls.Add(_dataGridViewLapProgression);
    tabPage.Controls.Add(topPanel);

    // Initialize the DataGridView columns
    InitializeLapProgressionGrid();

    return tabPage;
  }

  /// <summary>
  /// Record lap progression after a rider completes a lap
  /// </summary>
  public void RecordLapProgression(string riderId, int lapNumber, int position, TimeSpan raceTime, Dictionary<string, RiderInfo> riders)
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

    _lapProgressionHistory.Add(entry);
    _lapProgressionNeedsUpdate = true;
  }

  /// <summary>
  /// Update the lap progression display
  /// </summary>
  public void UpdateLapProgressionDisplay(List<RiderInfo> riderSnapshot, bool raceFinished, bool waitingForFinalLaps, Control parentControl)
  {
    if (_dataGridViewLapProgression == null) return;

    if (parentControl.InvokeRequired)
    {
      parentControl.Invoke(new Action(() => UpdateLapProgressionDisplay(riderSnapshot, raceFinished, waitingForFinalLaps, parentControl)));
      return;
    }

    // Create a snapshot of rider data outside of UI operations to avoid deadlocks
    bool raceFinishedSnapshot;
    bool waitingForFinalLapsSnapshot;

    // Data is already a snapshot, so no locking needed
    if (riderSnapshot.Count == 0) return;

    raceFinishedSnapshot = raceFinished;
    waitingForFinalLapsSnapshot = waitingForFinalLaps;

    try
    {
      _dataGridViewLapProgression.SuspendLayout();

      // Clear existing rows
      _dataGridViewLapProgression.Rows.Clear();

      // Find the maximum number of laps completed by any rider
      int maxLaps = riderSnapshot.Max(r => r.TotalLaps);
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
        var hasSplitLaps = rider.Laps.Any(l => l.IsSplitLap);
        var riderDisplayName = hasSplitLaps ? $"{rider.TagID} *" : rider.TagID;
        var row = new List<object> { riderDisplayName };

        // Add position for each completed lap
        for (int lap = 1; lap <= maxLaps; lap++)
        {
          if (lap <= rider.TotalLaps)
          {
            // Calculate what position this rider was in when they completed this lap
            var position = PositionCalculator.CalculatePositionAtLapFromSnapshot(rider, lap, riderSnapshot);
            var lapTime = GetLapTimeFromRider(rider, lap);

            // Calculate position change from previous lap
            string positionChangeArrow = "";
            string lapTimeChangeArrow = "";
            Color cellBackColor = Color.LightBlue; // Default neutral color for maintained position

            if (lap > 1)
            {
              var previousPosition = PositionCalculator.CalculatePositionAtLapFromSnapshot(rider, lap - 1, riderSnapshot);
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

            string cellValue = $"P{position} {positionChangeArrow}{lapTimeChangeArrow}";

            // Check if this lap is a split lap
            var lapData = rider.Laps.FirstOrDefault(l => l.LapNumber == lap);
            var isSplitLap = lapData?.IsSplitLap ?? false;

            if (lapTime.HasValue)
            {
              var splitIndicator = isSplitLap ? "*" : "";
              cellValue += $"\n{lapTime.Value:mm\\:ss\\.fff}{splitIndicator}";
            }

            row.Add(new { Value = cellValue, BackColor = cellBackColor, IsSplitLap = isSplitLap });
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

        _dataGridViewLapProgression.Rows.Add(rowValues);

        // Apply cell formatting
        var currentGridRow = _dataGridViewLapProgression.Rows[_dataGridViewLapProgression.Rows.Count - 1];

        // Apply individual cell background colors for position changes
        for (int i = 1; i < row.Count - 1; i++) // Skip rider ID (0) and status (last)
        {
          if (row[i] != null && row[i].GetType().GetProperty("BackColor") != null)
          {
            var backColor = (Color)(row[i].GetType().GetProperty("BackColor")?.GetValue(row[i]) ?? Color.White);
            var isSplitLap = (bool)(row[i].GetType().GetProperty("IsSplitLap")?.GetValue(row[i]) ?? false);

            if (i < currentGridRow.Cells.Count)
            {
              currentGridRow.Cells[i].Style.BackColor = backColor;

              // Add special formatting for split laps
              if (isSplitLap)
              {
                currentGridRow.Cells[i].Style.ForeColor = Color.Red;
                currentGridRow.Cells[i].Style.Font = new Font(currentGridRow.DefaultCellStyle.Font ?? _dataGridViewLapProgression.DefaultCellStyle.Font, FontStyle.Bold);
              }
            }
          }
        }

        // Make position text bold in each lap cell
        for (int i = 1; i < currentGridRow.Cells.Count - 1; i++) // Skip rider ID and status
        {
          if (!string.IsNullOrEmpty(currentGridRow.Cells[i].Value?.ToString()))
          {
            currentGridRow.Cells[i].Style.Font = new Font(currentGridRow.DefaultCellStyle.Font ?? _dataGridViewLapProgression.DefaultCellStyle.Font, FontStyle.Bold);
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

      _lapProgressionNeedsUpdate = false;
    }
    catch (Exception ex)
    {
      // Log error but don't crash the application
      System.Diagnostics.Debug.WriteLine($"Error updating lap progression: {ex.Message}");
    }
    finally
    {
      _dataGridViewLapProgression.ResumeLayout();
    }
  }

  private void InitializeLapProgressionGrid()
  {
    if (_dataGridViewLapProgression == null) return;

    _dataGridViewLapProgression.Columns.Clear();

    // Configure general grid appearance
    _dataGridViewLapProgression.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
    _dataGridViewLapProgression.DefaultCellStyle.SelectionBackColor = Color.FromArgb(51, 153, 255);
    _dataGridViewLapProgression.DefaultCellStyle.SelectionForeColor = Color.White;
    _dataGridViewLapProgression.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(70, 130, 180);
    _dataGridViewLapProgression.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
    _dataGridViewLapProgression.ColumnHeadersDefaultCellStyle.Font = new Font(_dataGridViewLapProgression.Font, FontStyle.Bold);
    _dataGridViewLapProgression.EnableHeadersVisualStyles = false;
    _dataGridViewLapProgression.GridColor = Color.LightGray;
    _dataGridViewLapProgression.RowHeadersVisible = false;

    _dataGridViewLapProgression.Columns.Add("RiderId", "Rider");
    _dataGridViewLapProgression.Columns.Add("Lap1", "Lap 1");
    _dataGridViewLapProgression.Columns.Add("Lap2", "Lap 2");
    _dataGridViewLapProgression.Columns.Add("Lap3", "Lap 3");
    _dataGridViewLapProgression.Columns.Add("Lap4", "Lap 4");
    _dataGridViewLapProgression.Columns.Add("Lap5", "Lap 5");
    _dataGridViewLapProgression.Columns.Add("Status", "Status");

    // Set column properties
    var riderIdColumn = _dataGridViewLapProgression.Columns["RiderId"];
    if (riderIdColumn != null)
    {
      riderIdColumn.Width = 180; // Increased width to accommodate up to 24-character tag IDs
      riderIdColumn.Frozen = true; // Keep TagID column always visible when scrolling
      riderIdColumn.Resizable = DataGridViewTriState.False; // Prevent user from resizing
      riderIdColumn.MinimumWidth = 180; // Ensure minimum width for long tag IDs
      riderIdColumn.DefaultCellStyle.Font = new Font(_dataGridViewLapProgression.Font, FontStyle.Bold);
      riderIdColumn.DefaultCellStyle.BackColor = Color.FromArgb(230, 230, 230);
    }

    var statusColumn = _dataGridViewLapProgression.Columns["Status"];
    if (statusColumn != null)
    {
      statusColumn.Width = 100;
      statusColumn.Resizable = DataGridViewTriState.True;
      statusColumn.DefaultCellStyle.Font = new Font(_dataGridViewLapProgression.Font, FontStyle.Bold);
      statusColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
    }

    // Set lap columns to have consistent width and allow scrolling
    for (int i = 1; i <= 5; i++)
    {
      var lapColumn = _dataGridViewLapProgression.Columns[$"Lap{i}"];
      if (lapColumn != null)
      {
        lapColumn.Width = 140; // Slightly wider for position and time
        lapColumn.Resizable = DataGridViewTriState.True;
        lapColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        lapColumn.DefaultCellStyle.Font = new Font(_dataGridViewLapProgression.Font, FontStyle.Regular);
      }
    }
  }

  private void EnsureLapProgressionColumns(int maxLaps)
  {
    if (_dataGridViewLapProgression == null) return;

    // Check if we need to add more lap columns
    int currentLapColumns = _dataGridViewLapProgression.Columns.Count - 2; // Subtract Rider and Status columns

    if (maxLaps > currentLapColumns)
    {
      for (int i = currentLapColumns + 1; i <= maxLaps; i++)
      {
        var newColumn = new DataGridViewTextBoxColumn
        {
          Name = $"Lap{i}",
          HeaderText = $"Lap {i}",
          Width = 140, // Consistent with initial columns
          Resizable = DataGridViewTriState.True
        };

        // Apply consistent styling
        newColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        newColumn.DefaultCellStyle.Font = new Font(_dataGridViewLapProgression.Font, FontStyle.Regular);

        // Insert before the Status column (which should be last)
        var statusColumnIndex = _dataGridViewLapProgression.Columns["Status"]?.Index ?? _dataGridViewLapProgression.Columns.Count;
        _dataGridViewLapProgression.Columns.Insert(statusColumnIndex, newColumn);
      }
    }
  }

  private void ButtonRefreshProgression_Click(object? sender, EventArgs e)
  {
    _lapProgressionNeedsUpdate = true;
  }

  /// <summary>
  /// Get lap time from rider data directly (no dictionary access needed)
  /// </summary>
  private TimeSpan? GetLapTimeFromRider(RiderInfo rider, int lapNumber)
  {
    var lap = rider.Laps.FirstOrDefault(l => l.LapNumber == lapNumber);
    return lap?.LapTime;
  }
}
