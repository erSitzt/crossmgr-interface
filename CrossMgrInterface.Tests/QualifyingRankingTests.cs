using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// The gate pick order: the field ranked on best lap, which is what decides
/// the order riders choose their starting gate in for the race.
/// </summary>
public class QualifyingRankingTests
{
  [Fact]
  public void TheFastestLapTakesTheFirstGatePick()
  {
    var slow = RiderBuilder.Rider("SLOW", "45").Lap(30).Lap(42).Build();
    var fast = RiderBuilder.Rider("FAST", "127").Lap(30).Lap(38).Build();

    var ranking = QualifyingRanking.Rank(new[] { slow, fast });

    Assert.Equal("FAST", ranking[0].Rider.TagID);
    Assert.Equal(1, ranking[0].GatePick);
    Assert.Equal("SLOW", ranking[1].Rider.TagID);
    Assert.Equal(2, ranking[1].GatePick);
  }

  [Fact]
  public void TheOutLapIsNeverSomebodysBestLap()
  {
    // Lap 1 runs from the start of the session to the first crossing. A rider
    // sitting on the timing loop when the clock starts records it as 0.000s,
    // and publishing that as their best lap would hand them pole every time.
    var rider = RiderBuilder.Rider("OUT", "1").Lap(0).Lap(41).Build();

    var entry = QualifyingRanking.Rank(new[] { rider }).Single();

    Assert.Equal(TimeSpan.FromSeconds(41), entry.BestLapTime);
    Assert.Equal(2, entry.BestLapNumber);
  }

  [Fact]
  public void OnEqualBestLapsWhoeverSetItFirstRanksAhead()
  {
    // Both end up on 40.000. The rider who got there on lap 2 is ahead of the
    // one who needed until lap 4 - the standard motorsport tie-break.
    var early = RiderBuilder.Rider("EARLY", "127").Lap(30).Lap(40).Lap(45).Lap(45).Build();
    var late = RiderBuilder.Rider("LATE", "293").Lap(30).Lap(45).Lap(45).Lap(40).Build();

    var ranking = QualifyingRanking.Rank(new[] { late, early });

    Assert.Equal("EARLY", ranking[0].Rider.TagID);
    Assert.Equal("LATE", ranking[1].Rider.TagID);
    Assert.Equal(ranking[0].BestLapTime, ranking[1].BestLapTime);
  }

  [Fact]
  public void ABestLapSetLateInTheSessionStillCounts()
  {
    // "Any lap during the session" - a rider who finds it on the last lap is
    // ranked on that, not on where they were for most of the session.
    var rider = RiderBuilder.Rider("LATE", "7").Lap(30).Lap(48).Lap(47).Lap(39).Build();

    var entry = QualifyingRanking.Rank(new[] { rider }).Single();

    Assert.Equal(TimeSpan.FromSeconds(39), entry.BestLapTime);
    Assert.Equal(4, entry.BestLapNumber);
  }

  [Fact]
  public void RidersWithNoTimedLapAreListedLastButStillGetAPick()
  {
    // They pick a gate too - they just pick last. Leaving them off the sheet
    // would mean nobody notices they are missing until the gate is being picked.
    var timed = RiderBuilder.Rider("TIMED", "1").Lap(30).Lap(40).Build();
    var outLapOnly = RiderBuilder.Rider("OUTLAP", "2").Lap(30).Build();

    var ranking = QualifyingRanking.Rank(new[] { outLapOnly, timed });

    Assert.Equal("TIMED", ranking[0].Rider.TagID);
    Assert.Equal(QualifyingStatus.NoTime, ranking[1].Status);
    Assert.Equal("OUTLAP", ranking[1].Rider.TagID);
    Assert.Equal(2, ranking[1].GatePick);
    Assert.Null(ranking[1].BestLapTime);
  }

  [Fact]
  public void ARiderWhoNeverCrossedRanksBelowOneWhoWentOutAndSetNoTime()
  {
    // Being on track and failing to record a time is still more than not
    // starting, so the two groups are ordered and not merged.
    var wentOut = RiderBuilder.Rider("OUT", "88").Lap(30).Build();
    var neverWentOut = RiderBuilder.Rider("NEVER", "12").Build();

    var ranking = QualifyingRanking.Rank(new[] { neverWentOut, wentOut });

    Assert.Equal(QualifyingStatus.NoTime, ranking[0].Status);
    Assert.Equal("OUT", ranking[0].Rider.TagID);
    Assert.Equal(QualifyingStatus.DidNotGoOut, ranking[1].Status);
    Assert.Equal("NEVER", ranking[1].Rider.TagID);
  }

