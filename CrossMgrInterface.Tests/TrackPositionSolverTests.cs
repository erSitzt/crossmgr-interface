using Xunit;

namespace CrossMgrInterface.Tests;

public class TrackPositionSolverTests
{
  private static readonly DateTime Noon = TrackBuilder.Noon;

  /// <summary>The 1000m square, start/finish on the north-west corner at fraction 0.</summary>
  private static TrackFrame Square() => TrackFrame.From(TrackBuilder.Square());

  private static void Close(double expected, double actual, double tolerance, string what) =>
    Assert.True(Math.Abs(expected - actual) <= tolerance,
      $"{what}: expected {expected}, got {actual} (tolerance {tolerance})");

  private static void AtSamePlace(LatLon expected, LatLon actual, double toleranceMetres, string what) =>
    Assert.True(TrackBuilder.Metres(expected, actual) <= toleranceMetres,
      $"{what}: {TrackBuilder.Metres(expected, actual):F2}m adrift (tolerance {toleranceMetres}m)");

  // ---- The core claim ------------------------------------------------------

  [Fact]
  public void ARiderHalfwayThroughTheirLapIsHalfwayRoundTheLoop()
  {
    // The single most important assertion in the feature. 40s pace, 20s since
    // the line, so half a lap by ARC LENGTH - which on this square is the far
    // corner, two sides along.
    var rider = TrackBuilder.Datum(lastCrossing: Noon, paceSeconds: 40);

    var p = TrackPositionSolver.Solve(rider, Noon.AddSeconds(20), Square(), TrackBuilder.Racing());

    Assert.Equal(TrackPositionState.OnTrack, p.State);
    Close(0.5, p.Fraction, 1e-9, "lap progress");
    AtSamePlace(TrackBuilder.Se, p.Location, 0.1, "position half a lap in");
  }

  [Fact]
  public void AFreshCrossingPutsTheRiderOnTheLine()
  {
    var rider = TrackBuilder.Datum(lastCrossing: Noon);

    var p = TrackPositionSolver.Solve(rider, Noon, Square(), TrackBuilder.Racing());

    Assert.Equal(TrackPositionState.OnTrack, p.State);
    Assert.Equal(0, p.Fraction);
    AtSamePlace(TrackBuilder.Nw, p.Location, 0.1, "position at the line");
  }

  [Fact]
  public void TheDotMovesAtAConstantSpeed()
  {
    // Pins the constant-speed model: equal time, equal distance. If someone later
    // adds sector weighting, this is the test that should be changed on purpose.
    var rider = TrackBuilder.Datum(lastCrossing: Noon, paceSeconds: 40);
    var frame = Square();

    var a = TrackPositionSolver.Solve(rider, Noon.AddSeconds(10), frame, TrackBuilder.Racing()).Location;
    var b = TrackPositionSolver.Solve(rider, Noon.AddSeconds(20), frame, TrackBuilder.Racing()).Location;
    var c = TrackPositionSolver.Solve(rider, Noon.AddSeconds(30), frame, TrackBuilder.Racing()).Location;

    // 10s of a 40s lap is a quarter of 1000m. Straight-line distance across a
    // square corner is shorter than the arc, so compare the two equal-arc hops.
    Close(TrackBuilder.Metres(a, b), TrackBuilder.Metres(b, c), 1.0, "equal time gives equal distance");
  }

  [Fact]
  public void TheStartFinishOffsetIsAppliedSoLapsBeginAtTheLineNotAtPointZero()
  {
    // Finish line half way along the south side, at loop fraction 0.625. Half a
    // lap on from there wraps past point zero to the middle of the north side.
    var frame = TrackFrame.From(TrackBuilder.SquareWithFinishOnTheSouthSide());
    var rider = TrackBuilder.Datum(lastCrossing: Noon, paceSeconds: 40);

    var p = TrackPositionSolver.Solve(rider, Noon.AddSeconds(20), frame, TrackBuilder.Racing());

    Close(0.5, p.Fraction, 1e-9, "lap progress");

    // 1e-4 of a 1000m loop is 10cm. The square's east-west sides measure a few
    // millimetres under 250m, because the geometry scales longitude at the
    // centroid latitude while the fixture builds it at the base latitude.
    Close(0.125, p.TrackFraction, 1e-4, "position round the loop");
    AtSamePlace(TrackBuilder.NorthMid, p.Location, 0.1, "half a lap on from the south side");
  }

