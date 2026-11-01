namespace CrossMgrInterface;

/// <summary>What the list of every class of the day is ranked on.</summary>
public enum DayRankingMeasure
{
  BestLap,
  AverageLap
}

/// <summary>One rider on the list of every class of the day.</summary>
public sealed class DayRankingEntry
{
  /// <summary>Place on best lap, or null for a rider with no timed lap.</summary>
  public int? BestRank { get; set; }

  /// <summary>Place on average lap, or null with fewer than <see cref="DayRankingScorer.MinimumLapsForAverage"/> timed laps.</summary>
  public int? AverageRank { get; set; }

  public string Number { get; init; } = "";
  public string Name { get; init; } = "";
  public string Team { get; init; } = "";

  /// <summary>The class of the latest race they rode.</summary>
  public string Category { get; init; } = "";

  /// <summary>Every class they rode in, when more than one.</summary>
  public IReadOnlyList<string> Classes { get; init; } = Array.Empty<string>();

  public TimeSpan? BestLap { get; init; }

  /// <summary>Which race (an index into <see cref="DayRankingResult.SessionTitles"/>) and which lap.</summary>
  public int? BestLapSession { get; init; }
  public int? BestLapNumber { get; init; }

  /// <summary>When the best lap was completed: on equal times, whoever set it first is ahead.</summary>
  internal DateTime? BestLapAt { get; init; }

  public TimeSpan? AverageLap { get; init; }

  /// <summary>Laps that count towards the average: every lap but the first of each race.</summary>
  public int TimedLaps { get; init; }

  /// <summary>Did not start in any race of the day they were entered in.</summary>
  public bool Dns { get; init; }

  /// <summary>The best lap is much faster than any other: worth a look before it is read out.</summary>
  public bool CheckBestLap { get; init; }

  /// <summary>The rider as entered in the latest race, so the website can name them as they agreed.</summary>
  public RiderInfo? Rider { get; init; }

  /// <summary>Something the organiser should check before handing the list out.</summary>
  public string? Warning { get; init; }

  public int? RankBy(DayRankingMeasure measure) => measure == DayRankingMeasure.AverageLap ? AverageRank : BestRank;
  public TimeSpan? TimeBy(DayRankingMeasure measure) => measure == DayRankingMeasure.AverageLap ? AverageLap : BestLap;
}

public sealed class DayRankingResult
{
  public string Title { get; init; } = "";
  public DayRankingMeasure Measure { get; init; }

  /// <summary>The races counted, in the order they were run.</summary>
  public IReadOnlyList<string> SessionTitles { get; init; } = Array.Empty<string>();

  /// <summary>Races of the day not counted, each with why.</summary>
  public IReadOnlyList<string> LeftOut { get; init; } = Array.Empty<string>();

  /// <summary>Every rider, in best-lap order. See <see cref="Ordered"/> for the other.</summary>
  public List<DayRankingEntry> Entries { get; init; } = new();

  /// <summary>Something about the whole list worth knowing, such as races on different circuits.</summary>
  public List<string> Notes { get; } = new();

  /// <summary>False while races of the day are still to be run: the list says provisional.</summary>
  public bool Final { get; init; } = true;

  public DateTime GeneratedAt { get; init; } = DateTime.Now;

  /// <summary>
  /// The riders in the order of <paramref name="measure"/>: those with a place
  /// on it, then those with a best lap but too few laps for an average, then
  /// those with no time at all, by class and number.
  /// </summary>
  public List<DayRankingEntry> Ordered(DayRankingMeasure measure) => Entries
    .OrderBy(e => e.RankBy(measure).HasValue ? 0 : e.BestRank.HasValue ? 1 : 2)
    .ThenBy(e => e.RankBy(measure) ?? e.BestRank ?? int.MaxValue)
    .ThenBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
    .ThenBy(e => NumberOrder(e.Number))
    .ThenBy(e => e.Number, StringComparer.OrdinalIgnoreCase)
    .ToList();

  /// <summary>Caption/value pairs printed under the list and sent to the website.</summary>
  public List<(string Caption, string Value)> Describe() => new()
  {
    ("Riders", "every rider of every race of the day, all classes; practice, qualifying and team races are not counted"),
    ("Laps", "lap 1 of each race is left out - it runs from the start, not once round the circuit"),
    ("Average lap", $"the mean of those laps over all races; ranked with at least {DayRankingScorer.MinimumLapsForAverage} of them"),
    ("Ties", "on best lap, whoever set it first; on average lap, the better best lap")
  };

