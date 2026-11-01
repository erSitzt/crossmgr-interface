using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// The list of every rider of every class of a race day: who is on it, where,
/// and on which lap times.
/// </summary>
public class DayRankingScorerTests
{
  private static OverallMoto Race(string title, params RiderInfo[] riders) =>
    new(title, riders.ToDictionary(r => r.TagID, r => r), new RaceRules { Duration = TimeSpan.FromMinutes(15) },
      RiderBuilder.RaceStart, RiderBuilder.RaceStart.AddMinutes(20));

  private static OverallMoto TeamRace(string title, params RiderInfo[] riders) =>
    new(title, riders.ToDictionary(r => r.TagID, r => r),
      new RaceRules { Duration = TimeSpan.FromMinutes(15), TeamEvent = true },
      RiderBuilder.RaceStart, RiderBuilder.RaceStart.AddMinutes(20));

  private static DayRankingResult Score(params OverallMoto[] races) =>
    DayRankingScorer.Score("Master of all classes", races, DayRankingMeasure.BestLap);

  /// <summary>A rider whose first lap is <paramref name="first"/> seconds and the rest as given.</summary>
  private static RiderBuilder Rider(string tag, string number, string name, string category, double first,
    params double[] laps)
  {
    var builder = RiderBuilder.Rider(tag, number, name).Category(category).Lap(first);
    foreach (var lap in laps) builder.Lap(lap);
    return builder;
  }

  private static DayRankingEntry Find(DayRankingResult result, string name) =>
    result.Entries.Single(e => e.Name == name);

  [Fact]
  public void EveryClassIsRankedTogetherOnLapTimes()
  {
    // Lizenz rode a longer race and more laps; Hobby's fastest is quicker
    // than Lizenz's slowest all the same.
    var result = Score(
      Race("Moto 1 - Hobby",
        Rider("H1", "5", "Hans Hobby", "Hobby", 130, 115, 116, 117).Build(),
        Rider("H2", "6", "Hugo Langsam", "Hobby", 150, 140, 141, 142).Build()),
      Race("Moto 1 - Lizenz",
        Rider("L1", "1", "Lena Lizenz", "Lizenz", 125, 110, 111, 112, 113, 114).Build(),
        Rider("L2", "2", "Lars Hinten", "Lizenz", 140, 120, 121, 122, 123, 124).Build()));

    Assert.Equal(new[] { "Lena Lizenz", "Hans Hobby", "Lars Hinten", "Hugo Langsam" },
      result.Entries.Select(e => e.Name));
    Assert.Equal(new int?[] { 1, 2, 3, 4 }, result.Entries.Select(e => e.BestRank));
    Assert.Equal(new[] { "Moto 1 - Hobby", "Moto 1 - Lizenz" }, result.SessionTitles);
  }

  [Fact]
  public void LapOneIsLeftOutAsOnTheSheets()
  {
    // A first lap of 5 s is the run from the start line, not a lap.
    var result = Score(Race("Moto 1", Rider("A", "1", "Anna Berger", "MX1", 5, 120, 121, 122).Build()));

    var anna = Assert.Single(result.Entries);
    Assert.Equal(TimeSpan.FromSeconds(120), anna.BestLap);
    Assert.Equal(TimeSpan.FromSeconds(121), anna.AverageLap);
    Assert.Equal(3, anna.TimedLaps);
    Assert.Equal((0, 2), (anna.BestLapSession, anna.BestLapNumber));
  }

  [Fact]
  public void BothMotosArePooledAndTheBestComesFromEither()
  {
    var result = Score(
      Race("Moto 1", Rider("A", "1", "Anna Berger", "MX1", 130, 120, 124).Build()),
      Race("Moto 2", Rider("A", "1", "Anna Berger", "MX1", 130, 118, 122).Build()));

    var anna = Assert.Single(result.Entries);
    Assert.Equal(TimeSpan.FromSeconds(118), anna.BestLap);
    Assert.Equal(1, anna.BestLapSession);
    // The mean of all four laps, not of the two races' averages.
    Assert.Equal(TimeSpan.FromSeconds(121), anna.AverageLap);
    Assert.Equal(4, anna.TimedLaps);
  }

