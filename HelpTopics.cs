using static CrossMgrInterface.HelpBlock;

namespace CrossMgrInterface;

/// <summary>
/// The words of the in-application help, one topic per page.
///
/// Written for the volunteer in the timing tent, in the words the application
/// itself uses on its buttons and screens, and describing what it actually does -
/// including the parts that surprise people, such as a race only noticing the
/// clock has run out on the next crossing. When behaviour changes, change the
/// topic that describes it.
/// </summary>
public static class HelpTopics
{
  private const string GettingStarted = "Getting started";
  private const string SessionTypes = "Session types";
  private const string DuringASession = "During a session";
  private const string AfterASession = "After a session";
  private const string Reference = "Setup and reference";

  /// <summary>The groups in the order the help window lists them.</summary>
  public static IReadOnlyList<string> Groups { get; } =
    new[] { GettingStarted, SessionTypes, DuringASession, AfterASession, Reference };

  public static IReadOnlyList<HelpTopic> All { get; } = new[]
  {
    QuickStart(), Demo(), Screens(),
    Race(), Qualifying(), Practice(), Waves(), Teams(),
    RaceDay(), Fixing(), Unknown(), TransponderCheck(), Track(),
    Results(), Publish(), PastSessions(),
    RiderLists(), Reader(), Settings(), Shortcuts(), Troubleshooting()
  };

  public static HelpTopic? Find(string id) => All.FirstOrDefault(t => t.Id == id);

  private static HelpTopic Topic(string id, string group, string title, string summary,
    HelpBlock[] blocks, params string[] seeAlso) =>
    new(id, group, title, summary, blocks, seeAlso);

  // ---- Getting started -----------------------------------------------------

  private static HelpTopic QuickStart() => Topic(HelpTopicIds.QuickStart, GettingStarted,
    "Quick start",
    "The steps for any session, from switching on to printing the sheet.",
    new[]
    {
      Heading("Before the session"),
      Steps(
        "Connect the transponder reader to this computer. The READER tile on the Race Day screen turns " +
        "green (Reader connected) once it has connected. If it stays grey, use Reader > Start reader connection.",
        "Press Set up race... on the Race Day screen, or Race > New race... (Ctrl+N). The wizard asks, in " +
        "order: what kind of session it is (race, timed qualifying or free practice), its name, the rider " +
        "list, how long it runs, and how the clock starts.",
        "On the Riders step press Choose file... and pick the rider list (Excel or CSV). Check the preview: " +
        "every rider needs a transponder. For a team race, tick Team event here.",
        "Press Finish. The SET UP checklist on the Race Day screen shows the name, the riders, the length " +
        "and the reader."),
      Picture("new-race-wizard", "The wizard's Riders step, with a rider list loaded."),

      Heading("During the session"),
      Steps(
        "If you chose to start the clock yourself, press START RACE (START SESSION) the moment the gate " +
        "drops - or F5. Otherwise the clock starts on the first transponder read.",
        "Watch the Race Day screen: time left, the state of the session, the reader and the top ten. A " +
        "banner at the top tells you about anything that needs attention.",
        "If a lap looks wrong - a rider shows CHECK or TWO OUT on the Riders tab - press Fix laps... (F2) and " +
        "then the suggested fix at the top of the window. Every change can be undone with Ctrl+Z.",
        "When the clock runs out the application runs the finish: the flag, the last laps, and the riders " +
        "who do not come round. Wait for Race finished (Session over)."),

      Heading("After the session"),
      Steps(
        "Press Results... (Ctrl+P) - or Gate pick order... after qualifying - and choose Preview, Print " +
        "or Export.",
        "Press NEW SESSION... for the next one. The finished session is kept: Race > Past sessions... " +
        "(Ctrl+O) prints it again at any time."),

      Tip("Each session type has its own topic under Session types: how to set it up, what happens when " +
          "the clock runs out, and what its sheet shows."),
      Tip("No reader to hand? Help > Try a demo race... runs a whole session with simulated riders, in a " +
          "window of its own.")
    },
    HelpTopicIds.Race, HelpTopicIds.Qualifying, HelpTopicIds.Practice, HelpTopicIds.RaceDay, HelpTopicIds.Fixing,
    HelpTopicIds.Demo);

  private static HelpTopic Demo() => Topic(HelpTopicIds.Demo, GettingStarted,
    "Try a demo race",
    "Watch the application time a whole session - simulated riders, or a real race replayed - with no " +
    "reader and no rider list needed.",
    new[]
    {
      Para("Help > Try a demo race... lists the demos; so does the link under Set up race... on the Race Day " +
           "screen. Choose one and press Start demo. It opens in a window of its own, with a yellow DEMO bar " +
           "along the top."),

      Heading("The demos"),
      Picture("demo-picker", "Help > Try a demo race..."),
      Bullets(
        "A short motocross race - 16 riders in two classes, 6 minutes and one lap, with a missed read, a rider " +
        "on a spare transponder, a pass read twice and a rider who retires. About 10 minutes.",
        "Qualifying to gate pick - ten riders, 6 minutes of timed qualifying, a lap that ends after the flag " +
        "and still counts, and two riders with the same best lap. About 7 minutes.",
        "Enduro in waves - 24 riders in three classes a minute apart. You press START RACE. About 13 minutes.",
        "Team event - six teams of two and two solo riders, with handovers, two riders of one team out at " +
        "once, and a rider waiting by the loop. About 10 minutes.",
        "Waiting for the leader - 12 riders in three classes, 5 minutes and no extra laps. Shows what the " +
        "clock running out actually does: it sends the leader out to finish the lap they are on, and a " +
        "rider who crosses a second after it still gets another lap. About 8 minutes.",
        "Problems to fix - two teams and six solo riders, and nearly everything that goes wrong on a race day, " +
        "one thing after another: missed reads, transponder mix-ups, a rider reported retired who is not, and " +
        "a reader that goes quiet. A checklist says what each problem is and ticks it off once it is put " +
        "right. About 16 minutes.",
        "A real race: Lauf 1 - 81 riders in five classes, timed by the club on 20 September 2026 and " +
        "replayed read for read, with the riders' names hidden. Two hours and no extra laps, one start " +
        "for everyone. You press START RACE. About 2 hours 20 minutes.",
        "A real race: Lauf 2 - the afternoon race of the same day: 119 riders in three classes a minute " +
        "apart. About 2 hours 25 minutes."),

      Heading("How a demo runs"),
      Bullets(
        "In real time: a demo takes as long as the session it shows - the two real races take the two " +
        "hours and more they took on the day.",
        "A card at the start says what is going to happen and what to try. What's happening? on the DEMO bar " +
        "shows it again. In Problems to fix, the checklist opens beside the race, and again from the DEMO bar.",
        "Everything works as it does on a race day: fixing laps, identifying a transponder, undo, the results " +
        "and the gate pick order.",
        "Close the demo's window to end it. Start it again from the Help menu to see it again."),
      Tip("A demo keeps everything in a folder of its own. Your real races, rider lists and reader settings are " +
          "never touched, and a session being timed in the main window carries on undisturbed.")
    },
    HelpTopicIds.QuickStart, HelpTopicIds.Race, HelpTopicIds.Qualifying, HelpTopicIds.Waves, HelpTopicIds.Teams);

