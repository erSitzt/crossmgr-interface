using LiteDB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CrossMgrInterface;

// Database models
public class DbRace
{
  public int Id { get; set; }
  public DateTime StartTime { get; set; }
  public DateTime? EndTime { get; set; }
  public TimeSpan Duration { get; set; }
  public bool IsFinished { get; set; }
  public bool IsTimeExpired { get; set; }
  public string? LeaderAtTimeExpiry { get; set; }
  public int LeaderLapsAtTimeExpiry { get; set; }
  public int TargetLapsToFinishRace { get; set; }
  public bool WaitingForLeaderFinish { get; set; }
  public bool WaitingForFinalLaps { get; set; }
  public DateTime? FinalLapsStartTime { get; set; }
  public bool FiveMinuteWarningShown { get; set; }
  public DateTime? LastSavedAt { get; set; }
  public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class DbRider
{
  public int Id { get; set; }
  public int RaceId { get; set; }
  public string TagID { get; set; } = "";
  public DateTime FirstCrossing { get; set; }
  public DateTime LastCrossing { get; set; }
  public DateTime? RaceStartTime { get; set; }
  public int FinalAllowedLap { get; set; } = int.MaxValue;
  public int TotalLaps { get; set; }
  public TimeSpan TotalTime { get; set; }
  public TimeSpan? BestLapTime { get; set; }
  public TimeSpan? LastLapTime { get; set; }
  public TimeSpan? PredictedLapTime { get; set; }
  public DateTime? EstimatedNextCrossing { get; set; }
  public bool IsDNF { get; set; }
  public DateTime? DNFTime { get; set; }
}

public class DbLap
{
  public int Id { get; set; }
  public int RaceId { get; set; }
  public string RiderTagID { get; set; } = "";
  public int LapNumber { get; set; }
  public DateTime CrossingTime { get; set; }
  public TimeSpan? LapTime { get; set; }
  public int PositionAtCompletion { get; set; }
}

public class DbPositionSnapshot
{
  public int Id { get; set; }
  public int RaceId { get; set; }
  public string RiderTagID { get; set; } = "";
  public int Position { get; set; }
  public int LapCount { get; set; }
  public DateTime SnapshotTime { get; set; }
}

public class DbRaceEvent
{
  public int Id { get; set; }
  public int RaceId { get; set; }
  public DateTime EventTime { get; set; }
  public string EventType { get; set; } = ""; // "PASSING", "LAPPING", "POSITION_CHANGE", "RACE_EVENT"
  public string RiderTagID { get; set; } = "";
  public string? OtherRiderTagID { get; set; }
  public string Message { get; set; } = "";
  public string? AdditionalData { get; set; } // JSON for extra data
}

public class DbLapDifference
{
  public int Id { get; set; }
  public int RaceId { get; set; }
  public string RiderA { get; set; } = "";
  public string RiderB { get; set; } = "";
  public int LapDifference { get; set; }
  public DateTime LastUpdated { get; set; }
}

/// <summary>
/// Database service for managing race data with LiteDB
/// </summary>
public class RaceDataService : IDisposable
{
  private readonly LiteDatabase _db;
  private readonly ILiteCollection<DbRace> _races;
  private readonly ILiteCollection<DbRider> _riders;
  private readonly ILiteCollection<DbLap> _laps;
  private readonly ILiteCollection<DbPositionSnapshot> _positions;
  private readonly ILiteCollection<DbRaceEvent> _events;
  private readonly ILiteCollection<DbLapDifference> _lapDiffs;

  public int CurrentRaceId { get; private set; }

  public RaceDataService(string dbPath = "races.db")
  {
    _db = new LiteDatabase(dbPath);

    _races = _db.GetCollection<DbRace>("races");
    _riders = _db.GetCollection<DbRider>("riders");
    _laps = _db.GetCollection<DbLap>("laps");
    _positions = _db.GetCollection<DbPositionSnapshot>("positions");
    _events = _db.GetCollection<DbRaceEvent>("events");
    _lapDiffs = _db.GetCollection<DbLapDifference>("lap_differences");

    // Create indexes for better performance
    _riders.EnsureIndex(x => x.RaceId);
    _riders.EnsureIndex(x => x.TagID);
    _laps.EnsureIndex(x => x.RaceId);
    _laps.EnsureIndex(x => x.RiderTagID);
    _positions.EnsureIndex(x => x.RaceId);
    _events.EnsureIndex(x => x.RaceId);
    _lapDiffs.EnsureIndex(x => x.RaceId);
  }

