namespace CrossMgrInterface;

/// <summary>What we can honestly say about where a rider is.</summary>
public enum TrackPositionState
{
  /// <summary>Somewhere round the loop, dead-reckoned from their last crossing.</summary>
  OnTrack,

  /// <summary>Should have crossed the line by now. Parked on it, with a badge saying by how much.</summary>
  Overdue,

  /// <summary>So far past due that the dot says nothing the leaderboard does not say better.</summary>
  LongOverdue,

  /// <summary>No usable pace, so no honest position. Drawn hollow at the line.</summary>
  NoPrediction,

  /// <summary>Has not left the line yet, or the race has not started.</summary>
  OnGrid,

  /// <summary>Retired. Frozen where the clock says we stopped hearing from them.</summary>
  Retired,

  DidNotStart,
  Finished
}

/// <summary>
/// The scalars the solver needs from a rider, and nothing else.
///
/// Deliberately flat: this is what gets snapshotted under ridersLock once per
/// frame, and copying whole lap lists for 250 riders eight times a second is not
/// affordable. It also means the solver can never touch a List&lt;RiderLap&gt;
/// outside the lock, and that tests can build one in a line.
/// </summary>
public readonly record struct RiderMapDatum(
  string TagId,
  string Label,
  string RiderNumber,
  string ShortName,
  string Category,
  DateTime LastCrossing,
  int TotalLaps,
  TimeSpan? RacingPace,
  bool IsDnf,
  bool IsDns,
  DateTime? DnfTime,
  int FinalAllowedLap)
{
  /// <summary>
  /// Projects a live rider. MUST be called while holding ridersLock: RacingPace
  /// walks the lap list, which the network thread appends to.
  /// </summary>
  /// <summary>Surname for preference: at map scale "Smith" says more than "John".</summary>
  private static string ShortNameOf(RiderInfo r) =>
    !string.IsNullOrWhiteSpace(r.LastName) ? r.LastName.Trim()
    : !string.IsNullOrWhiteSpace(r.FirstName) ? r.FirstName.Trim()
    : "";

  /// <summary>Enough of a transponder id to tell riders apart without filling the map.</summary>
  private static string ShortTag(string tagId) =>
    string.IsNullOrEmpty(tagId) ? "?"
    : tagId.Length <= 8 ? tagId
    : tagId[^6..];

  public static RiderMapDatum From(RiderInfo r) => new(
    r.TagID,
    r.Label,

    // Falls back to the transponder id, the way RiderInfo.Label does. Before a
    // rider list is imported every number is blank, and a map full of dots
    // labelled "?" identifies nobody.
    string.IsNullOrWhiteSpace(r.RiderNumber) ? ShortTag(r.TagID) : r.RiderNumber,

    ShortNameOf(r),
    r.Category,
    r.LastCrossing,
    r.TotalLaps,
    r.RacingPace,
    r.IsDNF,
    r.IsDNS,
    r.DNFTime,
    r.FinalAllowedLap);
}

/// <summary>Race-wide state the solver needs. Snapshotted with the field.</summary>
public readonly record struct RaceTiming(
  bool RaceStarted,
  bool RaceFinished,
  TimeSpan? FieldMedianLapTime);

/// <summary>
/// The circuit, snapshotted so a solve cannot see a mid-frame edit.
/// </summary>
public readonly record struct TrackFrame(
  TrackGeometry Geometry,
  double StartFinishFraction,
  IReadOnlyList<TrackSector> Sectors)
{
  public static readonly TrackFrame Empty =
    new(TrackGeometry.Empty, 0, Array.Empty<TrackSector>());

  public static TrackFrame From(TrackDefinition? track) => track is null
    ? Empty
    : new TrackFrame(track.Geometry, track.StartFinish.Fraction, track.Sectors);

  public bool IsUsable => Geometry.IsUsable;
}

