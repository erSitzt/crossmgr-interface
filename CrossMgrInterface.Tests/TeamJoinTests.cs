using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// Identify, in a team event: a spare transponder, or one read before the rider
/// list was loaded, turns out to be a team rider's.
/// </summary>
public class TeamJoinTests
{
  private static readonly DateTime Start = RiderBuilder.RaceStart;
  private static readonly TimeSpan MinimumLap = TimeSpan.FromSeconds(10);

  private static RiderBuilder AdlerEntry() => RiderBuilder.Team("MSC Adler",
    RiderBuilder.Member("11", "Anna Berger", "A01"),
    RiderBuilder.Member("14", "Ben Fischer", "A02"));

  /// <summary>Anna 4 x 40s then Ben 2 x 45s: the team's last crossing is at 250s.</summary>
  private static RiderInfo Adler() => AdlerEntry()
    .LapBy(40, "A01").LapBy(40, "A01").LapBy(40, "A01").LapBy(40, "A01")
    .LapBy(45, "A02").LapBy(45, "A02")
    .Build();

  private static RiderInfo Spare(params double[] seconds)
  {
    var spare = RiderBuilder.Rider("SPARE", "", "").Build();
    foreach (var s in seconds)
      spare.Laps.Add(new RiderLap { TagID = "SPARE", CrossingTime = Start.AddSeconds(s), CrossedBy = "SPARE" });
    RaceCorrectionService.RecomputeRider(spare, Start);
    return spare;
  }

  private static (RaceCorrectionService Service, Dictionary<string, RiderInfo> Field) NewField(params RiderInfo[] riders)
  {
    var field = riders.ToDictionary(r => r.TagID, r => r);
    return (new RaceCorrectionService(field, new object(), () => Start, _ => { }), field);
  }

  [Fact]
  public void JoiningATeamMovesTheLapsAndKeepsWhichTransponderRodeThem()
  {
    var adler = Adler();
    var (service, field) = NewField(adler, Spare(300, 340));

    var result = service.AssignTag("SPARE", new AssignTagRequest
    {
      Mode = AssignTagMode.JoinTeam,
      TeamKey = adler.TagID
    }, MinimumLap);

    Assert.True(result.Ok, result.Error);
    Assert.False(field.ContainsKey("SPARE"));
    Assert.Equal(8, field[adler.TagID].TotalLaps);
    Assert.Equal(new[] { "SPARE", "SPARE" }, field[adler.TagID].Laps.Skip(6).Select(l => l.CrossedBy));
    Assert.Equal(adler.TagID, result.Command!.AliasesAdded["SPARE"]);
  }

  [Fact]
  public void JoiningAsARiderMakesItThatRidersTransponder()
  {
    var adler = Adler();
    var (service, field) = NewField(adler, Spare(300, 340));

    var result = service.AssignTag("SPARE", new AssignTagRequest
    {
      Mode = AssignTagMode.JoinTeam,
      TeamKey = adler.TagID,
      MemberIndex = 0
    }, MinimumLap);

    Assert.True(result.Ok, result.Error);
    Assert.Equal(new[] { "A01", "SPARE" }, field[adler.TagID].Members![0].Transponders);
    Assert.Equal("Anna", field[adler.TagID].OnTrackMember!.FirstName);
  }

  [Fact]
  public void JoiningATeamThatHasNotCrossedYetStartsScoringIt()
  {
    var template = AdlerEntry().Build();
    var (service, field) = NewField(Spare(40, 80));

    var result = service.AssignTag("SPARE", new AssignTagRequest
    {
      Mode = AssignTagMode.JoinTeam,
      TeamKey = template.TagID,
      TeamTemplate = template,
      MemberIndex = 1
    }, MinimumLap);

    Assert.True(result.Ok, result.Error);
    var team = field[template.TagID];
    Assert.Equal(2, team.TotalLaps);
    Assert.Equal("#11/14 MSC Adler", team.Label);
    Assert.Equal("Ben", team.MemberFor("SPARE")!.FirstName);
  }

  [Fact]
  public void ATeamCannotBeMergedIntoAnotherEntry()
  {
    var adler = Adler();
    var other = RiderBuilder.Team("RC Falke", RiderBuilder.Member("21", "Carla Hoff", "B00")).LapBy(40, "B00").Build();
    var (service, _) = NewField(adler, other);

    var result = service.AssignTag(adler.TagID, new AssignTagRequest
    {
      Mode = AssignTagMode.JoinTeam,
      TeamKey = other.TagID
    }, MinimumLap);

    Assert.False(result.Ok);
  }

  [Fact]
  public void UndoingAJoinPutsTheTransponderAndTheTeamBack()
  {
    var adler = Adler();
    var (service, field) = NewField(adler, Spare(300, 340));

    Assert.True(service.AssignTag("SPARE", new AssignTagRequest
    {
      Mode = AssignTagMode.JoinTeam,
      TeamKey = adler.TagID,
      MemberIndex = 0
    }, MinimumLap).Ok);

    Assert.True(service.Undo().Ok);

    Assert.Equal(2, field["SPARE"].TotalLaps);
    Assert.Equal(6, field[adler.TagID].TotalLaps);
    Assert.Equal(new[] { "A01" }, field[adler.TagID].Members![0].Transponders);
  }

  [Fact]
  public void UndoingAJoinThatStartedATeamTakesTheTeamOutAgain()
  {
    var template = AdlerEntry().Build();
    var (service, field) = NewField(Spare(40, 80));

    Assert.True(service.AssignTag("SPARE", new AssignTagRequest
    {
      Mode = AssignTagMode.JoinTeam,
      TeamKey = template.TagID,
      TeamTemplate = template
    }, MinimumLap).Ok);

    Assert.True(service.Undo().Ok);

    Assert.False(field.ContainsKey(template.TagID));
    Assert.Equal(2, field["SPARE"].TotalLaps);
  }
}
