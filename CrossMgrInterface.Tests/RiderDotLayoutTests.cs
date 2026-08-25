using Xunit;

namespace CrossMgrInterface.Tests;

public class RiderDotLayoutTests
{
  private static readonly Size Screen = new(900, 700);

  private static MapViewport View(int zoom = 17) =>
    new(new LatLon(TrackBuilder.BaseLat + TrackBuilder.DeltaLat / 2,
                   TrackBuilder.BaseLon + TrackBuilder.DeltaLon / 2), zoom, Screen);

  private static MapRiderMarker Marker(
    string tag, LatLon at, TrackPositionState state = TrackPositionState.OnTrack,
    double heading = 90, int rank = 0, double fraction = 0.5, bool highlighted = false) =>
    new(tag, tag, $"#{tag} Rider", "Surname", "Elite", at, heading, rank, state, fraction, Badge: null, highlighted);

  private static readonly TrackGeometry Loop = TrackBuilder.SquareGeometry();

  /// <summary>
  /// Riders distributed round the loop itself, which is what the solver produces
  /// and what keeps them all inside the viewport. Placing them in a straight line
  /// off into the distance instead just tests the culling.
  /// </summary>
  private static List<MapRiderMarker> SpreadOut(int count, double? spacingFraction = null)
  {
    var spacing = spacingFraction ?? 1.0 / Math.Max(1, count);

    return Enumerable.Range(0, count).Select(i =>
    {
      var f = i * spacing % 1.0;
      var at = Loop.PointAtFraction(f);
      return Marker($"R{i:D3}", at.Location, heading: at.HeadingDegrees, fraction: f);
    }).ToList();
  }

  /// <summary>Riders bunched into a few metres, as they are on lap one.</summary>
  private static List<MapRiderMarker> Bunched(int count) =>
    Enumerable.Range(0, count)
      .Select(i => Marker($"B{i:D3}", TrackBuilder.East(TrackBuilder.Nw, 100 + i * 0.4)))
      .ToList();

  [Fact]
  public void RidersSpreadOutRoundTheCircuitAreDrawnIndividually()
  {
    var layout = RiderDotLayout.Build(SpreadOut(5), View(), null, null);

    Assert.Equal(5, layout.Dots.Count);
    Assert.Empty(layout.Clusters);
  }

  [Fact]
  public void ABunchCollapsesIntoOneCountedMarker()
  {
    var layout = RiderDotLayout.Build(Bunched(12), View(), null, null);

    Assert.NotEmpty(layout.Clusters);
    Assert.Equal(12, layout.Clusters.Sum(c => c.Count) + layout.Dots.Count);
  }

  [Fact]
  public void EveryRiderEndsUpEitherAsADotOrInsideACluster()
  {
    var riders = SpreadOut(8).Concat(Bunched(20)).ToList();

    var layout = RiderDotLayout.Build(riders, View(), null, null);

    var accounted = layout.Dots.Select(d => d.TagId)
      .Concat(layout.Clusters.SelectMany(c => c.TagIds))
      .ToHashSet();

    Assert.Equal(riders.Count, accounted.Count);
  }

  [Fact]
  public void ClusterMembershipDoesNotChangeWhenTheMapIsPannedByOnePixel()
  {
    // THE reason cells are indexed in world pixels rather than screen pixels.
    // Screen cells shift with the pan origin, so a one-pixel pan would reshuffle
    // every cluster and the whole map would visibly boil.
    var riders = SpreadOut(6, spacingFraction: 0.012).Concat(Bunched(15)).ToList();

    var before = RiderDotLayout.Build(riders, View(), null, null);
    var after = RiderDotLayout.Build(riders, View().PannedByPixels(1, 1), null, null);

    static string[] Groups(DotLayout l) => l.Clusters
      .Select(c => string.Join(",", c.TagIds.OrderBy(t => t)))
      .OrderBy(s => s).ToArray();

    Assert.Equal(Groups(before), Groups(after));
    Assert.Equal(before.Dots.Count, after.Dots.Count);
  }

  [Fact]
  public void TwoRidersInOneCellAreOffsetAcrossTheTrackRatherThanStackedOnTopOfEachOther()
  {
    var pair = new List<MapRiderMarker>
    {
      Marker("A", TrackBuilder.East(TrackBuilder.Nw, 100)),
      Marker("B", TrackBuilder.East(TrackBuilder.Nw, 100.3))
    };

    var layout = RiderDotLayout.Build(pair, View(), null, null);

    Assert.Equal(2, layout.Dots.Count);

    // Heading is due east, so the offset is north-south: the dots separate
    // vertically on screen and stay on the road rather than along it.
    var a = layout.Dots[0].Centre;
    var b = layout.Dots[1].Centre;
    Assert.True(Math.Abs(a.Y - b.Y) > 6, $"the pair was not fanned apart: {a} vs {b}");
  }

