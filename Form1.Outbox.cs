namespace CrossMgrInterface;

/// <summary>
/// What happens by itself after a session: its sheets saved into the day's
/// folder, its results put on the website, the overall once its last moto is
/// there - and all of it again after a correction. See AfterSession for the
/// switches and OutboxPolicy for the order.
///
/// One thing at a time, from a timer, and nothing is ever waited for on the
/// screen: a dead hotspot only means the work waits. What there is to do is
/// read from the race database each time (DbRace.Revision), so a restart in
/// the middle of it simply carries on.
/// </summary>
public partial class Form1
{
  private System.Windows.Forms.Timer? _outboxTimer;
  private bool _outboxBusy;
  private DateTime _outboxRetryAt = DateTime.MinValue;
  private int _outboxFailures;

  /// <summary>Work that failed for a reason trying again will not cure - a refused key. Until the session changes again.</summary>
  private readonly HashSet<string> _outboxParked = new();

  /// <summary>Sessions live timing was switched on for by itself, once each: switched off by hand, it stays off.</summary>
  private readonly HashSet<int> _liveAutoStarted = new();

  /// <summary>The publisher the outbox sends through. A demo puts a pretend one here; demos never publish.</summary>
  private IResultsPublisher? _outboxPublisher { get; set; }

  /// <summary>What the outbox last did or is waiting on, for the plan strip.</summary>
  private string _outboxStatus = "";

  // The day's programme on the websites: what was last sent, when, and
  // whether the website takes programmes at all.
  private string? _programmeSent;
  private DateTime _programmeSentAt = DateTime.MinValue;
  private bool _programmeUnsupported;

  /// <summary>At most this often: a day running late moves every expected time a little each minute.</summary>
  private static readonly TimeSpan ProgrammeEvery = TimeSpan.FromSeconds(30);

  /// <summary>What happens by itself now: today's plan's switches while one is in use, the settings' otherwise.</summary>
  private AfterSession AfterSessionNow => _activePlan?.After ?? _settings.AfterSession;

  private void StartOutbox()
  {
    if (_outboxTimer != null) return;
    _outboxTimer = new System.Windows.Forms.Timer { Interval = 10_000 };
    _outboxTimer.Tick += (_, _) => OutboxTick();
    _outboxTimer.Start();
  }

  private bool OutboxCanPublish =>
    _outboxPublisher?.IsConfigured ?? PublishingAvailable;

  private IResultsPublisher OutboxPublisher =>
    _outboxPublisher ?? HttpResultsPublisher.FromSettings(_settings, AddDiagnostic);

  /// <summary>Where the outbox says it published to - and that a demo went up as one.</summary>
  private string OutboxWhere => _outboxPublisher is DemoPublisher
    ? "the demo's pretend website"
    : IsDemo ? $"{PublishSiteName} as a DEMO" : PublishSiteName;

  /// <summary>A finished session was corrected, renamed or re-listed: publish and save it again.</summary>
  private void MarkSessionChanged()
  {
    if (currentRaceId is not { } raceId || !raceFinished) return;
    try { _raceDb.MarkChanged(raceId); }
    catch (Exception ex) { AddDiagnostic($"Could not mark the session as changed: {ex.Message}"); }
  }

  /// <summary>
  /// At the finish: records what is to happen to this session by itself, and
  /// where its sheets go - decided now, so a correction tomorrow still saves
  /// over the same files.
  /// </summary>
  private void MarkFinishedForOutbox(int raceId)
  {
    var after = AfterSessionNow;
    try
    {
      var race = _raceDb.GetRace(raceId);
      if (race == null) return;

      var plan = _activePlan != null && race.PlanId == _activePlan.Id ? _activePlan : null;
      var order = plan?.Find(race.PlanEntryId) is { } entry
        ? plan.Sessions.IndexOf(entry) + 1
        : _raceDb.GetAllRaces().Count(r => r.StartTime.Date == race.StartTime.Date && r.StartTime <= race.StartTime);

      var folder = SheetSaver.SessionFolder(
        SheetSaver.DayFolder(after.Folder, race.StartTime.Date, plan?.Name), order, race.Name);

      _raceDb.MarkFinished(raceId, after.Publish && OutboxCanPublish, after.SaveSheets, after.Pdf, folder);
    }
    catch (Exception ex)
    {
      AddDiagnostic($"Could not record what happens after the session: {ex.Message}");
    }
  }

