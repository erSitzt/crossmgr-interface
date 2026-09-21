using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// How a timed session ends: what each rider is still allowed to complete when
/// the flag comes out, and how long they are given to do it.
/// </summary>
public class ChequeredFlagTests
{
  private static readonly DateTime Flag = RiderBuilder.RaceStart.AddMinutes(20);

  [Fact]
  public void ALapStartedBeforeTheFlagStillCounts()
  {
    // The whole point of a chequered flag: a rider on a flying lap when the
    // clock runs out gets to finish it, and it counts towards their best.
    var rider = RiderBuilder.Rider("R", "1").Laps(5, 120).Build();

    // Five crossings, all inside the twenty minutes.
    Assert.Equal(5, rider.LapsCompletedBy(Flag));
    Assert.Equal(6, rider.LapsCompletedBy(Flag) + 1);
  }

  [Fact]
  public void ACrossingAfterTheFlagIsNotAnAllowance()
  {
    // The allowance is fixed at the flag. A lap completed afterwards is the one
    // the rider was already on, not a licence to start another.
    var rider = RiderBuilder.Rider("R", "1").Laps(11, 120).Build();

    // Eleven laps of two minutes runs to 22:00, so the last one is past the flag.
    Assert.Equal(10, rider.LapsCompletedBy(Flag));
  }

  [Fact]
  public void ACrossingExactlyOnTheFlagCounts()
  {
    // The boundary the allowance is counted from. Reading TotalLaps instead
    // would make this depend on whether the timer or the network thread reached
    // the lock first, and hand the rider an extra lap half the time.
    var rider = RiderBuilder.Rider("R", "1").Laps(10, 120).Build();

    Assert.Equal(Flag, rider.Laps[^1].CrossingTime);
    Assert.Equal(10, rider.LapsCompletedBy(Flag));
  }

  [Fact]
  public void ARiderWhoNeverWentOutIsAllowedOneLap()
  {
    // FinalAllowedLap of 1 - they may still complete an out-lap, which is right:
    // they were on track when the flag came out.
    var rider = RiderBuilder.Rider("R", "1").Build();

    Assert.Equal(0, rider.LapsCompletedBy(Flag));
  }

  [Fact]
  public void TheGraceAfterTheFlagCoversAFlagLap()
  {
    // The default DNF timeout is two minutes and a motocross lap is often
    // longer, so on the configured value alone a rider riding a good flag lap
    // is written off mid-lap - and the app then discards the crossing that
    // would have set their gate pick.
    var configured = TimeSpan.FromMinutes(2);
    var pace = TimeSpan.FromSeconds(140);

    var grace = ChequeredFlag.Grace(configured, pace);

    Assert.Equal(TimeSpan.FromSeconds(210), grace);
    Assert.True(grace > pace, "the grace has to outlast a single lap");
  }

  [Fact]
  public void TheGraceNeverShortensWhatTheOperatorConfigured()
  {
    // A short lap must not shrink a deliberately generous timeout.
    var configured = TimeSpan.FromMinutes(5);

    Assert.Equal(configured, ChequeredFlag.Grace(configured, TimeSpan.FromSeconds(40)));
  }

  [Fact]
  public void WithNoPaceAtAllTheConfiguredTimeoutStands()
  {
    // Nobody has set a timed lap, so there is nothing to scale from.
    var configured = TimeSpan.FromMinutes(2);

    Assert.Equal(configured, ChequeredFlag.Grace(configured, null));
  }

  [Fact]
  public void ARiderWhoCrossedJustBeforeTheFlagWasStillOut()
  {
    // Mid-lap when the clock ran out - this is the rider the operator needs to
    // hear about, because the session is waiting on them.
    Assert.True(ChequeredFlag.WasCirculatingAtFlag(
      TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(42)));
  }

  [Fact]
  public void ARiderWhoPulledInLongBeforeTheFlagWasNot()
  {
    // Their last crossing is over two laps old, so they were already overdue
    // when the flag fell. They finished their session and went home; announcing
    // them as off track would be a warning about nothing.
    Assert.False(ChequeredFlag.WasCirculatingAtFlag(
      TimeSpan.FromSeconds(96), TimeSpan.FromSeconds(42)));
  }

