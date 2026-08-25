using ClosedXML.Excel;
using System.Data;

namespace CrossMgrInterface;

/// <summary>
/// Outcome of an import, including the rows that were skipped and why.
///
/// An import previously returned only a count and logged per-row failures to
/// Console.WriteLine - invisible in a WinExe - so a half-read roster looked like
/// a clean success and the missing riders only surfaced mid-race.
/// </summary>
public class ImportResult
{
  public int ImportedCount { get; set; }

  /// <summary>The riders that were read, so the operator can see them before committing.</summary>
  public List<RiderDataImporter.RiderImportData> Riders { get; } = new();
  public List<(int Row, string Reason)> Skipped { get; } = new();
  public List<string> DetectedColumns { get; set; } = new();
  public bool HasTagColumn { get; set; } = true;
}

/// <summary>
/// Service for importing rider data from Excel/CSV files
/// </summary>
public class RiderDataImporter
{
  private readonly Dictionary<string, RiderImportData> _riderDataLookup = new();

  /// <summary>
  /// Data structure for imported rider information
  /// </summary>
  public class RiderImportData
  {
    public string TagID { get; set; } = "";
    public string RiderNumber { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Team { get; set; } = "";
    public string Category { get; set; } = "";
    public string Machine { get; set; } = "";

    /// <summary>
    /// Full name combining first and last name
    /// </summary>
    public string FullName
    {
      get
      {
        if (!string.IsNullOrEmpty(FirstName) || !string.IsNullOrEmpty(LastName))
        {
          return $"{FirstName} {LastName}".Trim();
        }
        return "";
      }
    }
  }

  /// <summary>
  /// Import rider data from an Excel file
  /// </summary>
  /// <param name="filePath">Path to the Excel file</param>
  /// <returns>Number of riders imported</returns>
  public int ImportFromExcel(string filePath) => ImportFromExcelDetailed(filePath).ImportedCount;

  public ImportResult ImportFromExcelDetailed(string filePath)
  {
    var result = new ImportResult();
    try
    {
      _riderDataLookup.Clear();
      int importedCount = 0;

      using (var workbook = new XLWorkbook(filePath))
      {
        var worksheet = workbook.Worksheets.First();
        var rows = worksheet.RowsUsed().ToList();

        if (rows.Count <= 1) return result; // No data rows

        // Check if first row contains headers
        var firstRow = rows[0];
        var hasHeaders = HasHeaders(firstRow);

        Dictionary<string, int>? columnMap = null;
        int startRow = 0;

        if (hasHeaders)
        {
          // Parse headers to create column mapping
          columnMap = ParseExcelHeaders(firstRow);
          startRow = 1;
          result.DetectedColumns = columnMap.Keys.ToList();
          result.HasTagColumn = columnMap.Keys.Any(k => k.Contains("tag"));
        }

        // Parse data rows
        for (int i = startRow; i < rows.Count; i++)
        {
          try
          {
            var riderData = columnMap != null
              ? ParseRowWithHeaders(rows[i], columnMap)
              : ParseRowToRiderData(rows[i]);

            if (!string.IsNullOrEmpty(riderData.TagID))
            {
              _riderDataLookup[riderData.TagID.ToUpper()] = riderData;
              result.Riders.Add(riderData);
              importedCount++;
            }
            else
            {
              result.Skipped.Add((rows[i].RowNumber(), "no transponder ID"));
            }
          }
          catch (Exception ex)
          {
            result.Skipped.Add((rows[i].RowNumber(), ex.Message));
          }
        }
      }

      result.ImportedCount = importedCount;
      return result;
    }
    catch (Exception ex)
    {
      throw new Exception($"Error importing Excel file: {ex.Message}", ex);
    }
  }

  /// <summary>
  /// Import rider data from a CSV file
  /// </summary>
  /// <param name="filePath">Path to the CSV file</param>
  /// <returns>Number of riders imported</returns>
  public int ImportFromCsv(string filePath) => ImportFromCsvDetailed(filePath).ImportedCount;