  // ---- Pace selection ------------------------------------------------------

  [Fact]
  public void ARiderWithOneLapUsesTheFieldMedianNotTheirRunFromTheStartLine()
  {
    // The trap this whole design exists to avoid. When the race starts on the
    // first transponder read, that rider's lap 1 is 0.000s. PredictedLapTime
    // happily returns it; dividing by it puts the leader into orbit.
    var leader = RiderBuilder.Rider("A").Lap(0.001).Build();

    Assert.True(leader.PredictedLapTime!.Value.TotalSeconds < 1,
      "the artefact must actually be present or this test proves nothing");
    Assert.Null(leader.RacingPace);

    var p = TrackPositionSolver.Solve(
      RiderMapDatum.From(leader), leader.LastCrossing.AddSeconds(20),
      Square(), TrackBuilder.Racing(medianPaceSeconds: 40));

    Close(0.5, p.Fraction, 1e-9, "lap progress from the field median");
    Assert.True(p.Fraction < 1.0,
      $"used the start-line run instead of the median: fraction came out {p.Fraction}");
  }

  [Fact]
  public void ARiderWithTwoLapsUsesTheirSecondLapAlone()
  {
    var rider = RiderBuilder.Rider("A").Lap(0.001).Lap(50).Build();

    Assert.Equal(TimeSpan.FromSeconds(50), rider.RacingPace);
  }

  [Fact]
  public void FromThreeLapsOnTheRacingPaceIgnoresTheStartLineRun()
  {
    // 0.001, 40, 40, 40. Including lap 1 gives (0.001 + 80 + 120)/6 = 33.3s,
    // seventeen percent fast. Excluding it gives 40s.
    var rider = RiderBuilder.Rider("A").Lap(0.001).Lap(40).Lap(40).Lap(40).Build();

    Close(40, rider.RacingPace!.Value.TotalSeconds, 0.001, "racing pace");
  }

  [Fact]
  public void APaceShorterThanTheApplicationsOwnMinimumLapIsRejected()
  {
    // Form1 already refuses to record a lap under ten seconds, so by the
    // application's own definition there is nothing here to divide by.
    var rider = TrackBuilder.Datum(paceSeconds: 5);

    var p = TrackPositionSolver.Solve(rider, Noon.AddSeconds(20), Square(), TrackBuilder.Racing());

    Assert.Equal(TrackPositionState.NoPrediction, p.State);
  }

  [Fact]
  public void APaceOfThreeQuartersOfAnHourIsRejectedBecauseTheRiderHasStopped()
  {
    var rider = TrackBuilder.Datum(paceSeconds: 2700);

    var p = TrackPositionSolver.Solve(rider, Noon.AddSeconds(20), Square(), TrackBuilder.Racing());

    Assert.Equal(TrackPositionState.NoPrediction, p.State);
  }

  [Fact]
  public void WithNoPaceAndNoFieldMedianTheRiderSitsAtTheLineWithNoPrediction()
  {
    var rider = TrackBuilder.Datum(paceSeconds: null);

    var p = TrackPositionSolver.Solve(rider, Noon.AddSeconds(90), Square(), TrackBuilder.Racing());

    Assert.Equal(TrackPositionState.NoPrediction, p.State);
    Assert.Null(p.Pace);
    AtSamePlace(TrackBuilder.Nw, p.Location, 0.1, "position with no prediction");
  }

  [Fact]
  public void TheFieldMedianIgnoresTheRiderWhoParkedForTwoMinutes()
  {
    // Median, not mean: one stopped rider must not drag the whole field's
    // fallback pace out with them.
    var field = new[]
    {
      TrackBuilder.Datum("A", paceSeconds: 40),
      TrackBuilder.Datum("B", paceSeconds: 41),
      TrackBuilder.Datum("C", paceSeconds: 42),
      TrackBuilder.Datum("D", paceSeconds: 1700)
    };

    var median = TrackPositionSolver.FieldMedianPace(field);

    Close(41.5, median!.Value.TotalSeconds, 0.001, "median pace");
  }

