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
  /// Builds every rider's position at every lap in one pass.
  ///
  /// This replaces calling <see cref="CalculatePositionAtLapFromSnapshot"/> once
  /// per grid cell, which scanned every rider's lap list twice per cell -
  /// roughly riders x laps x riders x laps work to fill one grid.
  ///
  /// The reduction is safe because laps are appended in crossing-time order and
  /// numbered sequentially, so for lap N every rider who has reached it has
  /// completed exactly N laps. The lap-count tiebreak in the per-cell version can
  /// therefore never fire, and ranking collapses to "sort by that lap's crossing
  /// time". LapProgressionEquivalenceTests pins that claim.
  /// </summary>
  /// <returns>lap number -&gt; (tag id -&gt; 1-based position at that lap)</returns>
  public static Dictionary<int, Dictionary<string, int>> BuildLapPositionTable(
    IReadOnlyList<RiderInfo> riderSnapshot, int maxLaps)
  {
    var byLap = new Dictionary<int, List<(string Tag, DateTime Crossing)>>();

    foreach (var rider in riderSnapshot)
    {
      foreach (var lap in rider.Laps)
      {
        if (lap.LapNumber < 1 || lap.LapNumber > maxLaps) continue;

        if (!byLap.TryGetValue(lap.LapNumber, out var list))
          byLap[lap.LapNumber] = list = new List<(string, DateTime)>();

        list.Add((rider.TagID, lap.CrossingTime));
      }
    }

    var table = new Dictionary<int, Dictionary<string, int>>(byLap.Count);
    foreach (var (lapNumber, list) in byLap)
    {
      list.Sort((a, b) => a.Crossing.CompareTo(b.Crossing));

      var positions = new Dictionary<string, int>(list.Count);
      for (var i = 0; i < list.Count; i++)
        positions[list[i].Tag] = i + 1;

      table[lapNumber] = positions;
    }

    return table;
  }

  /// <summary>
  /// Position at a given lap, from a table built by <see cref="BuildLapPositionTable"/>.
  /// Returns 999 for a lap the rider never completed, matching the per-cell version.
  /// </summary>
  public static int PositionAtLap(
    Dictionary<int, Dictionary<string, int>> table, string tagId, int lapNumber)
  {
    if (table.TryGetValue(lapNumber, out var positions) &&
        positions.TryGetValue(tagId, out var position))
    {
      return position;
    }
    return 999;
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
  /// Get sorted riders list from dictionary (for operations that already hold the ridersLock)
  /// </summary>
  public static List<RiderInfo> GetSortedRidersFromDictionary(Dictionary<string, RiderInfo> riders)
  {
    return GetSortedRidersFromSnapshot(riders.Values);
  }

  /// <summary>
  /// Get sorted riders list from snapshot (thread-safe, no locking needed)
  /// </summary>
  public static List<RiderInfo> GetSortedRidersFromSnapshot(IEnumerable<RiderInfo> riderSnapshot)
  {
    // Keys are projected once rather than recomputed inside each comparison.
    return riderSnapshot
        .Select(r => (Rider: r, Dnf: r.IsDNF ? 1 : 0, Laps: r.Laps.Count, Time: r.TotalTime))
        .OrderBy(x => x.Dnf) // Non-DNF riders first (0), DNF riders last (1)
        .ThenByDescending(x => x.Laps)
        .ThenBy(x => x.Time)
        .Select(x => x.Rider)
        .ToList();
  }

  /// <summary>
  /// Projected position for one rider if every suggested split were applied.
  /// Prefer <see cref="CalculateProjectedPositionsWithSplits"/> when more than
  /// one rider is needed: this rebuilds the whole virtual field per call.
  /// </summary>
  public static int CalculateProjectedPositionWithSplits(string riderId, Dictionary<string, RiderInfo> riders)
  {
    return CalculateProjectedPositionsWithSplits(riders).TryGetValue(riderId, out var position)
      ? position
      : 0;
  }

  /// <summary>
  /// Projected positions for the whole field in one pass.
  ///
  /// The single-rider version above was called from inside the riders-grid render
  /// loop, so filling the grid rebuilt and re-sorted a copy of every rider and
  /// every lap once per row.
  /// </summary>
  public static Dictionary<string, int> CalculateProjectedPositionsWithSplits(Dictionary<string, RiderInfo> riders)
  {
    // Create a virtual copy of all riders with suggested splits applied
    var virtualRiders = new List<RiderInfo>();

    foreach (var rider in riders.Values)
    {
      var virtualRider = new RiderInfo
      {
        TagID = rider.TagID,
        RiderNumber = rider.RiderNumber,
        FirstName = rider.FirstName,
        LastName = rider.LastName,
        Category = rider.Category,
        IsDNF = rider.IsDNF,
        LastCrossing = rider.LastCrossing,
        RaceStartTime = rider.RaceStartTime
      };

      // Copy laps and apply suggested splits
      virtualRider.Laps = new List<RiderLap>();

      foreach (var lap in rider.Laps.OrderBy(l => l.LapNumber))
      {
        if (lap.IsSuggestedForSplit && lap.SuggestedSplitCount > 1 && lap.SuggestedSplitLapTime.HasValue && lap.LapTime.HasValue)
        {
          // Replace this lap with multiple split laps
          var baseCrossingTime = lap.CrossingTime - lap.LapTime.Value;

          for (int i = 0; i < lap.SuggestedSplitCount; i++)
          {
            var splitCrossingTime = baseCrossingTime + TimeSpan.FromMilliseconds(lap.SuggestedSplitLapTime.Value.TotalMilliseconds * (i + 1));

            var splitLap = new RiderLap
            {
              TagID = lap.TagID,
              CrossingTime = splitCrossingTime,
              LapNumber = lap.LapNumber + i,
              LapTime = lap.SuggestedSplitLapTime.Value,
              IsSplitLap = true
            };

            virtualRider.Laps.Add(splitLap);
          }
        }
        else
        {
          // Keep original lap
          var virtualLap = new RiderLap
          {
            TagID = lap.TagID,
            CrossingTime = lap.CrossingTime,
            LapNumber = lap.LapNumber,
            LapTime = lap.LapTime,
            IsSplitLap = lap.IsSplitLap
          };

          virtualRider.Laps.Add(virtualLap);
        }
      }

      // Renumber laps to maintain sequential order after splits
      virtualRider.Laps = virtualRider.Laps.OrderBy(l => l.CrossingTime).ToList();
      for (int i = 0; i < virtualRider.Laps.Count; i++)
      {
        virtualRider.Laps[i].LapNumber = i + 1;
      }

      virtualRiders.Add(virtualRider);
    }

    // Rank the virtual field once and return every rider's projected position.
    var sortedVirtualRiders = GetSortedRidersFromSnapshot(virtualRiders);

    var projected = new Dictionary<string, int>(sortedVirtualRiders.Count);
    for (int i = 0; i < sortedVirtualRiders.Count; i++)
      projected[sortedVirtualRiders[i].TagID] = i + 1;

    return projected;
  }

}
