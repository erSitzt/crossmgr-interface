namespace CrossMgrInterface;

/// <summary>
/// How hard the app looks for a missed transponder read.
///
/// These were fixed constants until the transponder check went in and showed
/// them missing a pair of clean double-length laps. They are tunable because
/// the right answer depends on the circuit: a two-minute motocross lap and a
/// thirty-second supercross lap do not need the same sensitivity, and neither
/// does a club meeting with one loop versus a meeting with a spare.
///
/// Every value is compared against <see cref="Original"/> in the settings
/// dialog, so it is always visible which of them have been moved and what they
/// used to be.
/// </summary>
public sealed record LapAnomalySettings
{
  /// <summary>
  /// How much longer than the rider's recent pace a lap has to be before it
  /// looks like a read was missed. 1.8 means "almost twice as long".
  ///
  /// Lower catches more missed reads and starts mistaking bad laps for them -
  /// a rider who tips off and remounts can genuinely lose this much time.
  /// </summary>
  public double MinRatio { get; init; } = 1.8;

  /// <summary>
  /// Above this it is not a missed read but a rider who stopped, so it is left
  /// alone rather than split into five imaginary laps.
  /// </summary>
  public double MaxRatio { get; init; } = 5.5;

  /// <summary>
  /// How many of the rider's own timed laps are needed before their pace is
  /// worth comparing against.
  ///
  /// Originally two, which had a consequence nobody intended: the out-lap does
  /// not count, so a rider's third crossing was the earliest that could ever be
  /// checked - and a read missed before that was undetectable. Worse, it
  /// cascaded: an unflagged long lap stays in the pace window and drags the
  /// baseline up, hiding the next one too.
  /// </summary>
  public int MinPriorLaps { get; init; } = 1;

  /// <summary>How many recent laps the pace is averaged over.</summary>
  public int PaceWindow { get; init; } = 5;

  /// <summary>
  /// A split that would produce laps this much quicker than the field average
  /// is rejected - more likely a slow lap than a missed read.
  /// </summary>
  public double MinSplitToGlobalRatio { get; init; } = 0.5;

  /// <summary>What the app used before any of this was configurable.</summary>
  public static readonly LapAnomalySettings Original = new()
  {
    MinRatio = 1.8,
    MaxRatio = 5.5,
    MinPriorLaps = 2,
    PaceWindow = 5,
    MinSplitToGlobalRatio = 0.5
  };

  /// <summary>What the app ships with now.</summary>
  public static LapAnomalySettings Default => new();

  /// <summary>
  /// Keeps a hand-edited settings file from quietly disabling detection.
  ///
  /// A value outside its plausible range falls back to the shipped one rather
  /// than being squeezed to the nearest end. Clamping was the first attempt and
  /// was worse: a nonsense pair collapsed into a band a fraction of a lap wide,
  /// which flagged nothing at all and was indistinguishable from a detector
  /// that had stopped working.
  /// </summary>
  public LapAnomalySettings Validated()
  {
    var min = MinRatio is >= 1.1 and <= 5.0 ? MinRatio : Default.MinRatio;

    var max = MaxRatio >= min + 0.5 && MaxRatio <= 20.0
      ? MaxRatio
      : Math.Max(min + 3.0, Default.MaxRatio);

    return new LapAnomalySettings
    {
      MinRatio = min,
      MaxRatio = max,
      MinPriorLaps = MinPriorLaps is >= 1 and <= 10 ? MinPriorLaps : Default.MinPriorLaps,
      PaceWindow = PaceWindow is >= 1 and <= 20 ? PaceWindow : Default.PaceWindow,
      MinSplitToGlobalRatio = MinSplitToGlobalRatio is >= 0.0 and <= 1.0
        ? MinSplitToGlobalRatio
        : Default.MinSplitToGlobalRatio
    };
  }
}
