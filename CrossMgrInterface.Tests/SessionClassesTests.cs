using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// A session that counts only some of the day's classes: who is counted, who
/// is kept aside, when the operator is told, and that the sheet and a
/// recovered session still know which classes it was.
/// </summary>
public sealed class SessionClassesTests : IDisposable
{
  private static readonly string[] Mx = { "MX1", "MX2" };

  private readonly string _path = Path.Combine(Path.GetTempPath(), $"crossmgr-test-{Guid.NewGuid():N}.db");
  private readonly Lazy<RaceDataService> _db;

  public SessionClassesTests() => _db = new Lazy<RaceDataService>(() => new RaceDataService(_path));

  public void Dispose()
  {
    if (_db.IsValueCreated) _db.Value.Dispose();
    try { File.Delete(_path); } catch (IOException) { }
    try { File.Delete(Path.ChangeExtension(_path, null) + "-log.db"); } catch (IOException) { }
  }

  // ---- the gate -----------------------------------------------------------

  [Theory]
  [InlineData("MX1", ClassGateVerdict.Count)]
  [InlineData("mx2", ClassGateVerdict.Count)]
  [InlineData(" MX2 ", ClassGateVerdict.Count)]
  [InlineData("Youth", ClassGateVerdict.OtherClass)]
  [InlineData("", ClassGateVerdict.Count)]
  public void ARiderOnTheListIsCountedWhenTheirClassRidesInTheSession(string riderClass, ClassGateVerdict expected)
  {
    Assert.Equal(expected, SessionClassGate.Decide(Mx, alreadyCounted: false, onRiderList: true, riderClass, clockRunning: true));
  }

  [Fact]
  public void ASessionWithoutClassesCountsEveryone()
  {
    Assert.Equal(ClassGateVerdict.Count, SessionClassGate.Decide(null, false, true, "Youth", true));
    Assert.Equal(ClassGateVerdict.Count, SessionClassGate.Decide(Array.Empty<string>(), false, false, null, false));
  }

  [Fact]
  public void ARiderAlreadyInTheSessionIsNeverTurnedAway()
  {
    // Moved into another class with Change class halfway through: the rest of
    // their laps must still count.
    Assert.Equal(ClassGateVerdict.Count, SessionClassGate.Decide(Mx, alreadyCounted: true, true, "Youth", true));
  }

  [Fact]
  public void AnUnknownTransponderCannotStartTheClockButCountsOnceItRuns()
  {
    Assert.Equal(ClassGateVerdict.UnknownBeforeStart, SessionClassGate.Decide(Mx, false, onRiderList: false, null, clockRunning: false));
    Assert.Equal(ClassGateVerdict.Count, SessionClassGate.Decide(Mx, false, onRiderList: false, null, clockRunning: true));
  }

  // ---- reads kept aside ---------------------------------------------------

  private static readonly DateTime T0 = RiderBuilder.RaceStart;

  [Fact]
  public void ARiderRollingOverTheLoopOnceIsNotWorthAWarning()
  {
    var held = new HeldReads();
    Assert.Null(held.Add("Y1", "301", "Ada Kurz", "Youth", T0, "Y1"));
    Assert.Null(held.Add("Y1", "301", "Ada Kurz", "Youth", T0.AddSeconds(20), "Y1"));
    Assert.True(held.Any);
  }

  [Fact]
  public void ARiderGoingRoundIsReportedOnce()
  {
    var held = new HeldReads();
    held.Add("Y1", "301", "Ada Kurz", "Youth", T0, "Y1");

    var notice = held.Add("Y1", "301", "Ada Kurz", "Youth", T0.AddSeconds(70), "Y1");
    Assert.NotNull(notice);
    Assert.False(notice!.ManyRiders);

    Assert.Null(held.Add("Y1", "301", "Ada Kurz", "Youth", T0.AddSeconds(140), "Y1"));
  }

  [Fact]
  public void ManyRidersGoingRoundAskIfItIsTheRightSessionAndThenSayNoMore()
  {
    var held = new HeldReads();
    var notices = new List<HeldReadNotice>();
    for (var i = 0; i < HeldReads.ManyRiders + 2; i++)
    {
      var tag = $"Y{i}";
      held.Add(tag, $"{300 + i}", "Rider", "Youth", T0, tag);
      if (held.Add(tag, $"{300 + i}", "Rider", "Youth", T0.AddSeconds(70), tag) is { } notice) notices.Add(notice);
    }

    Assert.Equal(HeldReads.ManyRiders, notices.Count);
    Assert.True(notices[^1].ManyRiders);
    Assert.All(notices.Take(HeldReads.ManyRiders - 1), n => Assert.False(n.ManyRiders));
  }

  [Fact]
  public void CountingARiderTakesTheirReadsOldestFirst()
  {
    var held = new HeldReads();
    held.Add("Y1", "301", "Ada Kurz", "Youth", T0.AddSeconds(90), "SPARE");
    held.Add("Y1", "301", "Ada Kurz", "Youth", T0, "Y1");
    held.Add("Y2", "302", "Ben Lutz", "Youth", T0.AddSeconds(30), "Y2");

    var rider = held.Riders().Single(r => r.Tag == "Y1");
    Assert.Equal(2, rider.Reads);
    Assert.Equal("Youth", rider.Class);

    var reads = held.Take("Y1");
    Assert.Equal(new[] { T0, T0.AddSeconds(90) }, reads.Select(r => r.At));
    Assert.Equal("SPARE", reads[1].CrossedBy);
    Assert.Single(held.Riders());
    Assert.Empty(held.Take("Y1"));
  }

  // ---- the sheet and the database ----------------------------------------

  [Fact]
  public void TheSheetSaysWhichClassesTheSessionCounted()
  {
    var lines = new RaceRules { Classes = Mx }.Describe();
    Assert.Contains(lines, l => l.Caption == "Classes" && l.Value.StartsWith("MX1, MX2"));
    Assert.DoesNotContain(new RaceRules().Describe(), l => l.Caption == "Classes");
  }

  [Fact]
  public void TheClassesAreStoredWithTheRaceAndComeBackWithIt()
  {
    var db = _db.Value;
    var id = db.StartNewRace(T0, TimeSpan.FromMinutes(20), "Moto 1 - MX1/MX2", rules: new RaceRules
    {
      Duration = TimeSpan.FromMinutes(20),
      Classes = Mx
    });
    var everyone = db.StartNewRace(T0, TimeSpan.FromMinutes(20), "Practice", rules: new RaceRules
    {
      Duration = TimeSpan.FromMinutes(20)
    });

    Assert.Equal(Mx, RaceRules.FromRace(db.GetRace(id)!).Classes);
    Assert.Null(RaceRules.FromRace(db.GetRace(everyone)!).Classes);
  }
}
