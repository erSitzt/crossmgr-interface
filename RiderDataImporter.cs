using ClosedXML.Excel;
using System.Data;

namespace CrossMgrInterface;

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
  public int ImportFromExcel(string filePath)
  {
    try
    {
      _riderDataLookup.Clear();
      int importedCount = 0;

      using (var workbook = new XLWorkbook(filePath))
      {
        var worksheet = workbook.Worksheets.First();
        var rows = worksheet.RowsUsed().Skip(1); // Skip header row

        foreach (var row in rows)
        {
          try
          {
            var riderData = ParseRowToRiderData(row);
            if (!string.IsNullOrEmpty(riderData.TagID))
            {
              _riderDataLookup[riderData.TagID.ToUpper()] = riderData;
              importedCount++;
            }
          }
          catch (Exception ex)
          {
            // Log error but continue with other rows
            Console.WriteLine($"Error parsing row {row.RowNumber()}: {ex.Message}");
          }
        }
      }

      return importedCount;
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
  public int ImportFromCsv(string filePath)
  {
    try
    {
      _riderDataLookup.Clear();
      int importedCount = 0;

      var lines = File.ReadAllLines(filePath);
      if (lines.Length <= 1) return 0; // No data rows

      // Parse header to determine column indices
      var header = lines[0].Split(',');
      var columnMap = ParseHeader(header);

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
            importedCount++;
          }
        }
        catch (Exception ex)
        {
          // Log error but continue with other rows
          Console.WriteLine($"Error parsing CSV line {i + 1}: {ex.Message}");
        }
      }

      return importedCount;
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

    // Try to read from standard column positions or by header names
    var cellCount = row.CellsUsed().Count();

    if (cellCount >= 1) riderData.TagID = GetCellValue(row.Cell(1));
    if (cellCount >= 2)
    {
      var nameOrFirstName = GetCellValue(row.Cell(2));
      // If it contains a space, split into first and last name
      if (nameOrFirstName.Contains(' '))
      {
        var parts = nameOrFirstName.Split(' ', 2);
        riderData.FirstName = parts[0];
        riderData.LastName = parts[1];
      }
      else
      {
        riderData.FirstName = nameOrFirstName;
      }
    }
    if (cellCount >= 3) riderData.Team = GetCellValue(row.Cell(3));
    if (cellCount >= 4) riderData.LastName = GetCellValue(row.Cell(4));
    if (cellCount >= 5) riderData.RiderNumber = GetCellValue(row.Cell(5));
    if (cellCount >= 6) riderData.Category = GetCellValue(row.Cell(6));
    if (cellCount >= 7) riderData.Machine = GetCellValue(row.Cell(7));

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
