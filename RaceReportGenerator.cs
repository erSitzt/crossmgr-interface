using System.Drawing.Printing;
using System.Text;
using ClosedXML.Excel;

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
    DateTime? raceEndTime, TimeSpan raceDuration, bool raceFinished, string raceTitle = "Race Results",
    DateTime? additionalLapsSignShown = null, DateTime? raceActuallyEnded = null, int additionalLapsCount = 0)
  {
    _reportData = PrepareReportData(riders, raceStartTime, raceEndTime, raceDuration, raceFinished, raceTitle,
      additionalLapsSignShown, raceActuallyEnded, additionalLapsCount);
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
    DateTime? raceEndTime, TimeSpan raceDuration, bool raceFinished, string raceTitle = "Race Results",
    DateTime? additionalLapsSignShown = null, DateTime? raceActuallyEnded = null, int additionalLapsCount = 0)
  {
    _reportData = PrepareReportData(riders, raceStartTime, raceEndTime, raceDuration, raceFinished, raceTitle,
      additionalLapsSignShown, raceActuallyEnded, additionalLapsCount);
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
  /// Exports race report to file (Text or Excel)
  /// </summary>
  public void ExportToFile(Dictionary<string, RiderInfo> riders, DateTime? raceStartTime,
    DateTime? raceEndTime, TimeSpan raceDuration, bool raceFinished, string raceTitle = "Race Results",
    DateTime? additionalLapsSignShown = null, DateTime? raceActuallyEnded = null, int additionalLapsCount = 0)
  {
    _reportData = PrepareReportData(riders, raceStartTime, raceEndTime, raceDuration, raceFinished, raceTitle,
      additionalLapsSignShown, raceActuallyEnded, additionalLapsCount);
    _currentPage = 0;
    _currentRiderIndex = 0;

    using var saveDialog = new SaveFileDialog();
    saveDialog.Filter = "Excel Files (*.xlsx)|*.xlsx|Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
    saveDialog.DefaultExt = "xlsx";
    saveDialog.FileName = $"Race_Report_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

    if (saveDialog.ShowDialog() == DialogResult.OK)
    {
      var extension = Path.GetExtension(saveDialog.FileName).ToLower();

      if (extension == ".xlsx")
      {
        ExportToExcel(saveDialog.FileName);
      }
      else
      {
        var reportText = GenerateTextReport();
        File.WriteAllText(saveDialog.FileName, reportText, Encoding.UTF8);
      }

      MessageBox.Show($"Race report exported to:\n{saveDialog.FileName}", "Export Complete",
        MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
  }

  private RaceReportData PrepareReportData(Dictionary<string, RiderInfo> riders, DateTime? raceStartTime,
    DateTime? raceEndTime, TimeSpan raceDuration, bool raceFinished, string raceTitle,
    DateTime? additionalLapsSignShown = null, DateTime? raceActuallyEnded = null, int additionalLapsCount = 0)
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

      // Find rider info for this tag
      var riderInfo = riders.Values.FirstOrDefault(r => r.TagID == rider.TagID);

      var result = new RiderResult
      {
        Position = rider.IsDNF ? "DNF" : (i + 1).ToString(),
        TagID = rider.TagID,
        RiderNumber = riderInfo?.RiderNumber,
        RiderName = riderInfo != null && !string.IsNullOrWhiteSpace(riderInfo.FirstName + riderInfo.LastName)
                    ? $"{riderInfo.FirstName} {riderInfo.LastName}".Trim()
                    : null,
        Team = riderInfo?.Team,
        Category = riderInfo?.Category,
        Machine = riderInfo?.Machine,
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
      DNFRiders = raceFinished ? dnfRiders.Count : 0, // Only count DNF after race is finished
      TotalLapsCompleted = reportData.RiderResults.Sum(r => r.TotalLaps),
      FastestLap = finishedRiders.Where(r => r.BestLapTime.HasValue)
                                .OrderBy(r => r.BestLapTime ?? TimeSpan.MaxValue)
                                .FirstOrDefault(),
      ActualRaceDuration = finishedRiders.FirstOrDefault()?.TotalTime, // Winner's total time
      AdditionalLapsSignShown = additionalLapsSignShown,
      RaceActuallyEnded = raceActuallyEnded,
      AdditionalLapsCount = additionalLapsCount
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
      var titleSize = g.MeasureString(titleText ?? "Race Results", titleFont!);
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
      var titleSize = g.MeasureString(titleText ?? "Race Results", headerFont!);
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
      $"Finished: {stats.FinishedRiders}"
    };

    // Only show DNF count if race is finished
    if (stats.DNFRiders > 0)
    {
      statsLines.Add($"DNF: {stats.DNFRiders}");
    }

    statsLines.Add($"Total Laps Completed: {stats.TotalLapsCompleted}");

    if (stats.FastestLap != null)
    {
      statsLines.Add($"Fastest Lap: {stats.FastestLap.TagID} - {stats.FastestLap.BestLapTime:mm\\:ss\\.fff}");
    }

    // Add additional laps timing information
    if (stats.AdditionalLapsSignShown.HasValue)
    {
      statsLines.Add($"Additional Laps Sign Shown: {stats.AdditionalLapsSignShown.Value:yyyy-MM-dd HH:mm:ss}");
      if (stats.AdditionalLapsCount > 0)
      {
        var lapsText = stats.AdditionalLapsCount == 1 ? "lap" : "laps";
        statsLines.Add($"Additional Laps Required: {stats.AdditionalLapsCount} {lapsText}");
      }
    }

    if (stats.RaceActuallyEnded.HasValue)
    {
      statsLines.Add($"Race Actually Ended: {stats.RaceActuallyEnded.Value:yyyy-MM-dd HH:mm:ss}");
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
    var headers = new[] { "Pos", "Name", "Team", "Laps", "Total Time", "Best Lap", "Gap" };
    var columnWidths = new[] { 35, 120, 100, 45, 80, 80, 80 };
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
        (!string.IsNullOrWhiteSpace(result.RiderName) ? result.RiderName : result.TagID).Length > 18
          ? (!string.IsNullOrWhiteSpace(result.RiderName) ? result.RiderName : result.TagID)[..15] + "..."
          : (!string.IsNullOrWhiteSpace(result.RiderName) ? result.RiderName : result.TagID),
        (!string.IsNullOrWhiteSpace(result.Team) ? result.Team : "").Length > 15
          ? (!string.IsNullOrWhiteSpace(result.Team) ? result.Team : "")[..12] + "..."
          : (!string.IsNullOrWhiteSpace(result.Team) ? result.Team : ""),
        result.TotalLaps.ToString(),
        result.TotalTime.ToString(@"mm\:ss\.fff"),
        result.BestLapTime?.ToString(@"mm\:ss\.fff") ?? "N/A",
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

      // Only show DNF count if race is finished
      if (stats.DNFRiders > 0)
      {
        sb.AppendLine($"DNF:               {stats.DNFRiders}");
      }

      sb.AppendLine($"Total Laps:        {stats.TotalLapsCompleted}");

      if (stats.FastestLap != null)
        sb.AppendLine($"Fastest Lap:       {stats.FastestLap.TagID} - {stats.FastestLap.BestLapTime:mm\\:ss\\.fff}");

      // Add additional laps timing information
      if (stats.AdditionalLapsSignShown.HasValue)
      {
        sb.AppendLine($"Additional Laps Sign: {stats.AdditionalLapsSignShown.Value:yyyy-MM-dd HH:mm:ss}");
        if (stats.AdditionalLapsCount > 0)
        {
          var lapsText = stats.AdditionalLapsCount == 1 ? "lap" : "laps";
          sb.AppendLine($"Additional Laps:   {stats.AdditionalLapsCount} {lapsText}");
        }
      }

      if (stats.RaceActuallyEnded.HasValue)
      {
        sb.AppendLine($"Race Ended:        {stats.RaceActuallyEnded.Value:yyyy-MM-dd HH:mm:ss}");
      }
    }

    sb.AppendLine();

    // Results table
    sb.AppendLine("RACE RESULTS:");
    sb.AppendLine(new string('=', 130));
    sb.AppendLine($"{"Pos",-4} {"Tag ID",-12} {"Name",-20} {"Team",-15} {"Laps",-5} {"Total Time",-12} {"Best Lap",-10} {"Avg Lap",-10} {"Gap",-15}");
    sb.AppendLine(new string('-', 130));

    foreach (var result in _reportData.RiderResults)
    {
      var gapText = GetGapText(result);
      var bestLap = result.BestLapTime?.ToString(@"mm\:ss\.fff") ?? "N/A";
      var avgLap = result.AverageLapTime?.ToString(@"mm\:ss\.fff") ?? "N/A";
      var riderName = !string.IsNullOrWhiteSpace(result.RiderName) ? result.RiderName : "";
      var team = !string.IsNullOrWhiteSpace(result.Team) ? result.Team : "";

      // Truncate long names/teams if needed to fit format
      if (riderName.Length > 19) riderName = riderName[..16] + "...";
      if (team.Length > 14) team = team[..11] + "...";

      sb.AppendLine($"{result.Position,-4} {result.TagID,-12} {riderName,-20} {team,-15} {result.TotalLaps,-5} " +
                   $"{result.TotalTime:mm\\:ss\\.fff,-12} {bestLap,-10} {avgLap,-10} {gapText,-15}");
    }

    sb.AppendLine(new string('=', 130));
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

  /// <summary>
  /// Exports race report to Excel file
  /// </summary>
  private void ExportToExcel(string fileName)
  {
    if (_reportData == null) return;

    using var workbook = new XLWorkbook();

    // Create main results worksheet
    var resultsSheet = workbook.Worksheets.Add("Race Results");
    CreateResultsSheet(resultsSheet);

    // Create lap times worksheet
    var lapTimesSheet = workbook.Worksheets.Add("Lap Times");
    CreateLapTimesSheet(lapTimesSheet);

    // Create statistics worksheet
    var statsSheet = workbook.Worksheets.Add("Statistics");
    CreateStatisticsSheet(statsSheet);

    // Save the workbook
    workbook.SaveAs(fileName);
  }

  /// <summary>
  /// Creates the main race results sheet
  /// </summary>
  private void CreateResultsSheet(IXLWorksheet sheet)
  {
    if (_reportData == null) return;

    // Title and race info
    var currentRow = 1;
    sheet.Cell(currentRow, 1).Value = _reportData.RaceTitle;
    sheet.Cell(currentRow, 1).Style.Font.FontSize = 18;
    sheet.Cell(currentRow, 1).Style.Font.Bold = true;
    sheet.Range(currentRow, 1, currentRow, 12).Merge();
    currentRow += 2;

    // Race information
    if (_reportData.RaceStartTime.HasValue)
    {
      sheet.Cell(currentRow, 1).Value = "Start Time:";
      sheet.Cell(currentRow, 2).Value = _reportData.RaceStartTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
      currentRow++;
    }

    if (_reportData.RaceEndTime.HasValue)
    {
      sheet.Cell(currentRow, 1).Value = "End Time:";
      sheet.Cell(currentRow, 2).Value = _reportData.RaceEndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
      currentRow++;
    }

    if (_reportData.Statistics?.ActualRaceDuration.HasValue == true)
    {
      sheet.Cell(currentRow, 1).Value = "Race Duration:";
      sheet.Cell(currentRow, 2).Value = _reportData.Statistics.ActualRaceDuration.Value.ToString(@"hh\:mm\:ss");
      currentRow++;
    }

    sheet.Cell(currentRow, 1).Value = "Generated:";
    sheet.Cell(currentRow, 2).Value = _reportData.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss");
    currentRow += 2;

    // Headers
    var headers = new[] { "Position", "Tag ID", "Number", "Rider Name", "Team", "Category", "Laps", "Total Time", "Best Lap", "Avg Lap", "Gap", "Status" };
    for (int i = 0; i < headers.Length; i++)
    {
      var cell = sheet.Cell(currentRow, i + 1);
      cell.Value = headers[i];
      cell.Style.Font.Bold = true;
      cell.Style.Fill.BackgroundColor = XLColor.LightGray;
      cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
    }
    currentRow++;

    // Results data
    foreach (var rider in _reportData.RiderResults)
    {
      sheet.Cell(currentRow, 1).Value = rider.Position;
      sheet.Cell(currentRow, 2).Value = rider.TagID;
      sheet.Cell(currentRow, 3).Value = rider.RiderNumber ?? "";
      sheet.Cell(currentRow, 4).Value = rider.RiderName ?? "";
      sheet.Cell(currentRow, 5).Value = rider.Team ?? "";
      sheet.Cell(currentRow, 6).Value = rider.Category ?? "";
      sheet.Cell(currentRow, 7).Value = rider.TotalLaps;
      sheet.Cell(currentRow, 8).Value = rider.TotalTime.ToString(@"hh\:mm\:ss\.fff");
      sheet.Cell(currentRow, 9).Value = rider.BestLapTime?.ToString(@"mm\:ss\.fff") ?? "N/A";
      sheet.Cell(currentRow, 10).Value = rider.AverageLapTime?.ToString(@"mm\:ss\.fff") ?? "N/A";
      sheet.Cell(currentRow, 11).Value = rider.Gap ?? "";
      sheet.Cell(currentRow, 12).Value = rider.Status;

      // Color coding for positions
      if (rider.Position == "1" && rider.Status != "DNF")
        sheet.Row(currentRow).Style.Fill.BackgroundColor = XLColor.Gold;
      else if (rider.Position == "2" && rider.Status != "DNF")
        sheet.Row(currentRow).Style.Fill.BackgroundColor = XLColor.Silver;
      else if (rider.Position == "3" && rider.Status != "DNF")
        sheet.Row(currentRow).Style.Fill.BackgroundColor = XLColor.FromArgb(205, 127, 50); // Bronze
      else if (rider.Status == "DNF")
        sheet.Row(currentRow).Style.Fill.BackgroundColor = XLColor.LightGray;

      // Add borders
      sheet.Range(currentRow, 1, currentRow, 12).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

      currentRow++;
    }

    // Auto-fit columns
    sheet.Columns().AdjustToContents();
  }

  /// <summary>
  /// Creates the detailed lap times sheet
  /// </summary>
  private void CreateLapTimesSheet(IXLWorksheet sheet)
  {
    if (_reportData == null) return;

    var currentRow = 1;

    // Title
    sheet.Cell(currentRow, 1).Value = "Detailed Lap Times";
    sheet.Cell(currentRow, 1).Style.Font.FontSize = 16;
    sheet.Cell(currentRow, 1).Style.Font.Bold = true;
    currentRow += 2;

    foreach (var rider in _reportData.RiderResults)
    {
      // Rider header
      var riderDisplay = !string.IsNullOrWhiteSpace(rider.RiderName)
        ? $"{rider.RiderName} (Tag: {rider.TagID})"
        : $"Tag: {rider.TagID}";
      sheet.Cell(currentRow, 1).Value = $"Rider: {riderDisplay} (Position: {rider.Position})";
      sheet.Cell(currentRow, 1).Style.Font.Bold = true;
      sheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.LightBlue;
      sheet.Range(currentRow, 1, currentRow, 4).Merge();
      currentRow++;

      // Lap headers
      sheet.Cell(currentRow, 1).Value = "Lap";
      sheet.Cell(currentRow, 2).Value = "Lap Time";
      sheet.Cell(currentRow, 3).Value = "Crossing Time";
      sheet.Cell(currentRow, 4).Value = "Total Time";

      sheet.Range(currentRow, 1, currentRow, 4).Style.Font.Bold = true;
      sheet.Range(currentRow, 1, currentRow, 4).Style.Fill.BackgroundColor = XLColor.LightGray;
      currentRow++;

      var totalTime = TimeSpan.Zero;
      foreach (var lap in rider.LapTimes)
      {
        sheet.Cell(currentRow, 1).Value = lap.LapNumber;
        sheet.Cell(currentRow, 2).Value = lap.LapTime?.ToString(@"mm\:ss\.fff") ?? "N/A";
        sheet.Cell(currentRow, 3).Value = lap.CrossingTime.ToString("HH:mm:ss.fff");

        if (lap.LapTime.HasValue)
          totalTime += lap.LapTime.Value;
        sheet.Cell(currentRow, 4).Value = totalTime.ToString(@"hh\:mm\:ss\.fff");

        currentRow++;
      }

      currentRow += 1; // Space between riders
    }

    // Auto-fit columns
    sheet.Columns().AdjustToContents();
  }

  /// <summary>
  /// Creates the race statistics sheet
  /// </summary>
  private void CreateStatisticsSheet(IXLWorksheet sheet)
  {
    if (_reportData?.Statistics == null) return;

    var currentRow = 1;
    var stats = _reportData.Statistics;

    // Title
    sheet.Cell(currentRow, 1).Value = "Race Statistics";
    sheet.Cell(currentRow, 1).Style.Font.FontSize = 16;
    sheet.Cell(currentRow, 1).Style.Font.Bold = true;
    currentRow += 2;

    // Overall statistics
    sheet.Cell(currentRow, 1).Value = "Total Riders:";
    sheet.Cell(currentRow, 2).Value = stats.TotalRiders;
    currentRow++;

    sheet.Cell(currentRow, 1).Value = "Finished Riders:";
    sheet.Cell(currentRow, 2).Value = stats.FinishedRiders;
    currentRow++;

    if (stats.DNFRiders > 0)
    {
      sheet.Cell(currentRow, 1).Value = "DNF Riders:";
      sheet.Cell(currentRow, 2).Value = stats.DNFRiders;
      currentRow++;
    }

    sheet.Cell(currentRow, 1).Value = "Total Laps Completed:";
    sheet.Cell(currentRow, 2).Value = stats.TotalLapsCompleted;
    currentRow++;

    if (stats.FastestLap != null)
    {
      sheet.Cell(currentRow, 1).Value = "Fastest Lap:";
      sheet.Cell(currentRow, 2).Value = $"{stats.FastestLap.BestLapTime?.ToString(@"mm\:ss\.fff")} by {stats.FastestLap.TagID}";
      currentRow++;
    }

    if (stats.ActualRaceDuration.HasValue)
    {
      sheet.Cell(currentRow, 1).Value = "Actual Race Duration:";
      sheet.Cell(currentRow, 2).Value = stats.ActualRaceDuration.Value.ToString(@"hh\:mm\:ss");
      currentRow++;
    }

    // Additional timing information
    if (stats.AdditionalLapsSignShown.HasValue && _reportData.RaceStartTime.HasValue)
    {
      currentRow++;
      sheet.Cell(currentRow, 1).Value = "Additional Laps Timing:";
      sheet.Cell(currentRow, 1).Style.Font.Bold = true;
      currentRow++;

      var signTime = stats.AdditionalLapsSignShown!.Value - _reportData.RaceStartTime!.Value;
      sheet.Cell(currentRow, 1).Value = "Additional Laps Sign Shown:";
      sheet.Cell(currentRow, 2).Value = signTime.ToString(@"mm\:ss");
      currentRow++;

      if (stats.RaceActuallyEnded.HasValue)
      {
        var endTime = stats.RaceActuallyEnded.Value - _reportData.RaceStartTime.Value;
        sheet.Cell(currentRow, 1).Value = "Race Actually Ended:";
        sheet.Cell(currentRow, 2).Value = endTime.ToString(@"mm\:ss");
        currentRow++;

        sheet.Cell(currentRow, 1).Value = "Additional Laps Count:";
        sheet.Cell(currentRow, 2).Value = stats.AdditionalLapsCount;
        currentRow++;
      }
    }

    // Auto-fit columns
    sheet.Columns().AdjustToContents();
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
  public RaceStatistics? Statistics => RaceStatistics; // Alias for backwards compatibility
}

/// <summary>
/// Individual rider result data
/// </summary>
public class RiderResult
{
  public string Position { get; set; } = "";
  public string TagID { get; set; } = "";
  public string RiderNumber { get; set; } = "";
  public string RiderName { get; set; } = "";
  public string Team { get; set; } = "";
  public string Category { get; set; } = "";
  public string Machine { get; set; } = "";
  public int TotalLaps { get; set; }
  public TimeSpan TotalTime { get; set; }
  public TimeSpan? BestLapTime { get; set; }
  public TimeSpan? AverageLapTime { get; set; }
  public bool IsDNF { get; set; }
  public TimeSpan? GapToLeader { get; set; }
  public int LapGapToLeader { get; set; }
  public List<LapResult> LapTimes { get; set; } = new();

  // Additional properties for Excel export
  public string Gap => GapToLeader?.ToString(@"hh\:mm\:ss") ?? "";
  public string Status => IsDNF ? "DNF" : "Finished";

  /// <summary>
  /// Display name for the rider (name if available, otherwise tag ID)
  /// </summary>
  public string DisplayName
  {
    get
    {
      if (!string.IsNullOrEmpty(RiderName))
        return RiderName;
      return TagID;
    }
  }
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
  public DateTime? AdditionalLapsSignShown { get; set; }
  public DateTime? RaceActuallyEnded { get; set; }
  public int AdditionalLapsCount { get; set; }
}