  private static HelpTopic Screens() => Topic(HelpTopicIds.Screens, GettingStarted,
    "The screens",
    "What each tab is for. The normal view shows what a volunteer needs; the advanced tabs are for " +
    "diagnosis and settings.",
    new[]
    {
      Bullets(
        "Race Day (Ctrl+1) - the live view: time left, the session's state, the reader, the top ten, and " +
        "the buttons for starting, ending, fixing and printing.",
        "Riders (Ctrl+2) - every rider with laps and times, and a Status column. Right-click a rider to fix " +
        "laps, identify a transponder or stop counting it. Filter by: shows one class.",
        "Qualifying - timed qualifying only: the gate pick order as it stands.",
        "Transponders - timed qualifying and free practice only: transponders that are read badly or not at all.",
        "Track (Ctrl+3) - the circuit on a map with every rider's estimated position."),

      Heading("Advanced tabs"),
      Para("View > Show advanced tabs (Ctrl+Shift+A) adds these. The choice is remembered."),
      Bullets(
        "Race Events - everything that happened: starts, flags, warnings, retirements, corrections, and in " +
        "races the passes and lappings.",
        "Tag Events - the raw reader feed: every read as it arrived, including reads that were filtered " +
        "out or not counted.",
        "Race Statistics - race time, riders and laps, the next rider due, and a prediction of the leader's laps.",
        "Lap Chart - a bar for every lap of every rider along the race's time. Hover for a lap time; click a " +
        "rider to fix their laps.",
        "Lap Progression - positions lap by lap: green gained a place, pink lost one.",
        "Race Settings - the rules of the session on screen. See The Race Settings tab."),

      Heading("Status bar"),
      Para("The bar along the bottom shows the reader, the last read, the session's state, riders and laps, " +
           "and repeats the latest banner so it can be seen from every tab.")
    },
    HelpTopicIds.RaceDay, HelpTopicIds.Settings);

  // ---- Session types -------------------------------------------------------

  private static HelpTopic Race() => Topic(HelpTopicIds.Race, SessionTypes,
    "Running a race",
    "Ranked on laps completed, then on time. When the clock runs out the leader finishes the lap they are " +
    "on plus any extra laps, and everyone else finishes the lap they are on.",
    new[]
    {
      Heading("Setting up"),
      Steps(
        "Race > New race... (Ctrl+N) and choose Race.",
        "Name it. The name is printed on the results sheet.",
        "Choose the rider list. Tick Team event only for a team race - see Team events.",
        "Set the length in minutes and the extra laps: how many more laps the leader rides after the clock " +
        "runs out, on top of the lap in progress. 0 means the leader rides only the lap they are on.",
        "Choose when the clock starts: When the first rider crosses the line (right when the start is at the " +
        "timing loop), I will press Start Race myself (right when the gate is somewhere else), or The classes " +
        "start in waves (for an enduro - see Classes starting in waves).",
        "Press Finish and check the SET UP checklist on the Race Day screen."),

      Heading("Starting"),
      Para("With an automatic start the first transponder read starts the clock, and that crossing counts " +
           "as that rider's first lap. With a manual start press START RACE (F5) when the gate drops; reads " +
           "before that are ignored. The race is stored from the moment its clock starts. Banners warn at " +
           "5 minutes and 1 minute to go."),

      Heading("When the clock runs out"),
      Bullets(
        "A race notices that the clock has run out on the next crossing. Until someone crosses, the board " +
        "can show 00:00 and still say Race running - that is normal.",
        "The leader - most laps, then least time - then rides the lap they are on plus the extra laps. When " +
        "they reach that target the banner reads Leader has finished - everyone else completes their current lap.",
        "With 0 extra laps the leader still rides the lap they are on - the race always waits for them to come " +
        "round, and the flag falls when they do, not when the clock hits zero.",
        "Every other rider may then complete exactly one more lap: the lap they are on when the leader finishes. " +
        "A read after that is not counted.",
        "A rider who does not come round is marked DNF once the finish timeout has passed: the Time to finish " +
        "after the leader on the Race Settings tab (2 minutes unless changed), or one and a half laps of the " +
        "field's typical pace if that is longer.",
        "The race is finished when nobody is still out: Race finished - results are final."),

      Heading("Ending early"),
      Para("End race now... (Ctrl+E) ends the race on your word. Riders who have not finished their last lap " +
           "are scored DNF; before the clock has run out it simply freezes everyone's laps."),

      Heading("How it is ranked"),
      Para("Most laps first, then shortest total time. Total time runs from the rider's own start - the race " +
           "start, or their class's start in a race in waves. DNF riders come after everyone still classified, " +
           "and riders marked DNS after them."),

      Heading("The sheet"),
      Para("Results... (Ctrl+P) prints the classification: an overall sheet and, when the rider list has " +
           "more than one class, one sheet per class. See Results and sheets."),

      Tip("The length can still be changed on the Race Settings tab while the race runs; the end time moves " +
          "with it. Riders who crossed before the rider list was loaded are timed as UNKNOWN and named as " +
          "soon as the list is imported.")
    },
    HelpTopicIds.Waves, HelpTopicIds.Teams, HelpTopicIds.Results, HelpTopicIds.Fixing, HelpTopicIds.RaceDay);

