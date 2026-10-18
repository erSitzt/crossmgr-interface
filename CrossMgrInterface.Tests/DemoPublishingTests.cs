using System.Text.Json;
using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// A demo publishes like a real race day, marked DEMO - and a real race still
/// sends exactly what it always sent.
/// </summary>
public sealed class DemoPublishingTests : IDisposable
{
  private readonly string _real = Path.Combine(Path.GetTempPath(), $"crossmgr-real-{Guid.NewGuid():N}");
  private readonly string _sandbox = Path.Combine(Path.GetTempPath(), $"crossmgr-sandbox-{Guid.NewGuid():N}");

  public void Dispose()
  {
    foreach (var folder in new[] { _real, _sandbox })
      try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
  }

  private static RaceReportData Report() =>
    new RaceReportGenerator().PrepareReportData(
      new[] { RiderBuilder.Rider("A", "1", "Anna Berger").Laps(3, 40).Build() }.ToDictionary(r => r.TagID),
      RiderBuilder.RaceStart, RiderBuilder.RaceStart.AddMinutes(20), TimeSpan.FromMinutes(20), true, "Moto 1",
      rules: new RaceRules());

  private static JsonElement Header(bool demo)
  {
    var json = PublishPayloadBuilder.Serialise(PublishPayloadBuilder.Build(new PublishInputs
    {
      Report = Report(),
      PublicId = "b3f1c0de0000000000000000000000ff",
      Demo = demo
    }));
    return JsonDocument.Parse(json).RootElement.GetProperty("session").Clone();
  }

  [Fact]
  public void ADemoSessionSaysSoAndARealOneSendsNothingNew()
  {
    Assert.True(Header(demo: true).GetProperty("demo").GetBoolean());
    Assert.False(Header(demo: false).TryGetProperty("demo", out _));
  }

  [Fact]
  public void ADemoOverallAndADemoProgrammeSaySoToo()
  {
    var result = OverallScorer.Score("Overall", new[]
    {
      new OverallMoto("Moto 1", new[] { RiderBuilder.Rider("A", "1", "Anna Berger").Laps(3, 40).Build() }
        .ToDictionary(r => r.TagID), new RaceRules())
    }, new OverallRules());

    string Overall(bool demo) => PublishPayloadBuilder.Serialise(
      PublishPayloadBuilder.BuildOverall(result, "0123456789abcdef0123456789abcdef", new[] { "m1" }, demo: demo));
    Assert.Contains("\"demo\":true", Overall(true));
    Assert.DoesNotContain("demo", Overall(false));

    var plan = new DayPlan { Name = "Demo race day", Sessions = { new PlannedSession { Name = "Practice" } } };
    var forecast = PlanForecast.Forecast(plan, new Dictionary<string, PlanRun>(), null, null, DateTime.Now);
    string Programme(bool demo) => PublishPayloadBuilder.Serialise(
      PublishPayloadBuilder.BuildPlan(plan, forecast, _ => null, DateTime.Now, demo: demo));
    Assert.Contains("\"demo\":true", Programme(true));
    Assert.DoesNotContain("demo", Programme(false));
  }

  [Fact]
  public void BothWebsitesAndTheNameSettingsTravelIntoADemoAndNothingElse()
  {
    Directory.CreateDirectory(_real);
    Directory.CreateDirectory(_sandbox);
    var real = new AppSettings
    {
      ResultsSiteUrl = "https://results.example.org",
      LiveSiteUrl = "https://live.example.org",
      PublishNamesByDefault = false,
      HiddenNameStyle = NameStyle.Starred,
      LastRiderListPath = @"C:\Club\riders.xlsx",
      ReaderPort = 4000
    };
    File.WriteAllText(Path.Combine(_real, "settings.json"), JsonSerializer.Serialize(real));
    File.WriteAllText(Path.Combine(_real, "results-key.dat"), "key");

    DemoLaunch.SeedWebsites(_real, _sandbox);

    var seeded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path.Combine(_sandbox, "settings.json")))!;
    Assert.Equal("https://results.example.org", seeded.ResultsSiteUrl);
    Assert.Equal("https://live.example.org", seeded.LiveSiteUrl);
    Assert.False(seeded.PublishNamesByDefault);
    Assert.Equal(NameStyle.Starred, seeded.HiddenNameStyle);
    Assert.Null(seeded.LastRiderListPath);
    Assert.Equal(new AppSettings().ReaderPort, seeded.ReaderPort);
    Assert.True(File.Exists(Path.Combine(_sandbox, "results-key.dat")));
  }

  [Fact]
  public void WithNoWebsitesSetUpTheDemoStartsEmpty()
  {
    Directory.CreateDirectory(_real);
    Directory.CreateDirectory(_sandbox);
    File.WriteAllText(Path.Combine(_real, "settings.json"), JsonSerializer.Serialize(new AppSettings { ReaderPort = 4000 }));

    DemoLaunch.SeedWebsites(_real, _sandbox);

    Assert.False(File.Exists(Path.Combine(_sandbox, "settings.json")));
  }
}
