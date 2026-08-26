namespace CrossMgrInterface;

/// <summary>One prepared row of the transponder check grid. Mirrors
/// <see cref="QualifyingRowData"/>; the grid runs in virtual mode.</summary>
public sealed class TransponderCheckRowData
{
  public const int ColumnCount = 7;

  public const int ColRiderNumber = 0;
  public const int ColRiderName = 1;
  public const int ColCategory = 2;
  public const int ColLaps = 3;
  public const int ColMisses = 4;
  public const int ColDuplicates = 5;
  public const int ColDetail = 6;

  public string TagID { get; init; } = "";
  public string[] Cells { get; init; } = new string[ColumnCount];

  public Color RowBackColor { get; init; } = Color.Empty;
  public Color RowForeColor { get; init; } = Color.Empty;
  public bool NeedsAttention { get; init; }

  /// <summary>What to do about this verdict, shown on hover.</summary>
  public string Tooltip { get; init; } = "";
}