  [Fact]
  public void TheFieldMedianIgnoresRetiredAndNonStartingRiders()
  {
    var field = new[]
    {
      TrackBuilder.Datum("A", paceSeconds: 40),
      TrackBuilder.Datum("B", paceSeconds: 200, dnf: true),
      TrackBuilder.Datum("C", paceSeconds: 200, dns: true)
    };

    Close(40, TrackPositionSolver.FieldMedianPace(field)!.Value.TotalSeconds, 0.001, "median pace");
  }

  [Fact]
  public void AFieldWithNoTimedLapsHasNoMedianPace()
  {
    var field = new[] { TrackBuilder.Datum("A", paceSeconds: null) };

    Assert.Null(TrackPositionSolver.FieldMedianPace(field));
  }

  // ---- Overdue -------------------------------------------------------------

  [Fact]
  public void ARiderPastTheirLapTimeIsOverdueAndParkedOnTheLine()
  {
    // 40s pace, 52s elapsed. They have not crossed, so they cannot be past the
    // line; we do not know where they are, so we cannot honestly place them short
    // of it either. The dot stops on the line and the badge carries the truth.
    var rider = TrackBuilder.Datum(lastCrossing: Noon, paceSeconds: 40);

    var p = TrackPositionSolver.Solve(rider, Noon.AddSeconds(52), Square(), TrackBuilder.Racing());

    Assert.Equal(TrackPositionState.Overdue, p.State);
    Close(1.3, p.Fraction, 1e-9, "true lap progress");
    Assert.Equal(1.0, p.DrawFraction);
    AtSamePlace(TrackBuilder.Nw, p.Location, 0.1, "parked on the line");
  }

  [Fact]
  public void TheOverdueTimeIsReportedTruthfullySoTheBadgeCanShowSeconds()
  {
    var rider = TrackBuilder.Datum(lastCrossing: Noon, paceSeconds: 40);

    var p = TrackPositionSolver.Solve(rider, Noon.AddSeconds(52), Square(), TrackBuilder.Racing());

    var overdueBy = p.SinceLastCrossing - p.Pace!.Value;
    Close(12, overdueBy.TotalSeconds, 0.001, "seconds overdue for the badge");
  }

  [Fact]
  public void JustOverTheLineIsOrdinaryOverdueNotTheUrgentBand()
  {
    var rider = TrackBuilder.Datum(lastCrossing: Noon, paceSeconds: 40);

    var p = TrackPositionSolver.Solve(rider, Noon.AddSeconds(42), Square(), TrackBuilder.Racing());

    Assert.Equal(TrackPositionState.Overdue, p.State);
    Assert.True(p.Fraction <= TrackPositionSolver.MildOverdueFraction,
      "1.05 laps overdue should still read as ordinary");
  }

  [Fact]
  public void ThreeLapTimesOverdueBecomesLongOverdue()
  {
    var rider = TrackBuilder.Datum(lastCrossing: Noon, paceSeconds: 40);

    var p = TrackPositionSolver.Solve(rider, Noon.AddSeconds(200), Square(), TrackBuilder.Racing());

    Assert.Equal(TrackPositionState.LongOverdue, p.State);
    Assert.Equal(1.0, p.DrawFraction);
  }

  [Fact]
  public void ACrossingTimestampedInTheFutureDoesNotSendTheDotBackwards()
  {
    // Clock skew between the reader and this machine is a real thing.
    var rider = TrackBuilder.Datum(lastCrossing: Noon.AddSeconds(30));

    var p = TrackPositionSolver.Solve(rider, Noon, Square(), TrackBuilder.Racing());

    Assert.Equal(0, p.Fraction);
    Assert.Equal(TimeSpan.Zero, p.SinceLastCrossing);
  }

  // ---- Riders who are not circulating --------------------------------------

