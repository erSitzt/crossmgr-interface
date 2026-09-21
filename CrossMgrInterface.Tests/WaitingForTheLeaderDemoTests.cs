using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// The "Waiting for the leader" demo teaches one thing: with no extra laps the
/// clock does not end the race, it sends the leader out to finish the lap they
/// are on, and everyone still out keeps racing until they come round.
///
/// It only teaches it if the riders are where the card says they are when the
/// clock runs out, and that comes out of the paces in the scenario. Nudge one
/// of them and the demo still runs, still finishes, and quietly shows nothing -
/// which no other test would notice. These pin the lesson itself.
/// </summary>
public class WaitingForTheLeaderDemoTests
{
  private static readonly DemoScenario Demo = DemoScenarios.Build(DemoScenarios.WaitingId);

  /// <summary>The clock runs from the first gate, so reads are placed on that same timeline.</summary>
  private static TimeSpan RaceTime(DemoCrossing read)
  {
    var wave = Demo.Classes.ToList().FindIndex(c => string.Equals(c, read.AfterWaveOf, StringComparison.OrdinalIgnoreCase));
    return read.At + (wave < 0 ? 0 : wave) * Demo.WaveGap!.Value;
  }

  private static TimeSpan Clock => TimeSpan.FromMinutes(Demo.DurationMinutes);

  private static List<TimeSpan> CrossingsOf(string number) => Demo.Crossings
    .Where(c => c.Tag == Demo.Roster.First(r => r.Number == number).Tag)
    .Select(RaceTime)
    .OrderBy(t => t)
    .ToList();

  [Fact]
  public void ItIsAStaggeredRaceRunWithNoExtraLaps()
  {
    // All three are the point: no extra laps is what used to end the race on
    // the clock, and the waves are how Lauf2 was run.
    Assert.Equal(SessionType.Race, Demo.SessionType);
    Assert.Equal(0, Demo.AdditionalLaps);
    Assert.Equal(TimeSpan.FromSeconds(30), Demo.WaveGap);
    Assert.Equal(new[] { "MX1", "MX2", "Youth" }, Demo.Classes);
  }

  [Fact]
  public void TheLeaderIsAwayOnAFreshLapWhenTheClockRunsOut()
  {
    // #7 crosses just before the clock, so the race has to wait a whole lap
    // for him. Without that gap there is no window for anyone to race in.
    var lukas = CrossingsOf("7");

    var last = lukas.Last(t => t <= Clock);
    var home = lukas.First(t => t > Clock);

    Assert.InRange(Clock - last, TimeSpan.Zero, TimeSpan.FromSeconds(3));
    Assert.InRange(home - Clock, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60));
  }

  [Fact]
  public void ARiderWhoCrossesJustAfterTheClockIsStillSentOut()
  {
    // #111 is the rider the card names. She crosses a second after the clock -
    // under the old rule that read ended her race - and the flag does not fall
    // until #7 comes round, so she is out on a lap that counts.
    var hanna = CrossingsOf("111");
    var flag = CrossingsOf("7").First(t => t > Clock);

    var afterTheClock = hanna.First(t => t > Clock);
    Assert.InRange(afterTheClock - Clock, TimeSpan.Zero, TimeSpan.FromSeconds(3));
    Assert.True(afterTheClock < flag,
      $"#111 crosses at {afterTheClock} and the flag falls at {flag}; she must still be racing when it does.");

    // And she comes round again after the flag, so that lap is hers.
    Assert.Contains(hanna, t => t > flag);
  }

  [Fact]
  public void EveryRiderStillOutAtTheFlagHasALapToFinish()
  {
    // The flag falls on #7's crossing. Anyone whose last read is before it
    // would show as finished early - exactly the Lauf2 symptom the demo is
    // about - so every rider must have a read after it.
    var flag = CrossingsOf("7").First(t => t > Clock);

    foreach (var rider in Demo.Roster)
      Assert.True(CrossingsOf(rider.Number).Any(t => t > flag),
        $"#{rider.Number} has no crossing after the flag at {flag}.");
  }
}
