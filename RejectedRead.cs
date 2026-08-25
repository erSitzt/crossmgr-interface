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

  /// <summary>Set once an operator has put this read back as a lap.</summary>
  public bool Restored { get; set; }
}
