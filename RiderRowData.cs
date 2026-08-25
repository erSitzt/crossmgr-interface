namespace CrossMgrInterface;

/// <summary>
/// One prepared row of the riders grid: the cell text, and the few pieces of
/// state the painter needs to colour it.
///
/// The grid runs in virtual mode, so rows are never written into the control.
/// Building a plain list like this costs a couple of milliseconds even for a
/// 250-rider field, whereas pushing the same data through DataGridView cells
/// took the better part of a second.
/// </summary>
public sealed class RiderRowData
{
  /// <summary>Number of columns in the riders grid.</summary>
  public const int ColumnCount = 17;

  // Column indices, matching the order the columns are created in.
  public const int ColPosition = 0;
  public const int ColStatus = 1;
  public const int ColProjectedPosition = 2;
  public const int ColRiderNumber = 3;
  public const int ColTagID = 4;
  public const int ColRiderName = 5;
  public const int ColTeam = 6;
  public const int ColCategory = 7;
  public const int ColLaps = 8;
  public const int ColLastLap = 9;
  public const int ColBestLap = 10;
  public const int ColAvgLap = 11;
  public const int ColPredictedLap = 12;
  public const int ColNextCrossing = 13;
  public const int ColTimeToNext = 14;
  public const int ColTotalTime = 15;
  public const int ColGap = 16;

  public string TagID { get; init; } = "";
  public string[] Cells { get; init; } = new string[ColumnCount];

  public string StatusText { get; init; } = "";
  public string StatusTooltip { get; init; } = "";

  /// <summary>Podium or DNF shading for the whole row.</summary>
  public Color RowBackColor { get; init; } = Color.Empty;
  public Color RowForeColor { get; init; } = Color.Empty;

  public bool IsDnf { get; init; }

  /// <summary>The rider has not appeared when they were expected.</summary>
  public bool IsOverdue { get; init; }

  /// <summary>Applying the flagged split would move this rider up, or down.</summary>
  public bool ProjectedImproves { get; init; }
  public bool ProjectedDeclines { get; init; }
}
