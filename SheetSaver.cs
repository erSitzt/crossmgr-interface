using System.Drawing.Printing;

namespace CrossMgrInterface;

/// <summary>
/// Where the sheets of a day go when they are saved by themselves, and the
/// two things that make that work without anybody at the keyboard: a file
/// name that is free to write, and a PDF printed without a print dialog.
///
/// The folder is one per day and one per session in it, numbered in the order
/// they ran, so the secretary finds "03 Moto 1 - MX1 / MX2" where they expect
/// it and the morning's practice is not mixed in with the afternoon's motos.
/// </summary>
public static class SheetSaver
{
  /// <summary>Windows' own PDF printer, there on every Windows 10 and 11 unless someone removed it.</summary>
  public const string PdfPrinter = "Microsoft Print to PDF";

  public static string DefaultRoot =>
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CrossMgr Results");

  /// <summary>"…\CrossMgr Results\2026-10-18 Clubrennen".</summary>
  public static string DayFolder(string? root, DateTime day, string? dayName)
  {
    var name = string.IsNullOrWhiteSpace(dayName) ? $"{day:yyyy-MM-dd}" : $"{day:yyyy-MM-dd} {dayName.Trim()}";
    return Path.Combine(string.IsNullOrWhiteSpace(root) ? DefaultRoot : root, ReportHelpers.SanitizeFileName(name));
  }

  /// <summary>"03 Moto 1 - MX1 / MX2" → a folder name Windows accepts.</summary>
  public static string SessionFolder(string dayFolder, int order, string sessionName)
  {
    var name = string.IsNullOrWhiteSpace(sessionName) ? "Session" : sessionName.Trim();
    return Path.Combine(dayFolder, ReportHelpers.SanitizeFileName($"{order:00} {name}"));
  }

  /// <summary>
  /// <paramref name="fileName"/> in the folder, or "name (2)", "name (3)" when
  /// the file is open somewhere - most often in Excel, by the secretary looking
  /// at the last version. Overwriting what is not open is the point: a sheet
  /// saved again after a correction replaces the one before it.
  /// </summary>
  public static string WritablePath(string folder, string fileName)
  {
    var stem = Path.GetFileNameWithoutExtension(fileName);
    var extension = Path.GetExtension(fileName);

    for (var attempt = 1; attempt < 50; attempt++)
    {
      var path = Path.Combine(folder, attempt == 1 ? fileName : $"{stem} ({attempt}){extension}");
      if (!File.Exists(path) || CanWrite(path)) return path;
    }

    return Path.Combine(folder, $"{stem} {DateTime.Now:HHmmss}{extension}");
  }

  private static bool CanWrite(string path)
  {
    try
    {
      using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
      return true;
    }
    catch (IOException)
    {
      return false;
    }
    catch (UnauthorizedAccessException)
    {
      return false;
    }
  }

  public static bool PdfPrinterInstalled()
  {
    try
    {
      return PrinterSettings.InstalledPrinters.Cast<string>().Contains(PdfPrinter, StringComparer.OrdinalIgnoreCase);
    }
    catch (Exception)
    {
      return false;
    }
  }

  /// <summary>
  /// Prints a document to a PDF file without any window: the PDF printer, told
  /// the file name, and the plain print controller - the usual one puts up a
  /// "Printing..." box. Best effort: the spooler writes the file a moment after
  /// this returns, and a PDF open in a viewer is not replaced.
  /// </summary>
  public static bool PrintToPdf(PrintDocument document, string path)
  {
    if (!PdfPrinterInstalled()) return false;

    var settings = document.PrinterSettings;
    var controller = document.PrintController;
    try
    {
      document.PrinterSettings = new PrinterSettings
      {
        PrinterName = PdfPrinter,
        PrintToFile = true,
        PrintFileName = path
      };
      document.PrintController = new StandardPrintController();
      document.Print();
      return true;
    }
    catch (Exception)
    {
      return false;
    }
    finally
    {
      document.PrinterSettings = settings;
      document.PrintController = controller;
    }
  }
}
