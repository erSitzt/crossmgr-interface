using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// The day plan's rules, without a window: what a planned session is set up
/// with, how the day's times move when it runs late, and when the next
/// session is put on screen.
/// </summary>
public sealed class DayPlanTests : IDisposable
{
  private static readonly DateTime Day = new(2026, 10, 18);
  private readonly string _folder = Path.Combine(Path.GetTempPath(), $"crossmgr-plans-{Guid.NewGuid():N}");

  public void Dispose()
  {
    try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
  }

  private static PlannedSession Session(string name, int hour, int minute, int length = 15, int extraLaps = 1,
    PlannedStartMode start = PlannedStartMode.Gate, params string[] classes) => new()
  {
    Name = name,
    Time = new TimeSpan(hour, minute, 0),
    DurationMinutes = length,
    AdditionalLaps = extraLaps,
    Start = start,
    Classes = classes.ToList()
  };

  /// <summary>
  /// A morning of four races, each planned 15 minutes plus two laps of 2:30:
  /// 20 minutes on track, then a 10-minute break before the next one.
  /// </summary>
  private static DayPlan Morning() => new()
  {
    Name = "Clubrennen",
    Date = Day,
    ChangeoverMinutes = 5,
    TypicalLapSeconds = 150,
    Sessions =
    {
      Session("Moto 1 - MX1/MX2", 10, 0, classes: new[] { "MX1", "MX2" }),
      Session("Moto 1 - Youth", 10, 30, classes: "Youth"),
      Session("Moto 2 - MX1/MX2", 11, 0, classes: new[] { "MX1", "MX2" }),
      Session("Moto 2 - Youth", 11, 30, classes: "Youth")
    }
  };

  private static DateTime At(int hour, int minute) => Day + new TimeSpan(hour, minute, 0);

  private static Dictionary<string, PlanRun> Runs(DayPlan plan, params (int Index, DateTime Start, DateTime? End)[] runs) =>
    runs.ToDictionary(r => plan.Sessions[r.Index].Id, r => new PlanRun(100 + r.Index, r.Start, r.End, r.End != null));

  // ---- the plan ----------------------------------------------------------

  [Fact]
  public void APlannedSessionIsSetUpAsTheWizardWouldHaveSetItUp()
  {
    var session = Session("Moto 1 - MX1/MX2", 10, 0, length: 20, extraLaps: 2, classes: new[] { "MX1", "MX2" });
    session.OverallGroup = "MX1/MX2";

    var first = session.ToSetup(overall: null);
    Assert.Equal("Moto 1 - MX1/MX2", first.RaceName);
    Assert.Equal(20, first.DurationMinutes);
    Assert.Equal(2, first.AdditionalLaps);
    Assert.True(first.ManualStart);
    Assert.Equal(new[] { "MX1", "MX2" }, first.Classes);
    Assert.Null(first.Waves);
    Assert.True(first.NewOverall);
    Assert.Equal("Overall - MX1/MX2", first.OverallName);

    var second = session.ToSetup(overall: 7);
    Assert.Equal(7, second.OverallId);
    Assert.False(second.NewOverall);
  }

  [Fact]
  public void AWaveStartTakesItsOrderFromTheClasses()
  {
    var session = Session("Enduro", 13, 0, start: PlannedStartMode.Waves, classes: new[] { "E1", "E2", "E3" });
    session.WaveGapSeconds = 90;

    var waves = session.ToSetup(null).Waves!;
    Assert.Equal(new[] { "E1", "E2", "E3" }, waves.Select(w => w.Class));
    Assert.Equal(new[] { 0.0, 90, 90 }, waves.Select(w => w.Delay.TotalSeconds));
  }

  [Fact]
  public void ATimedSessionHasNoExtraLapsAndNoOverall()
  {
    var session = Session("Qualifying - MX1", 9, 0, extraLaps: 2, start: PlannedStartMode.FirstRider, classes: "MX1");
    session.SessionType = SessionType.TimedQualifying;
    session.OverallGroup = "MX1";

    var setup = session.ToSetup(null);
    Assert.Equal(0, setup.AdditionalLaps);
    Assert.False(setup.ManualStart);
    Assert.False(setup.NewOverall);
    Assert.Null(setup.OverallId);
  }