  public ImportResult ImportFromCsvDetailed(string filePath)
  {
    var result = new ImportResult();
    try
    {
      _riderDataLookup.Clear();
      int importedCount = 0;

      var lines = File.ReadAllLines(filePath);
      if (lines.Length <= 1) return result; // No data rows

      // Parse header to determine column indices
      var header = lines[0].Split(',');
      var columnMap = ParseHeader(header);
      result.DetectedColumns = columnMap.Keys.ToList();
      result.HasTagColumn = columnMap.Keys.Any(k => k.Contains("tag"));

      // Parse data rows
      for (int i = 1; i < lines.Length; i++)
      {
        try
        {
          var values = ParseCsvLine(lines[i]);
          var riderData = ParseValuesToRiderData(values, columnMap);

          if (!string.IsNullOrEmpty(riderData.TagID))
          {
            _riderDataLookup[riderData.TagID.ToUpper()] = riderData;
            result.Riders.Add(riderData);
            importedCount++;
          }
          else
          {
            result.Skipped.Add((i + 1, "no transponder ID"));
          }
        }
        catch (Exception ex)
        {
          result.Skipped.Add((i + 1, ex.Message));
        }
      }

      result.ImportedCount = importedCount;
      return result;
    }
    catch (Exception ex)
    {
      throw new Exception($"Error importing CSV file: {ex.Message}", ex);
    }
  }

  /// <summary>
  /// Get rider data for a specific tag ID
  /// </summary>
  /// <param name="tagId">Tag ID to look up</param>
  /// <returns>Rider data if found, null otherwise</returns>
  public RiderImportData? GetRiderData(string tagId)
  {
    return _riderDataLookup.TryGetValue(tagId.ToUpper(), out var data) ? data : null;
  }

  /// <summary>
  /// Check if rider data exists for a tag ID
  /// </summary>
  /// <param name="tagId">Tag ID to check</param>
  /// <returns>True if rider data exists</returns>
  public bool HasRiderData(string tagId)
  {
    return _riderDataLookup.ContainsKey(tagId.ToUpper());
  }

  /// <summary>
  /// Get all imported rider data
  /// </summary>
  /// <returns>Dictionary of tag ID to rider data</returns>
  public Dictionary<string, RiderImportData> GetAllRiderData()
  {
    return new Dictionary<string, RiderImportData>(_riderDataLookup);
  }

  /// <summary>
  /// Clear all imported rider data
  /// </summary>
  public void Clear()
  {
    _riderDataLookup.Clear();
  }

  /// <summary>
  /// Get the number of imported riders
  /// </summary>
  public int Count => _riderDataLookup.Count;

  private RiderImportData ParseRowToRiderData(IXLRow row)
  {
    var riderData = new RiderImportData();

    // Expected column order based on sample_riders.SCH:
    // Column 1: TagID
    // Column 2: RiderNumber  
    // Column 3: Name (full name)
    // Column 4: Team
    var cellCount = row.CellsUsed().Count();

    if (cellCount >= 1) riderData.TagID = GetCellValue(row.Cell(1));
    if (cellCount >= 2) riderData.RiderNumber = GetCellValue(row.Cell(2));
    if (cellCount >= 3)
    {
      var fullName = GetCellValue(row.Cell(3));
      // If it contains a space, split into first and last name
      if (fullName.Contains(' '))
      {
        var parts = fullName.Split(' ', 2);
        riderData.FirstName = parts[0];
        riderData.LastName = parts[1];
      }
      else
      {
        riderData.FirstName = fullName;
      }
    }
    if (cellCount >= 4) riderData.Team = GetCellValue(row.Cell(4));
    if (cellCount >= 5) riderData.Category = GetCellValue(row.Cell(5));
    if (cellCount >= 6) riderData.Machine = GetCellValue(row.Cell(6));

    return riderData;
  }

  private string GetCellValue(IXLCell cell)
  {
    return cell.Value.ToString()?.Trim() ?? "";
  }

  private Dictionary<string, int> ParseHeader(string[] header)
  {
    var columnMap = new Dictionary<string, int>();

    for (int i = 0; i < header.Length; i++)
    {
      var columnName = header[i].Trim().ToLower();
      columnMap[columnName] = i;
    }

    return columnMap;
  }

