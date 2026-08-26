using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// The measure that decides whether one rider has lapped another.
///
/// Worth testing thoroughly because the version this replaces was wrong in a way
/// nothing caught: it compared raw lap COUNTS, which differ by one for any two
/// riders on opposite sides of the start/finish line. On a 250-rider field that
/// turned 1,702 crossings into 15,225 "has LAPPED" announcements.
/// </summary>
public class RaceProgressTests
{
  private static readonly DateTime Start = RiderBuilder.RaceStart;

  private static void Close(double expected, double actual, double tolerance, string what) =>
    Assert.True(Math.Abs(expected - actual) <= tolerance,
      $"{what}: expected {expected}, got {actual} (tolerance {tolerance})");

  // ---- The measure itself ----------------------------------------------------

  [Fact]
  public void ARiderWhoHasJustCrossedIsExactlyOnTheirLapCount()
  {
    var rider = RiderBuilder.Rider("A").Lap(40).Lap(40).Lap(40).Build();

    Close(3, RaceProgress.Of(rider, rider.LastCrossing, null), 1e-9, "progress at the line");
  }

  [Fact]
  public void ProgressAdvancesThroughTheLapBetweenCrossings()
  {
    var rider = RiderBuilder.Rider("A").Lap(40).Lap(40).Lap(40).Build();

    Close(3.25, RaceProgress.Of(rider, rider.LastCrossing.AddSeconds(10), null), 1e-9, "quarter of a lap on");
    Close(3.5, RaceProgress.Of(rider, rider.LastCrossing.AddSeconds(20), null), 1e-9, "half a lap on");
  }

  [Fact]
  public void AnOverdueRiderDoesNotSilentlyGainALap()
  {
    // Clamped at a full lap. Without that, a rider who stopped would accumulate
    // progress for ever and start "lapping" the entire field from the trackside.
    var rider = RiderBuilder.Rider("A").Lap(40).Lap(40).Build();

    Close(2 + 1, RaceProgress.Of(rider, rider.LastCrossing.AddMinutes(10), null), 1e-9, "clamped progress");
  }

  [Fact]
  public void ARiderWithNoLapsHasMadeNoProgress()
  {
    var rider = RiderBuilder.Rider("A").Build();

    Assert.Equal(0, RaceProgress.Of(rider, Start.AddMinutes(5), TimeSpan.FromSeconds(40)));
  }

  [Fact]
  public void WithNoPaceAtAllProgressFallsBackToTheLapCount()
  {
    // No worse than the measure it replaces, never worse.
    var rider = RiderBuilder.Rider("A").Lap(40).Build();

    Assert.Equal(1, RaceProgress.Of(rider, rider.LastCrossing.AddSeconds(20), null));
  }

  // ---- What it was built to get right ---------------------------------------

  [Fact]
  public void TwoRidersSecondsApartAreNotAWholeLapApart()
  {
    // THE bug. Both are on the same lap of the race; one has simply crossed the
    // line and the other has not yet. Their lap COUNTS differ by one, and that is
    // what used to be announced as a lapping.
    var ahead = RiderBuilder.Rider("A").Lap(40).Lap(40).Lap(40).Build();
    var behind = RiderBuilder.Rider("B").Lap(40).Lap(40).Build();

    var now = ahead.LastCrossing;

    Assert.Equal(3, ahead.TotalLaps);
    Assert.Equal(2, behind.TotalLaps);

    // ...but three seconds of a forty second lap is 0.075 of a lap, not one.
    var lead = RaceProgress.WholeLapLead(
      RaceProgress.Of(ahead, now, null),
      RaceProgress.Of(behind, now.AddSeconds(-3), null));

    Assert.Equal(0, lead);
  }

  [Fact]
  public void ARiderAFullLapBehindIsReportedAsLapped()
  {
    // Measured at the BACKMARKER's crossing, which is where a one-lap deficit
    // actually becomes visible: their progress is then the whole number, and the
    // leader is a lap plus however far round they have got.
    //
    // At the LEADER's crossing the arithmetic runs the other way - the leader
    // sits on a whole number and the lead computes as one lap minus the
    // backmarker's fraction, which never reaches one. That asymmetry is why the
    // event check looks at both directions.
    var leader = RiderBuilder.Rider("A").Laps(4, 40).Build();
    var backmarker = RiderBuilder.Rider("B").Laps(3, 50).Build();

    var atBackmarkerCrossing = backmarker.LastCrossing;

    var lead = RaceProgress.WholeLapLead(
      RaceProgress.Of(leader, atBackmarkerCrossing, null),
      RaceProgress.Of(backmarker, atBackmarkerCrossing, null));

    Assert.Equal(1, lead);
  }

  [Fact]
  public void TwoLapsDownIsReportedAsTwo()
  {
    // Six laps at 40s against four at 60s - both 240 seconds into the race.
    var leader = RiderBuilder.Rider("A").Laps(6, 40).Build();
    var backmarker = RiderBuilder.Rider("B").Laps(4, 60).Build();

    var lead = RaceProgress.WholeLapLead(
      RaceProgress.Of(leader, leader.LastCrossing, null),
      RaceProgress.Of(backmarker, leader.LastCrossing, null));

    Assert.Equal(2, lead);
  }