  private static HelpTopic Qualifying() => Topic(HelpTopicIds.Qualifying, SessionTypes,
    "Timed qualifying",
    "Scored on each rider's best lap. The result is the gate pick order: the order in which riders choose " +
    "their starting gate for the race.",
    new[]
    {
      Heading("Setting up"),
      Steps(
        "Race > New race... (Ctrl+N) and choose Timed qualifying.",
        "Name it, choose the rider list and set the length. A timed session has no extra laps.",
        "Choose how the clock starts. Classes in waves and team events are for races only."),

      Heading("During the session"),
      Bullets(
        "The Qualifying tab shows the gate pick order as it stands. Class: shows one class, numbered from 1 " +
        "within that class. Double-click a rider to look at their laps.",
        "The Race Day board ranks on best lap: Pick, #, Rider, Laps, Best lap, Gap to pole. A rider without " +
        "a time shows NO TIME.",
        "The Transponders tab shows any transponder that is being read badly - there is still time to fix " +
        "it before the race. See Transponder check."),

      Heading("When the clock runs out"),
      Bullets(
        "The flag comes out from the clock itself, without anyone having to cross. The banner reads " +
        "Chequered flag - finish the lap you are on.",
        "Every rider out on track finishes the lap they are on, and that lap counts - it can still take pole.",
        "Anything after that lap is not counted.",
        "A rider who had already pulled in before the flag is marked as off track straight away. Their times still count.",
        "A rider still out who does not come round within the finish timeout (at least one and a half laps of " +
        "the field's pace) loses only that lap. Any time they already set still counts.",
        "Session over - the gate pick order is final."),

      Heading("How the gate pick order is decided"),
      Bullets(
        "Fastest single lap first. The first lap - from the start to the rider's first crossing - never counts.",
        "Two riders with exactly the same best lap: whoever set it first picks first.",
        "After everyone with a time come the riders who crossed the loop but never completed a timed lap " +
        "(out-lap only), then the riders on the list who never went out. Both groups are in start number order.",
        "A rider marked DNS is listed as did not start, with those who never went out.",
        "A rider marked DNF keeps their time and their pick.",
        "Gap is the time behind pole; Int is the time behind the rider ahead."),

      Heading("The sheet"),
      Para("Gate pick order... on the Race Day screen, the Qualifying tab or the Race menu prints Pick, #, " +
           "Rider, Class, Best lap, Gap, Int, On lap and Laps. A rider whose laps were corrected or carry a " +
           "warning is marked (check laps). Riders without a time are listed last with the reason. With " +
           "more than one class there is an overall sheet and one per class."),

      Tip("CHECK on the Qualifying tab means a lap was corrected or looks wrong. Look at it before the sheet " +
          "is handed out.")
    },
    HelpTopicIds.TransponderCheck, HelpTopicIds.Fixing, HelpTopicIds.Results, HelpTopicIds.Practice);

  private static HelpTopic Practice() => Topic(HelpTopicIds.Practice, SessionTypes,
    "Free practice",
    "Timed like qualifying, but not ranked. Use it to warm up, and to find transponders that are not being " +
    "read before the sessions that matter.",
    new[]
    {
      Heading("Setting up"),
      Steps(
        "Race > New race... (Ctrl+N) and choose Free practice.",
        "Name it, choose the rider list and set the length.",
        "Choose how the clock starts."),

      Heading("During the session"),
      Bullets(
        "Watch the Transponders tab. It lists every rider who has never been read, has laps missing, is read " +
        "twice per pass, or went quiet. Get those tags fixed while the riders are still in the paddock.",
        "The Race Day board and the Riders tab show laps and times. After the flag a rider who is no longer " +
        "on track reads off track on the board and OFF on the Riders tab - their times still count."),

      Heading("When the clock runs out"),
      Para("The same as timed qualifying: the flag comes out from the clock, everyone finishes the lap they " +
           "are on and it counts, and nothing after it does. The session is over when nobody is still out."),

      Heading("Sheets"),
      Bullets(
        "Race > Transponder check... is the sheet worth printing after practice. See Transponder check.",
        "Results... prints the laps and times in race order - most laps, then time. It is not a ranking, and " +
        "nobody is marked DNF on it: a rider who pulled in before the flag has simply stopped."),

      Tip("Team events are for races. In practice every transponder is checked on its own, which is exactly " +
          "what is wanted before a team race.")
    },
    HelpTopicIds.TransponderCheck, HelpTopicIds.Qualifying);

  private static HelpTopic Waves() => Topic(HelpTopicIds.Waves, SessionTypes,
    "Classes starting in waves (enduro)",
    "A race in which the classes leave the start one after another, and every rider is timed from their " +
    "own class's start.",
    new[]
    {
      Heading("Setting up"),
      Steps(
        "Import the rider list first - the classes come from its class column.",
        "Race > New race... (Ctrl+N), choose Race, and on the Start step choose The classes start in waves.",
        "Put the classes in their start order with Move up and Move down, and set how many minutes each " +
        "class starts after the previous one. Gap between classes with Apply to every class sets them all at " +
        "once. The first class goes on START RACE, so it has no gap.",
        "Press Finish. The same order and gaps are offered again next time."),

      Heading("During the race"),
      Bullets(
        "Press START RACE (F5) when the first class leaves the gate. That starts the clock and the first class.",
        "The strip on the Race Day screen shows each class: started with its time, the next one counting " +
        "down (MX2 in 0:45), and the ones after it. The application starts each class on time by itself.",
        "START <class> NOW sends the next class immediately, when its gate drops early. Only the next class " +
        "in order can go.",
        "Each class is due its gap after the previous class actually left, not at a fixed time of day.",
        "A read from a rider whose class has not started yet is ignored: that is a bike being wheeled over the loop.",
        "Riders with no class, or with a class that is not in the order, start with the first class."),

      Heading("Timing and the finish"),
      Bullets(
        "Every rider is timed from their own class's start: their first lap, total time and place are all " +
        "measured from it.",
        "Changing a rider's class afterwards with Fix laps... does not re-time them - they keep the start they " +
        "rode from, because they left the gate they left. It is the right fix for a rider on the wrong row of " +
        "the rider list. A class that has not left the gate yet cannot be chosen: a rider in it would stop " +
        "being counted until it goes.",
        "One clock runs from the first start, and one flag ends the race for everyone, as in any race.",
        "The overall sheet ranks on laps, then on time from each rider's own start. The class sheets compare " +
        "riders who started together. The board shows each rider's class beside their name."),

      Tip("A race in waves is always started by hand. A team event starts together, so it cannot start in waves.")
    },
    HelpTopicIds.Race, HelpTopicIds.Results);

