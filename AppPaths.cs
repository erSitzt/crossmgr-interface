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

  private static string EnsureRoot()
  {
    Directory.CreateDirectory(Root);
    return Root;
  }
}