  [Fact]
  public void ARiderWithNoLapsSitsOnTheStartLineAndIsMarkedOnGrid()
  {
    var rider = TrackBuilder.Datum(laps: 0);

    var p = TrackPositionSolver.Solve(rider, Noon.AddSeconds(90), Square(), TrackBuilder.Racing());

    Assert.Equal(TrackPositionState.OnGrid, p.State);
    Assert.Equal(0, p.Fraction);
    AtSamePlace(TrackBuilder.Nw, p.Location, 0.1, "position on the grid");
  }

  [Fact]
  public void ARiderWhoseLapsWereAllDeletedDoesNotUseTheStaleCrossingTime()
  {
    // RaceCorrectionService only refreshes LastCrossing while the rider still has
    // laps, so deleting every lap leaves the old timestamp behind. Reckoning from
    // it would fling the dot to a fraction in the hundreds.
    var rider = TrackBuilder.Datum(laps: 0, lastCrossing: Noon.AddHours(-2));

    var p = TrackPositionSolver.Solve(rider, Noon, Square(), TrackBuilder.Racing());

    Assert.Equal(TrackPositionState.OnGrid, p.State);
    Assert.Equal(0, p.Fraction);
  }

  [Fact]
  public void ARetiredRiderIsFrozenWhereTheyWereWhenTheyStopped()
  {
    // 40s pace, retired 10s after their last crossing: a quarter of a lap in.
    var rider = TrackBuilder.Datum(
      lastCrossing: Noon, paceSeconds: 40, dnf: true, dnfTime: Noon.AddSeconds(10));

    var p = TrackPositionSolver.Solve(rider, Noon.AddSeconds(15), Square(), TrackBuilder.Racing());

    Assert.Equal(TrackPositionState.Retired, p.State);
    Close(0.25, p.Fraction, 1e-9, "lap progress when they retired");
    AtSamePlace(TrackBuilder.Ne, p.Location, 0.1, "frozen a quarter of a lap in");
  }

  [Fact]
  public void ARetiredRidersPositionDoesNotDriftOnAsTheRaceContinues()
  {
    var rider = TrackBuilder.Datum(
      lastCrossing: Noon, paceSeconds: 40, dnf: true, dnfTime: Noon.AddSeconds(10));
    var frame = Square();

    var soon = TrackPositionSolver.Solve(rider, Noon.AddSeconds(15), frame, TrackBuilder.Racing());
    var muchLater = TrackPositionSolver.Solve(rider, Noon.AddMinutes(10), frame, TrackBuilder.Racing());

    Assert.Equal(soon.Fraction, muchLater.Fraction);
    Assert.Equal(soon.Location, muchLater.Location);
  }

  [Fact]
  public void ARetiredRiderWithNoRecordedTimeFallsBackToTheLine()
  {
    var rider = TrackBuilder.Datum(lastCrossing: Noon, dnf: true, dnfTime: null);

    var p = TrackPositionSolver.Solve(rider, Noon.AddSeconds(60), Square(), TrackBuilder.Racing());

    Assert.Equal(TrackPositionState.Retired, p.State);
    AtSamePlace(TrackBuilder.Nw, p.Location, 0.1, "fallback position");
  }

  [Fact]
  public void ARiderWhoNeverStartedIsAtTheLineAndSaysSo()
  {
    var rider = TrackBuilder.Datum(dns: true);

    var p = TrackPositionSolver.Solve(rider, Noon.AddSeconds(60), Square(), TrackBuilder.Racing());

    Assert.Equal(TrackPositionState.DidNotStart, p.State);
    AtSamePlace(TrackBuilder.Nw, p.Location, 0.1, "position of a non-starter");
  }

  [Fact]
  public void NotStartingBeatsRetiringWhenBothAreSomehowSet()
  {
    // Matches RiderInfo.StatusText, which reports DNS first.
    var rider = TrackBuilder.Datum(dnf: true, dns: true, dnfTime: Noon.AddSeconds(10));

    var p = TrackPositionSolver.Solve(rider, Noon.AddSeconds(60), Square(), TrackBuilder.Racing());

    Assert.Equal(TrackPositionState.DidNotStart, p.State);
  }