  private static HelpTopic Teams() => Topic(HelpTopicIds.Teams, SessionTypes,
    "Team events",
    "Several riders form a team and take turns on track. The team is scored as one entry, whichever of " +
    "them rides a lap.",
    new[]
    {
      Heading("The rider list"),
      Bullets(
        "Give every rider their own row and start number, and the same name in the team column for everyone " +
        "in a team.",
        "A team name used by only one rider - or an empty team column - is a solo rider, who races normally " +
        "alongside the teams.",
        "Team names match ignoring upper and lower case and extra spaces.",
        "A team is shown under its name with its riders' numbers, for example #11/14 MSC Adler."),

      Heading("Setting up"),
      Steps(
        "Race > New race... (Ctrl+N) and choose Race.",
        "On the Riders step choose the rider list and tick Team event. The Races as column shows the team " +
        "each rider rides for, and the line underneath says what the list makes, for example 12 teams, 3 solo riders.",
        "Read the warnings under the preview. A transponder on the rows of two different teams, or of a team " +
        "and a solo rider, stops the wizard: correct the list and choose it again. A team with riders in more " +
        "than one class is scored in the class most of them are in.",
        "Press Finish. A team event starts together; the classes cannot start in waves."),
      Tip("Tick Team event only for a team race. On an ordinary rider list the team column usually holds the " +
          "club, and every rider of a club would be scored as one team. A team of more than six riders is " +
          "warned about for that reason."),

      Heading("Transponders"),
      Bullets(
        "Each rider on their own transponder: the application knows who crossed the line. The Riders tab and " +
        "the Race Day board show who is on track, the results give every rider's laps and times, and two " +
        "riders of a team out at once is spotted.",
        "The whole team on one shared transponder: the team is scored exactly the same way, but nobody can " +
        "tell which rider rode a lap - so there are no times per rider and no two-on-track warning.",
        "Both can be mixed within one team."),

      Heading("Handing over"),
      Bullets(
        "The rider coming in leaves the track and hands over away from the timing loop, and the next rider " +
        "then goes out. The lap with the handover is a little longer; that is normal.",
        "Riders waiting to take over must stay out of reader range. Reads of a waiting rider are not counted, " +
        "and if one keeps being read the banner says they are waiting too close to the loop.",
        "TWO OUT on the Riders tab (TWO ON TRACK? in Race Events) means a different rider of the team crossed " +
        "far too soon after the last one, so two of them look to have been out at once. Nothing is removed by " +
        "itself: Fix laps offers to delete the lap that is not real - or to keep it, if it was real."),

      Heading("Results"),
      Para("The results list each team with its riders under its name, followed by a Team Members section: " +
           "for every rider the laps they rode, their best lap and their average lap. The first lap and the " +
           "handover laps are left out of a rider's times. The Excel export adds a Team Members sheet and " +
           "says who rode every lap."),

      Heading("A spare or unknown transponder"),
      Para("A rider on a spare transponder that is not on the list shows as UNKNOWN. Right-click it, choose " +
           "Identify this transponder..., then This transponder belongs to a team, and pick the team and the " +
           "rider. Its laps move to the team and later reads go there too. If a team rider's transponder was " +
           "read before the rider list was loaded, a banner says so and the team is already chosen.")
    },
    HelpTopicIds.Race, HelpTopicIds.Fixing, HelpTopicIds.Unknown, HelpTopicIds.Results);

  // ---- During a session ----------------------------------------------------

  private static HelpTopic RaceDay() => Topic(HelpTopicIds.RaceDay, DuringASession,
    "The Race Day screen",
    "The screen to watch during a session. It says what state the session is in and offers only the " +
    "buttons that make sense at that moment.",
    new[]
    {
      Picture("race-day", "The Race Day screen during a race."),

      Heading("The tiles"),
      Bullets(
        "TIME LEFT counts down while the session runs - dark red with five minutes left, red with one. " +
        "Afterwards it shows the final time.",
        "RACE shows the state: Waiting for first rider, Ready to start, Race running (Session running), Last " +
        "laps, Finishing (Chequered flag), Race finished (Session over). The line underneath says what happens next.",
        "READER is green while the reader is connected and reading. During a session it turns orange when a " +
        "rider due at the line has not come and nothing has been read, and red - with a banner - once several " +
        "have: check the reader and the loop. It stays green while nobody is still expected, such as after the " +
        "flag while the finish waits for a rider who retired. To go by a fixed time instead, see The " +
        "transponder reader.",
        "LIVE says whether the running race is going to the live timing website, with a Switch on / " +
        "Switch off button. Grey when off, green while sending, amber when an update did not get through, " +
        "red when the website has not been reached for a while. See Publishing results to the website."),

      Heading("The buttons"),
      Bullets(
        "START RACE / START SESSION (F5) - only when the clock is started by hand and has not started yet.",
        "START <class> NOW - only in a race in waves, while a class is still waiting.",
        "End race now... / End session now... (Ctrl+E) - ends the session on your word.",
        "Fix laps... (F2) - opens the rider who most needs it (TWO OUT before CHECK, the leaders first), or says " +
        "there is nothing to fix.",
        "Results... / Gate pick order... - turns green once the session is over.",
        "NEW SESSION... - after the session: sets up the next one. The finished one is kept.",
        "Set up race... / Set up session... - opens the wizard. A session that is still running is ended first, " +
        "after asking."),

      Heading("The board, the checklist and the banners"),
      Bullets(
        "The board shows the top ten. Everyone else is on the Riders tab.",
        "The SET UP checklist shows the name, how many riders are imported (entries, in a team event), the " +
        "length, and whether the reader is connected.",
        "Banners: blue for information (it goes by itself), gold for a warning, red for something urgent - it " +
        "beeps and stays until OK is pressed. The status bar repeats the latest one.")
    },
    HelpTopicIds.QuickStart, HelpTopicIds.Fixing, HelpTopicIds.Reader);

