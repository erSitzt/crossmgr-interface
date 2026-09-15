namespace CrossMgrInterface;

/// <summary>Where a planted problem stands.</summary>
public enum DemoProblemState
{
  /// <summary>It has not happened yet.</summary>
  Later,

  /// <summary>It has happened and is still to be put right - or, for something to watch, is going on now.</summary>
  ToFix,

  /// <summary>Put right, or seen.</summary>
  Done,

  /// <summary>Not done, and too late now to be worth doing.</summary>
  Missed
}

/// <summary>What the checklist can see of the race, taken once a second.</summary>
/// <param name="ReaderStart">The moment the demo's reads count from, or null before its reader has connected.</param>
public sealed record DemoRaceView(
  DateTime? ReaderStart,
  DateTime Now,
  IReadOnlyDictionary<string, RiderInfo> Riders,
  IReadOnlySet<string> Ignored,
  IReadOnlyDictionary<string, string> Aliases,
  IReadOnlyList<RejectedRead> Rejected);

/// <summary>
/// Works out, from the race itself, whether each problem a demo planted has been
/// put right - the laps, the ignored transponders, the merges and the grey reads
/// - rather than from which buttons were pressed. Pressing the wrong one, such as
/// Keep lap as is on a missed read, leaves the problem showing as still to fix.
/// </summary>
public static class DemoChecklist
{
  /// <summary>Reads are stamped to the millisecond; a little slack for the sums.</summary>
  private static readonly TimeSpan Slack = TimeSpan.FromMilliseconds(50);

  public static DemoProblemState StateOf(DemoProblem problem, DemoRaceView race)
  {
    if (race.ReaderStart is not { } start || race.Now < start + problem.At) return DemoProblemState.Later;
    if (IsDone(problem, race, start)) return DemoProblemState.Done;

    return problem.Deadline is { } deadline && race.Now >= start + deadline
      ? DemoProblemState.Missed
      : DemoProblemState.ToFix;
  }

  /// <summary>For an outage: how many riders still have a long lap to split.</summary>
  public static int RidersLeft(DemoProblem problem, DemoRaceView race)
  {
    if (problem.Kind != DemoProblemKind.SplitAfterOutage || race.ReaderStart is not { } start) return 0;
    return problem.Tags.Count(tag => !OutageFixedFor(tag, problem, race, start));
  }

  private static bool IsDone(DemoProblem problem, DemoRaceView race, DateTime start) => problem.Kind switch
  {
    DemoProblemKind.IdentifySpare =>
      Entry(race, problem.Tag) is { } spare && spare.RiderNumber == problem.Number,

    DemoProblemKind.StopCounting => race.Ignored.Contains(problem.Tag),

    DemoProblemKind.ReadTwice => true,

    // Split - not kept, which would take the warning away as well.
    DemoProblemKind.SplitMissedRead =>
      Entry(race, problem.Tag) is { } rider &&
      !rider.Laps.Any(IsCheck) &&
      rider.Laps.Count(l => l.IsSplitLap && l.CrossingTime > start + problem.Since &&
                            l.CrossingTime <= start + problem.At + Slack) >= problem.Laps,

    // Deleted - not kept.
    DemoProblemKind.DeleteSecondRider =>
      Entry(race, problem.Tag) is { } team &&
      !team.Laps.Any(l => l.CrossedBy == problem.OtherTag && Near(l.CrossingTime, start + problem.At)),

    DemoProblemKind.MergeSpare =>
      problem.OtherTag != null &&
      race.Aliases.TryGetValue(problem.OtherTag, out var mergedInto) && mergedInto == problem.Tag &&
      !race.Riders.ContainsKey(problem.OtherTag),

    DemoProblemKind.MarkDnf => Entry(race, problem.Tag) is { IsDNF: true },

    DemoProblemKind.BackInTheRace =>
      Entry(race, problem.Tag) is { IsDNF: false } back &&
      !race.Rejected.Any(r => r.WhileDnf && r.TagID == back.TagID && !r.IsCountedIn(back)),

    DemoProblemKind.ReaderOutage => race.Now >= start + problem.Until,

    DemoProblemKind.SplitAfterOutage => problem.Tags.All(tag => OutageFixedFor(tag, problem, race, start)),

    DemoProblemKind.Retires => Entry(race, problem.Tag) is { IsDNF: true },

    _ => false
  };

