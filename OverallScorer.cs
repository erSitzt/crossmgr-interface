namespace CrossMgrInterface;

/// <summary>
/// How an overall is scored: the points each moto place is worth.
///
/// The default is the table FIM (Art. 5.35) and the DMSB motocross rules
/// (8.8) both use, so an overall nobody has touched agrees with how the club
/// has always added it up by hand.
/// </summary>
public sealed class OverallRules
{
  /// <summary>FIM motocross: 25, 22, 20, 18, 16, then one fewer per place down to 1 for 20th.</summary>
  public static readonly IReadOnlyList<int> FimPoints =
    new[] { 25, 22, 20, 18, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 };

  public IReadOnlyList<int> Points { get; init; } = FimPoints;

  public static OverallRules FromOverall(DbOverall overall) => new()
  {
    Points = overall.PointsTable is { Count: > 0 } table ? table : FimPoints
  };

  public int PointsFor(int position) =>
    position >= 1 && position <= Points.Count ? Points[position - 1] : 0;

  public bool IsFim => Points.SequenceEqual(FimPoints);

  /// <summary>"25, 22, 20, ..." as typed into and read back from the dialog.</summary>
  public string PointsText => string.Join(", ", Points);

  /// <summary>
  /// Reads a points table as typed: numbers separated by commas, spaces or
  /// semicolons. Null when there is nothing usable, so a slip of the finger
  /// cannot quietly score the day on an empty table.
  /// </summary>
  public static List<int>? ParsePoints(string text)
  {
    var parts = text.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    var points = new List<int>();
    foreach (var part in parts)
    {
      if (!int.TryParse(part, out var value) || value < 0) return null;
      points.Add(value);
    }
    return points.Count > 0 ? points : null;
  }

  /// <summary>Caption/value pairs printed under the table and sent to the website.</summary>
  public List<(string Caption, string Value)> Describe() => new()
  {
    ("Points", (IsFim ? "FIM / DMSB table - " : "") + PointsText),
    ("Places", "as on each moto's class sheet: riders who took the flag, then riders who retired, " +
               "by laps completed - a rider who retired keeps the points of their place; DNS scores nothing"),
    ("Ties", "on points, the better place in the last moto; without points, the lower sum of places")
  };
}

/// <summary>One moto as the overall sees it: its riders, as stored, and how it was scored.</summary>
public sealed record OverallMoto(
  string Title,
  Dictionary<string, RiderInfo> Riders,
  RaceRules Rules,
  DateTime? Start = null,
  DateTime? End = null,
  bool Finished = true);

/// <summary>A rider's result in one moto of the overall.</summary>
public sealed class OverallMotoCell
{
  /// <summary>Place in the class, or 0 for no place: DNS, not entered, or out before a lap was done.</summary>
  public int Position { get; init; }

  public int Points { get; init; }

  /// <summary>
  /// "3"; "12 DNF" for a rider who retired but keeps their place; or why there
  /// is no place: DNF (no lap done), DNS, or "-" for not entered.
  /// </summary>
  public string Text { get; init; } = "-";

  public bool Placed => Position > 0;

  public static readonly OverallMotoCell Absent = new();
}

public sealed class OverallEntry
{
  /// <summary>Place in the overall, or null for a rider placed in no moto at all.</summary>
  public int? Rank { get; set; }

  public string Number { get; init; } = "";
  public string Name { get; init; } = "";
  public string Team { get; init; } = "";
  public string Category { get; init; } = "";
  public bool IsTeam { get; init; }

  /// <summary>The rider as entered in the latest moto they rode, so the website can name them as they agreed.</summary>
  public RiderInfo? Rider { get; init; }

  /// <summary>One cell per moto, in moto order.</summary>
  public List<OverallMotoCell> Motos { get; init; } = new();

  public int Points => Motos.Sum(m => m.Points);

  /// <summary>Something the organiser should check before handing the sheet out.</summary>
  public string? Warning { get; init; }

  public string RankText => Rank?.ToString() ?? "-";
}

public sealed class OverallClass
{
  /// <summary>The class name; empty for riders entered without one.</summary>
  public string Name { get; init; } = "";
  public List<OverallEntry> Entries { get; init; } = new();
}

public sealed class OverallResult
{
  public string Title { get; init; } = "";
  public IReadOnlyList<string> MotoTitles { get; init; } = Array.Empty<string>();
  public OverallRules Rules { get; init; } = new();
  public List<OverallClass> Classes { get; init; } = new();
  public DateTime GeneratedAt { get; init; } = DateTime.Now;

  /// <summary>Motos whose clock has not finished: the overall is provisional until they have.</summary>
  public IReadOnlyList<string> UnfinishedMotos { get; init; } = Array.Empty<string>();

  public bool TeamEvent { get; init; }
}