  [Fact]
  public void TheOffsetOfAPairIsStableBetweenFrames()
  {
    // An unstable ordering makes two dots swap sides on every repaint, which at
    // eight frames a second looks like a fault.
    var pair = new List<MapRiderMarker>
    {
      Marker("B", TrackBuilder.East(TrackBuilder.Nw, 100.3)),
      Marker("A", TrackBuilder.East(TrackBuilder.Nw, 100))
    };

    var first = RiderDotLayout.Build(pair, View(), null, null);
    var second = RiderDotLayout.Build(pair.AsEnumerable().Reverse().ToList(), View(), null, null);

    static PointF CentreOf(DotLayout l, string tag) => l.Dots.First(d => d.TagId == tag).Centre;

    Assert.Equal(CentreOf(first, "A"), CentreOf(second, "A"));
    Assert.Equal(CentreOf(first, "B"), CentreOf(second, "B"));
  }

  [Fact]
  public void OverdueRidersFanOutInsteadOfCollapsingIntoACluster()
  {
    // The solver parks every overdue rider on the line, so clustering them would
    // hide exactly the riders the operator most needs to pick out.
    var line = TrackBuilder.Nw;
    var overdue = Enumerable.Range(0, 6)
      .Select(i => Marker($"O{i}", line, TrackPositionState.Overdue, fraction: 1.1 + i * 0.2))
      .ToList();

    var layout = RiderDotLayout.Build(overdue, View(), null, null);

    Assert.Empty(layout.Clusters);
    Assert.Equal(6, layout.Dots.Count);
    Assert.Equal(6, layout.Dots.Select(d => d.Centre).Distinct().Count());
  }

  [Fact]
  public void TheMostOverdueRiderIsFurthestBackFromTheLine()
  {
    var line = TrackBuilder.Nw;
    var overdue = new List<MapRiderMarker>
    {
      Marker("Slightly", line, TrackPositionState.Overdue, heading: 90, fraction: 1.1),
      Marker("Badly", line, TrackPositionState.Overdue, heading: 90, fraction: 2.5)
    };

    var layout = RiderDotLayout.Build(overdue, View(), null, null);

    var badly = layout.Dots.First(d => d.TagId == "Badly").Centre;
    var slightly = layout.Dots.First(d => d.TagId == "Slightly").Centre;

    // Travelling east, so "behind" is to the west - a smaller screen X.
    Assert.True(badly.X < slightly.X,
      $"the more overdue rider should be further back: badly {badly.X}, slightly {slightly.X}");
  }

  [Fact]
  public void RidersOffTheEdgeOfTheScreenAreNotDrawn()
  {
    var riders = new List<MapRiderMarker>
    {
      Marker("Visible", Loop.LocationAtFraction(0.5)),
      Marker("Miles away", new LatLon(20, -100))
    };

    var layout = RiderDotLayout.Build(riders, View(), null, null);

    Assert.Single(layout.Dots);
    Assert.Equal("Visible", layout.Dots[0].TagId);
  }

  [Fact]
  public void AClusterNamesTheBestPlacedRiderInIt()
  {
    // "27+11" says who leads the group; "12" says only that a group exists.
    var bunch = Bunched(12);
    for (var i = 0; i < bunch.Count; i++)
      bunch[i] = bunch[i] with { Rank = bunch.Count - i, RiderNumber = $"{10 + i}" };

    var layout = RiderDotLayout.Build(bunch, View(), null, null);

    var cluster = Assert.Single(layout.Clusters);

    // Rank 1 is the last entry by construction.
    Assert.Equal(bunch[^1].RiderNumber, cluster.LeaderNumber);
    Assert.Equal(12, cluster.Count);
  }

  [Fact]
  public void AClusterOfUnrankedRidersStillFormsButNamesNobody()
  {
    var layout = RiderDotLayout.Build(Bunched(9), View(), null, null);

    var cluster = Assert.Single(layout.Clusters);
    Assert.Null(cluster.LeaderNumber);
    Assert.Equal(9, cluster.Count);
  }

  [Fact]
  public void ATwoHundredAndFiftyRiderFieldCollapsesToSomethingCountable()
  {
    // The measured reason the sector panel and the top-N filter exist: a full
    // field on a circuit-sized loop produces a handful of blobs, not 250 dots.
    var riders = SpreadOut(250);

    var layout = RiderDotLayout.Build(riders, View(), null, null);

    var drawn = layout.Dots.Count + layout.Clusters.Count;
    Assert.True(drawn < 100, $"{drawn} separate marks on screen is not a readable map");
    Assert.Equal(250, layout.Dots.Count + layout.Clusters.Sum(c => c.Count));
  }

  // ---- Labels --------------------------------------------------------------  // ---- Labels --------------------------------------------------------------

  [Fact]
  public void NotEveryRiderInABigFieldGetsALabel()
  {
    // 250 labels is a solid block of text, and each one is a halo - five
    // DrawString calls - so the cap is a performance bound as well as a legibility one.
    var riders = SpreadOut(250);

    var layout = RiderDotLayout.Build(riders, View(), null, null);

    Assert.True(layout.Dots.Count(d => d.Label is not null) <= RiderDotLayout.MaxLabels,
      "the label budget was exceeded");
  }