  internal static int NumberOrder(string number) => int.TryParse(number, out var n) ? n : int.MaxValue;
}

/// <summary>
/// Every rider of every class of a race day in one list, ranked on lap times.
///
/// At a club day each class rides its own races, and they are not the same
/// length, so laps and total time say nothing between classes - but a lap is
/// the same circuit for everyone. A fast rider of a lower class is often
/// quicker than the slow end of the class above, and this is the list that
/// shows it. It changes nobody's class result.
///
/// Both measures are the ones each race's sheet already prints, worked out
/// over the whole day: the best lap is the fastest any race's sheet gives the
/// rider, and the average pools every lap but the first of each race - lap 1
/// runs from the start, not round the circuit (see <see cref="RiderInfo.BestLapTime"/>).
/// Riders are followed from race to race as the overall follows them, by
/// transponder, across classes (see <see cref="RiderMatching"/>).
/// </summary>
public static class DayRankingScorer
{
  /// <summary>
  /// Fewer laps than this and an average is luck: one quick lap and then a
  /// retirement would top the list. Such a rider still has a best lap.
  /// </summary>
  public const int MinimumLapsForAverage = 3;

  /// <summary>
  /// A best lap this much faster than the rider's next best is flagged. On
  /// 31 October 2026 one beginner's best lap was 27 s quicker than any other
  /// lap he rode that day - a cut, or a read that belonged to someone else -
  /// and would have been read out as the 21st fastest of the day.
  /// </summary>
  private const double CheckLapRatio = 0.92;

  public static DayRankingResult Score(string title, IReadOnlyList<OverallMoto> races, DayRankingMeasure measure,
    bool final = true)
  {
    var generator = new RaceReportGenerator();
    var builders = new List<Builder>();
    var counted = new List<string>();
    var leftOut = new List<string>();

    for (var i = 0; i < races.Count; i++)
    {
      var race = races[i];
      if (race.Rules.TeamEvent)
      {
        // A team's laps are ridden by several people; its best lap is nobody's.
        leftOut.Add($"{race.Title} - a team race");
        continue;
      }

      var session = counted.Count;
      counted.Add(race.Title);

      var sheet = generator.PrepareReportData(race.Riders, race.Start, race.End, race.Rules.Duration,
        race.Finished, race.Title, rules: race.Rules);
      var tags = sheet.RiderResults.Select(r => r.TagID).ToHashSet(StringComparer.OrdinalIgnoreCase);

      foreach (var row in sheet.RiderResults)
      {
        var key = RiderKey.Of(row);
        var builder = RiderMatching.Find(builders, key, session, tags, acrossClasses: true);
        if (builder == null)
        {
          builder = new Builder(races.Count);
          builders.Add(builder);
        }
        race.Riders.TryGetValue(row.TagID, out var rider);
        builder.Add(session, row, rider);
      }
    }

    RiderMatching.NoteSharedNumbers(builders);
    NoteSharedTransponders(builders);

    var entries = builders.Select(b => b.Build()).ToList();
    Rank(entries);

    return new DayRankingResult
    {
      Title = title,
      Measure = measure,
      SessionTitles = counted,
      LeftOut = leftOut,
      Entries = new DayRankingResult { Entries = entries }.Ordered(DayRankingMeasure.BestLap),
      Final = final
    };
  }

  private static void Rank(List<DayRankingEntry> entries)
  {
    var byBest = entries.Where(e => e.BestLap.HasValue)
      .OrderBy(e => e.BestLap)
      // Equal to the thousandth: whoever set it first, as qualifying decides it.
      .ThenBy(e => e.BestLapSession)
      .ThenBy(e => e.BestLapAt ?? DateTime.MaxValue)
      .ThenBy(e => e.AverageLap ?? TimeSpan.MaxValue)
      .ThenBy(e => DayRankingResult.NumberOrder(e.Number))
      .ToList();
    for (var i = 0; i < byBest.Count; i++) byBest[i].BestRank = i + 1;

    var byAverage = entries.Where(e => e.AverageLap.HasValue && e.TimedLaps >= MinimumLapsForAverage)
      .OrderBy(e => e.AverageLap)
      .ThenBy(e => e.BestLap ?? TimeSpan.MaxValue)
      .ThenByDescending(e => e.TimedLaps)
      .ThenBy(e => DayRankingResult.NumberOrder(e.Number))
      .ToList();
    for (var i = 0; i < byAverage.Count; i++) byAverage[i].AverageRank = i + 1;
  }

