namespace CrossMgrInterface;

/// <summary>
/// What the timing loop saw of one rider's transponder, worst first.
/// </summary>
public enum TransponderVerdict
{
  /// <summary>Every lap accounted for, nothing rejected.</summary>
  Clean,

  /// <summary>The same tag read more than once in a pass.</summary>
  DoubleReads,

  /// <summary>Laps missing in the middle - long laps that are near multiples of their pace.</summary>
  Intermittent,

  /// <summary>Read for a while, then nothing while the rest of the field was still circulating.</summary>
  WentQuiet,

  /// <summary>Never seen at all.</summary>
  NeverRead
}

/// <summary>One rider's line on the transponder check.</summary>
public sealed record TransponderFinding
{
  public required RiderInfo Rider { get; init; }
  public TransponderVerdict Verdict { get; init; }
  public int Laps { get; init; }

  /// <summary>Reads the detector thinks were missed, from the length of long laps.</summary>
  public int SuspectedMisses { get; init; }

  /// <summary>Reads thrown away as too close to the previous one to be a lap.</summary>
  public int DuplicateReads { get; init; }

  /// <summary>How long they were silent before the rest of the field stopped.</summary>
  public TimeSpan? QuietFor { get; init; }

  /// <summary>Share of their expected laps that appear to be missing, 0 to 1.</summary>
  public double MissRate { get; init; }

  /// <summary>One sentence for the sheet.</summary>
  public string Detail { get; init; } = "";
}

/// <summary>
/// Finds riders whose transponder is not being read reliably, so they can be
/// told to re-fit it while there is still time to matter.
///
/// Practice is when this is worth doing: a tag that is never read costs a rider
/// nothing in practice and their whole result in the race.
///
/// One honest limit runs through all of this. The timing loop sees crossings and
/// nothing else, so it cannot tell a tag that stopped working from a rider who
/// came in early - both look like silence. Everything here is therefore reported
/// as an observation for someone to check, never as a diagnosis, and the
/// "went quiet" test is measured against the rest of the field rather than
/// against the clock so that a session where everybody comes in together flags
/// nobody.
/// </summary>
public static class TransponderCheck
{
  /// <summary>
  /// How far behind the field's last crossing a rider has to fall silent before
  /// it is worth asking about. Three laps, so a rider who simply finished a
  /// little earlier than the rest is not accused of a faulty tag.
  /// </summary>
  private const double QuietAfterLaps = 3.0;

  public static List<TransponderFinding> Run(
    IEnumerable<RiderInfo> field,
    IReadOnlyDictionary<string, int> duplicateReads,
    TimeSpan? fieldPace)
  {
    var riders = field as IReadOnlyList<RiderInfo> ?? field.ToList();

    // The last time anything crossed the loop. Using this rather than the end of
    // the session is what stops a normal early finish reading as a fault.
    DateTime? lastActivity = null;
    foreach (var rider in riders)
    {
      if (rider.TotalLaps == 0) continue;
      if (lastActivity is null || rider.LastCrossing > lastActivity) lastActivity = rider.LastCrossing;
    }

    var findings = new List<TransponderFinding>(riders.Count);

    foreach (var rider in riders)
    {
      var duplicates = duplicateReads.TryGetValue(rider.TagID, out var d) ? d : 0;
      var misses = CountSuspectedMisses(rider);
      var laps = rider.TotalLaps;

      // Expected laps is what they did plus what appears to be missing, so the
      // rate answers "what share of this rider's passes did the loop see?"
      var expected = laps + misses;
      var missRate = expected > 0 ? (double)misses / expected : 0;

      TimeSpan? quietFor = null;
      if (laps > 0 && lastActivity.HasValue)
      {
        var quiet = lastActivity.Value - rider.LastCrossing;
        if (quiet > TimeSpan.Zero) quietFor = quiet;
      }

      var pace = TrackPositionSolver.UsablePace(rider.RacingPace)
                 ?? TrackPositionSolver.UsablePace(fieldPace);

      var wentQuiet = laps > 0
                      && quietFor.HasValue
                      && pace.HasValue
                      && quietFor.Value > pace.Value * QuietAfterLaps;

      var verdict = laps == 0 ? TransponderVerdict.NeverRead
        : wentQuiet ? TransponderVerdict.WentQuiet
        : misses > 0 ? TransponderVerdict.Intermittent
        : duplicates > 0 ? TransponderVerdict.DoubleReads
        : TransponderVerdict.Clean;

      findings.Add(new TransponderFinding
      {
        Rider = rider,
        Verdict = verdict,
        Laps = laps,
        SuspectedMisses = misses,
        DuplicateReads = duplicates,
        QuietFor = quietFor,
        MissRate = missRate,
        Detail = Describe(verdict, laps, misses, duplicates, quietFor, missRate)
      });
    }

    // Worst first, then by start number, so the riders to go and find are at the
    // top of the sheet.
    return findings
      .OrderByDescending(f => (int)f.Verdict)
      .ThenByDescending(f => f.MissRate)
      .ThenBy(f => StartNumber(f.Rider))
      .ToList();
  }