  private static HelpTopic Fixing() => Topic(HelpTopicIds.Fixing, DuringASession,
    "Fixing laps",
    "Transponder timing goes wrong in a few predictable ways, and each can be fixed during the session. " +
    "Every change can be undone.",
    new[]
    {
      Heading("Opening it"),
      Bullets(
        "Right-click a rider on the Riders tab and choose Fix laps for ..., or double-click the rider.",
        "Riders > Fix laps... (F2) or Fix laps... on the Race Day screen opens the rider who most needs it: two " +
        "riders on track (TWO OUT) before a missed read (CHECK), and the leaders first. Press it again after each " +
        "fix for the next one."),

      Heading("Putting the Riders tab in a different order"),
      Para("The grid is in race order: first on laps, then on time. Click any column heading to sort by it " +
           "instead, and again to turn it round. Click Pos to go back to race order. The position and the " +
           "podium colours always mean where a rider is in the race, whatever order you are reading in."),
      Para("Last read is the time of day the loop last saw each rider. Sorting by it brings whoever stopped " +
           "earliest to the top, which is the quickest way to find a rider whose result looks wrong - someone " +
           "who pulled in an hour ago should not be among the finishers."),

      Heading("The Status column on the Riders tab"),
      Keys(
        ("DNF", "did not finish: timed out after the flag, or marked by hand"),
        ("DNS", "did not start: marked by hand"),
        ("OFF", "timed session: no longer on track after the flag - times already set still count"),
        ("TWO OUT", "team event: two riders of the team look to have been on track at once"),
        ("CHECK", "a lap looks long enough to be a missed read"),
        ("FIXED", "a long lap was split to make up for a missed read"),
        ("UNKNOWN", "this transponder is not on the rider list")),

      Heading("The Fix laps window"),
      Picture("fix-laps", "Fix laps with the suggested fix for a missed read at the top, and a read that came too soon in grey."),
      Para("A warning comes with its fix, at the top of the window: Split lap 5 into 2 laps for a missed read, " +
           "Delete lap 5 for two riders on track. Press it and the laps are corrected - or press Keep lap 5 if " +
           "the lap really was like that. The lap it is about is already selected in the list."),
      Para("Every lap in time order. Reads that were not counted are shown as grey rows among them. Select a " +
           "row, then:"),
      Bullets(
        "Add a missing lap... - when no read was recorded at all. Give the finish as time into the race or as clock time.",
        "Change lap time... - correct a crossing time.",
        "Delete this lap - for a read that was not a real lap.",
        "Split this lap... - a lap two or three times as long as usual means a read was missed. The lap becomes " +
        "2 to 6 equal laps; the last one ends on the real read.",
        "Keep lap as is - the lap really was that long (or, in a team, that short). The warning goes and stays away.",
        "Count this read - a grey read rejected as too soon that was a real lap after all.",
        "Mark as DNF, Mark as DNS, Back in the race - set the rider's status by hand. The automatic timeout " +
        "never overrides it; Back in the race hands the rider back to it.",
        "Change class... - when the rider list has a rider in the wrong class. Pick from the classes already " +
        "in the session or type a new one. It changes which sheet they appear on and nothing else: their laps, " +
        "their total time and their start time all stay exactly as they were. A team's class comes from its " +
        "riders on the list, so a team cannot be changed here.",
        "Undo last change and Redo - also Ctrl+Z and Ctrl+Y, in this window or anywhere in the application, for the last 50 " +
        "changes. Redo is there until the next change is made."),
      Para("Changes apply at once and are saved straight away; the standings behind the window move with " +
           "them. The list keeps up by itself when the rider crosses the line while the window is open; a change " +
           "made just as they cross is refused - check the list and do it again. A lap ridden after a change " +
           "stays when that change is undone. After the session is over a correction changes the sheet; " +
           "it does not restart the session."),

      Heading("Missed reads"),
      Para("From a rider's third lap on, a lap between 1.8 and 5.5 times their recent pace that looks like 2 " +
           "to 5 laps is flagged CHECK, and the banner says Possible missed read. How sensitive this is can " +
           "be changed under Race Settings > Missed read detection...."),

      Heading("Reads that come too soon"),
      Para("A read sooner than the minimum lap time after the rider's last counted crossing (10 seconds unless " +
           "changed) is the same pass seen twice. It is not counted, but it stays in Fix laps as a grey row " +
           "in case it was real."),

      Heading("A rider marked DNF crosses the line"),
      Para("Their read is not counted, but it stays in Fix laps as a grey row, and a banner says a rider marked " +
           "DNF crossed the line - someone who was reported retired may only have stopped. If they are racing " +
           "again, press Back in the race, then Count this read on each grey row.")
    },
    HelpTopicIds.Unknown, HelpTopicIds.Teams, HelpTopicIds.Settings);

  private static HelpTopic Unknown() => Topic(HelpTopicIds.Unknown, DuringASession,
    "Unknown transponders",
    "A transponder that is not on the rider list, a rider on a spare tag, a marshal's bike - and how to " +
    "stop counting one.",
    new[]
    {
      Heading("Identify a transponder"),
      Picture("unknown-transponder", "Identify transponder, for a rider out on a spare transponder."),
      Para("A transponder that is not on the rider list is timed anyway and shows as UNKNOWN. Right-click it " +
           "on the Riders tab, choose Identify this transponder..., then one of:"),
      Bullets(
        "Give this transponder a rider - pick the rider from the imported list (riders without laps yet are " +
        "at the top), or type the number, name, team and class. Its laps stay as they are.",
        "These laps belong to a rider already in the race - for a rider who changed to a spare transponder. " +
        "Its laps are added to that rider's, reads that clash with a lap already recorded are dropped, and " +
        "later reads of the spare count for that rider.",
        "This transponder belongs to a team - in a team event: pick the team and, if known, the rider."),
      Tip("Undo puts the laps back where they were, and later reads of that transponder no longer go to the " +
          "rider or team it was added to. Redo (Ctrl+Y) does it again."),

      Heading("Stop counting a transponder"),
      Para("Right-click a rider and choose Stop counting .... If they already have laps you are asked: Yes " +
           "stops counting and deletes the laps already recorded (this cannot be undone); No stops counting " +
           "and keeps the laps; Cancel does nothing. Either way the rider leaves the standings and the sheets."),
      Bullets(
        "Count ... again brings the rider back, with any laps that were kept.",
        "Riders > Ignored transponders... lists them in Race Events.",
        "The ignore list lasts all day: setting up a new session does not clear it.")
    },
    HelpTopicIds.Fixing, HelpTopicIds.Teams, HelpTopicIds.RiderLists);

  private static HelpTopic TransponderCheck() => Topic(HelpTopicIds.TransponderCheck, DuringASession,
    "Transponder check",
    "Finds the transponders that are not being read properly - in practice or qualifying, while there is " +
    "still time to fix them.",
    new[]
    {
      Para("Shown for timed qualifying and free practice: the Transponders tab while the session runs, and " +
           "Race > Transponder check... for the sheet. It covers everyone who crossed and everyone on the " +
           "rider list who did not."),
      Keys(
        ("Never read", "no reads at all - check the tag is fitted, working, and somewhere the loop can see it"),
        ("Went quiet", "read, then silent for more than three laps while the others rode on - ask whether they pulled in or the tag stopped"),
        ("Intermittent", "laps missing in the middle - usually the tag is mounted too high, too low, or behind metal"),
        ("Double reads", "read twice in one pass - usually the tag sits where it crosses the loop twice"),
        ("Clean", "every expected lap accounted for")),
      Para("The worst come first, and the line at the top says how many riders need attention. Double-click " +
           "a rider to look at their laps."),
      Para("Print transponder check... prints the list, with advice for each kind of problem found. It is not " +
           "split by class.")
    },
    HelpTopicIds.Practice, HelpTopicIds.Qualifying, HelpTopicIds.Fixing);

