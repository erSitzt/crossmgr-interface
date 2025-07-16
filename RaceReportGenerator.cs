using System.Drawing.Printing;
using System.Text;

namespace CrossMgrInterface;

/// <summary>
/// Generates and prints comprehensive race reports
/// </summary>
public class RaceReportGenerator
{
  private PrintDocument _printDocument;
  private RaceReportData? _reportData;
  private int _currentPage = 0;
  private int _totalPages = 0;
  private int _currentRiderIndex = 0; // Track which rider we're printing
  private float _headerHeight = 0; // Height of page header section

  public RaceReportGenerator()
  {
    _printDocument = new PrintDocument();
    _printDocument.PrintPage += PrintDocument_PrintPage;
  }

  /// <summary>
  /// Generates and shows print preview for race report
  /// </summary>
  public void ShowPrintPreview(Dictionary<string, RiderInfo> riders, DateTime? raceStartTime,
    DateTime? raceEndTime, TimeSpan raceDuration, bool raceFinished, string raceTitle = "Race Results")
  {
    _reportData = PrepareReportData(riders, raceStartTime, raceEndTime, raceDuration, raceFinished, raceTitle);
    _currentPage = 0;
    _currentRiderIndex = 0;

    using var printPreview = new PrintPreviewDialog();
    printPreview.Document = _printDocument;
    printPreview.WindowState = FormWindowState.Maximized;
    printPreview.ShowDialog();
  }

  /// <summary>
  /// Prints the race report directly
  /// </summary>
  public void PrintReport(Dictionary<string, RiderInfo> riders, DateTime? raceStartTime,
    DateTime? raceEndTime, TimeSpan raceDuration, bool raceFinished, string raceTitle = "Race Results")
  {
    _reportData = PrepareReportData(riders, raceStartTime, raceEndTime, raceDuration, raceFinished, raceTitle);
    _currentPage = 0;
    _currentRiderIndex = 0;

    using var printDialog = new PrintDialog();
    printDialog.Document = _printDocument;

    if (printDialog.ShowDialog() == DialogResult.OK)
    {
      _printDocument.Print();
    }
  }

