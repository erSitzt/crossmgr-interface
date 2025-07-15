namespace CrossMgrInterface;

/// <summary>
/// Helper class to track clickable areas in the lap chart
/// </summary>
public class LapChartElement
{
  public Rectangle Bounds { get; set; }
  public string RiderId { get; set; } = "";
  public int LapNumber { get; set; }
  public TimeSpan? LapTime { get; set; }
  public bool IsRider { get; set; } // true for rider label, false for individual lap
}
