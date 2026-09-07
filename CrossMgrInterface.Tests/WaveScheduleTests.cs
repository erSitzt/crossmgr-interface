using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// How a staggered start is scheduled and recorded: who goes next, when they
/// are due, and what happens to the timetable when a gate is early or late.
/// </summary>
public class WaveScheduleTests
{
  private static readonly DateTime Gate = new(2026, 9, 12, 10, 0, 0);
  private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

  private static WaveSchedule ThreeClasses() =>
    WaveSchedule.Build(new[] { "MX1", "MX2", "Youth" }, Minute);

  [Fact]
  public void BuildSpacesTheClassesByTheGap()
  {
    var schedule = ThreeClasses();

    Assert.Equal(new[] { "MX1", "MX2", "Youth" }, schedule.Waves.Select(w => w.Class));
    Assert.Equal(TimeSpan.Zero, schedule.Waves[0].Delay);
    Assert.Equal(Minute, schedule.Waves[1].Delay);
    Assert.Equal(Minute, schedule.Waves[2].Delay);
  }

  [Fact]
  public void NothingIsDueUntilTheOperatorStartsTheFirstWave()
  {
    // The first gate is the operator's: the app never starts it on its own.
    var schedule = ThreeClasses();

    Assert.Same(schedule.First, schedule.Next);
    Assert.Null(schedule.DueAt(schedule.First));
    Assert.Null(schedule.NextDue(Gate.AddHours(1)));
    Assert.Null(schedule.Countdown(Gate));
  }

  [Fact]
  public void EachWaveIsDueItsDelayAfterThePreviousOneActuallyLeft()
  {
    var schedule = ThreeClasses();
    schedule.Start(schedule.First, Gate);

    var mx2 = schedule.Next!;
    Assert.Equal("MX2", mx2.Class);
    Assert.Equal(Gate.AddMinutes(1), schedule.DueAt(mx2));
    Assert.Null(schedule.NextDue(Gate.AddSeconds(59)));
    Assert.Same(mx2, schedule.NextDue(Gate.AddSeconds(60)));
    Assert.Equal(TimeSpan.FromSeconds(42), schedule.Countdown(Gate.AddSeconds(18)));
  }

  [Fact]
  public void AnEarlyStartPullsTheFollowingWavesForwardByTheSameAmount()
  {
    // The delay is between gates, not from a timetable. When MX2 goes twenty
    // seconds early, the starter is already counting a minute from that
    // moment for Youth - and so must the app.
    var schedule = ThreeClasses();
    schedule.Start(schedule.First, Gate);
    schedule.Start(schedule.Next!, Gate.AddSeconds(40));

    Assert.Equal(Gate.AddSeconds(100), schedule.DueAt(schedule.Next!));
  }

  [Fact]
  public void ALateStartPushesTheFollowingWavesBackByTheSameAmount()
  {
    var schedule = ThreeClasses();
    schedule.Start(schedule.First, Gate);
    schedule.Start(schedule.Next!, Gate.AddSeconds(90));

    Assert.Equal(Gate.AddSeconds(150), schedule.DueAt(schedule.Next!));
  }

  [Fact]
  public void WavesCanOnlyBeStartedInOrder()
  {
    var schedule = ThreeClasses();

    Assert.Throws<InvalidOperationException>(() => schedule.Start(schedule.Waves[1], Gate));
  }

  [Fact]
  public void RidersAreTimedFromTheirOwnClassGate()
  {
    var schedule = ThreeClasses();
    schedule.Start(schedule.First, Gate);
    schedule.Start(schedule.Next!, Gate.AddMinutes(1));

    Assert.Equal(Gate, schedule.StartTimeFor("MX1"));
    Assert.Equal(Gate.AddMinutes(1), schedule.StartTimeFor("mx2"));
    Assert.True(schedule.HasStarted("MX2"));
    Assert.False(schedule.HasStarted("Youth"));
    Assert.Null(schedule.StartTimeFor("Youth"));
  }

  [Fact]
  public void AnUnknownClassGoesWithTheFirstWave()
  {
    // An unidentified transponder has no class, and a class missing from the
    // schedule is a setup slip. Both are timed from the first gate and counted,
    // rather than thrown away until somebody notices.
    var schedule = ThreeClasses();
    schedule.Start(schedule.First, Gate);

    Assert.Same(schedule.First, schedule.WaveFor(""));
    Assert.Same(schedule.First, schedule.WaveFor(null));
    Assert.Same(schedule.First, schedule.WaveFor("Quad"));
    Assert.True(schedule.HasStarted("Quad"));
    Assert.Equal(Gate, schedule.StartTimeFor(null));
  }

  [Fact]
  public void AllStartedOnceTheLastWaveHasGone()
  {
    var schedule = ThreeClasses();
    schedule.Start(schedule.First, Gate);
    schedule.Start(schedule.Next!, Gate.AddMinutes(1));
    Assert.False(schedule.AllStarted);

    schedule.Start(schedule.Next!, Gate.AddMinutes(2));
    Assert.True(schedule.AllStarted);
    Assert.Null(schedule.Next);
    Assert.Null(schedule.Countdown(Gate.AddMinutes(3)));
  }

  [Fact]
  public void ResetForgetsTheStartsButNotTheSchedule()
  {
    var schedule = ThreeClasses();
    schedule.Start(schedule.First, Gate);
    schedule.Start(schedule.Next!, Gate.AddMinutes(1));

    schedule.ResetStarts();

    Assert.Same(schedule.First, schedule.Next);
    Assert.All(schedule.Waves, w => Assert.Null(w.StartedAt));
    Assert.Equal(Minute, schedule.Waves[2].Delay);
  }

  [Fact]
  public void DescribesTheScheduleAndTheActualStarts()
  {
    var schedule = WaveSchedule.From(new[]
    {
      ("MX1", TimeSpan.Zero),
      ("MX2", TimeSpan.FromMinutes(1)),
      ("Youth", TimeSpan.FromSeconds(90))
    })!;
    schedule.Start(schedule.First, Gate);

    Assert.Equal("MX1 at the gate, MX2 +1:00, Youth +1:30", schedule.Describe());
    Assert.Equal("MX1 10:00:00, MX2 not started, Youth not started", schedule.DescribeStarts());
  }

  [Fact]
  public void FromReturnsNullForARaceWithOneStart()
  {
    Assert.Null(WaveSchedule.From(null));
    Assert.Null(WaveSchedule.From(Array.Empty<(string, TimeSpan)>()));
  }

  [Fact]
  public void SurvivesTheDatabaseRoundTripWithItsStarts()
  {
    var schedule = ThreeClasses();
    schedule.Start(schedule.First, Gate);

    var restored = WaveSchedule.FromRecords(schedule.ToRecords())!;

    Assert.Equal(schedule.Describe(), restored.Describe());
    Assert.Equal(Gate, restored.First.StartedAt);
    Assert.Equal("MX2", restored.Next!.Class);
    Assert.Equal(Gate.AddMinutes(1), restored.DueAt(restored.Next!));
    Assert.Null(WaveSchedule.FromRecords(null));
  }
}
