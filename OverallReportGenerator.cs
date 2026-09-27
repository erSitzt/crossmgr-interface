using System.Drawing.Printing;
using ClosedXML.Excel;

namespace CrossMgrInterface;

/// <summary>
/// Prints and exports an overall: one sheet per class, each rider's place and
/// points in every moto and the total, the rules it was scored under at the
/// bottom so a protest about a tie can be settled from the paper.
///
/// A class of its own, like <see cref="QualifyingReportGenerator"/>, rather
/// than more methods on the race report with its long-lived cursors. Everything
/// comes from <see cref="OverallResult"/>, which is also what the Overall
/// results window shows, so the screen and the paper cannot disagree.
/// </summary>
public sealed class OverallReportGenerator : IDisposable
{
  private readonly PrintDocument _printDocument;
  private OverallResult? _data;
  private string _title = "";
  private int _classIndex;
  private int _entryIndex;
  private int _pageNumber;

  public OverallReportGenerator()
  {
    _printDocument = new PrintDocument();
    _printDocument.PrintPage += PrintDocument_PrintPage;
  }

  public void Run(IWin32Window owner, OverallResult result, string title, ReportAction action)
  {
    switch (action)
    {
      case ReportAction.Preview:
        ShowPrintPreview(result, title);
        break;
      case ReportAction.Print:
        Print(result, title);
        break;
      case ReportAction.Export:
        Export(owner, result, title);
        break;
    }
  }

  public void ShowPrintPreview(OverallResult result, string title)
  {
    Prepare(result, title);
    using var preview = new PrintPreviewDialog
    {
      Document = _printDocument,
      WindowState = FormWindowState.Maximized
    };
    preview.ShowDialog();
  }

  public void Print(OverallResult result, string title)
  {
    Prepare(result, title);
    using var dialog = new PrintDialog { Document = _printDocument };
    if (dialog.ShowDialog() == DialogResult.OK) _printDocument.Print();
  }

