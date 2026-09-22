using System.Globalization;
using System.Text;

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
  public const string ProblemsId = "problems";
  public const string WaitingId = "waiting";
  public const string Lauf1Id = "lauf1";
  public const string Lauf2Id = "lauf2";

  /// <summary>The spare transponder the race demo's forgetful rider borrows. Not on the rider list.</summary>
  public const string SpareTransponder = "20269999";

  /// <summary>The spare the problems demo's rider carries on with after losing his own. Not on the rider list.</summary>
  public const string SwapSpareTransponder = "20269998";

  /// <summary>The marshal's bike that crosses the loop in the problems demo. Not on the rider list.</summary>
  public const string MarshalTransponder = "20269990";

  public static IReadOnlyList<DemoScenario> All { get; } = new[]
  {
    Race(), Qualifying(), Enduro(), Teams(), WaitingForTheLeader(), ProblemsToFix(), Lauf1(), Lauf2()
  };

  public static DemoScenario? Find(string? id) =>
    All.FirstOrDefault(s => string.Equals(s.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));

  /// <summary>Plans a demo afresh.</summary>
  public static DemoScenario Build(string id) => id switch
  {
    RaceId => Race(),
    QualifyingId => Qualifying(),
    EnduroId => Enduro(),
    TeamsId => Teams(),
    WaitingId => WaitingForTheLeader(),
    ProblemsId => ProblemsToFix(),
    Lauf1Id => Lauf1(),
    Lauf2Id => Lauf2(),
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

  // ---- Waiting for the leader ----------------------------------------------

  /// <summary>
  /// What "extra laps: 0" actually means, which is the thing operators get
  /// wrong: the clock does not end the race, it sends the leader out to finish
  /// the lap they are on. Everyone else is flagged when the leader crosses.
  ///
  /// Laid out to the second on purpose, with no jitter. The whole point is a
  /// rider who crosses one second after the clock, so the demo has to show the
  /// same thing every time it is run rather than nearly always.
  /// </summary>
  private static DemoScenario WaitingForTheLeader()
  {
    const int minutes = 5;
    var gap = TimeSpan.FromSeconds(30);
    var classes = new[] { "MX1", "MX2", "Youth" };

    // Pace and first crossing are chosen so that, at the moment the clock runs
    // out, #7 is a second past the line and away on a fresh lap while #111 is
    // a second short of it. Those two are the whole demonstration.
    var field = new (string Number, string Name, string Club, string Class, double Pace, double First)[]
    {
      ("7", "Lukas Brandt", "MSC Adler", "MX1", 42.0, 5.0),
      ("12", "Mia Hoffmann", "RC Falke", "MX1", 44.0, 6.0),
      ("23", "Jan Keller", "MX Team Nord", "MX1", 46.0, 7.0),
      ("31", "Sophie Wagner", "MSC Adler", "MX1", 48.0, 8.0),
      ("88", "Max Schröder", "RSV Blitz", "MX2", 45.0, 6.0),
      ("44", "Tim Schulz", "", "MX2", 47.0, 7.0),
      ("91", "Emma Koch", "RC Falke", "MX2", 49.0, 8.0),
      ("101", "Felix Bauer", "RSV Blitz", "MX2", 51.0, 9.0),
      ("111", "Hanna Klein", "", "Youth", 47.0, 6.0),
      ("124", "Noah Schmitt", "MX Team Nord", "Youth", 50.0, 7.0),
      ("133", "Lina Krüger", "RSV Blitz", "Youth", 53.0, 8.0),
      ("150", "Ben Hartmann", "", "Youth", 56.0, 9.0)
    };

    var reads = new List<DemoCrossing>();

    foreach (var rider in field)
    {
      // One clock runs from the first gate, so it runs out this much earlier in
      // a later class's own race. Reads are anchored to their class's gate.
      var clock = minutes * 60 - Array.IndexOf(classes, rider.Class) * gap.TotalSeconds;

      // Far enough past the flag for the leader to come round and for everyone
      // else to finish the lap they are on afterwards.
      for (var t = rider.First; t <= clock + 2.5 * rider.Pace; t += rider.Pace)
        reads.Add(new DemoCrossing(Seconds(t), Tag(rider.Number), DemoReadKind.Lap, rider.Class));
    }

    return new DemoScenario
    {
      Id = WaitingId,
      Title = "Waiting for the leader",
      Length = "About 8 minutes",
      Summary = "12 riders in three classes, a gate apart, race for 5 minutes with no extra laps. The clock " +
                "does not end the race - it sends the leader out to finish the lap they are on.",
      WhatHappens = new[]
      {
        "MX1 leaves the gate when you press START RACE, MX2 30 seconds later and Youth 30 seconds after that.",
        "When the 5 minutes are up the board reads Time is up - the leader is finishing their lap. The race " +
        "is not over: every rider still out keeps racing.",
        "The flag falls when the leader crosses. Everyone else then finishes the lap they are on, that lap " +
        "counts, and the race finishes by itself."
      },
      WhatToTry = new[]
      {
        "#7 Lukas Brandt crosses the line a second before the clock runs out, so he goes out on a fresh lap. " +
        "Watch the clock reach 00:00 while he is still out - that is normal.",
        "#111 Hanna Klein crosses a second after the clock. She is sent out again, because the race is still " +
        "waiting for Lukas, and the lap she rides counts.",
        "Compare their last lap times on the Results... sheet: Hanna's last lap ends after Lukas finishes, " +
        "not when the clock ran out.",
        "The sheet says Extra laps: none - the flag comes out when the leader finishes the lap in progress."
      },
      SessionType = SessionType.Race,
      DurationMinutes = minutes,
      AdditionalLaps = 0,
      WaveGap = gap,
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
      // Counted from the class's own start. One clock runs from the first
      // gate, so for a later class it runs out earlier in its own race - and
      // then the race waits for the leader, so there are laps to ride after it.
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
                "later and Youth a minute after that. After 10 minutes the race waits for the leader, and " +
                "then everyone finishes the lap they are on.",
      WhatHappens = new[]
      {
        "Nothing moves until you press START RACE: that is the moment MX1 leaves the gate. The application " +
        "sends MX2 and Youth off by itself, a minute apart, and the Race Day screen counts down to each.",
        "Every rider is timed from their own class's start.",
        "After 10 minutes the leader finishes the lap they are on. The flag comes out as they cross, " +
        "everyone else finishes the lap they are on, and the race finishes by itself."
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
        "After 8 minutes the race waits for the leader to finish the lap they are on. The flag comes out as " +
        "they cross, everyone else finishes the lap they are on, and the race finishes by itself."
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

  // ---- Problems to fix -----------------------------------------------------

  /// <summary>
  /// Nearly everything that goes wrong on a race day, one thing after another,
  /// for someone learning to put it right. A team event, because two riders of a
  /// team on track at once is one of those things.
  ///
  /// Each problem is planned at a time of its own, half a minute or more after
  /// the one before, so a volunteer can fix one before the next arrives. The
  /// checklist the demo opens (see <see cref="DemoChecklist"/>) says what each
  /// problem is and ticks it off once it has been put right.
  /// </summary>
  private static DemoScenario ProblemsToFix()
  {
    const int minutes = 12;
    const double start = 5.0;
    const double outageFrom = 560.0;
    const double outageUntil = 645.0;
    var rng = new Random(7070);
    var flag = start + minutes * 60;
    var until = flag + 2.5 * 60;

    var teams = new (string Name, int Stint, (string Number, string Name, double Pace)[] Riders)[]
    {
      ("MSC Adler", 8, new[] { ("11", "Anna Berger", 49.0), ("12", "Ben Fischer", 51.0) }),
      ("RC Falke", 4, new[] { ("21", "Carla Hoff", 52.0), ("22", "David Kern", 50.0) })
    };

    // Club names used once, or none: in a team event those are solo riders.
    var solos = new (string Number, string Name, string Club, double Pace)[]
    {
      ("7", "Lukas Brandt", "RSV Blitz", 50.0),
      ("23", "Jan Keller", "MX Team Nord", 52.0),
      ("57", "Lea Richter", "", 55.0),
      ("88", "Max Schröder", "", 53.0),
      ("68", "Paul Neumann", "", 56.0),
      ("101", "Felix Bauer", "", 54.0)
    };

    var roster = new List<DemoRider>();
    var reads = new List<DemoCrossing>();
    var problems = new List<DemoProblem>();
    var order = 0;

    foreach (var (team, stint, riders) in teams)
    {
      var tags = riders.Select(r => Tag(r.Number)).ToArray();
      roster.AddRange(riders.Select((r, i) => new DemoRider(tags[i], r.Number, r.Name, team, "Open")));

      var t = start + order++ * 0.8;
      var who = 0;
      reads.Add(new DemoCrossing(Seconds(t), tags[who]));

      for (var laps = 0; ; laps++)
      {
        // A handover every few laps, away from the line: that lap carries the changeover.
        var changeover = 0.0;
        if (laps > 0 && laps % stint == 0)
        {
          who = 1 - who;
          changeover = 8.0;
        }

        var duration = riders[who].Pace + Jitter(rng, 1.0) + changeover;

        // Late enough in the first rider's stint for the team to have a pace of
        // its own. Early enough in the lap that the one after it is not short too.
        if (team == "MSC Adler" && laps == 6)
        {
          var early = t + duration * 0.35;
          reads.Add(new DemoCrossing(Seconds(early), tags[1], DemoReadKind.SecondRiderOut));
          problems.Add(new DemoProblem(DemoProblemKind.DeleteSecondRider, Seconds(early),
            $"{team} sent #{riders[1].Number} {riders[1].Name} out while #{riders[0].Number} {riders[0].Name} was still riding",
            "The team shows TWO OUT, and that read is not a real lap. Press Fix laps... (F2), then the Delete " +
            "button at the top.")
          {
            Tag = TeamRoster.KeyFor(team),
            OtherTag = tags[1]
          });
        }

        t += duration;
        if (t > until) break;
        reads.Add(new DemoCrossing(Seconds(t), tags[who]));
      }
    }

    foreach (var (number, name, club, pace) in solos)
    {
      var own = Tag(number);
      var who = $"#{number} {name}";
      roster.Add(new DemoRider(own, number, name, club, "Open"));

      var times = GoRound(rng, start + order++ * 0.8, pace, 1.0, until);

      switch (number)
      {
        case "7":
        {
          var missed = Nearest(times, 190);
          times.RemoveAt(missed);
          problems.Add(new DemoProblem(DemoProblemKind.SplitMissedRead, Seconds(times[missed]),
            $"{who}'s transponder was missed once",
            "His lap shows CHECK. Press Fix laps... (F2), then the Split button at the top.")
          {
            Tag = own,
            Laps = 2,
            Since = Seconds(times[missed - 1])
          });
          break;
        }

        case "23":
        {
          var missed = Nearest(times, 255);
          times.RemoveRange(missed, 2);
          problems.Add(new DemoProblem(DemoProblemKind.SplitMissedRead, Seconds(times[missed]),
            $"{who}'s transponder was missed twice in a row",
            "His lap shows CHECK and looks like three laps. Press Fix laps... (F2), then the Split button at the top.")
          {
            Tag = own,
            Laps = 3,
            Since = Seconds(times[missed - 1])
          });
          break;
        }

        case "57":
          // Her own transponder is on the rider list and never read.
          reads.AddRange(times.Select(t => new DemoCrossing(Seconds(t), SpareTransponder, DemoReadKind.Unknown)));
          problems.Add(new DemoProblem(DemoProblemKind.IdentifySpare, Seconds(times[0]),
            $"{who} rides on a spare transponder that is not on the rider list",
            $"It shows as UNKNOWN ({SpareTransponder}) on the Riders tab. Right-click it, choose Identify this " +
            $"transponder... and pick {who} from the list.")
          {
            Tag = SpareTransponder,
            Number = number
          });
          continue;

        case "88":
        {
          // A stop at the pits for the spare makes that lap a little long, not a missed read.
          var lost = Nearest(times, 335);
          var spareFrom = times[lost] + pace + 18.0;
          times.RemoveRange(lost + 1, times.Count - lost - 1);

          var spare = GoRound(rng, spareFrom, pace, 1.0, until);
          reads.AddRange(spare.Select(t => new DemoCrossing(Seconds(t), SwapSpareTransponder, DemoReadKind.Unknown)));
          problems.Add(new DemoProblem(DemoProblemKind.MergeSpare, Seconds(spareFrom),
            $"{who} lost his transponder and carries on with a spare",
            $"The spare ({SwapSpareTransponder}) shows as UNKNOWN. Right-click it, choose Identify this " +
            $"transponder..., then These laps belong to a rider already in the race, and pick {who}.")
          {
            Tag = own,
            OtherTag = SwapSpareTransponder
          });
          break;
        }

        case "68":
        {
          // Race control calls him in as retired. He had only stopped to fix his
          // chain - a lap not long enough to look like a missed read.
          var stops = Nearest(times, 430);
          var back = times[stops] + pace + 25.0;
          var called = times[stops] + 15.0;
          times.RemoveRange(stops + 1, times.Count - stops - 1);
          times.AddRange(GoRound(rng, back, pace, 1.0, until));

          problems.Add(new DemoProblem(DemoProblemKind.MarkDnf, Seconds(called),
            $"Race control: {who} has pulled off the track",
            "Mark him DNF: right-click him on the Riders tab, Fix laps for him, then Mark as DNF.")
          {
            Tag = own,
            Deadline = Seconds(back),
            Announce = $"Race control: {who} has pulled off the track"
          });
          problems.Add(new DemoProblem(DemoProblemKind.BackInTheRace, Seconds(back),
            $"{who} crosses the line again",
            "He had only stopped to fix his chain. A banner says a rider marked DNF crossed the line: open Fix " +
            "laps for him, press Back in the race, then Count this read on each grey row.")
          {
            Tag = own
          });
          break;
        }

        case "101":
        {
          var twice = times[Nearest(times, 150)] + 4.0;
          reads.Add(new DemoCrossing(Seconds(twice), own, DemoReadKind.TooSoon));
          problems.Add(new DemoProblem(DemoProblemKind.ReadTwice, Seconds(twice),
            $"{who} was read twice in one pass",
            "Nothing to fix: the second read is not counted. Fix laps shows it as a grey row.")
          {
            Tag = own
          });

          // Pulls off for good a few minutes before the outage. Not near the end: a
          // rider who stops within a lap or two of the flag still looks due at the
          // line once the rest of the field has finished and nothing more is read,
          // and the finish sounded the no-reads alarm for him - straight after the
          // outage has taught what that alarm means.
          var last = Nearest(times, 420);
          times.RemoveRange(last + 1, times.Count - last - 1);
          problems.Add(new DemoProblem(DemoProblemKind.Retires, Seconds(times[^1] + pace),
            $"{who} retires",
            "Nothing to do: once the finish has waited long enough for him, he becomes DNF by himself.")
          {
            Tag = own
          });
          break;
        }
      }

      reads.AddRange(times.Select(t => new DemoCrossing(Seconds(t), own)));
    }

    // A marshal's bike, over the loop and back again.
    reads.Add(new DemoCrossing(Seconds(95.0), MarshalTransponder, DemoReadKind.Stray));
    reads.Add(new DemoCrossing(Seconds(138.5), MarshalTransponder, DemoReadKind.Stray));
    problems.Add(new DemoProblem(DemoProblemKind.StopCounting, Seconds(95.0),
      "A marshal's bike crossed the loop",
      $"Transponder {MarshalTransponder} is nobody on the rider list. Right-click it on the Riders tab and " +
      "choose Stop counting it.")
    {
      Tag = MarshalTransponder
    });

    // The reader goes quiet: nothing at all for a minute and a half.
    reads.RemoveAll(r => r.At > Seconds(outageFrom) && r.At < Seconds(outageUntil));

    var teamOf = roster
      .Where(r => teams.Any(t => t.Name == r.Team))
      .ToDictionary(r => r.Tag, r => TeamRoster.KeyFor(r.Team));

    var cutShort = reads
      .Where(r => r.Kind is DemoReadKind.Lap or DemoReadKind.Unknown)
      .GroupBy(r => teamOf.GetValueOrDefault(r.Tag, r.Tag))
      .Where(g => g.Any(r => r.At < Seconds(outageFrom)) && g.Any(r => r.At > Seconds(outageUntil)))
      .Select(g => g.Key)
      .ToList();

    problems.Add(new DemoProblem(DemoProblemKind.ReaderOutage, Seconds(outageFrom),
      "The reader goes quiet",
      "Nothing is read. Watch the READER tile turn orange, then red with a banner, as riders are due at the " +
      "line. It comes back by itself after about a minute and a half.")
    {
      Until = Seconds(outageUntil)
    });
    problems.Add(new DemoProblem(DemoProblemKind.SplitAfterOutage, Seconds(outageUntil),
      "The outage left every rider with a long lap",
      "Press Fix laps... (F2) and the Split button at the top, then F2 again for the next rider, until " +
      "nobody is left.")
    {
      Since = Seconds(outageFrom),
      Until = Seconds(outageUntil),
      Tags = cutShort
    });

    return new DemoScenario
    {
      Id = ProblemsId,
      Title = "Problems to fix",
      Length = "About 16 minutes",
      Summary = "Two teams and six solo riders race for 12 minutes - and nearly everything that goes wrong on a " +
                "race day does, one thing after another, for you to put right.",
      WhatHappens = new[]
      {
        "The clock starts when the first rider crosses the line. After 12 minutes the race waits for the " +
        "leader, then everyone finishes the lap they are on, and the race finishes by itself.",
        "A new problem turns up every minute or so: missed reads, transponder mix-ups, two riders of a team on " +
        "track at once, a rider reported retired who is not, and a reader that goes quiet."
      },
      WhatToTry = new[]
      {
        "Keep Problems to fix open beside the race - it opens with the demo, and again from the DEMO bar. Each " +
        "problem appears there as it happens, with what to do, and is ticked off once it is put right.",
        "Fix laps... (F2) opens the rider who needs it most, and the fix is usually the button at the top.",
        "When the race has finished, press Results... for the sheet."
      },
      SessionType = SessionType.Race,
      DurationMinutes = minutes,
      AdditionalLaps = 0,
      TeamEvent = true,
      Roster = roster,
      Crossings = Ordered(reads),
      Problems = problems.OrderBy(p => p.At).ToList()
    };
  }

  // ---- Two real races --------------------------------------------------------

  /// <summary>
  /// The morning race of 20 September 2026, as the club's own timing recorded
  /// it: every crossing the reader sent, at the moment it sent it. Nothing is
  /// planted - what went wrong that day goes wrong again, in real time.
  /// </summary>
  private static DemoScenario Lauf1()
  {
    var day = ReadRealRace(Lauf1Id);

    return new DemoScenario
    {
      Id = Lauf1Id,
      Title = "A real race: Lauf 1",
      Length = "About 2 hours 20 minutes",
      Summary = "81 riders in five classes, timed on 20 September 2026 and replayed read for read - the riders' " +
                "names hidden. Two hours and no extra laps, one start for everyone.",
      WhatHappens = new[]
      {
        "Press START RACE: that is the moment the field left the gate. The first riders reach the line about " +
        "a minute and a half later, and a lap takes ten to twelve minutes.",
        "After 2 hours the race waits for the leader to finish the lap they are on - their thirteenth - and " +
        "then everyone finishes theirs. Seven riders pull in for good along the way and end up DNF.",
        "Nothing here is made up. Every read is one the reader sent that day, with the gaps it left."
      },
      WhatToTry = new[]
      {
        "Watch the READER tile: with laps this long the reader is quiet for a minute at a time, and the " +
        "application judges from the lap times whether anyone is overdue. Early on, a few are.",
        "Some laps are long enough to be two or more and show CHECK. Open Fix laps... and judge for " +
        "yourself: a missed read, or a rider who stopped for a while?",
        "Nothing to do for the riders who pull in: once the leader is home and the grace has run out, " +
        "they become DNF by themselves.",
        "When the race has finished, press Results... for the sheet. The names are hidden the way the " +
        "website hides them for a rider who asks: Ale****** Sch******."
      },
      SessionType = SessionType.Race,
      DurationMinutes = 120,
      AdditionalLaps = 0,
      ManualStart = true,
      Roster = day.Roster,
      Crossings = day.Reads
    };
  }

  /// <summary>The afternoon race of the same day, started in three waves a minute apart.</summary>
  private static DemoScenario Lauf2()
  {
    var day = ReadRealRace(Lauf2Id);

    return new DemoScenario
    {
      Id = Lauf2Id,
      Title = "A real race: Lauf 2",
      Length = "About 2 hours 25 minutes",
      Summary = "119 riders in three classes a minute apart, timed on 20 September 2026 and replayed read for " +
                "read - the riders' names hidden. Two hours and no extra laps.",
      WhatHappens = new[]
      {
        "Press START RACE to send 1_expert off. The application sends 2_racer and 3_senior1 off by itself, " +
        "a minute apart, and every rider is timed from their own class's start.",
        "After 2 hours the race waits for the leader to finish the lap they are on - their sixteenth - and " +
        "then everyone finishes theirs. Ten riders pull in for good along the way and end up DNF.",
        "Nothing here is made up. Every read is one the reader sent that day, with the gaps it left."
      },
      WhatToTry = new[]
      {
        "Two riders in 2_racer both carry #123. Their transponders tell them apart, and so does the " +
        "application.",
        "#128 is on the rider list and never goes out. The sheet lists them last.",
        "#166's transponder is missed twice, so two of their laps show CHECK - and later that afternoon " +
        "race control reported them retired. Right-click them on the Riders tab, Fix laps, then Mark as DNF.",
        "When the race has finished, press Results... for the sheet. The names are hidden the way the " +
        "website hides them for a rider who asks: Hen**** Van**********."
      },
      SessionType = SessionType.Race,
      DurationMinutes = 120,
      AdditionalLaps = 0,
      WaveGap = TimeSpan.FromMinutes(1),
      Roster = day.Roster,
      Crossings = day.Reads
    };
  }

  /// <summary>A real race's file, parsed. See <see cref="ReadRealRace"/>.</summary>
  internal sealed record RealRace(IReadOnlyList<DemoRider> Roster, IReadOnlyList<DemoCrossing> Reads,
    IReadOnlyDictionary<string, int> LapsOnTheSheet);

  /// <summary>
  /// Reads a real race from demo_data/, embedded in the application like the
  /// help pictures. One line per rider - transponder, number, class, the laps
  /// on the day's sheet, name - and one per read, counted in seconds from the
  /// moment the rider's class left the gate. The riders' names are hidden in
  /// the files themselves, the way <see cref="NamePrivacy"/> hides a name on
  /// the website, so a real name is never in the application at all; a test
  /// holds every name to that shape.
  /// </summary>
  internal static RealRace ReadRealRace(string id)
  {
    using var stream = typeof(DemoScenarios).Assembly.GetManifestResourceStream($"demo_data/{id}.txt")
      ?? throw new InvalidOperationException($"The real race {id} is not embedded in the application.");
    using var text = new StreamReader(stream, Encoding.UTF8);

    var roster = new List<DemoRider>();
    var laps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var reads = new List<DemoCrossing>();

    // A read is anchored to its class's gate only when the race has waves; with
    // one start it counts from START RACE, which is what null means to the
    // demo reader. Riders come before reads in the file, so the class is known.
    var waved = false;
    var classOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    while (text.ReadLine() is { } line)
    {
      if (line.Length == 0 || line[0] == '#') continue;
      var fields = line.Split('\t');

      switch (fields[0])
      {
        case "rider":
          roster.Add(new DemoRider(fields[1], fields[2], fields[5], "", fields[3]));
          laps[fields[1]] = int.Parse(fields[4], CultureInfo.InvariantCulture);
          classOf[fields[1]] = fields[3];
          break;

        case "read":
          var at = Seconds(double.Parse(fields[1], CultureInfo.InvariantCulture));
          reads.Add(new DemoCrossing(at, fields[2], DemoReadKind.Lap, waved ? classOf[fields[2]] : null));
          break;

        case "waves":
          waved = true;
          break;

        default:
          throw new InvalidDataException($"demo_data/{id}.txt: a line starts with \"{fields[0]}\".");
      }
    }

    return new RealRace(roster, Ordered(reads), laps);
  }

  // ---- Helpers ---------------------------------------------------------------

  /// <summary>Which of <paramref name="times"/> is closest to <paramref name="target"/>.</summary>
  private static int Nearest(IReadOnlyList<double> times, double target) =>
    Enumerable.Range(0, times.Count).MinBy(i => Math.Abs(times[i] - target));

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
