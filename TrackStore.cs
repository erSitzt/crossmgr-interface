using System.Text.Json;

namespace CrossMgrInterface;

/// <summary>
/// The circuits this machine knows about, in tracks.json beside settings.json.
///
/// Deliberately NOT a LiteDB collection in races.db, for three reasons:
///
///   - RaceDataService quarantines an unreadable races.db wholesale and starts
///     empty. That code path exists because it has fired. Losing yesterday's race
///     log to it is survivable; losing the circuit the club surveyed that morning,
///     discovered on race day, is not.
///   - "Delete race data..." is a menu item. A venue asset must not be one
///     careless click from deletion, nor need a carve-out someone will forget.
///   - A club that surveys a circuit wants to email it to the club using the same
///     venue next month. A JSON file is an attachment; a LiteDB page file is not.
///
/// A thinned loop is a few hundred points, so rewriting the whole file per save
/// costs nothing and no incremental-write machinery is needed.
/// </summary>
public sealed class TrackStore
{
  private static readonly JsonSerializerOptions Options = new()
  {
    WriteIndented = true,
    PropertyNameCaseInsensitive = true
  };

  private readonly List<TrackDefinition> _tracks;

  public string Path { get; }

  /// <summary>Set when the file on disk was unreadable and had to be set aside.</summary>
  public string? RecoveredFrom { get; private set; }

  public IReadOnlyList<TrackDefinition> Tracks => _tracks;

  private TrackStore(string path, List<TrackDefinition> tracks)
  {
    Path = path;
    _tracks = tracks;
  }

  /// <summary>
  /// Reads the store. Never throws, matching AppSettings.Load's contract - the
  /// application must start even with a damaged file. A file that will not parse
  /// is renamed rather than deleted: it is small enough to always keep, and it is
  /// the only copy of work someone did.
  /// </summary>
  public static TrackStore Load(string? path = null)
  {
    path ??= AppPaths.TracksFile;

    try
    {
      if (!File.Exists(path)) return new TrackStore(path, new List<TrackDefinition>());

      var tracks = JsonSerializer.Deserialize<List<TrackDefinition>>(File.ReadAllText(path), Options);
      if (tracks is not null)
      {
        foreach (var track in tracks) track.InvalidateGeometry();
        return new TrackStore(path, tracks);
      }
    }
    catch (Exception)
    {
      // Fall through to quarantine.
    }

    var store = new TrackStore(path, new List<TrackDefinition>());
    store.RecoveredFrom = Quarantine(path);
    return store;
  }

  /// <summary>
  /// Writes via a temporary file. A power cut on a timing laptop is a real event,
  /// and a half-written tracks.json would lose the circuit rather than the edit.
  /// </summary>
  public void Save()
  {
    var directory = System.IO.Path.GetDirectoryName(Path);
    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

    var temp = Path + ".tmp";
    File.WriteAllText(temp, JsonSerializer.Serialize(_tracks, Options));
    File.Move(temp, Path, overwrite: true);
  }

  public TrackDefinition? Find(string? id) =>
    string.IsNullOrEmpty(id) ? null : _tracks.FirstOrDefault(t => t.Id == id);

  /// <summary>Adds a track, or replaces the one with the same id in place.</summary>
  public void AddOrUpdate(TrackDefinition track)
  {
    track.ModifiedUtc = DateTime.UtcNow;

    var existing = _tracks.FindIndex(t => t.Id == track.Id);
    if (existing >= 0) _tracks[existing] = track;
    else _tracks.Add(track);
  }

  public bool Remove(string id)
  {
    var index = _tracks.FindIndex(t => t.Id == id);
    if (index < 0) return false;

    _tracks.RemoveAt(index);
    return true;
  }

  /// <summary>
  /// The only track, when there is exactly one. Part of the startup resolution
  /// order: a club with one circuit should never be asked to pick it.
  /// </summary>
  public TrackDefinition? Only => _tracks.Count == 1 ? _tracks[0] : null;

  // ---- Sharing a single circuit between machines ---------------------------

  public static string ExportJson(TrackDefinition track) =>
    JsonSerializer.Serialize(track, Options);

  public static TrackDefinition? ImportJson(string json)
  {
    try
    {
      var track = JsonSerializer.Deserialize<TrackDefinition>(json, Options);
      track?.InvalidateGeometry();
      return track;
    }
    catch (Exception)
    {
      return null;
    }
  }

  private static string? Quarantine(string path)
  {
    try
    {
      if (!File.Exists(path)) return null;

      var moved = $"{path}.unreadable-{DateTime.Now:yyyyMMdd_HHmmss}";
      File.Move(path, moved, overwrite: true);
      return moved;
    }
    catch (Exception)
    {
      return null;
    }
  }
}
