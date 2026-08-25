namespace CrossMgrInterface;

/// <summary>
/// Track map plumbing for Form1: the snapshot the map paints from, the refresh
/// adapter that animates it, and the wiring to the circuit store.
///
/// Kept in its own partial for the same reason as Form1.Views.cs - Form1.cs is
/// already very large, and the designer rewrites Form1.Designer.cs wholesale.
/// </summary>
public partial class Form1
{
  private TrackTabView _trackTab = null!;
  private TabPage tabPageTrack = null!;
  private TrackStore _trackStore = null!;
  private TrackDefinition? _currentTrack;

  /// <summary>
  /// Scalar projection of the field, taken once per frame under ridersLock.
  ///
  /// Scalars only, deliberately. CloneRiderForDisplay deep-copies every lap of
  /// every rider - at 250 riders and twenty laps that is five thousand objects,
  /// which is not affordable eight times a second. Published by a single
  /// reference assignment, which is atomic, so a crossing landing mid-paint
  /// simply shows up on the next frame.
  /// </summary>
  private RiderMapDatum[] _trackSnapshot = Array.Empty<RiderMapDatum>();

  private Dictionary<string, int> _trackRanks = new();
  private RaceTiming _trackTiming;

  private readonly List<TrackPosition> _trackPositions = new();
  private readonly List<MapRiderMarker> _trackMarkers = new();

  private void InitializeTrackView()
  {
    _trackStore = TrackStore.Load();
    if (_trackStore.RecoveredFrom is { } quarantined)
      AddDiagnostic($"tracks.json could not be read and was set aside as {quarantined}.");

    _trackTab = new TrackTabView(
      TileProvider.ById(_settings.TileProviderId), _settings.TrackLabelParts, AddDiagnostic)
    {
      DescribeRider = DescribeRiderForMap
    };

    _trackTab.SetupRequested += (_, _) => OpenTrackEditor(_currentTrack);
    _trackTab.NewTrackRequested += (_, _) => OpenTrackEditor(null);
    _trackTab.TrackChosen += (_, id) => SelectTrack(id);
    _trackTab.RenameRequested += (_, _) => RenameCurrentTrack();
    _trackTab.DeleteRequested += (_, _) => DeleteCurrentTrack();
    _trackTab.ExportRequested += (_, _) => ExportCurrentTrack();

    _trackTab.LabelPartsChanged += (_, parts) =>
    {
      _settings.TrackLabelParts = parts;
      _settings.Save();
    };

    _trackTab.BasemapChosen += (_, provider) =>
    {
      _settings.TileProviderId = provider.Id;
      _settings.Save();

      AddMessage($"Track map basemap: {provider.Name}." +
                 (provider.Caveat is { } caveat ? $" {caveat}" : ""));
    };

    // The same gesture already means this from the lap chart.
    _trackTab.RiderActivated += (_, tagId) => OpenLapCorrection(tagId);

    tabPageTrack = _trackTab.CreateTrackTab();

    // Resolution order: whatever was last shown, then the only circuit there is,
    // then nothing. A club with one circuit should never be asked to pick it.
    var resolved = _trackStore.Find(_settings.LastTrackId) ?? _trackStore.Only;
    ApplyTrack(resolved);

    _trackTab.SetTracks(_trackStore.Tracks, _currentTrack?.Id);
    _trackTab.SetClasses(AvailableClasses());
  }

  private void SelectTrack(string id)
  {
    var track = _trackStore.Find(id);
    if (track is null || ReferenceEquals(track, _currentTrack)) return;

    ApplyTrack(track);
    _refresh?.RenderNow(RaceViewKind.Track);
  }

  private void ApplyTrack(TrackDefinition? track)
  {
    _currentTrack = track;
    _trackTab.SetTrack(track);

    AddDiagnostic(track is null
      ? "Track map: no circuit selected."
      : $"Track map: \"{track.Name}\" - {track.Points.Count} points, {track.LengthMetres:F0}m, " +
        $"{track.Sectors.Count} sectors, start/finish at {track.StartFinish.Fraction * 100:F0}%. " +
        $"Camera {_trackTab.Renderer.Viewport.Zoom}z on " +
        $"{_trackTab.Renderer.Viewport.Center.Lat:F5},{_trackTab.Renderer.Viewport.Center.Lon:F5}.");

    _settings.LastTrackId = track?.Id;
    _settings.Save();
  }

