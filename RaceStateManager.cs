namespace CrossMgrInterface;

/// <summary>
/// Manages the overall race state and transitions
/// </summary>
public class RaceStateManager
{
  private DateTime? _raceStartTime = null;
  private DateTime? _raceEndTime = null;
  private bool _raceStarted = false;
  private bool _raceFinished = false;
  private bool _raceTimeExpired = false;
  private bool _waitingForLeaderFinish = false;
  private bool _waitingForFinalLaps = false;
  private DateTime? _finalLapsStartTime = null;
  private string? _leaderAtTimeExpiry = null;
  private int _leaderLapsAtTimeExpiry = 0;
  private TimeSpan _raceDuration = TimeSpan.FromMinutes(20);
  private bool _fiveMinuteWarningShown = false;
  private bool _oneMinuteWarningShown = false;
  private int _additionalLapsAfterTimeExpiry = 1;
  private int _dnfTimeoutMinutes = 2;

  // Properties
  public DateTime? RaceStartTime => _raceStartTime;
  public DateTime? RaceEndTime => _raceEndTime;
  public bool RaceStarted => _raceStarted;
  public bool RaceFinished => _raceFinished;
  public bool RaceTimeExpired => _raceTimeExpired;
  public bool WaitingForLeaderFinish => _waitingForLeaderFinish;
  public bool WaitingForFinalLaps => _waitingForFinalLaps;
  public string? LeaderAtTimeExpiry => _leaderAtTimeExpiry;
  public int TargetLapsToFinishRace { get; private set; }
  public TimeSpan RaceDuration => _raceDuration;
  public int AdditionalLapsAfterTimeExpiry => _additionalLapsAfterTimeExpiry;
  public int DnfTimeoutMinutes => _dnfTimeoutMinutes;
  public DateTime? FinalLapsStartTime => _finalLapsStartTime;
  public int LeaderLapsAtTimeExpiry => _leaderLapsAtTimeExpiry;

  public Dictionary<string, int> LastKnownPositions { get; private set; } = new Dictionary<string, int>();
  public Dictionary<string, int> LastKnownLapCounts { get; private set; } = new Dictionary<string, int>();
  public DateTime LastPositionCheck { get; set; } = DateTime.Now;
  public List<LapProgressionEntry> LapProgressionHistory { get; private set; } = new List<LapProgressionEntry>();
  public bool LapProgressionNeedsUpdate { get; set; } = false;

  // Events
  public event Action<string>? MessageAdded;
  public event Action<string>? RaceEventAdded;

  /// <summary>
  /// Start a new race
  /// </summary>
  public void StartRace(DateTime startTime, TimeSpan duration)
  {
    _raceStartTime = startTime;
    _raceEndTime = startTime + duration;
    _raceDuration = duration;
    _raceStarted = true;
    _raceFinished = false;
    _raceTimeExpired = false;
    _waitingForLeaderFinish = false;
    _waitingForFinalLaps = false;
    _fiveMinuteWarningShown = false;
    _oneMinuteWarningShown = false;

    MessageAdded?.Invoke($"🏁 Race started manually at {startTime:HH:mm:ss}");
  }

  /// <summary>
  /// Start race automatically on first tag read
  /// </summary>
  public void StartRaceAutomatically(DateTime firstTagTime, TimeSpan duration)
  {
    _raceStartTime = firstTagTime;
    _raceEndTime = firstTagTime + duration;
    _raceDuration = duration;
    _raceStarted = true;
    _raceFinished = false;
    _raceTimeExpired = false;
    _waitingForLeaderFinish = false;
    _waitingForFinalLaps = false;
    _fiveMinuteWarningShown = false;
    _oneMinuteWarningShown = false;

    MessageAdded?.Invoke($"🏁 Race started automatically at {firstTagTime:HH:mm:ss}");
  }

