namespace CrossMgrInterface;

/// <summary>
/// Wiring between Form1 and <see cref="RaceCorrectionService"/>: opening the
/// correction dialog, and putting the race back into a consistent state
/// afterwards.
/// </summary>
public partial class Form1
{
  private RaceCorrectionService _corrections = null!;

  /// <summary>
  /// Stray transponder -> the transponder it should be counted as.
  ///
  /// Without this, every later read of a merged transponder would create a fresh
  /// unknown rider and the operator would have to merge again, lap after lap.
  /// </summary>
  private readonly Dictionary<string, string> tagAliases = new();

  private void InitializeCorrections()
  {
    _corrections = new RaceCorrectionService(riders, ridersLock, () => raceStartTime, AddMessage);
    _corrections.CorrectionApplied += RefreshAfterCorrection;
  }

  /// <summary>
  /// Opens the identify-transponder flow for a rider with no name attached.
  /// </summary>
  private void OpenAssignTag(string? tagId)
  {
    if (string.IsNullOrEmpty(tagId)) return;

    int lapsRecorded;
    List<RiderInfo> activeRiders;
    lock (ridersLock)
    {
      if (!riders.TryGetValue(tagId, out var rider)) return;
      lapsRecorded = rider.TotalLaps;
      activeRiders = riders.Values.Where(r => !ignoredTags.Contains(r.TagID)).ToList();
    }

    // Roster entries with no laps yet are the likely match, so surface them first.
    var trackedTags = activeRiders.Select(r => r.TagID).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var roster = _riderDataImporter.GetAllRiderData().Values
      .Select(d => new RiderImportRosterEntry(
        d.TagID, d.RiderNumber, d.FirstName, d.LastName, d.Team, d.Category)
      {
        Unused = !trackedTags.Contains(d.TagID)
      })
      .OrderByDescending(e => e.Unused)
      .ThenBy(e => e.RiderNumber)
      .ToList();

    using var dialog = new AssignTagDialog(tagId, lapsRecorded, roster, activeRiders);
    if (dialog.ShowDialog(this) != DialogResult.OK) return;

    var result = _corrections.AssignTag(tagId, dialog.Request, minimumLapTime);
    if (!result.Ok)
    {
      MessageBox.Show(this, result.Error, "Could not identify that transponder",
        MessageBoxButtons.OK, MessageBoxIcon.Warning);
      return;
    }

    // Route later reads of the stray transponder to the rider it was merged into.
    if (result.Command != null)
    {
      lock (ridersLock)
      {
        foreach (var (from, to) in result.Command.AliasesAdded)
          tagAliases[from] = to;
      }
    }

    PopulateClassFilter();
  }

  /// <summary>Opens the correction dialog for one rider.</summary>
  private void OpenLapCorrection(string? tagId)
  {
    if (string.IsNullOrEmpty(tagId)) return;

    using var dialog = new LapCorrectionDialog(
      _corrections,
      tagId,
      LookupRider,
      GetRejectedReadsFor,
      () => raceStartTime);

    dialog.ShowDialog(this);
  }

  private RiderInfo? LookupRider(string tagId)
  {
    lock (ridersLock)
    {
      return riders.TryGetValue(tagId, out var rider) ? rider : null;
    }
  }

  private IReadOnlyList<RejectedRead> GetRejectedReadsFor(string tagId)
  {
    lock (ridersLock)
    {
      return rejectedReads.Where(r => r.TagID == tagId).ToList();
    }
  }

