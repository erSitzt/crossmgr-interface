using System.Drawing.Printing;
using System.Text;
using ClosedXML.Excel;

namespace CrossMgrInterface;

/// <summary>
/// Prints the transponder check - the sheet a marshal walks the paddock with.
///
/// Deliberately not split by class the way the results and gate pick sheets are:
/// a faulty tag is an equipment problem, and the person going to find those
/// riders wants one list, not five.
/// </summary>
public sealed class TransponderCheckReportGenerator : IDisposable
{
  private readonly PrintDocument _printDocument;
  private List<TransponderFinding> _findings = new();
  private string _title = "";
  private DateTime? _sessionStart;
  private TimeSpan _sessionDuration;
  private DateTime _generatedAt;
  private int _nextIndex;
  private int _pageNumber;

  public TransponderCheckReportGenerator()
  {
    _printDocument = new PrintDocument();
    _printDocument.PrintPage += PrintDocument_PrintPage;
  }

  private void Prepare(List<TransponderFinding> findings, string title,
    DateTime? sessionStart, TimeSpan sessionDuration)
  {
    _findings = findings;
    _title = title;
    _sessionStart = sessionStart;
    _sessionDuration = sessionDuration;
    _generatedAt = DateTime.Now;
    _nextIndex = 0;
    _pageNumber = 0;
  }

  public void ShowPrintPreview(List<TransponderFinding> findings, string title,
    DateTime? sessionStart, TimeSpan sessionDuration)
  {
    Prepare(findings, title, sessionStart, sessionDuration);
    using var preview = new PrintPreviewDialog
    {
      Document = _printDocument,
      WindowState = FormWindowState.Maximized
    };
    preview.ShowDialog();
  }

  public void PrintReport(List<TransponderFinding> findings, string title,
    DateTime? sessionStart, TimeSpan sessionDuration)
  {
    Prepare(findings, title, sessionStart, sessionDuration);
    using var dialog = new PrintDialog { Document = _printDocument };
    if (dialog.ShowDialog() == DialogResult.OK) _printDocument.Print();
  }

