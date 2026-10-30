using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// Presets: making a day plan from one, matching its classes to a club's
/// rider list, and keeping the club's own in a list.
/// </summary>
public sealed class PresetTests : IDisposable
{
  private static readonly string[] ClubClasses = { "MX1", "MX2", "Jugend 85", "Senioren Ü40", "Damen" };
  private readonly string _file = Path.Combine(Path.GetTempPath(), $"crossmgr-presets-{Guid.NewGuid():N}.json");

  public void Dispose()
  {
    foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_file) + "*"))
      try { File.Delete(f); } catch (IOException) { }
  }

  private static DayPlan Preset(string name) => PresetCatalog.All.Single(p => p.Name == name).Plan;

  private static Dictionary<string, IReadOnlyList<string>> Map(params (string From, string[] To)[] map) =>
    map.ToDictionary(m => m.From, m => (IReadOnlyList<string>)m.To);

  // ---- the built-ins ------------------------------------------------------------

  [Fact]
  public void TheFourPresetsAreThere()
  {
    Assert.Equal(new[] { "MX club day", "Training day", "Enduro", "Team race" }, PresetCatalog.All.Select(p => p.Name));
    Assert.All(PresetCatalog.All, p => Assert.True(p.BuiltIn && p.Description.Length > 0 && p.Plan.Sessions.Count > 0));
  }

  [Fact]
  public void TheEnduroIsTheClubsDayOfTheTwentiethOfSeptember()
  {
    var enduro = Preset("Enduro");
    Assert.All(enduro.Sessions, s => Assert.Equal((120, 0, SessionType.Race), (s.DurationMinutes, s.AdditionalLaps, s.SessionType)));
    Assert.Equal(PlannedStartMode.Gate, enduro.Sessions[0].Start);
    Assert.Equal((PlannedStartMode.Waves, 60), (enduro.Sessions[1].Start, enduro.Sessions[1].WaveGapSeconds));
  }

  [Fact]
  public void EveryPresetMatchedToAClubsListHasNothingToFix()
  {
    var map = Map(("Group A", new[] { "MX1", "MX2" }), ("Group B", new[] { "Jugend 85", "Senioren Ü40", "Damen" }));
    foreach (var preset in PresetCatalog.All)
    {
      var plan = ClassMapping.Apply(preset.Plan, map, ClubClasses).Plan;
      Assert.Empty(plan.Problems(ClubClasses));
    }
  }

  // ---- making a plan -------------------------------------------------------------

  [Fact]
  public void APlanFromAPresetMovesEveryTimeWithTheFirst()
  {
    var day = new DateTime(2026, 11, 7);
    var plan = PresetCatalog.NewPlan(Preset("MX club day"), day, "Saisonabschluss", new TimeSpan(9, 0, 0), @"C:\riders.xlsx")!;

    Assert.Equal(("Saisonabschluss", day, @"C:\riders.xlsx"), (plan.Name, plan.Date, plan.RiderListPath));
    Assert.Equal(new TimeSpan(9, 0, 0), plan.Sessions[0].Time);
    Assert.Equal(new TimeSpan(14, 0, 0), plan.Sessions[^1].Time);
    Assert.NotEqual(Preset("MX club day").Sessions[0].Id, plan.Sessions[0].Id);
    Assert.True(plan.After.Publish);
  }

  [Fact]
  public void APlanThatWouldRunPastMidnightIsRefused()
  {
    Assert.Null(PresetCatalog.NewPlan(Preset("MX club day"), DateTime.Today, "x", new TimeSpan(20, 0, 0), null));
    Assert.Empty(PresetCatalog.NewPlan(null, DateTime.Today, "", new TimeSpan(20, 0, 0), null)!.Sessions);
  }

  // ---- matching classes -----------------------------------------------------------

  [Fact]
  public void AGroupBecomesItsClassesAndTheNamesFollow()
  {
    var result = ClassMapping.Apply(Preset("MX club day"), Map(("Group A", new[] { "MX1", "MX2" }),
      ("Group B", new[] { "Jugend 85" })), ClubClasses);
    var moto1 = result.Plan.Sessions.First(s => s.Name.StartsWith("Moto 1"));

    Assert.Equal(new[] { "MX1", "MX2" }, moto1.Classes);
    Assert.Equal("Moto 1 - MX1 / MX2", moto1.Name);
    Assert.Equal("MX1 / MX2", moto1.OverallGroup);
    Assert.Contains(result.Plan.Sessions, s => s.Name == "Practice - Jugend 85");
    Assert.Contains("Senioren Ü40, Damen", Assert.Single(result.Warnings));
  }

  [Fact]
  public void TheWaveOrderIsTheOrderChosen()
  {
    var result = ClassMapping.Apply(Preset("Enduro"), Map(("Group A", new[] { "MX1", "MX2" }),
      ("Group B", new[] { "Damen", "Jugend 85", "Senioren Ü40" })), ClubClasses);
    Assert.Equal(new[] { "Damen", "Jugend 85", "Senioren Ü40" }, result.Plan.Sessions[1].Classes);
    Assert.Equal("Race 2 - Damen / Jugend 85 / Senioren Ü40", result.Plan.Sessions[1].Name);
    Assert.Empty(result.Warnings);
  }

  [Fact]
  public void ATypedNameIsLeftAsTyped()
  {
    var plan = new DayPlan();
    plan.Sessions.Add(new PlannedSession { Name = "Das große Finale", Classes = { "Jugend" }, OverallGroup = "Jugend" });
    var result = ClassMapping.Apply(plan, Map(("Jugend", new[] { "Jugend 85" })), ClubClasses);
    Assert.Equal("Das große Finale", result.Plan.Sessions[0].Name);
    Assert.Equal("Jugend 85", result.Plan.Sessions[0].OverallGroup);
  }

  [Fact]
  public void ClassesSpeltDifferentlyMatchByThemselvesInTheListsSpelling()
  {
    var plan = new DayPlan();
    plan.Sessions.Add(new PlannedSession { Name = "Lauf 1 - mx1 / damen", Classes = { "mx1", " damen " } });
    Assert.Empty(ClassMapping.Unmatched(plan, ClubClasses));

    var result = ClassMapping.Apply(plan, Map(), ClubClasses);
    Assert.Equal(new[] { "MX1", "Damen" }, result.Plan.Sessions[0].Classes);

    var nfd = new DayPlan();
    nfd.Sessions.Add(new PlannedSession { Classes = { "Senioren U\u0308\u0034\u0030" } });
    Assert.Empty(ClassMapping.Unmatched(nfd, ClubClasses));
  }

  [Fact]
  public void TwoOverallsBecomingOneIsWarnedAbout()
  {
    var result = ClassMapping.Apply(Preset("MX club day"),
      Map(("Group A", new[] { "MX1" }), ("Group B", new[] { "MX1" })), ClubClasses);
    Assert.Contains(result.Warnings, w => w.Contains("Group A and Group B would both become MX1"));
  }

  [Fact]
  public void ASessionLeftWithNoClassIsTakenOutNotLeftCountingEveryone()
  {
    var result = ClassMapping.Apply(Preset("Training day"), Map(("Group A", new[] { "MX1" }), ("Group B", Array.Empty<string>())),
      ClubClasses);
    Assert.Equal(3, result.Plan.Sessions.Count);
    Assert.Equal(3, result.Dropped.Count);
    Assert.All(result.Plan.Sessions, s => Assert.Equal(new[] { "MX1" }, s.Classes));
  }

  [Fact]
  public void AWaveStartLeftWithOneClassIsWarnedAbout()
  {
    var result = ClassMapping.Apply(Preset("Enduro"), Map(("Group A", new[] { "MX1" }), ("Group B", new[] { "MX2" })), ClubClasses);
    Assert.Contains(result.Warnings, w => w.Contains("waves"));
  }

  [Fact]
  public void TeamsAreLeftAlone()
  {
    var result = ClassMapping.Apply(Preset("Team race"), Map(("Group A", new[] { "MX1" })), ClubClasses);
    Assert.True(result.Plan.Sessions[1].TeamEvent);
    Assert.Empty(result.Plan.Sessions[1].Classes);
  }

  // ---- the club's list --------------------------------------------------------------

  [Fact]
  public void TheListKeepsCodesAndKnowsADuplicateHoweverItWasTyped()
  {
    var store = PresetStore.Load(_file);
    var code = PresetCatalog.All[2].Code;

    var (first, added) = store.Add("Enduro Herbst", "Wie im September", code);
    Assert.True(added);
    var (again, addedAgain) = store.Add("Something else", "", code.ToLowerInvariant().Replace("-", " "));
    Assert.False(addedAgain);
    Assert.Same(first, again);
    store.Save();

    var read = PresetStore.Load(_file);
    var preset = Assert.Single(read.Presets);
    Assert.Equal(("Enduro Herbst", "Wie im September", code), (preset.Name, preset.Description, preset.Code));
    Assert.Same(preset, read.FindShort(DayPlanCode.ShortCodeOf(code)));
  }

  [Fact]
  public void ACodeFromANewerVersionIsKeptNotLost()
  {
    var payload = DayPlanCode.PayloadOf(PresetCatalog.All[3].Code);
    payload[0] = 2;
    var newer = DayPlanCode.Wrap(payload);

    var store = PresetStore.Load(_file);
    store.Add("Next year's day", "", newer);
    store.Save();
    Assert.Equal(newer, Assert.Single(PresetStore.Load(_file).Presets).Code);
  }

  [Fact]
  public void AnUnreadableListIsSetAside()
  {
    File.WriteAllText(_file, "{ broken");
    var store = PresetStore.Load(_file);
    Assert.Empty(store.Presets);
    Assert.NotNull(store.RecoveredFrom);
    Assert.True(File.Exists(store.RecoveredFrom));
  }

  [Fact]
  public void TheExportedListReadsBackIn()
  {
    var list = new[]
    {
      ("Clubrennen Sommer", "Mit Jugend", PresetCatalog.All[0].Code, (string?)"K7Q2-MX4P"),
      ("Enduro", "", PresetCatalog.All[2].Code, null)
    };

    var text = PresetStore.ExportText(list);
    var found = DayPlanCode.FindAll(text);
    Assert.Equal(2, found.Count);
    Assert.All(found, f => Assert.NotNull(f.Plan));

    var csv = PresetStore.ExportCsv(list);
    Assert.StartsWith("Name,Description,Short code,Code", csv);
    Assert.Equal(2, DayPlanCode.FindAll(csv).Count(f => f.Plan != null));
  }
}
