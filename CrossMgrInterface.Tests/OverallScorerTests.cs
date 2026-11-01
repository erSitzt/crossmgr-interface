using Xunit;

namespace CrossMgrInterface.Tests;

public class OverallScorerTests
{
  private static OverallMoto Moto(string title, params RiderInfo[] riders) =>
    new(title, riders.ToDictionary(r => r.TagID, r => r), new RaceRules { Duration = TimeSpan.FromMinutes(20) },
      RiderBuilder.RaceStart, RiderBuilder.RaceStart.AddMinutes(20));

  private static OverallResult Score(params OverallMoto[] motos) =>
    OverallScorer.Score("Overall", motos, new OverallRules());

  private static OverallClass OnlyClass(OverallResult result) => Assert.Single(result.Classes);

  [Fact]
  public void EachMotoPlaceIsWorthItsFimPointsAndTheHighestTotalWins()
  {
    var result = Score(
      Moto("Moto 1",
        RiderBuilder.Rider("A", "1", "Anna Berger").Laps(10, 40).Build(),
        RiderBuilder.Rider("B", "2", "Ben Fischer").Laps(10, 41).Build(),
        RiderBuilder.Rider("C", "3", "Carla Hoff").Laps(10, 42).Build()),
      Moto("Moto 2",
        RiderBuilder.Rider("A", "1", "Anna Berger").Laps(10, 43).Build(),
        RiderBuilder.Rider("B", "2", "Ben Fischer").Laps(10, 40).Build(),
        RiderBuilder.Rider("C", "3", "Carla Hoff").Laps(10, 41).Build()));

    var entries = OnlyClass(result).Entries;
    // Anna 25 + 20 = 45, Ben 22 + 25 = 47, Carla 20 + 22 = 42.
    Assert.Equal(new[] { "2", "1", "3" }, entries.Select(e => e.Number));
    Assert.Equal(new[] { 47, 45, 42 }, entries.Select(e => e.Points));
    Assert.Equal(new int?[] { 1, 2, 3 }, entries.Select(e => e.Rank));
    Assert.Equal(new[] { "2", "1" }, entries[0].Motos.Select(m => m.Text));
  }

  [Fact]
  public void ATieOnPointsGoesToTheBetterResultInTheLastMoto()
  {
    var result = Score(
      Moto("Moto 1",
        RiderBuilder.Rider("A", "1", "Anna Berger").Laps(10, 40).Build(),
        RiderBuilder.Rider("B", "2", "Ben Fischer").Laps(10, 41).Build()),
      Moto("Moto 2",
        RiderBuilder.Rider("A", "1", "Anna Berger").Laps(10, 41).Build(),
        RiderBuilder.Rider("B", "2", "Ben Fischer").Laps(10, 40).Build()));

    var entries = OnlyClass(result).Entries;
    Assert.Equal(47, entries[0].Points);
    Assert.Equal(47, entries[1].Points);
    Assert.Equal("2", entries[0].Number);
  }

  [Fact]
  public void ARiderWhoRetiredKeepsThePointsOfTheirPlace()
  {
    // DMSB 8.8 and FIM 5.34: retired riders are placed by laps completed, and
    // score. There is no minimum share of the winner's laps.
    var result = Score(Moto("Moto 1",
      RiderBuilder.Rider("A", "1", "Anna Berger").Laps(8, 40).Build(),
      RiderBuilder.Rider("B", "2", "Ben Fischer").Laps(6, 40).Dnf().Build(),
      RiderBuilder.Rider("C", "3", "Carla Hoff").Laps(1, 40).Dnf().Build()));

    var entries = OnlyClass(result).Entries;
    Assert.Equal(new[] { "1", "2 DNF", "3 DNF" }, entries.Select(e => e.Motos[0].Text));
    Assert.Equal(new[] { 25, 22, 20 }, entries.Select(e => e.Points));
    Assert.Equal(new int?[] { 1, 2, 3 }, entries.Select(e => e.Rank));
  }