  [Fact]
  public void ARiderWithASingleCrossingHasNoTime()
  {
    // Surprising but correct: one crossing is an out-lap and nothing else.
    var rider = RiderBuilder.Rider("ONE", "3").Lap(35).Build();

    var entry = QualifyingRanking.Rank(new[] { rider }).Single();

    Assert.Equal(QualifyingStatus.NoTime, entry.Status);
    Assert.Null(entry.BestLapTime);
    Assert.Equal(0, entry.TimedLaps);
  }

  [Fact]
  public void ACrashedRiderKeepsTheTimeTheySet()
  {
    // DNF demotes on a race classification and must not here. It also gets set
    // automatically to everyone who was not still circulating at the flag, so
    // demoting on it would scramble the whole sheet.
    var crashed = RiderBuilder.Rider("CRASH", "127").Lap(30).Lap(38).Dnf().Build();
    var finished = RiderBuilder.Rider("FINE", "293").Lap(30).Lap(42).Build();

    var ranking = QualifyingRanking.Rank(new[] { finished, crashed });

    Assert.Equal("CRASH", ranking[0].Rider.TagID);
    Assert.Equal(1, ranking[0].GatePick);
  }

  [Fact]
  public void ARiderMarkedDidNotStartIsListedLast()
  {
    // Unlike DNF, this is an operator ruling that they were not in the session
    // at all, and it outranks whatever the loop happened to record.
    var dns = RiderBuilder.Rider("DNS", "5").Lap(30).Lap(35).Dns().Build();
    var raced = RiderBuilder.Rider("RACED", "9").Lap(30).Lap(50).Build();

    var ranking = QualifyingRanking.Rank(new[] { dns, raced });

    Assert.Equal("RACED", ranking[0].Rider.TagID);
    Assert.Equal(QualifyingStatus.DidNotGoOut, ranking[1].Status);
    Assert.Equal("DNS", ranking[1].Rider.TagID);
  }

  [Fact]
  public void GapIsMeasuredToPoleAndIntervalToTheRiderAhead()
  {
    // The two columns are different numbers from third place down; showing the
    // same value twice would make the sheet look right while telling a rider
    // the wrong thing about who they need to beat.
    var pole = RiderBuilder.Rider("P1", "1").Lap(30).Lap(40).Build();
    var second = RiderBuilder.Rider("P2", "2").Lap(30).Lap(41).Build();
    var third = RiderBuilder.Rider("P3", "3").Lap(30).Lap(43).Build();

    var ranking = QualifyingRanking.Rank(new[] { third, pole, second });

    Assert.Null(ranking[0].GapToPole);
    Assert.Null(ranking[0].IntervalToAhead);

    Assert.Equal(TimeSpan.FromSeconds(1), ranking[1].GapToPole);
    Assert.Equal(TimeSpan.FromSeconds(1), ranking[1].IntervalToAhead);

    Assert.Equal(TimeSpan.FromSeconds(3), ranking[2].GapToPole);
    Assert.Equal(TimeSpan.FromSeconds(2), ranking[2].IntervalToAhead);
  }

  [Fact]
  public void RiderNumbersSortNumericallyNotAlphabetically()
  {
    // Start numbers are free text, so a plain string sort puts #12 before #7.
    var seven = RiderBuilder.Rider("SEVEN", "7").Build();
    var twelve = RiderBuilder.Rider("TWELVE", "12").Build();
    var hundred = RiderBuilder.Rider("HUNDRED", "100").Build();

    var ranking = QualifyingRanking.Rank(new[] { hundred, twelve, seven });

    Assert.Equal(new[] { "SEVEN", "TWELVE", "HUNDRED" },
      ranking.Select(e => e.Rider.TagID).ToArray());
  }

  [Fact]
  public void TimedLapsExcludeTheOutLap()
  {
    // The "Laps" column counts laps that carry a time, which is what a rider
    // means when they ask how many laps they got.
    var rider = RiderBuilder.Rider("R", "1").Lap(30).Lap(40).Lap(41).Build();

    Assert.Equal(2, QualifyingRanking.Rank(new[] { rider }).Single().TimedLaps);
  }

  [Fact]
  public void GatePicksAreContinuousAcrossEveryGroup()
  {
    // No gaps and no repeats, or two riders are sent to the same gate.
    var riders = new[]
    {
      RiderBuilder.Rider("A", "1").Lap(30).Lap(40).Build(),
      RiderBuilder.Rider("B", "2").Lap(30).Lap(41).Build(),
      RiderBuilder.Rider("C", "3").Lap(30).Build(),
      RiderBuilder.Rider("D", "4").Build()
    };

    var ranking = QualifyingRanking.Rank(riders);

    Assert.Equal(new[] { 1, 2, 3, 4 }, ranking.Select(e => e.GatePick).ToArray());
  }

  [Fact]
  public void AnEmptyFieldProducesAnEmptySheet()
  {
    // Printing before anyone has gone out must not throw.
    Assert.Empty(QualifyingRanking.Rank(Array.Empty<RiderInfo>()));
  }
}
