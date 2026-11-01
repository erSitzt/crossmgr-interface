using System.Drawing.Printing;
using ClosedXML.Excel;

namespace CrossMgrInterface;

/// <summary>
/// Prints and exports the list of every rider of every class of a race day:
/// one table in the order the club chose, the rules under it so a question
/// about why someone is where they are can be answered from the paper.
///
/// Everything comes from <see cref="DayRankingResult"/>, which is also what
/// the window shows, so the screen and the paper cannot disagree.
/// </summary>
public sealed class DayRankingReportGenerator : IDisposable
{
  private readonly PrintDocument _printDocument;
  private DayRankingResult? _data;
  private List<DayRankingEntry> _rows = new();
  private string _title = "";
  private int _entryIndex;
  private int _pageNumber;

  public DayRankingReportGenerator()
  {
    _printDocument = new PrintDocument();
    _printDocument.PrintPage += PrintDocument_PrintPage;
  }

  public void Run(IWin32Window owner, DayRankingResult result, string title, ReportAction action)
  {
    switch (action)
    {
      case ReportAction.Preview:
        Prepare(result, title);
        using (var preview = new PrintPreviewDialog { Document = _printDocument, WindowState = FormWindowState.Maximized })
          preview.ShowDialog();
        break;
      case ReportAction.Print:
        Prepare(result, title);
        using (var dialog = new PrintDialog { Document = _printDocument })
          if (dialog.ShowDialog() == DialogResult.OK) _printDocument.Print();
        break;
      case ReportAction.Export:
        Export(owner, result, title);
        break;
    }
  }

