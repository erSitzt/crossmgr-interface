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
}