  /// <summary>
  /// Exports race report to text file
  /// </summary>
  public void ExportToFile(Dictionary<string, RiderInfo> riders, DateTime? raceStartTime,
    DateTime? raceEndTime, TimeSpan raceDuration, bool raceFinished, string raceTitle = "Race Results")
  {
    _reportData = PrepareReportData(riders, raceStartTime, raceEndTime, raceDuration, raceFinished, raceTitle);
    _currentPage = 0;
    _currentRiderIndex = 0;

    using var saveDialog = new SaveFileDialog();
    saveDialog.Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
    saveDialog.DefaultExt = "txt";
    saveDialog.FileName = $"Race_Report_{DateTime.Now:yyyyMMdd_HHmmss}.txt";

    if (saveDialog.ShowDialog() == DialogResult.OK)
    {
      var reportText = GenerateTextReport();
      File.WriteAllText(saveDialog.FileName, reportText, Encoding.UTF8);
      MessageBox.Show($"Race report exported to:\n{saveDialog.FileName}", "Export Complete",
        MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
  }

  private RaceReportData PrepareReportData(Dictionary<string, RiderInfo> riders, DateTime? raceStartTime,
    DateTime? raceEndTime, TimeSpan raceDuration, bool raceFinished, string raceTitle)
  {
    var reportData = new RaceReportData
    {
      RaceTitle = raceTitle,
      RaceStartTime = raceStartTime,
      RaceEndTime = raceEndTime,
      RaceDuration = raceDuration,
      RaceFinished = raceFinished,
      GeneratedAt = DateTime.Now
    };

    // Sort riders by final position
    var sortedRiders = riders.Values
      .OrderBy(r => r.IsDNF ? 1 : 0) // Non-DNF first
      .ThenByDescending(r => r.TotalLaps)
      .ThenBy(r => r.TotalTime)
      .ToList();

    reportData.RiderResults = new List<RiderResult>();

    for (int i = 0; i < sortedRiders.Count; i++)
    {
      var rider = sortedRiders[i];
      var result = new RiderResult
      {
        Position = rider.IsDNF ? "DNF" : (i + 1).ToString(),
        TagID = rider.TagID,
        TotalLaps = rider.TotalLaps,
        TotalTime = rider.TotalTime,
        BestLapTime = rider.BestLapTime,
        AverageLapTime = CalculateAverageLapTime(rider),
        IsDNF = rider.IsDNF,
        LapTimes = rider.Laps.Select(l => new LapResult
        {
          LapNumber = l.LapNumber,
          LapTime = l.LapTime,
          CrossingTime = l.CrossingTime
        }).ToList()
      };

      // Calculate gap to leader if not leader and not DNF
      if (i > 0 && !rider.IsDNF && !sortedRiders[0].IsDNF)
      {
        var leader = sortedRiders[0];
        if (rider.TotalLaps == leader.TotalLaps)
        {
          // Same laps - time gap
          result.GapToLeader = rider.TotalTime - leader.TotalTime;
        }
        else
        {
          // Different laps - lap gap
          result.LapGapToLeader = leader.TotalLaps - rider.TotalLaps;
        }
      }

      reportData.RiderResults.Add(result);
    }

    // Calculate race statistics
    var finishedRiders = reportData.RiderResults.Where(r => !r.IsDNF).ToList();
    var dnfRiders = reportData.RiderResults.Where(r => r.IsDNF).ToList();

    reportData.RaceStatistics = new RaceStatistics
    {
      TotalRiders = reportData.RiderResults.Count,
      FinishedRiders = finishedRiders.Count,
      DNFRiders = dnfRiders.Count,
      TotalLapsCompleted = reportData.RiderResults.Sum(r => r.TotalLaps),
      FastestLap = finishedRiders.Where(r => r.BestLapTime.HasValue)
                                .OrderBy(r => r.BestLapTime ?? TimeSpan.MaxValue)
                                .FirstOrDefault(),
      ActualRaceDuration = finishedRiders.FirstOrDefault()?.TotalTime // Winner's total time
    };

    return reportData;
  }

  private void PrintDocument_PrintPage(object sender, PrintPageEventArgs e)
  {
    if (_reportData == null) return;

    var g = e.Graphics;
    var pageRect = e.PageBounds;
    var printableArea = e.MarginBounds;

    // Fonts
    var titleFont = new Font("Arial", 18, FontStyle.Bold);
    var headerFont = new Font("Arial", 12, FontStyle.Bold);
    var normalFont = new Font("Arial", 10);
    var smallFont = new Font("Arial", 8);

    float yPos = printableArea.Top;
    float leftMargin = printableArea.Left;
    float rightMargin = printableArea.Right;

    // Only draw title and race info on first page
    if (_currentPage == 0)
    {
      // Title
      var titleText = _reportData.RaceTitle ?? "Race Results";
      var titleSize = g.MeasureString(titleText ?? "Race Results", titleFont);
      g.DrawString(titleText, titleFont, Brushes.Black,
        leftMargin + (printableArea.Width - titleSize.Width) / 2, yPos);
      yPos += titleSize.Height + 10;

      // Race Information
      yPos = DrawRaceInformation(g, normalFont, headerFont, leftMargin, yPos);
      yPos += 15;

      // Race Statistics
      yPos = DrawRaceStatistics(g, normalFont, headerFont, leftMargin, yPos);
      yPos += 15;

      _headerHeight = yPos; // Store header height for subsequent pages
    }
    else
    {
      // On subsequent pages, just show title and skip to results
      var titleText = (_reportData.RaceTitle ?? "Race Results") + " (continued)";
      var titleSize = g.MeasureString(titleText ?? "Race Results", headerFont);
      g.DrawString(titleText, headerFont, Brushes.Black,
        leftMargin + (printableArea.Width - titleSize.Width) / 2, yPos);
      yPos += titleSize.Height + 20;
    }

    // Results Table
    bool hasMorePages = DrawResultsTable(g, normalFont, headerFont, smallFont, printableArea, ref yPos);

    // Page footer
    var footerText = $"Generated: {_reportData.GeneratedAt:yyyy-MM-dd HH:mm:ss} - Page {_currentPage + 1}";
    var footerSize = g.MeasureString(footerText, smallFont);
    g.DrawString(footerText, smallFont, Brushes.Gray,
      rightMargin - footerSize.Width, pageRect.Bottom - 30);

    // Cleanup fonts
    titleFont.Dispose();
    headerFont.Dispose();
    normalFont.Dispose();
    smallFont.Dispose();

    // Set up for next page if needed
    if (hasMorePages)
    {
      _currentPage++;
      e.HasMorePages = true;
    }
    else
    {
      e.HasMorePages = false;
      _currentPage = 0; // Reset for next print job
      _currentRiderIndex = 0;
    }
  }

  private float DrawRaceInformation(Graphics g, Font normalFont, Font headerFont, float leftMargin, float yPos)
  {
    g.DrawString("Race Information", headerFont, Brushes.Black, leftMargin, yPos);
    yPos += g.MeasureString("Race Information", headerFont).Height + 5;

    var infoLines = new List<string>();

    if (_reportData?.RaceStartTime.HasValue == true)
      infoLines.Add($"Start Time: {_reportData.RaceStartTime.Value:yyyy-MM-dd HH:mm:ss}");

    if (_reportData?.RaceEndTime.HasValue == true)
      infoLines.Add($"End Time: {_reportData.RaceEndTime.Value:yyyy-MM-dd HH:mm:ss}");

    infoLines.Add($"Scheduled Duration: {_reportData?.RaceDuration:mm\\:ss}");

    if (_reportData?.RaceStatistics?.ActualRaceDuration.HasValue == true)
      infoLines.Add($"Actual Duration: {_reportData.RaceStatistics.ActualRaceDuration.Value:mm\\:ss\\.fff}");

    infoLines.Add($"Race Status: {(_reportData?.RaceFinished == true ? "Finished" : "In Progress")}");

    foreach (var line in infoLines)
    {
      g.DrawString(line, normalFont, Brushes.Black, leftMargin + 20, yPos);
      yPos += g.MeasureString(line, normalFont).Height + 2;
    }

    return yPos;
  }

  private float DrawRaceStatistics(Graphics g, Font normalFont, Font headerFont, float leftMargin, float yPos)
  {
    g.DrawString("Race Statistics", headerFont, Brushes.Black, leftMargin, yPos);
    yPos += g.MeasureString("Race Statistics", headerFont).Height + 5;

    var stats = _reportData?.RaceStatistics;
    if (stats == null) return yPos;

    var statsLines = new List<string>
    {
      $"Total Riders: {stats.TotalRiders}",
      $"Finished: {stats.FinishedRiders}",
      $"DNF: {stats.DNFRiders}",
      $"Total Laps Completed: {stats.TotalLapsCompleted}"
    };

    if (stats.FastestLap != null)
    {
      statsLines.Add($"Fastest Lap: {stats.FastestLap.TagID} - {stats.FastestLap.BestLapTime:mm\\:ss\\.fff}");
    }

    foreach (var line in statsLines)
    {
      g.DrawString(line, normalFont, Brushes.Black, leftMargin + 20, yPos);
      yPos += g.MeasureString(line, normalFont).Height + 2;
    }

    return yPos;
  }

  private bool DrawResultsTable(Graphics g, Font normalFont, Font headerFont, Font smallFont,
    Rectangle printableArea, ref float yPos)
  {
    // Only draw "Race Results" header on first page or if we're starting fresh
    if (_currentPage == 0 || _currentRiderIndex == 0)
    {
      g.DrawString("Race Results", headerFont, Brushes.Black, printableArea.Left, yPos);
      yPos += g.MeasureString("Race Results", headerFont).Height + 10;
    }

    // Table headers
    var headers = new[] { "Pos", "Rider ID", "Laps", "Total Time", "Best Lap", "Avg Lap", "Gap" };
    var columnWidths = new[] { 40, 120, 50, 80, 80, 80, 80 };
    var totalTableWidth = columnWidths.Sum();

    // Draw headers
    float xPos = printableArea.Left;
    for (int i = 0; i < headers.Length; i++)
    {
      var headerRect = new Rectangle((int)xPos, (int)yPos, columnWidths[i], 20);
      g.FillRectangle(Brushes.LightGray, headerRect);
      g.DrawRectangle(Pens.Black, headerRect);

      var headerText = headers[i];
      var textSize = g.MeasureString(headerText, normalFont);
      var textX = xPos + (columnWidths[i] - textSize.Width) / 2;
      var textY = yPos + (20 - textSize.Height) / 2;
      g.DrawString(headerText, normalFont, Brushes.Black, textX, textY);

      xPos += columnWidths[i];
    }
    yPos += 20;

    // Draw data rows starting from current rider index
    var ridersLeft = _reportData?.RiderResults?.Skip(_currentRiderIndex) ?? new List<RiderResult>();
    int rowsDrawn = 0;

    foreach (var result in ridersLeft)
    {
      xPos = printableArea.Left;
      var rowHeight = 18;

      // Check if we have room for this row (need space for row + footer)
      if (yPos + rowHeight > printableArea.Bottom - 60)
      {
        // No room for this row, need a new page
        return _currentRiderIndex < (_reportData?.RiderResults?.Count ?? 0);
      }

      var rowData = new[]
      {
        result.Position,
        result.TagID.Length > 15 ? result.TagID.Substring(0, 12) + "..." : result.TagID,
        result.TotalLaps.ToString(),
        result.TotalTime.ToString(@"mm\:ss\.fff"),
        result.BestLapTime?.ToString(@"mm\:ss\.fff") ?? "N/A",
        result.AverageLapTime?.ToString(@"mm\:ss\.fff") ?? "N/A",
        GetGapText(result)
      };

      for (int i = 0; i < rowData.Length; i++)
      {
        var cellRect = new Rectangle((int)xPos, (int)yPos, columnWidths[i], rowHeight);

        // Color coding for position
        Brush backgroundBrush = Brushes.White;
        if (result.Position == "1") backgroundBrush = Brushes.LightGoldenrodYellow;
        else if (result.Position == "2") backgroundBrush = Brushes.LightGray;
        else if (result.Position == "3") backgroundBrush = Brushes.Wheat;
        else if (result.IsDNF) backgroundBrush = Brushes.MistyRose;

        g.FillRectangle(backgroundBrush, cellRect);
        g.DrawRectangle(Pens.Black, cellRect);

        var textBrush = result.IsDNF ? Brushes.DarkRed : Brushes.Black;
        var font = result.Position == "1" ? headerFont : normalFont;

        var textSize = g.MeasureString(rowData[i], font);
        var textX = xPos + (columnWidths[i] - textSize.Width) / 2;
        var textY = yPos + (rowHeight - textSize.Height) / 2;
        g.DrawString(rowData[i], font, textBrush, textX, textY);

        xPos += columnWidths[i];
      }

      yPos += rowHeight;
      _currentRiderIndex++;
      rowsDrawn++;
    }

    // Return false if we've drawn all riders
    return _currentRiderIndex < (_reportData?.RiderResults?.Count ?? 0);
  }

  private string GetGapText(RiderResult result)
  {
    if (result.Position == "1") return "Leader";
    if (result.IsDNF) return "DNF";

    if (result.LapGapToLeader > 0)
      return $"-{result.LapGapToLeader} lap{(result.LapGapToLeader == 1 ? "" : "s")}";

    if (result.GapToLeader.HasValue)
      return $"+{result.GapToLeader.Value:mm\\:ss}";

    return "N/A";
  }

  private string GenerateTextReport()
  {
    if (_reportData == null) return "No report data available.";

    var sb = new StringBuilder();

    // Title and header
    sb.AppendLine("=" + new string('=', 60) + "=");
    sb.AppendLine($" {_reportData.RaceTitle.ToUpper()}");
    sb.AppendLine("=" + new string('=', 60) + "=");
    sb.AppendLine();

    // Race information
    sb.AppendLine("RACE INFORMATION:");
    sb.AppendLine(new string('-', 40));

    if (_reportData.RaceStartTime.HasValue)
      sb.AppendLine($"Start Time:        {_reportData.RaceStartTime.Value:yyyy-MM-dd HH:mm:ss}");

    if (_reportData.RaceEndTime.HasValue)
      sb.AppendLine($"End Time:          {_reportData.RaceEndTime.Value:yyyy-MM-dd HH:mm:ss}");

    sb.AppendLine($"Scheduled Duration: {_reportData.RaceDuration:mm\\:ss}");

    if (_reportData.RaceStatistics?.ActualRaceDuration.HasValue == true)
      sb.AppendLine($"Actual Duration:   {_reportData.RaceStatistics.ActualRaceDuration.Value:mm\\:ss\\.fff}");

    sb.AppendLine($"Race Status:       {(_reportData.RaceFinished ? "Finished" : "In Progress")}");
    sb.AppendLine();

    // Race statistics
    sb.AppendLine("RACE STATISTICS:");
    sb.AppendLine(new string('-', 40));
    var stats = _reportData.RaceStatistics;
    if (stats != null)
    {
      sb.AppendLine($"Total Riders:      {stats.TotalRiders}");
      sb.AppendLine($"Finished:          {stats.FinishedRiders}");
      sb.AppendLine($"DNF:               {stats.DNFRiders}");
      sb.AppendLine($"Total Laps:        {stats.TotalLapsCompleted}");

      if (stats.FastestLap != null)
        sb.AppendLine($"Fastest Lap:       {stats.FastestLap.TagID} - {stats.FastestLap.BestLapTime:mm\\:ss\\.fff}");
    }

    sb.AppendLine();

    // Results table
    sb.AppendLine("RACE RESULTS:");
    sb.AppendLine(new string('=', 95));
    sb.AppendLine($"{"Pos",-4} {"Rider ID",-20} {"Laps",-5} {"Total Time",-12} {"Best Lap",-10} {"Avg Lap",-10} {"Gap",-15}");
    sb.AppendLine(new string('-', 95));

    foreach (var result in _reportData.RiderResults)
    {
      var gapText = GetGapText(result);
      var bestLap = result.BestLapTime?.ToString(@"mm\:ss\.fff") ?? "N/A";
      var avgLap = result.AverageLapTime?.ToString(@"mm\:ss\.fff") ?? "N/A";

      sb.AppendLine($"{result.Position,-4} {result.TagID,-20} {result.TotalLaps,-5} " +
                   $"{result.TotalTime:mm\\:ss\\.fff,-12} {bestLap,-10} {avgLap,-10} {gapText,-15}");
    }

    sb.AppendLine(new string('=', 95));
    sb.AppendLine();
    sb.AppendLine($"Report generated: {_reportData.GeneratedAt:yyyy-MM-dd HH:mm:ss}");

    return sb.ToString();
  }

  private TimeSpan? CalculateAverageLapTime(RiderInfo rider)
  {
    var validLapTimes = rider.Laps.Where(l => l.LapTime.HasValue).Select(l => l.LapTime!.Value).ToList();

    if (validLapTimes.Count == 0)
      return null;

    var totalMilliseconds = validLapTimes.Sum(t => t.TotalMilliseconds);
    var averageMilliseconds = totalMilliseconds / validLapTimes.Count;

    return TimeSpan.FromMilliseconds(averageMilliseconds);
  }

  public void Dispose()
  {
    _printDocument?.Dispose();
  }
}

/// <summary>
/// Data structure for race report
/// </summary>
public class RaceReportData
{
  public string RaceTitle { get; set; } = "";
  public DateTime? RaceStartTime { get; set; }
  public DateTime? RaceEndTime { get; set; }
  public TimeSpan RaceDuration { get; set; }
  public bool RaceFinished { get; set; }
  public DateTime GeneratedAt { get; set; }
  public List<RiderResult> RiderResults { get; set; } = new();
  public RaceStatistics? RaceStatistics { get; set; }
}

/// <summary>
/// Individual rider result data
/// </summary>
public class RiderResult
{
  public string Position { get; set; } = "";
  public string TagID { get; set; } = "";
  public int TotalLaps { get; set; }
  public TimeSpan TotalTime { get; set; }
  public TimeSpan? BestLapTime { get; set; }
  public TimeSpan? AverageLapTime { get; set; }
  public bool IsDNF { get; set; }
  public TimeSpan? GapToLeader { get; set; }
  public int LapGapToLeader { get; set; }
  public List<LapResult> LapTimes { get; set; } = new();
}

/// <summary>
/// Individual lap result data
/// </summary>
public class LapResult
{
  public int LapNumber { get; set; }
  public TimeSpan? LapTime { get; set; }
  public DateTime CrossingTime { get; set; }
}

/// <summary>
/// Overall race statistics
/// </summary>
public class RaceStatistics
{
  public int TotalRiders { get; set; }
  public int FinishedRiders { get; set; }
  public int DNFRiders { get; set; }
  public int TotalLapsCompleted { get; set; }
  public RiderResult? FastestLap { get; set; }
  public TimeSpan? ActualRaceDuration { get; set; }
}