  [Fact]
  public void TheAverageRanksOnlyRidersWithEnoughLaps()
  {
    var result = Score(Race("Moto 1",
      Rider("A", "1", "Anna Berger", "MX1", 130, 120, 121, 122).Build(),
      // One quick lap, then out: first on best lap, no place on average.
      Rider("B", "2", "Ben Fischer", "MX1", 130, 110).Dnf().Build()));

    var ben = Find(result, "Ben Fischer");
    Assert.Equal((1, (int?)null), (ben.BestRank!.Value, ben.AverageRank));
    Assert.Equal(1, Find(result, "Anna Berger").AverageRank);
    Assert.Equal(new[] { "Anna Berger", "Ben Fischer" },
      result.Ordered(DayRankingMeasure.AverageLap).Select(e => e.Name));
  }

  [Fact]
  public void TheTwoMeasuresCanDisagree()
  {
    var result = Score(Race("Moto 1",
      Rider("A", "1", "Anna Berger", "MX1", 130, 120, 130, 130).Build(),
      Rider("B", "2", "Ben Fischer", "MX1", 130, 121, 122, 123).Build()));

    Assert.Equal(new[] { "Anna Berger", "Ben Fischer" }, result.Ordered(DayRankingMeasure.BestLap).Select(e => e.Name));
    Assert.Equal(new[] { "Ben Fischer", "Anna Berger" }, result.Ordered(DayRankingMeasure.AverageLap).Select(e => e.Name));
  }

  [Fact]
  public void ASpareTransponderInTheSecondRaceIsTheSameRider()
  {
    var result = Score(
      Race("Moto 1", Rider("A", "1", "Anna Berger", "MX1", 130, 120, 121).Build()),
      Race("Moto 2", Rider("SPARE", "1", "Anna Berger", "MX1", 130, 119, 122).Build()));

    var anna = Assert.Single(result.Entries);
    Assert.Equal(4, anna.TimedLaps);
    Assert.Null(anna.Warning);
  }

  [Fact]
  public void ARiderWhoChangedClassIsStillOneRider()
  {
    var result = Score(
      Race("Moto 1 - Hobby", Rider("A", "1", "Anna Berger", "Hobby", 130, 120, 121).Build()),
      Race("Moto 1 - Lizenz", Rider("A", "1", "Anna Berger", "Lizenz", 130, 118, 122).Build()));

    var anna = Assert.Single(result.Entries);
    Assert.Equal("Lizenz", anna.Category);
    Assert.Equal(new[] { "Hobby", "Lizenz" }, anna.Classes);
  }

  [Fact]
  public void ATransponderLentToAnotherClassIsTwoRiders()
  {
    var result = Score(
      Race("Moto 1 - Kids", Rider("CLUB1", "7", "Mia Hoff", "Kids", 130, 150, 151).Build()),
      Race("Moto 1 - Lizenz", Rider("CLUB1", "77", "Jan Hoff", "Lizenz", 130, 118, 119).Build()));

    Assert.Equal(2, result.Entries.Count);
    Assert.All(result.Entries, e => Assert.Contains("One transponder was ridden by", e.Warning));
  }

  [Fact]
  public void TwoRidersOfOneClassSharingANumberAreTwoRiders()
  {
    var result = Score(Race("Moto 1",
      Rider("A", "23", "Ida Brandt", "Beginners", 130, 120, 121).Build(),
      Rider("B", "23", "Ole Sander", "Beginners", 130, 125, 126).Build()));

    Assert.Equal(2, result.Entries.Count);
    Assert.All(result.Entries, e => Assert.Contains("Number 23 was given to", e.Warning));
  }

  [Fact]
  public void TheSameNumberInTwoClassesIsNothingToWarnAbout()
  {
    var result = Score(Race("Moto 1",
      Rider("A", "7", "Anna Berger", "MX1", 130, 120, 121).Build(),
      Rider("B", "7", "Ben Fischer", "MX2", 130, 125, 126).Build()));

    Assert.All(result.Entries, e => Assert.Null(e.Warning));
  }

  [Fact]
  public void ARiderWithoutATimedLapIsListedLastWithoutAPlace()
  {
    var result = Score(Race("Moto 1",
      Rider("A", "1", "Anna Berger", "MX1", 130, 120).Build(),
      // Read once, at the end of lap 1: a lap, but no timed one.
      Rider("B", "2", "Ben Fischer", "MX1", 130).Dnf().Build(),
      RiderBuilder.Rider("C", "3", "Carla Hoff").Category("MX1").Dns().Build()));

    Assert.Equal(new[] { "Anna Berger", "Ben Fischer", "Carla Hoff" }, result.Entries.Select(e => e.Name));
    Assert.Null(Find(result, "Ben Fischer").BestRank);
    Assert.True(Find(result, "Carla Hoff").Dns);
    Assert.False(Find(result, "Ben Fischer").Dns);
  }