  [Theory]
  [InlineData("Moto 1 - MX1/MX2", "Moto 2 - MX1/MX2", "MX1/MX2")]
  [InlineData("Lauf 1 Jugend", "Lauf 2 Jugend", "Jugend")]
  [InlineData("moto 9", "moto 10", "MX1")]
  [InlineData("Final", "Final (2)", "Final")]
  public void AddMoto2CountsTheNameOnAndKeepsTheOverall(string name, string expected, string group)
  {
    var next = Session(name, 10, 0, classes: "MX1").NextMoto();
    Assert.Equal(expected, next.Name);
    Assert.Equal(group, next.OverallGroup);
    Assert.Equal(new[] { "MX1" }, next.Classes);
  }

  [Fact]
  public void ACopyForAnotherDayHasNewIdentitiesAndNoTypedTimes()
  {
    var plan = Morning();
    plan.Sessions[1].TypedTime = new TimeSpan(10, 40, 0);
    plan.Sessions[2].Skipped = true;

    var copy = plan.CopyFor(Day.AddDays(14));
    Assert.Equal(Day.AddDays(14), copy.Date);
    Assert.NotEqual(plan.Id, copy.Id);
    Assert.NotEqual(plan.PublicId, copy.PublicId);
    Assert.Empty(copy.Sessions.Select(s => s.Id).Intersect(plan.Sessions.Select(s => s.Id)));
    Assert.All(copy.Sessions, s => Assert.False(s.TypedTime.HasValue || s.Skipped));
    Assert.Equal(plan.Sessions.Select(s => s.Time), copy.Sessions.Select(s => s.Time));
  }

  [Fact]
  public void PlanProblemsAreFoundBeforeTheDay()
  {
    var plan = Morning();
    plan.Sessions[1].Classes = new List<string> { "Yuoth" };
    plan.Sessions[0].OverallGroup = "MX";
    plan.Sessions[1].OverallGroup = "MX";
    (plan.Sessions[2], plan.Sessions[3]) = (plan.Sessions[3], plan.Sessions[2]);

    var problems = plan.Problems(new[] { "MX1", "MX2", "Youth" });
    Assert.Contains(problems, p => p.Contains("Yuoth"));
    Assert.Contains(problems, p => p.Contains("different classes"));
    Assert.Contains(problems, p => p.Contains("order"));
    Assert.Empty(Morning().Problems(new[] { "MX1", "MX2", "Youth" }));
  }

  [Fact]
  public void PlansAreKeptInFilesAndAnUnreadableOneIsSetAside()
  {
    var store = PlanStore.Load(_folder);
    var plan = Morning();
    store.Save(plan);
    File.WriteAllText(Path.Combine(_folder, "broken" + PlanStore.Extension), "{ not json");

    var again = PlanStore.Load(_folder);
    var read = Assert.Single(again.Plans);
    Assert.Equal(plan.Id, read.Id);
    Assert.Equal(plan.Sessions.Select(s => s.Name), read.Sessions.Select(s => s.Name));
    Assert.Equal(new[] { "MX1", "MX2" }, read.Sessions[0].Classes);
    Assert.Single(again.SetAside);
    Assert.Single(again.For(Day));
  }

  // ---- the forecast -------------------------------------------------------

  [Fact]
  public void ADayOnTimeIsExpectedAsPlanned()
  {
    var plan = Morning();
    var forecast = PlanForecast.Forecast(plan, Runs(plan), null, null, At(9, 0));

    Assert.Equal(new[] { At(10, 0), At(10, 30), At(11, 0), At(11, 30) }, forecast.Select(e => e.ExpectedStart));
    Assert.All(forecast, e => Assert.Equal(TimeSpan.Zero, e.Late));
  }

  [Fact]
  public void ALateStartMovesEverySessionAfterItByAsMuch()
  {
    var plan = Morning();
    // Moto 1 MX1/MX2 started at 10:12 and is still running at 10:20.
    var runs = Runs(plan, (0, At(10, 12), null));
    var forecast = PlanForecast.Forecast(plan, runs, null, plan.Sessions[0].Id, At(10, 20));

    Assert.Equal(PlanEntryStatus.Running, forecast[0].Status);
    Assert.Equal(new[] { At(10, 42), At(11, 12), At(11, 42) }, forecast.Skip(1).Select(e => e.ExpectedStart));
    Assert.Equal(TimeSpan.FromMinutes(12), forecast[1].Late);
  }

