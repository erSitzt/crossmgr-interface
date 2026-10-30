using System.Text.Json;

namespace CrossMgrInterface;

/// <summary>One preset the club keeps: a name, a note, and the code that is the plan.</summary>
public sealed class StoredPreset
{
  public string Name { get; set; } = "";
  public string Description { get; set; } = "";

  /// <summary>The plan itself. See DayPlanCode.</summary>
  public string Code { get; set; } = "";

  /// <summary>The short code, once it has been put on the results website. See DayPlanCode.ShortCodeOf.</summary>
  public string? ShortCode { get; set; }

  public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// The club's own presets, in presets.json beside settings.json - the list a
/// club keeps of its race days, as codes.
///
/// Only the codes are kept, not the plans: the code is the plan, and keeping
/// it as written means the list holds exactly what was copied into it. One
/// that this version cannot read - made by a newer one - stays in the list,
/// marked, rather than being thrown away.
///
/// A file of its own, not races.db and not settings.json, for the reasons
/// TrackStore and PlanStore give: it is a club's work, and it must survive a
/// damaged database and a settings file that failed to save.
/// </summary>
public sealed class PresetStore
{
  private static readonly JsonSerializerOptions Options = new()
  {
    WriteIndented = true,
    PropertyNameCaseInsensitive = true
  };

  private readonly List<StoredPreset> _presets;

  public string Path { get; }

  /// <summary>Set when the file could not be read and was set aside.</summary>
  public string? RecoveredFrom { get; private set; }

  public IReadOnlyList<StoredPreset> Presets => _presets;

  private PresetStore(string path, List<StoredPreset> presets)
  {
    Path = path;
    _presets = presets;
  }

  /// <summary>Reads the list. Never throws: the application must start whatever is in the file.</summary>
  public static PresetStore Load(string? path = null)
  {
    path ??= AppPaths.PresetsFile;
    try
    {
      if (!File.Exists(path)) return new PresetStore(path, new List<StoredPreset>());
      var presets = JsonSerializer.Deserialize<List<StoredPreset>>(File.ReadAllText(path), Options);
      if (presets != null) return new PresetStore(path, presets.Where(p => p != null).ToList());
    }
    catch (Exception)
    {
      // Fall through to setting it aside.
    }

    var store = new PresetStore(path, new List<StoredPreset>());
    try
    {
      var moved = $"{path}.unreadable-{DateTime.Now:yyyyMMdd_HHmmss}";
      File.Move(path, moved);
      store.RecoveredFrom = moved;
    }
    catch (Exception)
    {
      // Kept where it is; a fresh list is written beside it only on the next save.
    }
    return store;
  }

  /// <summary>Via a temporary file: a laptop that loses power mid-save keeps the list.</summary>
  public void Save()
  {
    var directory = System.IO.Path.GetDirectoryName(Path);
    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
    var temp = Path + ".tmp";
    File.WriteAllText(temp, JsonSerializer.Serialize(_presets, Options));
    File.Move(temp, Path, overwrite: true);
  }

  /// <summary>
  /// The preset already in the list with this plan, however its code was
  /// typed or pasted. Codes that cannot be read are compared as written.
  /// </summary>
  public StoredPreset? FindSame(string code)
  {
    var key = Fingerprint(code);
    return _presets.FirstOrDefault(p => Fingerprint(p.Code) == key);
  }

  /// <summary>
  /// The preset a short code stands for, when it is one of this list's: the
  /// short code is worked out from the code, so this needs no website.
  /// </summary>
  public StoredPreset? FindShort(string shortCode) =>
    _presets.FirstOrDefault(p => string.Equals(DayPlanCode.ShortCodeOf(p.Code), shortCode, StringComparison.OrdinalIgnoreCase));

  /// <summary>Adds a preset, or returns the one already there with the same plan.</summary>
  public (StoredPreset Preset, bool Added) Add(string name, string description, string code)
  {
    if (FindSame(code) is { } existing) return (existing, false);

    var decoded = DayPlanCode.Decode(code);
    var preset = new StoredPreset
    {
      Name = name.Trim().Length > 0 ? name.Trim() : decoded.Plan?.Name ?? "Preset",
      Description = description.Trim(),
      Code = decoded.Plan != null ? DayPlanCode.Encode(decoded.Plan) : code.Trim()
    };
    _presets.Add(preset);
    return (preset, true);
  }

  public void Remove(StoredPreset preset) => _presets.Remove(preset);

  private static string Fingerprint(string code) =>
    DayPlanCode.Decode(code).Plan is { } plan ? DayPlanCode.Encode(plan) : DayPlanCode.Normalise(code);

  // ---- The list as a file --------------------------------------------------------

  /// <summary>
  /// The list as plain text, one block per preset - what a club emails round
  /// or pins up, and what Add codes... reads back.
  /// </summary>
  public static string ExportText(IEnumerable<(string Name, string Description, string Code, string? ShortCode)> presets)
  {
    var b = new System.Text.StringBuilder();
    foreach (var (name, description, code, shortCode) in presets)
    {
      b.AppendLine(name + (shortCode != null ? $"   ({shortCode})" : ""));
      if (description.Length > 0) b.AppendLine(description);
      b.AppendLine(code);
      b.AppendLine();
    }
    return b.ToString();
  }

  /// <summary>The list for Excel: name, description, short code, code.</summary>
  public static string ExportCsv(IEnumerable<(string Name, string Description, string Code, string? ShortCode)> presets)
  {
    static string Field(string? value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    var b = new System.Text.StringBuilder("Name,Description,Short code,Code\r\n");
    foreach (var (name, description, code, shortCode) in presets)
      b.Append($"{Field(name)},{Field(description)},{Field(shortCode)},{Field(code)}\r\n");
    return b.ToString();
  }
}
