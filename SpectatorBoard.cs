namespace CrossMgrInterface;

/// <summary>What the spectator screen is told each second. Built by <see cref="SpectatorBoardBuilder"/>.</summary>
public sealed record SpectatorInputs
{
  /// <summary>The field, in race order (<see cref="PositionCalculator"/>), as display copies.</summary>
  public required IReadOnlyList<RiderInfo> Field { get; init; }

  public SessionType SessionType { get; init; }
  public bool TeamEvent { get; init; }

  /// <summary>Classes start in waves, so the class column and each rider's place in it matter.</summary>
  public bool Waves { get; init; }

  public string Title { get; init; } = "";
  public RaceDayState State { get; init; }

  /// <summary>Laps the leader still has to ride once time is up; null before then or unknown.</summary>
  public int? LeaderLapsToGo { get; init; }

  public TimeSpan? Remaining { get; init; }
  public TimeSpan? FinalElapsed { get; init; }
  public TimeSpan Duration { get; init; }

  /// <summary>How many riders to list; null for all of them.</summary>
  public int? RowLimit { get; init; }
}

public sealed record SpectatorRow
{
  /// <summary>"1", "12", or "-" for a rider out of the race.</summary>
  public string Position { get; init; } = "";
  public string Number { get; init; } = "";
  public string Name { get; init; } = "";
  public string Class { get; init; } = "";

  /// <summary>Place within the class, "" when there is only one class or the rider is out.</summary>
  public string ClassPosition { get; init; } = "";
  public string Laps { get; init; } = "";
  public string LastLap { get; init; } = "";
  public string BestLap { get; init; } = "";

  /// <summary>Gap to the leader in a race, to pole in a timed session.</summary>
  public string Gap { get; init; } = "";

  /// <summary>1-3 for the podium, 0 otherwise.</summary>
  public int Podium { get; init; }

  /// <summary>Has taken the chequered flag.</summary>
  public bool Finished { get; init; }

  /// <summary>DNF, DNS, off track, or no time: shown dimmed.</summary>
  public bool Out { get; init; }

  /// <summary>Holds the fastest lap of the session.</summary>
  public bool FastestLap { get; init; }
}

public sealed record SpectatorCrossing(string Number, string Name, int Lap, string LapTime);

public sealed record SpectatorBoard
{
  public string Title { get; init; } = "";
  public string Clock { get; init; } = "--:--";

  /// <summary>"of 20:00", "final time", "20 minute race".</summary>
  public string ClockSub { get; init; } = "";

  /// <summary>Under five minutes, under one, or neither: how urgent the clock looks.</summary>
  public int ClockUrgency { get; init; }

  /// <summary>"Race running", "2 laps to go", "Chequered flag", "Finished".</summary>
  public string State { get; init; } = "";

  /// <summary>The state is the flag: the last laps, or everyone coming in.</summary>
  public bool FlagOut { get; init; }

  public bool Timed { get; init; }
  public bool ShowClass { get; init; }

  /// <summary>Headers that change with the session: "Pos"/"Pick", "Gap"/"Gap to pole".</summary>
  public string PositionHeader { get; init; } = "Pos";
  public string GapHeader { get; init; } = "Gap";

  public IReadOnlyList<SpectatorRow> Rows { get; init; } = Array.Empty<SpectatorRow>();

  /// <summary>Riders the row limit leaves out.</summary>
  public int MoreCount { get; init; }

  /// <summary>"#7 Lukas Brandt  0:46.5  lap 4", or null before anyone has a lap time.</summary>
  public string? FastestLap { get; init; }

  public IReadOnlyList<SpectatorCrossing> Recent { get; init; } = Array.Empty<SpectatorCrossing>();
}

/// <summary>
/// Turns the field into the spectator screen's board. Pure, so the ranking,
/// gaps and highlights can be tested without a window.
///
/// Full names always: this is a screen at the track, where the announcer reads
/// them out anyway - not the website, which follows the rider's consent.
/// </summary>
public static class SpectatorBoardBuilder
{
  public const int RecentCount = 5;

