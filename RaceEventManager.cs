namespace CrossMgrInterface;

/// <summary>
/// Manages race events like position changes, passing, and lapping events
/// </summary>
public class RaceEventManager
{
  private readonly RaceDataService _raceDb;
  private readonly Action<string> _addRaceEventCallback;
  private readonly Dictionary<string, int> _lastKnownPositions = new();
  private readonly Dictionary<string, int> _lastKnownLapCounts = new();
  private DateTime _lastPositionCheck = DateTime.MinValue;

  public RaceEventManager(RaceDataService raceDb, Action<string> addRaceEventCallback)
  {
    _raceDb = raceDb;
    _addRaceEventCallback = addRaceEventCallback;
  }

  /// <summary>
  /// Check for position changes and lapping events after a rider crossing
  /// </summary>
  public void CheckForPositionChangesAndLapping(string crossingRiderTagID, Dictionary<string, RiderInfo> riders, bool raceStarted, bool raceFinished)
  {
    // Don't check for position changes if race hasn't started or is finished
    if (!raceStarted || raceFinished)
      return;

    // Get current standings sorted by position (DNF riders last)
    var currentStandings = riders.Values
        .OrderBy(r => r.IsDNF ? 1 : 0) // Non-DNF riders first (0), DNF riders last (1)
        .ThenByDescending(r => r.TotalLaps)
        .ThenBy(r => r.TotalTime)
        .ToList();

    if (currentStandings.Count < 2)
      return; // Need at least 2 riders for position changes

    // Check for passing and lapping events (only if we have previous data)
    if (_lastKnownPositions.Count > 0 && _lastKnownLapCounts.Count > 0)
    {
      CheckForPassingAndLappingEvents(currentStandings, crossingRiderTagID, riders);
    }

    // Check for position changes (only if enough time has passed to avoid spam)
    if (_lastKnownPositions.Count > 0 &&
        (DateTime.Now - _lastPositionCheck).TotalSeconds >= 5)
    {
      var crossingRiderPosition = currentStandings.FindIndex(r => r.TagID == crossingRiderTagID) + 1;
      CheckForPositionChanges(currentStandings, crossingRiderTagID, crossingRiderPosition);
    }

    // Store current standings for future comparisons
    StoreCurrentStandings(currentStandings);
  }

  /// <summary>
  /// Check for passing and lapping events using lap difference analysis
  /// </summary>
  private void CheckForPassingAndLappingEvents(List<RiderInfo> currentStandings, string crossingRiderTagID, Dictionary<string, RiderInfo> riders)
  {
    // For each other rider, check if there's a passing or lapping event involving the crossing rider
    foreach (var otherRider in currentStandings)
    {
      if (otherRider.TagID == crossingRiderTagID) continue;

      var crossingRider = riders[crossingRiderTagID];
      var otherRiderInfo = riders[otherRider.TagID];

      // Determine if riders are on the same lap
      bool sameCurrentLap = crossingRider.TotalLaps == otherRiderInfo.TotalLaps;

      if (sameCurrentLap)
      {
        // Same lap = check for passing events only
        CheckPassingEvent(crossingRiderTagID, otherRider.TagID, currentStandings);
      }
      else
      {
        // Different laps = check for lapping events only
        CheckLappingEvent(crossingRiderTagID, otherRider.TagID, riders);
      }
    }
  }

  /// <summary>
  /// Check for a lapping event between two specific riders
  /// </summary>
  private void CheckLappingEvent(string crossingRiderTagID, string otherRiderTagID, Dictionary<string, RiderInfo> riders)
  {
    var crossingRider = riders[crossingRiderTagID];
    var otherRider = riders[otherRiderTagID];

    // Calculate current lap difference (crossing rider - other rider)
    int currentLapDiff = crossingRider.TotalLaps - otherRider.TotalLaps;

    // Don't check lapping if riders are on the same lap - that's a passing event, not lapping
    if (currentLapDiff == 0)
    {
      // Store the lap difference and return - passing logic will handle same-lap events
      StoreLapDifference(crossingRiderTagID, otherRiderTagID, currentLapDiff);
      return;
    }

    // Get previous lap difference
    int previousLapDiff = GetPreviousLapDifference(crossingRiderTagID, otherRiderTagID, currentLapDiff);

    // Lapping occurs when crossing rider gains a lap advantage (goes from same/behind to ahead)
    // The crossing rider must have MORE laps than the other rider to lap them
    if (currentLapDiff >= 1 && previousLapDiff < currentLapDiff)
    {
      // Lapping event detected - crossing rider has gained a lap advantage
      if (currentLapDiff == 1)
      {
        _addRaceEventCallback($"🔄 {crossingRiderTagID} has LAPPED {otherRiderTagID}!");
      }
      else if (currentLapDiff > 1)
      {
        _addRaceEventCallback($"🔄 {crossingRiderTagID} has LAPPED {otherRiderTagID} (now {currentLapDiff} laps ahead)!");
      }
    }

    // Store the current lap difference for next comparison
    StoreLapDifference(crossingRiderTagID, otherRiderTagID, currentLapDiff);
  }