  [Fact]
  public void ASessionThatRunsLongPushesTheNextPastItsBreak()
  {
    var plan = Morning();
    // Started on time, still going at 10:35 - ten minutes past its planned end.
    var runs = Runs(plan, (0, At(10, 0), null));
    var forecast = PlanForecast.Forecast(plan, runs, null, plan.Sessions[0].Id, At(10, 35));

    // It ends no sooner than now; then the changeover.
    Assert.Equal(At(10, 40), forecast[1].ExpectedStart);
    Assert.Equal(At(11, 10), forecast[2].ExpectedStart);
  }

  [Fact]
  public void ADayThatRunsEarlyDoesNotPullTheTimetableForward()
  {
    var plan = Morning();
    var runs = Runs(plan, (0, At(10, 0), At(10, 15)));
    var forecast = PlanForecast.Forecast(plan, runs, null, null, At(10, 16));

    Assert.Equal(At(10, 30), forecast[1].ExpectedStart);
  }

  [Fact]
  public void ASessionNotStartedWhenDueGetsLaterAndTakesTheRestWithIt()
  {
    var plan = Morning();
    var runs = Runs(plan, (0, At(10, 0), At(10, 20)));
    var forecast = PlanForecast.Forecast(plan, runs, null, null, At(10, 50));

    Assert.Equal(At(10, 30), forecast[1].DueAt);
    Assert.Equal(At(10, 50), forecast[1].ExpectedStart);
    Assert.Equal(At(11, 20), forecast[2].ExpectedStart);
  }

  [Fact]
  public void ATypedTimeStaysWhereItWasPut()
  {
    var plan = Morning();
    plan.Sessions[2].TypedTime = new TimeSpan(11, 15, 0);
    var runs = Runs(plan, (0, At(10, 5), null));
    var forecast = PlanForecast.Forecast(plan, runs, null, plan.Sessions[0].Id, At(10, 10));

    Assert.Equal(At(10, 35), forecast[1].ExpectedStart);
    Assert.Equal(At(11, 15), forecast[2].ExpectedStart);
    // Moto 2 MX1/MX2 was moved 15 minutes, so Moto 2 Youth moves 15 too - not
    // just the 5 the morning was running late.
    Assert.Equal(At(11, 45), forecast[3].ExpectedStart);
  }

  [Fact]
  public void ASkippedSessionTakesNoTime()
  {
    var plan = Morning();
    plan.Sessions[1].Skipped = true;
    var runs = Runs(plan, (0, At(10, 10), At(10, 30)));
    var forecast = PlanForecast.Forecast(plan, runs, null, null, At(10, 31));

    Assert.Equal(PlanEntryStatus.Skipped, forecast[1].Status);
    Assert.Equal(At(11, 10), forecast[2].ExpectedStart);
    Assert.Same(forecast[2], PlanForecast.Next(forecast));
  }

  [Fact]
  public void AStartedSessionNotOnScreenWasInterrupted()
  {
    var plan = Morning();
    var forecast = PlanForecast.Forecast(plan, Runs(plan, (0, At(10, 0), null)), null, null, At(10, 10));
    Assert.Equal(PlanEntryStatus.Interrupted, forecast[0].Status);
  }

  // ---- the runner ---------------------------------------------------------

  private static (IReadOnlyList<ForecastEntry> Forecast, PlanScreen Screen) AfterMoto1(DayPlan plan, DateTime now,
    DateTime finished)
  {
    var runs = Runs(plan, (0, At(10, 0), finished));
    var screen = new PlanScreen(plan.Sessions[0].Id, true, false, false, true, finished);
    return (PlanForecast.Forecast(plan, runs, null, null, now), screen);
  }

  [Fact]
  public void TheNextSessionIsSetUpAQuarterOfAnHourBeforeItIsDue()
  {
    var plan = Morning();
    var (forecast, screen) = AfterMoto1(plan, At(10, 14), At(10, 5));

    var waiting = PlanRunnerPolicy.Decide(forecast, screen, At(10, 14), false, null, null);
    Assert.Equal(PlanWait.NotYetDue, waiting.Waiting);
    Assert.Equal(At(10, 15), waiting.SetUpAt);

    var (forecast2, _) = AfterMoto1(plan, At(10, 15), At(10, 5));
    Assert.True(PlanRunnerPolicy.Decide(forecast2, screen, At(10, 15), false, null, null).SetUpNow);
  }

