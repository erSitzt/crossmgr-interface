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
/// Riders are matched between motos by class and start number, not by
/// transponder: a spare transponder in the second moto is common, a change of
/// number is not.
/// </summary>
public static class OverallScorer
{
  public static OverallResult Score(string title, IReadOnlyList<OverallMoto> motos, OverallRules rules)
  {
    var generator = new RaceReportGenerator();
    var entries = new Dictionary<string, EntryBuilder>(StringComparer.OrdinalIgnoreCase);

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

    foreach (var group in entries.Values.GroupBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
               .OrderBy(g => g.Key.Length == 0 ? 1 : 0).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
    {
      result.Classes.Add(new OverallClass { Name = group.Key, Entries = Rank(group.Select(e => e.Build())) });
    }

    return result;
  }

  private static void ScoreClass(List<RiderResult> sheet, Dictionary<string, RiderInfo> riders, int moto,
    int motoCount, OverallRules rules, Dictionary<string, EntryBuilder> entries)
  {
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
      var key = KeyFor(row, rider);
      if (!entries.TryGetValue(key, out var entry))
      {
        entry = new EntryBuilder(motoCount) { Category = row.Category ?? "" };
        entries[key] = entry;
      }
      entry.Add(moto, cell, row, rider);
    }
  }

  /// <summary>
  /// Class and start number. A team is its team key; a rider nobody gave a
  /// number to can only be followed by transponder.
  /// </summary>
  private static string KeyFor(RiderResult row, RiderInfo? rider)
  {
    if (row.IsTeam) return $"{row.Category}|team|{row.TagID}";
    var number = row.RiderNumber?.Trim() ?? "";
    return number.Length > 0 ? $"{row.Category}|{number}" : $"{row.Category}|tag|{row.TagID}";
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
    private readonly List<string> _names = new();
    private string _number = "";
    private string _name = "";
    private string _team = "";
    private bool _isTeam;
    private RiderInfo? _rider;

    public EntryBuilder(int motoCount)
    {
      _cells = Enumerable.Repeat(OverallMotoCell.Absent, motoCount).ToArray();
    }

    public string Category { get; init; } = "";

    public void Add(int moto, OverallMotoCell cell, RiderResult row, RiderInfo? rider)
    {
      _cells[moto] = cell;

      // The latest moto has the name as last corrected, so it wins.
      _number = row.RiderNumber ?? "";
      if (!string.IsNullOrWhiteSpace(row.RiderName))
      {
        _name = row.RiderName;
        if (!_names.Contains(row.RiderName, StringComparer.OrdinalIgnoreCase)) _names.Add(row.RiderName);
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
      Warning = _names.Count > 1 ? $"Number {_number} has different names: {string.Join(" / ", _names)}" : null
    };
  }
}
