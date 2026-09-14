using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace CrossMgrInterface;

/// <summary>A demo this copy of the application is running.</summary>
/// <param name="Unattended">
/// No intro card, and a manual start is pressed as soon as the reader is
/// connected - for running a demo from a script and checking its log.
/// </param>
public sealed record DemoSession(DemoScenario Scenario, bool Unattended);

/// <summary>
/// Starting a demo: a second copy of the application, run as
/// <c>CrossMgrInterface.exe --demo &lt;id&gt;</c>.
///
/// A separate process rather than a mode of the open window. The main window
/// holds the race database open for its whole life and remembers its setup
/// from a dozen places, and a demo must touch none of it - so the demo copy is
/// pointed at a folder of its own before it opens anything, and its reader
/// listens on a spare port on this computer only. A race being timed in the
/// main window carries on undisturbed.
/// </summary>
public static class DemoLaunch
{
  public const string DemoArgument = "--demo";
  public const string UnattendedArgument = "--unattended";

  /// <summary>Holds every demo's own folder.</summary>
  public static string DemosFolder => Path.Combine(AppPaths.DefaultRoot, "Demo");

  /// <summary>
  /// The demo the command line asks for. Null with no error when it asks for
  /// none; null with an error when it names a demo that does not exist.
  /// </summary>
  public static DemoSession? Parse(IReadOnlyList<string> args, out string? error)
  {
    error = null;

    var index = -1;
    for (var i = 0; i < args.Count; i++)
      if (string.Equals(args[i], DemoArgument, StringComparison.OrdinalIgnoreCase)) index = i;
    if (index < 0) return null;

    var id = index + 1 < args.Count ? args[index + 1] : "";
    var scenario = DemoScenarios.Find(id);
    if (scenario == null)
    {
      error = $"There is no demo called \"{id}\". The demos are: " +
              string.Join(", ", DemoScenarios.All.Select(s => s.Id)) + ".";
      return null;
    }

    var unattended = args.Any(a => string.Equals(a, UnattendedArgument, StringComparison.OrdinalIgnoreCase));
    return new DemoSession(scenario, unattended);
  }

  /// <summary>Opens the demo in a window of its own.</summary>
  public static void Launch(string scenarioId)
  {
    var exe = Environment.ProcessPath
              ?? throw new InvalidOperationException("The application's own path is not known.");
    var start = new ProcessStartInfo(exe) { UseShellExecute = false };

    // Run as "dotnet CrossMgrInterface.dll" - from a build folder, say - the
    // process is dotnet itself, and it needs to be told which program to run.
    if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
      start.ArgumentList.Add(typeof(DemoLaunch).Assembly.Location);

    start.ArgumentList.Add(DemoArgument);
    start.ArgumentList.Add(scenarioId);
    Process.Start(start);
  }

  /// <summary>A port on this computer that nothing is listening on right now.</summary>
  public static int FreeLoopbackPort()
  {
    var probe = new TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    try
    {
      return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
    finally
    {
      probe.Stop();
    }
  }
}

/// <summary>
/// One demo's folder: its database, settings and log. Holds a lock file open
/// for as long as the demo runs, which is how the next demo tells a folder
/// still in use from one it may clear away.
/// </summary>
public sealed class DemoSandbox : IDisposable
{
  private const string LockFileName = "demo.lock";

  private readonly FileStream _lock;

  public string Folder { get; }

  private DemoSandbox(string folder, FileStream lockFile)
  {
    Folder = folder;
    _lock = lockFile;
  }

  /// <summary>
  /// A fresh folder for <paramref name="scenarioId"/> under <paramref name="demosFolder"/>,
  /// after clearing away the folders of demos that have ended. A finished
  /// demo's folder stays until then, so its log can still be read.
  /// </summary>
  public static DemoSandbox Create(string demosFolder, string scenarioId, DateTime now)
  {
    Directory.CreateDirectory(demosFolder);
    ClearFinished(demosFolder, now);

    var name = $"{scenarioId}-{now:yyyyMMdd-HHmmss}";
    var folder = Path.Combine(demosFolder, name);
    for (var n = 2; Directory.Exists(folder); n++)
      folder = Path.Combine(demosFolder, $"{name}-{n}");

    Directory.CreateDirectory(folder);
    var lockFile = new FileStream(Path.Combine(folder, LockFileName), FileMode.CreateNew, FileAccess.Write,
      FileShare.None);
    return new DemoSandbox(folder, lockFile);
  }

  /// <summary>
  /// Deletes every demo folder whose lock is free. The lock file goes first:
  /// deleting a running demo's folder file by file would get as far as its
  /// open database and leave it half gone. A folder made in the last minute is
  /// left alone as well: a demo started at the same moment may not have taken
  /// its lock yet.
  /// </summary>
  private static void ClearFinished(string demosFolder, DateTime now)
  {
    foreach (var folder in Directory.GetDirectories(demosFolder))
    {
      if (now - Directory.GetCreationTime(folder) < TimeSpan.FromMinutes(1)) continue;

      try
      {
        var lockPath = Path.Combine(folder, LockFileName);
        if (File.Exists(lockPath)) File.Delete(lockPath);
        Directory.Delete(folder, recursive: true);
      }
      catch (IOException)
      {
        // Still running.
      }
      catch (UnauthorizedAccessException)
      {
        // Still running, or not ours to delete.
      }
    }
  }

  public void Dispose() => _lock.Dispose();
}
