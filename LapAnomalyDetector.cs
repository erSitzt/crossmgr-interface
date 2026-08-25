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
  /// <summary>A long lap is worth flagging between these multiples of the rider's pace.</summary>
  private const double MinRatio = 1.8;
  private const double MaxRatio = 5.5;

  /// <summary>Refuse to suggest a split that would produce implausibly short laps.</summary>
  private const double MinSplitToGlobalRatio = 0.5;

  /// <summary>
  /// Re-derives the missed-read warnings for one rider.
  ///
  /// Laps the operator has explicitly kept are left alone - otherwise dismissing
  /// a warning would be undone by the next re-scan.
  /// </summary>
  public static void Analyze(RiderInfo rider, TimeSpan? globalAverageLapTime)
  {
    foreach (var lap in rider.Laps)
    {
      if (lap.SuggestionDismissed) continue;

      lap.IsSuggestedForSplit = false;
      lap.SuggestedSplitCount = 0;
      lap.SuggestedSplitLapTime = null;
    }

    // The first lap runs from the start of the race and is not comparable.
    if (rider.Laps.Count < 3) return;

    for (var i = 2; i < rider.Laps.Count; i++)
    {
      var lap = rider.Laps[i];
      if (lap.SuggestionDismissed || lap.IsSplitLap || !lap.LapTime.HasValue) continue;

      var pace = RecentPaceBefore(rider, i);
      if (pace == null) continue;

      var ratio = lap.LapTime.Value.TotalMilliseconds / pace.Value.TotalMilliseconds;
      if (ratio < MinRatio || ratio > MaxRatio) continue;

      var missedLaps = (int)Math.Round(ratio);
      if (missedLaps < 2 || missedLaps > 5) continue;

      var splitLapTime = TimeSpan.FromMilliseconds(lap.LapTime.Value.TotalMilliseconds / missedLaps);

      // A "split" that produces laps far quicker than anyone is riding is more
      // likely a slow lap than a missed read.
      if (globalAverageLapTime.HasValue &&
          splitLapTime.TotalMilliseconds / globalAverageLapTime.Value.TotalMilliseconds < MinSplitToGlobalRatio)
      {
        continue;
      }

      lap.IsSuggestedForSplit = true;
      lap.SuggestedSplitCount = missedLaps;
      lap.SuggestedSplitLapTime = splitLapTime;
    }
  }

  /// <summary>
  /// The rider's pace over the up-to-five timed laps before <paramref name="index"/>,
  /// ignoring the first lap of the race and any lap already flagged as long.
  /// </summary>
  private static TimeSpan? RecentPaceBefore(RiderInfo rider, int index)
  {
    var window = rider.Laps
      .Take(index)
      .Skip(1)
      .Where(l => l.LapTime.HasValue && !l.IsSuggestedForSplit)
      .TakeLast(5)
      .ToList();

    if (window.Count < 2) return null;

    return TimeSpan.FromMilliseconds(window.Average(l => l.LapTime!.Value.TotalMilliseconds));
  }
}