  private void Export(IWin32Window owner, DayRankingResult result, string title)
  {
    using var save = new SaveFileDialog
    {
      Filter = "Excel Files (*.xlsx)|*.xlsx",
      DefaultExt = "xlsx",
      FileName = $"{ReportHelpers.SanitizeFileName(title)}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
    };
    if (save.ShowDialog(owner) != DialogResult.OK) return;

    try
    {
      using var workbook = BuildWorkbook(result, title);
      workbook.SaveAs(save.FileName);
      MessageBox.Show(owner, $"Exported to:\n{save.FileName}", "Export complete",
        MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(owner, "Could not export the list.",
        "Check the file is not open in Excel and the folder can be written to, then try again.", ex);
    }
  }

  /// <summary>Saved without a question: a workbook, and when asked a PDF of the printed list.</summary>
  public List<string> SaveSheets(DayRankingResult result, string title, string folder, bool pdf)
  {
    Directory.CreateDirectory(folder);
    var files = new List<string>();
    var name = ReportHelpers.SanitizeFileName(title);

    var xlsx = SheetSaver.WritablePath(folder, name + ".xlsx");
    using (var workbook = BuildWorkbook(result, title)) workbook.SaveAs(xlsx);
    files.Add(xlsx);

    if (pdf)
    {
      Prepare(result, title);
      var path = SheetSaver.WritablePath(folder, name + ".pdf");
      if (SheetSaver.PrintToPdf(_printDocument, path)) files.Add(path);
    }

    return files;
  }

  // ---- Shared layout -------------------------------------------------------

  public static readonly IReadOnlyList<string> Headers =
    new[] { "Pos", "#", "Rider", "Class", "Best lap", "Best lap in", "Avg lap", "Laps", "Gap" };

  /// <summary>One rider's line, placed and measured on <paramref name="measure"/>.</summary>
  public static List<string> Cells(DayRankingResult result, DayRankingEntry entry, DayRankingMeasure measure,
    TimeSpan? leader)
  {
    var time = entry.TimeBy(measure);
    var placed = entry.RankBy(measure).HasValue;
    return new List<string>
    {
      entry.RankBy(measure)?.ToString() ?? "-",
      entry.Number,
      RiderText(entry),
      entry.Classes.Count > 1 ? string.Join(" / ", entry.Classes) : entry.Category,
      entry.BestLap.HasValue ? TimeFormat.Precise(entry.BestLap.Value) : entry.Dns ? "DNS" : "no time",
      BestLapIn(result, entry),
      entry.AverageLap.HasValue ? TimeFormat.Precise(entry.AverageLap.Value) : "",
      entry.TimedLaps.ToString(),
      placed && leader.HasValue && time.HasValue && time.Value > leader.Value ? Gap(time.Value - leader.Value) : ""
    };
  }

  /// <summary>"+1.799" - seconds, as a lap time's difference is read out; minutes only past one.</summary>
  public static string Gap(TimeSpan gap) => gap < TimeSpan.FromMinutes(1)
    ? "+" + gap.TotalSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
    : "+" + TimeFormat.Precise(gap);

  /// <summary>The time every gap on the list is measured from.</summary>
  public static TimeSpan? Leader(DayRankingResult result, DayRankingMeasure measure) =>
    result.Entries.Where(e => e.RankBy(measure) == 1).Select(e => e.TimeBy(measure)).FirstOrDefault();

  public static string BestLapIn(DayRankingResult result, DayRankingEntry entry) =>
    entry.BestLapSession is { } s && s < result.SessionTitles.Count
      ? $"{result.SessionTitles[s]}, lap {entry.BestLapNumber}"
      : "";

  public static string RiderText(DayRankingEntry entry)
  {
    var name = entry.Name.Length > 0 ? entry.Name : entry.Number.Length > 0 ? $"#{entry.Number}" : "Unknown rider";
    return entry.Team.Length > 0 ? $"{name} ({entry.Team})" : name;
  }

  public static string MeasureText(DayRankingMeasure measure) =>
    measure == DayRankingMeasure.AverageLap ? "Ranked on average lap" : "Ranked on best lap";

  /// <summary>The block above the table: what is ranked, which races count, and whether more are to come.</summary>
  public static List<(string Caption, string Value)> DescribeRaces(DayRankingResult result)
  {
    var lines = new List<(string, string)> { ("Order", MeasureText(result.Measure)) };
    lines.Add(("Races", string.Join(", ", result.SessionTitles)));
    if (result.LeftOut.Count > 0) lines.Add(("Not counted", string.Join(", ", result.LeftOut)));
    if (!result.Final) lines.Add(("Provisional", "races of this day are still to be run"));
    lines.AddRange(result.Notes.Select(n => ("Note", n)));
    return lines;
  }

  // ---- Printing ------------------------------------------------------------

  private void Prepare(DayRankingResult result, string title)
  {
    _data = result;
    _rows = result.Ordered(result.Measure);
    _title = title;
    _entryIndex = 0;
    _pageNumber = 0;
  }

  private const float ColumnGap = 6f;

  // Pos, #, Rider, Class, Best, Best in, Avg, Laps, Gap.
  private static readonly float[] Weights = { 0.055f, 0.055f, 0.23f, 0.11f, 0.095f, 0.19f, 0.095f, 0.06f, 0.11f };

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
    using var rowFont = new Font("Segoe UI", 8.5f);
    using var rowBoldFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
    using var ink = new SolidBrush(Color.Black);
    using var quiet = new SolidBrush(Color.FromArgb(105, 105, 105));
    using var warn = new SolidBrush(Color.FromArgb(160, 60, 0));
    using var rule = new Pen(Color.Black, 0.8f);

    _pageNumber++;
    var measure = _data.Measure;
    var leader = Leader(_data, measure);

    if (_entryIndex == 0)
    {
      g.DrawString(_title, titleFont, ink, bounds.Left, y);
      y += titleFont.GetHeight(g) + 8;
      foreach (var (caption, value) in DescribeRaces(_data))
      {
        var height = g.MeasureString(value, infoFont, bounds.Width - 90).Height;
        g.DrawString(caption, infoFont, quiet, bounds.Left, y);
        g.DrawString(value, infoFont, ink, new RectangleF(bounds.Left + 90, y, bounds.Width - 90, height + 2));
        y += height + 2;
      }
      y += 10;
    }
    else
    {
      g.DrawString($"{_title}  (continued)", headerFont, ink, bounds.Left, y);
      y += headerFont.GetHeight(g) + 10;
    }

    var rowHeight = rowFont.GetHeight(g) + 6;
    var rulesWidth = bounds.Width - 90;
    var rulesHeight = _data.Describe()
      .Sum(line => g.MeasureString(line.Value, infoFont, (int)rulesWidth).Height + 2) + 16;

    DrawRow(Headers, headerFont, ink);
    y += headerFont.GetHeight(g) + 3;
    g.DrawLine(rule, bounds.Left, y, bounds.Right, y);
    y += 5;

    while (_entryIndex < _rows.Count)
    {
      if (y + rowHeight > bounds.Bottom - rowHeight)
      {
        DrawFooter(continued: true);
        e.HasMorePages = true;
        return;
      }

      var entry = _rows[_entryIndex];
      var rank = entry.RankBy(measure);
      DrawRow(Cells(_data, entry, measure, leader), rank is <= 3 ? rowBoldFont : rowFont,
        rank.HasValue ? ink : quiet);
      y += rowHeight;

      if (entry.Warning != null)
      {
        g.DrawString("! " + entry.Warning, infoFont, warn, bounds.Left + bounds.Width * 0.11f, y - 4);
        y += infoFont.GetHeight(g) + 2;
      }
      _entryIndex++;
    }

    if (y + rulesHeight > bounds.Bottom - infoFont.GetHeight(g) && _entryIndex != int.MaxValue)
    {
      DrawFooter(continued: true);
      e.HasMorePages = true;
      _entryIndex = int.MaxValue;
      return;
    }

    y += 12;
    foreach (var (caption, value) in _data.Describe())
    {
      var height = g.MeasureString(value, infoFont, (int)rulesWidth).Height;
      g.DrawString(caption, infoFont, quiet, bounds.Left, y);
      g.DrawString(value, infoFont, ink, new RectangleF(bounds.Left + 90, y, rulesWidth, height + 2));
      y += height + 2;
    }

    DrawFooter(continued: false);

    // A preview raises PrintPage again when scrolled back; start over then.
    e.HasMorePages = false;
    _entryIndex = 0;
    _pageNumber = 0;
    return;

    void DrawRow(IReadOnlyList<string> cells, Font font, Brush brush)
    {
      var x = (float)bounds.Left;
      for (var i = 0; i < cells.Count; i++)
      {
        var width = bounds.Width * Weights[i];
        var rightAligned = i is 0 or 1 or 4 or 6 or 7 or 8;
        if (!string.IsNullOrEmpty(cells[i]))
        {
          using var format = new StringFormat(StringFormatFlags.NoWrap)
          {
            Alignment = rightAligned ? StringAlignment.Far : StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter
          };
          g.DrawString(cells[i], font, brush,
            new RectangleF(x, y, Math.Max(0, width - ColumnGap), font.GetHeight(g) + 2), format);
        }
        x += width;
      }
    }

    void DrawFooter(bool continued)
    {
      var footerY = bounds.Bottom - infoFont.GetHeight(g);
      g.DrawString($"Generated {_data.GeneratedAt:yyyy-MM-dd HH:mm:ss}", infoFont, quiet, bounds.Left, footerY);

      using var right = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Far };
      g.DrawString(continued ? $"Page {_pageNumber} - continued" : $"Page {_pageNumber}",
        infoFont, quiet, new RectangleF(bounds.Left, footerY, bounds.Width, infoFont.GetHeight(g) + 2), right);
    }
  }

  // ---- Excel ---------------------------------------------------------------

  /// <summary>
  /// A worksheet in each order, the club's choice first: riders asking "and on
  /// average?" get their answer from the same file.
  /// </summary>
  public static XLWorkbook BuildWorkbook(DayRankingResult result, string title)
  {
    var workbook = new XLWorkbook();
    var other = result.Measure == DayRankingMeasure.AverageLap ? DayRankingMeasure.BestLap : DayRankingMeasure.AverageLap;

    foreach (var measure in new[] { result.Measure, other })
    {
      var sheet = workbook.Worksheets.Add(measure == DayRankingMeasure.AverageLap ? "By average lap" : "By best lap");
      var row = 1;

      sheet.Cell(row, 1).Value = title;
      sheet.Cell(row, 1).Style.Font.Bold = true;
      sheet.Cell(row, 1).Style.Font.FontSize = 14;
      row += 2;

      foreach (var (caption, value) in DescribeRaces(result).Skip(1).Prepend(("Order", MeasureText(measure))))
      {
        sheet.Cell(row, 1).Value = caption;
        sheet.Cell(row, 2).Value = value;
        row++;
      }
      row++;

      for (var c = 0; c < Headers.Count; c++)
      {
        sheet.Cell(row, c + 1).Value = Headers[c];
        sheet.Cell(row, c + 1).Style.Font.Bold = true;
      }
      sheet.Cell(row, Headers.Count + 1).Value = "Note";
      sheet.Cell(row, Headers.Count + 1).Style.Font.Bold = true;
      row++;

      var leader = Leader(result, measure);
      foreach (var entry in result.Ordered(measure))
      {
        var cells = Cells(result, entry, measure, leader);
        for (var c = 0; c < cells.Count; c++)
        {
          // Places and laps as numbers, so the list can be sorted in Excel.
          if ((c == 0 || c == 7) && int.TryParse(cells[c], out var n)) sheet.Cell(row, c + 1).Value = n;
          else sheet.Cell(row, c + 1).Value = cells[c];
        }
        if (entry.Warning != null) sheet.Cell(row, cells.Count + 1).Value = entry.Warning;
        row++;
      }

      row++;
      foreach (var (caption, value) in result.Describe())
      {
        sheet.Cell(row, 1).Value = caption;
        sheet.Cell(row, 2).Value = value;
        row++;
      }

      sheet.Columns().AdjustToContents();
    }

    return workbook;
  }

  public void Dispose() => _printDocument.Dispose();
}
