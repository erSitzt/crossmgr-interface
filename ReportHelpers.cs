namespace CrossMgrInterface;

/// <summary>
/// The pieces of report preparation that are the same whatever is being
/// printed: which classes are present, filtering to one of them, and turning a
/// class name into something a file system will accept.
///
/// Extracted from <see cref="RaceReportGenerator"/> when the gate pick sheet
/// arrived, so the two reports split a meeting into files identically rather
/// than drifting apart.
/// </summary>
public static class ReportHelpers
{
  /// <summary>Distinct rider classes, alphabetically. Riders with no class are skipped.</summary>
  public static List<string> GetUniqueClasses(Dictionary<string, RiderInfo> riders)
  {
    return riders.Values
      .Where(r => !string.IsNullOrEmpty(r.Category))
      .Select(r => r.Category)
      .Distinct()
      .OrderBy(c => c)
      .ToList();
  }

  public static Dictionary<string, RiderInfo> FilterRidersByClass(
    Dictionary<string, RiderInfo> riders, string className)
  {
    return riders
      .Where(kvp => kvp.Value.Category == className)
      .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
  }

  public static string SanitizeFileName(string fileName)
  {
    var invalidChars = Path.GetInvalidFileNameChars();
    return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
  }
}
