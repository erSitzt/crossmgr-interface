using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// What the settings block on a sheet says, in the words a protest would be
/// argued in.
/// </summary>
public class RaceRulesTests
{
  private static RaceRules Race(int? extraLaps = 2, int? dnf = 2, double? minLap = 10, bool? manual = false) => new()
  {
    SessionType = SessionType.Race,
    Duration = TimeSpan.FromMinutes(20),
    AdditionalLaps = extraLaps,
    DnfTimeoutMinutes = dnf,
    MinimumLapSeconds = minLap,
    ManualStart = manual
  };

  private static string ValueOf(RaceRules rules, string caption) =>
    rules.Describe().Single(l => l.Caption == caption).Value;

  [Fact]
  public void ARaceSaysHowManyExtraLapsTheLeaderOwes()
  {
    // The question the sheet exists to answer.
    Assert.Equal("the leader rides the lap in progress plus 2 more laps after the clock", ValueOf(Race(), "Extra laps"));
    Assert.Equal("the leader rides the lap in progress plus 1 more lap after the clock", ValueOf(Race(extraLaps: 1), "Extra laps"));
    Assert.Equal("none - the flag comes out when the clock runs out", ValueOf(Race(extraLaps: 0), "Extra laps"));
  }

  [Fact]
  public void ARaceListsEverySettingItWasScoredUnder()
  {
    var captions = Race().Describe().Select(l => l.Caption).ToList();

    Assert.Equal(new[] { "Session", "Extra laps", "DNF timeout", "Minimum lap", "Start" }, captions);
    Assert.Equal("2 minutes after the leader finishes to complete the last lap", ValueOf(Race(), "DNF timeout"));
    Assert.Equal("10 s - a read sooner than that after the previous one was not counted", ValueOf(Race(), "Minimum lap"));
    Assert.Equal("clock started on the first crossing", ValueOf(Race(), "Start"));
    Assert.Equal("clock started by the operator", ValueOf(Race(manual: true), "Start"));
    Assert.Equal("1 minute after the leader finishes to complete the last lap", ValueOf(Race(dnf: 1), "DNF timeout"));
  }

  [Fact]
  public void ATimedSessionHasAFlagAndAGraceRatherThanExtraLaps()
  {
    // There is no extra-laps rule in qualifying, and saying "0" would read as
    // though there could have been. What decides a disputed last lap there is
    // the grace, and the grace stretches with the field's pace.
    var rules = new RaceRules
    {
      SessionType = SessionType.TimedQualifying,
      Duration = TimeSpan.FromMinutes(15),
      AdditionalLaps = 0,
      DnfTimeoutMinutes = 2,
      MinimumLapSeconds = 10,
      ManualStart = false
    };

    var captions = rules.Describe().Select(l => l.Caption).ToList();
    Assert.DoesNotContain("Extra laps", captions);
    Assert.DoesNotContain("DNF timeout", captions);
    Assert.Contains("Clock", captions);
    Assert.Equal("2 minutes, or 1.5 laps of the field's pace if that is longer", ValueOf(rules, "Grace after flag"));
    Assert.StartsWith("Timed qualifying", ValueOf(rules, "Session"));
  }

  [Fact]
  public void SwitchedOffShortReadRejectionIsSaidPlainly()
  {
    Assert.Equal("off - every read counted as a lap", ValueOf(Race(minLap: 0), "Minimum lap"));
  }

  [Fact]
  public void UnrecordedSettingsSaySoRatherThanInventingAValue()
  {
    // Sessions stored before the rules were written down.
    var old = Race(extraLaps: null, dnf: null, minLap: null, manual: null);

    foreach (var caption in new[] { "Extra laps", "DNF timeout", "Minimum lap", "Start" })
      Assert.Equal("not recorded", ValueOf(old, caption));
  }

  [Fact]
  public void FromRaceCopiesEveryField()
  {
    var race = new DbRace
    {
      SessionType = SessionType.FreePractice,
      Duration = TimeSpan.FromMinutes(10),
      AdditionalLaps = 0,
      DnfTimeoutMinutes = 3,
      MinimumLapSeconds = 12.5,
      ManualStart = true
    };

    var rules = RaceRules.FromRace(race);

    Assert.Equal(SessionType.FreePractice, rules.SessionType);
    Assert.Equal(TimeSpan.FromMinutes(10), rules.Duration);
    Assert.Equal((0, 3, 12.5, true), (rules.AdditionalLaps, rules.DnfTimeoutMinutes, rules.MinimumLapSeconds, rules.ManualStart));
    Assert.True(rules.IsTimedSession);
  }
}