  /// <summary>Every ten seconds: the next thing to do, if nothing is under way.</summary>
  private void OutboxTick()
  {
    AutoStartLiveTiming();
    if (_outboxBusy) return;

    try
    {
      var races = _raceDb.RacesWithWorkToDo();
      var overalls = OverallCandidates();
      var canPublish = OutboxCanPublish && DateTime.Now >= _outboxRetryAt;
      var item = OutboxPolicy.Next(races, overalls, DateTime.Now, _outboxParked, canPublish);

      var waiting = races.Count(OutboxPolicy.WaitingToPublish);
      if (item == null && SendProgrammeIfChanged()) return;
      if (item == null)
      {
        _outboxStatus = waiting > 0 && OutboxCanPublish && DateTime.Now < _outboxRetryAt
          ? $"Website: {waiting} waiting for the internet"
          : "";
        return;
      }

      switch (item.Work)
      {
        case OutboxWork.SaveSheets:
          SaveSessionSheets(races.First(r => r.Id == item.Id), item);
          break;
        case OutboxWork.SaveOverall:
          SaveOverallSheets(overalls.First(o => o.OverallId == item.Id), item);
          break;
        case OutboxWork.PublishSession:
          PublishSessionQuietly(races.First(r => r.Id == item.Id), item);
          break;
        case OutboxWork.PublishOverall:
          PublishOverallQuietly(overalls.First(o => o.OverallId == item.Id), item);
          break;
      }
    }
    catch (Exception ex)
    {
      _outboxBusy = false;
      AddDiagnostic($"Outbox: {ex.Message}");
    }
  }

  /// <summary>
  /// Overalls with a moto that publishes or saves by itself. Complete once
  /// every moto it is meant to have has finished: for a planned overall, all
  /// of its group's sessions in the plan not skipped; otherwise at least two,
  /// all finished - an overall of one moto is that moto.
  /// </summary>
  private List<OverallCandidate> OverallCandidates()
  {
    var candidates = new List<OverallCandidate>();
    foreach (var overall in _raceDb.ListOveralls())
    {
      var motos = overall.RaceIds.Select(id => _raceDb.GetRace(id)).OfType<DbRace>().ToList();
      if (motos.Count == 0 || !motos.Any(m => m.AutoPublish == true || m.AutoSaveSheets == true)) continue;

      var complete = motos.All(m => m.IsFinished) && PlannedMotosDone(motos) is var planned &&
                     (planned ?? motos.Count >= 2);
      candidates.Add(new OverallCandidate(overall.Id, motos, complete, OutboxPolicy.StampOf(overall, motos),
        overall.PublishedStamp, overall.SavedStamp));
    }
    return candidates;
  }

  /// <summary>For the motos of today's plan: whether every planned moto of their group has finished. Null when not planned.</summary>
  private bool? PlannedMotosDone(IReadOnlyList<DbRace> motos)
  {
    if (_activePlan is not { } plan) return null;
    var first = motos.FirstOrDefault(m => m.PlanId == plan.Id);
    if (first == null || plan.Find(first.PlanEntryId)?.OverallGroup is not { } group) return null;

    var runs = PlanRuns();
    return plan.Sessions
      .Where(s => !s.Skipped && string.Equals(s.OverallGroup, group, StringComparison.OrdinalIgnoreCase))
      .All(s => runs.TryGetValue(s.Id, out var run) && run.Finished);
  }

  // ---- Saving ---------------------------------------------------------------

  private void SaveSessionSheets(DbRace race, OutboxItem item)
  {
    var folder = race.SheetsFolder ?? SheetSaver.SessionFolder(
      SheetSaver.DayFolder(AfterSessionNow.Folder, race.StartTime.Date, null), race.Id, race.Name);
    var pdf = race.AutoSavePdf != false;

    _outboxBusy = true;
    _outboxStatus = $"Saving the sheets of {race.Name}...";
    Task.Run(() =>
    {
      // Off the screen's thread, with a generator of its own: a big field
      // takes a moment to lay out, and nobody should wait on it.
      var gatePick = race.SessionType == SessionType.TimedQualifying;
      var field = _raceDb.RestoreRiderData(race.Id, includeRosterOnly: gatePick);
      foreach (var tag in _raceDb.GetIgnoredTags(race.Id)) field.Remove(tag);
      if (field.Count == 0) return new List<string>();

      var rules = RaceRules.FromRace(race);
      var title = string.IsNullOrWhiteSpace(race.Name) ? $"Session {race.StartTime:yyyy-MM-dd HH:mm}" : race.Name;
      if (gatePick)
      {
        using var qualifying = new QualifyingReportGenerator();
        return qualifying.SaveSheets(field, GatePickTitle(race.Name), race.StartTime, race.EndTime, race.Duration,
          race.IsFinished, rules, folder, pdf);
      }

      var generator = new RaceReportGenerator();
      try
      {
        return generator.SaveSheets(field, race.StartTime, race.EndTime ?? race.StartTime + race.Duration,
          race.Duration, race.IsFinished, title, race.FinalLapsStartTime, race.IsFinished ? race.EndTime : null,
          race.AdditionalLaps ?? 0, rules, folder, pdf);
      }
      finally
      {
        generator.Dispose();
      }
    }).ContinueWith(task => BeginInvoke(() =>
    {
      _outboxBusy = false;
      if (task.Exception is { } ex)
      {
        _outboxParked.Add(item.Key);
        RaiseNotice(NoticeLevel.Warning, $"The sheets of {race.Name} could not be saved: {ex.GetBaseException().Message}");
        AddDiagnostic($"Saving sheets: {ex.GetBaseException()}");
        return;
      }

      _raceDb.MarkSheetsSaved(race.Id, item.Revision);
      var pdfNote = pdf && !SheetSaver.PdfPrinterInstalled() ? " (no PDF: Microsoft Print to PDF is not installed)" : "";
      AddMessage($"💾 Sheets of {race.Name} saved to {folder}{pdfNote}");
      _outboxStatus = "";
    }));
  }

