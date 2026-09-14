namespace CrossMgrInterface;

/// <summary>
/// The demos the application ships with, so that someone with no reader and no
/// rider list can watch it time a session.
///
/// Each one is planned in advance with a fixed seed - every run of a demo is
/// the same run, and the tests can hold it to what its intro card promises.
/// They grew out of the Python harnesses (see TESTING.md) and obey the same
/// rule: a race only notices its time is up on the next crossing, so riders
/// keep coming round for a few laps past the length or it would never finish.
/// </summary>
public static class DemoScenarios
{
  public const string RaceId = "race";
  public const string QualifyingId = "qualifying";
  public const string EnduroId = "enduro";
  public const string TeamsId = "teams";

  /// <summary>The spare transponder the race demo's forgetful rider borrows. Not on the rider list.</summary>
  public const string SpareTransponder = "20269999";

  public static IReadOnlyList<DemoScenario> All { get; } = new[] { Race(), Qualifying(), Enduro(), Teams() };

  public static DemoScenario? Find(string? id) =>
    All.FirstOrDefault(s => string.Equals(s.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));

  /// <summary>Plans a demo afresh.</summary>
  public static DemoScenario Build(string id) => id switch
  {
    RaceId => Race(),
    QualifyingId => Qualifying(),
    EnduroId => Enduro(),
    TeamsId => Teams(),
    _ => throw new ArgumentException($"There is no demo called {id}.", nameof(id))
  };

  // ---- A short motocross race ----------------------------------------------

  private static DemoScenario Race()
  {
    const int minutes = 6;
    const int extraLaps = 1;
    var rng = new Random(2026);

    // Quickest first. MX1 a little faster than MX2, as on a real day.
    var field = new (string Number, string Name, string Club, string Class, double Pace)[]
    {
      ("7", "Lukas Brandt", "MSC Adler", "MX1", 46.5),
      ("12", "Mia Hoffmann", "RC Falke", "MX1", 47.2),
      ("23", "Jan Keller", "MX Team Nord", "MX1", 48.0),
      ("31", "Sophie Wagner", "MSC Adler", "MX1", 48.9),
      ("88", "Max Schröder", "RSV Blitz", "MX2", 49.5),
      ("44", "Tim Schulz", "", "MX1", 50.1),
      ("91", "Emma Koch", "RC Falke", "MX2", 50.8),
      ("57", "Lea Richter", "MX Team Nord", "MX1", 51.4),
      ("101", "Felix Bauer", "RSV Blitz", "MX2", 52.0),
      ("68", "Paul Neumann", "MSC Adler", "MX1", 52.7),
      ("111", "Hanna Klein", "", "MX2", 53.4),
      ("71", "Nina Wolf", "RC Falke", "MX1", 54.2),
      ("124", "Noah Schmitt", "MX Team Nord", "MX2", 55.0),
      ("133", "Lina Krüger", "RSV Blitz", "MX2", 56.1),
      ("150", "Ben Hartmann", "", "MX2", 57.6),
      ("171", "Jonas Maier", "MSC Adler", "MX2", 59.0)
    };

    const string readTwice = "12";
    const string retires = "68";
    const string missedRead = "101";
    const string onTheSpare = "171";

    // The start gate is just before the line: the whole field is through the
    // loop within a few seconds, quickest first.
    var firsts = field.Select((_, i) => 5.0 + i * 0.8 + rng.NextDouble() * 1.5).ToArray();
    var flag = firsts.Min() + minutes * 60;
    var reads = new List<DemoCrossing>();

    for (var i = 0; i < field.Length; i++)
    {
      var rider = field[i];
      var times = GoRound(rng, firsts[i], rider.Pace, 2.0, flag + (extraLaps + 2.5) * rider.Pace);

      if (rider.Number == retires) times = times.Take(4).ToList();
      if (rider.Number == missedRead) times.RemoveAt(4);

      var spare = rider.Number == onTheSpare;
      var tag = spare ? SpareTransponder : Tag(rider.Number);
      reads.AddRange(times.Select(t => new DemoCrossing(Seconds(t), tag, spare ? DemoReadKind.Unknown : DemoReadKind.Lap)));

      if (rider.Number == readTwice)
        reads.Add(new DemoCrossing(Seconds(times[2] + 4.0), tag, DemoReadKind.TooSoon));
    }

    return new DemoScenario
    {
      Id = RaceId,
      Title = "A short motocross race",
      Length = "About 10 minutes",
      Summary = "16 riders in two classes race for 6 minutes and one more lap. The clock starts when the " +
                "first rider crosses the line.",
      WhatHappens = new[]
      {
        "The riders reach the line a few seconds after you press Start demo, and the clock starts with the first of them.",
        "When the 6 minutes are up the leader rides the lap they are on and one more, everyone else finishes " +
        "the lap they are on, and the race finishes by itself.",
        "A few things go wrong on purpose, as they do on a real day."
      },
      WhatToTry = new[]
      {
        "#101 Felix Bauer's transponder is missed once, so one of his laps comes in twice as long and shows " +
        "CHECK. Press Fix laps... to split it.",
        "#171 Jonas Maier left his transponder at home and rides on a spare that is not on the rider list. It " +
        "shows as UNKNOWN on the Riders tab: right-click it and choose Identify this transponder...",
        "#12 Mia Hoffmann is read twice in one pass. The second read is not counted - Fix laps... shows it as a grey row.",
        "#68 Paul Neumann pulls in after three laps and ends up DNF.",
        "When the race has finished, press Results... for the sheet."
      },
      SessionType = SessionType.Race,
      DurationMinutes = minutes,
      AdditionalLaps = extraLaps,
      Roster = field.Select(r => new DemoRider(Tag(r.Number), r.Number, r.Name, r.Club, r.Class)).ToList(),
      Crossings = Ordered(reads)
    };
  }

