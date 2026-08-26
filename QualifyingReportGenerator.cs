using System.Drawing.Printing;
using System.Text;
using ClosedXML.Excel;

namespace CrossMgrInterface;

/// <summary>
/// Prints the gate pick order: the field ranked on best lap, which decides the
/// order riders choose their starting gate in for the race.
///
/// Deliberately a separate class from <see cref="RaceReportGenerator"/> rather
/// than another method on it. That class carries its pagination cursor on a
/// long-lived instance and every entry point has to remember to reset it; a
/// second report sharing those fields would inherit the hazard for no gain.
/// This one renders a single table, so its pagination is a single cursor.
///
/// Everything it prints comes from <see cref="QualifyingRanking"/>, which is
/// also what the Qualifying tab draws, so the screen and the paper cannot
/// disagree about who picks first.
/// </summary>
public sealed class QualifyingReportGenerator : IDisposable
{
  private readonly PrintDocument _printDocument;
  private QualifyingReportData? _data;
  private int _nextEntryIndex;
  private int _pageNumber;

  public QualifyingReportGenerator()
  {
    _printDocument = new PrintDocument();
    _printDocument.PrintPage += PrintDocument_PrintPage;
  }

  // ---- Entry points --------------------------------------------------------

  public void ShowPrintPreview(Dictionary<string, RiderInfo> riders, string title,
    DateTime? sessionStart, DateTime? sessionEnd, TimeSpan sessionDuration, bool sessionFinished)
  {
    Prepare(riders, title, sessionStart, sessionEnd, sessionDuration, sessionFinished);

    using var preview = new PrintPreviewDialog
    {
      Document = _printDocument,
      WindowState = FormWindowState.Maximized
    };
    preview.ShowDialog();
  }

  /// <summary>Overall first, then one preview per class, as the results report does.</summary>
  public void ShowClassBasedPrintPreview(Dictionary<string, RiderInfo> riders, string title,
    DateTime? sessionStart, DateTime? sessionEnd, TimeSpan sessionDuration, bool sessionFinished)
  {
    var classes = ReportHelpers.GetUniqueClasses(riders);

    if (classes.Count <= 1)
    {
      ShowPrintPreview(riders, title, sessionStart, sessionEnd, sessionDuration, sessionFinished);
      return;
    }

    ShowPrintPreview(riders, $"{title} - Overall", sessionStart, sessionEnd, sessionDuration, sessionFinished);

    foreach (var className in classes)
    {
      var classRiders = ReportHelpers.FilterRidersByClass(riders, className);
      if (classRiders.Count == 0) continue;

      ShowPrintPreview(classRiders, $"{title} - Class: {className}",
        sessionStart, sessionEnd, sessionDuration, sessionFinished);
    }
  }