  private static HelpTopic Track() => Topic(HelpTopicIds.Track, DuringASession,
    "Track map",
    "The circuit on a map with every rider's estimated position, so \"where is everyone?\" has an answer.",
    new[]
    {
      Heading("Setting up a circuit"),
      Steps(
        "On the Track tab, pan and zoom the map to the venue, then press New circuit....",
        "Draw loop: click round the circuit. Backspace removes the last point.",
        "Start / finish: click where the start/finish line is painted on the ground.",
        "Add sector, if wanted: click where a sector begins and name it. A sector runs to the start of the next one.",
        "Give the circuit a name and press Save circuit. It needs at least three points and a loop longer than 50 metres."),
      Bullets(
        "Move points: drag a point, Ctrl+click the line to add one, Delete to remove the selected one. Ctrl+Z undoes.",
        "Import... reads a GPX trace (the first lap is used; place the start/finish afterwards) or a CrossMgr " +
        "circuit file (.cmtrack) from another club.",
        "Export... writes GPX (the shape only) or a .cmtrack file that keeps the start/finish, the sectors and " +
        "the reference image."),

      Heading("Tracing over a picture"),
      Para("A screenshot of an online map with the track drawn on it, or the club's plan of the circuit, can be " +
           "laid over the map and traced. Zoom the map to the venue first."),
      Picture("circuit-editor", "Set up circuit, with a reference image being lined up."),
      Steps(
        "Under Reference image, press Import image..., or copy the picture and press Ctrl+V in the editor.",
        "Match 2 points: click a landmark on the picture - a corner, a jump, a building - then the same spot on " +
        "the map. Do the same with a second landmark far from the first, and the picture lines itself up.",
        "Align image, to adjust it by hand: drag the picture to move it, a corner to resize it, the round handle " +
        "to turn it. Drag with the right mouse button to move the map instead.",
        "Draw loop over it. Opacity sets how much of the map shows through; untick Show to hide the picture."),
      Bullets(
        "The picture is saved with the circuit and travels in a .cmtrack export. Only the editor shows it - " +
        "never the Track tab.",
        "Tick Lock once it is lined up, so a stray drag while tracing cannot move it. A locked picture cannot be " +
        "aligned, matched, replaced or removed until Lock is unticked, and it stays locked when the circuit is saved.",
        "Remove takes it off the circuit. Ctrl+Z brings it back, and undoes a move, a resize, a match or a lock."),
      Tip("Matching is exact for a flat, top-down screenshot of an online map. A tilted or 3D view, or a plan " +
          "that was not drawn to scale, only fits roughly - line it up where the track matters most."),

      Heading("Without internet"),
      Para("Map tiles are kept on this computer once they have been shown. Before going to a venue with no " +
           "signal, open Edit circuit... > Offline map... and download the detail levels for the chosen map " +
           "(14 to 17 unless changed)."),

      Heading("Reading the map"),
      Picture("track-map", "The Track tab during a race, with the riders in each sector counted on the left."),
      Keys(
        ("Blue dot", "on track - estimated from the last crossing and the rider's recent pace"),
        ("Orange", "overdue - should have crossed by now; waits on the line showing how late"),
        ("Red", "well overdue - more than a tenth of a lap late"),
        ("Dark dot", "finished"),
        ("Hollow grey", "retired, not started, or long overdue (hidden unless ticked)"),
        ("Hollow blue", "no pace yet - not started or no laps"),
        ("27+4", "a group of riders - click to zoom in")),
      Para("Click a dot for the rider's details; double-click it to fix their laps. Showing: picks a class, " +
           "Field: shows only the leading riders, Find: highlights a start number, and Label with: chooses what " +
           "is written beside each dot. Sector counts shows how many riders are in each sector."),
      Tip("Positions between crossings are estimates. A rider shown as overdue may just be having a slow lap.")
    },
    HelpTopicIds.RaceDay);

  // ---- After a session -----------------------------------------------------

  private static HelpTopic Results() => Topic(HelpTopicIds.Results, AfterASession,
    "Results and sheets",
    "Previewing, printing and exporting the sheets - for the session on screen or for any stored one.",
    new[]
    {
      Heading("Which sheet"),
      Keys(
        ("Race", "Results... (Ctrl+P) - the classification"),
        ("Timed qualifying", "Gate pick order... - the order riders choose their gates"),
        ("Practice, qualifying", "Transponder check... - tags that need attention")),

      Heading("Preview, print or export"),
      Para("Each asks for a title - the session's name unless changed - and whether to preview, print or export."),
      Bullets(
        "With more than one class, Preview opens the overall sheet and then one sheet per class, Print asks " +
        "whether to print them all or only the overall sheet, and Export asks for a folder and saves one " +
        "Excel file for each.",
        "With one class, Export asks where to save, as Excel (.xlsx) or as text (.txt).",
        "Every sheet states what the session was scored under - its length, extra laps, finish timeout, " +
        "minimum lap and how the clock started - so the sheet itself can settle a protest."),

      Heading("The race sheet"),
      Bullets(
        "Race Information and Race Statistics, including the winning time and the fastest lap.",
        "Race Results: position, number, name, team, laps, total time, best lap and gap. DNF riders are listed " +
        "last, and riders marked DNS after them. A practice sheet marks nobody DNF.",
        "In a team event, each team's riders on the line under its name, and a Team Members section after the results.",
        "The Excel export has a sheet for the results, one with every lap time, one with the statistics - and " +
        "one for the team members in a team event."),

      Heading("On the website"),
      Para("If your club publishes results to a website, Race > Publish results... puts the session on it, " +
           "where riders can read it on their phones. The sheet and the website are worked out from the same " +
           "figures, so they cannot disagree."),

      Heading("Later"),
      Para("Race > Past sessions... (Ctrl+O) prints the sheet of any stored session, with the rules it was " +
           "run under - and publishes it, if that was not done on the day.")
    },
    HelpTopicIds.PastSessions, HelpTopicIds.Publish, HelpTopicIds.Race, HelpTopicIds.Qualifying,
    HelpTopicIds.TransponderCheck);