  [Fact]
  public void ASlowLapStillCountsAsCirculating()
  {
    // A rider having a bad lap is still out on it. The boundary matches the
    // grace they are given after the flag, so the two rules agree.
    Assert.True(ChequeredFlag.WasCirculatingAtFlag(
      TimeSpan.FromSeconds(62), TimeSpan.FromSeconds(42)));
    Assert.False(ChequeredFlag.WasCirculatingAtFlag(
      TimeSpan.FromSeconds(64), TimeSpan.FromSeconds(42)));
  }

  [Fact]
  public void WithNoPaceToJudgeByTheRiderIsAssumedToHaveBeenOut()
  {
    // Over-reporting costs a line in the feed. Under-reporting hides a rider
    // who went out and never came back, so the doubt breaks that way.
    Assert.True(ChequeredFlag.WasCirculatingAtFlag(TimeSpan.FromMinutes(10), null));
  }
}

/// <summary>
/// What a rider may still ride once the flag is out, and why a correction made
/// after the flag must not change it beyond what the correction itself changed.
/// </summary>
public class FinalLapAllowanceTests
{
  private static readonly DateTime Start = RiderBuilder.RaceStart;

  [Fact]
  public void TheFlagAllowsTheLapInProgress()
  {
    Assert.Equal(7, ChequeredFlag.AllowedLap(lapsCompletedAtFlag: 6, targetLaps: 0));
  }

  [Fact]
  public void ARiderAlreadyAtTheTargetWhenTheLeaderFinishedRidesNoMore()
  {
    Assert.Equal(10, ChequeredFlag.AllowedLap(lapsCompletedAtFlag: 10, targetLaps: 10));
    Assert.Equal(10, ChequeredFlag.AllowedLap(lapsCompletedAtFlag: 9, targetLaps: 10));
  }

  [Fact]
  public void SplittingAMissedReadAfterTheFlagDoesNotGrantAnotherLap()
  {
    // Crossings at 40, 80, 120, 200 (a missed read) and 240; the flag at 250;
    // the lap in progress completes at 290. That was the rider's last lap.
    var rider = RiderBuilder.Rider("R").Lap(40).Lap(40).Lap(40).Lap(80).Lap(40).Lap(50).Build();
    var flag = Start.AddSeconds(250);
    Assert.Equal(6, ChequeredFlag.AllowedLap(rider.LapsCompletedBy(flag), 0));

    // After the flag, the operator splits the missed read into two laps.
    var field = new Dictionary<string, RiderInfo> { ["R"] = rider };
    var service = new RaceCorrectionService(field, new object(), () => Start, _ => { });
    Assert.True(service.SplitLap("R", 4, 2, rider.Revision).Ok);

    // Seven laps now, and seven allowed: the split added a lap before the flag,
    // so the allowance moves with it - and the rider has still ridden their last.
    Assert.Equal(7, rider.TotalLaps);
    Assert.Equal(7, ChequeredFlag.AllowedLap(rider.LapsCompletedBy(flag), 0));
  }

  [Fact]
  public void ALapAddedAfterTheFlagIsTheOneTheRiderWasOn()
  {
    // Five laps by the flag, allowed six. The sixth crossing was missed and the
    // operator adds it by hand after the flag.
    var rider = RiderBuilder.Rider("R").Laps(5, 40).Build();
    var flag = Start.AddSeconds(210);
    var field = new Dictionary<string, RiderInfo> { ["R"] = rider };
    var service = new RaceCorrectionService(field, new object(), () => Start, _ => { });

    Assert.True(service.AddLap("R", Start.AddSeconds(240), rider.Revision).Ok);

    Assert.Equal(6, rider.TotalLaps);
    Assert.Equal(6, ChequeredFlag.AllowedLap(rider.LapsCompletedBy(flag), 0));
  }
}

/// <summary>
/// A race ends on the leader's finish, never on the clock.
///
/// The clock only tells the leader to come round; the flag falls when they do,
/// and everyone else is then allowed the lap they are on. Ending a race on the
/// clock instead sounds the same and is not: the field is spread around the
/// track, so it ends the day for whoever happens to be a few seconds past the
/// loop while the leader, a few seconds short of it, rides a whole further lap.
/// </summary>
public class LeaderFinishTests
{
  private static readonly DateTime Start = RiderBuilder.RaceStart;