  [Fact]
  public void ARetiredRiderIsPlacedBehindEveryoneWhoTookTheFlag()
  {
    // As the moto's own sheet places them, even behind a lapped finisher.
    var result = Score(Moto("Moto 1",
      RiderBuilder.Rider("A", "1", "Anna Berger").Laps(8, 40).Build(),
      RiderBuilder.Rider("B", "2", "Ben Fischer").Laps(6, 40).Dnf().Build(),
      RiderBuilder.Rider("C", "3", "Carla Hoff").Laps(3, 100).Build()));

    var entries = OnlyClass(result).Entries;
    Assert.Equal(new[] { "1", "3", "2" }, entries.Select(e => e.Number));
    Assert.Equal("3 DNF", entries[2].Motos[0].Text);
  }

  [Fact]
  public void ARiderOutBeforeCompletingALapHasNoPlace()
  {
    var result = Score(Moto("Moto 1",
      RiderBuilder.Rider("A", "1", "Anna Berger").Laps(8, 40).Build(),
      RiderBuilder.Rider("B", "2", "Ben Fischer").Dnf().Build()));

    var ben = OnlyClass(result).Entries.Single(e => e.Number == "2");
    Assert.Equal("DNF", ben.Motos[0].Text);
    Assert.Equal(0, ben.Points);
    Assert.Null(ben.Rank);
  }

  [Fact]
  public void RidersWithoutPointsAreRankedOnTheSumOfTheirPlaces()
  {
    // FIM 5.36: no points, so the lowest sum of places; equal sums go to the
    // last moto; riders missing a moto after those with both.
    var rules = new OverallRules { Points = new[] { 10 } };
    var result = OverallScorer.Score("Overall", new[]
    {
      Moto("Moto 1",
        RiderBuilder.Rider("A", "1", "Anna Berger").Laps(10, 40).Build(),
        RiderBuilder.Rider("B", "2", "Ben Fischer").Laps(10, 41).Build(),
        RiderBuilder.Rider("C", "3", "Carla Hoff").Laps(10, 42).Build(),
        RiderBuilder.Rider("D", "4", "Dora Klein").Laps(10, 43).Build(),
        RiderBuilder.Rider("E", "5", "Emil Lang").Dns().Build()),
      Moto("Moto 2",
        RiderBuilder.Rider("A", "1", "Anna Berger").Laps(10, 40).Build(),
        RiderBuilder.Rider("C", "3", "Carla Hoff").Laps(10, 41).Build(),
        RiderBuilder.Rider("B", "2", "Ben Fischer").Laps(10, 42).Build(),
        RiderBuilder.Rider("E", "5", "Emil Lang").Dns().Build())
    }, rules);

    var entries = OnlyClass(result).Entries;
    // Ben 2+3 and Carla 3+2 both sum to 5: Carla was better in Moto 2.
    Assert.Equal(new[] { "1", "3", "2", "4", "5" }, entries.Select(e => e.Number));
    Assert.Equal(new int?[] { 1, 2, 3, 4, null }, entries.Select(e => e.Rank));
  }

  [Fact]
  public void ADnsScoresNothingAndARiderMissingAMotoIsStillListed()
  {
    var result = Score(
      Moto("Moto 1",
        RiderBuilder.Rider("A", "1", "Anna Berger").Laps(10, 40).Build(),
        RiderBuilder.Rider("B", "2", "Ben Fischer").Dns().Build()),
      Moto("Moto 2",
        RiderBuilder.Rider("A", "1", "Anna Berger").Laps(10, 40).Build()));

    var ben = OnlyClass(result).Entries.Single(e => e.Number == "2");
    Assert.Equal(new[] { "DNS", "-" }, ben.Motos.Select(m => m.Text));
    Assert.Equal(0, ben.Points);
    Assert.Null(ben.Rank);
    Assert.Equal("-", ben.RankText);
  }

