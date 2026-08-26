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
