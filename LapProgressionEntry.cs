namespace CrossMgrInterface;

/// <summary>
/// Represents a rider's position at a specific point in the race (when completing a lap)
/// </summary>
public class LapProgressionEntry
{
  public string RiderId { get; set; } = "";
  public int LapNumber { get; set; }
  public int Position { get; set; }
  public TimeSpan RaceTime { get; set; } // Time since race start when this lap was completed
  public DateTime CrossingTime { get; set; }
  public TimeSpan? LapTime { get; set; } // Time for this specific lap
  public bool IsDNF { get; set; } = false;
}