  public static SpectatorBoard Build(SpectatorInputs inputs)
  {
    var timed = inputs.SessionType != SessionType.Race;
    var started = inputs.State is not (RaceDayState.WaitingForFirstRider or RaceDayState.ReadyToStart);

    var rows = !started
      ? EnteredField(inputs.Field)
      : timed
        ? TimedRows(inputs)
        : RaceRows(inputs);

    var classes = inputs.Field
      .Select(r => r.Category.Trim())
      .Where(c => c.Length > 0)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .Count();

    var limit = inputs.RowLimit ?? int.MaxValue;
    var fastest = FastestLap(inputs.Field);

    var (clock, clockSub, urgency) = ClockText(inputs);
    var (state, flag) = StateText(inputs, timed);

    return new SpectatorBoard
    {
      Title = inputs.Title.Length > 0 ? inputs.Title : timed ? "Timed session" : "Race",
      Clock = clock,
      ClockSub = clockSub,
      ClockUrgency = urgency,
      State = state,
      FlagOut = flag,
      Timed = timed,
      ShowClass = inputs.Waves || classes > 1,
      PositionHeader = inputs.SessionType == SessionType.TimedQualifying ? "Pick" : "Pos",
      GapHeader = timed ? "Gap to P1" : "Gap",
      Rows = rows.Take(limit).ToList(),
      MoreCount = Math.Max(0, rows.Count - limit),
      FastestLap = fastest == null
        ? null
        : $"{Label(fastest.Value.Rider)}   {BoardText.LapTime(fastest.Value.Lap.LapTime)}   lap {fastest.Value.Lap.LapNumber}",
      Recent = started ? Recent(inputs.Field) : Array.Empty<SpectatorCrossing>()
    };
  }

  // ---- Rows -------------------------------------------------------------------------

  private static List<SpectatorRow> RaceRows(SpectatorInputs inputs)
  {
    var field = inputs.Field;
    var leader = field.FirstOrDefault();
    var fastestTag = FastestLap(field)?.Rider.TagID;
    var finishing = inputs.State is RaceDayState.Finishing or RaceDayState.Finished;
    var classPlaces = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    var rows = new List<SpectatorRow>();
    for (var i = 0; i < field.Count; i++)
    {
      var rider = field[i];
      var isOut = rider.IsDNF || rider.IsDNS;

      rows.Add(new SpectatorRow
      {
        Position = isOut ? "-" : (i + 1).ToString(),
        Number = rider.RiderNumber,
        Name = Name(rider, inputs.TeamEvent, timed: false),
        Class = rider.Category,
        ClassPosition = isOut ? "" : NextPlace(classPlaces, rider.Category),
        Laps = rider.TotalLaps.ToString(),
        LastLap = BoardText.LapTime(rider.LastLapTime),
        BestLap = BoardText.LapTime(rider.BestLapTime),
        Gap = BoardText.Gap(rider, leader, i, timedSession: false),
        Podium = !isOut && i < 3 ? i + 1 : 0,
        Finished = finishing && !isOut && rider.FinalAllowedLap != int.MaxValue && rider.TotalLaps >= rider.FinalAllowedLap,
        Out = isOut,
        FastestLap = rider.TagID == fastestTag
      });
    }

    return rows;
  }

  /// <summary>Qualifying and free practice: ranked on best lap, the same ranking as the gate pick sheet.</summary>
  private static List<SpectatorRow> TimedRows(SpectatorInputs inputs)
  {
    var ranking = QualifyingRanking.Rank(inputs.Field);
    var fastestTag = FastestLap(inputs.Field)?.Rider.TagID;
    var classPlaces = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    return ranking.Select((entry, i) =>
    {
      var hasTime = entry.Status == QualifyingStatus.Timed;
      var rider = entry.Rider;

      return new SpectatorRow
      {
        Position = entry.GatePick.ToString(),
        Number = rider.RiderNumber,
        Name = Name(rider, inputs.TeamEvent, timed: true),
        Class = rider.Category,
        ClassPosition = hasTime ? NextPlace(classPlaces, rider.Category) : "",
        Laps = entry.TimedLaps.ToString(),
        LastLap = BoardText.LapTime(rider.LastLapTime),
        BestLap = hasTime ? BoardText.LapTime(entry.BestLapTime) : "NO TIME",
        Gap = hasTime && entry.GapToPole.HasValue ? $"+{entry.GapToPole.Value.TotalSeconds:F2}" : "",
        Podium = hasTime && i < 3 ? i + 1 : 0,
        Out = !hasTime,
        FastestLap = rider.TagID == fastestTag
      };
    }).ToList();
  }