  /// <summary>One workbook, a worksheet per class: the overall is one result, not one per class.</summary>
  public void Export(IWin32Window owner, OverallResult result, string title)
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
      MessageBox.Show(owner, $"Overall results exported to:\n{save.FileName}", "Export complete",
        MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(owner, "Could not export the overall results.",
        "Check the file is not open in Excel and the folder can be written to, then try again.", ex);
    }
  }

  // ---- Shared layout -------------------------------------------------------

  /// <summary>Header row: Pos, #, Rider, then place and points per moto, then Total.</summary>
  public static List<string> Headers(OverallResult result)
  {
    var headers = new List<string> { "Pos", "#", "Rider" };
    for (var m = 0; m < result.MotoTitles.Count; m++)
    {
      headers.Add($"Moto {m + 1}");
      headers.Add("Pts");
    }
    headers.Add("Total");
    return headers;
  }

  public static List<string> Cells(OverallEntry entry)
  {
    var cells = new List<string> { entry.RankText, entry.Number, RiderText(entry) };
    foreach (var moto in entry.Motos)
    {
      cells.Add(moto.Text);
      cells.Add(moto.Placed ? moto.Points.ToString() : "");
    }
    cells.Add(entry.Points.ToString());
    return cells;
  }

  public static string RiderText(OverallEntry entry)
  {
    var name = entry.Name.Length > 0 ? entry.Name : entry.Number.Length > 0 ? $"#{entry.Number}" : "Unknown rider";
    return !entry.IsTeam && entry.Team.Length > 0 ? $"{name} ({entry.Team})" : name;
  }

  public static string ClassTitle(OverallClass cls) => cls.Name.Length > 0 ? cls.Name : "No class";

  /// <summary>The block above the table: which motos count, and whether any is still running.</summary>
  public static List<(string Caption, string Value)> DescribeMotos(OverallResult result)
  {
    var lines = result.MotoTitles.Select((t, i) => ($"Moto {i + 1}", t)).ToList();
    if (result.UnfinishedMotos.Count > 0)
      lines.Add(("Provisional", $"not finished yet: {string.Join(", ", result.UnfinishedMotos)}"));
    return lines;
  }

  // ---- Printing ------------------------------------------------------------

  private void Prepare(OverallResult result, string title)
  {
    _data = result;
    _title = title;
    _classIndex = 0;
    _entryIndex = 0;
    _pageNumber = 0;
  }

  private const float ColumnGap = 6f;

  private void PrintDocument_PrintPage(object? sender, PrintPageEventArgs e)
  {
    if (_data == null || e.Graphics == null) return;

    var g = e.Graphics;
    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

    var bounds = e.MarginBounds;
    var y = (float)bounds.Top;

    using var titleFont = new Font("Segoe UI", 15, FontStyle.Bold);
    using var classFont = new Font("Segoe UI", 12, FontStyle.Bold);
    using var infoFont = new Font("Segoe UI", 8.5f);
    using var headerFont = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold);
    using var rowFont = new Font("Segoe UI", 9);
    using var rowBoldFont = new Font("Segoe UI", 9, FontStyle.Bold);
    using var ink = new SolidBrush(Color.Black);
    using var quiet = new SolidBrush(Color.FromArgb(105, 105, 105));
    using var warn = new SolidBrush(Color.FromArgb(160, 60, 0));
    using var rule = new Pen(Color.Black, 0.8f);

    _pageNumber++;

    if (_data.Classes.Count == 0)
    {
      g.DrawString(_title, titleFont, ink, bounds.Left, y);
      y += titleFont.GetHeight(g) + 8;
      g.DrawString("Nobody has been scored in these motos.", rowFont, quiet, bounds.Left, y);
      Finish();
      return;
    }

    var cls = _data.Classes[_classIndex];
    var headers = Headers(_data);
    var weights = Weights(headers.Count);

    if (_entryIndex == 0)
    {
      g.DrawString(_title, titleFont, ink, bounds.Left, y);
      y += titleFont.GetHeight(g) + 4;
      g.DrawString(ClassTitle(cls), classFont, ink, bounds.Left, y);
      y += classFont.GetHeight(g) + 8;

      foreach (var (caption, value) in DescribeMotos(_data))
      {
        g.DrawString(caption, infoFont, quiet, bounds.Left, y);
        g.DrawString(value, infoFont, ink, bounds.Left + 90, y);
        y += infoFont.GetHeight(g) + 2;
      }
      y += 10;
    }
    else
    {
      g.DrawString($"{_title} - {ClassTitle(cls)}  (continued)", headerFont, ink, bounds.Left, y);
      y += headerFont.GetHeight(g) + 10;
    }

    var rowHeight = rowFont.GetHeight(g) + 7;
    var rulesHeight = (infoFont.GetHeight(g) + 2) * (_data.Rules.Describe().Count + 2) + 16;

    DrawRow(headers, headerFont, ink);
    y += headerFont.GetHeight(g) + 3;
    g.DrawLine(rule, bounds.Left, y, bounds.Right, y);
    y += 5;

    while (_entryIndex < cls.Entries.Count)
    {
      if (y + rowHeight > bounds.Bottom - rowHeight)
      {
        DrawFooter(continued: true);
        e.HasMorePages = true;
        return;
      }

      var entry = cls.Entries[_entryIndex];
      var font = entry.Rank is <= 3 ? rowBoldFont : rowFont;
      DrawRow(Cells(entry), font, entry.Rank.HasValue ? ink : quiet);
      y += rowHeight;

      if (entry.Warning != null)
      {
        g.DrawString("! " + entry.Warning, infoFont, warn, bounds.Left + bounds.Width * 0.14f, y - 4);
        y += infoFont.GetHeight(g) + 2;
      }
      _entryIndex++;
    }

    // The rules go under the table of every class, on a page of their own if
    // the table filled this one.
    if (y + rulesHeight > bounds.Bottom - infoFont.GetHeight(g))
    {
      DrawFooter(continued: true);
      e.HasMorePages = true;
      _entryIndex = int.MaxValue;
      return;
    }

    y += 12;
    foreach (var (caption, value) in _data.Rules.Describe())
    {
      g.DrawString(caption, infoFont, quiet, bounds.Left, y);
      g.DrawString(value, infoFont, ink, new RectangleF(bounds.Left + 90, y, bounds.Width - 90, infoFont.GetHeight(g) * 2 + 2));
      y += infoFont.GetHeight(g) + 2;
    }

    DrawFooter(continued: false);

    _classIndex++;
    _entryIndex = 0;
    if (_classIndex < _data.Classes.Count)
    {
      e.HasMorePages = true;
      return;
    }

    Finish();
    return;

    void Finish()
    {
      e.HasMorePages = false;
      // A preview raises PrintPage again when scrolled back; start over then.
      _classIndex = 0;
      _entryIndex = 0;
      _pageNumber = 0;
    }

    void DrawRow(IReadOnlyList<string> cells, Font font, Brush brush)
    {
      var x = (float)bounds.Left;
      for (var i = 0; i < cells.Count; i++)
      {
        var width = bounds.Width * weights[i];
        var rightAligned = i != 2;
        if (!string.IsNullOrEmpty(cells[i]))
        {
          using var format = new StringFormat(StringFormatFlags.NoWrap)
          {
            Alignment = rightAligned ? StringAlignment.Far : StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter
          };
          g.DrawString(cells[i], i == cells.Count - 1 ? rowBoldFont : font, brush,
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

  /// <summary>Pos, # and Total narrow, the name wide, the moto columns sharing what is left.</summary>
  private static float[] Weights(int columns)
  {
    var motoColumns = columns - 4;
    var weights = new float[columns];
    weights[0] = 0.07f;
    weights[1] = 0.07f;
    weights[columns - 1] = 0.09f;
    var name = motoColumns <= 4 ? 0.40f : 0.30f;
    weights[2] = name;
    var each = (1f - 0.07f - 0.07f - 0.09f - name) / Math.Max(1, motoColumns);
    for (var i = 3; i < columns - 1; i++) weights[i] = each;
    return weights;
  }

  // ---- Excel ---------------------------------------------------------------

  public static XLWorkbook BuildWorkbook(OverallResult result, string title)
  {
    var workbook = new XLWorkbook();
    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var cls in result.Classes)
    {
      var sheet = workbook.Worksheets.Add(SheetName(ClassTitle(cls), used));
      var row = 1;

      sheet.Cell(row, 1).Value = title;
      sheet.Cell(row, 1).Style.Font.Bold = true;
      sheet.Cell(row, 1).Style.Font.FontSize = 14;
      row++;
      sheet.Cell(row, 1).Value = ClassTitle(cls);
      sheet.Cell(row, 1).Style.Font.Bold = true;
      row += 2;

      foreach (var (caption, value) in DescribeMotos(result))
      {
        sheet.Cell(row, 1).Value = caption;
        sheet.Cell(row, 2).Value = value;
        row++;
      }
      row++;

      var headers = Headers(result);
      for (var c = 0; c < headers.Count; c++)
      {
        sheet.Cell(row, c + 1).Value = headers[c];
        sheet.Cell(row, c + 1).Style.Font.Bold = true;
      }
      sheet.Cell(row, headers.Count + 1).Value = "Note";
      sheet.Cell(row, headers.Count + 1).Style.Font.Bold = true;
      row++;

      foreach (var entry in cls.Entries)
      {
        var cells = Cells(entry);
        for (var c = 0; c < cells.Count; c++)
        {
          // Numbers as numbers, so the secretary can sort and add in Excel.
          if (int.TryParse(cells[c], out var n) && c != 1) sheet.Cell(row, c + 1).Value = n;
          else sheet.Cell(row, c + 1).Value = cells[c];
        }
        if (entry.Warning != null) sheet.Cell(row, cells.Count + 1).Value = entry.Warning;
        row++;
      }

      row++;
      foreach (var (caption, value) in result.Rules.Describe())
      {
        sheet.Cell(row, 1).Value = caption;
        sheet.Cell(row, 2).Value = value;
        row++;
      }

      sheet.Columns().AdjustToContents();
    }

    if (workbook.Worksheets.Count == 0) workbook.Worksheets.Add("Overall").Cell(1, 1).Value = "Nobody has been scored.";
    return workbook;
  }

  /// <summary>Excel allows 31 characters, none of []:*?/\, and no two sheets alike.</summary>
  private static string SheetName(string name, HashSet<string> used)
  {
    var clean = new string(name.Where(c => "[]:*?/\\".IndexOf(c) < 0).ToArray()).Trim();
    if (clean.Length == 0) clean = "Class";
    if (clean.Length > 31) clean = clean[..31];

    var candidate = clean;
    for (var i = 2; !used.Add(candidate); i++)
    {
      var suffix = $" ({i})";
      candidate = (clean.Length + suffix.Length > 31 ? clean[..(31 - suffix.Length)] : clean) + suffix;
    }
    return candidate;
  }

  public void Dispose() => _printDocument.Dispose();
}