/// <summary>
/// Where one rider is, and how much to believe it.
///
/// Three fractions, and they mean different things:
///   - Fraction is progress through the current lap, 0 at the line and 1 back at
///     the line. It is NOT clamped: 1.3 means thirty percent of a lap overdue,
///     and the badge on the dot reads exactly that.
///   - DrawFraction is the same value clamped into [0,1] - where the dot is
///     actually drawn, which is what parks an overdue rider on the line.
///   - TrackFraction is the position round the whole loop with the start/finish
///     offset applied, which is what the sector lookup runs on.
/// </summary>
public readonly record struct TrackPosition(
  string TagId,
  LatLon Location,
  double HeadingDegrees,
  double Fraction,
  double DrawFraction,
  double TrackFraction,
  int SectorIndex,
  TrackPositionState State,
  TimeSpan SinceLastCrossing,
  TimeSpan? Pace)
{
  /// <summary>True for states whose position is a real estimate rather than a placeholder.</summary>
  public bool IsMoving => State is TrackPositionState.OnTrack;
}

/// <summary>
/// Turns a crossing time and a pace into a position on the circuit.
///
/// This is the whole feature, and it is the only part of it that can be tested
/// without a screen - everything downstream is GDI+. So it is pure: no clock, no
/// lock, no control, no I/O. "now" is a parameter, which also means every dot in
/// one frame shares one instant instead of drifting apart as the loop runs.
///
/// The model is constant speed: the only measurement that exists is the crossing,
/// so between crossings the rider is assumed to cover equal distance per second.
/// Where that assumption runs out, the answer is a state that says so - a hollow
/// ring for "no pace yet", a dot parked on the line for "should have crossed by
/// now" - rather than a confident-looking position that is made up.
/// </summary>
public static class TrackPositionSolver
{
  /// <summary>
  /// Below this a "lap" is not a lap. Matches Form1's minimumLapTime: the
  /// application already refuses to record anything shorter, so by its own
  /// definition there is nothing here to divide by.
  /// </summary>
  public const double MinPlausibleLapSeconds = 10;

  /// <summary>
  /// Above this the rider has stopped, and dead-reckoning a stopped rider is a
  /// lie told confidently. Half an hour is far beyond any circuit lap.
  /// </summary>
  public const double MaxPlausibleLapSeconds = 1800;

  /// <summary>Past this much of a lap overdue, the dot stops being worth drawing.</summary>
  public const double LongOverdueFraction = 3.0;

  /// <summary>Below this, being overdue is ordinary - just a slower lap than the last three.</summary>
  public const double MildOverdueFraction = 1.10;

  public static TrackPosition Solve(
    in RiderMapDatum rider, DateTime now, in TrackFrame track, in RaceTiming timing)
  {
    var sinceLastCrossing = rider.TotalLaps > 0 && rider.LastCrossing != default
      ? Max(TimeSpan.Zero, now - rider.LastCrossing)
      : TimeSpan.Zero;

    // DNS beats DNF when both are somehow set, matching RiderInfo.StatusText.
    if (rider.IsDns)
      return AtTheLine(rider, track, TrackPositionState.DidNotStart, sinceLastCrossing, null);

    if (!timing.RaceStarted)
      return AtTheLine(rider, track, TrackPositionState.OnGrid, sinceLastCrossing, null);

    var pace = UsablePace(rider.RacingPace) ?? UsablePace(timing.FieldMedianLapTime);

    if (rider.IsDnf)
      return Retired(rider, track, pace);

    if (timing.RaceFinished || (rider.TotalLaps > 0 && rider.TotalLaps >= rider.FinalAllowedLap))
      return AtTheLine(rider, track, TrackPositionState.Finished, sinceLastCrossing, pace);

    // Gate on the lap count, never on LastCrossing. RaceCorrectionService only
    // refreshes LastCrossing when the rider still has laps, so a rider whose laps
    // were all deleted keeps a stale crossing time - dead-reckoning from it would
    // fling the dot to a fraction in the hundreds.
    if (rider.TotalLaps == 0)
      return AtTheLine(rider, track, TrackPositionState.OnGrid, sinceLastCrossing, pace);

    if (pace is null)
      return AtTheLine(rider, track, TrackPositionState.NoPrediction, sinceLastCrossing, null);

    var progress = sinceLastCrossing.TotalSeconds / pace.Value.TotalSeconds;

    var state = progress switch
    {
      > LongOverdueFraction => TrackPositionState.LongOverdue,
      > 1.0 => TrackPositionState.Overdue,
      _ => TrackPositionState.OnTrack
    };

    return Place(rider, track, progress, state, sinceLastCrossing, pace);
  }

