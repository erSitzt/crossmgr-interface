using System.Net;
using System.Text.Json;
using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// The day's programme as the websites are told it, and the spectator
/// screen's "next on track".
/// </summary>
public class DayProgrammeTests
{
  private static readonly DateTime Day = new(2026, 10, 18);
  private static DateTime At(int hour, int minute) => Day + new TimeSpan(hour, minute, 0);

  private static DayPlan Plan() => new()
  {
    Name = "Clubrennen",
    Date = Day,
    TrackId = "gsc",
    Sessions =
    {
      new PlannedSession { Name = "Moto 1 - MX1/MX2", Time = new TimeSpan(10, 0, 0), Classes = { "MX1", "MX2" }, OverallGroup = "MX1/MX2" },
      new PlannedSession { Name = "Qualifying - Youth", Time = new TimeSpan(10, 30, 0), SessionType = SessionType.TimedQualifying, Classes = { "Youth" } },
      new PlannedSession { Name = "Moto 2 - MX1/MX2", Time = new TimeSpan(11, 0, 0), Classes = { "MX1", "MX2" }, OverallGroup = "MX1/MX2" }
    }
  };

  private static PublishedPlan Build(DayPlan plan, DateTime now, DateTime? moto1Start = null, bool moto1Finished = false)
  {
    var runs = new Dictionary<string, PlanRun>();
    if (moto1Start is { } start)
      runs[plan.Sessions[0].Id] = new PlanRun(5, start, moto1Finished ? start.AddMinutes(20) : null, moto1Finished);

    var forecast = PlanForecast.Forecast(plan, runs, null, moto1Finished ? null : plan.Sessions[0].Id, now);
    return PublishPayloadBuilder.BuildPlan(plan, forecast, id => id == 5 ? "race5publicid000000000000000000a" : null,
      now, "v1.0.0");
  }

  [Fact]
  public void TheProgrammeSaysWhatIsOnWhatIsNextAndHowLate()
  {
    var plan = Plan();
    var payload = Build(plan, At(10, 15), moto1Start: At(10, 8));

    Assert.Equal("2026-10-18", payload.Plan.Date);
    Assert.Equal("Clubrennen", payload.Plan.Title);
    Assert.Equal(8, payload.Plan.RunningLateMinutes);
    Assert.Equal(new[] { "running", "planned", "planned" }, payload.Items.Select(i => i.State));
    Assert.Equal(new[] { 1, 2, 3 }, payload.Items.Select(i => i.Order));
    Assert.Equal("race5publicid000000000000000000a", payload.Items[0].SessionPublicId);
    Assert.Equal("timedQualifying", payload.Items[1].Type);
    Assert.Null(payload.Items[1].OverallGroup);
    Assert.Equal(At(10, 38), payload.Items[1].ExpectedStart.DateTime);
    Assert.Equal(At(10, 30), payload.Items[1].PlannedStart.DateTime);
  }

  [Fact]
  public void TheProgrammeCarriesNothingAboutRiders()
  {
    var json = PublishPayloadBuilder.Serialise(Build(Plan(), At(9, 0)));
    using var doc = JsonDocument.Parse(json);

    Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
    Assert.Equal(new[] { "schemaVersion", "plan", "items", "client" },
      doc.RootElement.EnumerateObject().Select(p => p.Name));
    Assert.DoesNotContain("rider", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("tag", json, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void TheProgrammeIsSentAgainOnlyWhenSomethingChanged()
  {
    var plan = Plan();
    var a = PublishPayloadBuilder.Fingerprint(Build(plan, At(9, 0)));
    var b = PublishPayloadBuilder.Fingerprint(Build(plan, At(9, 10)));
    Assert.Equal(a, b);

    var started = PublishPayloadBuilder.Fingerprint(Build(plan, At(10, 1), moto1Start: At(10, 1)));
    Assert.NotEqual(a, started);
  }

  [Fact]
  public async Task AnOlderWebsiteWithoutProgrammesIsNotAnError()
  {
    var handler = new NotFoundHandler();
    var publisher = new HttpResultsPublisher("https://results.example.org", "olt_abcd1234_secret", null, handler);

    var outcome = await publisher.PublishPlanAsync(Build(Plan(), At(9, 0)), CancellationToken.None);

    Assert.Equal(PublishStatus.NotSupported, outcome.Status);
    Assert.Equal(1, handler.Calls);
    Assert.Contains("/api/v1/plans/", handler.LastPath);
  }

  private sealed class NotFoundHandler : HttpMessageHandler
  {
    public int Calls { get; private set; }
    public string LastPath { get; private set; } = "";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      Calls++;
      LastPath = request.RequestUri!.AbsolutePath;
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
  }

  // ---- the spectator screen -------------------------------------------------

  private static SpectatorBoard Board(RaceDayState state) =>
    SpectatorBoardBuilder.Build(new SpectatorInputs
    {
      Field = new[] { RiderBuilder.Rider("A", "7", "Anna Berg").Laps(3, 48).Build() },
      Title = "Moto 1",
      State = state,
      Duration = TimeSpan.FromMinutes(20),
      NextUp = "Moto 1 - Youth   ~14:06"
    });

  [Theory]
  [InlineData(RaceDayState.ReadyToStart, true)]
  [InlineData(RaceDayState.Running, false)]
  [InlineData(RaceDayState.LastLaps, false)]
  [InlineData(RaceDayState.Finished, true)]
  public void TheSpectatorScreenSaysWhatIsNextOnlyWhileNothingIsBeingTimed(RaceDayState state, bool shown)
  {
    Assert.Equal(shown, Board(state).NextUp != null);
  }
}
