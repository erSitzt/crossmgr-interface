namespace CrossMgrInterface;

/// <summary>
/// Represents a snapshot of rider positions at a specific point in time during the race
/// </summary>
public class PositionSnapshot
{
  /// <summary>
  /// The time when this position snapshot was taken
  /// </summary>
  public DateTime Timestamp { get; set; }

  /// <summary>
  /// The elapsed time since the race started when this snapshot was taken
  /// </summary>
  public TimeSpan RaceElapsedTime { get; set; }

  /// <summary>
  /// Dictionary mapping rider tag IDs to their position (1-based) at this time
  /// </summary>
  public Dictionary<string, int> Positions { get; set; } = new();

  /// <summary>
  /// Dictionary mapping rider tag IDs to their lap count at this time
  /// </summary>
  public Dictionary<string, int> LapCounts { get; set; } = new();

  /// <summary>
  /// Dictionary mapping rider tag IDs to their total time at this point
  /// </summary>
  public Dictionary<string, TimeSpan> TotalTimes { get; set; } = new();

  /// <summary>
  /// Creates a new position snapshot
  /// </summary>
  /// <param name="timestamp">The time when this snapshot was taken</param>
  /// <param name="raceStartTime">The race start time for calculating elapsed time</param>
  public PositionSnapshot(DateTime timestamp, DateTime raceStartTime)
  {
    Timestamp = timestamp;
    RaceElapsedTime = timestamp - raceStartTime;
  }

  /// <summary>
  /// Gets a formatted string representation of the race elapsed time
  /// </summary>
  public string FormattedElapsedTime => RaceElapsedTime.ToString(@"mm\:ss");

  /// <summary>
  /// Gets the position of a specific rider, or null if the rider wasn't ranked at this time
  /// </summary>
  /// <param name="riderId">The rider's tag ID</param>
  /// <returns>The rider's position (1-based) or null if not found</returns>
  public int? GetPosition(string riderId)
  {
    return Positions.TryGetValue(riderId, out var position) ? position : null;
  }

  /// <summary>
  /// Gets the lap count of a specific rider at this time
  /// </summary>
  /// <param name="riderId">The rider's tag ID</param>
  /// <returns>The rider's lap count or 0 if not found</returns>
  public int GetLapCount(string riderId)
  {
    return LapCounts.TryGetValue(riderId, out var lapCount) ? lapCount : 0;
  }
}