  // ---- Qualifying to gate pick ---------------------------------------------

  private static DemoScenario Qualifying()
  {
    const int minutes = 6;

    // The field of qualifying.py --late, brought 12 s forward so the first
    // rider is out moments after the demo starts: an out-lap, then each lap
    // time exactly, so the tie below really is a tie.
    var field = new (string Number, string Name, string Club, double OutLap, double[] Laps)[]
    {
      ("11", "Anna Berger", "MSC Adler", 8.0, new[] { 45.0, 44.0, 43.5, 42.0, 43.0 }),
      ("22", "Ben Fischer", "RC Falke", 13.0, new[] { 46.0, 38.5 }),
      ("33", "Carla Hoff", "MX Team Nord", 6.0, new[] { 41.0, 40.0, 41.5, 40.5, 42.0, 41.0 }),
      ("44", "David Kern", "RSV Blitz", 10.0, new[] { 42.0, 44.5, 45.0 }),
      ("55", "Elif Yilmaz", "MSC Adler", 9.0, new[] { 40.0, 39.2, 40.5, 39.8 }),
      // Still out at the flag, which falls at 366 s: the 38.0 lap ends after
      // it and counts, taking pole, and the lap after that does not.
      ("66", "Frank Weber", "RC Falke", 11.0, new[] { 43.0, 41.0, 42.0, 42.0, 42.0, 42.0, 42.0, 42.0, 38.0, 42.0 }),
      ("77", "Greta Lang", "", 12.0, Array.Empty<double>()),
      ("88", "Hugo Reiter", "MX Team Nord", 14.0, new[] { 44.0, 43.5, 45.0 })
    };

    // On the list, never out of the paddock. Numbered 9 and 10 so the sheet
    // also shows start numbers sorting as numbers.
    var stayIn = new (string Number, string Name, string Club)[]
    {
      ("9", "Ida Vogt", "RSV Blitz"),
      ("10", "Jonas Ritter", "")
    };

    const string readTwice = "55";
    var reads = new List<DemoCrossing>();

    foreach (var rider in field)
    {
      var tag = Tag(rider.Number);
      var t = rider.OutLap;
      reads.Add(new DemoCrossing(Seconds(t), tag));

      for (var lap = 0; lap < rider.Laps.Length; lap++)
      {
        t += rider.Laps[lap];
        reads.Add(new DemoCrossing(Seconds(t), tag));
        if (rider.Number == readTwice && lap == 1)
          reads.Add(new DemoCrossing(Seconds(t + 4.0), tag, DemoReadKind.TooSoon));
      }
    }

    return new DemoScenario
    {
      Id = QualifyingId,
      Title = "Qualifying to gate pick",
      Length = "About 7 minutes",
      Summary = "Ten riders have 6 minutes of timed qualifying. Each rider's best lap decides the order they " +
                "pick their place on the start gate.",
      WhatHappens = new[]
      {
        "The session starts when the first rider crosses the line. Riders come and go as they like, and two " +
        "of them never go out at all.",
        "When the clock runs out the chequered flag comes out. A rider still on track finishes the lap they " +
        "are on and that lap counts - nothing after it does."
      },
      WhatToTry = new[]
      {
        "Watch the order on the Race Day screen change as the quick laps come in.",
        "#66 Frank Weber is still out at the flag. His last lap ends after it, counts, and takes pole. The " +
        "lap after that is not counted.",
        "#44 David Kern and #11 Anna Berger both set 42.000. David set his first, so he picks first.",
        "#55 Elif Yilmaz is read twice in one pass; the second read is not counted.",
        "When it says Session over, press Gate pick order... for the sheet."
      },
      SessionType = SessionType.TimedQualifying,
      DurationMinutes = minutes,
      Roster = field.Select(r => new DemoRider(Tag(r.Number), r.Number, r.Name, r.Club, "MX1"))
        .Concat(stayIn.Select(r => new DemoRider(Tag(r.Number), r.Number, r.Name, r.Club, "MX1")))
        .ToList(),
      Crossings = Ordered(reads)
    };
  }