  private static HelpTopic Publish() => Topic(HelpTopicIds.Publish, AfterASession,
    "Putting results on the website",
    "One button sends a finished session to your club's results website, where riders can look themselves " +
    "up on their phones.",
    new[]
    {
      Heading("Setting it up, once"),
      Para("Race > Results website... holds the two addresses - the results website and the live timing " +
           "website - and the key your club was given. One key works for both. The addresses are already " +
           "filled in; leave them unless you were told otherwise."),
      Bullets(
        "Paste puts the key in without retyping it - it is long, and arrives by email.",
        "Test connection asks the website whether the key works. Do this in the club house, not at the track.",
        "The key identifies your club. Treat it like a password - do not email it on, and do not put it in " +
        "a screenshot."),
      Tip("The key is kept encrypted on this computer, for this Windows user. Copying the folder to another " +
          "laptop does not carry it across: that laptop asks for the key of its own."),
      Para("The window never shows a saved key back - it is a password - but it does say that one is there, " +
           "and which one, by its first few letters. Leave the box empty to keep it. Pasting a key into the box " +
           "and pressing OK replaces the saved one, so paste only a key you were given for this computer."),

      Heading("Live timing while the race runs"),
      Para("Race > Live timing - or Switch on in the LIVE tile on the Race Day screen - sends the running " +
           "order and the last crossings to the live timing website every few seconds, for spectators to " +
           "follow on their phones. It is off for every new session; switch it on once the clock is " +
           "running, or before."),
      Bullets(
        "What goes: number, name, class, team, laps, last and best lap, and the gap - plus the last " +
        "twenty crossings. Never transponder IDs, never your notes about corrections.",
        "The LIVE tile's dot says how it is going: green means sending, amber means the last update did " +
        "not get through and it is trying again, red means the website has not been reached for a while. " +
        "The race is timed exactly the same whatever the colour - nothing on this laptop waits for the website.",
        "Switch off stops sending. The page keeps the last picture it received.",
        "When the race finishes, one last update says so and live timing switches itself off. Publish " +
        "results... afterwards puts the full sheet up as before, and the live page then links to it."),
      Keys(
        ("The tile says the key was refused", "Race > Results website... and press Test beside the live address"),
        ("Amber or red", "Nothing to do during the race. Check the hotspot when there is a moment")),
      Tip("A demo can go out live too - handy for showing someone what the page looks like on a phone. " +
          "It is marked DEMO on the website, is never listed among the real races, and is cleared away " +
          "within a day. A demo never publishes results."),

      Heading("Publishing a session"),
      Picture("publish-results", "Publish results, before anything is sent."),
      Para("Once the flag is out, Race > Publish results... - or Publish results... on the Race Day screen. " +
           "The window says how many riders and laps are about to be sent, and which circuit, before it sends " +
           "anything."),
      Bullets(
        "Publish sends it. It takes a few seconds; the window says what it is doing.",
        "When it is done the address of the page is shown. Copy link puts it on the clipboard, ready to paste " +
        "into the club's group chat.",
        "Publishing the same session again replaces what is on the website. Do that after correcting a lap - " +
        "the website will not end up with the race twice."),

      Heading("What is sent"),
      Para("Exactly what is on the printed sheet: numbers, names, classes, teams, machines, every lap time, " +
           "and what the session was scored under. The circuit is sent too, so the page can show a map."),
      Para("Transponder IDs are not sent, and neither are your notes about which laps were corrected."),

      Heading("Riders who would rather not be named"),
      Para("Not every rider wants their full name on a public website. Race > Results website... has a " +
           "setting for it: show full names unless a rider says no, or shorten every name unless a rider says " +
           "yes. A rider says so in the rider list, with a column called public (or showname) holding yes or no. " +
           "Their own answer always wins over the setting."),
      Bullets(
        "A shortened name is never blank - the rider still has to find themselves. It is either " +
        "the first name and an initial (Lena B.) or the first three letters of each part with the rest " +
        "starred (Len* Bra***). The setting chooses which, and shows an example.",
        "Only the websites are affected. The screen, the printed sheet and the Excel file always carry " +
        "full names - the operator has to tell riders apart, and a sheet handed to a rider is not public.",
        "A team's name is a club, not a person, so it is never shortened. The riders listed under it " +
        "each follow their own answer."),

      Heading("No internet at the track"),
      Para("Plenty of fields have none, and nothing is lost. The results are safe on this laptop. Publish " +
           "them later from anywhere with Race > Past sessions... - the Published column shows which sessions " +
           "have already gone up."),

      Heading("If it will not publish"),
      Keys(
        ("The website did not accept the key", "Check it under Race > Results website... and press Test connection"),
        ("This computer cannot reach the internet", "Publish later from Past sessions"),
        ("The website is not answering", "Nothing was changed. Try again in a few minutes")),
      Tip("A demo race is never published. The menu item is not there inside a demo.")
    },
    HelpTopicIds.Results, HelpTopicIds.PastSessions);

  private static HelpTopic PastSessions() => Topic(HelpTopicIds.PastSessions, AfterASession,
    "Past sessions and crash recovery",
    "Every session is kept from the moment its clock starts, and nothing is lost when the laptop restarts.",
    new[]
    {
      Heading("Past sessions"),
      Picture("past-sessions", "Race > Past sessions..., listing a day's sessions."),
      Para("Race > Past sessions... (Ctrl+O) lists every stored session, newest first, with its date, name, " +
           "type, length, riders, laps, status and when it was published, if it was."),
      Bullets(
        "Results... (or double-click) - prints its sheet, with the rules it was run under.",
        "Publish... - puts it on the club's results website. Only shown once a website has been set up.",
        "Open - makes it the session on screen, to look at its laps or correct them. Not while another session is running.",
        "Rename... - changes the name printed on its sheet.",
        "Delete... - removes it and all its laps for good."),
      Para("Race > Delete this session... removes the session on screen, once it is over - a running session " +
           "has to be ended first. To keep it and carry on, use NEW SESSION... instead."),

      Heading("If the laptop restarts"),
      Bullets(
        "Every lap is saved the moment it is recorded, and the whole session every 30 seconds.",
        "When the application starts it offers to restore a session that was not finished, from the last 24 " +
        "hours. Yes brings back the riders, laps, flag, waves and teams, and reconnects the reader.",
        "No leaves it under Past sessions as Not finished.",
        "The rider list and the reader connection come back by themselves."),
      Tip("A restored session comes back with the minimum lap time it was run under. The transponder filter " +
          "is not part of a session: it stays as it was last set, and if it is on, a banner says so when the " +
          "application starts.")
    },
    HelpTopicIds.Results, HelpTopicIds.Publish, HelpTopicIds.Settings);

  // ---- Setup and reference -------------------------------------------------

  private static HelpTopic RiderLists() => Topic(HelpTopicIds.RiderLists, Reference,
    "Rider lists",
    "An Excel (.xlsx) or CSV file with one row per rider. Without one, riders show as transponder codes - " +
    "still timed correctly, but hard to read.",
    new[]
    {
      Heading("Columns"),
      Keys(
        ("Transponder", "tagid, tag or id - required"),
        ("Name", "name, fullname or rider - or firstname/first with lastname/last/surname"),
        ("Number", "number, ridernumber or bib"),
        ("Class", "category, class or division"),
        ("Team", "team, club or sponsor - only groups riders in a team event"),
        ("Machine", "machine, bike or motorcycle"),
        ("Public name", "public, showname or nameok - yes or no; whether the rider agrees to their full name on the websites")),
      Bullets(
        "The first row holds the column names; capitals do not matter, and other columns are ignored.",
        "In an Excel file only the first sheet is read.",
        "In a CSV file values are separated by commas, so a value cannot contain a comma itself."),

      Heading("Importing"),
      Bullets(
        "In the wizard (Riders step, Choose file...) or with Race > Import riders... (Ctrl+I).",
        "An import replaces the whole list. Riders already on track keep their laps and are given their names.",
        "A row without a transponder is skipped, and the application says which rows. A file without a " +
        "transponder column is refused, naming the columns it did find.",
        "The list is loaded again by itself when the application starts."),
      Tip("Check the preview in the wizard before the session: a rider missing from the list shows as UNKNOWN " +
          "when they cross the line.")
    },
    HelpTopicIds.Teams, HelpTopicIds.Unknown);

