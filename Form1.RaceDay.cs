namespace CrossMgrInterface;

/// <summary>
/// Wiring for the Race Day view: translating the race state machine into the
/// six plain-English states a volunteer sees, and the actions that view offers.
/// </summary>
public partial class Form1
{
  private RaceDayView _raceDayView = null!;
  private TabPage tabPageRaceDay = null!;
  private string raceName = "";

  private void InitializeRaceDayView()
  {
    _raceDayView = new RaceDayView();
    tabPageRaceDay = _raceDayView.CreateRaceDayTab();

    // Every action reuses the handler that already exists behind the old button.
    _raceDayView.StartRaceClicked += (s, e) => buttonStartRace_Click(s, e);
    // The button relabels itself to "Gate pick order..." for a timed session,
    // so it has to lead there too. Sending it to the race report instead prints
    // a race classification of a qualifying session, in which every rider shows
    // DNF - the flag's grace marks everyone who was not still circulating, and
    // in a timed session that means nothing worse than "had already pulled in".
    _raceDayView.ResultsClicked += (s, e) =>
    {
      if (IsQualifying) ShowQualifyingReport();
      else buttonGenerateReport_Click(s, e);
    };
    _raceDayView.FixLapsClicked += (s, e) => OpenLapCorrectionForMostUrgentRider();
    _raceDayView.EndRaceNowClicked += (s, e) => EndRaceNow();
    _raceDayView.SetupClicked += (s, e) => RunNewRaceWizard();
  }