  // ---- Enduro in waves -----------------------------------------------------

  private static DemoScenario Enduro()
  {
    const int minutes = 10;
    var gap = TimeSpan.FromMinutes(1);
    var rng = new Random(1985);

    // In start order. Quickest first within each class.
    var classes = new (string Name, double Fastest, double Slowest, (string Number, string Name)[] Riders)[]
    {
      ("MX1", 76, 86, new[]
      {
        ("101", "Moritz Lehmann"), ("102", "Clara Braun"), ("103", "Jakob Zimmermann"), ("104", "Laura Krause"),
        ("105", "Elias Hartung"), ("106", "Marie Lorenz"), ("107", "David Seidel"), ("108", "Sarah Jung")
      }),
      ("MX2", 84, 96, new[]
      {
        ("201", "Luca Frank"), ("202", "Julia Berger"), ("203", "Finn Roth"), ("204", "Anna Busch"),
        ("205", "Leon Kraus"), ("206", "Lisa Vogel"), ("207", "Henri Kühn"), ("208", "Amelie Winter")
      }),
      ("Youth", 96, 110, new[]
      {
        ("301", "Emil Pohl"), ("302", "Ella Arnold"), ("303", "Oskar Graf"), ("304", "Frieda Brandt"),
        ("305", "Anton Haas"), ("306", "Mila Schreiber"), ("307", "Theo Dietrich"), ("308", "Pia Ludwig")
      })
    };

    const string missedRead = "104";
    const string retires = "206";
    const string earlyToTheGate = "308";

    var reads = new List<DemoCrossing>();

    for (var c = 0; c < classes.Length; c++)
    {
      var cls = classes[c];
      // Counted from the class's own start. The flag falls for everyone at
      // the same moment, which for a later class is earlier in its own race.
      var flag = minutes * 60 - c * gap.TotalSeconds;

      for (var i = 0; i < cls.Riders.Length; i++)
      {
        var (number, _) = cls.Riders[i];
        var pace = cls.Fastest + (cls.Slowest - cls.Fastest) * i / (cls.Riders.Length - 1);
        var times = GoRound(rng, pace * 1.05 + i * 2.0, pace, 3.0, flag + 2.5 * pace);

        if (number == missedRead) times.RemoveAt(3);
        if (number == retires) times = times.Take(4).ToList();

        reads.AddRange(times.Select(t => new DemoCrossing(Seconds(t), Tag(number), DemoReadKind.Lap, cls.Name)));

        // Riding over the loop on the way to the gate, while MX1 is racing.
        if (number == earlyToTheGate)
          reads.Add(new DemoCrossing(TimeSpan.FromSeconds(20), Tag(number), DemoReadKind.BeforeWave, classes[0].Name));
      }
    }

    return new DemoScenario
    {
      Id = EnduroId,
      Title = "Enduro in waves",
      Length = "About 13 minutes",
      Summary = "24 riders in three classes. MX1 leaves the gate when you press START RACE, MX2 a minute " +
                "later and Youth a minute after that. After 10 minutes everyone finishes the lap they are on.",
      WhatHappens = new[]
      {
        "Nothing moves until you press START RACE: that is the moment MX1 leaves the gate. The application " +
        "sends MX2 and Youth off by itself, a minute apart, and the Race Day screen counts down to each.",
        "Every rider is timed from their own class's start.",
        "After 10 minutes the chequered flag comes out, everyone finishes the lap they are on, and the race " +
        "finishes by itself."
      },
      WhatToTry = new[]
      {
        "Press START RACE when you are ready. START MX2 NOW on the Race Day screen sends the next class off " +
        "early, and its riders go with it.",
        "#308 Pia Ludwig rides over the loop on her way to the gate before Youth has started. That read is ignored.",
        "#104 Laura Krause's transponder is missed once, so one lap shows CHECK. Press Fix laps... to split it.",
        "#206 Lisa Vogel retires after three laps and ends up DNF.",
        "When the race has finished, press Results... for the sheet."
      },
      SessionType = SessionType.Race,
      DurationMinutes = minutes,
      AdditionalLaps = 0,
      WaveGap = gap,
      Roster = classes
        .SelectMany(cls => cls.Riders.Select(r => new DemoRider(Tag(r.Number), r.Number, r.Name, "", cls.Name)))
        .ToList(),
      Crossings = Ordered(reads)
    };
  }