  /// <summary>
  /// One rider's lap across the outage is split and nothing after it still shows
  /// CHECK. A rider no longer in the race, or marked DNF, has nothing to split.
  /// </summary>
  private static bool OutageFixedFor(string tag, DemoProblem problem, DemoRaceView race, DateTime start)
  {
    if (Entry(race, tag) is not { IsDNF: false } rider) return true;

    var from = start + problem.Since;
    var until = start + problem.Until;

    return !rider.Laps.Any(l => IsCheck(l) && l.CrossingTime > from) &&
           rider.Laps.Any(l => l.IsSplitLap && l.CrossingTime > from && l.CrossingTime < until);
  }

  /// <summary>The entry a transponder's laps are on now: its own, or the one it was merged into.</summary>
  private static RiderInfo? Entry(DemoRaceView race, string tag)
  {
    if (race.Riders.TryGetValue(tag, out var rider)) return rider;
    return race.Aliases.TryGetValue(tag, out var into) && race.Riders.TryGetValue(into, out var merged) ? merged : null;
  }

  private static bool IsCheck(RiderLap lap) => lap is { IsSuggestedForSplit: true, SuggestionDismissed: false };

  private static bool Near(DateTime a, DateTime b) => (a - b).Duration() <= Slack;
}

/// <summary>
/// A demo's checklist over the whole run: the state of every problem, kept ticked
/// once it has been put right, and which announcements are due.
///
/// Kept ticked because the race moves on. A missed read split early in the race
/// still has a lap that shows CHECK after a reader outage later on; the problem
/// the operator fixed does not become unfixed because a new one arrived.
/// </summary>
public sealed class DemoChecklistTracker
{
  private readonly Dictionary<DemoProblem, DemoProblemState> _states = new(ReferenceEqualityComparer.Instance);
  private readonly HashSet<DemoProblem> _done = new(ReferenceEqualityComparer.Instance);
  private readonly HashSet<DemoProblem> _announced = new(ReferenceEqualityComparer.Instance);

  public DemoChecklistTracker(IReadOnlyList<DemoProblem> problems)
  {
    Problems = problems;
    foreach (var problem in problems) _states[problem] = DemoProblemState.Later;
  }

  public IReadOnlyList<DemoProblem> Problems { get; }

  /// <summary>The race as it was at the last update.</summary>
  public DemoRaceView? Race { get; private set; }

  public DemoProblemState StateOf(DemoProblem problem) => _states[problem];

  /// <summary>How many of the problems are there to put right, rather than to watch.</summary>
  public int Fixable => Problems.Count(p => !p.JustWatch);

  public int Fixed => Problems.Count(p => !p.JustWatch && _states[p] == DemoProblemState.Done);

  public int LeftToFix => Problems.Count(p => !p.JustWatch && _states[p] == DemoProblemState.ToFix);

  /// <summary>Looks at the race again. Returns the problems that have just happened and have something to announce.</summary>
  public IReadOnlyList<DemoProblem> Update(DemoRaceView race)
  {
    Race = race;
    var announce = new List<DemoProblem>();

    foreach (var problem in Problems)
    {
      var state = DemoChecklist.StateOf(problem, race);
      if (state == DemoProblemState.Done) _done.Add(problem);

      _states[problem] = _done.Contains(problem) ? DemoProblemState.Done : state;

      if (state != DemoProblemState.Later && problem.Announce != null && _announced.Add(problem))
        announce.Add(problem);
    }

    return announce;
  }
}
