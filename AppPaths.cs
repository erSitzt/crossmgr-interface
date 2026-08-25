namespace CrossMgrInterface;

/// <summary>
/// Stable per-user locations for everything the application writes.
///
/// These used to be relative paths ("races.db") or Application.StartupPath, which
/// meant the database silently followed the working directory - launching the app
/// from a different folder started a race with an empty database, and an install
/// under Program Files could not write its log at all.
/// </summary>
public static class AppPaths
{
  private static readonly string Root = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "CrossMgrInterface");

  /// <summary>Race database. Created on first use.</summary>
  public static string DatabaseFile => Path.Combine(EnsureRoot(), "races.db");

  /// <summary>Folder holding the rolling text logs.</summary>
  public static string LogsFolder
  {
    get
    {
      var dir = Path.Combine(EnsureRoot(), "logs");
      Directory.CreateDirectory(dir);
      return dir;
    }
  }

  /// <summary>User settings file (advanced mode, reader port, ...).</summary>
  public static string SettingsFile => Path.Combine(EnsureRoot(), "settings.json");

  /// <summary>
  /// Circuits, as JSON. Deliberately NOT in races.db: a track is a venue asset
  /// surveyed once and reused all season, whereas an unreadable races.db gets
  /// quarantined wholesale and a "Delete race data..." menu item wipes it. It is
  /// also a plain file, so a club can email a surveyed circuit to the next club
  /// using the same venue.
  /// </summary>
  public static string TracksFile => Path.Combine(EnsureRoot(), "tracks.json");

  /// <summary>
  /// Downloaded map tiles, laid out as tiles/{host}/{z}/{x}/{y}.png. Kept
  /// indefinitely - a circuit's basemap does not change during a season, and a
  /// timing laptop is regularly on a field with no usable internet.
  /// </summary>
  public static string TileCacheFolder
  {
    get
    {
      var dir = Path.Combine(EnsureRoot(), "tiles");
      Directory.CreateDirectory(dir);
      return dir;
    }
  }

  private static string EnsureRoot()
  {
    Directory.CreateDirectory(Root);
    return Root;
  }
}