  [Fact]
  public void ADnsInOneRaceAndARideInTheOtherIsNoDns()
  {
    var result = Score(
      Race("Moto 1", RiderBuilder.Rider("A", "1", "Anna Berger").Category("MX1").Dns().Build()),
      Race("Moto 2", Rider("A", "1", "Anna Berger", "MX1", 130, 120, 121).Build()));

    var anna = Assert.Single(result.Entries);
    Assert.False(anna.Dns);
    Assert.Equal(1, anna.BestRank);
  }

  [Fact]
  public void EqualBestLapsGoToWhoeverSetThemFirst()
  {
    var result = Score(
      Race("Moto 1", Rider("B", "2", "Ben Fischer", "MX1", 130, 125, 120).Build()),
      Race("Moto 2", Rider("A", "1", "Anna Berger", "MX2", 130, 120, 125).Build()));

    Assert.Equal(new[] { "Ben Fischer", "Anna Berger" }, result.Entries.Select(e => e.Name));
  }

  [Fact]
  public void ABestLapFarQuickerThanAnyOtherIsNotTakenAndFlagged()
  {
    // As one beginner on 31 October 2026: 2:10 once, otherwise never under 2:36.
    var result = Score(Race("Moto 1",
      Rider("A", "41", "Paul Wendt", "Beginners", 165, 157, 130, 158, 158, 157).Build()));

    var paul = Assert.Single(result.Entries);
    Assert.Equal(TimeSpan.FromSeconds(157), paul.BestLap);
    Assert.Equal(1, paul.QuickLapsLeftOut);
    Assert.Contains("Not taken as best lap", paul.Warning);
    // The lap still happened: the average keeps it.
    Assert.Equal(TimeSpan.FromSeconds(152), paul.AverageLap);
  }

  [Fact]
  public void SeveralMisreadLapsAreAllLeftOut()
  {
    // As a Hobby rider on 31 October 2026: crossings read at irregular moments,
    // laps adding up to a steady 2:15 but two of them "1:53". Each misread lap
    // has a near twin, so comparing with the next best lap would not see them.
    var result = Score(Race("Moto 1",
      Rider("A", "52", "Kai Lorenz", "Hobby", 135, 117.5, 162.9, 113.7, 139.1, 159.1, 135.5, 136.2, 114.0).Build()));

    var kai = Assert.Single(result.Entries);
    Assert.Equal(3, kai.QuickLapsLeftOut);
    Assert.Equal(TimeSpan.FromSeconds(135.5), kai.BestLap);
    Assert.Equal(7, kai.BestLapNumber);
  }

  [Fact]
  public void AFastRiderWithSteadyLapsKeepsTheirBest()
  {
    var result = Score(Race("Moto 1",
      Rider("A", "1", "Lena Lizenz", "Lizenz", 125, 112, 115, 114, 116, 118, 117).Build()));

    var lena = Assert.Single(result.Entries);
    Assert.Equal(TimeSpan.FromSeconds(112), lena.BestLap);
    Assert.Equal(0, lena.QuickLapsLeftOut);
    Assert.Null(lena.Warning);
  }

  [Fact]
  public void WithTooFewLapsToKnowWhatIsUsualEveryLapCounts()
  {
    var result = Score(Race("Moto 1", Rider("A", "1", "Anna Berger", "MX1", 130, 100, 140).Build()));
    Assert.Equal(TimeSpan.FromSeconds(100), Assert.Single(result.Entries).BestLap);
  }

  [Fact]
  public void ATeamRaceIsLeftOutAndSaidSo()
  {
    var result = Score(
      Race("Moto 1", Rider("A", "1", "Anna Berger", "MX1", 130, 120, 121).Build()),
      TeamRace("Team race", Rider("T", "9", "Team Rot", "", 130, 100, 100).Build()));

    Assert.Single(result.Entries);
    Assert.Equal(new[] { "Moto 1" }, result.SessionTitles);
    Assert.Equal(new[] { "Team race - a team race" }, result.LeftOut);
  }
}
