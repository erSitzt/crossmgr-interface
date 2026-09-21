using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// The order the Riders tab is read in, when the operator wants something
/// other than the race order.
///
/// Sorting on the cell text alone would put lap 10 before lap 9 and file "N/A"
/// among the lap times, so the columns that need it carry a typed key. The
/// cases below are the ones that make a sorted grid trustworthy: a rider with
/// nothing in the column never floats to the top just because the column was
/// turned round, and riders level on the column stay in race order rather than
/// swapping places on every refresh.
/// </summary>
public class RiderRowSortTests
{
  /// <summary>A row as the grid builds it: the text, and the key it sorts on.</summary>
  private static RiderRowData Row(string tag, string cell, IComparable? key, int column)
  {
    var cells = new string[RiderRowData.ColumnCount];
    var keys = new IComparable?[RiderRowData.ColumnCount];
    cells[column] = cell;
    keys[column] = key;
    return new RiderRowData { TagID = tag, Cells = cells, SortKeys = keys };
  }

  private static string[] Order(List<RiderRowData> rows, int column, bool descending) =>
    RiderRowSort.By(rows, column, descending).Select(r => r.TagID).ToArray();

  [Fact]
  public void LapsSortAsNumbersRatherThanAsText()
  {
    // The bug this guards: "10" sorts before "9" as text, so the rider a lap
    // up would be shown a lap down.
    const int col = RiderRowData.ColLaps;
    var rows = new List<RiderRowData>
    {
      Row("A", "9", 9, col),
      Row("B", "10", 10, col),
      Row("C", "2", 2, col)
    };

    Assert.Equal(new[] { "C", "A", "B" }, Order(rows, col, descending: false));
    Assert.Equal(new[] { "B", "A", "C" }, Order(rows, col, descending: true));
  }

  [Fact]
  public void ARiderWithNoTimeIsLastWhicheverWayTheColumnPoints()
  {
    // Turning Best Lap round asks for the slowest rider who set one, not for
    // everyone who never set one.
    const int col = RiderRowData.ColBestLap;
    var rows = new List<RiderRowData>
    {
      Row("QUICK", "1:02.000", TimeSpan.FromSeconds(62), col),
      Row("NONE", "N/A", null, col),
      Row("SLOW", "1:20.000", TimeSpan.FromSeconds(80), col)
    };

    Assert.Equal(new[] { "QUICK", "SLOW", "NONE" }, Order(rows, col, descending: false));
    Assert.Equal(new[] { "SLOW", "QUICK", "NONE" }, Order(rows, col, descending: true));
  }

  [Fact]
  public void RidersLevelOnTheColumnStayInRaceOrder()
  {
    // The grid rebuilds about once a second. Without a stable tie-break, every
    // rider in the same class would swap places each time it did.
    const int col = RiderRowData.ColCategory;
    var rows = new List<RiderRowData>
    {
      Row("P1", "MX1", null, col),
      Row("P2", "MX1", null, col),
      Row("P3", "MX1", null, col)
    };

    Assert.Equal(new[] { "P1", "P2", "P3" }, Order(rows, col, descending: false));

    // Even reversed: the column is equal for all three, so nothing reverses.
    Assert.Equal(new[] { "P1", "P2", "P3" }, Order(rows, col, descending: true));
  }

  [Fact]
  public void LastReadBringsWhoeverStoppedEarliestToTheTop()
  {
    // Why the column exists. In Lauf2 of 20.09.2026 a rider was scored among
    // the finishers whose last read was twenty minutes before the flag; this
    // is the sort that would have shown that at a glance.
    const int col = RiderRowData.ColLastRead;
    var day = new DateTime(2026, 9, 20);
    var rows = new List<RiderRowData>
    {
      Row("LATE", "15:08:25", day.AddHours(15).AddMinutes(8).AddSeconds(25), col),
      Row("STOPPED", "15:02:00", day.AddHours(15).AddMinutes(2), col),
      Row("NEVER", "-", null, col),
      Row("FLAG", "15:05:40", day.AddHours(15).AddMinutes(5).AddSeconds(40), col)
    };

    // Earliest first, and the rider who never crossed at all is last.
    Assert.Equal(new[] { "STOPPED", "FLAG", "LATE", "NEVER" }, Order(rows, col, descending: false));
  }

  [Fact]
  public void AColumnWithNoTypedKeySortsOnItsText()
  {
    // Names, teams and classes have nothing better to sort on, and should not
    // need a key invented for them.
    const int col = RiderRowData.ColRiderName;
    var rows = new List<RiderRowData>
    {
      Row("C", "Weber, Alexander", null, col),
      Row("A", "Baum, Tobias", null, col),
      Row("B", "Keller, Paul", null, col)
    };

    Assert.Equal(new[] { "A", "B", "C" }, Order(rows, col, descending: false));
    Assert.Equal(new[] { "C", "B", "A" }, Order(rows, col, descending: true));
  }
}