  /// <summary>
  /// Check if race time has expired and handle accordingly
  /// </summary>
  public void CheckRaceTimeExpiry(Dictionary<string, RiderInfo> riders)
  {
    if (!_raceStarted || _raceFinished || !_raceEndTime.HasValue)
      return;

    var timeRemaining = GetTimeRemaining();

    // Show warnings
    if (timeRemaining.TotalMinutes <= 5 && timeRemaining.TotalMinutes > 1 && !_fiveMinuteWarningShown)
    {
      MessageAdded?.Invoke("⏰ WARNING: 5 minutes remaining in race!");
      _fiveMinuteWarningShown = true;
    }
    else if (timeRemaining.TotalMinutes <= 1 && !_oneMinuteWarningShown)
    {
      MessageAdded?.Invoke("⏰ WARNING: 1 minute remaining in race!");
      _oneMinuteWarningShown = true;
    }

    // Check if time has expired
    if (timeRemaining <= TimeSpan.Zero && !_raceTimeExpired)
    {
      HandleRaceTimeExpiry(riders);
    }
  }

  /// <summary>
  /// Handle when a rider might finish the race (reached target laps)
  /// </summary>
  public void CheckRaceFinish(string riderId, Dictionary<string, RiderInfo> riders)
  {
    if (!_raceStarted || _raceFinished)
      return;

    var rider = riders[riderId];

    // If we're in waiting for leader finish mode, check if this rider reached the target
    if (_waitingForLeaderFinish && rider.TotalLaps >= TargetLapsToFinishRace)
    {
      FinishRace(riders);
    }
  }

  /// <summary>
  /// Check if all final laps are completed
  /// </summary>
  public void CheckIfAllFinalLapsCompleted(Dictionary<string, RiderInfo> riders)
  {
    if (!_waitingForFinalLaps || !_finalLapsStartTime.HasValue)
      return;

    // Check if all riders have either completed their final allowed lap or have timed out
    bool allRidersFinished = true;

    foreach (var rider in riders.Values)
    {
      // Skip riders already marked as DNF
      if (rider.IsDNF)
        continue;

      // If rider hasn't reached their final allowed lap yet
      if (rider.TotalLaps < rider.FinalAllowedLap)
      {
        // Check if too much time has passed since leader finished (timeout)
        var timeSinceLeaderFinished = DateTime.Now - _finalLapsStartTime.Value;

        // If less than timeout period since leader finished, rider might still finish their lap
        if (timeSinceLeaderFinished.TotalMinutes < _dnfTimeoutMinutes)
        {
          allRidersFinished = false;
          // Don't break - continue checking other riders for DNF timeout
        }
        else
        {
          // Rider has timed out - mark as DNF
          rider.IsDNF = true;
          rider.DNFTime = DateTime.Now;
          MessageAdded?.Invoke($"🚫 Rider {rider.TagID} marked as DNF (Did Not Finish) - {timeSinceLeaderFinished.TotalMinutes:F1} min since leader finished, failed to complete final lap");
          RaceEventAdded?.Invoke($"DNF: {rider.TagID} - Timeout after {timeSinceLeaderFinished.TotalMinutes:F1} minutes");
        }
      }
    }

    if (allRidersFinished)
    {
      CompletelyFinishRace(riders);
    }
  }

  /// <summary>
  /// Set additional laps after time expiry
  /// </summary>
  public void SetAdditionalLaps(int additionalLaps, Dictionary<string, RiderInfo> riders)
  {
    _additionalLapsAfterTimeExpiry = additionalLaps;
    MessageAdded?.Invoke($"⚙️ Additional laps after time expiry set to: {additionalLaps}");

    // If race has already finished in time mode, update the target
    if (_raceTimeExpired && TargetLapsToFinishRace > 0)
    {
      // Recalculate target laps based on new setting (exclude DNF riders from leader calculation)
      var currentLeader = riders.Values
          .Where(r => !r.IsDNF)
          .OrderByDescending(r => r.TotalLaps)
          .ThenBy(r => r.TotalTime)
          .FirstOrDefault();

      if (currentLeader != null && _leaderLapsAtTimeExpiry > 0)
      {
        // Calculate target: leader's current lap (in progress when time expired) + additional laps
        var leaderCurrentLapWhenTimeExpired = _leaderLapsAtTimeExpiry + 1;
        TargetLapsToFinishRace = leaderCurrentLapWhenTimeExpired + additionalLaps;
        var lapsText = additionalLaps == 1 ? "lap" : "laps";
        MessageAdded?.Invoke($"🏁 Updated race finish target to {TargetLapsToFinishRace} laps (leader was on lap {leaderCurrentLapWhenTimeExpired} when time expired + {additionalLaps} additional {lapsText})");
      }
    }
  }