  // ---- Race state ----------------------------------------------------------

  [Fact]
  public void BeforeTheRaceStartsEveryoneIsOnTheGrid()
  {
    var rider = TrackBuilder.Datum(laps: 3, paceSeconds: 40);

    var p = TrackPositionSolver.Solve(rider, Noon.AddSeconds(60), Square(), TrackBuilder.NotStarted);

    Assert.Equal(TrackPositionState.OnGrid, p.State);
    AtSamePlace(TrackBuilder.Nw, p.Location, 0.1, "position before the start");
  }

  [Fact]
  public void OnceTheRaceIsOverEveryoneIsFrozenAtTheLine()
  {
    var rider = TrackBuilder.Datum(lastCrossing: Noon, paceSeconds: 40);

    var p = TrackPositionSolver.Solve(rider, Noon.AddSeconds(20), Square(), TrackBuilder.Finished);

    Assert.Equal(TrackPositionState.Finished, p.State);
    AtSamePlace(TrackBuilder.Nw, p.Location, 0.1, "position after the finish");
  }

  [Fact]
  public void ARiderWhoHasCompletedTheirFinalLapIsFinishedWhileOthersAreStillRiding()
  {
    var done = TrackBuilder.Datum("A", lastCrossing: Noon, laps: 12, finalAllowedLap: 12);
    var stillGoing = TrackBuilder.Datum("B", lastCrossing: Noon, laps: 11, finalAllowedLap: 12);

    var frame = Square();
    var now = Noon.AddSeconds(20);

    Assert.Equal(TrackPositionState.Finished,
      TrackPositionSolver.Solve(done, now, frame, TrackBuilder.Racing()).State);
    Assert.Equal(TrackPositionState.OnTrack,
      TrackPositionSolver.Solve(stillGoing, now, frame, TrackBuilder.Racing()).State);
  }

  // ---- Sectors -------------------------------------------------------------

  [Fact]
  public void TheSectorFollowsTheRiderRoundTheLoop()
  {
    var track = TrackBuilder.Square();
    track.AddSector("North", TrackBuilder.Nw);
    track.AddSector("East", TrackBuilder.Ne);
    track.AddSector("South", TrackBuilder.Se);
    track.AddSector("West", TrackBuilder.Sw);

    var frame = TrackFrame.From(track);
    var rider = TrackBuilder.Datum(lastCrossing: Noon, paceSeconds: 40);

    Assert.Equal(0, TrackPositionSolver.Solve(rider, Noon.AddSeconds(4), frame, TrackBuilder.Racing()).SectorIndex);
    Assert.Equal(1, TrackPositionSolver.Solve(rider, Noon.AddSeconds(12), frame, TrackBuilder.Racing()).SectorIndex);
    Assert.Equal(2, TrackPositionSolver.Solve(rider, Noon.AddSeconds(24), frame, TrackBuilder.Racing()).SectorIndex);
    Assert.Equal(3, TrackPositionSolver.Solve(rider, Noon.AddSeconds(36), frame, TrackBuilder.Racing()).SectorIndex);
  }

  [Fact]
  public void WithNoSectorsDefinedTheSectorIndexIsMinusOne()
  {
    var rider = TrackBuilder.Datum(lastCrossing: Noon);

    var p = TrackPositionSolver.Solve(rider, Noon.AddSeconds(20), Square(), TrackBuilder.Racing());

    Assert.Equal(-1, p.SectorIndex);
  }

  // ---- What a dot is labelled with -----------------------------------------

  [Fact]
  public void ARiderWithNoStartNumberIsLabelledWithTheirTransponderNotAQuestionMark()
  {
    // Before a rider list is imported every start number is blank. A map full of
    // dots reading "?" identifies nobody, whereas the transponder at least tells
    // two riders apart and matches what the tag events tab is showing.
    var unknown = RiderBuilder.Rider("RIDER003", number: "").Lap(40).Lap(40).Build();

    var datum = RiderMapDatum.From(unknown);

    Assert.Equal("RIDER003", datum.RiderNumber);
  }