  /// <summary>
  /// Reads the detector believes went unseen. A lap flagged as three laps' worth
  /// means two reads were missed, not one.
  /// </summary>
  private static int CountSuspectedMisses(RiderInfo rider)
  {
    var misses = 0;
    foreach (var lap in rider.Laps)
    {
      if (!lap.IsSuggestedForSplit) continue;
      misses += lap.SuggestedSplitCount > 1 ? lap.SuggestedSplitCount - 1 : 1;
    }
    return misses;
  }

  /// <summary>
  /// What the loop saw, as short as it can honestly be put.
  ///
  /// Facts only. The advice that goes with each verdict is the same for every
  /// rider who has it, so it belongs in a legend under the table rather than
  /// repeated down a column - which is also what kept the column too wide to
  /// fit on a page.
  /// </summary>
  private static string Describe(TransponderVerdict verdict, int laps, int misses,
    int duplicates, TimeSpan? quietFor, double missRate) => verdict switch
  {
    TransponderVerdict.NeverRead => "no reads at all",

    TransponderVerdict.WentQuiet => $"{Laps(laps)}, then nothing for {Format(quietFor)}",

    TransponderVerdict.Intermittent =>
      $"{Laps(laps)}, {misses} missed ({missRate:P0})" +
      (duplicates > 0 ? $", {duplicates} double" : ""),

    TransponderVerdict.DoubleReads => $"{Laps(laps)}, {duplicates} double {Reads(duplicates)}",

    _ => $"{Laps(laps)}, read cleanly"
  };

  /// <summary>
  /// What to do about a verdict. One line per verdict, for the legend under the
  /// sheet and for the tooltip on the tab.
  /// </summary>
  public static string Advice(TransponderVerdict verdict) => verdict switch
  {
    TransponderVerdict.NeverRead =>
      "Never seen. Check the tag is fitted, live, and somewhere the loop can see it.",
    TransponderVerdict.WentQuiet =>
      "Read, then silent while the rest rode on. Ask whether they pulled in or the tag stopped.",
    TransponderVerdict.Intermittent =>
      "Laps missing in the middle. Usually the tag is mounted too high, too low, or shielded by metal.",
    TransponderVerdict.DoubleReads =>
      "Read twice in one pass. Usually the tag sits where it crosses the loop twice.",
    _ => "Every expected lap accounted for."
  };

  private static string Laps(int laps) => laps == 1 ? "1 lap" : $"{laps} laps";
  private static string Reads(int n) => n == 1 ? "read" : "reads";

  private static string Format(TimeSpan? value) =>
    value.HasValue ? value.Value.ToString(@"m\:ss") : "-";

  /// <summary>Start numbers are free text, so #7 must not sort after #12.</summary>
  private static (int Rank, int Number, string Text) StartNumber(RiderInfo rider) =>
    int.TryParse(rider.RiderNumber, out var n)
      ? (0, n, "")
      : (1, 0, rider.RiderNumber ?? "");
}
