namespace CrossMgrInterface;

/// <summary>
/// The transponder check: which riders the loop is reading reliably.
///
/// Lives on the practice and qualifying sessions, because that is when a tag can
/// still be re-fitted. By the race it is too late to do anything but explain.
/// </summary>
public partial class Form1
{
  private TransponderCheckView _transponderView = null!;
  private TabPage tabPageTransponder = null!;
  private TransponderCheckReportGenerator _transponderReportGenerator = null!;

  private void InitializeTransponderView()
  {
    _transponderView = new TransponderCheckView();
    tabPageTransponder = _transponderView.CreateTransponderTab();
    _transponderReportGenerator = new TransponderCheckReportGenerator();

    _transponderView.PrintRequested += (_, _) => ShowTransponderReport();
    _transponderView.RiderActivated += (_, tagId) => OpenLapCorrection(tagId);
  }

  /// <summary>
  /// How many reads were thrown away per tag as too close to the previous one.
  ///
  /// Counted from the rejected-read list rather than the laps, because by
  /// definition these never became laps.
  /// </summary>
  private Dictionary<string, int> DuplicateReadsByTag()
  {
    var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    lock (ridersLock)
    {
      foreach (var read in rejectedReads)
      {
        if (read.Restored) continue;
        counts[read.TagID] = counts.TryGetValue(read.TagID, out var n) ? n + 1 : 1;
      }
    }

    return counts;
  }

  private List<TransponderFinding> RunTransponderCheck()
  {
    var field = BuildSessionField();
    return TransponderCheck.Run(field, DuplicateReadsByTag(), RaceProgress.MedianPace(field));
  }

  private void RenderTransponderCheck()
  {
    var findings = RunTransponderCheck();
    var rows = new List<TransponderCheckRowData>(findings.Count);

    foreach (var finding in findings)
    {
      var cells = new string[TransponderCheckRowData.ColumnCount];
      cells[TransponderCheckRowData.ColRiderNumber] = finding.Rider.RiderNumber;
      cells[TransponderCheckRowData.ColRiderName] = finding.Rider.Label;
      cells[TransponderCheckRowData.ColCategory] = finding.Rider.Category;
      cells[TransponderCheckRowData.ColLaps] = finding.Laps.ToString();
      cells[TransponderCheckRowData.ColMisses] =
        finding.SuspectedMisses > 0 ? finding.SuspectedMisses.ToString() : "";
      cells[TransponderCheckRowData.ColDuplicates] =
        finding.DuplicateReads > 0 ? finding.DuplicateReads.ToString() : "";
      cells[TransponderCheckRowData.ColDetail] = finding.Detail;

      rows.Add(new TransponderCheckRowData
      {
        TagID = finding.Rider.TagID,
        Cells = cells,
        RowBackColor = finding.Verdict switch
        {
          TransponderVerdict.NeverRead => Color.FromArgb(255, 205, 205),
          TransponderVerdict.WentQuiet => Color.FromArgb(255, 228, 196),
          TransponderVerdict.Intermittent => Color.FromArgb(255, 242, 204),
          TransponderVerdict.DoubleReads => Color.FromArgb(255, 250, 225),
          _ => Color.Empty
        },
        RowForeColor = finding.Verdict == TransponderVerdict.Clean ? Color.DimGray : Color.Black,
        NeedsAttention = finding.Verdict != TransponderVerdict.Clean,
        Tooltip = TransponderCheck.Advice(finding.Verdict)
      });
    }

    var problems = findings.Count(f => f.Verdict != TransponderVerdict.Clean);
    _transponderView.SetRows(rows, DescribeTransponderHeadline(findings.Count, problems), problems > 0);
  }

  private static string DescribeTransponderHeadline(int total, int problems)
  {
    if (total == 0) return "Nobody has been out yet.";
    if (problems == 0) return $"All {total} transponders reading cleanly.";
    return problems == 1
      ? $"1 of {total} riders needs attention."
      : $"{problems} of {total} riders need attention.";
  }

  /// <summary>Preview, print or save the transponder check.</summary>
  private void ShowTransponderReport()
  {
    try
    {
      var findings = RunTransponderCheck();

      if (findings.Count == 0)
      {
        MessageBox.Show(this,
          "Nobody has been out and no rider list has been imported, so there is " +
          "nothing to check yet.",
          "Nothing to check", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
      }

      DateTime? sessionStart;
      TimeSpan sessionDuration;
      lock (ridersLock)
      {
        sessionStart = raceStartTime;
        sessionDuration = raceDuration;
      }

      var defaultTitle = string.IsNullOrWhiteSpace(raceName)
        ? $"Transponder Check - {DateTime.Now:yyyy-MM-dd HH:mm}"
        : $"{raceName} - Transponder Check";

      using var options = new ReportOptionsDialog(defaultTitle);
      if (options.ShowDialog(this) != DialogResult.OK) return;

      switch (options.SelectedAction)
      {
        case ReportAction.Preview:
          _transponderReportGenerator.ShowPrintPreview(findings, options.RaceTitle, sessionStart, sessionDuration);
          break;
        case ReportAction.Print:
          _transponderReportGenerator.PrintReport(findings, options.RaceTitle, sessionStart, sessionDuration);
          break;
        case ReportAction.Export:
          _transponderReportGenerator.ExportToFile(findings, options.RaceTitle, sessionStart, sessionDuration);
          break;
      }
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(this, "The transponder check could not be produced.",
        "Nothing has been changed. The lap data is unaffected, so you can try again.", ex);
    }
  }
}
