using Xunit;

namespace CrossMgrInterface.Tests;

public class TrackDefinitionTests
{
  private static void Close(double expected, double actual, double tolerance, string what) =>
    Assert.True(Math.Abs(expected - actual) <= tolerance,
      $"{what}: expected {expected}, got {actual} (tolerance {tolerance})");

  private static void AtSamePlace(LatLon expected, LatLon actual, double toleranceMetres, string what) =>
    Assert.True(TrackBuilder.Metres(expected, actual) <= toleranceMetres,
      $"{what}: {TrackBuilder.Metres(expected, actual):F2}m adrift (tolerance {toleranceMetres}m)");

  // ---- Anchors survive re-editing ------------------------------------------

  [Fact]
  public void PlacingTheStartFinishSnapsItOntoTheLineNotToTheNearestCorner()
  {
    var track = TrackBuilder.Square();
    var clickedBesideTheSouthSide = TrackBuilder.North(TrackBuilder.SouthMid, 8);

    track.StartFinish.PlaceAt(track.Geometry, clickedBesideTheSouthSide);

    Close(0.625, track.StartFinish.Fraction, 0.001, "fraction of the south side midpoint");
    AtSamePlace(TrackBuilder.SouthMid, track.StartFinishLocation, 0.1, "start/finish position");

    // The remembered ground position is the snapped one, not the raw click, or
    // every later reprojection would drift the same few metres again.
    AtSamePlace(TrackBuilder.SouthMid, track.StartFinish.Ground, 0.1, "remembered ground position");
  }