  #region Race Management

  public int StartNewRace(DateTime startTime, TimeSpan duration)
  {
    var race = new DbRace
    {
      StartTime = startTime,
      Duration = duration,
      IsFinished = false,
      IsTimeExpired = false
    };

    CurrentRaceId = _races.Insert(race);
    return CurrentRaceId;
  }

  public void UpdateRace(Action<DbRace> updateAction)
  {
    if (CurrentRaceId == 0) return;

    var race = _races.FindById(CurrentRaceId);
    if (race != null)
    {
      updateAction(race);
      _races.Update(race);
    }
  }

  public DbRace? GetCurrentRace()
  {
    return CurrentRaceId > 0 ? _races.FindById(CurrentRaceId) : null;
  }

  #endregion

  #region Rider Management

  public void UpsertRider(RiderInfo riderInfo)
  {
    if (CurrentRaceId == 0) return;

    var existingRider = _riders.FindOne(r => r.RaceId == CurrentRaceId && r.TagID == riderInfo.TagID);

    var dbRider = new DbRider
    {
      RaceId = CurrentRaceId,
      TagID = riderInfo.TagID,
      FirstCrossing = riderInfo.FirstCrossing,
      LastCrossing = riderInfo.LastCrossing,
      RaceStartTime = riderInfo.RaceStartTime,
      FinalAllowedLap = riderInfo.FinalAllowedLap,
      TotalLaps = riderInfo.TotalLaps,
      TotalTime = riderInfo.TotalTime,
      BestLapTime = riderInfo.BestLapTime,
      LastLapTime = riderInfo.LastLapTime,
      PredictedLapTime = riderInfo.PredictedLapTime,
      EstimatedNextCrossing = riderInfo.EstimatedNextCrossing,
      IsDNF = riderInfo.IsDNF,
      DNFTime = riderInfo.DNFTime
    };

    if (existingRider != null)
    {
      dbRider.Id = existingRider.Id;
      _riders.Update(dbRider);
    }
    else
    {
      _riders.Insert(dbRider);
    }
  }

  public List<DbRider> GetAllRiders()
  {
    if (CurrentRaceId == 0) return new List<DbRider>();
    return _riders.Find(r => r.RaceId == CurrentRaceId).ToList();
  }

  #endregion

  #region Lap Management

  public void AddLap(string riderTagID, RiderLap lap, int positionAtCompletion)
  {
    if (CurrentRaceId == 0)
    {
      // Debug: Log when CurrentRaceId is 0
      Console.WriteLine($"AddLap failed: CurrentRaceId is 0 for rider {riderTagID}, lap {lap.LapNumber}");
      return;
    }

    // Check if this lap already exists to prevent duplicates
    var existingLap = _laps.FindOne(l => l.RaceId == CurrentRaceId &&
                                        l.RiderTagID == riderTagID &&
                                        l.LapNumber == lap.LapNumber);

    if (existingLap != null)
    {
      // Lap already exists, update it instead
      existingLap.CrossingTime = lap.CrossingTime;
      existingLap.LapTime = lap.LapTime;
      existingLap.PositionAtCompletion = positionAtCompletion;
      _laps.Update(existingLap);
      Console.WriteLine($"Updated lap: Rider {riderTagID}, Lap {lap.LapNumber}, Race {CurrentRaceId}");
      return;
    }

    var dbLap = new DbLap
    {
      RaceId = CurrentRaceId,
      RiderTagID = riderTagID,
      LapNumber = lap.LapNumber,
      CrossingTime = lap.CrossingTime,
      LapTime = lap.LapTime,
      PositionAtCompletion = positionAtCompletion
    };

    _laps.Insert(dbLap);
    Console.WriteLine($"Inserted lap: Rider {riderTagID}, Lap {lap.LapNumber}, Race {CurrentRaceId}");
  }

  public List<DbLap> GetRiderLaps(string riderTagID)
  {
    if (CurrentRaceId == 0) return new List<DbLap>();
    return _laps.Find(l => l.RaceId == CurrentRaceId && l.RiderTagID == riderTagID)
               .OrderBy(l => l.LapNumber)
               .ToList();
  }

