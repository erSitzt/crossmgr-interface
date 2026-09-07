namespace CrossMgrInterface;

/// <summary>
/// How a duration is written wherever a person reads one.
///
/// A TimeSpan formatted "mm:ss" shows only the minutes within the hour, so
/// 2:05:12 came out as "05:12" - on the results sheet, in the riders grid, on
/// the status bar. Nobody saw it because every motocross race is under an
/// hour. An enduro is not. These add the hours exactly when there are any,
/// so a short race still reads the way it always has.
/// </summary>
public static class TimeFormat
{
  /// <summary>"mm:ss.fff", or "h:mm:ss.fff" from an hour on. Lap and total times.</summary>
  public static string Precise(TimeSpan value) =>
    value.Duration().TotalHours >= 1 ? value.ToString(@"h\:mm\:ss\.fff") : value.ToString(@"mm\:ss\.fff");

  public static string Precise(TimeSpan? value, string fallback) =>
    value.HasValue ? Precise(value.Value) : fallback;

  /// <summary>"mm:ss.f", or "h:mm:ss.f" from an hour on. Race time at a crossing.</summary>
  public static string Tenths(TimeSpan value) =>
    value.Duration().TotalHours >= 1 ? value.ToString(@"h\:mm\:ss\.f") : value.ToString(@"mm\:ss\.f");

  /// <summary>"mm:ss", or "h:mm:ss" from an hour on. Clocks, countdowns, durations.</summary>
  public static string Clock(TimeSpan value) =>
    value.Duration().TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"mm\:ss");
}