  private void SaveOverallSheets(OverallCandidate candidate, OutboxItem item)
  {
    var overall = _raceDb.GetOverall(candidate.OverallId);
    if (overall == null) return;

    var first = candidate.Motos.First();
    var folder = first.SheetsFolder != null ? Path.GetDirectoryName(first.SheetsFolder)!
      : SheetSaver.DayFolder(AfterSessionNow.Folder, first.StartTime.Date, null);
    var pdf = candidate.Motos.Any(m => m.AutoSavePdf != false);
    var result = ScoreOverall(overall);

    _outboxBusy = true;
    Task.Run(() =>
    {
      using var generator = new OverallReportGenerator();
      return generator.SaveSheets(result, overall.Name, folder, pdf);
    }).ContinueWith(task => BeginInvoke(() =>
    {
      _outboxBusy = false;
      if (task.Exception is { } ex)
      {
        _outboxParked.Add(item.Key);
        RaiseNotice(NoticeLevel.Warning, $"The overall '{overall.Name}' could not be saved: {ex.GetBaseException().Message}");
        return;
      }

      _raceDb.SetOverallStamps(overall.Id, saved: item.Stamp);
      AddMessage($"💾 Overall '{overall.Name}' saved to {folder}");
    }));
  }

  // ---- Publishing ------------------------------------------------------------

  private void PublishSessionQuietly(DbRace race, OutboxItem item)
  {
    if (BuildSessionPayload(race) is not { } built)
    {
      // Nothing was recorded: there is nothing to put up, now or later.
      _raceDb.MarkPublished(race.Id, DateTime.Now, race.PublishedUrl ?? "", item.Revision);
      return;
    }

    _outboxBusy = true;
    _outboxStatus = $"Publishing {race.Name}...";
    var publisher = OutboxPublisher;
    Task.Run(() => publisher.PublishAsync(built.Session, null, CancellationToken.None))
      .ContinueWith(task => BeginInvoke(() =>
      {
        _outboxBusy = false;
        var outcome = task.IsCompletedSuccessfully
          ? task.Result
          : new PublishOutcome(PublishStatus.ServerError, task.Exception?.GetBaseException().Message ?? "failed");

        if (outcome.Ok)
        {
          _raceDb.MarkPublished(race.Id, DateTime.Now, outcome.Url ?? "", item.Revision);
          _outboxFailures = 0;
          _outboxStatus = "";
          AddMessage($"🌐 {race.Name} published to {OutboxWhere} by itself" +
                     (race.PublishedAt != null ? " again, with the changes." : "."));
          return;
        }

        OutboxFailed(item, race.Name, outcome);
      }));
  }

  private void PublishOverallQuietly(OverallCandidate candidate, OutboxItem item)
  {
    var overall = _raceDb.GetOverall(candidate.OverallId);
    if (overall == null) return;

    var payload = BuildOverallPayload(overall, candidate.Motos);
    _outboxBusy = true;
    var publisher = OutboxPublisher;
    Task.Run(() => publisher.PublishOverallAsync(payload, null, CancellationToken.None))
      .ContinueWith(task => BeginInvoke(() =>
      {
        _outboxBusy = false;
        var outcome = task.IsCompletedSuccessfully
          ? task.Result
          : new PublishOutcome(PublishStatus.ServerError, task.Exception?.GetBaseException().Message ?? "failed");

        if (outcome.Ok)
        {
          _raceDb.MarkOverallPublished(overall.Id, DateTime.Now, outcome.Url ?? "");
          _raceDb.SetOverallStamps(overall.Id, published: item.Stamp);
          _outboxFailures = 0;
          AddMessage($"🌐 Overall '{overall.Name}' published to {OutboxWhere} by itself.");
          return;
        }

        OutboxFailed(item, $"the overall '{overall.Name}'", outcome);
      }));
  }