  /// <summary>
  /// Opens the circuit editor.
  ///
  /// Refuses mid-race without an explicit confirmation. A modal does not stop
  /// reading or lap recording - those run on the network thread - but it does
  /// stop the UI pump, so the clock and the leaderboard freeze while it is open.
  /// Same shape as the guard on starting a new race.
  /// </summary>
  private void RenameCurrentTrack()
  {
    if (_currentTrack is null) return;

    var name = TextPrompt.Ask(this, "Rename circuit", _currentTrack.Name, "What should this circuit be called?");
    if (name is null || name == _currentTrack.Name) return;

    var previous = _currentTrack.Name;
    _currentTrack.Name = name;

    if (!SaveTracks($"renaming \"{previous}\""))
    {
      _currentTrack.Name = previous;
      return;
    }

    _trackTab.SetTracks(_trackStore.Tracks, _currentTrack.Id);
    AddMessage($"Circuit \"{previous}\" renamed to \"{name}\".");
  }

  private void DeleteCurrentTrack()
  {
    if (_currentTrack is null) return;

    // Naming the circuit in the prompt rather than saying "this circuit": a
    // surveyed loop is a season's worth of someone's work, and the operator
    // should be able to see which one they are about to lose.
    var confirm = MessageBox.Show(this,
      $"Delete the circuit \"{_currentTrack.Name}\"?\n\n" +
      $"{_currentTrack.Points.Count} points, {_currentTrack.LengthMetres:F0}m, " +
      $"{_currentTrack.Sectors.Count} sectors.\n\nThis cannot be undone.",
      "Delete circuit", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

    if (confirm != DialogResult.Yes) return;

    var deleted = _currentTrack;
    _trackStore.Remove(deleted.Id);

    if (!SaveTracks($"deleting \"{deleted.Name}\""))
    {
      _trackStore.AddOrUpdate(deleted);
      return;
    }

    ApplyTrack(_trackStore.Tracks.FirstOrDefault());
    _trackTab.SetTracks(_trackStore.Tracks, _currentTrack?.Id);

    AddMessage($"Circuit \"{deleted.Name}\" deleted.");
    _refresh?.RenderNow(RaceViewKind.Track);
  }

  /// <summary>
  /// Writes the circuit out for another machine or another tool.
  ///
  /// Two formats that are not equivalent, so the confirmation says which one was
  /// actually written rather than trusting the filter name to have been read:
  /// GPX carries the shape and is what other software understands, while the
  /// circuit file also carries the start/finish line and the sectors.
  /// </summary>
  private void ExportCurrentTrack()
  {
    if (_currentTrack is not { IsUsable: true } track) return;

    var safeName = string.Join("_", track.Name.Split(Path.GetInvalidFileNameChars()));
    if (string.IsNullOrWhiteSpace(safeName)) safeName = "circuit";

    using var save = new SaveFileDialog
    {
      Title = "Export circuit",
      FileName = safeName,
      Filter = "GPX track (*.gpx)|*.gpx|CrossMgr circuit, keeps sectors (*.cmtrack)|*.cmtrack",
      DefaultExt = "gpx",
      AddExtension = true
    };

    if (save.ShowDialog(this) != DialogResult.OK) return;

    var asCircuitFile = Path.GetExtension(save.FileName)
      .Equals(TrackGpxExporter.CircuitFileExtension, StringComparison.OrdinalIgnoreCase);

    try
    {
      if (asCircuitFile) TrackGpxExporter.SaveCircuitFile(track, save.FileName);
      else TrackGpxExporter.SaveGpx(track, save.FileName);
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, $"The circuit could not be written: {ex.Message}",
        "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
      return;
    }

    AddMessage($"Circuit \"{track.Name}\" exported to {Path.GetFileName(save.FileName)}.");

    var caveat = asCircuitFile
      ? "Sectors and the start/finish line are all preserved."
      : "GPX carries the shape of the loop. It has no way to record a start/finish " +
        "line or sectors, so those are written as waypoints for reference only and " +
        "will need setting again after importing.";

    MessageBox.Show(this, $"Saved to {Path.GetFileName(save.FileName)}.\n\n{caveat}",
      "Circuit exported", MessageBoxButtons.OK, MessageBoxIcon.Information);
  }

  /// <summary>Writes tracks.json, reporting rather than throwing. False means the caller should roll back.</summary>
  private bool SaveTracks(string what)
  {
    try
    {
      _trackStore.Save();
      return true;
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, $"The circuit list could not be saved while {what}: {ex.Message}",
        "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
      return false;
    }
  }

  /// <param name="existing">The circuit to edit, or null to draw a new one.</param>
  private void OpenTrackEditor(TrackDefinition? existing)
  {
    if (raceStarted && !raceFinished)
    {
      var proceed = MessageBox.Show(this,
        "A race is running.\n\nSetting up a circuit stops the clock display and the " +
        "leaderboard until you close it. Transponder reads carry on being recorded.\n\n" +
        "Open the circuit editor anyway?",
        "Race in progress", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

      if (proceed != DialogResult.Yes) return;
    }

    var viewport = _trackTab.Renderer.Viewport;

    using var editor = new TrackEditorDialog(
      _trackStore, existing, _trackTab.Provider, AddDiagnostic,
      (viewport.Center, viewport.Zoom));

    var outcome = editor.ShowDialog(this);

    // Carried back even on Cancel: switching basemap is a view preference, not
    // part of the edit being thrown away.
    _trackTab.SetBasemap(editor.Provider);

    if (outcome != DialogResult.OK || editor.Result is null) return;

    ApplyTrack(editor.Result);
    _trackTab.SetTracks(_trackStore.Tracks, _currentTrack?.Id);

    AddMessage($"Circuit \"{editor.Result.Name}\" saved: " +
               $"{editor.Result.LengthMetres:F0}m, {editor.Result.Sectors.Count} sectors.");

    _refresh?.RenderNow(RaceViewKind.Track);
  }

  /// <summary>Takes a consistent copy of the field for the map, then re-solves it.</summary>
  private void RefreshTrackSnapshot()
  {
    RiderMapDatum[] field;
    var ranks = new Dictionary<string, int>();
    bool started, finished;

    lock (ridersLock)
    {
      var live = riders.Values.Where(r => !ignoredTags.Contains(r.TagID)).ToList();

      // RacingPace walks the lap list, so it has to be evaluated in here.
      field = live.Select(RiderMapDatum.From).ToArray();

      var sorted = PositionCalculator.GetSortedRidersFromSnapshot(live);
      for (var i = 0; i < sorted.Count; i++) ranks[sorted[i].TagID] = i + 1;

      started = raceStarted;
      finished = raceFinished;
    }

    _trackRanks = ranks;
    _trackTiming = new RaceTiming(started, finished, TrackPositionSolver.FieldMedianPace(field));
    _trackSnapshot = field;
  }

  /// <summary>Solves the snapshot against the current circuit and hands it to the tab.</summary>
  private void RenderTrackMap()
  {
    // Reads the PREVIOUS frame's paint cost, which is the only place it is
    // visible - the coordinator times Render, not OnPaint.
    ReportTrackPaintCost();

    var field = _trackSnapshot;
    var frame = TrackFrame.From(_currentTrack);

    // One clock for the whole frame, so the field cannot drift apart while the
    // loop runs.
    TrackPositionSolver.SolveAll(field, DateTime.Now, frame, _trackTiming, _trackPositions);

    _trackMarkers.Clear();

    for (var i = 0; i < _trackPositions.Count && i < field.Length; i++)
    {
      var position = _trackPositions[i];
      var rider = field[i];

      _trackMarkers.Add(new MapRiderMarker(
        rider.TagId,
        rider.RiderNumber,
        rider.Label,
        rider.ShortName,
        rider.Category,
        position.Location,
        position.HeadingDegrees,
        _trackRanks.TryGetValue(rider.TagId, out var rank) ? rank : 0,
        position.State,
        position.Fraction,
        BadgeFor(position),
        Highlighted: false));
    }

    _trackTab.SetSectorInfo(SummariseSectors());
    _trackTab.SetField(_trackMarkers);
    _trackTab.SetLeaderboard(LeaderboardRows());

    // Rebuilt every frame, not captured on click. A card that froze at the moment
    // the dot was clicked would keep claiming "40% through the lap" while the
    // rider went round again - and the overdue count, which is the number worth
    // watching, would never move at all.
    if (_trackTab.Renderer.SelectedTagId is { } selected)
      _trackTab.Renderer.Callout = DescribeRiderForMap(selected);
    _trackTab.SetWatermark(TrackWatermark());
    _trackTab.Invalidate();
  }

  private readonly List<MapSectorInfo> _sectorInfo = new();
  private readonly List<TrackLeaderRow> _leaderboard = new();

  /// <summary>
  /// The riders currently passing the field filter, in running order.
  ///
  /// Only built when a top-N filter is on: with the whole field showing, a list
  /// of 250 rows is the leaderboard tab rather than something worth putting
  /// beside the map.
  /// </summary>
  private IReadOnlyList<TrackLeaderRow> LeaderboardRows()
  {
    _leaderboard.Clear();

    var limit = _trackTab.LeaderboardLimit;
    if (limit <= 0) return _leaderboard;

    for (var i = 0; i < _trackPositions.Count && i < _trackSnapshot.Length; i++)
    {
      var rider = _trackSnapshot[i];

      // The same limit the map applies. This is built from the whole solved
      // field, so without the check the list shows everybody.
      if (!_trackRanks.TryGetValue(rider.TagId, out var rank) || rank <= 0 || rank > limit) continue;

      _leaderboard.Add(new TrackLeaderRow(
        rank,
        rider.TagId,
        rider.RiderNumber,
        rider.ShortName.Length > 0 ? rider.ShortName : rider.Label,
        rider.TotalLaps,
        ListStateText(_trackPositions[i])));
    }

    _leaderboard.Sort((a, b) => a.Position.CompareTo(b.Position));
    return _leaderboard;
  }

  /// <summary>
  /// Status for a table row, which needs to be STABLE in a way the map callout
  /// does not.
  ///
  /// The callout can say "38% through the lap" because it belongs to one rider
  /// the operator is deliberately watching. Putting that in a table makes every
  /// row rewrite itself several times a second, which reads as flicker rather
  /// than as information. A sector name changes only on a boundary crossing, and
  /// an overdue count changes once a second - both are worth reading.
  /// </summary>
  private string ListStateText(TrackPosition position) => position.State switch
  {
    TrackPositionState.OnTrack when _currentTrack is { Sectors.Count: > 0 } track && position.SectorIndex >= 0 =>
      track.SectorNameAt(position.TrackFraction),
    TrackPositionState.OnTrack => "On track",
    TrackPositionState.Overdue or TrackPositionState.LongOverdue =>
      position.Pace is { } pace
        ? $"+{(position.SinceLastCrossing - pace).TotalSeconds:F0}s overdue"
        : "Overdue",
    TrackPositionState.NoPrediction => "No pace yet",
    TrackPositionState.OnGrid => "Not away",
    TrackPositionState.Retired => "Retired",
    TrackPositionState.DidNotStart => "Did not start",
    TrackPositionState.Finished => "Finished",
    _ => ""
  };

  /// <summary>
  /// How many riders are in each sector, and who leads it.
  ///
  /// Counts only riders who are actually circulating: a retired rider frozen
  /// where they stopped, or a finisher parked on the line, is not "in" a sector
  /// in any sense a marshal would recognise, and including them would quietly
  /// inflate every count as the race wore on.
  /// </summary>
  private IReadOnlyList<MapSectorInfo> SummariseSectors()
  {
    _sectorInfo.Clear();

    if (_currentTrack is not { Sectors.Count: > 0 } track) return _sectorInfo;

    var counts = new int[track.Sectors.Count];
    var bestRank = new int[track.Sectors.Count];
    var leader = new string?[track.Sectors.Count];
    Array.Fill(bestRank, int.MaxValue);

    for (var i = 0; i < _trackPositions.Count && i < _trackSnapshot.Length; i++)
    {
      var position = _trackPositions[i];
      if (position.State is not (TrackPositionState.OnTrack or TrackPositionState.Overdue)) continue;

      var sector = position.SectorIndex;
      if (sector < 0 || sector >= counts.Length) continue;

      counts[sector]++;

      var rank = _trackRanks.TryGetValue(position.TagId, out var r) ? r : int.MaxValue;
      if (rank <= 0 || rank >= bestRank[sector]) continue;

      bestRank[sector] = rank;
      leader[sector] = _trackSnapshot[i].RiderNumber;
    }

    for (var i = 0; i < track.Sectors.Count; i++)
    {
      var name = string.IsNullOrWhiteSpace(track.Sectors[i].Name) ? $"Sector {i + 1}" : track.Sectors[i].Name;
      _sectorInfo.Add(new MapSectorInfo(i, name, track.Sectors[i].Color, counts[i], leader[i]));
    }

    return _sectorInfo;
  }

  /// <summary>
  /// The seconds a rider is overdue by. This is the whole reason the solver keeps
  /// the true fraction alongside the clamped one - the dot stops at the line, but
  /// the number carries on telling the truth.
  /// </summary>
  private static string? BadgeFor(TrackPosition position)
  {
    if (position.State is not (TrackPositionState.Overdue or TrackPositionState.LongOverdue)) return null;
    if (position.Pace is not { } pace) return null;

    var overdueBy = position.SinceLastCrossing - pace;
    if (overdueBy <= TimeSpan.Zero) return null;

    return overdueBy.TotalSeconds < 90
      ? $"+{overdueBy.TotalSeconds:F0}s"
      : $"+{overdueBy.TotalMinutes:F0}m";
  }

  private string? TrackWatermark()
  {
    if (_currentTrack is null) return null;
    if (!raceStarted) return "Race not started";
    return raceFinished ? "Race finished" : null;
  }

  /// <summary>
  /// The selection card. Locks briefly - a click is rare, and the alternative is
  /// showing details a frame out of date.
  /// </summary>
  private IReadOnlyList<string> DescribeRiderForMap(string tagId)
  {
    var lines = new List<string>();

    lock (ridersLock)
    {
      if (!riders.TryGetValue(tagId, out var rider)) return lines;

      lines.Add(rider.Label);

      if (!string.IsNullOrWhiteSpace(rider.Category)) lines.Add(rider.Category);
      if (!string.IsNullOrWhiteSpace(rider.Team)) lines.Add(rider.Team);

      lines.Add($"Lap {rider.TotalLaps}" +
                (rider.LastLapTime is { } last ? $"  -  last {last.TotalSeconds:F1}s" : ""));

      if (rider.RacingPace is { } pace) lines.Add($"Typical lap {pace.TotalSeconds:F1}s");
      if (_trackRanks.TryGetValue(tagId, out var rank) && rank > 0) lines.Add($"Position {rank}");
    }

    var position = _trackPositions.FirstOrDefault(p => p.TagId == tagId);
    if (position.TagId == tagId)
    {
      lines.Add(StateText(position));

      if (_currentTrack is { Sectors.Count: > 0 } && position.SectorIndex >= 0)
        lines.Add($"In {_currentTrack.SectorNameAt(position.TrackFraction)}");
    }

    return lines;
  }

  private static string StateText(TrackPosition position) => position.State switch
  {
    TrackPositionState.OnTrack => $"{position.Fraction * 100:F0}% through the lap",
    TrackPositionState.Overdue or TrackPositionState.LongOverdue =>
      position.Pace is { } pace
        ? $"{(position.SinceLastCrossing - pace).TotalSeconds:F0}s overdue"
        : "Overdue",
    TrackPositionState.NoPrediction => "No pace yet",
    TrackPositionState.OnGrid => "Not away yet",
    TrackPositionState.Retired => "Retired",
    TrackPositionState.DidNotStart => "Did not start",
    TrackPositionState.Finished => "Finished",
    _ => ""
  };

  // ---- View adapter --------------------------------------------------------

  /// <summary>
  /// Drives the map from the refresh coordinator.
  ///
  /// The dots move with the wall clock even when no data changes, and one frame a
  /// second is not motion - a 1.5km loop at a 60s lap covers about twenty pixels
  /// a second at zoom 17, so at 1Hz the display looks broken rather than live.
  ///
  /// Rather than bolt on a second timer that would fight the coordinator's dirty
  /// bits and its backoff, the view declares WantsContinuousRepaint while the
  /// race is running. The existing 125ms pump then supplies about eight frames a
  /// second, and every one of them still goes through the coordinator's own
  /// instrumentation and cooldown.
  ///
  /// A hidden tab costs nothing at all: Flush skips invisible views before it
  /// renders, and selecting the tab picks the chain back up through OnTabChanged.
  /// </summary>
  private sealed class TrackMapViewAdapter : IRaceView
  {
    private readonly Form1 _form;
    public TrackMapViewAdapter(Form1 form) => _form = form;

    public RaceViewKind Kind => RaceViewKind.Track;
    public TabPage? HostTab => _form.tabPageTrack;
    public bool NeedsHeartbeat => true;

    /// <summary>
    /// Only while there is something to animate. Before the start and after the
    /// finish every dot is frozen on the line, so the once-a-second heartbeat is
    /// plenty and the pump goes back to idling.
    /// </summary>
    public bool WantsContinuousRepaint => _form.raceStarted && !_form.raceFinished;

    public void Render()
    {
      _form.RefreshTrackSnapshot();
      _form.RenderTrackMap();
    }

    /// <summary>
    /// Reached only when the view is not dirty, which means nothing is animating.
    /// Re-solves against a fresh clock so the watermark and any overdue badges
    /// stay honest, without re-reading the field under the lock.
    /// </summary>
    public void RenderHeartbeat() => _form.RenderTrackMap();
  }

  private double _paintTotalUs;
  private double _paintMaxUs;
  private int _paintFrames;
  private DateTime _lastPaintReport = DateTime.MinValue;

  /// <summary>
  /// The refresh coordinator times Render - taking the snapshot and solving - but
  /// not OnPaint, which is where the drawing actually happens and where the cost
  /// of a big field lands. Without this the map could be the slowest thing on the
  /// UI thread and the render log would still read zero.
  ///
  /// Reported on the same thirty-second cadence as the coordinator's own summary,
  /// alongside how many dots survived clustering - which is what says whether a
  /// large field is still legible rather than merely fast.
  /// </summary>
  private void ReportTrackPaintCost()
  {
    if (_trackTab is null) return;

    var paint = _trackTab.LastPaintMicroseconds;
    _paintFrames++;
    _paintTotalUs += paint;
    if (paint > _paintMaxUs) _paintMaxUs = paint;

    if ((DateTime.Now - _lastPaintReport).TotalSeconds < 30) return;

    if (_lastPaintReport != DateTime.MinValue && _paintFrames > 0)
    {
      var renderer = _trackTab.Renderer;

      // "past filters" rather than "shown": the dot counts are what actually
      // reached the screen, and anything off the edge of the viewport is culled
      // after filtering. Conflating the two made the map look busier than it was.
      AddDiagnostic(
        $"Track map paint (last 30s): x{_paintFrames} avg {_paintTotalUs / _paintFrames / 1000:F1}ms " +
        $"max {_paintMaxUs / 1000:F1}ms | drew {renderer.LastDotCount} dots, " +
        $"{renderer.LastClusterCount} clusters, {renderer.LastLabelCount} labels | " +
        $"{_trackMarkers.Count} of {_trackSnapshot.Length} riders past filters");
    }

    _lastPaintReport = DateTime.Now;
    _paintFrames = 0;
    _paintTotalUs = 0;
    _paintMaxUs = 0;
  }
}
