using Xunit;

namespace CrossMgrInterface.Tests;

public class AssignTagTests
{
  private static readonly DateTime Start = RiderBuilder.RaceStart;
  private static readonly TimeSpan MinimumLap = TimeSpan.FromSeconds(10);

  private static (RaceCorrectionService Service, Dictionary<string, RiderInfo> Field) NewField(
    params RiderInfo[] riders)
  {
    var field = riders.ToDictionary(r => r.TagID, r => r);
    var service = new RaceCorrectionService(field, new object(), () => Start, _ => { });
    return (service, field);
  }

  [Fact]
  public void AttachingAnIdentityKeepsEveryRecordedLap()
  {
    var unknown = RiderBuilder.Rider("STRAY", name: "").Laps(4, 40).Build();
    unknown.RiderNumber = "";
    unknown.FirstName = unknown.LastName = "";
    var (service, field) = NewField(unknown);

    var result = service.AssignTag("STRAY", new AssignTagRequest
    {
      Mode = AssignTagMode.AttachIdentity,
      RiderNumber = "12",
      FirstName = "Max",
      LastName = "Mustermann"
    }, MinimumLap);

    Assert.True(result.Ok, result.Error);
    Assert.Equal(4, field["STRAY"].TotalLaps);
    Assert.Equal("#12 Max Mustermann", field["STRAY"].Label);
  }

  [Fact]
  public void MergingCombinesLapsAndRemovesTheStrayTransponder()
  {
    var known = RiderBuilder.Rider("KNOWN", "12", "Max Mustermann").Laps(3, 40).Build();

    // Stray reads at times that do not clash with the known rider's laps.
    var stray = RiderBuilder.Rider("STRAY").Build();
    foreach (var offset in new[] { 200.0, 240.0 })
    {
      stray.Laps.Add(new RiderLap { TagID = "STRAY", CrossingTime = Start.AddSeconds(offset) });
    }

    var (service, field) = NewField(known, stray);

    var result = service.AssignTag("STRAY", new AssignTagRequest
    {
      Mode = AssignTagMode.MergeIntoRider,
      MergeTargetTag = "KNOWN"
    }, MinimumLap);

    Assert.True(result.Ok, result.Error);
    Assert.False(field.ContainsKey("STRAY"));
    Assert.Equal(5, field["KNOWN"].TotalLaps);
    Assert.Equal(new[] { 1, 2, 3, 4, 5 }, field["KNOWN"].Laps.Select(l => l.LapNumber));
  }

  [Fact]
  public void MergingDropsReadsThatClashWithAnExistingLap()
  {
    // Both transponders saw the same three passes, a fraction of a second apart.
    var known = RiderBuilder.Rider("KNOWN", "12", "Max Mustermann").Laps(3, 40).Build();

    var stray = RiderBuilder.Rider("STRAY").Build();
    foreach (var lap in known.Laps)
    {
      stray.Laps.Add(new RiderLap
      {
        TagID = "STRAY",
        CrossingTime = lap.CrossingTime.AddMilliseconds(300)
      });
    }

    var (service, field) = NewField(known, stray);

    service.AssignTag("STRAY", new AssignTagRequest
    {
      Mode = AssignTagMode.MergeIntoRider,
      MergeTargetTag = "KNOWN",
      DropDuplicateCrossings = true
    }, MinimumLap);

    // Without the guard this would be six laps, three of them 0.3 seconds long.
    Assert.Equal(3, field["KNOWN"].TotalLaps);
    Assert.All(field["KNOWN"].Laps.Where(l => l.LapTime.HasValue),
      l => Assert.True(l.LapTime!.Value >= MinimumLap,
        $"lap {l.LapNumber} is {l.LapTime.Value.TotalSeconds}s, which is physically impossible"));
  }

  [Fact]
  public void MergingCanBeUndone()
  {
    var known = RiderBuilder.Rider("KNOWN", "12", "Max Mustermann").Laps(3, 40).Build();
    var stray = RiderBuilder.Rider("STRAY").Build();
    stray.Laps.Add(new RiderLap { TagID = "STRAY", CrossingTime = Start.AddSeconds(200) });

    var (service, field) = NewField(known, stray);

    service.AssignTag("STRAY", new AssignTagRequest
    {
      Mode = AssignTagMode.MergeIntoRider,
      MergeTargetTag = "KNOWN"
    }, MinimumLap);

    Assert.False(field.ContainsKey("STRAY"));

    service.Undo();

    // Both riders come back exactly as they were.
    Assert.True(field.ContainsKey("STRAY"));
    Assert.Single(field["STRAY"].Laps);
    Assert.Equal(3, field["KNOWN"].TotalLaps);
  }

  [Fact]
  public void MergingRegistersAnAliasSoLaterReadsFollowTheRider()
  {
    var known = RiderBuilder.Rider("KNOWN", "12", "Max Mustermann").Laps(3, 40).Build();
    var stray = RiderBuilder.Rider("STRAY").Build();
    stray.Laps.Add(new RiderLap { TagID = "STRAY", CrossingTime = Start.AddSeconds(200) });

    var (service, _) = NewField(known, stray);

    var result = service.AssignTag("STRAY", new AssignTagRequest
    {
      Mode = AssignTagMode.MergeIntoRider,
      MergeTargetTag = "KNOWN",
      RegisterAlias = true
    }, MinimumLap);

    Assert.Equal("KNOWN", result.Command!.AliasesAdded["STRAY"]);
  }

  [Fact]
  public void MergingIntoItselfIsRefused()
  {
    var rider = RiderBuilder.Rider("A").Laps(3, 40).Build();
    var (service, _) = NewField(rider);

    var result = service.AssignTag("A", new AssignTagRequest
    {
      Mode = AssignTagMode.MergeIntoRider,
      MergeTargetTag = "A"
    }, MinimumLap);

    Assert.False(result.Ok);
  }
}
