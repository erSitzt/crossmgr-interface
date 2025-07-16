namespace CrossMgrInterface;

/// <summary>
/// Utility class for calculating rider positions at various points in time during the race
/// </summary>
public static class PositionCalculator
{
  /// <summary>
  /// Calculate what position a rider was in when they completed a specific lap
  /// </summary>
  public static int CalculatePositionAtTime(string riderId, DateTime lapCompletionTime, int riderLapCount, Dictionary<string, RiderInfo> riders)
  {
    // Count how many riders had completed more laps at this time, or same laps but faster total time
    int ridersAhead = 0;

    foreach (var otherRider in riders.Values)
    {
      if (otherRider.TagID == riderId) continue;

      // Count laps completed by this other rider at the time of the target rider's lap completion
      int otherRiderLapsAtTime = 0;
      DateTime? otherRiderTimeAtSameLaps = null;

      foreach (var otherLap in otherRider.Laps)
      {
        if (otherLap.CrossingTime <= lapCompletionTime)
        {
          otherRiderLapsAtTime++;
          if (otherRiderLapsAtTime == riderLapCount)
          {
            otherRiderTimeAtSameLaps = otherLap.CrossingTime;
          }
        }
        else
        {
          break; // No need to check further laps
        }
      }

      // Determine if this other rider was ahead
      if (otherRiderLapsAtTime > riderLapCount)
      {
        // Other rider had more laps completed - they were ahead
        ridersAhead++;
      }
      else if (otherRiderLapsAtTime == riderLapCount && otherRiderTimeAtSameLaps.HasValue)
      {
        // Same number of laps - compare completion times
        if (otherRiderTimeAtSameLaps.Value < lapCompletionTime)
        {
          // Other rider completed the same lap faster - they were ahead
          ridersAhead++;
        }
      }
    }

    return ridersAhead + 1; // Position is number of riders ahead + 1
  }

  /// <summary>
  /// Calculate position at lap using snapshot data (no locking needed)
  /// </summary>
  public static int CalculatePositionAtLapFromSnapshot(RiderInfo targetRider, int lapNumber, List<RiderInfo> riderSnapshot)
  {
    var riderLap = targetRider.Laps.FirstOrDefault(l => l.LapNumber == lapNumber);
    if (riderLap == null) return 999; // Should not happen

    var ridersAtThisLap = new List<(string Id, DateTime CompletionTime, int TotalLapsAtTime)>();

    foreach (var otherRider in riderSnapshot)
    {
      var otherRiderLap = otherRider.Laps.FirstOrDefault(l => l.LapNumber == lapNumber);

      if (otherRiderLap != null)
      {
        // Count how many laps this rider had when they completed this lap
        var lapsAtTime = otherRider.Laps.Count(l => l.CrossingTime <= otherRiderLap.CrossingTime);
        ridersAtThisLap.Add((otherRider.TagID, otherRiderLap.CrossingTime, lapsAtTime));
      }
    }

    // Sort by laps completed (desc) then by completion time (asc)
    ridersAtThisLap.Sort((a, b) =>
    {
      var lapComparison = b.TotalLapsAtTime.CompareTo(a.TotalLapsAtTime);
      if (lapComparison != 0) return lapComparison;
      return a.CompletionTime.CompareTo(b.CompletionTime);
    });

    // Find position
    for (int i = 0; i < ridersAtThisLap.Count; i++)
    {
      if (ridersAtThisLap[i].Id == targetRider.TagID)
      {
        return i + 1; // 1-based position
      }
    }

    return 999; // Fallback
  }

  /// <summary>
  /// Calculate position for a specific lap using a snapshot of riders
  /// </summary>
  public static int CalculatePositionAtLap(string riderId, int lapNumber, List<RiderInfo> riderSnapshot)
  {
    // Find all riders who had completed at least 'lapNumber' laps
    // and determine this rider's position among them based on when they completed that lap

    var targetRider = riderSnapshot.FirstOrDefault(r => r.TagID == riderId);
    if (targetRider == null) return 999;

    var riderLap = targetRider.Laps.FirstOrDefault(l => l.LapNumber == lapNumber);
    if (riderLap == null) return 999; // Should not happen

    var ridersAtThisLap = new List<(string Id, DateTime CompletionTime, int TotalLapsAtTime)>();

    foreach (var otherRider in riderSnapshot)
    {
      var otherRiderLap = otherRider.Laps.FirstOrDefault(l => l.LapNumber == lapNumber);

      if (otherRiderLap != null)
      {
        // Count how many laps this rider had when they completed this lap
        var lapsAtTime = otherRider.Laps.Count(l => l.CrossingTime <= otherRiderLap.CrossingTime);
        ridersAtThisLap.Add((otherRider.TagID, otherRiderLap.CrossingTime, lapsAtTime));
      }
    }

    // Sort by laps completed (desc) then by completion time (asc)
    ridersAtThisLap.Sort((a, b) =>
    {
      var lapComparison = b.TotalLapsAtTime.CompareTo(a.TotalLapsAtTime);
      if (lapComparison != 0) return lapComparison;
      return a.CompletionTime.CompareTo(b.CompletionTime);
    });

    // Find position
    for (int i = 0; i < ridersAtThisLap.Count; i++)
    {
      if (ridersAtThisLap[i].Id == riderId)
      {
        return i + 1; // 1-based position
      }
    }

    return 999; // Fallback
  }

  /// <summary>
  /// Calculate current position based on current standings
  /// </summary>
  public static int CalculateCurrentPosition(string riderId, Dictionary<string, RiderInfo> riders)
  {
    var sortedRiders = riders.Values
        .OrderBy(r => r.IsDNF ? 1 : 0) // Non-DNF riders first
        .ThenByDescending(r => r.TotalLaps)
        .ThenBy(r => r.TotalTime)
        .ToList();

    for (int i = 0; i < sortedRiders.Count; i++)
    {
      if (sortedRiders[i].TagID == riderId)
      {
        return i + 1; // 1-based position
      }
    }

    return 999; // Not found
  }

  /// <summary>
  /// Get sorted riders list for current standings
  /// </summary>
  public static List<RiderInfo> GetSortedRiders(Dictionary<string, RiderInfo> riders)
  {
    return riders.Values
        .OrderBy(r => r.IsDNF ? 1 : 0) // Non-DNF riders first (0), DNF riders last (1)
        .ThenByDescending(r => r.TotalLaps)
        .ThenBy(r => r.TotalTime)
        .ToList();
  }
}