  private string[] ParseCsvLine(string line)
  {
    // Simple CSV parsing - handles basic cases
    // For more complex CSV with quoted fields, consider using a proper CSV library
    return line.Split(',').Select(s => s.Trim().Trim('"')).ToArray();
  }

  private RiderImportData ParseValuesToRiderData(string[] values, Dictionary<string, int> columnMap)
  {
    var riderData = new RiderImportData();

    // Map common column names to rider data fields
    if (TryGetValue(values, columnMap, new[] { "tagid", "tag", "id" }, out string tagId))
      riderData.TagID = tagId;

    if (TryGetValue(values, columnMap, new[] { "name", "fullname", "rider" }, out string fullName))
    {
      // Split full name into first and last
      if (fullName.Contains(' '))
      {
        var parts = fullName.Split(' ', 2);
        riderData.FirstName = parts[0];
        riderData.LastName = parts[1];
      }
      else
      {
        riderData.FirstName = fullName;
      }
    }

    if (TryGetValue(values, columnMap, new[] { "firstname", "first" }, out string firstName))
      riderData.FirstName = firstName;

    if (TryGetValue(values, columnMap, new[] { "lastname", "last", "surname" }, out string lastName))
      riderData.LastName = lastName;

    if (TryGetValue(values, columnMap, new[] { "team", "club", "sponsor" }, out string team))
      riderData.Team = team;

    if (TryGetValue(values, columnMap, new[] { "number", "ridernumber", "bib" }, out string number))
      riderData.RiderNumber = number;

    if (TryGetValue(values, columnMap, new[] { "category", "class", "division" }, out string category))
      riderData.Category = category;

    if (TryGetValue(values, columnMap, new[] { "machine", "bike", "motorcycle" }, out string machine))
      riderData.Machine = machine;

    return riderData;
  }

  private bool HasHeaders(IXLRow firstRow)
  {
    // Check if first row contains text that looks like headers
    var cells = firstRow.CellsUsed().Take(4).ToList();
    if (cells.Count == 0) return false;

    // Look for common header patterns
    foreach (var cell in cells)
    {
      var value = GetCellValue(cell).ToLower();
      if (value.Contains("tag") || value.Contains("number") || value.Contains("name") ||
          value.Contains("team") || value.Contains("rider") || value.Contains("id"))
      {
        return true;
      }
    }

    return false;
  }

  private Dictionary<string, int> ParseExcelHeaders(IXLRow headerRow)
  {
    var columnMap = new Dictionary<string, int>();
    var cells = headerRow.CellsUsed().ToList();

    for (int i = 0; i < cells.Count; i++)
    {
      var columnName = GetCellValue(cells[i]).Trim().ToLower();
      if (!string.IsNullOrEmpty(columnName))
      {
        columnMap[columnName] = i + 1; // Excel columns are 1-based
      }
    }

    return columnMap;
  }

  private RiderImportData ParseRowWithHeaders(IXLRow row, Dictionary<string, int> columnMap)
  {
    var riderData = new RiderImportData();

    // Convert Excel row to string array for reuse with existing logic
    var maxColumn = columnMap.Values.Max();
    var values = new string[maxColumn];

    for (int i = 1; i <= maxColumn; i++)
    {
      values[i - 1] = GetCellValue(row.Cell(i));
    }

    // Convert column map to 0-based indexing for consistency with CSV parser
    var zeroBasedColumnMap = columnMap.ToDictionary(
      kvp => kvp.Key,
      kvp => kvp.Value - 1
    );

    return ParseValuesToRiderData(values, zeroBasedColumnMap);
  }

  private bool TryGetValue(string[] values, Dictionary<string, int> columnMap, string[] possibleColumnNames, out string value)
  {
    value = "";

    foreach (var columnName in possibleColumnNames)
    {
      if (columnMap.TryGetValue(columnName, out int index) && index < values.Length)
      {
        value = values[index];
        return !string.IsNullOrEmpty(value);
      }
    }

    return false;
  }
}
