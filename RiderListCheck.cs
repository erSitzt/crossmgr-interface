namespace CrossMgrInterface;

public enum RiderListSeverity
{
  Warning,
  Error
}

/// <summary>Something wrong with one row of the rider list; <see cref="Row"/> is its index in the list.</summary>
public sealed record RiderListProblem(int Row, RiderListSeverity Severity, string Message);

/// <summary>
/// What is wrong with a rider list, row by row, before anyone races on it.
///
/// The import only refuses rows without a transponder. Everything else went
/// through: a start number given twice, or one transponder on two riders -
/// where the last row silently won and the first rider's laps went to the
/// second. Both only showed on the results sheet. This finds them while there
/// is still time to phone the rider.
/// </summary>
public static class RiderListCheck
{
  public static IReadOnlyList<RiderListProblem> Run(
    IReadOnlyList<RiderDataImporter.RiderImportData> rows, bool teamEvent)
  {
    var problems = new List<RiderListProblem>();

    // A class is only missing when the list has classes at all: a club running
    // one class leaves the column out, and that is not a problem.
    var listHasClasses = rows.Any(r => r.Category.Trim().Length > 0);

    for (var i = 0; i < rows.Count; i++)
    {
      var row = rows[i];

      if (row.TagID.Trim().Length == 0)
        problems.Add(new(i, RiderListSeverity.Error, "No transponder - this row cannot be timed"));

      if (row.RiderNumber.Trim().Length == 0)
        problems.Add(new(i, RiderListSeverity.Warning, "No start number"));

      if (row.FullName.Length == 0)
        problems.Add(new(i, RiderListSeverity.Warning, "No name"));

      if (listHasClasses && row.Category.Trim().Length == 0)
        problems.Add(new(i, RiderListSeverity.Warning, "No class"));
    }

    // The same person on two rows - a spare transponder - is how a rider is
    // given a second tag, not a mistake. Only different people clash.
    foreach (var group in Clashes(rows, r => r.TagID.Trim().ToUpperInvariant(), teamEvent))
    {
      foreach (var i in group)
        problems.Add(new(i, RiderListSeverity.Error,
          $"Transponder also on {Others(rows, group, i)} - its laps would go to one of them only"));
    }

    foreach (var group in Clashes(rows, r => r.RiderNumber.Trim(), teamEvent))
    {
      foreach (var i in group)
        problems.Add(new(i, RiderListSeverity.Warning, $"Number {rows[i].RiderNumber.Trim()} also on {Others(rows, group, i)}"));
    }

    return problems;
  }

  /// <summary>"58 riders in 4 classes - 2 problems", counting rows with a problem rather than problems.</summary>
  public static string Summary(IReadOnlyList<RiderDataImporter.RiderImportData> rows,
    IReadOnlyList<RiderListProblem> problems)
  {
    var classes = rows
      .Select(r => r.Category.Trim())
      .Where(c => c.Length > 0)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .Count();

    var text = rows.Count == 1 ? "1 rider" : $"{rows.Count} riders";
    if (classes > 0) text += classes == 1 ? " in 1 class" : $" in {classes} classes";

    var withProblems = problems.Select(p => p.Row).Distinct().Count();
    return text + (withProblems switch
    {
      0 => " - no problems found",
      1 => " - 1 rider needs a look",
      _ => $" - {withProblems} riders need a look"
    });
  }

  /// <summary>
  /// Rows sharing a value, grouped, where at least two different people share it.
  /// In a team event, team mates sharing a transponder or a number is the team
  /// roster's business: sharing one tag is how a team is entered.
  /// </summary>
  private static IEnumerable<List<int>> Clashes(
    IReadOnlyList<RiderDataImporter.RiderImportData> rows,
    Func<RiderDataImporter.RiderImportData, string> value, bool teamEvent)
  {
    return Enumerable.Range(0, rows.Count)
      .Where(i => value(rows[i]).Length > 0)
      .GroupBy(i => value(rows[i]), StringComparer.OrdinalIgnoreCase)
      .Select(g => g.ToList())
      .Where(g => g.Select(i => Person(rows[i])).Distinct().Count() > 1)
      .Where(g => !(teamEvent && SameTeam(rows, g)));
  }

  private static bool SameTeam(IReadOnlyList<RiderDataImporter.RiderImportData> rows, List<int> group)
  {
    var teams = group.Select(i => TeamRoster.NormaliseName(rows[i].Team).ToUpperInvariant()).Distinct().ToList();
    return teams.Count == 1 && teams[0].Length > 0;
  }

  /// <summary>Who a row is. The number is left out, so "number given twice" can find two people.</summary>
  private static string Person(RiderDataImporter.RiderImportData row) =>
    TeamRoster.NormaliseName(row.FullName).ToUpperInvariant();

  private static string Others(IReadOnlyList<RiderDataImporter.RiderImportData> rows, List<int> group, int self) =>
    string.Join(", ", group.Where(i => i != self && Person(rows[i]) != Person(rows[self])).Select(i => Describe(rows[i])));

  /// <summary>"#14 Ben Fischer", or whatever the row has.</summary>
  public static string Describe(RiderDataImporter.RiderImportData row)
  {
    var number = row.RiderNumber.Trim();
    var name = row.FullName;
    if (number.Length > 0 && name.Length > 0) return $"#{number} {name}";
    if (number.Length > 0) return $"#{number}";
    return name.Length > 0 ? name : "an unnamed row";
  }
}
