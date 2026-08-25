namespace CrossMgrInterface;

/// <summary>
/// Where a lap came from. Anything other than <see cref="Read"/> means an
/// operator intervened, which matters when a result is queried afterwards.
/// </summary>
public enum LapSource
{
  /// <summary>Recorded from a transponder read.</summary>
  Read,
  /// <summary>Created by splitting a long lap where a read was missed.</summary>
  Split,
  /// <summary>Entered by hand because no read was recorded at all.</summary>
  ManualInsert,
  /// <summary>Reinstated after being rejected as a short lap.</summary>
  RestoredShortRead,
  /// <summary>Brought across when two transponders were merged onto one rider.</summary>
  Merged
}

/// <summary>
/// Class to track rider lap information
/// </summary>
public class RiderLap
{
  public string TagID { get; set; } = "";
  public DateTime CrossingTime { get; set; }
  public int LapNumber { get; set; }
  public TimeSpan? LapTime { get; set; } // Time for this lap (null for first lap)
  public bool IsSplitLap { get; set; } = false; // Indicates if this lap was created by splitting a missed read
  public bool IsSuggestedForSplit { get; set; } = false; // Indicates if this lap is suggested for splitting
  public int SuggestedSplitCount { get; set; } = 0; // How many laps this should be split into
  public TimeSpan? SuggestedSplitLapTime { get; set; } = null; // Suggested time for each split lap

  /// <summary>How this lap came to exist.</summary>
  public LapSource Source { get; set; } = LapSource.Read;

  /// <summary>
  /// Set when an operator has decided this lap is genuinely long. Stops the
  /// missed-read detector from re-flagging it after every subsequent lap.
  /// </summary>
  public bool SuggestionDismissed { get; set; } = false;

  /// <summary>Non-null when the crossing time was corrected by hand; holds the original.</summary>
  public DateTime? OriginalCrossingTime { get; set; }

  /// <summary>
  /// Tombstone. Deleted laps are flagged rather than removed so a correction can
  /// be undone - the previous behaviour deleted rows outright, leaving nothing
  /// to undo back to.
  /// </summary>
  public bool IsDeleted { get; set; } = false;

  /// <summary>Free-text note shown in the correction dialog and the audit log.</summary>
  public string? CorrectionNote { get; set; }

  /// <summary>True if an operator created or altered this lap.</summary>
  public bool WasCorrected =>
    Source != LapSource.Read || OriginalCrossingTime.HasValue;

  public RiderLap Clone() => new()
  {
    TagID = TagID,
    CrossingTime = CrossingTime,
    LapNumber = LapNumber,
    LapTime = LapTime,
    IsSplitLap = IsSplitLap,
    IsSuggestedForSplit = IsSuggestedForSplit,
    SuggestedSplitCount = SuggestedSplitCount,
    SuggestedSplitLapTime = SuggestedSplitLapTime,
    Source = Source,
    SuggestionDismissed = SuggestionDismissed,
    OriginalCrossingTime = OriginalCrossingTime,
    IsDeleted = IsDeleted,
    CorrectionNote = CorrectionNote
  };
}
