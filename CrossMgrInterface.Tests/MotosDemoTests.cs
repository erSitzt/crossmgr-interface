using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// The Moto 1 and Moto 2 demo promises an overall on its card: who ties, who
/// wins the tie, who retires and still scores. That comes out of the planned
/// paces, so these play each moto's reads through the finish rule and score
/// the overall the way the application does - a pace nudged too far would
/// otherwise quietly show something other than the card says.
/// </summary>
public class MotosDemoTests
{
  private static readonly DemoScenario Demo = DemoScenarios.Build(DemoScenarios.MotosId);

  /// <summary>
  /// A moto as the race would count it: the clock runs from the first crossing;
  /// the leader then rides the lap in progress plus the extra laps, and every
  /// other rider's last counted crossing is the first one after the leader is home.
  /// A rider read nowhere near the end retired, as the DNF timeout would mark them.
  /// </summary>
  private static OverallMoto Play(DemoScenario moto, string title)
  {
    var flag = moto.Crossings.Min(c => c.At) + TimeSpan.FromMinutes(moto.DurationMinutes);
    var byTag = moto.Crossings.GroupBy(c => c.Tag).ToDictionary(g => g.Key, g => g.Select(c => c.At).OrderBy(t => t).ToList());

    var leaderTag = byTag.OrderByDescending(p => p.Value.Count(t => t <= flag))
      .ThenBy(p => p.Value.Last(t => t <= flag)).First().Key;
    var leaderHome = byTag[leaderTag].Where(t => t > flag).ElementAt(moto.AdditionalLaps);

    var start = RiderBuilder.RaceStart;
    var riders = new Dictionary<string, RiderInfo>();
    foreach (var (tag, times) in byTag)
    {
      var entry = moto.Roster.Single(r => r.Tag == tag);
      var counted = times.Where(t => t <= leaderHome).ToList();
      var after = times.FirstOrDefault(t => t > leaderHome);
      var retired = after == default;
      if (!retired) counted.Add(after);

      var builder = RiderBuilder.Rider(tag, entry.Number, entry.Name).Category(entry.Class);
      var previous = TimeSpan.Zero;
      foreach (var t in counted)
      {
        builder.Lap((t - previous).TotalSeconds);
        previous = t;
      }
      if (retired) builder.Dnf();
      riders[tag] = builder.Build();
    }

    return new OverallMoto(title, riders, new RaceRules { Duration = TimeSpan.FromMinutes(moto.DurationMinutes) },
      start, start.AddMinutes(moto.DurationMinutes + 3));
  }

  private static OverallResult Overall() =>
    OverallScorer.Score("Demo: Overall", new[] { Play(Demo, "Moto 1"), Play(Demo.NextMoto!, "Moto 2") }, new OverallRules());

  private static OverallEntry Rider(string number) =>
    Overall().Classes.SelectMany(c => c.Entries).Single(e => e.Number == number);

  [Fact]
  public void ItIsTwoRaceMotosThatMakeANewOverall()
  {
    Assert.NotNull(Demo.NextMoto);
    Assert.Null(Demo.NextMoto!.NextMoto);
    Assert.Equal("Demo: Moto 1", Demo.ToSetup("r.csv").RaceName);
    Assert.True(Demo.ToSetup("r.csv").NewOverall);
    Assert.Equal("Demo: Overall", Demo.ToSetup("r.csv").OverallName);

    var second = Demo.NextMoto.ToSetup("r.csv", overallId: 5);
    Assert.Equal("Demo: Moto 2", second.RaceName);
    Assert.Equal(5, second.OverallId);
    Assert.False(second.NewOverall);
  }

  [Fact]
  public void TwoMx1RidersTieOnPointsAndTheLastMotoDecides()
  {
    var mx1 = Overall().Classes.Single(c => c.Name == "MX1").Entries;
    Assert.Equal(new[] { "12", "7" }, mx1.Take(2).Select(e => e.Number));
    Assert.Equal(new[] { 45, 45 }, mx1.Take(2).Select(e => e.Points));
    Assert.Equal(new[] { "3", "1" }, Rider("12").Motos.Select(m => m.Text));
    Assert.Equal(new[] { "1", "3" }, Rider("7").Motos.Select(m => m.Text));
  }

  [Fact]
  public void TheRiderWhoRetiresInMoto2KeepsThePointsOfSixth()
  {
    var lea = Rider("57");
    Assert.Equal("6 DNF", lea.Motos[1].Text);
    Assert.Equal(15, lea.Motos[1].Points);
  }

  [Fact]
  public void TheRiderWhoDoesNotLineUpForMoto2HasOnlyHisMoto1Points()
  {
    var ben = Rider("150");
    Assert.Equal("-", ben.Motos[1].Text);
    Assert.Equal(15, ben.Points);
  }

  [Fact]
  public void Mx2IsWonOnPointsOverBothMotos()
  {
    var mx2 = Overall().Classes.Single(c => c.Name == "MX2").Entries;
    Assert.Equal(new[] { "88", "101", "91", "124", "111", "150" }, mx2.Select(e => e.Number));
    Assert.Equal(new[] { 47, 45, 40, 36, 34, 15 }, mx2.Select(e => e.Points));
  }
}