  [Fact]
  public void WithNoExtraLapsTheLeaderStillRidesTheLapInProgress()
  {
    // Zero extra laps does not mean the clock ends the race. It means the
    // leader rides the lap they are on and no more.
    Assert.Equal(16, ChequeredFlag.TargetLaps(leaderLapsAtExpiry: 15, additionalLaps: 0));
  }

  [Fact]
  public void ExtraLapsAreCountedOnTopOfTheLapInProgress()
  {
    Assert.Equal(17, ChequeredFlag.TargetLaps(leaderLapsAtExpiry: 15, additionalLaps: 1));
    Assert.Equal(19, ChequeredFlag.TargetLaps(leaderLapsAtExpiry: 15, additionalLaps: 3));
  }

  [Fact]
  public void ARiderWhoCrossesJustAfterTheClockStillFinishesTheirLap()
  {
    // Lauf2, 20.09.2026 - a two-hour race, started in waves, run with no extra
    // laps. The clock ran out at 7200s. The leader had crossed 75s before it,
    // so they were away on a fresh lap and did not come round until 7619s.
    //
    // A rider who crossed one second after the clock had their race ended
    // there and then, 418 seconds before the leader's, and the full lap they
    // went on to ride was thrown away. Sixty-eight riders lost a lap that way.
    var clock = Start.AddSeconds(7200);

    var leader = RiderBuilder.Rider("LEAD").Laps(15, 475).Lap(494).Build();
    var backMarker = RiderBuilder.Rider("BACK").Laps(11, 600).Lap(601).Build();

    // What the old rule did: flag the whole field on the clock. The back
    // marker's crossing one second later was the last thing they were allowed.
    Assert.Equal(12, ChequeredFlag.AllowedLap(backMarker.LapsCompletedBy(clock), targetLaps: 0));

    // What it does now. The clock sets the leader a target - the lap they are
    // on - and the flag waits for them to reach it.
    var target = ChequeredFlag.TargetLaps(leader.LapsCompletedBy(clock), additionalLaps: 0);
    Assert.Equal(16, target);

    var flag = Start.AddSeconds(7619);
    Assert.Equal(16, leader.LapsCompletedBy(flag));

    // The leader is home and rides no further lap.
    Assert.Equal(16, ChequeredFlag.AllowedLap(leader.LapsCompletedBy(flag), target));

    // The back marker is allowed the lap they are on at that moment - the one
    // they actually rode, and which used not to count.
    Assert.Equal(13, ChequeredFlag.AllowedLap(backMarker.LapsCompletedBy(flag), target));
  }

  [Fact]
  public void ARiderOnTheLeadersLapWhenTheFlagFallsRidesNoMore()
  {
    // Being on the same lap as the leader means the flag is out for them too.
    var rider = RiderBuilder.Rider("R").Laps(16, 450).Build();
    var flag = Start.AddSeconds(16 * 450);

    Assert.Equal(16, ChequeredFlag.AllowedLap(rider.LapsCompletedBy(flag), targetLaps: 16));
  }

  [Fact]
  public void TheWaitForTheLeaderCoversTheLapsTheyStillOwe()
  {
    var pace = TimeSpan.FromMinutes(8);
    var dnf = TimeSpan.FromMinutes(2);

    // With no extra laps the leader owes one lap, and the lap and a half that
    // Grace already allows covers it.
    Assert.Equal(ChequeredFlag.Grace(dnf, pace), ChequeredFlag.LeaderWait(dnf, pace, 0));

    // With extra laps it must not: flagging the leader off half way round
    // their second extra lap would end the race early for the whole field.
    Assert.Equal(TimeSpan.FromMinutes(12 + 8), ChequeredFlag.LeaderWait(dnf, pace, 1));
    Assert.Equal(TimeSpan.FromMinutes(12 + 16), ChequeredFlag.LeaderWait(dnf, pace, 2));
  }

  [Fact]
  public void WithNoPaceToJudgeByTheWaitIsTheOperatorsTimeout()
  {
    // Nobody has completed a lap, so there is no pace to scale by. The
    // operator's own timeout is all there is to go on.
    var dnf = TimeSpan.FromMinutes(5);
    Assert.Equal(dnf, ChequeredFlag.LeaderWait(dnf, null, 2));
  }
}