  // ---- Team event ----------------------------------------------------------

  private static DemoScenario Teams()
  {
    const int minutes = 8;
    var rng = new Random(4040);

    var teams = new (string Name, (string Number, string Name)[] Riders)[]
    {
      ("MSC Adler", new[] { ("11", "Anna Berger"), ("12", "Ben Fischer") }),
      ("RC Falke", new[] { ("21", "Carla Hoff"), ("22", "David Kern") }),
      ("RSV Blitz", new[] { ("31", "Elif Yilmaz"), ("32", "Frank Weber") }),
      ("Dirt Devils", new[] { ("41", "Greta Lang"), ("42", "Hugo Reiter") }),
      ("MX Team Nord", new[] { ("51", "Ida Vogt"), ("52", "Jonas Ritter") }),
      ("Enduro Freunde", new[] { ("61", "Kira Sommer"), ("62", "Leon Frank") })
    };

    // A team name used once is a solo rider, and so is an empty one.
    var solos = new (string Number, string Name, string Team)[]
    {
      ("71", "Mara Engel", "Team Sued"),
      ("72", "Nils Otto", "")
    };

    const string twoOnTrack = "MSC Adler";
    const string sharedTransponder = "RC Falke";
    const string waitsAtTheLoop = "RSV Blitz";

    var start = 5.0;
    var flag = start + minutes * 60;
    var until = flag + 2.5 * 70;   // two of the slowest laps with a changeover, and some
    var roster = new List<DemoRider>();
    var reads = new List<DemoCrossing>();

    for (var k = 0; k < teams.Length; k++)
    {
      var (team, riders) = teams[k];
      var tags = team == sharedTransponder
        ? new[] { Tag(riders[0].Number), Tag(riders[0].Number) }
        : riders.Select(r => Tag(r.Number)).ToArray();
      roster.AddRange(riders.Select((r, i) => new DemoRider(tags[i], r.Number, r.Name, team, "Open")));

      var paces = riders.Select(_ => 48.0 + rng.NextDouble() * 10.0).ToArray();
      var who = 0;
      // Long enough a first stint for the team to have a pace of its own
      // before the second rider goes out too early.
      var stint = team == twoOnTrack ? 5 : rng.Next(2, 5);
      var inStint = 0;
      var handovers = 0;
      var t = start + k;
      reads.Add(new DemoCrossing(Seconds(t), tags[who]));

      for (var lap = 1; ; lap++)
      {
        double duration;
        if (inStint == stint)
        {
          if (team == waitsAtTheLoop && handovers == 0)
          {
            // The rider going out next is already standing by the loop as the
            // other comes in, and is read every few seconds.
            for (var n = 1; n <= 4; n++)
              reads.Add(new DemoCrossing(Seconds(t + 3.0 * n), tags[1 - who], DemoReadKind.Waiting));
          }

          // The lap with the handover carries the changeover as well.
          who = 1 - who;
          inStint = 0;
          stint = rng.Next(2, 5);
          handovers++;
          duration = paces[who] + Jitter(rng, 1.5) + 6.0 + rng.NextDouble() * 6.0;
        }
        else
        {
          duration = paces[who] + Jitter(rng, 1.5);
        }

        inStint++;

        if (team == twoOnTrack && lap == 4)
          reads.Add(new DemoCrossing(Seconds(t + duration * 0.4), tags[1 - who], DemoReadKind.SecondRiderOut));

        t += duration;
        if (t > until) break;
        reads.Add(new DemoCrossing(Seconds(t), tags[who]));
      }
    }

    for (var j = 0; j < solos.Length; j++)
    {
      var (number, name, club) = solos[j];
      roster.Add(new DemoRider(Tag(number), number, name, club, "Open"));
      var times = GoRound(rng, start + teams.Length + j, 50.0 + rng.NextDouble() * 6.0, 1.5, until);
      reads.AddRange(times.Select(t => new DemoCrossing(Seconds(t), Tag(number))));
    }

    return new DemoScenario
    {
      Id = TeamsId,
      Title = "Team event",
      Length = "About 10 minutes",
      Summary = "Six teams of two and two solo riders race for 8 minutes. The riders of a team take turns on " +
                "track and hand over away from the line.",
      WhatHappens = new[]
      {
        "The clock starts when the first rider crosses the line. Each team is one entry: a lap counts for the " +
        "team whichever of its riders rides it.",
        "Riders hand over every few laps. The lap with the handover is a little longer; that is normal.",
        "After 8 minutes the chequered flag comes out, everyone finishes the lap they are on, and the race " +
        "finishes by itself."
      },
      WhatToTry = new[]
      {
        "Watch the Race Day board show which rider of each team is on track.",
        "MSC Adler send #12 Ben Fischer out while #11 Anna Berger is still riding. The lap shows TWO OUT.",
        "#32 Frank Weber waits right by the loop before his turn and is read again and again. Those reads are " +
        "not counted, and a banner says so.",
        "RC Falke share one transponder: the team is scored the same way, but nobody can tell which of them rode a lap.",
        "When the race has finished, press Results...: the sheet lists the teams, then every rider's laps."
      },
      SessionType = SessionType.Race,
      DurationMinutes = minutes,
      AdditionalLaps = 0,
      TeamEvent = true,
      Roster = roster,
      Crossings = Ordered(reads)
    };
  }

  // ---- Helpers ---------------------------------------------------------------

  /// <summary>Every demo transponder is 2026 and the start number: #7 is 20260007.</summary>
  private static string Tag(string number) => "2026" + number.PadLeft(4, '0');

  private static TimeSpan Seconds(double seconds) => TimeSpan.FromMilliseconds(Math.Round(seconds * 1000));

  private static double Jitter(Random rng, double amount) => (rng.NextDouble() * 2 - 1) * amount;

  /// <summary>
  /// A rider going round: crossings from <paramref name="first"/>, a lap of
  /// <paramref name="pace"/> give or take <paramref name="jitter"/> apart, for
  /// as long as they come before <paramref name="until"/>.
  /// </summary>
  private static List<double> GoRound(Random rng, double first, double pace, double jitter, double until)
  {
    var times = new List<double>();
    for (var t = first; t <= until; t += pace + Jitter(rng, jitter))
      times.Add(t);
    return times;
  }

  private static List<DemoCrossing> Ordered(IEnumerable<DemoCrossing> reads) =>
    reads.OrderBy(r => r.At).ThenBy(r => r.Tag, StringComparer.Ordinal).ToList();
}