  /// <summary>
  /// Check for a passing event between two specific riders (same lap only)
  /// </summary>
  private void CheckPassingEvent(string crossingRiderTagID, string otherRiderTagID, List<RiderInfo> currentStandings)
  {
    // Get current positions
    int currentPosCrossing = currentStandings.FindIndex(r => r.TagID == crossingRiderTagID) + 1;
    int currentPosOther = currentStandings.FindIndex(r => r.TagID == otherRiderTagID) + 1;

    // Get previous positions (if we have history)
    if (!_lastKnownPositions.ContainsKey(crossingRiderTagID) || !_lastKnownPositions.ContainsKey(otherRiderTagID))
      return; // No previous position data to compare

    int previousPosCrossing = _lastKnownPositions[crossingRiderTagID];
    int previousPosOther = _lastKnownPositions[otherRiderTagID];

    // Check if crossing rider passed the other rider (was behind but now ahead)
    if (previousPosCrossing > previousPosOther && currentPosCrossing < currentPosOther)
    {
      _addRaceEventCallback($"⚡ {crossingRiderTagID} PASSES {otherRiderTagID} for position {currentPosCrossing}!");
    }
  }

  /// <summary>
  /// Check for position changes since last check
  /// </summary>
  private void CheckForPositionChanges(List<RiderInfo> currentStandings, string crossingRiderTagID, int currentPosition)
  {
    // Check if the crossing rider's position changed significantly
    if (_lastKnownPositions.ContainsKey(crossingRiderTagID))
    {
      int previousPosition = _lastKnownPositions[crossingRiderTagID];
      int positionChange = previousPosition - currentPosition; // Positive = moved up, Negative = moved down

      if (Math.Abs(positionChange) >= 1) // Position changed by at least 1 place
      {
        if (positionChange > 0)
        {
          // Moved up in positions
          if (currentPosition == 1)
          {
            _addRaceEventCallback($"🥇 NEW LEADER! {crossingRiderTagID} takes the lead! (was P{previousPosition})");
          }
          else if (currentPosition <= 3 && previousPosition > 3)
          {
            _addRaceEventCallback($"🏆 {crossingRiderTagID} moves into podium position {currentPosition}! (was P{previousPosition})");
          }
          else if (positionChange >= 3)
          {
            _addRaceEventCallback($"⬆️ {crossingRiderTagID} surges up {positionChange} positions to P{currentPosition}! (was P{previousPosition})");
          }
          else
          {
            _addRaceEventCallback($"⬆️ {crossingRiderTagID} moves up to P{currentPosition} (was P{previousPosition})");
          }
        }
        else
        {
          // Moved down in positions
          if (previousPosition == 1)
          {
            var newLeader = currentStandings.FirstOrDefault();
            _addRaceEventCallback($"🔄 LEADER CHANGE! {newLeader?.TagID} takes over from {crossingRiderTagID} who drops to P{currentPosition}");
          }
          else if (Math.Abs(positionChange) >= 3)
          {
            _addRaceEventCallback($"⬇️ {crossingRiderTagID} drops {Math.Abs(positionChange)} positions to P{currentPosition} (was P{previousPosition})");
          }
        }
      }
    }

    // Check for other significant position battles in top 5
    CheckForTopPositionBattles(currentStandings);
  }

  /// <summary>
  /// Check for position battles in the top positions
  /// </summary>
  private void CheckForTopPositionBattles(List<RiderInfo> currentStandings)
  {
    // Look for close battles in top 5 positions
    for (int i = 0; i < Math.Min(5, currentStandings.Count - 1); i++)
    {
      var rider1 = currentStandings[i];
      var rider2 = currentStandings[i + 1];

      // Check if riders are on the same lap
      if (rider1.TotalLaps == rider2.TotalLaps)
      {
        var timeDifference = rider2.TotalTime - rider1.TotalTime;

        // If the gap is very close (less than 5 seconds), announce close battle
        if (timeDifference.TotalSeconds < 5 && timeDifference.TotalSeconds > 0)
        {
          // Only announce occasionally to avoid spam
          if (ShouldAnnounceBattle(rider1.TagID, rider2.TagID))
          {
            _addRaceEventCallback($"🔥 CLOSE BATTLE! P{i + 1} {rider1.TagID} leads P{i + 2} {rider2.TagID} by only {timeDifference.TotalSeconds:F1} seconds!");
          }
        }
      }
    }
  }

  /// <summary>
  /// Check if a battle between two riders should be announced (to avoid spam)
  /// </summary>
  private bool ShouldAnnounceBattle(string rider1, string rider2)
  {
    // For now, limit battle announcements to avoid spam
    // Could implement more sophisticated logic later
    return DateTime.Now.Second % 30 == 0; // Announce battles every 30 seconds max
  }

  /// <summary>
  /// Store current standings to track position changes over time
  /// </summary>
  private void StoreCurrentStandings(List<RiderInfo> currentStandings)
  {
    // Update position tracking
    _lastKnownPositions.Clear();
    _lastKnownLapCounts.Clear();

    for (int i = 0; i < currentStandings.Count; i++)
    {
      var rider = currentStandings[i];
      _lastKnownPositions[rider.TagID] = i + 1; // 1-based position
      _lastKnownLapCounts[rider.TagID] = rider.TotalLaps;
    }
    _lastPositionCheck = DateTime.Now;
  }

  /// <summary>
  /// Get the previous lap difference between two riders
  /// </summary>
  private int GetPreviousLapDifference(string riderA, string riderB, int defaultValue)
  {
    return _raceDb.GetPreviousLapDifference(riderA, riderB, defaultValue);
  }

  /// <summary>
  /// Store the lap difference between two riders
  /// </summary>
  private void StoreLapDifference(string riderA, string riderB, int lapDifference)
  {
    _raceDb.StoreLapDifference(riderA, riderB, lapDifference);
  }
}
