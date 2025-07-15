namespace CrossMgrInterface;

/// <summary>
/// Class to track comprehensive rider information including laps, times, and race status
/// </summary>
public class RiderInfo
{
  public string TagID { get; set; } = "";
  public List<RiderLap> Laps { get; set; } = new List<RiderLap>();
  public DateTime FirstCrossing { get; set; }
  public DateTime LastCrossing { get; set; }
  public DateTime? RaceStartTime { get; set; } // Store race start time for this rider
  public int FinalAllowedLap { get; set; } = int.MaxValue; // Maximum lap number allowed for this rider after race finish
  public bool IsDNF { get; set; } = false; // Did Not Finish - marked when rider times out after race ends
  public DateTime? DNFTime { get; set; } // When the rider was marked as DNF

  public int TotalLaps => Laps.Count;
  public TimeSpan? BestLapTime => Laps.Where(l => l.LapTime.HasValue).Min(l => l.LapTime);
  public TimeSpan? LastLapTime => Laps.LastOrDefault()?.LapTime;

  /// <summary>
  /// Total time should be from race start (if available) to last crossing
  /// </summary>
  public TimeSpan TotalTime
  {
    get
    {
      // Use race start time if available, otherwise fall back to first crossing
      var startTime = RaceStartTime ?? FirstCrossing;
      return LastCrossing - startTime;
    }
  }

  /// <summary>
  /// Predicted next lap time based on recent performance using weighted average
  /// </summary>
  public TimeSpan? PredictedLapTime
  {
    get
    {
      var recentLaps = Laps.Where(l => l.LapTime.HasValue).TakeLast(3).ToList();
      if (recentLaps.Count == 0) return null;

      // Use weighted average of recent laps (more weight to recent laps)
      double totalWeight = 0;
      double weightedSum = 0;

      for (int i = 0; i < recentLaps.Count; i++)
      {
        double weight = i + 1; // More recent laps get higher weight
        weightedSum += recentLaps[i].LapTime!.Value.TotalMilliseconds * weight;
        totalWeight += weight;
      }

      return TimeSpan.FromMilliseconds(weightedSum / totalWeight);
    }
  }

  /// <summary>
  /// Estimated time for next finish line crossing based on predicted lap time
  /// </summary>
  public DateTime? EstimatedNextCrossing
  {
    get
    {
      if (PredictedLapTime == null) return null;
      return LastCrossing + PredictedLapTime.Value;
    }
  }
}
