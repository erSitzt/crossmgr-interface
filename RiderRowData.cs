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
  public const int ColumnCount = 19;

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

  /// <summary>
  /// A team's rider on track. Created last so every index above stays put, and
  /// moved beside the name on screen; shown in team events only.
  /// </summary>
  public const int ColOnTrack = 17;

  /// <summary>
  /// When the loop last saw this rider, as a time of day. Added last for the
  /// same reason as ColOnTrack: every index above stays where it was.
  ///
  /// The other time columns are durations, which say nothing about when a
  /// rider stopped. Sorted on, this answers "who has not been seen lately",
  /// which is the question behind a result that looks wrong.
  /// </summary>
  public const int ColLastRead = 18;

  public string TagID { get; init; } = "";
  public string[] Cells { get; init; } = new string[ColumnCount];

  /// <summary>
  /// What each column sorts on, where its text would sort wrongly - lap 9
  /// before lap 10, "N/A" among the lap times, 1:05:00 before 59:00. Null
  /// means the cell text is a good enough key, and a null sorts last whichever
  /// way the column is pointing: a rider with no best lap has not beaten
  /// anyone.
  /// </summary>
  public IComparable?[] SortKeys { get; init; } = new IComparable?[ColumnCount];

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

/// <summary>
/// Puts the riders grid in the order the operator clicked for.
///
/// The rows arrive in race order and keep it as their tie-break, so sorting by
/// a column everyone shares - the class, say - leaves each class internally in
/// the order it is being raced, rather than shuffled by whatever the sort
/// happened to do with equal keys.
/// </summary>
public static class RiderRowSort
{
  /// <param name="rows">The rows in race order.</param>
  /// <param name="column">A column index from <see cref="RiderRowData"/>.</param>
  /// <param name="descending">Which way the operator pointed the column.</param>
  public static List<RiderRowData> By(List<RiderRowData> rows, int column, bool descending) =>
    rows
      .Select((row, index) => (Row: row, Index: index))
      .OrderBy(entry => entry, Comparer<(RiderRowData Row, int Index)>.Create((a, b) =>
      {
        var keyA = a.Row.SortKeys[column];
        var keyB = b.Row.SortKeys[column];

        int result;
        if (keyA != null || keyB != null)
        {
          // A rider with nothing in the column sorts last whichever way it
          // points: turning the column round is asking for the other end of
          // the field, not for everyone who has no time yet.
          if (keyA == null && keyB == null) result = 0;
          else if (keyA == null) return 1;
          else if (keyB == null) return -1;
          else result = keyA.CompareTo(keyB);
        }
        else
        {
          result = string.Compare(a.Row.Cells[column], b.Row.Cells[column],
            StringComparison.CurrentCultureIgnoreCase);
        }

        return result != 0 ? (descending ? -result : result) : a.Index.CompareTo(b.Index);
      }))
      .Select(entry => entry.Row)
      .ToList();
}