  [Fact]
  public void ADrawnLoopWhoseStartFinishWasNeverPlacedSaysSo()
  {
    // Drawing a loop leaves the start/finish defaulted to wherever the operator
    // started clicking, which is almost never the painted line. That has to be
    // flagged, or the circuit saves silently and every rider position on it is
    // measured from the wrong place.
    var drawn = new TrackDefinition { Name = "Hand drawn" };
    foreach (var p in TrackBuilder.SquarePoints()) drawn.AddPoint(p);

    Assert.True(drawn.StartFinish.NeedsReview);
    Assert.Contains(drawn.Validate(), m => m.Contains("start/finish", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void PlacingTheStartFinishClearsTheWarning()
  {
    var drawn = new TrackDefinition { Name = "Hand drawn" };
    foreach (var p in TrackBuilder.SquarePoints()) drawn.AddPoint(p);

    drawn.StartFinish.PlaceAt(drawn.Geometry, TrackBuilder.SouthMid);

    Assert.False(drawn.StartFinish.NeedsReview);
    Assert.DoesNotContain(drawn.Validate(), m => m.Contains("start/finish", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void APlacedStartFinishStaysUnflaggedThroughLaterEdits()
  {
    // The flag must not come back every time another point is added, or it would
    // be noise rather than a warning.
    var drawn = TrackBuilder.SquareWithFinishOnTheSouthSide();

    drawn.AddPoint(TrackBuilder.North(TrackBuilder.NorthMid, 40));
    drawn.MovePoint(1, TrackBuilder.East(TrackBuilder.Ne, 20));

    Assert.False(drawn.StartFinish.NeedsReview);
  }

  [Fact]
  public void MovingAVertexElsewhereOnTheLoopLeavesTheStartFinishWhereItWas()
  {
    var track = TrackBuilder.SquareWithFinishOnTheSouthSide();
    var before = track.StartFinishLocation;

    // Push the north-east corner 100m further east. The south side is untouched,
    // but the loop is longer, so a fraction stored on its own would slide.
    track.MovePoint(1, TrackBuilder.East(TrackBuilder.Ne, 100));

    AtSamePlace(before, track.StartFinishLocation, 1.0, "start/finish after moving a distant vertex");
    Assert.False(track.StartFinish.NeedsReview);
  }

  [Fact]
  public void InsertingPointsBeforeTheStartFinishDoesNotSlideIt()
  {
    // The exact failure a bare fraction would have. A 100m detour is added to the
    // north side, lengthening the loop from 1000m to about 1070m. The finish
    // line's FRACTION must change - and its position on the ground must not.
    var track = TrackBuilder.SquareWithFinishOnTheSouthSide();
    var before = track.StartFinishLocation;
    var fractionBefore = track.StartFinish.Fraction;

    track.InsertPoint(1, TrackBuilder.North(TrackBuilder.NorthMid, 100));

    Assert.True(track.LengthMetres > 1050, $"the detour should lengthen the loop, got {track.LengthMetres:F0}m");
    Assert.True(Math.Abs(track.StartFinish.Fraction - fractionBefore) > 0.01,
      "the fraction must have moved, or this test is passing vacuously");
    AtSamePlace(before, track.StartFinishLocation, 1.0, "start/finish after lengthening the loop");
  }

  [Fact]
  public void AnAnchorTheLoopHasMovedAwayFromIsFlaggedRatherThanTeleported()
  {
    var track = TrackBuilder.SquareWithFinishOnTheSouthSide();

    // Replace the circuit with one a kilometre away. Silently snapping the finish
    // line to whatever is nearest now would be worse than admitting the problem.
    track.SetPoints(TrackBuilder.SquarePoints()
      .Select(p => TrackBuilder.North(p, 1000)).ToList());

    Assert.True(track.StartFinish.NeedsReview);
    Assert.Contains(track.Validate(), m => m.Contains("start/finish", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void SectorBoundariesReprojectIndependentlyOfTheStartFinish()
  {
    var track = TrackBuilder.SquareWithFinishOnTheSouthSide();
    track.AddSector("The Climb", TrackBuilder.Ne);
    track.AddSector("Back Straight", TrackBuilder.Sw);

    var climbBefore = track.Geometry.LocationAtFraction(track.Sectors[0].Start.Fraction);
    var straightBefore = track.Geometry.LocationAtFraction(track.Sectors[1].Start.Fraction);
    var finishBefore = track.StartFinishLocation;

    track.InsertPoint(1, TrackBuilder.North(TrackBuilder.NorthMid, 100));

    AtSamePlace(climbBefore, track.Geometry.LocationAtFraction(track.Sectors[0].Start.Fraction), 1.0, "first sector");
    AtSamePlace(straightBefore, track.Geometry.LocationAtFraction(track.Sectors[1].Start.Fraction), 1.0, "second sector");
    AtSamePlace(finishBefore, track.StartFinishLocation, 1.0, "start/finish");
  }

  [Fact]
  public void NudgingTheFinishLineDoesNotDragTheSectorsWithIt()
  {
    // Sector fractions are absolute, not relative to the start/finish. "Move the
    // finish line" has to mean only that.
    var track = TrackBuilder.Square();
    track.AddSector("The Climb", TrackBuilder.Ne);
    var climbBefore = track.Sectors[0].Start.Fraction;

    track.StartFinish.PlaceAt(track.Geometry, TrackBuilder.SouthMid);

    Assert.Equal(climbBefore, track.Sectors[0].Start.Fraction);
  }

  // ---- Sector lookup -------------------------------------------------------

  [Fact]
  public void WithNoSectorsTheSectorIndexIsMinusOne()
  {
    Assert.Equal(-1, TrackGeometry.SectorIndexAt(0.42, Array.Empty<TrackSector>()));
  }

  [Fact]
  public void TheSectorIndexFollowsTheDotRoundTheLoop()
  {
    var sectors = new[]
    {
      TrackBuilder.Sector("Start Straight", 0.00),
      TrackBuilder.Sector("The Climb", 0.25),
      TrackBuilder.Sector("Back Straight", 0.50),
      TrackBuilder.Sector("Descent", 0.75)
    };

    Assert.Equal(0, TrackGeometry.SectorIndexAt(0.1, sectors));
    Assert.Equal(1, TrackGeometry.SectorIndexAt(0.3, sectors));
    Assert.Equal(2, TrackGeometry.SectorIndexAt(0.6, sectors));
    Assert.Equal(3, TrackGeometry.SectorIndexAt(0.9, sectors));
  }

  [Fact]
  public void ASectorBoundaryBelongsToTheSectorItStarts()
  {
    // Half-open intervals, pinned. Otherwise a dot sitting exactly on a boundary
    // flickers between two sector names.
    var sectors = new[]
    {
      TrackBuilder.Sector("A", 0.00),
      TrackBuilder.Sector("B", 0.25),
      TrackBuilder.Sector("C", 0.50)
    };

    Assert.Equal(1, TrackGeometry.SectorIndexAt(0.25, sectors));
    Assert.Equal(2, TrackGeometry.SectorIndexAt(0.50, sectors));
  }

  [Fact]
  public void SectorsThatDoNotStartAtTheFinishLineStillCoverTheWholeLoop()
  {
    // The wrap case, and the one this always gets wrong. With boundaries at 0.1
    // and 0.6, the stretch from 0.6 round past the line to 0.1 is one sector, so
    // a rider at 0.05 is in the SECOND sector, not off the end of the list.
    var sectors = new[]
    {
      TrackBuilder.Sector("Front", 0.10),
      TrackBuilder.Sector("Back", 0.60)
    };

    Assert.Equal(1, TrackGeometry.SectorIndexAt(0.05, sectors));
    Assert.Equal(1, TrackGeometry.SectorIndexAt(0.95, sectors));
    Assert.Equal(0, TrackGeometry.SectorIndexAt(0.30, sectors));
  }

  [Fact]
  public void SectorsAreKeptInOrderRoundTheLoopHoweverTheyWereAdded()
  {
    var track = TrackBuilder.Square();

    track.AddSector("Third", TrackBuilder.Sw);
    track.AddSector("First", TrackBuilder.Ne);
    track.AddSector("Second", TrackBuilder.Se);

    Assert.Equal(new[] { "First", "Second", "Third" }, track.Sectors.Select(s => s.Name));
  }

  [Fact]
  public void AnUnnamedSectorStillReadsAsSomething()
  {
    var track = TrackBuilder.Square();
    track.AddSector("", TrackBuilder.Ne);

    Assert.Equal("Sector 1", track.SectorNameAt(0.3));
  }

  // ---- Point editing -------------------------------------------------------

  [Fact]
  public void AVertexCannotBeRemovedIfItWouldLeaveFewerThanThree()
  {
    var track = TrackBuilder.Square();

    Assert.True(track.RemovePointAt(0));
    Assert.Equal(3, track.Points.Count);
    Assert.False(track.RemovePointAt(0));
    Assert.Equal(3, track.Points.Count);
  }

  [Fact]
  public void ReversingTheDirectionKeepsTheLoopAndTheFinishLineButFlipsTheHeading()
  {
    var track = TrackBuilder.SquareWithFinishOnTheSouthSide();
    var finishBefore = track.StartFinishLocation;
    var lengthBefore = track.LengthMetres;
    var headingBefore = track.Geometry.PointAtFraction(track.StartFinish.Fraction).HeadingDegrees;

    track.ReverseDirection();

    Close(lengthBefore, track.LengthMetres, 0.01, "length after reversing");
    AtSamePlace(finishBefore, track.StartFinishLocation, 1.0, "start/finish after reversing");

    var headingAfter = track.Geometry.PointAtFraction(track.StartFinish.Fraction).HeadingDegrees;
    Close(180, Math.Abs(GeoMath.Normalise360(headingAfter - headingBefore)), 1.0, "heading flip");
  }

  // ---- Housekeeping --------------------------------------------------------

  [Fact]
  public void CloningGivesAnIndependentCopyTheEditorCanThrowAway()
  {
    var track = TrackBuilder.Square();
    track.AddSector("The Climb", TrackBuilder.Ne);

    var copy = track.Clone();
    copy.AddPoint(TrackBuilder.North(TrackBuilder.Ne, 50));
    copy.Sectors[0].Name = "Renamed";
    copy.StartFinish.Fraction = 0.9;

    Assert.Equal(4, track.Points.Count);
    Assert.Equal("The Climb", track.Sectors[0].Name);
    Assert.Equal(0, track.StartFinish.Fraction);
  }

  [Fact]
  public void ValidationComplainsAboutATrackTooSmallToRace()
  {
    var tiny = new TrackDefinition { Name = "Tiny" };
    tiny.SetPoints(TrackBuilder.CirclePoints(2, 6));

    Assert.Contains(tiny.Validate(), m => m.Contains("too short"));
  }

  [Fact]
  public void ValidationComplainsAboutTooFewPoints()
  {
    var line = new TrackDefinition { Name = "Not a loop" };
    line.SetPoints(new[] { TrackBuilder.Nw, TrackBuilder.Ne });

    Assert.Contains(line.Validate(), m => m.Contains("at least three points"));
  }

  [Fact]
  public void AFigureOfEightIsAcceptedBecauseItIsALegitimateLayout()
  {
    var track = new TrackDefinition { Name = "Figure of eight" };
    track.SetPoints(new[]
    {
      TrackBuilder.Nw, TrackBuilder.Se, TrackBuilder.Ne, TrackBuilder.Sw
    });
    track.StartFinish.PlaceAt(track.Geometry, TrackBuilder.Nw);

    Assert.Empty(track.Validate());
  }
}
