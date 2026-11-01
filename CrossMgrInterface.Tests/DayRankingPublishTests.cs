using System.Text.Json;
using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>The list of every class of the day as the website is sent it.</summary>
public class DayRankingPublishTests
{
  private static OverallMoto Race(string title, params RiderInfo[] riders) =>
    new(title, riders.ToDictionary(r => r.TagID, r => r), new RaceRules { Duration = TimeSpan.FromMinutes(15) },
      RiderBuilder.RaceStart, RiderBuilder.RaceStart.AddMinutes(20));

  private static DayRankingResult Day()
  {
    var hidden = RiderBuilder.Rider("B", "2", "Ben Fischer").Category("MX2").Lap(130).Laps(3, 121).Build();
    hidden.ShowName = false;
    return DayRankingScorer.Score("Master of all classes", new[]
    {
      Race("Moto 1 - MX1", RiderBuilder.Rider("A", "1", "Anna Berger").Category("MX1").Lap(130).Laps(3, 120).Build(),
        RiderBuilder.Rider("C", "3", "Carla Hoff").Category("MX1").Dns().Build()),
      Race("Moto 1 - MX2", hidden)
    }, DayRankingMeasure.AverageLap, final: false);
  }

  [Fact]
  public void EveryRiderGoesWithBothRanksAndNoTransponder()
  {
    var payload = PublishPayloadBuilder.BuildDayRanking(Day(), "0123456789abcdef0123456789abcdef",
      new[] { "m1", "m2" }, clientVersion: "v0.27.0");
    var json = PublishPayloadBuilder.Serialise(payload);
    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;

    Assert.Equal(new[] { "schemaVersion", "ranking", "sessions", "entries", "client" },
      root.EnumerateObject().Select(p => p.Name));
    Assert.Equal("averageLap", root.GetProperty("ranking").GetProperty("measure").GetString());
    Assert.False(root.GetProperty("ranking").GetProperty("final").GetBoolean());
    Assert.False(root.GetProperty("ranking").TryGetProperty("demo", out _));

    var anna = root.GetProperty("entries")[0];
    Assert.Equal(1, anna.GetProperty("bestRank").GetInt32());
    Assert.Equal(120000, anna.GetProperty("bestLapMs").GetInt64());
    Assert.Equal(0, anna.GetProperty("bestLapSession").GetInt32());
    Assert.Equal(3, anna.GetProperty("timedLaps").GetInt32());
    Assert.Equal("MX1", anna.GetProperty("category").GetString());

    Assert.DoesNotContain("\"A\"", json);
    Assert.DoesNotContain("tag", json, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void ARiderWhoAskedNotToBeNamedIsNotNamed()
  {
    var payload = PublishPayloadBuilder.BuildDayRanking(Day(), "0123456789abcdef0123456789abcdef", new[] { "m1", "m2" });

    var ben = payload.Entries.Single(e => e.Number == "2");
    Assert.Equal("Ben F.", ben.Name);
  }

  [Fact]
  public void ARiderWithoutATimeSaysWhy()
  {
    var payload = PublishPayloadBuilder.BuildDayRanking(Day(), "0123456789abcdef0123456789abcdef", new[] { "m1", "m2" });

    var carla = payload.Entries.Single(e => e.Number == "3");
    Assert.Equal(("dns", (int?)null, (long?)null), (carla.Status, carla.BestRank, carla.BestLapMs));
    Assert.Equal("", payload.Entries.Single(e => e.Number == "1").Status);
  }

  [Fact]
  public void ADemoSaysSo()
  {
    var payload = PublishPayloadBuilder.BuildDayRanking(Day(), "0123456789abcdef0123456789abcdef", new[] { "m1" },
      demo: true);
    Assert.True(payload.Ranking.Demo);
  }
}

/// <summary>The Master of all classes on the spectator screen.</summary>
public class DayRankingSpectatorTests
{
  [Fact]
  public void TheBoardListsEveryClassOnTheChosenMeasure()
  {
    OverallMoto Race(string title, params RiderInfo[] riders) =>
      new(title, riders.ToDictionary(r => r.TagID, r => r), new RaceRules { Duration = TimeSpan.FromMinutes(15) },
        RiderBuilder.RaceStart, RiderBuilder.RaceStart.AddMinutes(20));

    var result = DayRankingScorer.Score("Master of all classes", new[]
    {
      Race("Moto 1 - Hobby", RiderBuilder.Rider("H", "5", "Hans Hobby").Category("Hobby").Lap(130).Laps(3, 115).Build()),
      Race("Moto 1 - Lizenz", RiderBuilder.Rider("L", "1", "Lena Lizenz").Category("Lizenz").Lap(125).Laps(3, 110).Build(),
        RiderBuilder.Rider("D", "2", "Dora Dns").Category("Lizenz").Dns().Build())
    }, DayRankingMeasure.BestLap);

    var board = SpectatorBoardBuilder.BuildAllClasses(result, rowLimit: 2, nextUp: "Moto 2 - Hobby  ~14:06");

    Assert.True(board.AllClasses);
    Assert.Equal("Best lap", board.State);
    Assert.Equal(new[] { "1", "2" }, board.Rows.Select(r => r.Position));
    Assert.Equal(new[] { "Lizenz", "Hobby" }, board.Rows.Select(r => r.Class));
    // As the qualifying board writes a gap to pole.
    Assert.Equal($"+{5.0:F2}", board.Rows[1].Gap);
    Assert.Equal(1, board.MoreCount);
    Assert.Equal("Moto 2 - Hobby  ~14:06", board.NextUp);
  }
}