  [Fact]
  public void ALongTransponderIsShortenedToSomethingThatFitsBesideADot()
  {
    var unknown = RiderBuilder.Rider("E2003036F5A2C1", number: "").Lap(40).Build();

    var datum = RiderMapDatum.From(unknown);

    Assert.Equal("F5A2C1", datum.RiderNumber);
    Assert.True(datum.RiderNumber.Length <= 8);
  }

  [Fact]
  public void AStartNumberIsAlwaysPreferredToTheTransponder()
  {
    var known = RiderBuilder.Rider("E2003036F5A2C1", number: "27").Lap(40).Build();

    Assert.Equal("27", RiderMapDatum.From(known).RiderNumber);
  }

  [Fact]
  public void TheShortNameIsTheSurname()
  {
    // At map scale "Smith" carries more than "John", and there is only room for one.
    var rider = RiderBuilder.Rider("A", name: "John Smith").Lap(40).Build();

    Assert.Equal("Smith", RiderMapDatum.From(rider).ShortName);
  }

  [Fact]
  public void ARiderWithOnlyAFirstNameStillGetsAShortName()
  {
    var rider = RiderBuilder.Rider("A", name: "Cher").Lap(40).Build();

    Assert.Equal("Cher", RiderMapDatum.From(rider).ShortName);
  }

  [Fact]
  public void ARiderWithNoNameAtAllHasNoShortName()
  {
    var anonymous = new RiderInfo { TagID = "A", RiderNumber = "27" };

    Assert.Equal("", RiderMapDatum.From(anonymous).ShortName);
  }

  // ---- Contract ------------------------------------------------------------

  [Fact]
  public void EveryDotInOneFrameSharesOneClock()
  {
    // Guards against DateTime.Now sneaking inside the loop, which would let the
    // field drift apart by however long the solve took.
    var field = Enumerable.Range(0, 50)
      .Select(i => TrackBuilder.Datum($"T{i}", lastCrossing: Noon, paceSeconds: 40))
      .ToList();

    var into = new List<TrackPosition>();
    TrackPositionSolver.SolveAll(field, Noon.AddSeconds(20), Square(), TrackBuilder.Racing(), into);

    Assert.Equal(50, into.Count);
    Assert.Single(into.Select(p => p.Fraction).Distinct());
  }

  [Fact]
  public void SolvingTheFieldReusesTheCallersBuffer()
  {
    // The anti-churn contract: this runs eight times a second with 250 riders.
    var field = Enumerable.Range(0, 250)
      .Select(i => TrackBuilder.Datum($"T{i}", lastCrossing: Noon))
      .ToList();

    var into = new List<TrackPosition>();
    var frame = Square();

    TrackPositionSolver.SolveAll(field, Noon.AddSeconds(5), frame, TrackBuilder.Racing(), into);
    var capacity = into.Capacity;

    for (var i = 0; i < 20; i++)
      TrackPositionSolver.SolveAll(field, Noon.AddSeconds(5 + i), frame, TrackBuilder.Racing(), into);

    Assert.Equal(250, into.Count);
    Assert.Equal(capacity, into.Capacity);
  }

  [Fact]
  public void SolvingReturnsEveryRiderIncludingTheOnesTheViewWillHide()
  {
    // Filtering is the view's job. The solver stays pure and total.
    var field = new[]
    {
      TrackBuilder.Datum("A"),
      TrackBuilder.Datum("B", dnf: true),
      TrackBuilder.Datum("C", dns: true),
      TrackBuilder.Datum("D", laps: 0)
    };

    var into = new List<TrackPosition>();
    TrackPositionSolver.SolveAll(field, Noon, Square(), TrackBuilder.Racing(), into);

    Assert.Equal(4, into.Count);
  }

  [Fact]
  public void SolvingAgainstATrackWithNoGeometryDoesNotThrow()
  {
    var rider = TrackBuilder.Datum(lastCrossing: Noon);

    var p = TrackPositionSolver.Solve(rider, Noon.AddSeconds(20), TrackFrame.Empty, TrackBuilder.Racing());

    Assert.Equal(rider.TagId, p.TagId);
    Assert.True(double.IsFinite(p.Location.Lat));
  }
}
