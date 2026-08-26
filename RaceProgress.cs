namespace CrossMgrInterface;

/// <summary>
/// How far through the race a rider actually is, counted in laps and fractions
/// of the lap they are on.
///
/// This exists because a raw lap COUNT cannot answer "has A lapped B?". Lap
/// counts increment at the line, so two riders three seconds apart on the track
/// have counts differing by one for most of every lap. Treating that difference
/// as a lapping event announced, on a 250-rider field, that every rider had
/// lapped everybody who happened to cross after them - fifteen thousand events
/// from seventeen hundred crossings.
///
/// Progress closes the gap by adding the fraction of the current lap already
/// covered, dead-reckoned from the last crossing exactly as the track map does:
///
///   progress = laps completed + (now - last crossing) / recent pace
///
/// Two riders seconds apart then differ by ~0.07 of a lap rather than by a whole
/// one, and a genuine lapping still reads as a difference of at least 1.
/// </summary>
public static class RaceProgress
{
  /// <summary>
  /// Laps and fractions covered. Falls back to the whole lap count when there is
  /// no usable pace - which makes it no worse than the old measure, never worse.
  /// </summary>
  public static double Of(RiderInfo rider, DateTime now, TimeSpan? fallbackPace)
  {
    if (rider.TotalLaps == 0) return 0;

    var pace = TrackPositionSolver.UsablePace(rider.RacingPace)
               ?? TrackPositionSolver.UsablePace(fallbackPace);

    if (pace is null) return rider.TotalLaps;

    var elapsed = now - rider.LastCrossing;
    if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

    // Clamped at a full lap: a rider who is overdue has not silently gained one.
    return rider.TotalLaps + Math.Clamp(elapsed.TotalSeconds / pace.Value.TotalSeconds, 0, 1);
  }

  /// <summary>
  /// Median pace across the field, for riders too new to have one of their own.
  /// Median rather than mean, so one rider who stopped does not drag it out.
  /// </summary>
  public static TimeSpan? MedianPace(IReadOnlyList<RiderInfo> field)
  {
    var paces = new List<double>(field.Count);

    for (var i = 0; i < field.Count; i++)
    {
      if (field[i].IsDNF || field[i].IsDNS) continue;

      var pace = TrackPositionSolver.UsablePace(field[i].RacingPace);
      if (pace.HasValue) paces.Add(pace.Value.TotalSeconds);
    }

    if (paces.Count == 0) return null;

    paces.Sort();
    var middle = paces.Count / 2;

    return TimeSpan.FromSeconds(paces.Count % 2 == 1
      ? paces[middle]
      : (paces[middle - 1] + paces[middle]) / 2);
  }

  /// <summary>
  /// Whole laps by which one rider leads another on the track. Zero means not
  /// lapped - being merely ahead in the standings is not the same thing.
  /// </summary>
  public static int WholeLapLead(double leaderProgress, double otherProgress)
  {
    var lead = leaderProgress - otherProgress;
    return lead < 1.0 ? 0 : (int)Math.Floor(lead);
  }
}
