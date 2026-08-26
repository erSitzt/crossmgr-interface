using System.Text.Json;

namespace CrossMgrInterface;

/// <summary>
/// The handful of preferences that should survive a restart.
///
/// A small JSON file rather than the Visual Studio settings designer: the
/// project has no settings infrastructure and adding it would be more churn than
/// this is worth.
/// </summary>
public sealed class AppSettings
{
  /// <summary>Show the technical tabs. Off by default - volunteers get the calm view.</summary>
  public bool AdvancedMode { get; set; }

  /// <summary>TCP port the transponder reader connects to.</summary>
  public int ReaderPort { get; set; } = 53135;

  /// <summary>Show the raw transponder column in the riders grid.</summary>
  public bool ShowTransponderIds { get; set; }

  /// <summary>Log the raw bytes of every socket read. Expensive; for diagnosis only.</summary>
  public bool VerboseProtocolLogging { get; set; }

  /// <summary>
  /// Whether the reader connection was open when the application last closed.
  ///
  /// A timing laptop that restarts - after a crash, or because someone closed the
  /// window - should come back listening rather than silently dropping every
  /// transponder read until an operator notices and reconnects by hand.
  /// </summary>
  public bool ReaderConnected { get; set; }

  /// <summary>
  /// The rider list last imported, reloaded on startup for the same reason: a
  /// restart mid-meeting should not leave every rider showing as an
  /// unidentified transponder.
  /// </summary>
  public string? LastRiderListPath { get; set; }

  // Race setup. A club runs the same format all day, so re-entering it after
  // every restart is pure friction - and silently falling back to a 20-minute
  // default is worse than friction.
  public int RaceDurationMinutes { get; set; } = 20;
  public int AdditionalLaps { get; set; } = 1;
  public bool ManualStart { get; set; }
  public int DnfTimeoutMinutes { get; set; } = 2;

  /// <summary>
  /// Practice, qualifying or a race. Part of race setup for the same reason as
  /// the rest of this block: a club runs a block of the same format, so the
  /// wizard should come back offering what was chosen last time.
  /// </summary>
  public SessionType SessionType { get; set; }

  /// <summary>Which of position, start number and name are written beside each rider dot.</summary>
  public MapLabelParts TrackLabelParts { get; set; } = MapLabelParts.Position | MapLabelParts.Number;

  /// <summary>Basemap last chosen for the track map - street, satellite, and so on.</summary>
  public string? TileProviderId { get; set; }

  /// <summary>
  /// The circuit last shown on the track map. A club runs the same venue all day,
  /// so the map should come back with the right loop already on it.
  /// </summary>
  public string? LastTrackId { get; set; }

  private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

  public static AppSettings Load()
  {
    try
    {
      var path = AppPaths.SettingsFile;
      if (!File.Exists(path)) return new AppSettings();

      return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
    }
    catch (Exception)
    {
      // A corrupt or unreadable settings file must never stop a race starting.
      return new AppSettings();
    }
  }

  public void Save()
  {
    try
    {
      File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, Options));
    }
    catch (Exception)
    {
      // Losing a preference is not worth interrupting the operator over.
    }
  }
}
