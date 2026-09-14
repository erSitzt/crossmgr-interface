using System.Reflection;
using Xunit;

namespace CrossMgrInterface.Tests;

public class TeamEntryTests
{
  private static RiderBuilder Adler() => RiderBuilder.Team("MSC Adler",
    RiderBuilder.Member("11", "Anna Berger", "A01"),
    RiderBuilder.Member("14", "Ben Fischer", "A02"));

  [Fact]
  public void ATeamIsLabelledWithItsNumbersAndName()
  {
    var team = Adler().Build();

    Assert.Equal("#11/14 MSC Adler", team.Label);
    Assert.Equal("A01, A02", team.TransponderText);
  }

  [Fact]
  public void ARiderIsShortenedToNumberAndSurname()
  {
    Assert.Equal("#14 Fischer", RiderBuilder.Member("14", "Ben Fischer", "A02").ShortLabel);
    // Nothing to shorten to: the full label, not a bare number.
    Assert.Equal("Ben", RiderBuilder.Member("", "Ben", "A02").ShortLabel);
  }

  [Fact]
  public void ASoloRidersTransponderIsTheirOwnTag()
  {
    var rider = RiderBuilder.Rider("10000001").Build();

    Assert.False(rider.IsTeam);
    Assert.Equal("10000001", rider.TransponderText);
  }

  [Fact]
  public void TheRiderOnTrackIsWhoeverCrossedLast()
  {
    var team = Adler().LapBy(40, "A01").LapBy(40, "A01").LapBy(45, "A02").Build();

    Assert.Equal("Ben", team.OnTrackMember!.FirstName);
  }

  [Fact]
  public void NobodyIsOnTrackWhenTheTransponderIsShared()
  {
    var team = RiderBuilder.Team("RC Falke",
        RiderBuilder.Member("21", "Carla Hoff", "B00"),
        RiderBuilder.Member("22", "David Kern", "B00"))
      .LapBy(40, "B00").Build();

    Assert.Null(team.OnTrackMember);
  }

  [Fact]
  public void NobodyIsOnTrackForALapWithNoRecordedTransponder()
  {
    var team = Adler().LapBy(40, "A01").LapBy(40, null).Build();

    Assert.Null(team.OnTrackMember);
  }

  [Fact]
  public void AnOverlapWarningCountsAsAnAnomalyUntilDismissed()
  {
    var team = Adler().LapBy(40, "A01").LapBy(10, "A02").Build();
    team.Laps[1].IsSuspectedOverlap = true;

    Assert.True(team.HasAnomalies);

    team.Laps[1].OverlapDismissed = true;
    Assert.False(team.HasAnomalies);
  }

  [Fact]
  public void ALapFlaggedTwoOnTrackIsNotTheTeamsBestLap()
  {
    var team = Adler().LapBy(40, "A01").LapBy(41, "A01").LapBy(15, "A02").LapBy(43, "A02").Build();
    team.Laps[2].IsSuspectedOverlap = true;

    Assert.Equal(TimeSpan.FromSeconds(41), team.BestLapTime);

    // Kept by the operator it counts as a lap, but it is still a handover - not a
    // lap one rider rode from line to line - so it still is not the best lap.
    team.Laps[2].IsSuspectedOverlap = false;
    team.Laps[2].OverlapDismissed = true;
    Assert.Equal(TimeSpan.FromSeconds(41), team.BestLapTime);
  }

  [Fact]
  public void AHandoverIsNotTheTeamsBestLap()
  {
    // Lap 3 is Ben's return 29s after an overlap: a handover, and a fragment.
    var team = Adler().LapBy(40, "A01").LapBy(41, "A01").LapBy(29, "A02").LapBy(43, "A02").Build();

    Assert.Equal(TimeSpan.FromSeconds(41), team.BestLapTime);
  }

  [Fact]
  public void ATeamThatHandsOverEveryLapStillHasABestLap()
  {
    var team = Adler().LapBy(40, "A01").LapBy(45, "A02").LapBy(44, "A01").Build();

    Assert.Equal(TimeSpan.FromSeconds(44), team.BestLapTime);
  }

  [Fact]
  public void ASnapshotBringsBackATeamsMembers()
  {
    var team = Adler().LapBy(40, "A01").Build();
    var riders = new Dictionary<string, RiderInfo> { [team.TagID] = team };

    var snapshot = RiderSnapshot.Capture(team, team.TagID);
    riders.Clear();
    snapshot.RestoreInto(riders);

    var restored = riders[team.TagID];
    Assert.Same(team.Members, restored.Members);
    Assert.Equal("A01", restored.Laps[0].CrossedBy);
  }

  /// <summary>
  /// Undo, redo and the merge all go through Clone. A field it forgets is
  /// silently lost on the first correction, which is how who-rode-which-lap would
  /// disappear - so check every settable property, including future ones.
  /// </summary>
  [Fact]
  public void CloneCopiesEverySettableProperty()
  {
    var original = new RiderLap();
    var properties = typeof(RiderLap).GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => p.CanWrite)
      .ToList();

    foreach (var property in properties)
      property.SetValue(original, NonDefaultValue(property.PropertyType));

    var copy = original.Clone();

    foreach (var property in properties)
      Assert.True(Equals(property.GetValue(original), property.GetValue(copy)),
        $"RiderLap.Clone does not copy {property.Name}");
  }

  private static object NonDefaultValue(Type type)
  {
    var underlying = Nullable.GetUnderlyingType(type) ?? type;

    if (underlying == typeof(string)) return "x";
    if (underlying == typeof(bool)) return true;
    if (underlying == typeof(int)) return 7;
    if (underlying == typeof(DateTime)) return new DateTime(2026, 9, 13, 12, 0, 0);
    if (underlying == typeof(TimeSpan)) return TimeSpan.FromSeconds(42);
    if (underlying.IsEnum) return Enum.GetValues(underlying).GetValue(Enum.GetValues(underlying).Length - 1)!;

    throw new InvalidOperationException($"Add a non-default value for {type.Name} to this test.");
  }
}
