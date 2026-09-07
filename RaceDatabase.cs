using System.Diagnostics.CodeAnalysis;
using LiteDB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CrossMgrInterface;

// Database models
public class DbRace
{
  public int Id { get; set; }

  /// <summary>Operator-supplied name, e.g. "Moto 1 - 250cc". LiteDB is schemaless,
  /// so existing race documents simply read back as empty.</summary>
  public string Name { get; set; } = "";
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

  /// <summary>Circuit this race was run on, or null. Schemaless, like Name above:
  /// races recorded before the track map existed read back as null.</summary>
  public string? TrackId { get; set; }

  /// <summary>Practice, qualifying or a race. Schemaless, like Name and TrackId
  /// above: races recorded before session types existed read back as
  /// <see cref="SessionType.Race"/>, which is what they were.</summary>
  public SessionType SessionType { get; set; }

  /// <summary>
  /// The extra-laps rule the race was run under, so a reprinted sheet can say
  /// so. Nullable rather than zero: races recorded before this existed read
  /// back as null, and a crash-recovered one then keeps the operator's current
  /// setting instead of silently switching to "no extra laps".
  /// </summary>
  public int? AdditionalLaps { get; set; }

  // The rest of what the race was scored under, for the sheet. Nullable for
  // the same reason as AdditionalLaps. See RaceRules.
  public int? DnfTimeoutMinutes { get; set; }
  public double? MinimumLapSeconds { get; set; }
  public bool? ManualStart { get; set; }

  /// <summary>
  /// The classes' start order and gaps, with the moment each one actually
  /// left, for a race started in waves. Null for a race with one start.
  /// Kept on the race row so a crash mid-schedule comes back still knowing
  /// which classes are away and when the next is due.
  /// </summary>
  public List<DbStartWave>? Waves { get; set; }

  public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>One class of a staggered start, as stored. See <see cref="WaveSchedule"/>.</summary>
public class DbStartWave
{
  public string Class { get; set; } = "";
  public double DelaySeconds { get; set; }
  public DateTime? StartedAt { get; set; }
}

public class DbRider
{
  public int Id { get; set; }
  public int RaceId { get; set; }
  public string TagID { get; set; } = "";
  public string RiderNumber { get; set; } = "";
  public string FirstName { get; set; } = "";
  public string LastName { get; set; } = "";
  public string Team { get; set; } = "";
  public string Category { get; set; } = "";
  public string Machine { get; set; } = "";
  public DateTime LastCrossingTime { get; set; }
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

  // Operator rulings. Without these a restored race forgot every DNS and every
  // "ended by the operator" reason, so the reprinted sheet disagreed with the
  // one handed out on the day.
  public bool IsDNS { get; set; }
  public bool StatusSetByOperator { get; set; }
  public string? StatusReason { get; set; }

  /// <summary>
  /// The operator stopped counting this transponder but kept its laps. The
  /// ignore list itself lives in memory, so this is how it comes back.
  /// </summary>
  public bool IsIgnored { get; set; }

  /// <summary>
  /// On the rider list for the session but never crossed the loop. Written once
  /// at the end of a session so a gate pick order reprinted later still lists
  /// them last, as the one printed on the day did. Never counted as a rider
  /// who took part, and never restored into a live race.
  /// </summary>
  public bool RosterOnly { get; set; }
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
  public bool IsSplitLap { get; set; } = false; // Track if this lap was created by splitting missed reads

  // Pending missed-read warnings and operator corrections. Without these a crash
  // recovery silently dropped every outstanding warning and every note about a
  // lap having been corrected.
  public bool IsSuggestedForSplit { get; set; }
  public int SuggestedSplitCount { get; set; }
  public TimeSpan? SuggestedSplitLapTime { get; set; }
  public bool SuggestionDismissed { get; set; }
  public int Source { get; set; }
  public DateTime? OriginalCrossingTime { get; set; }
  public string? CorrectionNote { get; set; }
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

/// <summary>One row of the Past sessions list: the race and what it holds.</summary>
public sealed record SessionSummary(DbRace Race, int Riders, int Laps);

/// <summary>
/// Database service for managing race data with LiteDB
/// </summary>
public class RaceDataService : IDisposable
{
  private LiteDatabase _db;
  private ILiteCollection<DbRace> _races;
  private ILiteCollection<DbRider> _riders;
  private ILiteCollection<DbLap> _laps;
  private ILiteCollection<DbPositionSnapshot> _positions;
  private ILiteCollection<DbRaceEvent> _events;
  private ILiteCollection<DbLapDifference> _lapDiffs;