  /// <summary>
  /// Solves the whole field into the caller's list, which is reused frame to
  /// frame rather than reallocated. Returns everyone: filtering by state is the
  /// view's job, so the solver stays pure and testable.
  /// </summary>
  public static void SolveAll(
    IReadOnlyList<RiderMapDatum> field, DateTime now, in TrackFrame track,
    in RaceTiming timing, List<TrackPosition> into)
  {
    into.Clear();
    for (var i = 0; i < field.Count; i++)
      into.Add(Solve(field[i], now, track, timing));
  }

  /// <summary>
  /// Median pace across the field, for riders who have no pace of their own.
  ///
  /// Median rather than mean: one rider who parked for two minutes wrecks a mean,
  /// and the whole point of this fallback is to be the sane default.
  /// </summary>
  public static TimeSpan? FieldMedianPace(IReadOnlyList<RiderMapDatum> field)
  {
    var paces = new List<double>(field.Count);

    for (var i = 0; i < field.Count; i++)
    {
      if (field[i].IsDnf || field[i].IsDns) continue;
      var pace = UsablePace(field[i].RacingPace);
      if (pace.HasValue) paces.Add(pace.Value.TotalSeconds);
    }

    if (paces.Count == 0) return null;

    paces.Sort();
    var middle = paces.Count / 2;

    return TimeSpan.FromSeconds(paces.Count % 2 == 1
      ? paces[middle]
      : (paces[middle - 1] + paces[middle]) / 2);
  }

  /// <summary>A pace we are willing to divide by, or null.</summary>
  public static TimeSpan? UsablePace(TimeSpan? pace)
  {
    if (pace is not { } p) return null;

    var seconds = p.TotalSeconds;
    return seconds >= MinPlausibleLapSeconds && seconds <= MaxPlausibleLapSeconds ? p : null;
  }

  // ---- Placement -----------------------------------------------------------

  private static TrackPosition Retired(in RiderMapDatum rider, in TrackFrame track, TimeSpan? pace)
  {
    // Frozen where the clock says they stopped, not swept round to "now". A
    // retired rider's true position is unknowable, but "somewhere around here
    // when we last heard from them" is a real search hint for a marshal, and it
    // is the only answer that does not keep changing after the fact.
    if (rider.DnfTime is not { } dnfTime || pace is null || rider.TotalLaps == 0)
      return AtTheLine(rider, track, TrackPositionState.Retired, TimeSpan.Zero, pace);

    var elapsed = Max(TimeSpan.Zero, dnfTime - rider.LastCrossing);
    var progress = Math.Min(1.0, elapsed.TotalSeconds / pace.Value.TotalSeconds);

    return Place(rider, track, progress, TrackPositionState.Retired, elapsed, pace);
  }

  private static TrackPosition AtTheLine(
    in RiderMapDatum rider, in TrackFrame track, TrackPositionState state,
    TimeSpan sinceLastCrossing, TimeSpan? pace) =>
    Place(rider, track, 0, state, sinceLastCrossing, pace);

  private static TrackPosition Place(
    in RiderMapDatum rider, in TrackFrame track, double progress,
    TrackPositionState state, TimeSpan sinceLastCrossing, TimeSpan? pace)
  {
    // Clamping is what parks an overdue rider ON the line rather than letting the
    // dot sail past it. That is the only position consistent with the evidence:
    // we know they have not crossed, so they cannot be beyond it; we do not know
    // where they are, so we cannot honestly place them short of it either.
    var drawFraction = Math.Clamp(progress, 0.0, 1.0);

    var trackFraction = TrackGeometry.NormaliseFraction(track.StartFinishFraction + drawFraction);
    var point = track.Geometry.PointAtFraction(trackFraction);

    return new TrackPosition(
      rider.TagId,
      point.Location,
      point.HeadingDegrees,
      progress,
      drawFraction,
      trackFraction,
      TrackGeometry.SectorIndexAt(trackFraction, track.Sectors),
      state,
      sinceLastCrossing,
      pace);
  }

  private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
