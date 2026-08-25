namespace CrossMgrInterface;

/// <summary>
/// Manages the Lap Progression tab functionality
/// </summary>
public class LapProgressionManager : IDisposable
{
  private DataGridView? _dataGridViewLapProgression;
  private Button? _buttonRefreshProgression;

  /// <summary>
  /// Raised when the user asks for a manual rebuild of the progression grid.
  /// </summary>
  public event Action? RefreshRequested;

  private Font? _boldCellFont;

  private static readonly Color[] PodiumColors =
  {
    Color.Gold,
    Color.Silver,
    Color.FromArgb(205, 127, 50)
  };

  /// <summary>
  /// One bold font for the whole grid. This used to be allocated per cell and
  /// never disposed, leaking hundreds of GDI handles on every rebuild.
  /// </summary>
  private Font GetBoldCellFont()
  {
    var baseFont = _dataGridViewLapProgression?.DefaultCellStyle.Font ?? Control.DefaultFont;
    if (_boldCellFont == null || _boldCellFont.FontFamily != baseFont.FontFamily ||
        Math.Abs(_boldCellFont.Size - baseFont.Size) > 0.01f)
    {
      _boldCellFont?.Dispose();
      _boldCellFont = new Font(baseFont, FontStyle.Bold);
    }
    return _boldCellFont;
  }

  public void Dispose()
  {
    _boldCellFont?.Dispose();
    _boldCellFont = null;
  }