  public void PrintReport(Dictionary<string, RiderInfo> riders, string title,
    DateTime? sessionStart, DateTime? sessionEnd, TimeSpan sessionDuration, bool sessionFinished)
  {
    var classes = ReportHelpers.GetUniqueClasses(riders);

    if (classes.Count <= 1)
    {
      PrintOne(riders, title, sessionStart, sessionEnd, sessionDuration, sessionFinished);
      return;
    }

    var answer = MessageBox.Show(
      $"There are {classes.Count} classes in this session.\n\n" +
      "Yes: print the overall sheet and one per class\n" +
      "No: print the overall sheet only\n" +
      "Cancel: do not print",
      "Print gate pick order", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

    switch (answer)
    {
      case DialogResult.Yes:
        PrintOne(riders, $"{title} - Overall", sessionStart, sessionEnd, sessionDuration, sessionFinished);
        foreach (var className in classes)
        {
          var classRiders = ReportHelpers.FilterRidersByClass(riders, className);
          if (classRiders.Count == 0) continue;
          PrintOne(classRiders, $"{title} - Class: {className}",
            sessionStart, sessionEnd, sessionDuration, sessionFinished);
        }
        break;

      case DialogResult.No:
        PrintOne(riders, $"{title} - Overall", sessionStart, sessionEnd, sessionDuration, sessionFinished);
        break;
    }
  }

  public void ExportToFile(Dictionary<string, RiderInfo> riders, string title,
    DateTime? sessionStart, DateTime? sessionEnd, TimeSpan sessionDuration, bool sessionFinished)
  {
    var classes = ReportHelpers.GetUniqueClasses(riders);

    if (classes.Count <= 1)
    {
      ExportSingle(riders, title, sessionStart, sessionEnd, sessionDuration, sessionFinished);
      return;
    }

    ExportPerClass(riders, title, sessionStart, sessionEnd, sessionDuration, sessionFinished, classes);
  }

  // ---- Data ----------------------------------------------------------------

  private void Prepare(Dictionary<string, RiderInfo> riders, string title,
    DateTime? sessionStart, DateTime? sessionEnd, TimeSpan sessionDuration, bool sessionFinished)
  {
    _data = new QualifyingReportData
    {
      Title = title,
      SessionStart = sessionStart,
      SessionEnd = sessionEnd,
      SessionDuration = sessionDuration,
      SessionFinished = sessionFinished,
      GeneratedAt = DateTime.Now,
      Entries = QualifyingRanking.Rank(riders.Values)
    };

    _nextEntryIndex = 0;
    _pageNumber = 0;
  }

  // ---- Printing ------------------------------------------------------------

  private void PrintOne(Dictionary<string, RiderInfo> riders, string title,
    DateTime? sessionStart, DateTime? sessionEnd, TimeSpan sessionDuration, bool sessionFinished)
  {
    Prepare(riders, title, sessionStart, sessionEnd, sessionDuration, sessionFinished);

    using var dialog = new PrintDialog { Document = _printDocument };
    if (dialog.ShowDialog() == DialogResult.OK) _printDocument.Print();
  }

  /// <summary>
  /// One column of the printed sheet. Widths are fractions of the printable
  /// width rather than character counts: the previous version drew the whole
  /// row as one fixed-width string, which at ten columns ran off the right of
  /// the page on any normal paper size.
  /// </summary>
  private sealed record Column(string Header, float Weight, bool RightAligned);

  private static readonly Column[] Sheet =
  {
    new("Pick", 0.06f, true),
    new("#", 0.07f, true),
    new("Rider", 0.28f, false),
    new("Class", 0.11f, false),
    new("Best lap", 0.13f, true),
    new("Gap", 0.11f, true),
    new("Int", 0.11f, true),
    new("On lap", 0.06f, true),
    new("Laps", 0.07f, true)
  };

  /// <summary>Gap between columns, in hundredths of an inch.</summary>
  private const float ColumnGap = 6f;

  private void PrintDocument_PrintPage(object? sender, PrintPageEventArgs e)
  {
    if (_data == null || e.Graphics == null) return;

    var g = e.Graphics;
    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

    var bounds = e.MarginBounds;
    var y = (float)bounds.Top;

    using var titleFont = new Font("Segoe UI", 15, FontStyle.Bold);
    using var infoFont = new Font("Segoe UI", 8.5f);
    using var headerFont = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold);
    using var rowFont = new Font("Segoe UI", 9);
    using var rowBoldFont = new Font("Segoe UI", 9, FontStyle.Bold);
    using var ink = new SolidBrush(Color.Black);
    using var quiet = new SolidBrush(Color.FromArgb(105, 105, 105));
    using var rule = new Pen(Color.Black, 0.8f);
    using var hairline = new Pen(Color.FromArgb(200, 200, 200), 0.5f);

    _pageNumber++;

    if (_pageNumber == 1)
    {
      g.DrawString(_data.Title, titleFont, ink, bounds.Left, y);
      y += titleFont.GetHeight(g) + 8;

      foreach (var (caption, value) in DescribeSession())
      {
        g.DrawString(caption, infoFont, quiet, bounds.Left, y);
        g.DrawString(value, infoFont, ink, bounds.Left + 110, y);
        y += infoFont.GetHeight(g) + 2;
      }

      y += 10;
    }
    else
    {
      g.DrawString($"{_data.Title}  (continued)", headerFont, ink, bounds.Left, y);
      y += headerFont.GetHeight(g) + 10;
    }

    var rowHeight = rowFont.GetHeight(g) + 7;

    // Header, then a rule under it. Repeated on every page so a sheet that runs
    // to two pages is still readable on its own.
    DrawHeader();

    var previousStatus = QualifyingStatus.Timed;

    while (_nextEntryIndex < _data.Entries.Count)
    {
      if (y + rowHeight > bounds.Bottom - rowHeight)
      {
        DrawFooter(continued: true);
        e.HasMorePages = true;
        return;
      }

      var entry = _data.Entries[_nextEntryIndex];

      // A rule where the riders with a time end and the rest begin, so nobody
      // reads a NO TIME row as part of the order on merit.
      if (entry.Status != QualifyingStatus.Timed && previousStatus == QualifyingStatus.Timed
          && _nextEntryIndex > 0)
      {
        g.DrawLine(hairline, bounds.Left, y - 2, bounds.Right, y - 2);
        y += 3;
      }
      previousStatus = entry.Status;

      DrawEntry(entry);
      y += rowHeight;
      _nextEntryIndex++;
    }

    DrawFooter(continued: false);
    e.HasMorePages = false;

    // The document is re-printable - a preview dialog raises PrintPage again
    // when the operator scrolls back, and would otherwise render a blank page.
    _nextEntryIndex = 0;
    _pageNumber = 0;
    return;

    void DrawHeader()
    {
      var x = (float)bounds.Left;
      foreach (var column in Sheet)
      {
        var width = bounds.Width * column.Weight;
        Draw(column.Header, headerFont, ink, x, width, column.RightAligned);
        x += width;
      }

      y += headerFont.GetHeight(g) + 3;
      g.DrawLine(rule, bounds.Left, y, bounds.Right, y);
      y += 5;
    }

    void DrawEntry(QualifyingEntry entry)
    {
      var timed = entry.Status == QualifyingStatus.Timed;

      // The top three are what everyone looks for first.
      var font = timed && entry.GatePick <= 3 ? rowBoldFont : rowFont;
      var brush = timed ? ink : quiet;

      var note = timed ? StatusText(entry) : "";

      var cells = new[]
      {
        entry.GatePick.ToString(),
        entry.Rider.RiderNumber,
        note.Length > 0 ? $"{RiderName(entry.Rider)} ({note})" : RiderName(entry.Rider),
        entry.Rider.Category,
        timed ? FormatLap(entry.BestLapTime) : "NO TIME",
        FormatDelta(entry, entry.GapToPole),
        FormatDelta(entry, entry.IntervalToAhead),
        entry.BestLapNumber > 0 ? entry.BestLapNumber.ToString() : "",
        entry.TimedLaps > 0 ? entry.TimedLaps.ToString() : ""
      };

      var x = (float)bounds.Left;
      for (var i = 0; i < Sheet.Length; i++)
      {
        var width = bounds.Width * Sheet[i].Weight;

        // Past the best-lap column a rider without a time has nothing to say in
        // these columns, so the reason runs across them instead of four blanks.
        if (!timed && i == 5)
        {
          var remaining = bounds.Right - x;
          Draw(StatusText(entry), rowFont, quiet, x, remaining, rightAligned: false);
          break;
        }

        Draw(cells[i], font, brush, x, width, Sheet[i].RightAligned);
        x += width;
      }
    }

    void Draw(string text, Font font, Brush brush, float left, float width, bool rightAligned)
    {
      if (string.IsNullOrEmpty(text)) return;

      using var format = new StringFormat(StringFormatFlags.NoWrap)
      {
        Alignment = rightAligned ? StringAlignment.Far : StringAlignment.Near,
        Trimming = StringTrimming.EllipsisCharacter
      };

      g.DrawString(text, font, brush,
        new RectangleF(left, y, Math.Max(0, width - ColumnGap), font.GetHeight(g) + 2), format);
    }

    void DrawFooter(bool continued)
    {
      var footerY = bounds.Bottom - infoFont.GetHeight(g);
      g.DrawString($"Generated {_data.GeneratedAt:yyyy-MM-dd HH:mm:ss}", infoFont, quiet,
        bounds.Left, footerY);

      using var right = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Far };
      g.DrawString(continued ? $"Page {_pageNumber} - continued" : $"Page {_pageNumber}",
        infoFont, quiet, new RectangleF(bounds.Left, footerY, bounds.Width, infoFont.GetHeight(g) + 2),
        right);
    }
  }

  // ---- Shared text layout --------------------------------------------------

  private static string HeaderLine() =>
    $"{"Pick",-5} {"#",-6} {"Rider",-24} {"Class",-10} {"Best Lap",-11} " +
    $"{"Gap",-9} {"Int",-9} {"On lap",-7} {"Laps",-5} Status";

  private static string RowLine(QualifyingEntry entry)
  {
    var best = entry.Status == QualifyingStatus.Timed ? FormatLap(entry.BestLapTime) : "NO TIME";

    return $"{entry.GatePick,-5} {Trim(entry.Rider.RiderNumber, 6),-6} " +
           $"{Trim(RiderName(entry.Rider), 24),-24} {Trim(entry.Rider.Category, 10),-10} " +
           $"{best,-11} {FormatDelta(entry, entry.GapToPole),-9} " +
           $"{FormatDelta(entry, entry.IntervalToAhead),-9} " +
           $"{(entry.BestLapNumber > 0 ? entry.BestLapNumber.ToString() : "-"),-7} " +
           $"{entry.TimedLaps,-5} {StatusText(entry)}";
  }

  /// <summary>
  /// Name only. RiderInfo.Label prefixes the start number, which already has its
  /// own column here, and falls back to the raw transponder code - which is
  /// exactly what this sheet exists not to put in front of a rider.
  /// </summary>
  private static string RiderName(RiderInfo rider)
  {
    var name = $"{rider.FirstName} {rider.LastName}".Trim();
    if (name.Length > 0) return name;
    return rider.RiderNumber.Length > 0 ? $"#{rider.RiderNumber}" : "Unknown rider";
  }

  /// <summary>
  /// Why a rider has no time, or why the one they have is in doubt.
  ///
  /// Deliberately says nothing about IsDNF. In a timed session the flag's grace
  /// marks everyone who was not still circulating at the end, which is almost
  /// the whole field - printing that against each of them reads as though
  /// something went wrong for all of them, when they simply pulled in.
  /// </summary>
  private static string StatusText(QualifyingEntry entry) => entry.Status switch
  {
    QualifyingStatus.DidNotGoOut => entry.Rider.IsDNS ? "did not start" : "did not go out",
    QualifyingStatus.NoTime => "out-lap only",
    _ => entry.Rider.HasAnomalies ? "check laps" : ""
  };

  private static string FormatLap(TimeSpan? lap) =>
    lap.HasValue ? lap.Value.ToString(@"m\:ss\.fff") : "-";

  private static string FormatDelta(QualifyingEntry entry, TimeSpan? delta)
  {
    if (entry.Status != QualifyingStatus.Timed) return "";
    return delta.HasValue ? $"+{delta.Value.TotalSeconds:F3}" : "-";
  }

  private static string Trim(string value, int width) =>
    value.Length <= width ? value : value.Substring(0, width);

  /// <summary>
  /// The block above the table, as caption/value pairs so the print path can
  /// line the values up in a column and the Excel path can put them in adjacent
  /// cells, rather than both re-splitting one pre-padded string.
  /// </summary>
  private List<(string Caption, string Value)> DescribeSession()
  {
    var lines = new List<(string, string)>();
    if (_data == null) return lines;

    if (_data.SessionStart.HasValue)
      lines.Add(("Session start", $"{_data.SessionStart.Value:yyyy-MM-dd HH:mm:ss}"));

    if (_data.SessionEnd.HasValue)
      lines.Add(("Session end", $"{_data.SessionEnd.Value:yyyy-MM-dd HH:mm:ss}"));

    lines.Add(("Length", $"{_data.SessionDuration:hh\\:mm\\:ss}"));
    lines.Add(("Riders", $"{_data.Entries.Count} " +
                         $"({_data.Entries.Count(x => x.Status == QualifyingStatus.Timed)} with a time)"));

    // Worth saying on the paper: a sheet printed mid-session is a snapshot, and
    // handing one out as though it were the gate pick order would be wrong.
    lines.Add(("Status", _data.SessionFinished
      ? "Session over - this is the gate pick order."
      : "SESSION STILL RUNNING - provisional."));

    return lines;
  }

  // ---- Export --------------------------------------------------------------

  private void ExportSingle(Dictionary<string, RiderInfo> riders, string title,
    DateTime? sessionStart, DateTime? sessionEnd, TimeSpan sessionDuration, bool sessionFinished)
  {
    Prepare(riders, title, sessionStart, sessionEnd, sessionDuration, sessionFinished);

    using var save = new SaveFileDialog
    {
      Filter = "Excel Files (*.xlsx)|*.xlsx|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
      DefaultExt = "xlsx",
      FileName = $"Gate_Pick_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
    };

    if (save.ShowDialog() != DialogResult.OK) return;

    try
    {
      if (Path.GetExtension(save.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        ExportToExcel(save.FileName);
      else
        File.WriteAllText(save.FileName, GenerateTextReport(), Encoding.UTF8);

      MessageBox.Show($"Gate pick order saved to:\n{save.FileName}",
        "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(null, "The gate pick order could not be saved.",
        "Check that the file is not already open in Excel, then try again.", ex);
    }
  }

  private void ExportPerClass(Dictionary<string, RiderInfo> riders, string title,
    DateTime? sessionStart, DateTime? sessionEnd, TimeSpan sessionDuration, bool sessionFinished,
    List<string> classes)
  {
    using var folder = new FolderBrowserDialog
    {
      Description = "Choose a folder for the gate pick sheets",
      UseDescriptionForTitle = true
    };

    if (folder.ShowDialog() != DialogResult.OK) return;

    var baseName = $"Gate_Pick_{DateTime.Now:yyyyMMdd_HHmmss}";
    var written = new List<string>();

    try
    {
      var overall = Path.Combine(folder.SelectedPath, $"{baseName}_Overall.xlsx");
      Prepare(riders, $"{title} - Overall", sessionStart, sessionEnd, sessionDuration, sessionFinished);
      ExportToExcel(overall);
      written.Add(Path.GetFileName(overall));

      foreach (var className in classes)
      {
        var classRiders = ReportHelpers.FilterRidersByClass(riders, className);
        if (classRiders.Count == 0) continue;

        var path = Path.Combine(folder.SelectedPath,
          $"{baseName}_Class_{ReportHelpers.SanitizeFileName(className)}.xlsx");

        Prepare(classRiders, $"{title} - Class: {className}",
          sessionStart, sessionEnd, sessionDuration, sessionFinished);
        ExportToExcel(path);
        written.Add(Path.GetFileName(path));
      }

      MessageBox.Show(
        $"Gate pick sheets saved to:\n{folder.SelectedPath}\n\n{string.Join("\n", written)}",
        "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(null, "The gate pick sheets could not be saved.",
        "Check that none of the files are already open in Excel, then try again.", ex);
    }
  }

  private string GenerateTextReport()
  {
    var sb = new StringBuilder();
    if (_data == null) return sb.ToString();

    sb.AppendLine(_data.Title);
    sb.AppendLine(new string('=', Math.Max(_data.Title.Length, 40)));
    sb.AppendLine();

    foreach (var (caption, value) in DescribeSession())
      sb.AppendLine($"{caption + ":",-16}{value}");
    sb.AppendLine();

    sb.AppendLine(HeaderLine());
    sb.AppendLine(new string('-', HeaderLine().Length));

    foreach (var entry in _data.Entries) sb.AppendLine(RowLine(entry));

    sb.AppendLine();
    sb.AppendLine($"Generated {_data.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
    return sb.ToString();
  }

  private void ExportToExcel(string path)
  {
    if (_data == null) return;

    using var workbook = new XLWorkbook();
    var sheet = workbook.Worksheets.Add("Gate Pick Order");

    var row = 1;
    sheet.Cell(row, 1).Value = _data.Title;
    sheet.Cell(row, 1).Style.Font.FontSize = 16;
    sheet.Cell(row, 1).Style.Font.Bold = true;
    sheet.Range(row, 1, row, QualifyingRowData.ColumnCount).Merge();
    row += 2;

    foreach (var (caption, value) in DescribeSession())
    {
      sheet.Cell(row, 1).Value = caption + ":";
      sheet.Cell(row, 2).Value = value;
      row++;
    }

    row++;

    var headers = new[]
    {
      "Gate Pick", "Number", "Rider", "Class", "Best Lap",
      "Gap to Pole", "Interval", "On Lap", "Timed Laps", "Status"
    };

    for (var i = 0; i < headers.Length; i++)
    {
      var cell = sheet.Cell(row, i + 1);
      cell.Value = headers[i];
      cell.Style.Font.Bold = true;
      cell.Style.Fill.BackgroundColor = XLColor.LightGray;
      cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
    }
    row++;

    foreach (var entry in _data.Entries)
    {
      var timed = entry.Status == QualifyingStatus.Timed;

      sheet.Cell(row, 1).Value = entry.GatePick;
      sheet.Cell(row, 2).Value = entry.Rider.RiderNumber;
      sheet.Cell(row, 3).Value = RiderName(entry.Rider);
      sheet.Cell(row, 4).Value = entry.Rider.Category;
      sheet.Cell(row, 5).Value = timed ? FormatLap(entry.BestLapTime) : "NO TIME";
      sheet.Cell(row, 6).Value = FormatDelta(entry, entry.GapToPole);
      sheet.Cell(row, 7).Value = FormatDelta(entry, entry.IntervalToAhead);
      sheet.Cell(row, 8).Value = entry.BestLapNumber > 0 ? entry.BestLapNumber.ToString() : "-";
      sheet.Cell(row, 9).Value = entry.TimedLaps;
      sheet.Cell(row, 10).Value = StatusText(entry);

      // Same three podium colours as the screen and the results sheet.
      if (timed)
      {
        var shade = entry.GatePick switch
        {
          1 => XLColor.Gold,
          2 => XLColor.Silver,
          3 => XLColor.FromArgb(205, 127, 50),
          _ => (XLColor?)null
        };
        if (shade != null) sheet.Row(row).Style.Fill.BackgroundColor = shade;
      }
      else
      {
        sheet.Row(row).Style.Fill.BackgroundColor = XLColor.WhiteSmoke;
      }

      sheet.Range(row, 1, row, headers.Length).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
      row++;
    }

    sheet.Columns().AdjustToContents();
    workbook.SaveAs(path);
  }

  public void Dispose()
  {
    _printDocument.PrintPage -= PrintDocument_PrintPage;
    _printDocument.Dispose();
  }

  /// <summary>Everything one sheet needs, shaped once and rendered three ways.</summary>
  private sealed class QualifyingReportData
  {
    public string Title { get; init; } = "";
    public DateTime? SessionStart { get; init; }
    public DateTime? SessionEnd { get; init; }
    public TimeSpan SessionDuration { get; init; }
    public bool SessionFinished { get; init; }
    public DateTime GeneratedAt { get; init; }
    public List<QualifyingEntry> Entries { get; init; } = new();
  }
}
