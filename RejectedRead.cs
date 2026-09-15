namespace CrossMgrInterface;

/// <summary>
/// A transponder read that was not counted as a lap.
///
/// These used to vanish into a log line. Sometimes a rejected read is a genuine
/// lap on a short course, so the operator needs to be able to see them and put
/// one back.
/// </summary>
public sealed class RejectedRead
{
  public string TagID { get; init; } = "";
  public DateTime CrossingTime { get; init; }

  /// <summary>Gap to the rider's previous crossing.</summary>
  public TimeSpan GapToPrevious { get; init; }

  public string Reason { get; init; } = "";

  /// <summary>
  /// Read while the rider was marked DNF, rather than too soon after a lap. Not a
  /// double read, so the transponder check leaves it out of that count.
  /// </summary>
  public bool WhileDnf { get; init; }

  /// <summary>The transponder that was read, when TagID names a team. See <see cref="RiderLap.CrossedBy"/>.</summary>
  public string? CrossedBy { get; init; }

  /// <summary>
  /// Whether an operator has put this read back as a lap.
  ///
  /// Worked out from the rider's laps rather than remembered in a flag. The flag
  /// was set when Count this read was pressed and nothing cleared it on undo, so
  /// an undone read vanished from Fix laps for good.
  /// </summary>
  public bool IsCountedIn(RiderInfo? rider) =>
    rider != null && rider.Laps.Any(l => l.Source == LapSource.RestoredShortRead && l.CrossingTime == CrossingTime);
}