  private static HelpTopic Reader() => Topic(HelpTopicIds.Reader, Reference,
    "The transponder reader",
    "The reader connects to this computer over the network and sends every transponder it reads.",
    new[]
    {
      Bullets(
        "Reader > Start reader connection opens the connection, and the reader then connects to this computer. " +
        "The READER tile and the status bar show Waiting for reader, then Reader connected.",
        "If the connection was open when the application closed, it opens again by itself.",
        "Reader > Connection settings... changes the port (53135, unless something else on this computer uses " +
        "it) and can log the raw reader traffic for diagnosing problems. A new port needs the connection " +
        "stopped and started again.",
        "Reader > Connection settings... also says when to warn that nothing is being read. The usual choice " +
        "goes by the riders' lap times: a rider is late once they are well past their usual lap, and the " +
        "warning comes when three riders still out are late (or all of them, if fewer are out) and nothing " +
        "has been read for at least 30 seconds. The other choice is a fixed number of seconds, orange at half " +
        "of it. Until the riders have lap times the seconds are used either way, and neither warns while " +
        "nobody is still expected at the line.",
        "The reader's clock is checked when it connects, and each crossing is timed by the reader's own timestamp.",
        "More than one reader can be connected at once."),

      Picture("reader-settings", "Reader > Connection settings..."),

      Heading("No reads?"),
      Steps(
        "Look at the READER tile. Grey: press Reader > Start reader connection. Orange, Waiting for reader: " +
        "the reader has not connected - check its network cable and its settings.",
        "Connected but no reads: check the loop and its cable, and that riders are actually crossing it.",
        "View > Show advanced tabs, then Tag Events: every read arrives there first. Reads marked FILTERED do " +
        "not match the transponder filter on the Race Settings tab.",
        "Help > Open log folder: the log records everything the reader sent.")
    },
    HelpTopicIds.Settings, HelpTopicIds.RaceDay, HelpTopicIds.Troubleshooting);

  private static HelpTopic Settings() => Topic(HelpTopicIds.Settings, Reference,
    "The Race Settings tab",
    "The rules of the session on screen. It is an advanced tab: View > Show advanced tabs (Ctrl+Shift+A). " +
    "The wizard sets most of these; this tab changes one of them on its own.",
    new[]
    {
      Keys(
        ("Race length", "How long is the race? - press Set, up to 600 minutes. Can be changed while the race runs; the end time moves with it."),
        ("Extra laps", "laps the leader rides after the clock runs out, on top of the lap in progress. Races only."),
        ("Minimum lap", "Ignore laps faster than - reads closer together are not counted. Tick Enable Detection and press Set. Kept when the application restarts."),
        ("Finish timeout", "Time to finish after the leader - how long a rider has to finish their last lap before DNF, but never less than one and a half laps of the field's pace. Also the grace after a timed session's flag."),
        ("Transponder filter", "Only transponders starting with - count only tags that begin with these characters (several, separated by commas). Press Set Filter, then tick Enabled. Kept when the application restarts; while it is on, a banner says so at startup."),
        ("Clock start", "When the first rider crosses, or I will press Start Race myself, with the Start Race button."),
        ("Session type", "Race, timed qualifying or free practice. Locked from the moment the clock starts until the session is put away."),
        ("Missed reads", "Missed read detection... - how much longer than usual a lap must be before it is flagged CHECK.")),
      Tip("Opening a stored session under Past sessions fills this tab with the length, extra laps, finish " +
          "timeout and minimum lap time that session was run under.")
    },
    HelpTopicIds.Race, HelpTopicIds.Fixing, HelpTopicIds.Reader);

  private static HelpTopic Shortcuts() => Topic(HelpTopicIds.Shortcuts, Reference,
    "Keyboard shortcuts",
    "Keys that work anywhere in the main window.",
    new[]
    {
      Keys(
        ("F1", "Help"),
        ("Ctrl+N", "New race... - set up a session"),
        ("Ctrl+I", "Import riders..."),
        ("F5", "Start race / Start session"),
        ("Ctrl+E", "End race now... / End session now..."),
        ("Ctrl+P", "Results..."),
        ("Ctrl+O", "Past sessions..."),
        ("F2", "Fix laps..."),
        ("Ctrl+Z", "Undo the last correction"),
        ("Ctrl+Y", "Redo the correction just undone"),
        ("Ctrl+1", "Race Day"),
        ("Ctrl+2", "Riders"),
        ("Ctrl+3", "Track map"),
        ("Ctrl+Shift+A", "Show or hide the advanced tabs")),

      Heading("In the circuit editor"),
      Keys(
        ("Backspace", "remove the last point while drawing the loop"),
        ("Ctrl+click", "add a point on the line"),
        ("Delete", "remove the selected point"),
        ("Ctrl+V", "paste a reference image"),
        ("Right-drag", "move the map, whichever tool is chosen"),
        ("Esc", "stop matching points"),
        ("Ctrl+Z", "undo"))
    },
    HelpTopicIds.QuickStart);

  private static HelpTopic Troubleshooting() => Topic(HelpTopicIds.Troubleshooting, Reference,
    "When something goes wrong",
    "The usual problems on race day, and what to do about each.",
    new[]
    {
      Heading("The READER tile is red, or nothing is being read"),
      Para("See The transponder reader: it goes through the checks in order."),

      Heading("A rider shows UNKNOWN"),
      Para("Their transponder is not on the rider list, or the list was not loaded. Import the list, or " +
           "identify the transponder - see Unknown transponders."),

      Heading("A lap is flagged CHECK"),
      Para("Probably a missed read. Press Fix laps... (F2): the fix at the top splits the lap into the laps it " +
           "looks like - or keeps it, if the lap really was that slow."),

      Heading("A read was not counted"),
      Para("A read too soon after the rider's last crossing is taken as the same pass seen twice. Fix laps " +
           "shows it as a grey row: Count this read if it was a real lap. A read of a rider marked DNF is a grey " +
           "row too, and a banner says so. A read after the rider's final lap is not kept."),

      Heading("The clock shows 00:00 but the race goes on"),
      Para("A race notices the clock has run out on the next crossing; then the leader rides the lap in " +
           "progress plus the extra laps, and everyone else finishes their lap. Wait for Race finished, or " +
           "press End race now...."),

      Heading("Anything else"),
      Para("Help > Open log folder. The log records every read, every race event and every correction with " +
           "its time, and is usually quicker to read than watching the screen.")
    },
    HelpTopicIds.Reader, HelpTopicIds.Unknown, HelpTopicIds.Fixing);
}
