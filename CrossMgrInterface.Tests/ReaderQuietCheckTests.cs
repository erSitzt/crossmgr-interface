using Xunit;

namespace CrossMgrInterface.Tests;

public class ReaderQuietCheckTests
{
  private static readonly DateTime LastRead = new(2026, 9, 14, 13, 0, 0);
  private static readonly TimeSpan OneMinute = TimeSpan.FromSeconds(60);

  private static ReaderQuietRider Rider(string label, double lastCrossedSecondsBeforeLastRead, double? paceSeconds) =>
    new(label, LastRead.AddSeconds(-lastCrossedSecondsBeforeLastRead),
      paceSeconds.HasValue ? TimeSpan.FromSeconds(paceSeconds.Value) : null);

  private static ReaderQuietVerdict After(double silenceSeconds, IReadOnlyList<ReaderQuietRider> field,
    bool fromLapTimes = true, double? fieldPaceSeconds = null) =>
    ReaderQuietCheck.Evaluate(LastRead.AddSeconds(silenceSeconds), LastRead, field,
      fieldPaceSeconds.HasValue ? TimeSpan.FromSeconds(fieldPaceSeconds.Value) : null, fromLapTimes, OneMinute);

  /// <summary>Twenty riders on 50 s laps, one through the loop every 2.5 s, the last just as the reader stopped.</summary>
  private static List<ReaderQuietRider> BusyRace() =>
    Enumerable.Range(0, 20).Select(k => Rider($"#{k + 1}", k * 2.5, 50)).ToList();

  [Fact]
  public void ABusyRaceWhoseReaderStopsIsCaughtWellInsideAMinute()
  {
    Assert.Equal(ReaderQuietLevel.Ok, After(10, BusyRace()).Level);
    Assert.Equal(ReaderQuietLevel.Quiet, After(25, BusyRace()).Level);

    var verdict = After(35, BusyRace());
    Assert.Equal(ReaderQuietLevel.Silent, verdict.Level);
    Assert.True(verdict.OverdueCount >= ReaderQuietCheck.Evidence);
  }

  [Fact]
  public void NothingIsWrongWhileTheFinishWaitsForARiderWhoRetiredLongAgo()
  {
    // Everyone else has finished, so only the retired rider is still in; he
    // last crossed six minutes before the last read.
    var field = new[] { Rider("#68 Paul Neumann", 360, 52) };

    Assert.Equal(ReaderQuietLevel.Ok, After(90, field).Level);
    Assert.Equal(ReaderQuietLevel.Ok, After(90, field, fromLapTimes: false).Level);
  }

  [Fact]
  public void WithNobodyStillRacingThereIsNothingToWaitFor()
  {
    Assert.Equal(ReaderQuietLevel.Ok, After(600, Array.Empty<ReaderQuietRider>()).Level);
    Assert.Equal(ReaderQuietLevel.Ok, After(600, Array.Empty<ReaderQuietRider>(), fromLapTimes: false).Level);
  }

  [Fact]
  public void LongEnduroLapsDoNotRaiseTheAlarmBeforeAnybodyIsDue()
  {
    // Ten riders on 17-minute laps, the most recent crossings in the last three minutes.
    var field = Enumerable.Range(0, 10).Select(k => Rider($"#{k + 1}", k * 18, 17 * 60)).ToList();

    Assert.Equal(ReaderQuietLevel.Ok, After(120, field).Level);
    Assert.Equal(ReaderQuietLevel.Silent, After(120, field, fromLapTimes: false).Level);
  }

  [Fact]
  public void AFixedTimeWarnsAtHalfOfItAndSoundsTheAlarmAfterAllOfIt()
  {
    Assert.Equal(ReaderQuietLevel.Ok, After(29, BusyRace(), fromLapTimes: false).Level);
    Assert.Equal(ReaderQuietLevel.Quiet, After(31, BusyRace(), fromLapTimes: false).Level);

    var verdict = After(61, BusyRace(), fromLapTimes: false);
    Assert.Equal(ReaderQuietLevel.Silent, verdict.Level);
    Assert.Equal("No transponder reads for 61 seconds - check the reader", verdict.Notice);
  }

  [Fact]
  public void ARiderWhoseLastReadWasMissedIsStillExpectedALapLater()
  {
    // Due 30 s before the last read and not seen then: a missed read, so due
    // again 50 s later - and overdue once that is 20 s gone.
    var field = new[] { Rider("#101 Felix Bauer", 80, 50) };

    Assert.Equal(ReaderQuietLevel.Ok, After(35, field).Level);

    var verdict = After(45, field);
    Assert.Equal(ReaderQuietLevel.Silent, verdict.Level);
    Assert.Equal("No transponder reads for 45 seconds and #101 Felix Bauer is overdue - check the reader, " +
                 "or whether they have stopped", verdict.Notice);
  }

  [Fact]
  public void BeforeAnybodyHasALapTimeTheFixedTimeIsUsed()
  {
    var field = new[] { Rider("#7", 5, null), Rider("#12", 3, null) };

    Assert.Equal(ReaderQuietLevel.Ok, After(20, field).Level);

    var verdict = After(61, field);
    Assert.Equal(ReaderQuietLevel.Silent, verdict.Level);
    Assert.False(verdict.FromLapTimes);
  }

  [Fact]
  public void ARiderWithoutALapTimeOfTheirOwnIsJudgedByTheField()
  {
    var field = new[] { Rider("#7", 40, null), Rider("#12", 42, null), Rider("#23", 44, null) };

    var verdict = After(40, field, fieldPaceSeconds: 50);

    Assert.True(verdict.FromLapTimes);
    Assert.Equal(ReaderQuietLevel.Silent, verdict.Level);
  }

  [Fact]
  public void NeverAnAlarmSoonerThanHalfAMinuteHoweverShortTheLaps()
  {
    // A short track: 15 s laps, and all three riders late within 25 s.
    var field = new[] { Rider("#1", 10, 15), Rider("#2", 11, 15), Rider("#3", 12, 15) };

    Assert.Equal(ReaderQuietLevel.Quiet, After(25, field).Level);
    Assert.Equal(ReaderQuietLevel.Silent, After(31, field).Level);
  }

  [Fact]
  public void LatenessGrowsWithALongLap()
  {
    Assert.Equal(TimeSpan.FromSeconds(20), ReaderQuietCheck.Lateness(TimeSpan.FromSeconds(50)));
    Assert.Equal(TimeSpan.FromSeconds(150), ReaderQuietCheck.Lateness(TimeSpan.FromSeconds(1000)));
  }

  [Fact]
  public void TheWordsNameAFewRidersAndCountTheRest()
  {
    var many = After(35, BusyRace());
    Assert.StartsWith("No transponder reads for 35 seconds and ", many.Notice);
    Assert.EndsWith(" riders are overdue - check the reader", many.Notice);
    Assert.Equal(3, many.FirstOverdue.Count);
    Assert.StartsWith($"NO READS FOR 35s - {many.OverdueCount} overdue (", many.LogLine);
    Assert.Equal($"{many.OverdueCount} overdue - check the reader and the loop", many.TileDetail);

    var two = After(45, new[] { Rider("#7 Lukas", 80, 50), Rider("#12 Mia", 79, 50) });
    Assert.Contains("#7 Lukas and #12 Mia are overdue", two.Notice);

    var one = After(25, BusyRace().Take(1).Append(Rider("#99 Late", 50, 50)).ToList());
    Assert.Equal(ReaderQuietLevel.Quiet, one.Level);
    Assert.Equal("#99 Late is overdue", one.TileDetail);
  }
}