  [Fact]
  public void EachClassIsScoredOnItsOwn()
  {
    var result = Score(Moto("Moto 1",
      RiderBuilder.Rider("A", "1", "Anna Berger").Laps(10, 40).Category("MX1").Build(),
      RiderBuilder.Rider("B", "2", "Ben Fischer").Laps(10, 41).Category("MX2").Build(),
      RiderBuilder.Rider("C", "3", "Carla Hoff").Laps(10, 42).Category("MX2").Build()));

    Assert.Equal(new[] { "MX1", "MX2" }, result.Classes.Select(c => c.Name));
    Assert.Equal(25, result.Classes[1].Entries[0].Points);
    Assert.Equal("2", result.Classes[1].Entries[0].Number);
  }

  [Fact]
  public void ASpareTransponderInTheSecondMotoIsTheSameRiderByNumber()
  {
    var result = Score(
      Moto("Moto 1", RiderBuilder.Rider("A", "1", "Anna Berger").Laps(10, 40).Build()),
      Moto("Moto 2", RiderBuilder.Rider("SPARE", "1", "Anna Berger").Laps(10, 40).Build()));

    var anna = Assert.Single(OnlyClass(result).Entries);
    Assert.Equal(50, anna.Points);
    Assert.Null(anna.Warning);
  }

  [Fact]
  public void ANameCorrectedBetweenMotosIsOneRiderAndFlagged()
  {
    var result = Score(
      Moto("Moto 1", RiderBuilder.Rider("A", "7", "Ana Berger").Laps(10, 40).Build()),
      Moto("Moto 2", RiderBuilder.Rider("A", "7", "Anna Berger").Laps(10, 40).Build()));

    var entry = Assert.Single(OnlyClass(result).Entries);
    Assert.Equal(("Anna Berger", 50), (entry.Name, entry.Points));
    Assert.Contains("different names: Ana Berger / Anna Berger", entry.Warning);
  }

  [Fact]
  public void AnotherRiderOnTheSameNumberIsNotAddedToTheFirst()
  {
    var result = Score(
      Moto("Moto 1", RiderBuilder.Rider("A", "7", "Anna Berger").Laps(10, 40).Build()),
      Moto("Moto 2", RiderBuilder.Rider("B", "7", "Ben Fischer").Laps(10, 40).Build()));

    var entries = OnlyClass(result).Entries;
    Assert.Equal(new[] { 25, 25 }, entries.Select(e => e.Points));
    Assert.All(entries, e => Assert.Contains("Number 7 was given to Anna Berger and Ben Fischer", e.Warning));
  }

  // 31 October 2026: two beginners were both #231 in the rider list. The
  // second overwrote the first, and a rider on the podium of both motos was
  // missing from the overall.
  [Fact]
  public void TwoRidersGivenOneNumberAreScoredApart()
  {
    var result = Score(
      Moto("Moto 1",
        RiderBuilder.Rider("A", "1", "Anna Berger").Laps(8, 40).Build(),
        RiderBuilder.Rider("B", "231", "Ben Fischer").Laps(8, 41).Build(),
        RiderBuilder.Rider("C", "231", "Carla Hoff").Laps(8, 42).Build()),
      Moto("Moto 2",
        RiderBuilder.Rider("A", "1", "Anna Berger").Laps(8, 40).Build(),
        RiderBuilder.Rider("C", "231", "Carla Hoff").Laps(8, 41).Build(),
        RiderBuilder.Rider("B", "231", "Ben Fischer").Laps(8, 42).Build()));

    var entries = OnlyClass(result).Entries;
    Assert.Equal(3, entries.Count);
    var ben = entries.Single(e => e.Name == "Ben Fischer");
    var carla = entries.Single(e => e.Name == "Carla Hoff");
    Assert.Equal(new[] { "2", "3" }, ben.Motos.Select(m => m.Text));
    Assert.Equal(new[] { "3", "2" }, carla.Motos.Select(m => m.Text));
    Assert.Equal((42, 42), (ben.Points, carla.Points));
    Assert.All(new[] { ben, carla }, e => Assert.Contains("Ben Fischer and Carla Hoff", e.Warning));
    Assert.DoesNotContain("different names", ben.Warning);
  }

