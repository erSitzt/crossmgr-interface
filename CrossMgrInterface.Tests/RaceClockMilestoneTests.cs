using Xunit;

namespace CrossMgrInterface.Tests;

public class RaceClockMilestoneTests
{
  private static readonly TimeSpan Five = TimeSpan.FromMinutes(5);
  private static readonly TimeSpan One = TimeSpan.FromMinutes(1);

  [Fact]
  public void AnnouncesWhenTheClockReachesTheMilestone()
  {
    var due = RaceClockMilestones.ShouldAnnounce(
      remaining: TimeSpan.FromMinutes(4.9), duration: TimeSpan.FromMinutes(20),
      milestone: Five, alreadyShown: false);

    Assert.True(due);
  }

  [Fact]
  public void StaysQuietWhileThereIsPlentyOfTimeLeft()
  {
    Assert.False(RaceClockMilestones.ShouldAnnounce(
      TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(20), Five, false));
  }

  [Fact]
  public void DoesNotFireAtTheStartOfARaceShorterThanTheMilestone()
  {
    // A four-minute race begins with less than five minutes remaining. Warning
    // then is nonsense, and it trains the operator to ignore warnings.
    Assert.False(RaceClockMilestones.ShouldAnnounce(
      remaining: TimeSpan.FromMinutes(4), duration: TimeSpan.FromMinutes(4),
      milestone: Five, alreadyShown: false));
  }

  [Fact]
  public void ShortRaceStillGetsItsOneMinuteWarning()
  {
    // The five-minute warning is meaningless on a four-minute race; the
    // one-minute warning is exactly as meaningful as ever.
    Assert.True(RaceClockMilestones.ShouldAnnounce(
      remaining: TimeSpan.FromSeconds(55), duration: TimeSpan.FromMinutes(4),
      milestone: One, alreadyShown: false));
  }

  [Fact]
  public void NeverRepeatsAMilestone()
  {
    Assert.False(RaceClockMilestones.ShouldAnnounce(
      TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(20), Five, alreadyShown: true));
  }

  [Fact]
  public void SaysNothingOnceTheClockHasRunOut()
  {
    Assert.False(RaceClockMilestones.ShouldAnnounce(
      TimeSpan.Zero, TimeSpan.FromMinutes(20), Five, false));
    Assert.False(RaceClockMilestones.ShouldAnnounce(
      TimeSpan.FromSeconds(-30), TimeSpan.FromMinutes(20), Five, false));
  }

  [Fact]
  public void ARaceExactlyTheMilestoneLengthDoesNotWarnAtTheStart()
  {
    Assert.False(RaceClockMilestones.ShouldAnnounce(
      TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), Five, false));
  }
}