  /// <summary>
  /// Puts everything that depends on a rider's laps back in step after a
  /// correction. Order matters here; see the comments on each step.
  /// </summary>
  private void RefreshAfterCorrection(IReadOnlyList<string> affectedTags)
  {
    if (InvokeRequired)
    {
      BeginInvoke(new Action<IReadOnlyList<string>>(RefreshAfterCorrection), affectedTags);
      return;
    }

    List<RiderInfo> affected;
    List<RiderInfo> standings;

    lock (ridersLock)
    {
      affected = affectedTags
        .Where(riders.ContainsKey)
        .Select(t => riders[t])
        .ToList();

      // 1. A correction can turn a long lap into two normal ones, or the other
      //    way round, so the missed-read warnings have to be re-derived. Laps the
      //    operator explicitly kept are skipped, or dismissing one would be undone
      //    by the very next re-scan.
      var globalAverage = CalculateGlobalAverageLapTime();
      foreach (var rider in affected)
        LapAnomalyDetector.Analyze(rider, globalAverage, missedReadSettings);

      // 2. Work out the new standings. The position baseline itself is re-seeded
      //    below, outside this lock: the position check takes positionCheckLock
      //    and then ridersLock, so storing the baseline while already holding
      //    ridersLock would invert that order and could deadlock.
      standings = PositionCalculator.GetSortedRidersFromSnapshot(
        riders.Values.Where(r => !ignoredTags.Contains(r.TagID)).ToList());

      // 3. A rider's lap allowance was frozen when the leader finished. If a
      //    correction changed their lap count, the allowance has to move with it
      //    or they are cut short - or allowed an extra lap.
      if (waitingForFinalLaps)
      {
        foreach (var rider in affected.Where(r => r.FinalAllowedLap != int.MaxValue))
          rider.FinalAllowedLap = rider.TotalLaps + 1;
      }
    }

    // Re-seed the baseline the position check compares against. Stale values
    // here make the next real crossing announce a flood of passes and lappings
    // that never happened.
    lock (positionCheckLock)
    {
      StoreCurrentStandings(standings);
    }

    // 4. Persist synchronously. A correction must be durable before the operator
    //    moves on; the live crossing path can be fire-and-forget, this cannot.
    if (currentRaceId.HasValue)
    {
      lock (ridersLock)
      {
        foreach (var rider in affected)
        {
          try
          {
            _raceDb.UpsertRider(rider);
            _raceDb.ReplaceRiderLaps(rider.TagID, rider.Laps,
              _ => PositionCalculator.CalculateCurrentPosition(rider.TagID, riders));
          }
          catch (Exception ex)
          {
            AddDiagnostic($"Could not save the correction for {rider.Label}: {ex.Message}");
          }
        }
      }
    }

    // 5. Race-finish state. Adding a lap to the leader can complete the race;
    //    removing one never un-finishes a race that has already been called.
    if (!raceFinished)
    {
      if (waitingForLeaderFinish && targetLapsToFinishRace > 0)
      {
        var leaderReachedTarget = standings
          .Any(r => !r.IsDNF && r.TotalLaps >= targetLapsToFinishRace);

        if (leaderReachedTarget)
          FinishRace();
      }

      // Marking the last straggler DNF by hand is how an operator closes out a
      // race that would otherwise wait for the timeout, so re-check immediately.
      if (waitingForFinalLaps)
        CheckIfAllFinalLapsCompleted();
    }
    else
    {
      AddDiagnostic("Correction applied after the race was called - the results sheet changes, the race does not restart.");
    }

    // 6. Repaint straight away: the operator is watching the standings behind
    //    the dialog to see what their change did.
    _refresh.RenderNow(RaceViewKind.All);
  }

  /// <summary>
  /// Opens the missed-read detection settings, then re-scans every rider.
  ///
  /// The re-scan matters: the flags already on the laps were worked out under
  /// the old values, so without it the change appears to do nothing until the
  /// next crossing arrives.
  /// </summary>
  private void ShowMissedReadSettings()
  {
    using var dialog = new MissedReadSettingsDialog(missedReadSettings);
    if (dialog.ShowDialog(this) != DialogResult.OK) return;
    if (dialog.Result == missedReadSettings) return;

    missedReadSettings = dialog.Result;
    RememberRaceSetup();

    int flagged;
    lock (ridersLock)
    {
      var globalAverage = CalculateGlobalAverageLapTime();
      foreach (var rider in riders.Values)
        LapAnomalyDetector.Analyze(rider, globalAverage, missedReadSettings);

      flagged = riders.Values.Sum(r => r.Laps.Count(l => l.IsSuggestedForSplit));
    }

    AddMessage($"⚙️ Missed read detection: long lap at {missedReadSettings.MinRatio:0.0}x pace, " +
               $"judged after {missedReadSettings.MinPriorLaps} lap(s). " +
               $"{flagged} lap(s) now flagged.");

    _refresh.RenderNow(RaceViewKind.All);
  }
}