  [Fact]
  public void ASharedNumberStillFollowsARiderOnASpareTransponderByName()
  {
    var result = Score(
      Moto("Moto 1",
        RiderBuilder.Rider("B", "231", "Ben Fischer").Laps(8, 40).Build(),
        RiderBuilder.Rider("C", "231", "Carla Hoff").Laps(8, 41).Build()),
      Moto("Moto 2",
        RiderBuilder.Rider("SPARE", "231", "Ben Fischer").Laps(8, 41).Build(),
        RiderBuilder.Rider("C", "231", "Carla Hoff").Laps(8, 40).Build()));

    var entries = OnlyClass(result).Entries;
    Assert.Equal(2, entries.Count);
    Assert.Equal(new[] { "1", "2" }, entries.Single(e => e.Name == "Ben Fischer").Motos.Select(m => m.Text));
  }

  [Fact]
  public void ARiderWhoseNumberIsSharedOnlyInTheSecondMotoKeepsTheirFirst()
  {
    var result = Score(
      Moto("Moto 1", RiderBuilder.Rider("B", "231", "Ben Fischer").Laps(8, 40).Build()),
      Moto("Moto 2",
        RiderBuilder.Rider("C", "231", "Carla Hoff").Laps(8, 40).Build(),
        RiderBuilder.Rider("B", "231", "Ben Fischer").Laps(8, 41).Build()));

    var entries = OnlyClass(result).Entries;
    Assert.Equal(new[] { "1", "2" }, entries.Single(e => e.Name == "Ben Fischer").Motos.Select(m => m.Text));
    Assert.Equal(new[] { "-", "1" }, entries.Single(e => e.Name == "Carla Hoff").Motos.Select(m => m.Text));
  }

  [Fact]
  public void TheSameNumberInTwoClassesIsTwoRiders()
  {
    var result = Score(
      Moto("Moto 1",
        RiderBuilder.Rider("A", "7", "Anna Berger").Category("MX1").Laps(8, 40).Build(),
        RiderBuilder.Rider("B", "7", "Ben Fischer").Category("MX2").Laps(8, 40).Build()));

    Assert.All(result.Classes, c => Assert.Null(Assert.Single(c.Entries).Warning));
  }

  [Fact]
  public void AClubsOwnPointsTableIsUsed()
  {
    var rules = new OverallRules { Points = new[] { 10, 5 } };
    var result = OverallScorer.Score("Overall", new[]
    {
      Moto("Moto 1",
        RiderBuilder.Rider("A", "1", "Anna Berger").Laps(8, 40).Build(),
        RiderBuilder.Rider("B", "2", "Ben Fischer").Laps(4, 40).Dnf().Build(),
        RiderBuilder.Rider("C", "3", "Carla Hoff").Laps(8, 45).Build())
    }, rules);

    var entries = OnlyClass(result).Entries;
    Assert.Equal(new[] { 10, 5, 0 }, entries.Select(e => e.Points));
    // Ben retired and is placed third: past the end of the table, so a place
    // but no points.
    Assert.Equal(new int?[] { 1, 2, 3 }, entries.Select(e => e.Rank));
  }

  [Fact]
  public void ATeamIsFollowedByItsTeamName()
  {
    var result = Score(
      Moto("Moto 1", RiderBuilder.Team("Rot", RiderBuilder.Member("11", "Anna Berger", "T1")).Laps(10, 40).Build()),
      Moto("Moto 2", RiderBuilder.Team("Rot", RiderBuilder.Member("11", "Anna Berger", "T1")).Laps(10, 40).Build()));

    var team = Assert.Single(OnlyClass(result).Entries);
    Assert.True(team.IsTeam);
    Assert.Equal(50, team.Points);
  }

  [Theory]
  [InlineData("25, 22 20;18", new[] { 25, 22, 20, 18 })]
  [InlineData("", null)]
  [InlineData("25, x", null)]
  public void APointsTableIsReadAsTyped(string text, int[]? expected)
  {
    Assert.Equal(expected, OverallRules.ParsePoints(text)?.ToArray());
  }
}