  [Fact]
  public void TheRiderTheOperatorAskedAboutIsAlwaysLabelled()
  {
    // The label cap must never spend itself on the pack and leave the highlighted
    // rider anonymous - "where is 27?" is the whole point of the highlight box.
    var riders = SpreadOut(250);
    riders[200] = riders[200] with { Highlighted = true };
    var wanted = riders[200].TagId;

    var layout = RiderDotLayout.Build(riders, View(), null, null);

    var dot = layout.Dots.FirstOrDefault(d => d.TagId == wanted);
    Assert.NotNull(dot);
    Assert.NotNull(dot!.Label);
  }

  [Fact]
  public void TheSelectedRiderIsDrawnLargerSoItCanBePickedOut()
  {
    var riders = SpreadOut(5);

    var layout = RiderDotLayout.Build(riders, View(), riders[2].TagId, null);

    var selected = layout.Dots.First(d => d.TagId == riders[2].TagId);
    var other = layout.Dots.First(d => d.TagId == riders[0].TagId);

    Assert.True(selected.Radius > other.Radius);
    Assert.True(selected.Highlighted);
  }

  [Fact]
  public void TurningLabelsOffLeavesNone()
  {
    var layout = RiderDotLayout.Build(SpreadOut(5), View(), null, null, MapLabelParts.None);

    Assert.All(layout.Dots, d => Assert.Null(d.Label));
  }

  [Fact]
  public void ZoomedOutOnlyTheLeadersAreLabelled()
  {
    var riders = SpreadOut(20, spacingFraction: 0.05);
    for (var i = 0; i < riders.Count; i++) riders[i] = riders[i] with { Rank = i + 1 };

    var layout = RiderDotLayout.Build(riders, View(zoom: 15), null, null, MapLabelParts.Number, labelTopN: 3);

    var labelled = layout.Dots.Where(d => d.Label is not null).Select(d => d.TagId).ToList();

    Assert.True(labelled.Count <= 3, $"expected at most the top three, got {labelled.Count}");
  }

  [Fact]
  public void LabelsAreBuiltFromWhicheverPartsWereAskedFor()
  {
    var rider = Marker("A", Loop.LocationAtFraction(0.5), rank: 3) with { RiderNumber = "27" };

    Assert.Equal("P3", RiderDotLayout.Compose(rider, MapLabelParts.Position));
    Assert.Equal("27", RiderDotLayout.Compose(rider, MapLabelParts.Number));
    Assert.Equal("Surname", RiderDotLayout.Compose(rider, MapLabelParts.Name));
    Assert.Equal("P3 27", RiderDotLayout.Compose(rider, MapLabelParts.Position | MapLabelParts.Number));
    Assert.Equal("P3 27 Surname",
      RiderDotLayout.Compose(rider, MapLabelParts.Position | MapLabelParts.Number | MapLabelParts.Name));
    Assert.Equal("", RiderDotLayout.Compose(rider, MapLabelParts.None));
  }

  [Fact]
  public void PositionIsWrittenSoItCannotBeReadAsAStartNumber()
  {
    // "3 27" beside a dot is ambiguous; "P3 27" is not.
    var rider = Marker("A", Loop.LocationAtFraction(0.5), rank: 3) with { RiderNumber = "27" };

    Assert.StartsWith("P", RiderDotLayout.Compose(rider, MapLabelParts.Position));
  }

  [Fact]
  public void ARiderWithNoPositionYetIsNotLabelledPZero()
  {
    var unranked = Marker("A", Loop.LocationAtFraction(0.5), rank: 0) with { RiderNumber = "27" };

    Assert.Equal("27", RiderDotLayout.Compose(unranked, MapLabelParts.Position | MapLabelParts.Number));
  }

  [Fact]
  public void AskingForANameNobodyHasFallsBackRatherThanLeavingABlankLabel()
  {
    var anonymous = Marker("A", Loop.LocationAtFraction(0.5), rank: 2) with { ShortName = "", RiderNumber = "27" };

    Assert.Equal("P2 27", RiderDotLayout.Compose(anonymous, MapLabelParts.Position | MapLabelParts.Number | MapLabelParts.Name));
    Assert.Equal("", RiderDotLayout.Compose(anonymous, MapLabelParts.Name));
  }

  [Fact]
  public void NamesAreActuallyDrawnRatherThanOnlyOnTheSelectedDot()
  {
    // The old "numbers + names" mode only ever put a name on the highlighted dot,
    // so asking for names appeared to do nothing at all.
    var riders = SpreadOut(4);
    for (var i = 0; i < riders.Count; i++)
      riders[i] = riders[i] with { Rank = i + 1, RiderNumber = $"{i + 1}" };

    var layout = RiderDotLayout.Build(riders, View(), null, null, MapLabelParts.Name);

    Assert.Contains(layout.Dots, d => d.Label == "Surname");
  }

  [Fact]
  public void AnEmptyFieldLaysOutToNothingWithoutThrowing()
  {
    var layout = RiderDotLayout.Build(Array.Empty<MapRiderMarker>(), View(), null, null);

    Assert.Empty(layout.Dots);
    Assert.Empty(layout.Clusters);
  }
}