  /// <summary>
  /// Creates and initializes the Lap Progression tab
  /// </summary>
  public TabPage CreateLapProgressionTab()
  {
    // Create the Lap Progression tab page
    var tabPage = new TabPage("Lap Progression") { Name = "tabPageLapProgression" };

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
    if (riderSnapshot.Count == 0)
    {
      _rows = new List<LapProgressionRowData>();
      _dataGridViewLapProgression.RowCount = 0;
      return;
    }

    raceFinishedSnapshot = raceFinished;
    waitingForFinalLapsSnapshot = waitingForFinalLaps;

    try
    {
      _dataGridViewLapProgression.SuspendLayout();

      var rows = new List<LapProgressionRowData>(riderSnapshot.Count);

      // Find the maximum number of laps completed by any rider
      int maxLaps = riderSnapshot.Max(r => r.TotalLaps);
      maxLaps = Math.Max(maxLaps, 5); // Show at least 5 laps

      // Update columns if needed
      EnsureLapProgressionColumns(maxLaps);

      // Every rider's position at every lap, computed once for the whole grid
      // instead of twice per cell.
      var positionTable = PositionCalculator.BuildLapPositionTable(riderSnapshot, maxLaps);

      // Sort riders by their final position (finishing riders first, then DNF)
      var sortedRiders = PositionCalculator.GetSortedRidersFromSnapshot(riderSnapshot);


      for (int rank = 0; rank < sortedRiders.Count; rank++)
      {
        var rider = sortedRiders[rank];
        var hasSplitLaps = rider.Laps.Any(l => l.IsSplitLap);

        // Lap number -> lap, so the per-cell lookups below are not linear scans.
        var lapsByNumber = new Dictionary<int, RiderLap>(rider.Laps.Count);
        foreach (var lap in rider.Laps)
          lapsByNumber[lap.LapNumber] = lap;

        var riderDisplayName = rider.Label;

        // Add split lap indicator if needed
        if (hasSplitLaps)
          riderDisplayName += " *";

        // A concrete type rather than an anonymous one: the values used to be
        // read back out with GetType().GetProperty(...).GetValue(...) three times
        // per cell.
        var cells = new ProgressionCell[maxLaps];

        for (int lap = 1; lap <= maxLaps; lap++)
        {
          if (lap > rider.TotalLaps)
          {
            cells[lap - 1] = new ProgressionCell("", Color.White, false);
            continue;
          }

          var position = PositionCalculator.PositionAtLap(positionTable, rider.TagID, lap);
          lapsByNumber.TryGetValue(lap, out var lapData);
          var lapTime = lapData?.LapTime;

          string positionChangeArrow = "";
          string lapTimeChangeArrow = "";
          Color cellBackColor = Color.LightBlue; // Default neutral color for maintained position

          if (lap > 1)
          {
            var previousPosition = PositionCalculator.PositionAtLap(positionTable, rider.TagID, lap - 1);
            int positionChange = previousPosition - position; // Positive = improved (lower position number)

            lapsByNumber.TryGetValue(lap - 1, out var previousLapData);
            var previousLapTime = previousLapData?.LapTime;

            bool lapTimeImproved = false;
            bool lapTimeWorsened = false;

            if (lapTime.HasValue && previousLapTime.HasValue)
            {
              var timeDifference = lapTime.Value.TotalMilliseconds - previousLapTime.Value.TotalMilliseconds;
              if (timeDifference < 0) // Any improvement, even 1ms faster
              {
                lapTimeImproved = true;
                lapTimeChangeArrow = "\u26a1"; // Fast lap indicator
              }
              else if (timeDifference > 0) // Any degradation, even 1ms slower
              {
                lapTimeWorsened = true;
                lapTimeChangeArrow = "\U0001f40c"; // Slow lap indicator
              }
            }

            // Determine cell color based on position AND lap time changes
            if (positionChange > 0)
            {
              positionChangeArrow = " \u2191"; // Improved position
              cellBackColor = Color.LightGreen;
            }
            else if (positionChange < 0)
            {
              positionChangeArrow = " \u2193"; // Lost position
              cellBackColor = Color.LightPink;
            }
            else if (lapTimeImproved)
            {
              cellBackColor = Color.LightCyan; // Faster lap at the same position
            }
            else if (lapTimeWorsened)
            {
              cellBackColor = Color.MistyRose; // Slower lap at the same position
            }
          }

          var isSplitLap = lapData?.IsSplitLap ?? false;
          string cellValue = $"P{position} {positionChangeArrow}{lapTimeChangeArrow}";

          if (lapTime.HasValue)
          {
            var splitIndicator = isSplitLap ? "*" : "";
            cellValue += $"\n{lapTime.Value:mm\\:ss\\.fff}{splitIndicator}";
          }

          cells[lap - 1] = new ProgressionCell(cellValue, cellBackColor, isSplitLap);
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
          status = rider.FinalAllowedLap > 0 && rider.TotalLaps >= rider.FinalAllowedLap
            ? "Finished"
            : "Final Lap";
        }
        else
        {
          status = "Racing";
        }

        // Build the row; the grid is in virtual mode and pulls what it paints.
        var rowBack = Color.Empty;
        var rowFore = Color.Empty;
        if (rider.IsDNF)
        {
          rowBack = Color.LightGray;
          rowFore = Color.DarkRed;
        }
        else if (rank < PodiumColors.Length)
        {
          rowBack = PodiumColors[rank];
        }

        rows.Add(new LapProgressionRowData
        {
          RiderName = riderDisplayName,
          Status = status,
          LapCells = cells,
          EdgeBackColor = rowBack,
          EdgeForeColor = rowFore
        });
      }

      _rows = rows;

      // Virtual mode: the control asks for the rows it is painting, so a large
      // field costs no more than a small one.
      if (_dataGridViewLapProgression.RowCount != rows.Count)
        _dataGridViewLapProgression.RowCount = rows.Count;

      _dataGridViewLapProgression.Invalidate();
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

  private List<LapProgressionRowData> _rows = new();

  /// <summary>Supplies cell text for the rows the grid is painting.</summary>
  private void Grid_CellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
  {
    if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
    var row = _rows[e.RowIndex];

    // Column layout is: rider name, one column per lap, then status.
    if (e.ColumnIndex == 0) { e.Value = row.RiderName; return; }

    var statusIndex = (_dataGridViewLapProgression?.ColumnCount ?? 0) - 1;
    if (e.ColumnIndex == statusIndex) { e.Value = row.Status; return; }

    var lapIndex = e.ColumnIndex - 1;
    e.Value = lapIndex >= 0 && lapIndex < row.LapCells.Length ? row.LapCells[lapIndex].Text : "";
  }

  /// <summary>Applies the position and lap-time colouring at paint time.</summary>
  private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
  {
    if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
    var row = _rows[e.RowIndex];

    var statusIndex = (_dataGridViewLapProgression?.ColumnCount ?? 0) - 1;

    // Rider name and status carry the podium or DNF shading.
    if (e.ColumnIndex == 0 || e.ColumnIndex == statusIndex)
    {
      if (!row.EdgeBackColor.IsEmpty) e.CellStyle.BackColor = row.EdgeBackColor;
      if (!row.EdgeForeColor.IsEmpty) e.CellStyle.ForeColor = row.EdgeForeColor;
      return;
    }

    var lapIndex = e.ColumnIndex - 1;
    if (lapIndex < 0 || lapIndex >= row.LapCells.Length) return;

    var cell = row.LapCells[lapIndex];
    e.CellStyle.BackColor = cell.BackColor;
    if (cell.IsSplitLap) e.CellStyle.ForeColor = Color.Red;
    if (cell.Text.Length > 0) e.CellStyle.Font = GetBoldCellFont();
  }

  private void InitializeLapProgressionGrid()
  {
    if (_dataGridViewLapProgression == null) return;

    // Virtual mode: no row data lives in the control.
    _dataGridViewLapProgression.VirtualMode = true;
    _dataGridViewLapProgression.CellValueNeeded -= Grid_CellValueNeeded;
    _dataGridViewLapProgression.CellValueNeeded += Grid_CellValueNeeded;
    _dataGridViewLapProgression.CellFormatting -= Grid_CellFormatting;
    _dataGridViewLapProgression.CellFormatting += Grid_CellFormatting;

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

    _dataGridViewLapProgression.Columns.Add("RiderId", "Rider Name");
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

        // Alignment only: the grid's own font already applies, and allocating a
        // Font per column leaked a GDI handle for every lap of the race.
        newColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

        // Insert before the Status column (which should be last)
        var statusColumnIndex = _dataGridViewLapProgression.Columns["Status"]?.Index ?? _dataGridViewLapProgression.Columns.Count;
        _dataGridViewLapProgression.Columns.Insert(statusColumnIndex, newColumn);
      }
    }
  }

  private void ButtonRefreshProgression_Click(object? sender, EventArgs e)
  {
    RefreshRequested?.Invoke();
  }

}

/// <summary>
/// One cell of the lap progression grid: what to show, how to tint it, and
/// whether the lap it represents came from a split.
/// </summary>
public readonly record struct ProgressionCell(string Text, Color BackColor, bool IsSplitLap);

/// <summary>
/// One prepared row of the lap progression grid.
///
/// The grid runs in virtual mode: rows are built into a plain list and the
/// control asks for the handful it is painting, so the cost no longer grows
/// with the size of the field.
/// </summary>
public sealed class LapProgressionRowData
{
  public string RiderName { get; init; } = "";
  public string Status { get; init; } = "";

  /// <summary>One entry per lap column.</summary>
  public ProgressionCell[] LapCells { get; init; } = Array.Empty<ProgressionCell>();

  /// <summary>Podium or DNF shading for the rider-name and status columns.</summary>
  public Color EdgeBackColor { get; init; } = Color.Empty;
  public Color EdgeForeColor { get; init; } = Color.Empty;
}