  public int CurrentRaceId { get; private set; }

  /// <summary>
  /// Set when the database could not be opened and a fresh one was started in
  /// its place. The old file is kept; see <see cref="QuarantinedDatabasePath"/>.
  /// </summary>
  public bool RecoveredFromUnreadableDatabase { get; private set; }

  /// <summary>Where the unreadable database was moved to, if that happened.</summary>
  public string? QuarantinedDatabasePath { get; private set; }

  public RaceDataService(string dbPath = "races.db")
  {
    try
    {
      Initialise(dbPath);
    }
    catch (Exception)
    {
      // LiteDB opens lazily: a damaged file does not fail in the constructor but
      // on the first real page read, which is why the whole initialisation has to
      // be inside the retry rather than just the open.
      QuarantineDatabase(dbPath);
      Initialise(dbPath);
    }
  }

  [MemberNotNull(nameof(_db), nameof(_races), nameof(_riders), nameof(_laps),
                 nameof(_positions), nameof(_events), nameof(_lapDiffs))]
  private void Initialise(string dbPath)
  {
    _db = new LiteDatabase(dbPath);

    _races = _db.GetCollection<DbRace>("races");
    _riders = _db.GetCollection<DbRider>("riders");
    _laps = _db.GetCollection<DbLap>("laps");
    _positions = _db.GetCollection<DbPositionSnapshot>("positions");
    _events = _db.GetCollection<DbRaceEvent>("events");
    _lapDiffs = _db.GetCollection<DbLapDifference>("lap_differences");

    // Touching an index forces the first real read, so a damaged file fails here.
    _riders.EnsureIndex(x => x.RaceId);
    _riders.EnsureIndex(x => x.TagID);
    _laps.EnsureIndex(x => x.RaceId);
    _laps.EnsureIndex(x => x.RiderTagID);
    _positions.EnsureIndex(x => x.RaceId);
    _events.EnsureIndex(x => x.RaceId);
    _lapDiffs.EnsureIndex(x => x.RaceId);
  }

  /// <summary>
  /// Moves an unreadable database aside so a fresh one can take its place.
  ///
  /// Not being able to time today's racing is far worse than losing access to an
  /// old database, so the file is preserved rather than deleted and the
  /// application carries on.
  /// </summary>
  private void QuarantineDatabase(string dbPath)
  {
    try { _db?.Dispose(); } catch (Exception) { }

    var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    var quarantine = $"{dbPath}.unreadable-{stamp}";

    if (File.Exists(dbPath)) File.Move(dbPath, quarantine);

    // LiteDB keeps a write-ahead log beside the database. An orphaned log will
    // corrupt the replacement immediately, so it has to move as well.
    var log = Path.ChangeExtension(dbPath, null) + "-log.db";
    if (File.Exists(log)) File.Move(log, $"{quarantine}-log");

    RecoveredFromUnreadableDatabase = true;
    QuarantinedDatabasePath = quarantine;
  }

  #region Race Management

  /// <summary>
  /// Records the session type once, here. It is deliberately absent from
  /// SaveRaceState: the type is chosen in setup and cannot change once the
  /// clock is running, so periodic state saves have nothing to say about it.
  /// </summary>
  public int StartNewRace(DateTime startTime, TimeSpan duration, string name = "",
    SessionType sessionType = SessionType.Race, RaceRules? rules = null)
  {
    var race = new DbRace
    {
      Name = name,
      StartTime = startTime,
      Duration = duration,
      SessionType = sessionType,
      IsFinished = false,
      IsTimeExpired = false
    };
    if (rules != null) ApplyRules(race, rules);

    CurrentRaceId = _races.Insert(race);
    return CurrentRaceId;
  }