  [Fact]
  public void TheFinishedSessionStaysOnScreenAtLeastTwoMinutes()
  {
    var plan = Morning();
    var (forecast, screen) = AfterMoto1(plan, At(10, 28), At(10, 27));

    var decision = PlanRunnerPolicy.Decide(forecast, screen, At(10, 28), false, null, null);
    Assert.Equal(At(10, 29), decision.SetUpAt);
    Assert.Equal(PlanWait.NotYetDue, decision.Waiting);
  }

  [Fact]
  public void AFirstRiderStartIsSetUpOnlyFiveMinutesAhead()
  {
    var plan = Morning();
    plan.Sessions[1].Start = PlannedStartMode.FirstRider;
    var (forecast, screen) = AfterMoto1(plan, At(10, 16), At(10, 5));

    Assert.Equal(At(10, 25), PlanRunnerPolicy.Decide(forecast, screen, At(10, 16), false, null, null).SetUpAt);
  }

  [Theory]
  [InlineData(true, null, null, PlanWait.WindowOpen)]
  [InlineData(false, 1, null, PlanWait.Correcting)]
  [InlineData(false, 3, null, PlanWait.None)]
  [InlineData(false, null, 2, PlanWait.Snoozed)]
  public void NothingIsSetUpWhileTheOperatorIsBusy(bool windowOpen, int? correctedMinutesAgo, int? snoozedForMinutes,
    PlanWait expected)
  {
    var plan = Morning();
    var now = At(10, 20);
    var (forecast, screen) = AfterMoto1(plan, now, At(10, 5));

    var decision = PlanRunnerPolicy.Decide(forecast, screen, now, windowOpen,
      correctedMinutesAgo is { } ago ? now.AddMinutes(-ago) : null,
      snoozedForMinutes is { } snooze ? now.AddMinutes(snooze) : null);

    Assert.Equal(expected, decision.Waiting);
  }

  [Fact]
  public void ASessionReopenedFromPastSessionsIsNeverPutAwayByThePlan()
  {
    var plan = Morning();
    var (forecast, _) = AfterMoto1(plan, At(10, 25), At(10, 5));
    var reopened = new PlanScreen(null, FromPlan: false, Empty: false, Running: false, Finished: true, At(9, 0));

    Assert.Equal(PlanWait.NotFromPlan, PlanRunnerPolicy.Decide(forecast, reopened, At(10, 25), false, null, null).Waiting);
    // By hand it can still be done: the operator decides to leave the old session.
    Assert.Equal(PlanWait.None, PlanRunnerPolicy.CanSetUpByHand(forecast, reopened, false));
  }

  [Fact]
  public void NothingIsSetUpOverARunningSessionEvenByHand()
  {
    var plan = Morning();
    var runs = Runs(plan, (0, At(10, 0), null));
    var forecast = PlanForecast.Forecast(plan, runs, null, plan.Sessions[0].Id, At(10, 10));
    var screen = new PlanScreen(plan.Sessions[0].Id, true, false, true, false, null);

    Assert.Equal(PlanWait.SessionRunning, PlanRunnerPolicy.Decide(forecast, screen, At(10, 10), false, null, null).Waiting);
    Assert.Equal(PlanWait.SessionRunning, PlanRunnerPolicy.CanSetUpByHand(forecast, screen, false));
  }

  [Fact]
  public void RemindersComeBeforeAtAndWellAfterTheTimeItIsDue()
  {
    var plan = Morning();
    PlanReminder At_(int hour, int minute)
    {
      var forecast = PlanForecast.Forecast(plan, Runs(plan), null, null, At(hour, minute));
      return PlanRunnerPolicy.ReminderFor(PlanForecast.Next(forecast), At(hour, minute));
    }

    Assert.Equal(PlanReminder.None, At_(9, 54));
    Assert.Equal(PlanReminder.Soon, At_(9, 55));
    Assert.Equal(PlanReminder.Due, At_(10, 0));
    Assert.Equal(PlanReminder.Due, At_(10, 9));
    Assert.Equal(PlanReminder.Overdue, At_(10, 10));
  }

  [Fact]
  public void WhenEverySessionHasRunThereIsNothingLeft()
  {
    var plan = Morning();
    plan.Sessions.RemoveRange(1, 3);
    var (forecast, screen) = AfterMoto1(plan, At(10, 30), At(10, 20));
    Assert.Equal(PlanWait.NothingLeft, PlanRunnerPolicy.Decide(forecast, screen, At(10, 30), false, null, null).Waiting);
  }
}