  public List<DbLap> GetAllLaps()
  {
    if (CurrentRaceId == 0) return new List<DbLap>();
    return _laps.Find(l => l.RaceId == CurrentRaceId)
               .OrderBy(l => l.CrossingTime)
               .ToList();
  }

  #endregion

  #region Position Tracking

  public void SavePositionSnapshot(Dictionary<string, int> positions, Dictionary<string, int> lapCounts)
  {
    if (CurrentRaceId == 0) return;

    var snapshots = positions.Select(kvp => new DbPositionSnapshot
    {
      RaceId = CurrentRaceId,
      RiderTagID = kvp.Key,
      Position = kvp.Value,
      LapCount = lapCounts.GetValueOrDefault(kvp.Key, 0),
      SnapshotTime = DateTime.Now
    });

    _positions.InsertBulk(snapshots);
  }

  public Dictionary<string, int> GetLastKnownPositions()
  {
    if (CurrentRaceId == 0) return new Dictionary<string, int>();

    var latestTime = _positions.Find(p => p.RaceId == CurrentRaceId)
                              .Max(p => p?.SnapshotTime);

    if (latestTime == null) return new Dictionary<string, int>();

    return _positions.Find(p => p.RaceId == CurrentRaceId && p.SnapshotTime == latestTime)
                    .ToDictionary(p => p.RiderTagID, p => p.Position);
  }

  public Dictionary<string, int> GetLastKnownLapCounts()
  {
    if (CurrentRaceId == 0) return new Dictionary<string, int>();

    var latestTime = _positions.Find(p => p.RaceId == CurrentRaceId)
                              .Max(p => p?.SnapshotTime);

    if (latestTime == null) return new Dictionary<string, int>();

    return _positions.Find(p => p.RaceId == CurrentRaceId && p.SnapshotTime == latestTime)
                    .ToDictionary(p => p.RiderTagID, p => p.LapCount);
  }

  #endregion

  #region Race Events

  public void AddRaceEvent(string eventType, string riderTagID, string message, string? otherRiderTagID = null, string? additionalData = null)
  {
    if (CurrentRaceId == 0) return;

    var raceEvent = new DbRaceEvent
    {
      RaceId = CurrentRaceId,
      EventTime = DateTime.Now,
      EventType = eventType,
      RiderTagID = riderTagID,
      OtherRiderTagID = otherRiderTagID,
      Message = message,
      AdditionalData = additionalData
    };

    _events.Insert(raceEvent);
  }

  public List<DbRaceEvent> GetRaceEvents(int limit = 100)
  {
    if (CurrentRaceId == 0) return new List<DbRaceEvent>();

    return _events.Find(e => e.RaceId == CurrentRaceId)
                 .OrderByDescending(e => e.EventTime)
                 .Take(limit)
                 .ToList();
  }

  #endregion

  #region Lap Differences

  public void StoreLapDifference(string riderA, string riderB, int lapDifference)
  {
    if (CurrentRaceId == 0) return;

    var existing = _lapDiffs.FindOne(ld => ld.RaceId == CurrentRaceId &&
                                          ld.RiderA == riderA &&
                                          ld.RiderB == riderB);

    var lapDiff = new DbLapDifference
    {
      RaceId = CurrentRaceId,
      RiderA = riderA,
      RiderB = riderB,
      LapDifference = lapDifference,
      LastUpdated = DateTime.Now
    };

    if (existing != null)
    {
      lapDiff.Id = existing.Id;
      _lapDiffs.Update(lapDiff);
    }
    else
    {
      _lapDiffs.Insert(lapDiff);
    }
  }

  public int GetPreviousLapDifference(string riderA, string riderB, int defaultValue)
  {
    if (CurrentRaceId == 0) return defaultValue;

    var lapDiff = _lapDiffs.FindOne(ld => ld.RaceId == CurrentRaceId &&
                                         ld.RiderA == riderA &&
                                         ld.RiderB == riderB);

    return lapDiff?.LapDifference ?? defaultValue;
  }

  #endregion

  #region Crash Recovery

  /// <summary>
  /// Gets the latest unfinished race for crash recovery
  /// </summary>
  public DbRace? GetLatestUnfinishedRace()
  {
    return _races.Find(r => !r.IsFinished)
                 .OrderByDescending(r => r.StartTime)
                 .FirstOrDefault();
  }

