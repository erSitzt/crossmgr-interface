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
  /// <summary>Where the application keeps everything, unless a demo has moved it.</summary>
  public static string DefaultRoot { get; } = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "CrossMgrInterface");

  private static string Root = DefaultRoot;

  /// <summary>
  /// Sends everything the application writes into <paramref name="folder"/>.
  /// A demo calls this once, before the main window exists: the database, the
  /// settings and the log are opened from here on first use, and a demo must
  /// never write into the real race database or remember its setup as the
  /// club's own.
  /// </summary>
  public static void UseRoot(string folder) => Root = folder;

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
  /// The results website key, encrypted for this Windows user.
  ///
  /// Deliberately not in settings.json: that is the file that gets emailed to
  /// sort a problem out, pasted into an issue and copied between laptops, and
  /// AppSettings.Save swallows write failures by design - right for a
  /// preference, wrong for a credential. See PublishCredentials.
  /// </summary>
  public static string ResultsKeyFile => Path.Combine(EnsureRoot(), "results-key.dat");

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