/// <summary>
/// Adds up the motos of a day into one overall, the way the club has been
/// doing by hand: per class, points by place in each moto, highest total wins.
///
/// Each moto is placed exactly as its own class sheet places it (see
/// <see cref="RaceReportGenerator.PrepareReportData"/>), so the overall can
/// never disagree with the sheets already handed out. That order is also the
/// one the rulebooks give (DMSB 8.8, FIM 5.34): riders who took the flag, then
/// riders who retired by laps completed - and a retired rider scores the
/// points of that place. There is deliberately no minimum share of the
/// winner's laps; neither rulebook has one for a motocross moto.
///
/// Riders are matched between motos by transponder - each rider has their
/// own for the day - and a rider on a spare one by start number and name.
/// See <see cref="EntryFor"/>.
/// </summary>
public static class OverallScorer
{
  public static OverallResult Score(string title, IReadOnlyList<OverallMoto> motos, OverallRules rules)
  {
    var generator = new RaceReportGenerator();
    var entries = new List<EntryBuilder>();

    for (var m = 0; m < motos.Count; m++)
    {
      var moto = motos[m];
      var classes = moto.Riders.Values.Select(r => r.Category ?? "").Distinct();

      foreach (var className in classes)
      {
        var classRiders = moto.Riders
          .Where(kvp => (kvp.Value.Category ?? "") == className)
          .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        var sheet = generator.PrepareReportData(classRiders, moto.Start, moto.End,
          moto.Rules.Duration, moto.Finished, moto.Title, rules: moto.Rules);

        ScoreClass(sheet.RiderResults, classRiders, m, motos.Count, rules, entries);
      }
    }

    var result = new OverallResult
    {
      Title = title,
      MotoTitles = motos.Select(m => m.Title).ToList(),
      Rules = rules,
      UnfinishedMotos = motos.Where(m => !m.Finished).Select(m => m.Title).ToList(),
      TeamEvent = motos.Any(m => m.Rules.TeamEvent)
    };

    NoteSharedNumbers(entries);

    foreach (var group in entries.GroupBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
               .OrderBy(g => g.Key.Length == 0 ? 1 : 0).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
    {
      result.Classes.Add(new OverallClass { Name = group.Key, Entries = Rank(group.Select(e => e.Build())) });
    }

    return result;
  }

  private static void ScoreClass(List<RiderResult> sheet, Dictionary<string, RiderInfo> riders, int moto,
    int motoCount, OverallRules rules, List<EntryBuilder> entries)
  {
    var tagsInMoto = sheet.Select(r => r.TagID).ToHashSet(StringComparer.OrdinalIgnoreCase);

    var place = 0;
    foreach (var row in sheet)
    {
      // A rider who never completed a lap did not take part in the moto in any
      // sense the sheet can show, so gets no place rather than the last one.
      var placed = !row.IsDNS && row.TotalLaps > 0;

      OverallMotoCell cell;
      if (placed)
      {
        place++;
        cell = new OverallMotoCell
        {
          Position = place,
          Points = rules.PointsFor(place),
          Text = row.IsDNF ? $"{place} DNF" : place.ToString()
        };
      }
      else
      {
        cell = new OverallMotoCell { Text = row.IsDNS ? "DNS" : "DNF" };
      }

      riders.TryGetValue(row.TagID, out var rider);
      var entry = EntryFor(entries, row, moto, tagsInMoto);
      if (entry == null)
      {
        entry = new EntryBuilder(motoCount) { Category = row.Category ?? "" };
        entries.Add(entry);
      }
      entry.Add(moto, cell, row, rider);
    }
  }

  /// <summary>
  /// The entry this row continues from an earlier moto of the class, or null
  /// for a rider not seen before.
  ///
  /// On a race day each rider has their own transponder, so the transponder
  /// says who it is; a team is followed by its team key the same way. A rider
  /// whose transponder failed rides the next moto on a spare, and is found by
  /// start number and name instead - but only while their old transponder is
  /// not out in this moto too. The number alone is not enough: at a club day it
  /// can be on two riders. On 31 October 2026 two beginners were both #231,
  /// matching by number put the second over the first, and a rider on the
  /// podium of both motos was missing from the overall.
  /// </summary>
  private static EntryBuilder? EntryFor(List<EntryBuilder> entries, RiderResult row, int moto,
    HashSet<string> tagsInMoto)
  {
    var open = entries
      .Where(e => !e.Has(moto) && string.Equals(e.Category, row.Category ?? "", StringComparison.OrdinalIgnoreCase))
      .ToList();

    var sameTag = open.FirstOrDefault(e => e.IsTeam == row.IsTeam && e.HasTag(row.TagID));
    if (sameTag != null || row.IsTeam) return sameTag;

    var number = row.RiderNumber?.Trim() ?? "";
    if (number.Length == 0 || string.IsNullOrWhiteSpace(row.RiderName)) return null;

    return open.FirstOrDefault(e => !e.IsTeam
      && string.Equals(e.Number, number, StringComparison.OrdinalIgnoreCase)
      && e.HasName(row.RiderName)
      && !e.Tags.Any(tagsInMoto.Contains));
  }

  /// <summary>Riders of one class kept apart under one number are each told about the other.</summary>
  private static void NoteSharedNumbers(List<EntryBuilder> entries)
  {
    var groups = entries.Where(e => !e.IsTeam && e.Number.Length > 0)
      .GroupBy(e => (Category: e.Category.ToUpperInvariant(), Number: e.Number.ToUpperInvariant()))
      .Where(g => g.Count() > 1);

    foreach (var group in groups)
    {
      var names = string.Join(" and ", group.Select(e => e.DisplayName));
      foreach (var entry in group)
        entry.SharedNumber = $"Number {entry.Number} was given to {names} - scored as different riders. " +
                             "Check the rider list.";
    }
  }

  /// <summary>
  /// FIM 5.36: most points first, a tie going to the better place in the last
  /// moto (DMSB 8.8 says the same). Riders without points follow, ranked by
  /// the sum of their places - those placed in every moto before those who
  /// missed one - and riders placed nowhere come last, without a place.
  /// </summary>
  private static List<OverallEntry> Rank(IEnumerable<OverallEntry> entries)
  {
    var ordered = entries
      .OrderBy(e => e.Motos.Any(m => m.Placed) ? 0 : 1)
      .ThenByDescending(e => e.Points)
      .ThenBy(e => e.Points > 0 ? 0 : e.Motos.Count(m => !m.Placed))
      .ThenBy(e => e.Points > 0 ? 0 : e.Motos.Where(m => m.Placed).Sum(m => m.Position))
      .ThenBy(e => e, LastMotoFirst.Instance)
      .ThenBy(e => NumberOrder(e.Number))
      .ThenBy(e => e.Number, StringComparer.OrdinalIgnoreCase)
      .ToList();

    var rank = 0;
    foreach (var entry in ordered)
      entry.Rank = entry.Motos.Any(m => m.Placed) ? ++rank : null;

    return ordered;
  }

  private static int NumberOrder(string number) => int.TryParse(number, out var n) ? n : int.MaxValue;

  /// <summary>The better place in the last moto wins a tie, then the moto before, and so on.</summary>
  private sealed class LastMotoFirst : IComparer<OverallEntry>
  {
    public static readonly LastMotoFirst Instance = new();

    public int Compare(OverallEntry? x, OverallEntry? y)
    {
      if (x == null || y == null) return 0;
      for (var m = Math.Max(x.Motos.Count, y.Motos.Count) - 1; m >= 0; m--)
      {
        var a = m < x.Motos.Count && x.Motos[m].Placed ? x.Motos[m].Position : int.MaxValue;
        var b = m < y.Motos.Count && y.Motos[m].Placed ? y.Motos[m].Position : int.MaxValue;
        if (a != b) return a.CompareTo(b);
      }
      return 0;
    }
  }

  private sealed class EntryBuilder
  {
    private readonly OverallMotoCell[] _cells;
    private readonly bool[] _rode;
    private readonly HashSet<string> _tags = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _names = new();
    private string _number = "";
    private string _name = "";
    private string _team = "";
    private bool _isTeam;
    private RiderInfo? _rider;

    public EntryBuilder(int motoCount)
    {
      _cells = Enumerable.Repeat(OverallMotoCell.Absent, motoCount).ToArray();
      _rode = new bool[motoCount];
    }

    public string Category { get; init; } = "";
    public string Number => _number.Trim();
    public bool IsTeam => _isTeam;
    public string DisplayName => _name.Length > 0 ? _name : $"transponder {_tags.FirstOrDefault()}";

    /// <summary>Set when another rider of the class has the same number. See NoteSharedNumbers.</summary>
    public string? SharedNumber { get; set; }

    public bool Has(int moto) => _rode[moto];
    public bool HasTag(string tag) => _tags.Contains(tag);
    public IEnumerable<string> Tags => _tags;

    public bool HasName(string? name) =>
      !string.IsNullOrWhiteSpace(name) && _names.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);

    public void Add(int moto, OverallMotoCell cell, RiderResult row, RiderInfo? rider)
    {
      _cells[moto] = cell;
      _rode[moto] = true;
      _tags.Add(row.TagID);

      // The latest moto has the name as last corrected, so it wins.
      _number = row.RiderNumber ?? "";
      if (!string.IsNullOrWhiteSpace(row.RiderName))
      {
        _name = row.RiderName.Trim();
        if (!_names.Contains(_name, StringComparer.OrdinalIgnoreCase)) _names.Add(_name);
      }
      _team = row.Team ?? "";
      _isTeam = row.IsTeam;
      _rider = rider ?? _rider;
    }

    public OverallEntry Build() => new()
    {
      Number = _number,
      Name = _name,
      Team = _team,
      Category = Category,
      IsTeam = _isTeam,
      Rider = _rider,
      Motos = _cells.ToList(),
      // Two names on one number is usually a typo in a rider list, but it can
      // be two riders sharing a number, and adding their points together would
      // hand one of them a result that is not theirs. Say so rather than guess.
      Warning = string.Join(" ", new[]
      {
        _names.Count > 1 ? $"Number {_number} has different names: {string.Join(" / ", _names)}" : null,
        SharedNumber
      }.OfType<string>()) is { Length: > 0 } warning ? warning : null
    };
  }
}
