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
    DateTime? additionalLapsSignShown = null, DateTime? raceActuallyEnded = null, int additionalLapsCount = 0, RaceRules? rules = null)
  {
    _reportData = PrepareReportData(riders, raceStartTime, raceEndTime, raceDuration, raceFinished, raceTitle,
      additionalLapsSignShown, raceActuallyEnded, additionalLapsCount, rules);
    _currentPage = 0;
    _currentRiderIndex = 0;

    using var printPreview = new PrintPreviewDialog();
    printPreview.Document = _printDocument;
    printPreview.WindowState = FormWindowState.Maximized;
    printPreview.ShowDialog();
  }

  /// <summary>
  /// Generates and shows print preview for all classes + overall report
  /// </summary>
  public void ShowClassBasedPrintPreview(Dictionary<string, RiderInfo> riders, DateTime? raceStartTime,
    DateTime? raceEndTime, TimeSpan raceDuration, bool raceFinished, string raceTitle = "Race Results",
    DateTime? additionalLapsSignShown = null, DateTime? raceActuallyEnded = null, int additionalLapsCount = 0, RaceRules? rules = null)
  {
    // Get unique classes
    var classes = GetUniqueClasses(riders);

    if (classes.Count <= 1)
    {
      // No classes or only one class, show regular report
      ShowPrintPreview(riders, raceStartTime, raceEndTime, raceDuration, raceFinished,
        raceTitle, additionalLapsSignShown, raceActuallyEnded, additionalLapsCount, rules);
      return;
    }

    // Show overall report first
    ShowPrintPreview(riders, raceStartTime, raceEndTime, raceDuration, raceFinished,
      $"{raceTitle} - Overall Results", additionalLapsSignShown, raceActuallyEnded, additionalLapsCount, rules);

    // Show class-specific reports
    foreach (var className in classes.OrderBy(c => c))
    {
      var classRiders = FilterRidersByClass(riders, className);
      if (classRiders.Count > 0)
      {
        ShowPrintPreview(classRiders, raceStartTime, raceEndTime, raceDuration, raceFinished,
          $"{raceTitle} - Class: {className}", additionalLapsSignShown, raceActuallyEnded, additionalLapsCount, rules);
      }
    }
  }

  /// <summary>
  /// Prints the race report directly
  /// </summary>
  public void PrintReport(Dictionary<string, RiderInfo> riders, DateTime? raceStartTime,
    DateTime? raceEndTime, TimeSpan raceDuration, bool raceFinished, string raceTitle = "Race Results",
    DateTime? additionalLapsSignShown = null, DateTime? raceActuallyEnded = null, int additionalLapsCount = 0, RaceRules? rules = null)
  {
    // Get unique classes
    var classes = GetUniqueClasses(riders);

    if (classes.Count <= 1)
    {
      // No classes or only one class, print regular report
      PrintSingleReport(riders, raceStartTime, raceEndTime, raceDuration, raceFinished,
        raceTitle, additionalLapsSignShown, raceActuallyEnded, additionalLapsCount, rules);
      return;
    }

    // Multiple classes - ask user what to print
    var result = MessageBox.Show(
      $"Multiple classes detected ({classes.Count} classes).\n\n" +
      "Yes: Print all reports (Overall + each class)\n" +
      "No: Print overall report only\n" +
      "Cancel: Cancel printing",
      "Print Options", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

    switch (result)
    {
      case DialogResult.Yes:
        // Print overall + all class reports
        PrintClassBasedReports(riders, raceStartTime, raceEndTime, raceDuration, raceFinished,
          raceTitle, additionalLapsSignShown, raceActuallyEnded, additionalLapsCount, classes, rules);
        break;
      case DialogResult.No:
        // Print overall only
        PrintSingleReport(riders, raceStartTime, raceEndTime, raceDuration, raceFinished,
          $"{raceTitle} - Overall Results", additionalLapsSignShown, raceActuallyEnded, additionalLapsCount, rules);
        break;
      case DialogResult.Cancel:
        // Do nothing
        break;
    }
  }

  /// <summary>
  /// Prints a single race report
  /// </summary>
  private void PrintSingleReport(Dictionary<string, RiderInfo> riders, DateTime? raceStartTime,
    DateTime? raceEndTime, TimeSpan raceDuration, bool raceFinished, string raceTitle = "Race Results",
    DateTime? additionalLapsSignShown = null, DateTime? raceActuallyEnded = null, int additionalLapsCount = 0, RaceRules? rules = null)
  {
    _reportData = PrepareReportData(riders, raceStartTime, raceEndTime, raceDuration, raceFinished, raceTitle,
      additionalLapsSignShown, raceActuallyEnded, additionalLapsCount, rules);
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
  /// Prints class-based reports
  /// </summary>
  private void PrintClassBasedReports(Dictionary<string, RiderInfo> riders, DateTime? raceStartTime,
    DateTime? raceEndTime, TimeSpan raceDuration, bool raceFinished, string raceTitle,
    DateTime? additionalLapsSignShown, DateTime? raceActuallyEnded, int additionalLapsCount, List<string> classes, RaceRules? rules = null)
  {
    using var printDialog = new PrintDialog();
    printDialog.Document = _printDocument;

    if (printDialog.ShowDialog() == DialogResult.OK)
    {
      // Print overall report
      _reportData = PrepareReportData(riders, raceStartTime, raceEndTime, raceDuration, raceFinished,
        $"{raceTitle} - Overall Results", additionalLapsSignShown, raceActuallyEnded, additionalLapsCount, rules);
      _currentPage = 0;
      _currentRiderIndex = 0;
      _printDocument.Print();

      // Print class-specific reports
      foreach (var className in classes.OrderBy(c => c))
      {
        var classRiders = FilterRidersByClass(riders, className);
        if (classRiders.Count > 0)
        {
          _reportData = PrepareReportData(classRiders, raceStartTime, raceEndTime, raceDuration, raceFinished,
            $"{raceTitle} - Class: {className}", additionalLapsSignShown, raceActuallyEnded, additionalLapsCount, rules);
          _currentPage = 0;
          _currentRiderIndex = 0;
          _printDocument.Print();
        }
      }
    }
  }

  /// <summary>
  /// Exports race report to file (Text or Excel)
  /// </summary>
  public void ExportToFile(Dictionary<string, RiderInfo> riders, DateTime? raceStartTime,
    DateTime? raceEndTime, TimeSpan raceDuration, bool raceFinished, string raceTitle = "Race Results",
    DateTime? additionalLapsSignShown = null, DateTime? raceActuallyEnded = null, int additionalLapsCount = 0, RaceRules? rules = null)
  {
    // Get unique classes
    var classes = GetUniqueClasses(riders);

    if (classes.Count <= 1)
    {
      // No classes or only one class, export regular report
      ExportSingleReportToFile(riders, raceStartTime, raceEndTime, raceDuration, raceFinished,
        raceTitle, additionalLapsSignShown, raceActuallyEnded, additionalLapsCount, rules);
      return;
    }

    // Multiple classes - export all reports
    ExportClassBasedReportsToFile(riders, raceStartTime, raceEndTime, raceDuration, raceFinished,
      raceTitle, additionalLapsSignShown, raceActuallyEnded, additionalLapsCount, classes, rules);
  }

  /// <summary>
  /// Exports a single race report to file
  /// </summary>
  private void ExportSingleReportToFile(Dictionary<string, RiderInfo> riders, DateTime? raceStartTime,
    DateTime? raceEndTime, TimeSpan raceDuration, bool raceFinished, string raceTitle = "Race Results",
    DateTime? additionalLapsSignShown = null, DateTime? raceActuallyEnded = null, int additionalLapsCount = 0, RaceRules? rules = null)
  {
    _reportData = PrepareReportData(riders, raceStartTime, raceEndTime, raceDuration, raceFinished, raceTitle,
      additionalLapsSignShown, raceActuallyEnded, additionalLapsCount, rules);
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

  /// <summary>
  /// Exports class-based reports to files
  /// </summary>
  private void ExportClassBasedReportsToFile(Dictionary<string, RiderInfo> riders, DateTime? raceStartTime,
    DateTime? raceEndTime, TimeSpan raceDuration, bool raceFinished, string raceTitle,
    DateTime? additionalLapsSignShown, DateTime? raceActuallyEnded, int additionalLapsCount, List<string> classes, RaceRules? rules = null)
  {
    using var folderDialog = new FolderBrowserDialog();
    folderDialog.Description = "Select folder to save class-based race reports";
    folderDialog.UseDescriptionForTitle = true;

    if (folderDialog.ShowDialog() == DialogResult.OK)
    {
      var baseFileName = $"Race_Report_{DateTime.Now:yyyyMMdd_HHmmss}";
      var exportedFiles = new List<string>();

      try
      {
        // Export overall report
        var overallFileName = Path.Combine(folderDialog.SelectedPath, $"{baseFileName}_Overall.xlsx");
        _reportData = PrepareReportData(riders, raceStartTime, raceEndTime, raceDuration, raceFinished,
          $"{raceTitle} - Overall Results", additionalLapsSignShown, raceActuallyEnded, additionalLapsCount, rules);
        ExportToExcel(overallFileName);
        exportedFiles.Add(overallFileName);

        // Export class-specific reports
        foreach (var className in classes.OrderBy(c => c))
        {
          var classRiders = FilterRidersByClass(riders, className);
          if (classRiders.Count > 0)
          {
            var classFileName = Path.Combine(folderDialog.SelectedPath,
              $"{baseFileName}_Class_{SanitizeFileName(className)}.xlsx");

            _reportData = PrepareReportData(classRiders, raceStartTime, raceEndTime, raceDuration, raceFinished,
              $"{raceTitle} - Class: {className}", additionalLapsSignShown, raceActuallyEnded, additionalLapsCount, rules);
            ExportToExcel(classFileName);
            exportedFiles.Add(classFileName);
          }
        }

        var filesList = string.Join("\n", exportedFiles.Select(f => Path.GetFileName(f)));
        MessageBox.Show($"Race reports exported to:\n{folderDialog.SelectedPath}\n\nFiles created:\n{filesList}",
          "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
      }
      catch (Exception ex)
      {
        MessageBox.Show($"Error exporting reports: {ex.Message}", "Export Error",
          MessageBoxButtons.OK, MessageBoxIcon.Error);
      }
    }
  }

  // Shared with the gate pick sheet so a meeting is split into files the same
  // way whichever report is being printed. See ReportHelpers.
  private List<string> GetUniqueClasses(Dictionary<string, RiderInfo> riders) =>
    ReportHelpers.GetUniqueClasses(riders);

  private Dictionary<string, RiderInfo> FilterRidersByClass(Dictionary<string, RiderInfo> riders, string className) =>
    ReportHelpers.FilterRidersByClass(riders, className);

  private string SanitizeFileName(string fileName) =>
    ReportHelpers.SanitizeFileName(fileName);

  // Public so the tests can check the ranking and the statuses without printing.
  public RaceReportData PrepareReportData(Dictionary<string, RiderInfo> riders, DateTime? raceStartTime,
    DateTime? raceEndTime, TimeSpan raceDuration, bool raceFinished, string raceTitle,
    DateTime? additionalLapsSignShown = null, DateTime? raceActuallyEnded = null, int additionalLapsCount = 0, RaceRules? rules = null)
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

    // In a timed session IsDNF only means the rider is no longer on track, so a
    // rider who had pulled in before the flag was printed as DNF on the practice
    // sheet. Nobody is DNF there. A DNS is a DNS in any session.
    var timed = rules?.IsTimedSession == true;
    bool Dnf(RiderInfo r) => !timed && r.IsDNF && !r.IsDNS;
    bool Out(RiderInfo r) => r.IsDNS || Dnf(r);

    // Sort riders by final position: classified, then DNF, then DNS
    var sortedRiders = riders.Values
      .OrderBy(r => r.IsDNS ? 2 : Dnf(r) ? 1 : 0)
      .ThenByDescending(r => r.TotalLaps)
      .ThenBy(r => r.TotalTime)
      .ToList();

    reportData.RiderResults = new List<RiderResult>();

    for (int i = 0; i < sortedRiders.Count; i++)
    {
      var rider = sortedRiders[i];

      // Find rider info for this tag
      var riderInfo = riders.Values.FirstOrDefault(r => r.TagID == rider.TagID);

      var groups = TransponderGroup.Of(rider.Members);

      var result = new RiderResult
      {
        Position = rider.IsDNS ? "DNS" : Dnf(rider) ? "DNF" : (i + 1).ToString(),
        TagID = rider.TagID,
        TransponderText = rider.TransponderText,
        IsTeam = rider.IsTeam,
        MemberLine = rider.IsTeam ? string.Join(" · ", rider.Members!.Select(m => m.Label)) : "",
        MemberLineShort = rider.IsTeam ? string.Join(" · ", rider.Members!.Select(m => m.ShortLabel)) : "",
        BestLapBy = rider.IsTeam ? RiddenBy(rider, rider.BestLap?.CrossedBy) : null,
        MemberBreakdown = TeamMemberStats.For(rider),
        RiderNumber = riderInfo?.RiderNumber ?? "",
        RiderName = riderInfo != null && !string.IsNullOrWhiteSpace(riderInfo.FirstName + riderInfo.LastName)
                    ? $"{riderInfo.FirstName} {riderInfo.LastName}".Trim()
                    : "",
        Team = riderInfo?.Team ?? "",
        Category = riderInfo?.Category ?? "",
        Machine = riderInfo?.Machine ?? "",
        TotalLaps = rider.TotalLaps,
        TotalTime = rider.TotalTime,
        BestLapTime = rider.BestLapTime,
        AverageLapTime = CalculateAverageLapTime(rider),
        IsDNF = Dnf(rider),
        IsDNS = rider.IsDNS,
        LapTimes = rider.Laps.Select((l, index) => new LapResult
        {
          LapNumber = l.LapNumber,
          LapTime = l.LapTime,
          CrossingTime = l.CrossingTime,
          PositionAtCompletion = l.PositionAtCompletion,
          RiddenBy = rider.IsTeam ? RiddenBy(rider, l.CrossedBy) ?? "" : "",
          Note = !rider.IsTeam ? ""
            : l.IsSuspectedOverlap ? "two riders on track?"
            : index > 0 && TwoOnTrackDetector.IsHandover(groups, rider.Laps[index - 1], l) ? "handover"
            : ""
        }).ToList()
      };

      // Calculate gap to leader if not leader and not DNF
      if (i > 0 && !Out(rider) && !Out(sortedRiders[0]))
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
    var finishedRiders = reportData.RiderResults.Where(r => !r.IsDNF && !r.IsDNS).ToList();
    var dnfRiders = reportData.RiderResults.Where(r => r.IsDNF).ToList();
    var dnsRiders = reportData.RiderResults.Where(r => r.IsDNS).ToList();

    reportData.RaceStatistics = new RaceStatistics
    {
      TotalRiders = reportData.RiderResults.Count,
      FinishedRiders = finishedRiders.Count,
      DNFRiders = raceFinished ? dnfRiders.Count : 0, // Only count DNF after race is finished
      // An operator's decision, so counted whether or not the race is over.
      DNSRiders = dnsRiders.Count,
      TotalLapsCompleted = reportData.RiderResults.Sum(r => r.TotalLaps),
      FastestLap = finishedRiders.Where(r => r.BestLapTime.HasValue)
                                .OrderBy(r => r.BestLapTime ?? TimeSpan.MaxValue)
                                .FirstOrDefault(),
      // The winner's elapsed time. Labelled "Winning Time" wherever it is
      // shown: it is not the race duration, and a results sheet that prints it
      // as one contradicts its own start and end times.
      ActualRaceDuration = finishedRiders.FirstOrDefault()?.TotalTime,
      AdditionalLapsSignShown = additionalLapsSignShown,
      RaceActuallyEnded = raceActuallyEnded,
      AdditionalLapsCount = additionalLapsCount
    };
    reportData.Rules = rules;
    reportData.TeamEvent = rules?.TeamEvent == true || reportData.RiderResults.Any(r => r.IsTeam);
    reportData.PrintLines = BuildPrintLines(reportData.RiderResults);

    return reportData;
  }

  /// <summary>Who rode a team's lap, for the sheet: a rider, or the riders sharing that transponder.</summary>
  private static string? RiddenBy(RiderInfo team, string? transponder)
  {
    if (transponder == null) return null;
    if (team.MemberFor(transponder) is { } member) return member.Label;
    return team.Members?.Any(m => m.Owns(transponder)) == true ? "shared transponder" : null;
  }

  /// <summary>"MSC Adler (#14 Ben Fischer)": who set the fastest lap, down to the team rider.</summary>
  private static string DescribeFastest(RiderResult result) =>
    result.BestLapBy != null ? $"{result.DisplayName} ({result.BestLapBy})" : result.DisplayName;

  /// <summary>
  /// What the printed table walks through: every result, then - when there are
  /// teams - each team's riders in finishing order.
  ///
  /// One list with one cursor, rather than a second table with a second cursor:
  /// this class keeps its page cursor on a long-lived instance, and a second one
  /// would be one more thing every entry point has to remember to reset.
  /// </summary>
  public static List<ReportLine> BuildPrintLines(IReadOnlyList<RiderResult> results)
  {
    var lines = results.Select(r => new ReportLine { Kind = ReportLineKind.Result, Result = r }).ToList();

    var teams = results.Where(r => r.IsTeam).ToList();
    if (teams.Count == 0) return lines;

    lines.Add(new ReportLine { Kind = ReportLineKind.TeamsHeading });
    foreach (var team in teams)
    {
      lines.Add(new ReportLine { Kind = ReportLineKind.TeamHeading, Result = team });
      lines.AddRange(team.MemberBreakdown.Select(m =>
        new ReportLine { Kind = ReportLineKind.Member, Result = team, Member = m }));
    }

    return lines;
  }

  private void PrintDocument_PrintPage(object sender, PrintPageEventArgs e)
  {
    if (_reportData == null) return;

    var g = e.Graphics;
    var pageRect = e.PageBounds;
    var printableArea = e.MarginBounds;

    // Fonts - using using statements to ensure non-null
    using var titleFont = new Font("Arial", 18, FontStyle.Bold);
    using var headerFont = new Font("Arial", 12, FontStyle.Bold);
    using var normalFont = new Font("Arial", 10);
    using var smallFont = new Font("Arial", 8);

    float yPos = printableArea.Top;
    float leftMargin = printableArea.Left;
    float rightMargin = printableArea.Right;

    // Only draw title and race info on first page
    if (_currentPage == 0)
    {
      // Title
      var titleText = _reportData.RaceTitle ?? "Race Results";
      var titleSize = g.MeasureString(titleText, titleFont);
      g.DrawString(titleText, titleFont, Brushes.Black,
        leftMargin + (printableArea.Width - titleSize.Width) / 2, yPos);
      yPos += titleSize.Height + 10;

      // Race Information
      yPos = DrawRaceInformation(g, normalFont, headerFont, leftMargin, yPos, printableArea.Width);
      yPos += 15;

      // Race Statistics
      yPos = DrawRaceStatistics(g, normalFont, headerFont, leftMargin, yPos, printableArea.Width);
      yPos += 15;

      _headerHeight = yPos; // Store header height for subsequent pages
    }
    else
    {
      // On subsequent pages, just show title and skip to results
      var titleText = (_reportData.RaceTitle ?? "Race Results") + " (continued)";
      var titleSize = g.MeasureString(titleText, headerFont);
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

  private float DrawRaceInformation(Graphics g, Font normalFont, Font headerFont, float leftMargin, float yPos,
    float width)
  {
    g.DrawString("Race Information", headerFont, Brushes.Black, leftMargin, yPos);
    yPos += g.MeasureString("Race Information", headerFont).Height + 5;

    var infoLines = new List<string>();

    if (_reportData?.RaceStartTime.HasValue == true)
      infoLines.Add($"Start Time: {_reportData.RaceStartTime.Value:yyyy-MM-dd HH:mm:ss}");

    if (_reportData?.RaceEndTime.HasValue == true)
      infoLines.Add($"End Time: {_reportData.RaceEndTime.Value:yyyy-MM-dd HH:mm:ss}");

    infoLines.Add($"Scheduled Duration: {TimeFormat.Clock(_reportData?.RaceDuration ?? TimeSpan.Zero)}");

    if (_reportData?.RaceStatistics?.ActualRaceDuration.HasValue == true)
      infoLines.Add($"Winning Time: {TimeFormat.Precise(_reportData.RaceStatistics.ActualRaceDuration.Value)}");

    infoLines.Add($"Race Status: {(_reportData?.RaceFinished == true ? "Finished" : "In Progress")}");

    // What it was scored under. A sheet that stops at the length cannot
    // settle whether the leader owed one more lap or two.
    if (_reportData?.Rules != null)
      foreach (var (caption, value) in _reportData.Rules.Describe())
        infoLines.Add($"{caption}: {value}");

    yPos = DrawWrappedLines(g, infoLines, normalFont, leftMargin + 20, yPos, width - 20);

    return yPos;
  }

  /// <summary>
  /// Lines that wrap at the right margin instead of running off the page. The
  /// rules a race was scored under are whole sentences, and the longer ones did.
  /// </summary>
  private static float DrawWrappedLines(Graphics g, IEnumerable<string> lines, Font font, float left, float yPos,
    float width)
  {
    foreach (var line in lines)
    {
      var size = g.MeasureString(line, font, (int)width);
      g.DrawString(line, font, Brushes.Black, new RectangleF(left, yPos, width, size.Height));
      yPos += size.Height + 2;
    }
    return yPos;
  }

  /// <summary>In a team event the table lists entries - teams and solo riders - not riders.</summary>
  private string EntriesCaption => _reportData?.TeamEvent == true ? "Entries" : "Total Riders";

  private string DescribeEntries(int total)
  {
    if (_reportData?.TeamEvent != true) return total.ToString();

    var teams = _reportData.RiderResults.Count(r => r.IsTeam);
    var solo = total - teams;
    return $"{total} ({teams} {(teams == 1 ? "team" : "teams")}, {solo} solo {(solo == 1 ? "rider" : "riders")})";
  }

  private float DrawRaceStatistics(Graphics g, Font normalFont, Font headerFont, float leftMargin, float yPos,
    float width)
  {
    g.DrawString("Race Statistics", headerFont, Brushes.Black, leftMargin, yPos);
    yPos += g.MeasureString("Race Statistics", headerFont).Height + 5;

    var stats = _reportData?.RaceStatistics;
    if (stats == null) return yPos;

    var statsLines = new List<string>
    {
      $"{EntriesCaption}: {DescribeEntries(stats.TotalRiders)}",
      $"Finished: {stats.FinishedRiders}"
    };

    // Only show DNF count if race is finished
    if (stats.DNFRiders > 0)
    {
      statsLines.Add($"DNF: {stats.DNFRiders}");
    }

    if (stats.DNSRiders > 0)
    {
      statsLines.Add($"DNS: {stats.DNSRiders}");
    }

    statsLines.Add($"Total Laps Completed: {stats.TotalLapsCompleted}");

    if (stats.FastestLap != null)
    {
      statsLines.Add($"Fastest Lap: {DescribeFastest(stats.FastestLap)} - {TimeFormat.Precise(stats.FastestLap.BestLapTime, "N/A")}");
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

    yPos = DrawWrappedLines(g, statsLines, normalFont, leftMargin + 20, yPos, width - 20);

    return yPos;
  }

  private bool DrawResultsTable(Graphics g, Font normalFont, Font headerFont, Font smallFont,
    Rectangle printableArea, ref float yPos)
  {
    var lines = _reportData?.PrintLines ?? new List<ReportLine>();
    var teamEvent = _reportData?.TeamEvent == true;

    // Only draw "Race Results" header on first page or if we're starting fresh
    if (_currentPage == 0 || _currentRiderIndex == 0)
    {
      g.DrawString("Race Results", headerFont, Brushes.Black, printableArea.Left, yPos);
      yPos += g.MeasureString("Race Results", headerFont).Height + 10;
    }

    // Table headers. Widths are shares of the printable width rather than
    // fixed units: the fixed ones were chosen for minute-long times and left a
    // third of the page unused, and the winner's row, drawn in the larger
    // header font, ran an hours-long total time into the next column.
    //
    // In a team event the name cell has two lines - the team and its riders,
    // or a solo rider and their club - so it takes the Team column's width,
    // and the number column is wider for "101/102".
    var headers = teamEvent
      ? new[] { "Pos", "No.", "Team / Rider", "Laps", "Total Time", "Best Lap", "Gap" }
      : new[] { "Pos", "No.", "Name", "Team", "Laps", "Total Time", "Best Lap", "Gap" };
    var weights = teamEvent
      ? new[] { 0.06f, 0.11f, 0.37f, 0.07f, 0.14f, 0.13f, 0.12f }
      : new[] { 0.06f, 0.08f, 0.24f, 0.17f, 0.07f, 0.14f, 0.12f, 0.12f };
    var columnWidths = weights.Select(w => (int)(printableArea.Width * w)).ToArray();
    var memberWidths = MemberWeights.Select(w => (int)(printableArea.Width * w)).ToArray();

    // Bold at the same size for the winner; the header font is two points
    // larger and was what overflowed.
    using var winnerFont = new Font(normalFont, FontStyle.Bold);

    // Centred, one line, and cut with an ellipsis rather than drawn over the
    // neighbouring cell when a name or a team is still too long.
    using var cellFormat = new StringFormat
    {
      Alignment = StringAlignment.Center,
      LineAlignment = StringAlignment.Center,
      Trimming = StringTrimming.EllipsisCharacter,
      FormatFlags = StringFormatFlags.NoWrap
    };
    using var leftFormat = new StringFormat(cellFormat) { Alignment = StringAlignment.Near };

    // A page starts with the headers of whichever table it continues.
    if (_currentRiderIndex < lines.Count)
    {
      var continuing = lines[_currentRiderIndex].Kind;
      if (continuing == ReportLineKind.Result)
        DrawHeaderRow(g, normalFont, cellFormat, headers, columnWidths, printableArea.Left, ref yPos);
      else if (continuing != ReportLineKind.TeamsHeading)
        DrawHeaderRow(g, normalFont, cellFormat, MemberHeaders, memberWidths, printableArea.Left, ref yPos);
    }

    while (_currentRiderIndex < lines.Count)
    {
      var line = lines[_currentRiderIndex];
      var rowHeight = line.Kind switch
      {
        ReportLineKind.Result => SecondLine(line.Result!, teamEvent).Length > 0 ? 32 : 18,
        ReportLineKind.TeamsHeading => 60,
        ReportLineKind.TeamHeading => 22,
        _ => 18
      };

      // Room for this row and the footer. A team is kept together - its heading
      // and all its riders - and the Team Members heading comes with its first
      // team, so no page starts with a rider cut off from their team. A block
      // taller than half a page may still break rather than waste the page.
      var needed = line.Kind switch
      {
        ReportLineKind.TeamHeading => TeamBlockHeight(lines, _currentRiderIndex),
        ReportLineKind.TeamsHeading => rowHeight +
          (_currentRiderIndex + 1 < lines.Count ? TeamBlockHeight(lines, _currentRiderIndex + 1) : 0),
        _ => rowHeight
      };
      needed = Math.Min(needed, printableArea.Height / 2);

      if (yPos + needed > printableArea.Bottom - 60)
        return true;

      switch (line.Kind)
      {
        case ReportLineKind.Result:
          DrawResultRow(g, normalFont, winnerFont, smallFont, cellFormat, leftFormat, line.Result!,
            columnWidths, printableArea.Left, yPos, rowHeight, teamEvent);
          break;

        case ReportLineKind.TeamsHeading:
          g.DrawString("Team Members", headerFont, Brushes.Black, printableArea.Left, yPos + 18);
          var headerTop = yPos + 40;
          DrawHeaderRow(g, normalFont, cellFormat, MemberHeaders, memberWidths, printableArea.Left, ref headerTop);
          break;

        case ReportLineKind.TeamHeading:
          var team = line.Result!;
          var headingRect = new Rectangle(printableArea.Left, (int)yPos, memberWidths.Sum(), rowHeight);
          g.FillRectangle(Brushes.Gainsboro, headingRect);
          g.DrawRectangle(Pens.Black, headingRect);
          var name = team.RiderNumber.Length > 0 ? $"#{team.RiderNumber} {team.RiderName}" : team.RiderName;
          var laps = team.TotalLaps == 1 ? "1 lap" : $"{team.TotalLaps} laps";
          headingRect.Inflate(-6, 0);
          g.DrawString($"{team.Position}.  {name}  -  {laps}", winnerFont, Brushes.Black, headingRect, leftFormat);
          break;

        case ReportLineKind.Member:
          DrawMemberRow(g, normalFont, smallFont, cellFormat, leftFormat, line.Member!, memberWidths,
            printableArea.Left, yPos, rowHeight);
          break;
      }

      yPos += rowHeight;
      _currentRiderIndex++;
    }

    // Return false if we've drawn every line
    return false;
  }

  /// <summary>A team's heading and the riders listed under it, in page units.</summary>
  private static int TeamBlockHeight(List<ReportLine> lines, int headingIndex)
  {
    var height = 22;
    for (var i = headingIndex + 1; i < lines.Count && lines[i].Kind == ReportLineKind.Member; i++)
      height += 18;
    return height;
  }

  /// <summary>
  /// The end of a transponder code when the whole code does not fit. The end is
  /// what differs from one tag to the next, and what is read off the tag.
  /// </summary>
  private static string FitTail(Graphics g, string text, Font font, float width)
  {
    if (g.MeasureString(text, font).Width <= width) return text;

    for (var cut = 1; cut < text.Length; cut++)
    {
      var candidate = "…" + text[cut..];
      if (g.MeasureString(candidate, font).Width <= width) return candidate;
    }

    return text;
  }

  private static readonly string[] MemberHeaders = { "Rider", "Transponder", "Laps", "Best Lap", "Avg Lap" };
  private static readonly float[] MemberWeights = { 0.40f, 0.26f, 0.08f, 0.13f, 0.13f };

  /// <summary>The smaller line under a name in a team event: a team's riders, or a solo rider's club.</summary>
  private static string SecondLine(RiderResult result, bool teamEvent) =>
    !teamEvent ? "" : result.IsTeam ? result.MemberLineShort : result.Team ?? "";

  private static void DrawHeaderRow(Graphics g, Font font, StringFormat format, string[] headers, int[] widths,
    float left, ref float yPos)
  {
    float xPos = left;
    for (int i = 0; i < headers.Length; i++)
    {
      var headerRect = new Rectangle((int)xPos, (int)yPos, widths[i], 20);
      g.FillRectangle(Brushes.LightGray, headerRect);
      g.DrawRectangle(Pens.Black, headerRect);
      g.DrawString(headers[i], font, Brushes.Black, headerRect, format);
      xPos += widths[i];
    }
    yPos += 20;
  }

  private void DrawResultRow(Graphics g, Font normalFont, Font winnerFont, Font smallFont, StringFormat cellFormat,
    StringFormat leftFormat, RiderResult result, int[] columnWidths, float left, float top, int rowHeight,
    bool teamEvent)
  {
    var name = !string.IsNullOrWhiteSpace(result.RiderName) ? result.RiderName : result.DisplayName;
    var laps = result.TotalLaps.ToString();
    var total = TimeFormat.Precise(result.TotalTime);
    var best = TimeFormat.Precise(result.BestLapTime, "N/A");
    var gap = GetGapText(result);

    var rowData = teamEvent
      ? new[] { result.Position, result.RiderNumber ?? "", name, laps, total, best, gap }
      : new[] { result.Position, result.RiderNumber ?? "", name, result.Team ?? "", laps, total, best, gap };

    // Color coding for position
    Brush backgroundBrush = Brushes.White;
    if (result.Position == "1") backgroundBrush = Brushes.LightGoldenrodYellow;
    else if (result.Position == "2") backgroundBrush = Brushes.LightGray;
    else if (result.Position == "3") backgroundBrush = Brushes.Wheat;
    else if (result.IsDNF || result.IsDNS) backgroundBrush = Brushes.MistyRose;

    var textBrush = result.IsDNF || result.IsDNS ? Brushes.DarkRed : Brushes.Black;
    var font = result.Position == "1" ? winnerFont : normalFont;
    var second = SecondLine(result, teamEvent);

    float xPos = left;
    for (int i = 0; i < rowData.Length; i++)
    {
      var cellRect = new Rectangle((int)xPos, (int)top, columnWidths[i], rowHeight);
      g.FillRectangle(backgroundBrush, cellRect);
      g.DrawRectangle(Pens.Black, cellRect);

      if (teamEvent && i == 2)
      {
        // Left-aligned, so the riders line sits under the name it belongs to.
        var nameRect = new Rectangle(cellRect.X + 6, cellRect.Y, cellRect.Width - 12, second.Length > 0 ? 18 : rowHeight);
        g.DrawString(rowData[i], font, textBrush, nameRect, leftFormat);

        if (second.Length > 0)
        {
          var secondRect = new Rectangle(cellRect.X + 6, cellRect.Y + 16, cellRect.Width - 12, rowHeight - 17);
          g.DrawString(second, smallFont, result.IsDNF || result.IsDNS ? Brushes.DarkRed : Brushes.DimGray, secondRect, leftFormat);
        }
      }
      else
      {
        g.DrawString(rowData[i], font, textBrush, cellRect, cellFormat);
      }

      xPos += columnWidths[i];
    }
  }

  private static void DrawMemberRow(Graphics g, Font font, Font smallFont, StringFormat cellFormat,
    StringFormat leftFormat, TeamMemberLine member, int[] widths, float left, float top, int rowHeight)
  {
    void Box(Rectangle r)
    {
      g.FillRectangle(Brushes.White, r);
      g.DrawRectangle(Pens.Black, r);
    }

    var x = (int)left;
    var y = (int)top;
    var riderRect = new Rectangle(x, y, widths[0], rowHeight); x += widths[0];
    var tagsRect = new Rectangle(x, y, widths[1], rowHeight); x += widths[1];
    var lapsRect = new Rectangle(x, y, widths[2], rowHeight); x += widths[2];
    var bestRect = new Rectangle(x, y, widths[3], rowHeight); x += widths[3];
    var avgRect = new Rectangle(x, y, widths[4], rowHeight);

    var brush = member.IsUnattributed ? Brushes.DimGray : Brushes.Black;

    Box(riderRect);
    Box(tagsRect);
    Box(lapsRect);

    // Indented under the team's heading.
    var riderText = new Rectangle(riderRect.X + 14, riderRect.Y, riderRect.Width - 20, rowHeight);
    // Riders sharing a transponder by surname, so both names fit.
    var riderLabel = member.SharedTransponder
      ? string.Join(" / ", member.Group!.Members.Select(m => m.ShortLabel))
      : member.Label;
    g.DrawString(riderLabel, font, brush, riderText, leftFormat);
    g.DrawString(FitTail(g, string.Join(", ", member.Transponders), smallFont, tagsRect.Width - 8),
      smallFont, Brushes.DimGray, tagsRect, cellFormat);
    g.DrawString(member.LapsRidden.ToString(), font, brush, lapsRect, cellFormat);

    if (member.SharedTransponder)
    {
      // One transponder cannot say which of them rode, so there is no time to give.
      var both = Rectangle.Union(bestRect, avgRect);
      Box(both);
      g.DrawString("shared transponder - no times", smallFont, Brushes.DimGray, both, cellFormat);
      return;
    }

    Box(bestRect);
    Box(avgRect);
    g.DrawString(TimeFormat.Precise(member.BestLap, "-"), font, brush, bestRect, cellFormat);
    g.DrawString(TimeFormat.Precise(member.AverageLap, "-"), font, brush, avgRect, cellFormat);
  }

  private string GetGapText(RiderResult result)
  {
    if (result.Position == "1") return "Leader";
    if (result.IsDNS) return "DNS";
    if (result.IsDNF) return "DNF";

    if (result.LapGapToLeader > 0)
      return $"-{result.LapGapToLeader} lap{(result.LapGapToLeader == 1 ? "" : "s")}";

    if (result.GapToLeader.HasValue)
      return $"+{TimeFormat.Clock(result.GapToLeader.Value)}";

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

    sb.AppendLine($"Scheduled Duration: {TimeFormat.Clock(_reportData.RaceDuration)}");

    if (_reportData.RaceStatistics?.ActualRaceDuration.HasValue == true)
      sb.AppendLine($"Winning Time:      {TimeFormat.Precise(_reportData.RaceStatistics.ActualRaceDuration.Value)}");

    sb.AppendLine($"Race Status:       {(_reportData.RaceFinished ? "Finished" : "In Progress")}");

    if (_reportData.Rules != null)
      foreach (var (caption, value) in _reportData.Rules.Describe())
        sb.AppendLine($"{caption + ":",-19}{value}");
    sb.AppendLine();

    // Race statistics
    sb.AppendLine("RACE STATISTICS:");
    sb.AppendLine(new string('-', 40));
    var stats = _reportData.RaceStatistics;
    if (stats != null)
    {
      sb.AppendLine($"{EntriesCaption + ":",-19}{DescribeEntries(stats.TotalRiders)}");
      sb.AppendLine($"Finished:          {stats.FinishedRiders}");

      // Only show DNF count if race is finished
      if (stats.DNFRiders > 0)
      {
        sb.AppendLine($"DNF:               {stats.DNFRiders}");
      }

      if (stats.DNSRiders > 0)
      {
        sb.AppendLine($"DNS:               {stats.DNSRiders}");
      }

      sb.AppendLine($"Total Laps:        {stats.TotalLapsCompleted}");

      if (stats.FastestLap != null)
        sb.AppendLine($"Fastest Lap:       {DescribeFastest(stats.FastestLap)} - {TimeFormat.Precise(stats.FastestLap.BestLapTime, "N/A")}");

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
    sb.AppendLine(new string('=', 160)); // Increased width to accommodate longer tag IDs
    sb.AppendLine($"{"Pos",-4} {"Transponder",-35} {"Name",-20} {(_reportData.TeamEvent ? "Riders" : "Team"),-15} {"Laps",-5} {"Total Time",-12} {"Best Lap",-10} {"Avg Lap",-10} {"Gap",-15}");
    sb.AppendLine(new string('-', 160)); // Increased width to accommodate longer tag IDs

    foreach (var result in _reportData.RiderResults)
    {
      var gapText = GetGapText(result);
      var bestLap = TimeFormat.Precise(result.BestLapTime, "N/A");
      var avgLap = TimeFormat.Precise(result.AverageLapTime, "N/A");
      var riderName = !string.IsNullOrWhiteSpace(result.RiderName) ? result.RiderName : "";
      var team = !result.IsTeam && !string.IsNullOrWhiteSpace(result.Team) ? result.Team : "";

      // Truncate long names/teams if needed to fit format
      if (riderName.Length > 19) riderName = riderName[..16] + "...";
      if (team.Length > 14) team = team[..11] + "...";

      var transponder = result.TransponderText.Length > 34 ? result.TransponderText[..31] + "..." : result.TransponderText;

      sb.AppendLine($"{result.Position,-4} {transponder,-35} {riderName,-20} {team,-15} {result.TotalLaps,-5} " +
                   $"{TimeFormat.Precise(result.TotalTime),-12} {bestLap,-10} {avgLap,-10} {gapText,-15}");

      if (result.IsTeam && result.MemberLine.Length > 0)
        sb.AppendLine($"{"",-4} {"",-35} {result.MemberLine}");
    }

    sb.AppendLine(new string('=', 160)); // Increased width to accommodate longer tag IDs

    var teams = _reportData.RiderResults.Where(r => r.IsTeam).ToList();
    if (teams.Count > 0)
    {
      sb.AppendLine();
      sb.AppendLine("TEAM MEMBERS:");
      sb.AppendLine(new string('-', 100));

      foreach (var result in teams)
      {
        sb.AppendLine($"{result.Position}.  #{result.RiderNumber} {result.RiderName} - " +
                      (result.TotalLaps == 1 ? "1 lap" : $"{result.TotalLaps} laps"));
        foreach (var member in result.MemberBreakdown)
        {
          var who = member.Label.Length > 44 ? member.Label[..41] + "..." : member.Label;
          var tags = string.Join(", ", member.Transponders);
          if (tags.Length > 34) tags = tags[..31] + "...";
          var times = member.SharedTransponder
            ? "shared transponder - no times"
            : $"best {TimeFormat.Precise(member.BestLap, "-"),-10} avg {TimeFormat.Precise(member.AverageLap, "-")}";
          sb.AppendLine($"     {who,-44} {tags,-34} {member.LapsRidden,3} laps   {times}");
        }
      }
    }

    sb.AppendLine();
    sb.AppendLine($"Report generated: {_reportData.GeneratedAt:yyyy-MM-dd HH:mm:ss}");

    return sb.ToString();
  }

  private TimeSpan? CalculateAverageLapTime(RiderInfo rider)
  {
    // Skip the first lap: it runs from the race start to the first crossing and
    // is not a lap, so including it drags the average below anything the rider
    // actually rode.
    var validLapTimes = rider.Laps.Skip(1).Where(l => l.LapTime.HasValue).Select(l => l.LapTime!.Value).ToList();

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

    // Who rode what, per team, in a team event.
    if (_reportData.RiderResults.Any(r => r.IsTeam))
      CreateTeamMembersSheet(workbook.Worksheets.Add("Team Members"));

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

    if (_reportData.RaceStartTime.HasValue && _reportData.RaceEndTime.HasValue)
    {
      sheet.Cell(currentRow, 1).Value = "Race Duration:";
      sheet.Cell(currentRow, 2).Value =
        (_reportData.RaceEndTime.Value - _reportData.RaceStartTime.Value).ToString(@"hh\:mm\:ss");
      currentRow++;
    }

    if (_reportData.Statistics?.ActualRaceDuration.HasValue == true)
    {
      sheet.Cell(currentRow, 1).Value = "Winning Time:";
      sheet.Cell(currentRow, 2).Value = _reportData.Statistics.ActualRaceDuration.Value.ToString(@"hh\:mm\:ss\.fff");
      currentRow++;
    }

    sheet.Cell(currentRow, 1).Value = "Scheduled Duration:";
    sheet.Cell(currentRow, 2).Value = _reportData.RaceDuration.ToString(@"hh\:mm\:ss");
    currentRow++;

    if (_reportData.Rules != null)
    {
      foreach (var (caption, value) in _reportData.Rules.Describe())
      {
        sheet.Cell(currentRow, 1).Value = caption + ":";
        sheet.Cell(currentRow, 2).Value = value;
        currentRow++;
      }
    }

    sheet.Cell(currentRow, 1).Value = "Generated:";
    sheet.Cell(currentRow, 2).Value = _reportData.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss");
    currentRow += 2;

    // Headers
    var tableStart = currentRow;
    var headers = new List<string> { "Position", "Transponder", "Number", "Rider Name", "Team", "Category", "Laps", "Total Time", "Best Lap", "Avg Lap", "Gap", "Status" };
    if (_reportData.TeamEvent) headers.Add("Riders");
    for (int i = 0; i < headers.Count; i++)
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
      sheet.Cell(currentRow, 2).Value = rider.TransponderText;
      sheet.Cell(currentRow, 3).Value = rider.RiderNumber ?? "";
      sheet.Cell(currentRow, 4).Value = rider.RiderName ?? "";
      // A team's name is already the rider name; the column is for a solo rider's club.
      sheet.Cell(currentRow, 5).Value = rider.IsTeam ? "" : rider.Team ?? "";
      sheet.Cell(currentRow, 6).Value = rider.Category ?? "";
      sheet.Cell(currentRow, 7).Value = rider.TotalLaps;
      sheet.Cell(currentRow, 8).Value = rider.TotalTime.ToString(@"hh\:mm\:ss\.fff");
      sheet.Cell(currentRow, 9).Value = TimeFormat.Precise(rider.BestLapTime, "N/A");
      sheet.Cell(currentRow, 10).Value = TimeFormat.Precise(rider.AverageLapTime, "N/A");
      // The same words as the printed sheet. Gap only ever held a time, so a rider
      // laps down had an empty cell.
      sheet.Cell(currentRow, 11).Value = GetGapText(rider);
      sheet.Cell(currentRow, 12).Value = rider.Status;
      if (_reportData.TeamEvent) sheet.Cell(currentRow, 13).Value = rider.MemberLine;

      // Color coding for positions
      if (rider.Position == "1" && rider.Status != "DNF")
        sheet.Row(currentRow).Style.Fill.BackgroundColor = XLColor.Gold;
      else if (rider.Position == "2" && rider.Status != "DNF")
        sheet.Row(currentRow).Style.Fill.BackgroundColor = XLColor.Silver;
      else if (rider.Position == "3" && rider.Status != "DNF")
        sheet.Row(currentRow).Style.Fill.BackgroundColor = XLColor.FromArgb(205, 127, 50); // Bronze
      else if (rider.IsDNF || rider.IsDNS)
        sheet.Row(currentRow).Style.Fill.BackgroundColor = XLColor.LightGray;

      // Add borders
      sheet.Range(currentRow, 1, currentRow, headers.Count).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

      currentRow++;
    }

    // Fitted to the table only: the rules above it are whole sentences, and
    // fitting to those made the transponder column a hundred characters wide.
    sheet.Columns().AdjustToContents(tableStart, currentRow);
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

    // A team event says who rode each lap, and which laps were handovers.
    var lapColumns = _reportData.TeamEvent ? 6 : 4;

    foreach (var rider in _reportData.RiderResults)
    {
      // Rider header
      var riderDisplay = !string.IsNullOrWhiteSpace(rider.RiderName)
        ? $"{rider.RiderName} (Transponder: {rider.TransponderText})"
        : $"Transponder: {rider.TransponderText}";
      sheet.Cell(currentRow, 1).Value = $"Rider: {riderDisplay} (Position: {rider.Position})";
      sheet.Cell(currentRow, 1).Style.Font.Bold = true;
      sheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.LightBlue;
      sheet.Range(currentRow, 1, currentRow, lapColumns).Merge();
      currentRow++;

      // Lap headers
      sheet.Cell(currentRow, 1).Value = "Lap";
      sheet.Cell(currentRow, 2).Value = "Lap Time";
      sheet.Cell(currentRow, 3).Value = "Crossing Time";
      sheet.Cell(currentRow, 4).Value = "Total Time";
      if (_reportData.TeamEvent)
      {
        sheet.Cell(currentRow, 5).Value = "Ridden By";
        sheet.Cell(currentRow, 6).Value = "Note";
      }

      sheet.Range(currentRow, 1, currentRow, lapColumns).Style.Font.Bold = true;
      sheet.Range(currentRow, 1, currentRow, lapColumns).Style.Fill.BackgroundColor = XLColor.LightGray;
      currentRow++;

      var totalTime = TimeSpan.Zero;
      foreach (var lap in rider.LapTimes)
      {
        sheet.Cell(currentRow, 1).Value = lap.LapNumber;
        sheet.Cell(currentRow, 2).Value = TimeFormat.Precise(lap.LapTime, "N/A");
        sheet.Cell(currentRow, 3).Value = lap.CrossingTime.ToString("HH:mm:ss.fff");

        if (lap.LapTime.HasValue)
          totalTime += lap.LapTime.Value;
        sheet.Cell(currentRow, 4).Value = totalTime.ToString(@"hh\:mm\:ss\.fff");

        if (_reportData.TeamEvent)
        {
          sheet.Cell(currentRow, 5).Value = lap.RiddenBy;
          sheet.Cell(currentRow, 6).Value = lap.Note;
        }

        currentRow++;
      }

      currentRow += 1; // Space between riders
    }

    // Auto-fit columns
    sheet.Columns().AdjustToContents();
  }

  /// <summary>
  /// One row per team rider: the laps they rode and their own times. See
  /// <see cref="TeamMemberStats"/> for which laps count as a rider's time.
  /// </summary>
  private void CreateTeamMembersSheet(IXLWorksheet sheet)
  {
    if (_reportData == null) return;

    var headers = new[] { "Position", "Team", "Rider", "Transponder", "Laps Ridden", "Best Lap", "Avg Lap", "Note" };
    for (int i = 0; i < headers.Length; i++)
    {
      var cell = sheet.Cell(1, i + 1);
      cell.Value = headers[i];
      cell.Style.Font.Bold = true;
      cell.Style.Fill.BackgroundColor = XLColor.LightGray;
      cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
    }

    var row = 2;
    foreach (var team in _reportData.RiderResults.Where(r => r.IsTeam))
    {
      foreach (var member in team.MemberBreakdown)
      {
        sheet.Cell(row, 1).Value = team.Position;
        sheet.Cell(row, 2).Value = team.RiderName;
        sheet.Cell(row, 3).Value = member.Label;
        sheet.Cell(row, 4).Value = string.Join(", ", member.Transponders);
        sheet.Cell(row, 5).Value = member.LapsRidden;
        sheet.Cell(row, 6).Value = TimeFormat.Precise(member.BestLap, "-");
        sheet.Cell(row, 7).Value = TimeFormat.Precise(member.AverageLap, "-");
        sheet.Cell(row, 8).Value = member.SharedTransponder
          ? "shared transponder - laps not split per rider"
          : member.IsUnattributed ? "no recorded rider" : "";
        row++;
      }
    }

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
    sheet.Cell(currentRow, 1).Value = EntriesCaption + ":";
    sheet.Cell(currentRow, 2).Value = stats.TotalRiders;
    currentRow++;

    sheet.Cell(currentRow, 1).Value = "Finished:";
    sheet.Cell(currentRow, 2).Value = stats.FinishedRiders;
    currentRow++;

    if (stats.DNFRiders > 0)
    {
      sheet.Cell(currentRow, 1).Value = "DNF Riders:";
      sheet.Cell(currentRow, 2).Value = stats.DNFRiders;
      currentRow++;
    }

    if (stats.DNSRiders > 0)
    {
      sheet.Cell(currentRow, 1).Value = "DNS Riders:";
      sheet.Cell(currentRow, 2).Value = stats.DNSRiders;
      currentRow++;
    }

    sheet.Cell(currentRow, 1).Value = "Total Laps Completed:";
    sheet.Cell(currentRow, 2).Value = stats.TotalLapsCompleted;
    currentRow++;

    if (stats.FastestLap != null)
    {
      sheet.Cell(currentRow, 1).Value = "Fastest Lap:";
      sheet.Cell(currentRow, 2).Value = $"{TimeFormat.Precise(stats.FastestLap.BestLapTime, "N/A")} by {DescribeFastest(stats.FastestLap)}";
      currentRow++;
    }

    if (stats.ActualRaceDuration.HasValue)
    {
      sheet.Cell(currentRow, 1).Value = "Winning Time:";
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
      sheet.Cell(currentRow, 2).Value = TimeFormat.Clock(signTime);
      currentRow++;

      if (stats.RaceActuallyEnded.HasValue)
      {
        var endTime = stats.RaceActuallyEnded.Value - _reportData.RaceStartTime.Value;
        sheet.Cell(currentRow, 1).Value = "Race Actually Ended:";
        sheet.Cell(currentRow, 2).Value = TimeFormat.Clock(endTime);
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

  /// <summary>What the race was scored under, or null for a caller that did not say.</summary>
  public RaceRules? Rules { get; set; }

  /// <summary>Riders sharing a team name were scored as one entry: the sheet names each team's riders.</summary>
  public bool TeamEvent { get; set; }

  /// <summary>What the printed table walks through, in order. See RaceReportGenerator.BuildPrintLines.</summary>
  public List<ReportLine> PrintLines { get; set; } = new();
}

public enum ReportLineKind
{
  /// <summary>A row of the results table.</summary>
  Result,
  /// <summary>The start of the team members table.</summary>
  TeamsHeading,
  /// <summary>One team, above its riders.</summary>
  TeamHeading,
  /// <summary>One rider of a team, or riders sharing a transponder.</summary>
  Member
}

/// <summary>One line of the printed results.</summary>
public sealed class ReportLine
{
  public ReportLineKind Kind { get; init; }

  /// <summary>The result the line belongs to; for a member, their team's.</summary>
  public RiderResult? Result { get; init; }

  public TeamMemberLine? Member { get; init; }
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
  public bool IsDNS { get; set; }
  public TimeSpan? GapToLeader { get; set; }
  public int LapGapToLeader { get; set; }
  public List<LapResult> LapTimes { get; set; } = new();

  /// <summary>The transponder code(s) to print. A team's TagID is its key, not a transponder.</summary>
  public string TransponderText { get; set; } = "";

  public bool IsTeam { get; set; }

  /// <summary>A team's riders: "#11 Anna Berger · #14 Ben Fischer".</summary>
  public string MemberLine { get; set; } = "";

  /// <summary>
  /// The same, by surname: "#11 Berger · #14 Fischer". The printed table has one
  /// narrow line for it; the full names are in the Team Members section below.
  /// </summary>
  public string MemberLineShort { get; set; } = "";

  /// <summary>Which team rider set the best lap, when that is known.</summary>
  public string? BestLapBy { get; set; }

  public IReadOnlyList<TeamMemberLine> MemberBreakdown { get; set; } = Array.Empty<TeamMemberLine>();

  // Additional properties for Excel export
  public string Gap => GapToLeader?.ToString(@"hh\:mm\:ss") ?? "";
  public string Status => IsDNS ? "DNS" : IsDNF ? "DNF" : "Finished";

  /// <summary>
  /// Display name for the rider (name if available, otherwise tag ID)
  /// </summary>
  public string DisplayName
  {
    get
    {
      if (!string.IsNullOrEmpty(RiderName))
        return RiderName;
      return TransponderText.Length > 0 ? TransponderText : TagID;
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

  /// <summary>
  /// Where the rider stood at the end of this lap, or 0 when it was never
  /// recorded. See <see cref="RiderLap.PositionAtCompletion"/>.
  /// </summary>
  public int PositionAtCompletion { get; set; }

  /// <summary>For a team: the rider whose transponder ended the lap.</summary>
  public string RiddenBy { get; set; } = "";

  /// <summary>For a team: "handover" or "two riders on track?".</summary>
  public string Note { get; set; } = "";
}

/// <summary>
/// Overall race statistics
/// </summary>
public class RaceStatistics
{
  public int TotalRiders { get; set; }
  public int FinishedRiders { get; set; }
  public int DNFRiders { get; set; }
  public int DNSRiders { get; set; }
  public int TotalLapsCompleted { get; set; }
  public RiderResult? FastestLap { get; set; }
  public TimeSpan? ActualRaceDuration { get; set; }
  public DateTime? AdditionalLapsSignShown { get; set; }
  public DateTime? RaceActuallyEnded { get; set; }
  public int AdditionalLapsCount { get; set; }
}
