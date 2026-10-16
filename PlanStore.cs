using System.Text.Json;

namespace CrossMgrInterface;

/// <summary>
/// The day plans this machine knows, one .cmplan file each in plans/ beside
/// settings.json.
///
/// Files rather than a collection in races.db, for the reasons TrackStore
/// gives: races.db is set aside wholesale when it cannot be read, and Delete
/// this session... is a menu item - a plan written the evening before must
/// survive both. And a plan is a file the secretary can email to the timing
/// laptop. What has happened on the day is never in here; see DayPlan.
/// </summary>
public sealed class PlanStore
{
  private static readonly JsonSerializerOptions Options = new()
  {
    WriteIndented = true,
    PropertyNameCaseInsensitive = true
  };

  public const string Extension = ".cmplan";

  private readonly List<DayPlan> _plans;

  public string Folder { get; }

  /// <summary>Files that would not read, set aside under a new name. Kept: they are somebody's work.</summary>
  public IReadOnlyList<string> SetAside { get; }

  public IReadOnlyList<DayPlan> Plans => _plans;

  private PlanStore(string folder, List<DayPlan> plans, List<string> setAside)
  {
    Folder = folder;
    _plans = plans;
    SetAside = setAside;
  }

  /// <summary>Reads every plan. Never throws: the application must start whatever is in the folder.</summary>
  public static PlanStore Load(string? folder = null)
  {
    folder ??= AppPaths.PlansFolder;
    var plans = new List<DayPlan>();
    var setAside = new List<string>();

    try
    {
      if (Directory.Exists(folder))
        foreach (var file in Directory.GetFiles(folder, "*" + Extension))
        {
          var plan = Read(file);
          if (plan != null) plans.Add(plan);
          else if (Quarantine(file) is { } moved) setAside.Add(moved);
        }
    }
    catch (Exception)
    {
      // A folder that cannot be listed is a day without plans, not a crash.
    }

    return new PlanStore(folder, plans, setAside);
  }

  /// <summary>Newest day first, then by name.</summary>
  public IReadOnlyList<DayPlan> Newest() =>
    _plans.OrderByDescending(p => p.Date).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();

  public DayPlan? Find(string? id) => id == null ? null : _plans.FirstOrDefault(p => p.Id == id);

  /// <summary>The plans for one day.</summary>
  public IReadOnlyList<DayPlan> For(DateTime date) => _plans.Where(p => p.Date.Date == date.Date).ToList();

  /// <summary>
  /// Writes one plan via a temporary file - a laptop that loses power mid-save
  /// must keep the plan, not half of it - and adds it if it is new.
  /// </summary>
  public void Save(DayPlan plan)
  {
    plan.ModifiedUtc = DateTime.UtcNow;
    if (!_plans.Contains(plan))
    {
      _plans.RemoveAll(p => p.Id == plan.Id);
      _plans.Add(plan);
    }

    Directory.CreateDirectory(Folder);
    var path = PathOf(plan);
    var temp = path + ".tmp";
    File.WriteAllText(temp, ExportJson(plan));
    File.Move(temp, path, overwrite: true);
  }

  public void Delete(DayPlan plan)
  {
    _plans.Remove(plan);
    try { File.Delete(PathOf(plan)); } catch (Exception) { }
  }

  private string PathOf(DayPlan plan) => Path.Combine(Folder, plan.Id + Extension);

  // ---- Sharing a plan between machines --------------------------------------

  public static string ExportJson(DayPlan plan) => JsonSerializer.Serialize(plan, Options);

  /// <summary>A plan from a file someone sent, or null when it is not one.</summary>
  public static DayPlan? ImportJson(string json)
  {
    try
    {
      var plan = JsonSerializer.Deserialize<DayPlan>(json, Options);
      if (plan == null || string.IsNullOrWhiteSpace(plan.Id)) return null;
      plan.Sessions ??= new List<PlannedSession>();
      foreach (var session in plan.Sessions)
      {
        session.Classes ??= new List<string>();
        if (string.IsNullOrWhiteSpace(session.Id)) session.Id = DayPlan.NewId();
      }
      return plan;
    }
    catch (Exception)
    {
      return null;
    }
  }

  private static DayPlan? Read(string file)
  {
    try { return ImportJson(File.ReadAllText(file)); }
    catch (Exception) { return null; }
  }

  private static string? Quarantine(string file)
  {
    try
    {
      var moved = $"{file}.unreadable-{DateTime.Now:yyyyMMdd_HHmmss}";
      File.Move(file, moved);
      return moved;
    }
    catch (Exception)
    {
      return null;
    }
  }
}