  /// <summary>Before the start: who is entered, by number, so the screen is not empty.</summary>
  private static List<SpectatorRow> EnteredField(IReadOnlyList<RiderInfo> field) =>
    field
      .OrderBy(r => int.TryParse(r.RiderNumber, out var n) ? n : int.MaxValue)
      .ThenBy(r => r.RiderNumber, StringComparer.OrdinalIgnoreCase)
      .ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase)
      .Select(r => new SpectatorRow
      {
        Number = r.RiderNumber,
        Name = $"{r.FirstName} {r.LastName}".Trim(),
        Class = r.Category
      })
      .ToList();

  private static string NextPlace(Dictionary<string, int> places, string category)
  {
    var key = category.Trim();
    places[key] = places.GetValueOrDefault(key) + 1;
    return places[key].ToString();
  }

  /// <summary>The rider's name; for a team, the team and who is out on the bike.</summary>
  private static string Name(RiderInfo rider, bool teamEvent, bool timed)
  {
    var name = $"{rider.FirstName} {rider.LastName}".Trim();
    if (name.Length == 0) name = rider.RiderNumber.Length > 0 ? "" : "unidentified rider";

    if (teamEvent && rider.OnTrackMember is { } member)
      name += $" · {member.ShortLabel}";

    var status = BoardText.Status(rider, timed);
    return status.Length > 0 ? $"{name} ({status})" : name;
  }

  private static string Label(RiderInfo rider)
  {
    var name = $"{rider.FirstName} {rider.LastName}".Trim();
    if (rider.RiderNumber.Length > 0) return name.Length > 0 ? $"#{rider.RiderNumber} {name}" : $"#{rider.RiderNumber}";
    return name;
  }

  // ---- Highlights -------------------------------------------------------------------

  /// <summary>The quickest timed lap anyone has done, and whose. The first to set a time keeps it on a tie.</summary>
  private static (RiderInfo Rider, RiderLap Lap)? FastestLap(IEnumerable<RiderInfo> field)
  {
    (RiderInfo Rider, RiderLap Lap)? best = null;

    foreach (var rider in field)
    {
      if (rider.IsDNS) continue;
      foreach (var lap in rider.Laps)
      {
        if (lap.LapTime is not { } time || time <= TimeSpan.Zero) continue;
        if (best == null || time < best.Value.Lap.LapTime ||
            (time == best.Value.Lap.LapTime && lap.CrossingTime < best.Value.Lap.CrossingTime))
          best = (rider, lap);
      }
    }

    return best;
  }

  private static List<SpectatorCrossing> Recent(IEnumerable<RiderInfo> field) =>
    field
      .SelectMany(r => r.Laps.Select(l => (Rider: r, Lap: l)))
      .OrderByDescending(x => x.Lap.CrossingTime)
      .Take(RecentCount)
      .Select(x => new SpectatorCrossing(
        x.Rider.RiderNumber,
        $"{x.Rider.FirstName} {x.Rider.LastName}".Trim(),
        x.Lap.LapNumber,
        x.Lap.LapTime is { } t ? BoardText.LapTime(t) : "start"))
      .ToList();

  // ---- Header -----------------------------------------------------------------------

  private static (string Clock, string Sub, int Urgency) ClockText(SpectatorInputs inputs)
  {
    if (inputs.FinalElapsed is { } final) return (BoardText.Clock(final), "final time", 0);

    if (inputs.Remaining is not { } remaining)
      return ("--:--", $"{inputs.Duration.TotalMinutes:F0} minutes", 0);

    var urgency = remaining.TotalMinutes switch { <= 1 => 2, <= 5 => 1, _ => 0 };
    return (BoardText.Clock(remaining), $"of {inputs.Duration.TotalMinutes:F0}:00", urgency);
  }

  private static (string Text, bool Flag) StateText(SpectatorInputs inputs, bool timed) => inputs.State switch
  {
    RaceDayState.WaitingForFirstRider or RaceDayState.ReadyToStart => ("Starting soon", false),
    RaceDayState.Running => (timed ? "Session running" : "Race running", false),
    RaceDayState.LastLaps when !timed && inputs.LeaderLapsToGo is { } toGo =>
      (toGo <= 1 ? "Last lap" : $"{toGo} laps to go", toGo <= 1),
    RaceDayState.LastLaps => (timed ? "Chequered flag" : "Last lap", true),
    RaceDayState.Finishing => ("Chequered flag", true),
    RaceDayState.Finished => (timed ? "Session over" : "Finished", false),
    _ => ("", false)
  };
}
