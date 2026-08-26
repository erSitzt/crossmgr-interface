namespace CrossMgrInterface;

/// <summary>
/// Flags laps that look like a transponder read was missed - a lap that is a
/// near multiple of the rider's recent pace.
///
/// The live path checks only the lap that has just been recorded. This runs over
/// a rider's whole lap list, which is what is needed after a correction: editing
/// or deleting a lap changes the laps around it, so an earlier warning may no
/// longer apply, or a new one may now be warranted.
/// </summary>
public static class LapAnomalyDetector
{

  /// <summary>
  /// Re-derives the missed-read warnings for one rider.
  ///
  /// Laps the operator has explicitly kept are left alone - otherwise dismissing
  /// a warning would be undone by the next re-scan.
  /// </summary>
  public static void Analyze(RiderInfo rider, TimeSpan? globalAverageLapTime,
    LapAnomalySettings? settings = null)
  {
    var tuning = (settings ?? LapAnomalySettings.Default).Validated();

    foreach (var lap in rider.Laps)
    {
      if (lap.SuggestionDismissed) continue;

      lap.IsSuggestedForSplit = false;
      lap.SuggestedSplitCount = 0;
      lap.SuggestedSplitLapTime = null;
    }

    // The first lap runs from the start of the race and is not comparable.
    if (rider.Laps.Count < 2 + tuning.MinPriorLaps) return;

    for (var i = 2; i < rider.Laps.Count; i++)
    {
      var lap = rider.Laps[i];
      if (lap.SuggestionDismissed || lap.IsSplitLap || !lap.LapTime.HasValue) continue;

      var pace = RecentPaceBefore(rider, i, tuning);
      if (pace == null) continue;

      var ratio = lap.LapTime.Value.TotalMilliseconds / pace.Value.TotalMilliseconds;
      if (ratio < tuning.MinRatio || ratio > tuning.MaxRatio) continue;

      var missedLaps = (int)Math.Round(ratio);
      if (missedLaps < 2 || missedLaps > 5) continue;

      var splitLapTime = TimeSpan.FromMilliseconds(lap.LapTime.Value.TotalMilliseconds / missedLaps);

      // A "split" that produces laps far quicker than anyone is riding is more
      // likely a slow lap than a missed read.
      if (globalAverageLapTime.HasValue &&
          splitLapTime.TotalMilliseconds / globalAverageLapTime.Value.TotalMilliseconds < tuning.MinSplitToGlobalRatio)
      {
        continue;
      }

      lap.IsSuggestedForSplit = true;
      lap.SuggestedSplitCount = missedLaps;
      lap.SuggestedSplitLapTime = splitLapTime;
    }
  }

  /// <summary>
  /// The rider's pace over the most recent timed laps before <paramref name="index"/>,
  /// ignoring the first lap of the race and any lap already flagged as long.
  ///
  /// Excluding already-flagged laps is what makes detection cascade correctly:
  /// once a double-length lap is flagged it stops inflating the baseline, so the
  /// next one is measured against the rider's real pace.
  /// </summary>
  private static TimeSpan? RecentPaceBefore(RiderInfo rider, int index, LapAnomalySettings tuning)
  {
    var window = rider.Laps
      .Take(index)
      .Skip(1)
      .Where(l => l.LapTime.HasValue && !l.IsSuggestedForSplit)
      .TakeLast(tuning.PaceWindow)
      .ToList();

    if (window.Count < tuning.MinPriorLaps) return null;

    return TimeSpan.FromMilliseconds(window.Average(l => l.LapTime!.Value.TotalMilliseconds));
  }
}