  /// <summary>
  /// No internet, or a website that did not answer: try again later, a little
  /// later each time. Refused for a reason that will not go away - the key,
  /// the results themselves - park it and say so once; Past sessions still
  /// publishes it by hand.
  /// </summary>
  private void OutboxFailed(OutboxItem item, string what, PublishOutcome outcome)
  {
    if (outcome.Status is PublishStatus.NoInternet or PublishStatus.TimedOut or PublishStatus.ServerError
        or PublishStatus.Cancelled)
    {
      _outboxFailures++;
      var wait = OutboxPolicy.Backoff(_outboxFailures);
      _outboxRetryAt = DateTime.Now + wait;
      _outboxStatus = $"Website: no answer - trying again at {_outboxRetryAt:HH:mm}";
      AddDiagnostic($"Outbox: {what} not published ({outcome.Status}); trying again in {wait.TotalSeconds:0} s");
      return;
    }

    _outboxParked.Add(item.Key);
    _outboxStatus = $"Website: {what} was not accepted";
    RaiseNotice(NoticeLevel.Warning,
      $"{what} could not be published by itself: {outcome.Message} Publish it from Race > Past sessions... once that is sorted.");
  }

  /// <summary>Race > After each session...: the switches for sessions set up by hand.</summary>
  private void ShowAfterSessionSettings()
  {
    var after = _settings.AfterSession.Copy();
    using var dialog = new AfterSessionDialog(after, PublishingAvailable, LiveTimingConfigured, forPlan: null);
    if (dialog.ShowDialog(this) != DialogResult.OK) return;

    _settings.AfterSession = after;
    _settings.Save();
    if (_activePlan != null)
      AddMessage("📋 Saved. While today's plan is in use its own switches apply - change them in Race > Plan the day....");
  }

  // ---- The programme -----------------------------------------------------------

  /// <summary>
  /// Sends today's programme when it has changed - a session started or
  /// finished, a time moved - so the websites can say what is next. Only
  /// while the plan publishes; an older website without programmes is told
  /// nothing more after the first try, and nobody is bothered about it.
  /// </summary>
  private bool SendProgrammeIfChanged()
  {
    if (_activePlan is not { } plan || !plan.After.Publish || !OutboxCanPublish || _programmeUnsupported) return false;
    var now = DateTime.Now;
    if (now < _outboxRetryAt || now - _programmeSentAt < ProgrammeEvery) return false;

    var payload = PublishPayloadBuilder.BuildPlan(plan, PlanForecastNow(now), id => _raceDb.GetRace(id)?.PublicId, now,
      demo: IsDemo);
    var fingerprint = PublishPayloadBuilder.Fingerprint(payload);
    if (fingerprint == _programmeSent) return false;

    _outboxBusy = true;
    _programmeSentAt = now;
    var publisher = OutboxPublisher;
    Task.Run(() => publisher.PublishPlanAsync(payload, CancellationToken.None))
      .ContinueWith(task => BeginInvoke(() =>
      {
        _outboxBusy = false;
        var outcome = task.IsCompletedSuccessfully
          ? task.Result
          : new PublishOutcome(PublishStatus.ServerError, task.Exception?.GetBaseException().Message ?? "failed");

        if (outcome.Ok)
        {
          _programmeSent = fingerprint;
          return;
        }

        if (outcome.Status == PublishStatus.NotSupported)
        {
          _programmeUnsupported = true;
          AddDiagnostic("The results website does not take the day's programme yet; it is not sent again until the app restarts.");
          return;
        }

        AddDiagnostic($"The programme was not sent ({outcome.Status}); trying again shortly.");
      }));
    return true;
  }

  // ---- Live timing -----------------------------------------------------------

  /// <summary>
  /// Live timing on once a session's clock has started, when the switch says
  /// so - once per session, so switching it off by hand is respected. Never
  /// for a session brought back by crash recovery, which comes back with live
  /// timing off on purpose (see RestoreRaceState).
  /// </summary>
  private void AutoStartLiveTiming()
  {
    if (!AfterSessionNow.LiveTiming || _liveOn || !raceStarted || raceFinished) return;
    if (currentRaceId is not { } raceId || !_liveAutoStarted.Add(raceId)) return;
    if (!LiveTimingAllowed || !LiveTimingConfigured) return;

    SetLiveTiming(true, silent: true);
    AddMessage("📡 Live timing switched on by itself for this session.");
  }
}