  public void ExportToFile(List<TransponderFinding> findings, string title,
    DateTime? sessionStart, TimeSpan sessionDuration)
  {
    Prepare(findings, title, sessionStart, sessionDuration);

    using var save = new SaveFileDialog
    {
      Filter = "Excel Files (*.xlsx)|*.xlsx|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
      DefaultExt = "xlsx",
      FileName = $"Transponder_Check_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
    };

    if (save.ShowDialog() != DialogResult.OK) return;

    try
    {
      if (Path.GetExtension(save.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        ExportToExcel(save.FileName);
      else
        File.WriteAllText(save.FileName, GenerateTextReport(), Encoding.UTF8);

      MessageBox.Show($"Transponder check saved to:\n{save.FileName}",
        "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(null, "The transponder check could not be saved.",
        "Check that the file is not already open in Excel, then try again.", ex);
    }
  }

  // ---- Layout --------------------------------------------------------------

  private sealed record Column(string Header, float Weight, bool RightAligned);

  private static readonly Column[] Sheet =
  {
    new("#", 0.06f, true),
    new("Rider", 0.22f, false),
    new("Class", 0.10f, false),
    new("Laps", 0.06f, true),
    new("Missed", 0.07f, true),
    new("Double", 0.07f, true),
    new("What the loop saw", 0.42f, false)
  };

  private const float ColumnGap = 6f;

  private void PrintDocument_PrintPage(object? sender, PrintPageEventArgs e)
  {
    if (e.Graphics == null) return;

    var g = e.Graphics;
    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

    var bounds = e.MarginBounds;
    var y = (float)bounds.Top;

    using var titleFont = new Font("Segoe UI", 15, FontStyle.Bold);
    using var headlineFont = new Font("Segoe UI", 11, FontStyle.Bold);
    using var infoFont = new Font("Segoe UI", 8.5f);
    using var headerFont = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold);
    using var rowFont = new Font("Segoe UI", 9);
    using var rowBoldFont = new Font("Segoe UI", 9, FontStyle.Bold);
    using var ink = new SolidBrush(Color.Black);
    using var quiet = new SolidBrush(Color.FromArgb(105, 105, 105));
    using var alarm = new SolidBrush(Color.FromArgb(150, 30, 20));
    using var rule = new Pen(Color.Black, 0.8f);
    using var hairline = new Pen(Color.FromArgb(200, 200, 200), 0.5f);

    _pageNumber++;

    var problems = _findings.Count(f => f.Verdict != TransponderVerdict.Clean);

    if (_pageNumber == 1)
    {
      g.DrawString(_title, titleFont, ink, bounds.Left, y);
      y += titleFont.GetHeight(g) + 8;

      var headline = problems == 0
        ? $"All {_findings.Count} transponders reading cleanly."
        : $"{problems} of {_findings.Count} riders need attention.";
      g.DrawString(headline, headlineFont, problems == 0 ? ink : alarm, bounds.Left, y);
      y += headlineFont.GetHeight(g) + 8;

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
      g.DrawString($"{_title}  (continued)", headerFont, ink, bounds.Left, y);
      y += headerFont.GetHeight(g) + 10;
    }

    DrawHeader();

    var rowHeight = rowFont.GetHeight(g) + 7;
    var previousClean = false;

    while (_nextIndex < _findings.Count)
    {
      if (y + rowHeight > bounds.Bottom - rowHeight)
      {
        DrawFooter(continued: true);
        e.HasMorePages = true;
        return;
      }

      var finding = _findings[_nextIndex];
      var clean = finding.Verdict == TransponderVerdict.Clean;

      // A rule where the riders to go and find end and the rest begin.
      if (clean && !previousClean && _nextIndex > 0)
      {
        g.DrawLine(hairline, bounds.Left, y - 2, bounds.Right, y - 2);
        y += 3;
      }
      previousClean = clean;

      DrawRow(finding, clean ? rowFont : rowBoldFont, clean ? quiet : ink);
      y += rowHeight;
      _nextIndex++;
    }

    DrawLegend();
    DrawFooter(continued: false);
    e.HasMorePages = false;
    _nextIndex = 0;
    _pageNumber = 0;
    return;

    void DrawLegend()
    {
      // Only the verdicts actually present, so a clean session does not print a
      // page of advice about faults nobody has.
      var present = _findings
        .Select(f => f.Verdict)
        .Where(v => v != TransponderVerdict.Clean)
        .Distinct()
        .OrderByDescending(v => (int)v)
        .ToList();

      if (present.Count == 0) return;
      if (y + (present.Count + 2) * (infoFont.GetHeight(g) + 3) > bounds.Bottom) return;

      y += 10;
      g.DrawLine(hairline, bounds.Left, y, bounds.Right, y);
      y += 6;

      g.DrawString("What to check", headerFont, ink, bounds.Left, y);
      y += headerFont.GetHeight(g) + 3;

      foreach (var verdict in present)
      {
        g.DrawString(TransponderCheck.Advice(verdict), infoFont, quiet, bounds.Left, y);
        y += infoFont.GetHeight(g) + 3;
      }
    }

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

    void DrawRow(TransponderFinding finding, Font font, Brush brush)
    {
      var cells = new[]
      {
        finding.Rider.RiderNumber,
        RiderName(finding.Rider),
        finding.Rider.Category,
        finding.Laps.ToString(),
        finding.SuspectedMisses > 0 ? finding.SuspectedMisses.ToString() : "",
        finding.DuplicateReads > 0 ? finding.DuplicateReads.ToString() : "",
        finding.Detail
      };

      var x = (float)bounds.Left;
      for (var i = 0; i < Sheet.Length; i++)
      {
        var width = bounds.Width * Sheet[i].Weight;
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
      g.DrawString($"Generated {_generatedAt:yyyy-MM-dd HH:mm:ss}", infoFont, quiet, bounds.Left, footerY);
      using var right = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Far };
      g.DrawString(continued ? $"Page {_pageNumber} - continued" : $"Page {_pageNumber}",
        infoFont, quiet, new RectangleF(bounds.Left, footerY, bounds.Width, infoFont.GetHeight(g) + 2), right);
    }
  }

  private static string RiderName(RiderInfo rider)
  {
    var name = $"{rider.FirstName} {rider.LastName}".Trim();
    if (name.Length > 0) return name;
    return rider.RiderNumber.Length > 0 ? $"#{rider.RiderNumber}" : rider.TagID;
  }

  private List<(string Caption, string Value)> DescribeSession()
  {
    var lines = new List<(string, string)>();
    if (_sessionStart.HasValue)
      lines.Add(("Session start", $"{_sessionStart.Value:yyyy-MM-dd HH:mm:ss}"));
    lines.Add(("Length", $"{_sessionDuration:hh\\:mm\\:ss}"));
    lines.Add(("Riders entered", _findings.Count.ToString()));
    return lines;
  }

  private string GenerateTextReport()
  {
    var sb = new StringBuilder();
    sb.AppendLine(_title);
    sb.AppendLine(new string('=', Math.Max(_title.Length, 40)));
    sb.AppendLine();

    var problems = _findings.Count(f => f.Verdict != TransponderVerdict.Clean);
    sb.AppendLine(problems == 0
      ? $"All {_findings.Count} transponders reading cleanly."
      : $"{problems} of {_findings.Count} riders need attention.");
    sb.AppendLine();

    foreach (var (caption, value) in DescribeSession())
      sb.AppendLine($"{caption + ":",-18}{value}");
    sb.AppendLine();

    foreach (var finding in _findings)
      sb.AppendLine($"{finding.Rider.RiderNumber,-6} {RiderName(finding.Rider),-24} {finding.Detail}");

    var present = _findings
      .Select(f => f.Verdict)
      .Where(v => v != TransponderVerdict.Clean)
      .Distinct()
      .OrderByDescending(v => (int)v)
      .ToList();

    if (present.Count > 0)
    {
      sb.AppendLine();
      sb.AppendLine("What to check");
      sb.AppendLine(new string('-', 40));
      foreach (var verdict in present) sb.AppendLine("  " + TransponderCheck.Advice(verdict));
    }

    sb.AppendLine();
    sb.AppendLine($"Generated {_generatedAt:yyyy-MM-dd HH:mm:ss}");
    return sb.ToString();
  }

  private void ExportToExcel(string path)
  {
    using var workbook = new XLWorkbook();
    var sheet = workbook.Worksheets.Add("Transponder Check");

    var row = 1;
    sheet.Cell(row, 1).Value = _title;
    sheet.Cell(row, 1).Style.Font.FontSize = 16;
    sheet.Cell(row, 1).Style.Font.Bold = true;
    sheet.Range(row, 1, row, Sheet.Length).Merge();
    row += 2;

    foreach (var (caption, value) in DescribeSession())
    {
      sheet.Cell(row, 1).Value = caption + ":";
      sheet.Cell(row, 2).Value = value;
      row++;
    }
    row++;

    var headers = new[] { "Number", "Rider", "Class", "Laps", "Missed reads", "Double reads", "What the loop saw" };
    for (var i = 0; i < headers.Length; i++)
    {
      var cell = sheet.Cell(row, i + 1);
      cell.Value = headers[i];
      cell.Style.Font.Bold = true;
      cell.Style.Fill.BackgroundColor = XLColor.LightGray;
      cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
    }
    row++;

    foreach (var finding in _findings)
    {
      sheet.Cell(row, 1).Value = finding.Rider.RiderNumber;
      sheet.Cell(row, 2).Value = RiderName(finding.Rider);
      sheet.Cell(row, 3).Value = finding.Rider.Category;
      sheet.Cell(row, 4).Value = finding.Laps;
      sheet.Cell(row, 5).Value = finding.SuspectedMisses;
      sheet.Cell(row, 6).Value = finding.DuplicateReads;
      sheet.Cell(row, 7).Value = finding.Detail;

      sheet.Row(row).Style.Fill.BackgroundColor = finding.Verdict switch
      {
        TransponderVerdict.NeverRead => XLColor.FromArgb(255, 205, 205),
        TransponderVerdict.WentQuiet => XLColor.FromArgb(255, 228, 196),
        TransponderVerdict.Intermittent => XLColor.FromArgb(255, 242, 204),
        TransponderVerdict.DoubleReads => XLColor.FromArgb(255, 250, 225),
        _ => XLColor.White
      };

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
}