  /// <summary>
  /// Get the time remaining in the race
  /// </summary>
  public TimeSpan GetTimeRemaining()
  {
    if (!_raceEndTime.HasValue)
      return TimeSpan.Zero;

    return _raceEndTime.Value - DateTime.Now;
  }

  /// <summary>
  /// Get race status text
  /// </summary>
  public (string text, Color color) GetRaceStatus(Dictionary<string, RiderInfo> riders)
  {
    if (_raceFinished)
    {
      return ("Race: FINISHED", Color.Blue);
    }
    else if (_waitingForFinalLaps)
    {
      // Count how many riders are still eligible to complete their final lap
      var ridersStillActive = riders.Values.Count(r => r.TotalLaps < r.FinalAllowedLap &&
                                                     (r.PredictedLapTime.HasValue ||
                                                      (DateTime.Now - r.LastCrossing).TotalMinutes < 2));

      return ($"Race: LEADER FINISHED - {ridersStillActive} riders completing final lap", Color.DarkBlue);
    }
    else if (_raceTimeExpired)
    {
      return ("Race: TIME EXPIRED - Waiting for ongoing lap to complete", Color.Orange);
    }
    else if (_waitingForLeaderFinish)
    {
      // AMA Motocross regulations: Show status for the leader who was leading when time expired
      if (!string.IsNullOrEmpty(_leaderAtTimeExpiry) && riders.ContainsKey(_leaderAtTimeExpiry))
      {
        var leaderRider = riders[_leaderAtTimeExpiry];
        var remainingLaps = TargetLapsToFinishRace - leaderRider.TotalLaps;
        var lapsText = remainingLaps == 1 ? "lap" : "laps";
        return ($"Race: LEADER {_leaderAtTimeExpiry} - {remainingLaps} {lapsText} to go (target: {TargetLapsToFinishRace})", Color.Purple);
      }
      else
      {
        return ($"Race: Waiting for Leader {_leaderAtTimeExpiry} to complete additional laps", Color.Purple);
      }
    }
    else if (_raceStarted)
    {
      return ("Race: Started", Color.Green);
    }
    else
    {
      return ("Race: Waiting for First Tag", Color.DarkRed);
    }
  }

  private void HandleRaceTimeExpiry(Dictionary<string, RiderInfo> riders)
  {
    _raceTimeExpired = true;

    // Find the current leader (exclude DNF riders)
    var currentLeader = riders.Values
        .Where(r => !r.IsDNF)
        .OrderByDescending(r => r.TotalLaps)
        .ThenBy(r => r.TotalTime)
        .FirstOrDefault();

    if (currentLeader != null)
    {
      _leaderAtTimeExpiry = currentLeader.TagID;
      _leaderLapsAtTimeExpiry = currentLeader.TotalLaps;

      // Calculate target laps: current lap + 1 (finish current lap) + additional laps
      TargetLapsToFinishRace = _leaderLapsAtTimeExpiry + 1 + _additionalLapsAfterTimeExpiry;

      MessageAdded?.Invoke($"⏰ TIME EXPIRED! Leader {_leaderAtTimeExpiry} has {_leaderLapsAtTimeExpiry} laps completed.");
      MessageAdded?.Invoke($"🏁 Race will finish when leader completes {TargetLapsToFinishRace} laps total.");

      _waitingForLeaderFinish = true;
    }
    else
    {
      MessageAdded?.Invoke("⏰ TIME EXPIRED! No active riders found - finishing race immediately.");
      CompletelyFinishRace(riders);
    }
  }

