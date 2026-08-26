namespace CrossMgrInterface;

/// <summary>
/// One prepared row of the qualifying grid: the cell text, and the little state
/// the painter needs to colour it.
///
/// Mirrors <see cref="RiderRowData"/>, and for the same reason - the grid runs
/// in virtual mode, so rows are never written into the control.
/// </summary>
public sealed class QualifyingRowData
{
  /// <summary>Number of columns in the qualifying grid.</summary>
  public const int ColumnCount = 10;

  // Column indices, matching the order the columns are created in.
  public const int ColGatePick = 0;
  public const int ColRiderNumber = 1;
  public const int ColRiderName = 2;
  public const int ColCategory = 3;
  public const int ColBestLap = 4;
  public const int ColGap = 5;
  public const int ColInterval = 6;
  public const int ColOnLap = 7;
  public const int ColLaps = 8;
  public const int ColStatus = 9;

  public string TagID { get; init; } = "";
  public string[] Cells { get; init; } = new string[ColumnCount];

  /// <summary>Podium shading for the top three, grey for riders without a time.</summary>
  public Color RowBackColor { get; init; } = Color.Empty;
  public Color RowForeColor { get; init; } = Color.Empty;

  /// <summary>Set when the rider's laps carry an unresolved missed-read suggestion.</summary>
  public bool NeedsCheck { get; init; }

  public string Tooltip { get; init; } = "";
}