  /// <summary>
  /// Restores rider data from database for crash recovery
  /// </summary>
  public Dictionary<string, RiderInfo> RestoreRiderData(int raceId)
  {
    var restoredRiders = new Dictionary<string, RiderInfo>();
    var dbRiders = _riders.Find(r => r.RaceId == raceId).ToList();

    Console.WriteLine($"RestoreRiderData: Found {dbRiders.Count} riders for race {raceId}");

    foreach (var dbRider in dbRiders)
    {
      var riderInfo = new RiderInfo
      {
        TagID = dbRider.TagID,
        FirstCrossing = dbRider.FirstCrossing,
        LastCrossing = dbRider.LastCrossing,
        RaceStartTime = dbRider.RaceStartTime,
        FinalAllowedLap = dbRider.FinalAllowedLap,
        IsDNF = dbRider.IsDNF,
        DNFTime = dbRider.DNFTime
      };

      // Restore laps for this rider
      var dbLaps = _laps.Find(l => l.RaceId == raceId && l.RiderTagID == dbRider.TagID)
                        .OrderBy(l => l.LapNumber)
                        .ToList();

      Console.WriteLine($"RestoreRiderData: Rider {dbRider.TagID} has {dbLaps.Count} laps in database");

      foreach (var dbLap in dbLaps)
      {
        riderInfo.Laps.Add(new RiderLap
        {
          TagID = dbLap.RiderTagID,
          LapNumber = dbLap.LapNumber,
          CrossingTime = dbLap.CrossingTime,
          LapTime = dbLap.LapTime
        });
      }

      restoredRiders[dbRider.TagID] = riderInfo;
    }

    Console.WriteLine($"RestoreRiderData: Returning {restoredRiders.Count} riders with total laps: {restoredRiders.Values.Sum(r => r.Laps.Count)}");
    return restoredRiders;
  }

  /// <summary>
  /// Saves complete race state for crash recovery
  /// </summary>
  public void SaveRaceState(Dictionary<string, RiderInfo> riders, DateTime? raceStartTime,
    DateTime? raceEndTime, TimeSpan raceDuration, bool raceFinished, bool raceTimeExpired,
    bool waitingForLeaderFinish, bool waitingForFinalLaps, DateTime? finalLapsStartTime,
    string? leaderAtTimeExpiry, int leaderLapsAtTimeExpiry, int targetLapsToFinishRace,
    bool fiveMinuteWarningShown)
  {
    if (CurrentRaceId == 0) return;

    // Update race record with current state
    UpdateRace(race =>
    {
      race.StartTime = raceStartTime ?? race.StartTime;
      race.EndTime = raceEndTime;
      race.IsFinished = raceFinished;
      race.IsTimeExpired = raceTimeExpired;
      race.WaitingForLeaderFinish = waitingForLeaderFinish;
      race.WaitingForFinalLaps = waitingForFinalLaps;
      race.FinalLapsStartTime = finalLapsStartTime;
      race.LeaderAtTimeExpiry = leaderAtTimeExpiry;
      race.LeaderLapsAtTimeExpiry = leaderLapsAtTimeExpiry;
      race.TargetLapsToFinishRace = targetLapsToFinishRace;
      race.FiveMinuteWarningShown = fiveMinuteWarningShown;
      race.LastSavedAt = DateTime.Now;
    });

    // Save all rider data
    foreach (var rider in riders.Values)
    {
      UpsertRider(rider);
    }
  }

  #endregion

  #region Historical Data

  public List<DbRace> GetAllRaces()
  {
    return _races.FindAll().OrderByDescending(r => r.StartTime).ToList();
  }

  public void SetCurrentRace(int raceId)
  {
    CurrentRaceId = raceId;
  }

  public void ClearCurrentRaceData()
  {
    if (CurrentRaceId == 0) return;

    _riders.DeleteMany(r => r.RaceId == CurrentRaceId);
    _laps.DeleteMany(l => l.RaceId == CurrentRaceId);
    _positions.DeleteMany(p => p.RaceId == CurrentRaceId);
    _events.DeleteMany(e => e.RaceId == CurrentRaceId);
    _lapDiffs.DeleteMany(ld => ld.RaceId == CurrentRaceId);
  }

  #endregion

  public void Dispose()
  {
    _db?.Dispose();
  }
}