  private void FinishRace(Dictionary<string, RiderInfo> riders)
  {
    // Don't immediately finish - allow other riders to complete their current lap
    _waitingForLeaderFinish = false;
    _waitingForFinalLaps = true;
    _finalLapsStartTime = DateTime.Now; // Track when final laps phase started

    // Calculate actual race finish time
    var actualRaceFinishTime = DateTime.Now;
    var actualRaceDuration = actualRaceFinishTime - _raceStartTime!.Value;

    // Find the rider who just completed the target lap count
    var finishingRider = riders.Values
        .FirstOrDefault(r => r.TotalLaps >= TargetLapsToFinishRace);

    var finishingRiderTag = finishingRider?.TagID ?? "Unknown";

    MessageAdded?.Invoke($"🏁 RACE TARGET REACHED! {finishingRiderTag} completed {TargetLapsToFinishRace} laps in {actualRaceDuration:mm\\:ss}.");
    MessageAdded?.Invoke($"🏁 All other riders must complete only their current lap, then no more laps will be counted.");

    // Store the current lap numbers for all riders at race finish
    foreach (var rider in riders.Values)
    {
      if (rider.TotalLaps >= TargetLapsToFinishRace)
      {
        // Riders who reached the target are NOT allowed to complete another lap
        rider.FinalAllowedLap = rider.TotalLaps;
        MessageAdded?.Invoke($"📋 Rider {rider.TagID}: Reached target with {rider.TotalLaps} laps, RACE FINISHED - no more laps allowed");
      }
      else
      {
        // All other riders are allowed to complete exactly one more lap (their current lap)
        rider.FinalAllowedLap = rider.TotalLaps + 1;
        MessageAdded?.Invoke($"📋 Rider {rider.TagID}: Currently has {rider.TotalLaps} laps, allowed to complete lap {rider.FinalAllowedLap}");
      }
    }
  }

  private void CompletelyFinishRace(Dictionary<string, RiderInfo> riders)
  {
    _raceFinished = true;
    _waitingForFinalLaps = false;
    _finalLapsStartTime = null; // Reset final laps tracking

    var actualRaceFinishTime = DateTime.Now;

    // Set the actual race end time
    _raceEndTime = actualRaceFinishTime;

    var actualRaceDuration = actualRaceFinishTime - _raceStartTime!.Value;

    // Count DNF riders
    var dnfRiders = riders.Values.Where(r => r.IsDNF).ToList();
    var finishedRiders = riders.Values.Where(r => !r.IsDNF).Count();

    MessageAdded?.Invoke($"🏁 RACE COMPLETELY FINISHED! All riders have completed their final laps or timed out.");
    MessageAdded?.Invoke($"🏁 Final race duration: {actualRaceDuration:mm\\:ss}");

    if (dnfRiders.Any())
    {
      MessageAdded?.Invoke($"🚫 DNF Summary: {dnfRiders.Count} rider(s) marked as Did Not Finish:");
      foreach (var dnfRider in dnfRiders)
      {
        var raceLeaderFinishTime = dnfRider.DNFTime?.AddMinutes(-_dnfTimeoutMinutes) ?? DateTime.Now;
        var timeAtDNF = dnfRider.DNFTime.HasValue ?
            (dnfRider.DNFTime.Value - raceLeaderFinishTime).TotalMinutes : 0;
        MessageAdded?.Invoke($"   • {dnfRider.TagID}: {dnfRider.TotalLaps} laps completed, DNF after {timeAtDNF:F1} min timeout");
      }
      MessageAdded?.Invoke($"✅ {finishedRiders} rider(s) completed the race successfully.");
    }
    else
    {
      MessageAdded?.Invoke($"✅ All {finishedRiders} riders completed the race successfully - no DNF!");
    }

    MessageAdded?.Invoke($"🏁 Race results are now final. Additional tag reads will be ignored.");
  }

  /// <summary>
  /// Reset all race state data
  /// </summary>
  public void Reset()
  {
    _raceStartTime = null;
    _raceEndTime = null;
    _raceStarted = false;
    _raceFinished = false;
    _raceTimeExpired = false;
    _waitingForLeaderFinish = false;
    _waitingForFinalLaps = false;
    _finalLapsStartTime = null;
    _leaderAtTimeExpiry = null;
    _leaderLapsAtTimeExpiry = 0;
    TargetLapsToFinishRace = 0;
    _fiveMinuteWarningShown = false;
    _oneMinuteWarningShown = false;

    MessageAdded?.Invoke("🔄 Race state reset.");
  }

  /// <summary>
  /// Set the race duration in minutes
  /// </summary>
  public void SetRaceDuration(int minutes)
  {
    _raceDuration = TimeSpan.FromMinutes(minutes);
    _fiveMinuteWarningShown = false;
    _oneMinuteWarningShown = false;

    if (_raceStartTime.HasValue)
    {
      _raceEndTime = _raceStartTime.Value + _raceDuration;
      MessageAdded?.Invoke($"⏰ Race duration updated to {minutes} minutes. New end time: {_raceEndTime:HH:mm:ss}");
    }
  }