  /// <summary>
  /// Records what the race is being scored under. Written at the start and
  /// again on every save, because the duration and the DNF timeout can be
  /// changed while the clock is running and the sheet must show what applied.
  /// </summary>
  private static void ApplyRules(DbRace race, RaceRules rules)
  {
    race.Duration = rules.Duration;
    race.AdditionalLaps = rules.AdditionalLaps;
    race.DnfTimeoutMinutes = rules.DnfTimeoutMinutes;
    race.MinimumLapSeconds = rules.MinimumLapSeconds;
    race.ManualStart = rules.ManualStart;
    race.Waves = rules.Waves?.ToRecords();
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

  public DbRace? GetRace(int raceId) => raceId > 0 ? _races.FindById(raceId) : null;

  public void RenameRace(int raceId, string name)
  {
    var race = _races.FindById(raceId);
    if (race == null) return;
    race.Name = name;
    _races.Update(race);
  }

  /// <summary>
  /// Forgets which race is current without touching it. Every write in this
  /// class is guarded by CurrentRaceId, so after this nothing lands in the
  /// session that just ended.
  /// </summary>
  public void CloseCurrentRace() => CurrentRaceId = 0;

  #endregion

  #region Rider Management

  /// <param name="ignored">Whether the operator has stopped counting this
  /// transponder. Not on RiderInfo, because the ignore list is kept beside the
  /// riders rather than on them.</param>
  public void UpsertRider(RiderInfo riderInfo, bool ignored = false)
  {
    if (CurrentRaceId == 0) return;

    var existingRider = _riders.FindOne(r => r.RaceId == CurrentRaceId && r.TagID == riderInfo.TagID);

    var dbRider = ToDbRider(riderInfo);
    dbRider.IsIgnored = ignored;

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

  private DbRider ToDbRider(RiderInfo riderInfo) => new()
  {
    RaceId = CurrentRaceId,
    TagID = riderInfo.TagID,
    RiderNumber = riderInfo.RiderNumber,
    FirstName = riderInfo.FirstName,
    LastName = riderInfo.LastName,
    Team = riderInfo.Team,
    Category = riderInfo.Category,
    Machine = riderInfo.Machine,
    LastCrossingTime = riderInfo.LastCrossingTime,
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
    DNFTime = riderInfo.DNFTime,
    IsDNS = riderInfo.IsDNS,
    StatusSetByOperator = riderInfo.StatusSetByOperator,
    StatusReason = riderInfo.StatusReason
  };

  /// <summary>
  /// Records the riders who were entered but never crossed the loop, so the
  /// session's gate pick order can be reprinted complete. Only tags with no row
  /// yet: a rider who did cross is already here as a real rider.
  /// </summary>
  public void SaveRosterOnlyRiders(IEnumerable<RiderInfo> rosterOnly)
  {
    if (CurrentRaceId == 0) return;

    var known = _riders.Find(r => r.RaceId == CurrentRaceId)
      .Select(r => r.TagID)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var rows = new List<DbRider>();
    foreach (var rider in rosterOnly)
    {
      if (string.IsNullOrWhiteSpace(rider.TagID) || !known.Add(rider.TagID)) continue;
      var row = ToDbRider(rider);
      row.RosterOnly = true;
      rows.Add(row);
    }

    if (rows.Count > 0) _riders.InsertBulk(rows);
  }

  /// <summary>
  /// Removes a rider's row along with their laps. Used when the operator stops
  /// counting a transponder and discards what it recorded - deleting only the
  /// laps left a zero-lap rider behind to reappear on a reprinted sheet.
  /// </summary>
  public void DeleteRider(string riderTagID)
  {
    if (CurrentRaceId == 0) return;

    _laps.DeleteMany(l => l.RaceId == CurrentRaceId && l.RiderTagID == riderTagID);
    _riders.DeleteMany(r => r.RaceId == CurrentRaceId && r.TagID == riderTagID);
  }

  /// <summary>Transponders the operator stopped counting, as last saved.</summary>
  public List<string> GetIgnoredTags(int raceId)
  {
    return _riders.Find(r => r.RaceId == raceId)
      .Where(r => r.IsIgnored)
      .Select(r => r.TagID)
      .ToList();
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
      existingLap.IsSplitLap = lap.IsSplitLap;
      CopyCorrectionFields(lap, existingLap);
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
      PositionAtCompletion = positionAtCompletion,
      IsSplitLap = lap.IsSplitLap
    };
    CopyCorrectionFields(lap, dbLap);

    _laps.Insert(dbLap);
    Console.WriteLine($"Inserted lap: Rider {riderTagID}, Lap {lap.LapNumber}, Race {CurrentRaceId}");
  }

  private static void CopyCorrectionFields(RiderLap from, DbLap to)
  {
    to.IsSuggestedForSplit = from.IsSuggestedForSplit;
    to.SuggestedSplitCount = from.SuggestedSplitCount;
    to.SuggestedSplitLapTime = from.SuggestedSplitLapTime;
    to.SuggestionDismissed = from.SuggestionDismissed;
    to.Source = (int)from.Source;
    to.OriginalCrossingTime = from.OriginalCrossingTime;
    to.CorrectionNote = from.CorrectionNote;
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

  /// <summary>
  /// Replaces a rider's stored laps wholesale.
  ///
  /// Corrections renumber laps, and lap rows are keyed by (race, rider, lap
  /// number) - so patching row by row means deleting and re-inserting in an
  /// order that must not collide with itself. Replacing the set is both simpler
  /// and atomic from the caller's point of view.
  /// </summary>
  public void ReplaceRiderLaps(string riderTagID, IEnumerable<RiderLap> laps, Func<RiderLap, int> positionOf)
  {
    if (CurrentRaceId == 0) return;

    _laps.DeleteMany(l => l.RaceId == CurrentRaceId && l.RiderTagID == riderTagID);

    var rows = laps
      .Where(l => !l.IsDeleted)
      .Select(l => new DbLap
      {
        RaceId = CurrentRaceId,
        RiderTagID = riderTagID,
        LapNumber = l.LapNumber,
        CrossingTime = l.CrossingTime,
        LapTime = l.LapTime,
        PositionAtCompletion = positionOf(l),
        IsSplitLap = l.IsSplitLap,
        IsSuggestedForSplit = l.IsSuggestedForSplit,
        SuggestedSplitCount = l.SuggestedSplitCount,
        SuggestedSplitLapTime = l.SuggestedSplitLapTime,
        SuggestionDismissed = l.SuggestionDismissed,
        Source = (int)l.Source,
        OriginalCrossingTime = l.OriginalCrossingTime,
        CorrectionNote = l.CorrectionNote
      })
      .ToList();

    if (rows.Count > 0)
      _laps.InsertBulk(rows);
  }

  public bool DeleteLap(string riderTagID, int lapNumber)
  {
    if (CurrentRaceId == 0) return false;

    var lapToDelete = _laps.FindOne(l => l.RaceId == CurrentRaceId &&
                                        l.RiderTagID == riderTagID &&
                                        l.LapNumber == lapNumber);

    if (lapToDelete != null)
    {
      _laps.Delete(lapToDelete.Id);
      Console.WriteLine($"Deleted lap: Rider {riderTagID}, Lap {lapNumber}, Race {CurrentRaceId}");
      return true;
    }

    return false;
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
  /// Rebuilds the riders of a race from the database, for crash recovery and
  /// for reprinting a past session's sheets.
  /// </summary>
  /// <param name="includeRosterOnly">Also return the riders who were entered but
  /// never crossed. Wanted for a gate pick order, which lists them last; never
  /// for a race classification or a live race, where a zero-lap rider would be
  /// scored as though they had started.</param>
  public Dictionary<string, RiderInfo> RestoreRiderData(int raceId, bool includeRosterOnly = false)
  {
    var restoredRiders = new Dictionary<string, RiderInfo>();
    var dbRiders = _riders.Find(r => r.RaceId == raceId)
      .Where(r => includeRosterOnly || !r.RosterOnly)
      .ToList();

    Console.WriteLine($"RestoreRiderData: Found {dbRiders.Count} riders for race {raceId}");

    foreach (var dbRider in dbRiders)
    {
      var riderInfo = new RiderInfo
      {
        TagID = dbRider.TagID,
        RiderNumber = dbRider.RiderNumber,
        FirstName = dbRider.FirstName,
        LastName = dbRider.LastName,
        Team = dbRider.Team,
        Category = dbRider.Category,
        Machine = dbRider.Machine,
        LastCrossingTime = dbRider.LastCrossingTime,
        FirstCrossing = dbRider.FirstCrossing,
        LastCrossing = dbRider.LastCrossing,
        RaceStartTime = dbRider.RaceStartTime,
        FinalAllowedLap = dbRider.FinalAllowedLap,
        IsDNF = dbRider.IsDNF,
        DNFTime = dbRider.DNFTime,
        IsDNS = dbRider.IsDNS,
        StatusSetByOperator = dbRider.StatusSetByOperator,
        StatusReason = dbRider.StatusReason
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
          LapTime = dbLap.LapTime,
          IsSplitLap = dbLap.IsSplitLap,
        IsSuggestedForSplit = dbLap.IsSuggestedForSplit,
        SuggestedSplitCount = dbLap.SuggestedSplitCount,
        SuggestedSplitLapTime = dbLap.SuggestedSplitLapTime,
        SuggestionDismissed = dbLap.SuggestionDismissed,
        Source = (LapSource)dbLap.Source,
        OriginalCrossingTime = dbLap.OriginalCrossingTime,
        CorrectionNote = dbLap.CorrectionNote
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
    bool fiveMinuteWarningShown, IReadOnlyCollection<string>? ignoredTags = null,
    RaceRules? rules = null)
  {
    if (CurrentRaceId == 0) return;

    // Update race record with current state
    UpdateRace(race =>
    {
      if (rules != null) ApplyRules(race, rules);
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

    // Save all rider data. Ignored riders are saved too, marked as such: they
    // used to be skipped, which left their row stale and their status lost.
    foreach (var rider in riders.Values)
    {
      UpsertRider(rider, ignored: ignoredTags?.Contains(rider.TagID) == true);
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

  /// <summary>
  /// Every stored session, newest first, with how much each one holds.
  ///
  /// Counted in memory rather than with a database predicate on the new flags:
  /// a rider written before those fields existed has no such field at all, and
  /// LiteDB does not treat a missing field as false.
  /// </summary>
  public List<SessionSummary> ListSessions()
  {
    var summaries = new List<SessionSummary>();

    foreach (var race in GetAllRaces())
    {
      var riders = _riders.Find(r => r.RaceId == race.Id)
        .Count(r => !r.RosterOnly && !r.IsIgnored);
      var laps = _laps.Count(l => l.RaceId == race.Id);
      summaries.Add(new SessionSummary(race, riders, laps));
    }

    return summaries;
  }

  /// <summary>
  /// Removes a race and everything recorded under it. The race row goes too -
  /// the old clear-out left it behind, so the list of stored sessions filled
  /// with empty entries nobody could get rid of.
  /// </summary>
  public void DeleteRace(int raceId)
  {
    if (raceId <= 0) return;

    _riders.DeleteMany(r => r.RaceId == raceId);
    _laps.DeleteMany(l => l.RaceId == raceId);
    _positions.DeleteMany(p => p.RaceId == raceId);
    _events.DeleteMany(e => e.RaceId == raceId);
    _lapDiffs.DeleteMany(ld => ld.RaceId == raceId);
    _races.Delete(raceId);

    if (CurrentRaceId == raceId) CurrentRaceId = 0;
  }

  #endregion

  public void Dispose()
  {
    _db?.Dispose();
  }
}
