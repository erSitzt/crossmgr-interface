namespace CrossMgrInterface;

public enum ReaderQuietLevel
{
  /// <summary>Reads are arriving, or nobody is due at the line.</summary>
  Ok,

  /// <summary>Somebody was due and has not come: worth a look, not yet an alarm.</summary>
  Quiet,

  /// <summary>The reader has most likely stopped.</summary>
  Silent
}

/// <summary>A rider still in the session, as the reader check sees them.</summary>
/// <param name="Pace">Their usual lap, or null before they have one.</param>
public readonly record struct ReaderQuietRider(string Label, DateTime LastCrossing, TimeSpan? Pace);

/// <summary>What the reader check made of the time since the last read.</summary>
/// <param name="FirstOverdue">Up to three overdue riders, the longest overdue first.</param>
/// <param name="FromLapTimes">Judged from the riders' lap times rather than a fixed number of seconds.</param>
public sealed record ReaderQuietVerdict(
  ReaderQuietLevel Level,
  TimeSpan Silence,
  int OverdueCount,
  IReadOnlyList<string> FirstOverdue,
  bool FromLapTimes)
{
  /// <summary>For the banner: what is wrong and what to do about it.</summary>
  public string Notice
  {
    get
    {
      var quiet = $"No transponder reads for {Silence.TotalSeconds:F0} seconds";
      if (!FromLapTimes) return $"{quiet} - check the reader";
      if (OverdueCount >= ReaderQuietCheck.Evidence) return $"{quiet} and {OverdueCount} riders are overdue - check the reader";

      // Few riders left out: a reader that stopped and riders who stopped look
      // the same from here, so say both.
      return $"{quiet} and {Names()} {(OverdueCount == 1 ? "is" : "are")} overdue - check the reader, " +
             "or whether they have stopped";
    }
  }

  /// <summary>For the log.</summary>
  public string LogLine => FromLapTimes
    ? $"NO READS FOR {Silence.TotalSeconds:F0}s - {OverdueCount} overdue ({string.Join(", ", FirstOverdue)}" +
      $"{(OverdueCount > FirstOverdue.Count ? ", ..." : "")}) - check the reader and the loop"
    : $"NO READS FOR {Silence.TotalSeconds:F0}s - check the reader and the loop";

  /// <summary>The line under the READER tile's value.</summary>
  public string TileDetail => Level switch
  {
    ReaderQuietLevel.Silent when FromLapTimes => $"{OverdueCount} overdue - check the reader and the loop",
    ReaderQuietLevel.Silent => "Check the reader and the loop",
    ReaderQuietLevel.Quiet when FromLapTimes && OverdueCount == 1 => $"{FirstOverdue[0]} is overdue",
    ReaderQuietLevel.Quiet when FromLapTimes => $"{OverdueCount} riders overdue",
    ReaderQuietLevel.Quiet => "Quiet - is that expected?",
    _ => ""
  };

  private string Names() => FirstOverdue.Count switch
  {
    0 => "riders",
    1 => FirstOverdue[0],
    _ => string.Join(", ", FirstOverdue.Take(FirstOverdue.Count - 1)) + " and " + FirstOverdue[^1]
  };
}

/// <summary>
/// Whether the reader has gone quiet - the failure that costs a race.
///
/// A fixed minute without a read was the whole rule, and it was wrong both
/// ways. After the flag, while the finish waited out the grace for a rider who
/// had retired, nobody was coming and the banner still said to check the
/// reader. And a minute is nothing on an enduro loop: a small field on
/// fifteen-minute laps goes a minute without a read all the time.
///
/// So the question is not how long it has been quiet, but who should have come
/// in that time. Every rider still in the session has a usual lap; a rider is
/// late once their next crossing is overdue by more than lap times normally
/// spread. A reader that has stopped makes rider after rider late, and nothing
/// arrives to reset the count - one rider who crashed is overtaken by the next
/// read of somebody else.
/// </summary>
public static class ReaderQuietCheck
{
  /// <summary>Late means this much past the due crossing, at the least...</summary>
  public static readonly TimeSpan MinimumLateness = TimeSpan.FromSeconds(20);

  /// <summary>...or this share of the rider's lap, on a long loop where laps spread by more.</summary>
  public const double LatenessShare = 0.15;

  /// <summary>Late riders that make it the reader rather than a rider. Fewer when fewer are out.</summary>
  public const int Evidence = 3;

  /// <summary>Never an alarm sooner than this: on a short track a whole pack is due within seconds.</summary>
  public static readonly TimeSpan MinimumSilence = TimeSpan.FromSeconds(30);

  /// <param name="stillRacing">Riders still to finish: not DNF, not past their final lap, not ignored.</param>
  /// <param name="fieldPace">The field's pace, for a rider without a lap time of their own.</param>
  /// <param name="fromLapTimes">False to go by <paramref name="fixedAfter"/> alone.</param>
  /// <param name="fixedAfter">
  /// Silence before the alarm when not judging from lap times - and before
  /// anybody has a lap time to judge by. Half of it is quiet.
  /// </param>
  public static ReaderQuietVerdict Evaluate(DateTime now, DateTime lastRead,
    IReadOnlyList<ReaderQuietRider> stillRacing, TimeSpan? fieldPace, bool fromLapTimes, TimeSpan fixedAfter)
  {
    var silence = now - lastRead;
    var expected = 0;
    var timed = 0;
    var overdue = new List<(DateTime Late, string Label)>();

    foreach (var rider in stillRacing)
    {
      if ((rider.Pace ?? fieldPace) is not { } lap || lap <= TimeSpan.Zero)
      {
        // Nothing to judge by: they could be anywhere on the circuit.
        expected++;
        continue;
      }

      var late = rider.LastCrossing + lap + Lateness(lap);

      // Late already before the last read, when the reader was evidently
      // working: allow a missed read, which puts them a lap on. Later than that
      // they have stopped, which is nothing to do with the reader - this is the
      // retired rider the finish waits for.
      if (late <= lastRead) late += lap;
      if (late <= lastRead) continue;

      expected++;
      timed++;
      if (late <= now) overdue.Add((late, rider.Label));
    }

    var names = overdue.OrderBy(o => o.Late).Take(Evidence).Select(o => o.Label).ToList();

    if (expected == 0)
      return new ReaderQuietVerdict(ReaderQuietLevel.Ok, silence, 0, names, fromLapTimes);

    if (!fromLapTimes || timed == 0)
    {
      var level = silence > fixedAfter ? ReaderQuietLevel.Silent
        : silence > fixedAfter / 2 ? ReaderQuietLevel.Quiet
        : ReaderQuietLevel.Ok;
      return new ReaderQuietVerdict(level, silence, overdue.Count, names, FromLapTimes: false);
    }

    var needed = Math.Min(Evidence, timed);
    var judged = overdue.Count >= needed && silence >= MinimumSilence ? ReaderQuietLevel.Silent
      : overdue.Count > 0 ? ReaderQuietLevel.Quiet
      : ReaderQuietLevel.Ok;
    return new ReaderQuietVerdict(judged, silence, overdue.Count, names, FromLapTimes: true);
  }

  /// <summary>How far past their due crossing a rider on laps of <paramref name="lap"/> may be before they are late.</summary>
  public static TimeSpan Lateness(TimeSpan lap)
  {
    var share = lap * LatenessShare;
    return share > MinimumLateness ? share : MinimumLateness;
  }
}