  public void FinishRace(string finishingRiderTag)
  {
    // Don't immediately finish - allow other riders to complete their current lap
    _waitingForLeaderFinish = false;
    _waitingForFinalLaps = true;
    _finalLapsStartTime = DateTime.Now; // Track when final laps phase started

    // Calculate actual race finish time
    var actualRaceFinishTime = DateTime.Now;
    var actualRaceDuration = actualRaceFinishTime - _raceStartTime!.Value;

    MessageAdded?.Invoke($"🏁 RACE TARGET REACHED! {finishingRiderTag} completed {TargetLapsToFinishRace} laps in {actualRaceDuration:mm\\:ss}.");
  }

  public void CompleteRaceFinish(DateTime actualRaceFinishTime)
  {
    _raceFinished = true;
    _waitingForFinalLaps = false;
    _finalLapsStartTime = null; // Reset final laps tracking

    // Set the actual race end time to when the race actually finished
    _raceEndTime = actualRaceFinishTime;

    // Calculate actual race finish time and duration
    var actualRaceDuration = actualRaceFinishTime - _raceStartTime!.Value;

    MessageAdded?.Invoke($"🏆 RACE COMPLETE! Total time: {actualRaceDuration:mm\\:ss}");
  }

  public void SetAdditionalLapsAfterTimeExpiry(int additionalLaps)
  {
    _additionalLapsAfterTimeExpiry = additionalLaps;
    MessageAdded?.Invoke($"⚙️ Additional laps after time expiry set to: {additionalLaps}");
  }

  public void SetDnfTimeoutMinutes(int timeoutMinutes)
  {
    _dnfTimeoutMinutes = timeoutMinutes;
    MessageAdded?.Invoke($"⚙️ DNF timeout set to {timeoutMinutes} minutes after leader finishes");
  }

  public void SetRaceFinished(bool finished)
  {
    _raceFinished = finished;
  }

  public void SetWaitingForFinalLaps(bool waiting)
  {
    _waitingForFinalLaps = waiting;
  }

  public void SetFinalLapsStartTime(DateTime? startTime)
  {
    _finalLapsStartTime = startTime;
  }

  public void SetTargetLapsToFinishRace(int targetLaps)
  {
    TargetLapsToFinishRace = targetLaps;
  }

  public void ClearPositionTracking()
  {
    LastKnownPositions.Clear();
    LastKnownLapCounts.Clear();
    LastPositionCheck = DateTime.Now;
  }

  public void UpdatePositionTracking(Dictionary<string, RiderInfo> riders)
  {
    LastKnownPositions.Clear();
    LastKnownLapCounts.Clear();

    var sortedRiders = riders.Values
      .OrderBy(r => r.IsDNF ? 1 : 0) // Non-DNF riders first (0), DNF riders last (1)
      .ThenByDescending(r => r.TotalLaps)
      .ThenBy(r => r.TotalTime)
      .ToList();

    for (int i = 0; i < sortedRiders.Count; i++)
    {
      var rider = sortedRiders[i];
      LastKnownPositions[rider.TagID] = i + 1; // 1-based position
      LastKnownLapCounts[rider.TagID] = rider.TotalLaps;
    }
    LastPositionCheck = DateTime.Now;
  }

  /// <summary>
  /// Reset all race state data
  /// </summary>
  public void ResetRace()
  {
    _raceStartTime = null;
    _raceEndTime = null;
    _raceStarted = false;
    _raceFinished = false;
    _raceTimeExpired = false;
    _waitingForLeaderFinish = false;
    _waitingForFinalLaps = false;
    _fiveMinuteWarningShown = false;
    _oneMinuteWarningShown = false;
    _finalLapsStartTime = null;
    _leaderAtTimeExpiry = null;
    _leaderLapsAtTimeExpiry = 0;
    TargetLapsToFinishRace = 0;
    ClearPositionTracking();
    LapProgressionHistory.Clear();
    LapProgressionNeedsUpdate = false;
  }
}
