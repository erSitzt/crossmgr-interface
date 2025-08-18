namespace CrossMgrInterface;

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
}