  [Fact]
  public void ARiderAboutToBeLappedIsNotLappedYet()
  {
    // The leader is 0.95 of a lap ahead - close, and not the same thing.
    var leader = RiderBuilder.Rider("A").Lap(40).Lap(40).Lap(40).Build();
    var backmarker = RiderBuilder.Rider("B").Lap(40).Lap(40).Build();

    var now = leader.LastCrossing;

    var lead = RaceProgress.WholeLapLead(
      RaceProgress.Of(leader, now, null),
      RaceProgress.Of(backmarker, now.AddSeconds(-38), null));

    Assert.Equal(0, lead);
  }

  [Fact]
  public void BeingAheadInTheStandingsIsNotTheSameAsHavingLapped()
  {
    // Every rider in a spread-out field leads somebody by a lap count. Almost
    // none of them have lapped anybody.
    var field = Enumerable.Range(0, 20)
      .Select(i => RiderBuilder.Rider($"R{i}").Laps(5, 40).Build())
      .ToList();

    var now = field[0].LastCrossing;
    var lapped = 0;

    for (var i = 0; i < field.Count; i++)
      for (var j = 0; j < field.Count; j++)
      {
        if (i == j) continue;

        // Spread them a couple of seconds apart round the circuit.
        var a = RaceProgress.Of(field[i], now.AddSeconds(-i * 2), null);
        var b = RaceProgress.Of(field[j], now.AddSeconds(-j * 2), null);
        if (RaceProgress.WholeLapLead(a, b) > 0) lapped++;
      }

    Assert.Equal(0, lapped);
  }

  [Fact]
  public void AtTheLeadersOwnCrossingAOneLapLeadDoesNotYetShow()
  {
    // Pinned deliberately, because it looks like a bug and is not. The leader is
    // at a whole number of laps at that instant, so the lead is 1 minus the
    // backmarker's fraction. Only once the backmarker reaches the line does the
    // full lap show - hence checking both directions on every crossing.
    var leader = RiderBuilder.Rider("A").Laps(4, 40).Build();
    var backmarker = RiderBuilder.Rider("B").Laps(3, 50).Build();

    var atLeaderCrossing = leader.LastCrossing;

    var lead = RaceProgress.WholeLapLead(
      RaceProgress.Of(leader, atLeaderCrossing, null),
      RaceProgress.Of(backmarker, atLeaderCrossing, null));

    Assert.Equal(0, lead);
  }

  [Fact]
  public void AStoppedRidersDeficitIsUnderReportedRatherThanOverReported()
  {
    // Progress is clamped at one lap, so a rider who stopped two laps ago reads
    // as one lap down rather than two. That is the deliberate direction to err:
    // the clamp exists to stop a stationary rider accumulating progress for ever
    // and "lapping" the field from the trackside, and under-reporting a deficit
    // only ever means FEWER lapping announcements, never a false one.
    var leader = RiderBuilder.Rider("A").Laps(6, 40).Build();
    var stopped = RiderBuilder.Rider("B").Laps(4, 40).Build();

    var lead = RaceProgress.WholeLapLead(
      RaceProgress.Of(leader, leader.LastCrossing, null),
      RaceProgress.Of(stopped, leader.LastCrossing, null));

    Assert.Equal(1, lead);
  }

  [Fact]
  public void ALeadHoveringOnTheBoundaryIsStillJustOneLap()
  {
    // A pair sitting right around a full lap apart drifts across the boundary as
    // the pace estimate shifts each lap. WholeLapLead has to answer consistently
    // for values either side of it, and the caller keeps a high-water mark so the
    // same lapping is never announced twice.
    Assert.Equal(0, RaceProgress.WholeLapLead(5.00, 4.02));
    Assert.Equal(1, RaceProgress.WholeLapLead(5.04, 4.02));
    Assert.Equal(0, RaceProgress.WholeLapLead(5.01, 4.02));
    Assert.Equal(1, RaceProgress.WholeLapLead(5.02, 4.00));
  }

  [Fact]
  public void ALeadOfAlmostTwoLapsIsStillReportedAsOne()
  {
    // Floor, not round: two laps down means two whole laps, not one and a bit.
    Assert.Equal(1, RaceProgress.WholeLapLead(5.99, 4.0));
    Assert.Equal(2, RaceProgress.WholeLapLead(6.0, 4.0));
  }

  // ---- Field median ----------------------------------------------------------

  [Fact]
  public void TheFieldMedianIgnoresRidersWhoHaveStopped()
  {
    var field = new List<RiderInfo>
    {
      RiderBuilder.Rider("A").Lap(40).Lap(40).Build(),
      RiderBuilder.Rider("B").Lap(40).Lap(42).Build(),
      RiderBuilder.Rider("C").Lap(40).Lap(44).Build(),
      RiderBuilder.Rider("D").Lap(40).Lap(2400).Build()
    };

    // 2400s is beyond any lap, so D is discarded rather than dragging the median.
    Close(42, RaceProgress.MedianPace(field)!.Value.TotalSeconds, 0.001, "median pace");
  }

  [Fact]
  public void AFieldWithNoTimedLapsHasNoMedian()
  {
    var field = new List<RiderInfo> { RiderBuilder.Rider("A").Lap(40).Build() };

    Assert.Null(RaceProgress.MedianPace(field));
  }

  [Fact]
  public void ARiderWithNoPaceOfTheirOwnBorrowsTheFieldMedian()
  {
    var newcomer = RiderBuilder.Rider("A").Lap(40).Build();

    var progress = RaceProgress.Of(newcomer, newcomer.LastCrossing.AddSeconds(20), TimeSpan.FromSeconds(40));

    Close(1.5, progress, 1e-9, "progress from the field median");
  }
}