  /// <summary>
  /// The guided setup flow. Each step hands off to the handler that already sits
  /// behind the corresponding control - buttonSetDuration_Click also resets the
  /// countdown warnings and recomputes the race end time, so reimplementing it
  /// here would quietly drop that behaviour.
  /// </summary>
  private void RunNewRaceWizard()
  {
    int existingRiders;
    lock (ridersLock)
    {
      existingRiders = riders.Count;
    }

    if (existingRiders > 0)
    {
      var answer = MessageBox.Show(this,
        $"There are {existingRiders} riders from a race already in progress.\n\n" +
        "Setting up a new race does not delete them - use Race > Delete race data first " +
        "if you want to start clean.\n\nCarry on?",
        "Set up a race", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

      if (answer != DialogResult.Yes) return;
    }

    using var wizard = new NewRaceWizard(
      file => Path.GetExtension(file).Equals(".csv", StringComparison.OrdinalIgnoreCase)
        ? _riderDataImporter.ImportFromCsvDetailed(file)
        : _riderDataImporter.ImportFromExcelDetailed(file),
      _riderDataImporter.Count,
      isListening,
      sessionType);

    if (wizard.ShowDialog(this) != DialogResult.OK) return;

    var setup = wizard.Result;

    // Before the settings handlers below: buttonSetAdditionalLaps_Click and the
    // start-mode radios both read the session type as they go.
    sessionType = setup.SessionType;
    raceName = setup.RaceName;
    Text = string.IsNullOrEmpty(raceName)
      ? "CrossMgr RFID Interface"
      : $"CrossMgr - {raceName}";

    // Push the chosen values through the existing handlers rather than assigning
    // the fields directly.
    numericUpDownRaceDuration.Value = setup.DurationMinutes;
    buttonSetDuration_Click(this, EventArgs.Empty);

    numericUpDownAdditionalLaps.Value = setup.AdditionalLaps;
    buttonSetAdditionalLaps_Click(this, EventArgs.Empty);

    // Setting Checked fires RaceStartMode_CheckedChanged, which owns manualStartMode.
    radioButtonStartManual.Checked = setup.ManualStart;
    radioButtonStartOnFirstTag.Checked = !setup.ManualStart;

    if (setup.ImportedFile != null)
    {
      ApplyImportedDataToExistingRiders();
      PopulateClassFilter();
      AddMessage($"📋 Imported {_riderDataImporter.Count} riders from {Path.GetFileName(setup.ImportedFile)}");
      RememberRiderList(setup.ImportedFile);
    }

    if (setup.StartReader && !isListening)
    {
      StartTcpListener(readerPort);
    }

    ApplySessionTypeToUi();
    RememberRaceSetup();

    var format = sessionType switch
    {
      SessionType.TimedQualifying => "timed qualifying",
      SessionType.FreePractice => "free practice",
      _ => "race"
    };

    AddMessage($"🏁 Ready: {raceName} ({format}) - {setup.DurationMinutes} minutes, " +
               $"{(setup.ManualStart ? "manual start" : "starts on the first rider")}");

    RaiseNotice(NoticeLevel.Info, setup.ManualStart
      ? IsTimedSession
        ? "Set up. Press START SESSION when the gate opens."
        : "Set up. Press START RACE when the gate drops."
      : "Set up. The clock starts on the first rider.");

    tabControl.SelectedTab = tabPageRaceDay;
    _refresh.RenderNow(RaceViewKind.All);
  }

  /// <summary>Repaints the Race Day view from current race state.</summary>
  private void RenderRaceDay()
  {
    List<RiderInfo> sorted;
    int riderCount;
    lock (ridersLock)
    {
      sorted = PositionCalculator.GetSortedRidersFromSnapshot(
        riders.Values.Where(r => !ignoredTags.Contains(r.TagID)).Select(CloneRiderForDisplay).ToList());
      riderCount = sorted.Count;
    }

    var finalElapsed = raceFinished && raceStartTime.HasValue && raceEndTime.HasValue
      ? raceEndTime.Value - raceStartTime.Value
      : (TimeSpan?)null;

    _raceDayView.SetClock(
      raceStarted && !raceFinished ? GetTimeRemaining() : null,
      finalElapsed,
      raceDuration);

    var (state, detail) = DescribeRaceState(sorted, riderCount);
    _raceDayView.SetState(state, detail);

    _raceDayView.SetReaderHealth(
      isListening,
      ConnectedClientCount(),
      lastTagTime,
      raceStarted && !raceFinished);

    // A timed session is ranked on best lap, not on laps completed. Feeding the
    // race board here would show a sort order that is not a ranking, on the one
    // screen the operator watches all session.
    //
    // Ranks the snapshot already taken above rather than calling
    // BuildSessionField, which would clone the whole field a second time on
    // every heartbeat. The board shows the top of the order, and riders who
    // never went out have no time and so are never in it - so the picks shown
    // here are the same ones the sheet prints.
    if (IsQualifying)
      _raceDayView.SetQualifyingLeaderboard(QualifyingRanking.Rank(sorted));
    else
      _raceDayView.SetLeaderboard(sorted);

    _raceDayView.SetChecklist(
      raceName,
      _riderDataImporter.Count,
      raceDuration,
      isListening && ConnectedClientCount() > 0);
  }

  private int ConnectedClientCount()
  {
    lock (clientsLock)
    {
      return connectedClients.Count;
    }
  }

  /// <summary>
  /// Maps the internal race flags onto the six states a volunteer sees. The
  /// status label this replaces printed things like
  /// "Race: LEADER 11240EC8F5F4 - 2 laps to go (target: 14)".
  /// </summary>
  private (RaceDayState State, string Detail) DescribeRaceState(List<RiderInfo> sorted, int riderCount)
  {
    if (raceFinished)
      return (RaceDayState.Finished, IsQualifying
        ? "Session over - press Gate pick order..."
        : IsTimedSession
          ? "Session over - press Results... for the lap times"
          : "Results are final - press Results...");

    if (!raceStarted)
    {
      return manualStartMode
        ? (RaceDayState.ReadyToStart, IsTimedSession
            ? "Press START SESSION when the gate opens"
            : "Press START RACE when the gate drops")
        : (RaceDayState.WaitingForFirstRider, "The clock starts on the first transponder read");
    }

    if (waitingForFinalLaps)
    {
      var stillOut = sorted.Count(r => !r.IsDNF && !r.IsDNS && r.TotalLaps < r.FinalAllowedLap);

      // The count is the same either way - past the flag, "has not reached
      // FinalAllowedLap" is exactly "has not come round since" - but the words
      // are not: in a timed session the lap they are on still counts.
      if (IsTimedSession)
      {
        return (RaceDayState.Finishing, stillOut == 1
          ? "1 rider still out - their lap still counts"
          : $"{stillOut} riders still out - their lap still counts");
      }

      return (RaceDayState.Finishing,
        stillOut == 1
          ? "1 rider still out on their last lap"
          : $"{stillOut} riders still out on their last lap");
    }

    if (raceTimeExpired || waitingForLeaderFinish)
    {
      var leader = sorted.FirstOrDefault(r => !r.IsDNF && !r.IsDNS);
      if (leader != null && targetLapsToFinishRace > 0)
      {
        var toGo = Math.Max(0, targetLapsToFinishRace - leader.TotalLaps);
        return (RaceDayState.LastLaps, toGo == 1
          ? $"Leader {leader.Label} has 1 lap to go"
          : $"Leader {leader.Label} has {toGo} laps to go");
      }
      return (RaceDayState.LastLaps, "Time is up - the leader is finishing their lap");
    }

    var started = raceStartTime.HasValue ? raceStartTime.Value.ToString("HH:mm") : "";
    return (RaceDayState.Running, $"Started {started} · {riderCount} riders");
  }

  /// <summary>
  /// Ends the race on the operator's word rather than waiting out the timeout.
  ///
  /// Without this, a race whose last rider has retired sits in "finishing" until
  /// the DNF timeout expires, with no way to close it out.
  /// </summary>
  private void EndRaceNow()
  {
    if (raceFinished || !raceStarted) return;

    int stillOut;
    lock (ridersLock)
    {
      stillOut = riders.Values.Count(r =>
        !r.IsDNF && !r.IsDNS && !ignoredTags.Contains(r.TagID) &&
        r.FinalAllowedLap != int.MaxValue && r.TotalLaps < r.FinalAllowedLap);
    }

    // In a timed session nobody is "scored DNF" - any time they already set
    // still stands. What they lose is the lap they are riding right now.
    var warning = stillOut == 0
      ? ""
      : IsTimedSession
        ? $"\n\n{stillOut} rider(s) are still out. The lap they are on will not count; " +
          "any time they have already set still does."
        : $"\n\n{stillOut} rider(s) have not finished their last lap and will be scored DNF.";

    var answer = MessageBox.Show(this,
      IsTimedSession ? $"End the session now?{warning}" : $"End the race now?{warning}",
      IsTimedSession ? "End session" : "End race",
      MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);

    if (answer != DialogResult.Yes) return;

    lock (ridersLock)
    {
      foreach (var rider in riders.Values.Where(r =>
        !r.IsDNF && !r.IsDNS && !ignoredTags.Contains(r.TagID) &&
        r.FinalAllowedLap != int.MaxValue && r.TotalLaps < r.FinalAllowedLap))
      {
        rider.IsDNF = true;
        rider.DNFTime = DateTime.Now;
        rider.StatusReason = IsTimedSession
          ? "Session ended by the operator"
          : "Race ended by the operator";
      }
    }

    AddMessage(IsTimedSession
      ? "🏁 Session ended by the operator."
      : "🏁 Race ended by the operator.");
    CompletelyFinishRace();
    _refresh.RenderNow(RaceViewKind.All);
  }

  /// <summary>
  /// The Race Day "Fix laps..." button. Opens the rider who most needs looking
  /// at - the one with an outstanding missed-read warning - rather than asking
  /// the operator to go and find them.
  /// </summary>
  private void OpenLapCorrectionForMostUrgentRider()
  {
    string? target;
    lock (ridersLock)
    {
      target = riders.Values
        .Where(r => !ignoredTags.Contains(r.TagID))
        .FirstOrDefault(r => r.Laps.Any(l => l.IsSuggestedForSplit && !l.SuggestionDismissed))
        ?.TagID;
    }

    if (target == null)
    {
      // Nothing flagged, so send them to the grid to pick someone.
      tabControl.SelectedTab = tabPageRiders;
      MessageBox.Show(this,
        "Nothing needs fixing right now.\n\nTo correct a rider anyway, right-click them in the Riders list.",
        "Fix laps", MessageBoxButtons.OK, MessageBoxIcon.Information);
      return;
    }

    OpenLapCorrection(target);
  }

  private sealed class RaceDayViewAdapter : IRaceView
  {
    private readonly Form1 _form;
    public RaceDayViewAdapter(Form1 form) => _form = form;

    public RaceViewKind Kind => RaceViewKind.RaceDay;
    public TabPage? HostTab => _form.tabPageRaceDay;

    // The clock, the countdown colour and the reader-quiet warning all move with
    // wall-clock time even when no data has changed.
    public bool NeedsHeartbeat => true;

    public void Render() => _form.RenderRaceDay();
  }
}
