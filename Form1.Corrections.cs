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
    _corrections = new RaceCorrectionService(riders, ridersLock, () => raceStartTime, AddMessage, tagAliases);
    _corrections.CorrectionApplied += RefreshAfterCorrection;
  }

  /// <summary>
  /// Opens the identify-transponder flow for a rider with no name attached.
  /// </summary>
  private void OpenAssignTag(string? tagId)
  {
    if (string.IsNullOrEmpty(tagId)) return;

    int lapsRecorded;
    bool isTeam;
    List<RiderInfo> activeRiders;
    lock (ridersLock)
    {
      if (!riders.TryGetValue(tagId, out var rider)) return;
      lapsRecorded = rider.TotalLaps;
      isTeam = rider.IsTeam;
      activeRiders = riders.Values.Where(r => !ignoredTags.Contains(r.TagID)).ToList();
    }

    if (isTeam)
    {
      MessageBox.Show(this,
        "This is a team, not a transponder. Its riders come from the rider list - to change them, " +
        "correct the list and import it again.",
        "Identify transponder", MessageBoxButtons.OK, MessageBoxIcon.Information);
      return;
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

    // In a team event the transponder may be a team rider's: a spare, or one read
    // before the rider list was loaded. Teams that have not crossed yet count too.
    var teamChoices = new List<TeamJoinChoice>();
    (string Key, int MemberIndex)? suggestion = null;
    if (teamEvent)
    {
      var teams = _teams;
      foreach (var team in teams.Teams)
      {
        var live = activeRiders.FirstOrDefault(r => r.TagID == team.Key);
        teamChoices.Add(new TeamJoinChoice(team.Key, live ?? team.ToRiderInfo(), live != null));
      }

      if (teams.EntryKeyFor(tagId) is { } key && teamChoices.FirstOrDefault(c => c.Key == key) is { } owner)
        suggestion = (key, owner.Template.Members?.ToList().FindIndex(m => m.Owns(tagId)) ?? -1);
    }

    using var dialog = new AssignTagDialog(tagId, lapsRecorded, roster, activeRiders, teamChoices, suggestion);
    if (dialog.ShowDialog(this) != DialogResult.OK) return;

    var result = _corrections.AssignTag(tagId, dialog.Request, minimumLapTime);
    if (!result.Ok)
    {
      MessageBox.Show(this, result.Error, "Could not identify that transponder",
        MessageBoxButtons.OK, MessageBoxIcon.Warning);
      return;
    }

    // Later reads of the stray transponder are routed by the correction service,
    // which takes the route away again on undo. A transponder joined to a team
    // reaches the roster through RefreshAfterCorrection, on undo and redo too.

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
      () => raceStartTime,
      ChangeRiderClass);

    dialog.ShowDialog(this);
  }

  /// <summary>
  /// Moves a rider into another class, from the Fix laps window. Returns whether
  /// anything changed.
  ///
  /// The window asks; everything that decides the answer lives here - the classes
  /// on offer, the wave schedule, and whether this entry is a team at all.
  /// </summary>
  private bool ChangeRiderClass(string tagId)
  {
    string current;
    bool isTeam;
    lock (ridersLock)
    {
      if (!riders.TryGetValue(tagId, out var rider)) return false;
      current = rider.Category;
      isTeam = rider.IsTeam;
    }

    // Same reason the Identify window gives: a team's class is worked out from
    // its members' classes, so it is the list that decides it, not this window.
    if (isTeam)
    {
      MessageBox.Show(this,
        "This is a team. Its class comes from the classes of its riders on the rider list - to change " +
        "it, correct the list and import it again.",
        "Change class", MessageBoxButtons.OK, MessageBoxIcon.Information);
      return false;
    }

    var caption = waves == null
      ? "Which class should this rider be scored in?"
      : "Which class should this rider be scored in? They keep the start time they " +
        "already have, so their lap times do not change.";

    var chosen = TextPrompt.Ask(this, "Change class", current, caption, AvailableClasses());
    if (chosen == null) return false;

    // Match an existing class's spelling rather than adding one that differs
    // only in case: the riders grid filters on an exact match, so "mx1" beside
    // "MX1" would be two classes on screen and one everywhere else.
    chosen = AvailableClasses()
      .FirstOrDefault(c => string.Equals(c, chosen, StringComparison.OrdinalIgnoreCase)) ?? chosen;

    if (string.Equals(chosen, current, StringComparison.Ordinal)) return false;

    // A read from a class still at the gate is thrown away, so moving a rider
    // into one would quietly stop counting their laps until it goes.
    if (waves != null && !waves.HasStarted(chosen))
    {
      MessageBox.Show(this,
        $"{chosen} has not left the gate yet. A rider in a class that has not started is not counted, " +
        "so this would stop their laps being recorded. Wait until that class has started.",
        "Change class", MessageBoxButtons.OK, MessageBoxIcon.Warning);
      return false;
    }

    var result = _corrections.SetRiderClass(tagId, chosen);
    if (!result.Ok)
    {
      MessageBox.Show(this, result.Error, "Change class", MessageBoxButtons.OK, MessageBoxIcon.Warning);
      return false;
    }

    return result.Command != null;
  }

  /// <summary>
  /// A copy of one rider, taken under the lock. The Fix laps window reads the
  /// laps while riders are still crossing; handing it the live list let a read on
  /// the network thread change the list while the window was drawing it.
  /// </summary>
  private RiderInfo? LookupRider(string tagId)
  {
    lock (ridersLock)
    {
      return riders.TryGetValue(tagId, out var rider) ? CloneRiderForDisplay(rider) : null;
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

    // A team's riders and their transponders are part of its snapshot, so
    // joining a transponder to a team - or undoing that - changes who owns it.
    // Only the application of the join used to rebuild the roster, so after an
    // undo the transponder still counted for the team.
    if (teamEvent && affectedTags.Any(TeamRoster.IsTeamKey))
      RebuildTeamRoster();

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
      //    Two team riders out at once first, so those laps stay out of the pace
      //    the missed-read detector measures against.
      var fieldPace = FieldPace();
      foreach (var rider in affected.Where(r => r.IsTeam))
        TwoOnTrackDetector.Analyze(rider, fieldPace);

      var globalAverage = CalculateGlobalAverageLapTime();
      foreach (var rider in affected)
        LapAnomalyDetector.Analyze(rider, globalAverage, missedReadSettings);

      // 2. Work out the new standings. The position baseline itself is re-seeded
      //    below, outside this lock: the position check takes positionCheckLock
      //    and then ridersLock, so storing the baseline while already holding
      //    ridersLock would invert that order and could deadlock.
      standings = PositionCalculator.GetSortedRidersFromSnapshot(
        riders.Values.Where(r => !ignoredTags.Contains(r.TagID)).ToList());

      // 3. A rider's lap allowance was fixed when the flag came out, from the laps
      //    they had completed by then. A correction can change that count - a lap
      //    split or added before the flag - so it is worked out again the same
      //    way. It used to be reset to one more than the laps they have now, which
      //    handed a rider who had already ridden their last lap another one: a
      //    missed read split after the flag gave its team a lap, and the win.
      if (waitingForFinalLaps)
      {
        var flagAt = raceEndTime ?? finalLapsStartTime ?? DateTime.Now;
        foreach (var rider in affected.Where(r => r.FinalAllowedLap != int.MaxValue))
          rider.FinalAllowedLap = ChequeredFlag.AllowedLap(rider.LapsCompletedBy(flagAt), targetLapsToFinishRace);
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

        // An entry a correction took away - a transponder merged onto someone, or
        // a team an undo removed again - goes from the database as well, or a
        // restart brings it back beside the laps it gave up.
        foreach (var gone in affectedTags.Where(t => !riders.ContainsKey(t)))
        {
          try
          {
            _raceDb.DeleteRider(gone);
          }
          catch (Exception ex)
          {
            AddDiagnostic($"Could not remove {gone} from the database after a correction: {ex.Message}");
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
        var leader = standings
          .FirstOrDefault(r => !r.IsDNF && r.TotalLaps >= targetLapsToFinishRace);

        // The flag fell when that lap was ridden, not when the operator got
        // round to the correction: everyone else's allowance is counted from
        // it, and dating it now would hand the field laps it never rode.
        if (leader != null)
          FinishRace(leader.Laps.FirstOrDefault(l => l.LapNumber == targetLapsToFinishRace)?.CrossingTime
            ?? leader.LastCrossing);
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

    // 6. The classes on offer may have moved: identifying a transponder can
    //    introduce one, changing a rider's class can introduce or empty one.
    //    Here rather than at the call site, because undo and redo come through
    //    this and nowhere else - which is why undoing an Identify used to leave
    //    a class in the filter that no longer had anyone in it. The track tab
    //    keeps its own list; Qualifying rebuilds its own as it renders.
    PopulateClassFilter();
    _trackTab.SetClasses(AvailableClasses());

    // 7. Repaint straight away: the operator is watching the standings behind
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
