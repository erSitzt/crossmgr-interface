using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>What the Race Day plan strip tells a volunteer, situation by situation.</summary>
public class PlanStripTests
{
  private static readonly DateTime Day = new(2026, 10, 18);
  private static DateTime At(int hour, int minute) => Day + new TimeSpan(hour, minute, 0);

  private static DayPlan Plan() => new()
  {
    Name = "Clubrennen",
    Date = Day,
    Sessions =
    {
      new PlannedSession { Name = "Moto 1 - MX1/MX2", Time = new TimeSpan(10, 0, 0), DurationMinutes = 15 },
      new PlannedSession { Name = "Moto 1 - Youth", Time = new TimeSpan(10, 30, 0), DurationMinutes = 15 }
    }
  };

  private static PlanStripModel Strip(DayPlan plan, Dictionary<string, PlanRun> runs, PlanScreen screen, DateTime now,
    string? ready = null, string? running = null, TimeSpan? countdown = null)
  {
    var forecast = PlanForecast.Forecast(plan, runs, ready, running, now);
    var decision = PlanRunnerPolicy.Decide(forecast, screen, now, false, null, null);
    var reminder = PlanRunnerPolicy.ReminderFor(decision.Next, now);
    return PlanStripText.Build(plan, forecast, decision, reminder, countdown, now);
  }

  [Fact]
  public void BeforeTheDayStartsItSaysWhatIsFirstAndWhenItIsSetUp()
  {
    var strip = Strip(Plan(), new(), PlanScreen.Nothing, At(9, 30));

    Assert.Equal("TODAY'S PLAN · Clubrennen", strip.Title);
    Assert.Equal("on time", strip.Timing);
    Assert.Equal("Next: Moto 1 - MX1/MX2 ~10:00 (in 30 min) - set up by itself at 09:45", strip.NextLine);
    Assert.True(strip.CanSetUpNow);
    Assert.Equal("SET UP MOTO 1 - MX1/MX2", strip.SetUpButtonText);
    Assert.Equal(PlanStripTone.Calm, strip.Tone);
  }

  [Fact]
  public void ASetUpSessionSaysHowItsClockStarts()
  {
    var plan = Plan();
    var screen = new PlanScreen(plan.Sessions[0].Id, true, false, false, false, null);
    var strip = Strip(plan, new(), screen, At(9, 57), ready: plan.Sessions[0].Id);

    Assert.StartsWith("Moto 1 - MX1/MX2 is set up - due ~10:00 (in 3 min). Press START RACE", strip.NextLine);
    Assert.False(strip.CanSetUpNow);
    Assert.Equal(PlanStripTone.Soon, strip.Tone);
  }

  [Fact]
  public void WhileASessionRunsItSaysHowLateTheDayIs()
  {
    var plan = Plan();
    var runs = new Dictionary<string, PlanRun> { [plan.Sessions[0].Id] = new(1, At(10, 8), null, false) };
    var screen = new PlanScreen(plan.Sessions[0].Id, true, false, true, false, null);
    var strip = Strip(plan, runs, screen, At(10, 15), running: plan.Sessions[0].Id);

    Assert.Equal("running 8 min late", strip.Timing);
    Assert.Equal("Next: Moto 1 - Youth ~10:38 (in 23 min)", strip.NextLine);
    Assert.StartsWith("▶ 10:08", strip.Chips[0].Text);
    Assert.True(strip.Chips[1].Next);
    Assert.False(strip.CanSetUpNow);
  }

  [Fact]
  public void TheCountdownBeforeASetUpOffersNotYet()
  {
    var plan = Plan();
    var strip = Strip(plan, new(), PlanScreen.Nothing, At(9, 46), countdown: TimeSpan.FromSeconds(24.2));

    Assert.Equal("Setting up Moto 1 - MX1/MX2 in 25 s", strip.NextLine);
    Assert.True(strip.ShowNotYet);
  }

  [Fact]
  public void ASessionSetUpByHandPausesThePlanAndSaysSo()
  {
    var plan = Plan();
    var byHand = new PlanScreen(null, FromPlan: false, Empty: false, Running: false, Finished: false, null);
    var strip = Strip(plan, new(), byHand, At(9, 50));

    Assert.StartsWith("Paused: the session on screen is not from the plan.", strip.NextLine);
    Assert.Equal(PlanStripTone.Attention, strip.Tone);
  }

  [Fact]
  public void AnInterruptedSessionIsPointedOut()
  {
    var plan = Plan();
    var runs = new Dictionary<string, PlanRun> { [plan.Sessions[0].Id] = new(1, At(10, 0), null, false) };
    var strip = Strip(plan, runs, PlanScreen.Nothing, At(10, 20));

    Assert.StartsWith("Moto 1 - MX1/MX2 was interrupted.", strip.NextLine);
    Assert.Equal("⚠ Moto 1 - MX1/MX2 - interrupted", strip.Chips[0].Text);
    Assert.True(strip.CanSkip);
  }

  [Fact]
  public void AtTheEndOfTheDayThereIsNothingLeft()
  {
    var plan = Plan();
    var runs = new Dictionary<string, PlanRun>
    {
      [plan.Sessions[0].Id] = new(1, At(10, 0), At(10, 20), true),
      [plan.Sessions[1].Id] = new(2, At(10, 30), At(10, 50), true)
    };
    var strip = Strip(plan, runs, new PlanScreen(plan.Sessions[1].Id, true, false, false, true, At(10, 50)), At(11, 0));

    Assert.Equal("Every session of the plan has run.", strip.NextLine);
    Assert.All(strip.Chips, c => Assert.StartsWith("✓", c.Text));
    Assert.Null(strip.SetUpButtonText);
  }

  [Fact]
  public void ALongDayShowsOnlyTheSessionsEitherSideOfNow()
  {
    var plan = Plan();
    for (var i = 0; i < 8; i++)
      plan.Sessions.Add(new PlannedSession { Name = $"Moto {i + 2}", Time = new TimeSpan(11 + i / 2, i % 2 * 30, 0) });
    var runs = plan.Sessions.Take(4).Select((s, i) => (s, i))
      .ToDictionary(p => p.s.Id, p => new PlanRun(p.i, plan.PlannedStart(p.s), plan.PlannedStart(p.s).AddMinutes(20), true));

    var strip = Strip(plan, runs, PlanScreen.Nothing, At(12, 5));

    Assert.Equal("✓ 3 earlier", strip.Chips[0].Text);
    Assert.StartsWith("✓", strip.Chips[1].Text);
    Assert.True(strip.Chips[2].Next);
    Assert.Equal("+3 more", strip.Chips[^1].Text);
    Assert.Equal(PlanStripText.ChipWindow + 2, strip.Chips.Count);
  }

  [Fact]
  public void TheRemindersNameTheClassesToSendToTheGate()
  {
    var plan = Plan();
    plan.Sessions[0].Classes = new List<string> { "MX1", "MX2" };
    var next = PlanForecast.Forecast(plan, new Dictionary<string, PlanRun>(), null, null, At(9, 55))[0];

    Assert.Equal("Moto 1 - MX1/MX2 is due in 5 minutes (10:00) - MX1, MX2 to the gate.",
      PlanStripText.ReminderText(next, PlanReminder.Soon));
    Assert.Contains("was due at 10:00", PlanStripText.ReminderText(next, PlanReminder.Overdue));
  }
}
