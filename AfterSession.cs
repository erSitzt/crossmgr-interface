namespace CrossMgrInterface;

/// <summary>
/// What happens by itself when a session finishes, and when one starts.
///
/// Kept twice: once in the settings, for sessions set up by hand, and once in
/// each day plan, which turns everything on for its day - a club that plans
/// the day wants the day to run itself, and one that sets up a practice by
/// hand on a Tuesday evening may want none of it.
/// </summary>
public sealed class AfterSession
{
  /// <summary>Put the results on the club's website, and again after a correction. Waits while offline.</summary>
  public bool Publish { get; set; }

  /// <summary>Save the sheets into a folder for the day, and again after a correction.</summary>
  public bool SaveSheets { get; set; }

  /// <summary>Where the day folders go. Null for Documents\CrossMgr Results.</summary>
  public string? Folder { get; set; }

  /// <summary>Also as PDF, through Windows' own PDF printer.</summary>
  public bool Pdf { get; set; } = true;

  /// <summary>Switch live timing on when a session's clock starts. It switches itself off at the finish.</summary>
  public bool LiveTiming { get; set; }

  public static AfterSession ForNewPlan() => new() { Publish = true, SaveSheets = true, Pdf = true, LiveTiming = true };

  public AfterSession Copy() => new()
  {
    Publish = Publish,
    SaveSheets = SaveSheets,
    Folder = Folder,
    Pdf = Pdf,
    LiveTiming = LiveTiming
  };

  public bool AnyOn => Publish || SaveSheets || LiveTiming;
}