  /// <summary>
  /// One transponder under two riders: a club's spare, lent from one class to
  /// another. Kept apart (see <see cref="RiderMatching.Find"/>), and said so,
  /// because a lap read in the wrong race would land on the wrong one.
  /// </summary>
  private static void NoteSharedTransponders(List<Builder> builders)
  {
    var byTag = builders.Where(b => !b.IsTeam)
      .SelectMany(b => b.Tags.Select(t => (Tag: t, Rider: b)))
      .GroupBy(p => p.Tag, StringComparer.OrdinalIgnoreCase)
      .Where(g => g.Count() > 1);

    foreach (var group in byTag)
    {
      var names = string.Join(" and ", group.Select(p => p.Rider.DisplayName));
      foreach (var (_, rider) in group)
        rider.SharedTransponder = $"One transponder was ridden by {names} - listed as different riders.";
    }
  }

  private sealed class Builder : MatchedRider
  {
    private readonly List<(int Session, RiderLap Lap)> _laps = new();
    private readonly List<string> _classes = new();
    private (int Session, RiderLap Lap)? _best;
    private string _team = "";
    private RiderInfo? _rider;
    private bool _sawDns;
    private bool _started;

    public Builder(int sessionCount) : base(sessionCount) { }

    public string? SharedTransponder { get; set; }

    public void Add(int session, RiderResult row, RiderInfo? rider)
    {
      Record(session, RiderKey.Of(row));
      Category = row.Category ?? "";
      if (Category.Length > 0 && !_classes.Contains(Category, StringComparer.OrdinalIgnoreCase)) _classes.Add(Category);
      _team = row.Team ?? "";
      _rider = rider ?? _rider;

      // A rider who did not start one race but rode another started the day.
      if (row.IsDNS && row.TotalLaps == 0) _sawDns = true;
      else _started = true;

      if (rider == null) return;

      // The same laps the race's sheet averages, so the two never disagree.
      foreach (var lap in rider.Laps.Skip(1).Where(l => l.LapTime.HasValue))
        _laps.Add((session, lap));

      // The sheet's own best lap: it already leaves out a team's handovers
      // and a lap suspected of being two riders.
      if (rider.BestLap is { } best && (_best == null || best.LapTime < _best.Value.Lap.LapTime))
        _best = (session, best);
    }

    public DayRankingEntry Build()
    {
      TimeSpan? average = _laps.Count == 0
        ? null
        : TimeSpan.FromMilliseconds(_laps.Average(l => l.Lap.LapTime!.Value.TotalMilliseconds));

      var check = false;
      if (_best is { } best && _laps.Count >= MinimumLapsForAverage)
      {
        var next = _laps.Where(l => !ReferenceEquals(l.Lap, best.Lap)).Min(l => l.Lap.LapTime!.Value);
        check = best.Lap.LapTime!.Value.TotalMilliseconds < next.TotalMilliseconds * CheckLapRatio;
      }

      var warnings = new List<string>();
      if (Names.Count > 1) warnings.Add($"Number {RawNumber} has different names: {string.Join(" / ", Names)}");
      if (SharedNumber != null) warnings.Add(SharedNumber);
      if (SharedTransponder != null) warnings.Add(SharedTransponder);
      if (check)
        warnings.Add($"Best lap {TimeFormat.Precise(_best!.Value.Lap.LapTime!.Value)} is far quicker than any other lap " +
                     "this rider rode - check it.");

      return new DayRankingEntry
      {
        Number = RawNumber,
        Name = Name,
        Team = _team,
        Category = Category,
        Classes = _classes.Count > 1 ? _classes.ToList() : Array.Empty<string>(),
        BestLap = _best?.Lap.LapTime,
        BestLapSession = _best?.Session,
        BestLapNumber = _best?.Lap.LapNumber,
        BestLapAt = _best?.Lap.CrossingTime,
        AverageLap = average,
        TimedLaps = _laps.Count,
        Dns = _sawDns && !_started,
        CheckBestLap = check,
        Rider = _rider,
        Warning = warnings.Count > 0 ? string.Join(" ", warnings) : null
      };
    }
  }
}
