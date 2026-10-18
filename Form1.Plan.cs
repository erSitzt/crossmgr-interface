using System.Runtime.InteropServices;

namespace CrossMgrInterface;

/// <summary>
/// The day plan at work: the plan chosen for today, the tick that puts each
/// session on screen as it comes due and reminds when one is due, and the
/// strip on the Race Day screen that says what is happening.
///
/// The rules themselves are in PlanForecast and PlanRunnerPolicy, which know
/// nothing of this window. This file only gathers what they need, carries out
/// what they decide, and puts the results on screen.
/// </summary>
public partial class Form1
{
  [DllImport("user32.dll")]
  private static extern bool IsWindowEnabled(IntPtr hWnd);

  private PlanStore _plans = null!;

  /// <summary>Today's plan, once the operator has said to use it.</summary>
  private DayPlan? _activePlan;

  /// <summary>
  /// The planned session on screen: set up, running or finished. Null when
  /// nothing from the plan is - nothing set up, or a session set up by hand or
  /// reopened from Past sessions.
  /// </summary>
  private string? _planEntryOnScreen;

  /// <summary>
  /// Nothing is set up: the screen is between sessions. With a plan, reads
  /// are held then, or the first bike over the loop would start an unnamed
  /// race. Under ridersLock.
  /// </summary>
  private bool _nothingSetUp = true;

  /// <summary>A plan is in use, so reads with nothing set up are held. Read on the reader's thread.</summary>
  private volatile bool _planHoldsReads;

  /// <summary>When the session on screen finished, for how long it stays there.</summary>
  private DateTime? _finishedAt;

  private DateTime? _lastCorrectionAt;
  private DateTime? _planSnoozedUntil;
  private DateTime? _planCountdownFrom;
  private readonly HashSet<string> _planRemindersGiven = new();
  private bool _planCrossingWarned;

  /// <summary>Riders read after the finish, by class: the next group may already be on track. Under ridersLock.</summary>
  private readonly List<(string Tag, string Class, DateTime At)> _postRaceReads = new();

  private Dictionary<string, PlanRun>? _planRunsCache;
  private DateTime _planRunsReadAt;

  private PlanRunnerTimings PlanTimings { get; set; } = PlanRunnerTimings.Default;

  private void InitializePlans()
  {
    _plans = PlanStore.Load();
    foreach (var file in _plans.SetAside)
      AddDiagnostic($"A day plan could not be read and was set aside as {file}");

    _raceDayView.PlanSetUpClicked += (_, _) => SetUpNextPlannedByHand();
    _raceDayView.PlanNotYetClicked += (_, _) => SnoozePlan();
    _raceDayView.PlanChangeTimesClicked += (_, _) => ShowPlanDialog(focusNext: true);
    _raceDayView.PlanSkipClicked += (_, _) => SkipPlannedSession();
  }

  // ---- Choosing a plan -----------------------------------------------------

  /// <summary>
  /// Puts a plan to work for today: its rider list, its circuit, the reader,
  /// and from then on the tick that sets each session up.
  /// </summary>
  private void ActivatePlan(DayPlan plan)
  {
    _activePlan = plan;
    _planHoldsReads = true;
    _planRemindersGiven.Clear();
    _planCountdownFrom = null;
    _planSnoozedUntil = null;
    InvalidatePlanRuns();

    _settings.ActivePlanId = plan.Id;
    _settings.Save();

    // The day's list, unless it is already the one loaded. Never under a
    // running session: the riders on track are scored against the list.
    if (!string.IsNullOrEmpty(plan.RiderListPath) && !(raceStarted && !raceFinished) &&
        (_riderDataImporter.Count == 0 ||
         !string.Equals(plan.RiderListPath, _settings.LastRiderListPath, StringComparison.OrdinalIgnoreCase)))
    {
      if (LoadRiderListFile(plan.RiderListPath)) RememberRiderList(plan.RiderListPath);
    }

    if (!string.IsNullOrEmpty(plan.TrackId)) SelectTrack(plan.TrackId);
    if (!isListening) StartTcpListener(readerPort);

    // A session brought back by crash recovery may be one of this plan's.
    if (currentRaceId is { } raceId && _raceDb.GetRace(raceId) is { } race) SyncPlanEntryOnScreen(race);

    var sessions = plan.Sessions.Count(s => !s.Skipped);
    AddMessage($"📋 Using today's plan '{plan.Name}': {sessions} session(s). Each is set up by itself as it comes due.");
    PlanTick();
  }

  private void DeactivatePlan()
  {
    if (_activePlan == null) return;

    AddMessage($"📋 Stopped using the plan '{_activePlan.Name}'. Sessions are set up by hand again.");
    _activePlan = null;
    _planHoldsReads = false;
    _planEntryOnScreen = null;
    _settings.ActivePlanId = null;
    _settings.Save();
    _raceDayView.SetPlan(null);
  }

  /// <summary>
  /// At start-up, after crash recovery: carries on with the plan that was in
  /// use today, or offers today's plan once.
  /// </summary>
  private void OfferTodaysPlan()
  {
    if (_activePlan != null || IsDemo) return;

    var todays = _plans.For(DateTime.Today);
    if (todays.Count == 0) return;

    if (todays.FirstOrDefault(p => p.Id == _settings.ActivePlanId) is { } inUse)
    {
      ActivatePlan(inUse);
      return;
    }

    var plan = todays.OrderBy(p => p.Sessions.Select(s => s.Time).DefaultIfEmpty().Min()).First();
    var first = plan.Sessions.Where(s => !s.Skipped).OrderBy(s => s.Time).FirstOrDefault();
    var answer = MessageBox.Show(this,
      $"There is a plan for today: {plan.Name}\n\n" +
      $"{plan.Sessions.Count(s => !s.Skipped)} session(s)" +
      (first != null ? $", the first is {first.Name} at {first.Time:hh\\:mm}." : ".") +
      "\n\nUse it? Each session is then set up by itself as it comes due.",
      "Today's plan", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

    if (answer == DialogResult.Yes) ActivatePlan(plan);
  }

  /// <summary>When a stored race comes back on screen, whether it is the plan's.</summary>
  private void SyncPlanEntryOnScreen(DbRace race)
  {
    _planEntryOnScreen = _activePlan != null && race.PlanId == _activePlan.Id ? race.PlanEntryId : null;
    _finishedAt = race.IsFinished ? race.EndTime ?? race.LastSavedAt ?? race.StartTime : null;
    lock (ridersLock) _nothingSetUp = false;
  }

  /// <summary>Called once the clock has started and the race is recorded: joins its overall, ties it to the plan.</summary>
  private void OnRaceRecorded(int raceId)
  {
    JoinChosenOverall(raceId);

    if (_activePlan is { } plan && _planEntryOnScreen is { } entry)
    {
      try
      {
        _raceDb.StampPlanEntry(raceId, plan.Id, entry);
      }
      catch (Exception ex)
      {
        AddDiagnostic($"Could not tie the race to the plan: {ex.Message}");
      }
      InvalidatePlanRuns();
    }
  }

  // ---- The tick -----------------------------------------------------------

  private void InvalidatePlanRuns() => _planRunsCache = null;

  /// <summary>
  /// What the database says has run. Read every few seconds rather than every
  /// tick: it only changes when a clock starts or a session finishes, and both
  /// of those clear it straight away.
  /// </summary>
  private Dictionary<string, PlanRun> PlanRuns()
  {
    if (_activePlan == null) return new Dictionary<string, PlanRun>();
    if (_planRunsCache != null && DateTime.Now - _planRunsReadAt < TimeSpan.FromSeconds(5)) return _planRunsCache;

    try
    {
      _planRunsCache = _raceDb.RunsForPlan(_activePlan.Id);
    }
    catch (Exception ex)
    {
      AddDiagnostic($"Could not read the plan's sessions from the database: {ex.Message}");
      _planRunsCache ??= new Dictionary<string, PlanRun>();
    }
    _planRunsReadAt = DateTime.Now;
    return _planRunsCache;
  }

  private IReadOnlyList<ForecastEntry> PlanForecastNow(DateTime now)
  {
    var plan = _activePlan!;
    var running = raceStarted && !raceFinished ? _planEntryOnScreen : null;
    var ready = !raceStarted && !raceFinished ? _planEntryOnScreen : null;
    return PlanForecast.Forecast(plan, PlanRuns(), ready, running, now);
  }

  private PlanScreen PlanScreenNow()
  {
    bool empty;
    lock (ridersLock) empty = _nothingSetUp;
    if (empty) return PlanScreen.Nothing;

    return new PlanScreen(_planEntryOnScreen, _planEntryOnScreen != null, false,
      raceStarted && !raceFinished, raceFinished, _finishedAt);
  }

  /// <summary>A modal window - Fix laps, a message box, a settings window - is open over the main one.</summary>
  private bool WindowOpenOverMain() => IsHandleCreated && !IsWindowEnabled(Handle);

  /// <summary>Once a second, from the clock tick: the plan's whole judgement, carried out.</summary>
  private void PlanTick()
  {
    if (_activePlan is not { } plan) return;

    try
    {
      var now = DateTime.Now;
      var forecast = PlanForecastNow(now);
      var screen = PlanScreenNow();
      var windowOpen = WindowOpenOverMain();
      var decision = PlanRunnerPolicy.Decide(forecast, screen, now, windowOpen, _lastCorrectionAt,
        _planSnoozedUntil, PlanTimings);

      if (NextGroupOnTrack(decision, screen, now))
      {
        if (decision.Waiting is PlanWait.NotYetDue or PlanWait.Snoozed)
        {
          AddMessage($"📋 Riders of {decision.Next!.Session.Name} are crossing the line - setting it up now.");
          SetUpPlanned(decision.Next, byHand: false);
          return;
        }

        if (decision.Waiting is PlanWait.WindowOpen or PlanWait.Correcting && !_planCrossingWarned)
        {
          _planCrossingWarned = true;
          RaiseNotice(NoticeLevel.Critical,
            $"Riders of {decision.Next!.Session.Name} are crossing the line, and it is not set up yet. " +
            "Close the open window, or press Set up now on the Race Day screen.");
        }
      }

      TimeSpan? countdown = null;
      if (decision.SetUpNow && decision.Next != null)
      {
        _planCountdownFrom ??= now;
        var left = PlanTimings.Countdown - (now - _planCountdownFrom.Value);
        if (left <= TimeSpan.Zero)
        {
          SetUpPlanned(decision.Next, byHand: false);
          return;
        }
        countdown = left;
      }
      else
      {
        _planCountdownFrom = null;
      }

      var reminder = PlanRunnerPolicy.ReminderFor(decision.Next, now, PlanTimings);
      GiveReminder(decision.Next, reminder);

      var strip = PlanStripText.Build(plan, forecast, decision, reminder, countdown, now);
      if (_outboxStatus.Length > 0)
        strip = strip with { Timing = strip.Timing.Length > 0 ? $"{strip.Timing}  ·  {_outboxStatus}" : _outboxStatus };
      _raceDayView.SetPlan(strip);
    }
    catch (Exception ex)
    {
      // The plan helps run the day; it must never be what stops it.
      AddDiagnostic($"Plan: {ex.Message}");
    }
  }

  /// <summary>
  /// Riders of the next session's classes crossing after the finish - not
  /// the last of the finished session rolling in, but the next group on its
  /// way: more than one of them, after the finished session has settled, and
  /// close to when the next is due.
  /// </summary>
  private bool NextGroupOnTrack(PlanDecision decision, PlanScreen screen, DateTime now)
  {
    if (decision.Next is not { Status: PlanEntryStatus.Planned } next || !screen.Finished || screen.FinishedAt is not { } finished)
      return false;
    if (now < next.DueAt - TimeSpan.FromMinutes(10)) return false;

    var since = finished + PlanTimings.AfterFinishFirstRider;
    lock (ridersLock)
    {
      return _postRaceReads
        .Where(r => r.At >= since && r.At >= now - TimeSpan.FromMinutes(1))
        .Where(r => SessionClassGate.InSession(next.Session.Classes, r.Class))
        .Select(r => r.Tag)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count() >= 2;
    }
  }

  private void GiveReminder(ForecastEntry? next, PlanReminder reminder)
  {
    if (next == null || reminder == PlanReminder.None) return;

    // A running session has its own clock to watch; reminding about the next
    // one is for between sessions, and for the one set up and waiting. Not
    // marked as given, so it still comes once the running one is over.
    if (raceStarted && !raceFinished) return;
    if (!_planRemindersGiven.Add($"{next.Session.Id}:{reminder}")) return;

    if (PlanStripText.ReminderText(next, reminder) is not { } text) return;

    // Played here, not left to the banner: a Critical banner from the finish
    // would swallow a Warning, and the sound is the point.
    try { System.Media.SystemSounds.Exclamation.Play(); }
    catch (Exception) { /* no audio device is not a problem worth reporting */ }

    AddMessage($"⏰ {text}");
    RaiseNotice(reminder == PlanReminder.Overdue ? NoticeLevel.Critical : NoticeLevel.Warning, text);
  }

  /// <summary>
  /// The plan's next session after the one on screen, for the spectator
  /// screen: "Moto 1 - Youth  ~14:06". Null without a plan or at the end of it.
  /// </summary>
  private string? PlanNextUpText()
  {
    if (_activePlan == null) return null;
    try
    {
      var forecast = PlanForecastNow(DateTime.Now);
      var next = forecast.FirstOrDefault(e => e.IsPending && e.Session.Id != _planEntryOnScreen);
      return next == null ? null : $"{next.Session.Name}   ~{next.ExpectedStart:HH:mm}";
    }
    catch (Exception)
    {
      return null;
    }
  }

  // ---- Setting up ---------------------------------------------------------

  /// <summary>
  /// Puts a planned session on screen, set up as the plan says, waiting for
  /// its clock - the finished session before it put away, exactly as NEW
  /// SESSION... does.
  /// </summary>
  private void SetUpPlanned(ForecastEntry next, bool byHand)
  {
    var plan = _activePlan!;
    var session = next.Session;

    try
    {
      var overall = OverallOfGroup(plan, session);
      var previous = raceName;
      bool putAway;
      lock (ridersLock) putAway = !_nothingSetUp;

      using (HoldReadsWhileSettingUp())
      {
        if (putAway) ResetForNextSession();
        ApplyNewRaceSetup(session.ToSetup(overall), automatic: true);
      }

      _planEntryOnScreen = session.Id;
      _planCountdownFrom = null;
      _planSnoozedUntil = null;
      _planCrossingWarned = false;
      InvalidatePlanRuns();

      if (putAway && !string.IsNullOrEmpty(previous))
        AddMessage($"📁 '{previous}' put away. Its results stay under Race > Past sessions...");
      AddMessage($"📋 {session.Name} set up from the plan{(byHand ? "" : " by itself")} - due ~{next.ExpectedStart:HH:mm}.");
      RaiseNotice(NoticeLevel.Info, $"{session.Name} is set up - due ~{next.ExpectedStart:HH:mm}.");

      PlanTick();
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(this, $"{session.Name} could not be set up.",
        "Set it up with Race > New race... instead. The plan and the finished sessions are unaffected.", ex);
    }
  }

  /// <summary>The overall an earlier moto of the same group started, to join; null when this moto starts it.</summary>
  private int? OverallOfGroup(DayPlan plan, PlannedSession session)
  {
    if (session.OverallGroup is not { } group || session.IsTimed) return null;

    var runs = PlanRuns();
    foreach (var earlier in plan.Sessions.TakeWhile(s => s != session))
    {
      if (!string.Equals(earlier.OverallGroup, group, StringComparison.OrdinalIgnoreCase)) continue;
      if (runs.TryGetValue(earlier.Id, out var run) && _raceDb.OverallFor(run.RaceId) is { } overall)
        return overall.Id;
    }
    return null;
  }

  /// <summary>
  /// Set up now, from the strip or the big button: the operator's word skips
  /// the waiting, but never sets up over a running session or under an open window.
  /// </summary>
  private void SetUpNextPlannedByHand()
  {
    if (_activePlan == null) return;

    var now = DateTime.Now;
    var forecast = PlanForecastNow(now);
    var screen = PlanScreenNow();
    var next = PlanForecast.Next(forecast);

    switch (PlanRunnerPolicy.CanSetUpByHand(forecast, screen, WindowOpenOverMain()))
    {
      case PlanWait.SessionRunning:
        MessageBox.Show(this, "A session is still running. End it first, then set up the next one.",
          "Set up the next session", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;

      case PlanWait.NothingLeft:
        MessageBox.Show(this, "Every session of today's plan has run. Race > New race... sets up another one.",
          "Set up the next session", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;

      case PlanWait.None when next != null:
        // A session that is not the plan's goes away for good if it never
        // started; say so before it does.
        if (!screen.Empty && !screen.FromPlan && !raceFinished && currentRaceId == null)
        {
          var answer = MessageBox.Show(this,
            $"The session set up on screen is not from the plan. Set up {next.Session.Name} instead?",
            "Set up the next session", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
          if (answer != DialogResult.Yes) return;
        }
        SetUpPlanned(next, byHand: true);
        return;
    }
  }

  private void SnoozePlan()
  {
    _planSnoozedUntil = DateTime.Now + PlanTimings.Snooze;
    _planCountdownFrom = null;
    AddMessage($"📋 Not yet: the next session is set up in {PlanTimings.Snooze.TotalMinutes:0} minutes at the earliest.");
    PlanTick();
  }

  /// <summary>Skips the session the day is waiting for, or one that was interrupted, after asking.</summary>
  private void SkipPlannedSession()
  {
    if (_activePlan is not { } plan) return;

    var forecast = PlanForecastNow(DateTime.Now);
    var target = forecast.FirstOrDefault(e => e.Status == PlanEntryStatus.Interrupted) ?? PlanForecast.Next(forecast);
    if (target == null) return;

    var answer = MessageBox.Show(this,
      $"Skip {target.Session.Name}?\n\nIt stays on the timetable, marked as skipped. The sessions after it " +
      "keep their times. Change times... undoes it.",
      "Skip a session", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
    if (answer != DialogResult.Yes) return;

    target.Session.Skipped = true;
    SavePlan(plan);

    // Set up but never started: it goes, and the next one takes its place in time.
    if (target.Status == PlanEntryStatus.Ready && !raceStarted)
      ResetForNextSession();

    AddMessage($"📋 {target.Session.Name} skipped.");
    PlanTick();
  }

  private void SavePlan(DayPlan plan)
  {
    try
    {
      _plans.Save(plan);
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(this, "The plan could not be saved.",
        "The change is in use until the application closes, but it is not on disk. " +
        "Check the disk is not full, then change something again to save it.", ex);
    }
    InvalidatePlanRuns();
  }

  // ---- The editor ---------------------------------------------------------

  /// <summary>Race > Plan the day...</summary>
  private void ShowPlanDialog(bool focusNext = false)
  {
    using var dialog = new PlanDialog(new PlanHost(this), _activePlan, focusNext);
    dialog.ShowDialog(this);
    InvalidatePlanRuns();
    PlanTick();
  }

  /// <summary>What the plan window may ask of the form.</summary>
  private sealed class PlanHost : IPlanHost
  {
    private readonly Form1 _form;
    public PlanHost(Form1 form) => _form = form;

    public PlanStore Store => _form._plans;
    public DayPlan? ActivePlan => _form._activePlan;
    public IReadOnlyList<TrackDefinition> Tracks => _form._trackStore.Tracks;
    public string? LoadedRiderList => _form._settings.LastRiderListPath;
    public IReadOnlyList<string> ClassesOf(string? riderList) => _form.PlanClassesOf(riderList);
    public IReadOnlyDictionary<string, PlanRun> RunsOf(DayPlan plan) =>
      plan == _form._activePlan ? _form.PlanRuns() : _form._raceDb.RunsForPlan(plan.Id);
    public void Save(DayPlan plan) => _form.SavePlan(plan);
    public bool PublishingConfigured => _form.PublishingAvailable;
    public bool LiveConfigured => _form.LiveTimingConfigured;
    public void Activate(DayPlan plan) => _form.ActivatePlan(plan);
    public void Deactivate() => _form.DeactivatePlan();
  }

  /// <summary>
  /// The classes of a rider list, for picking a session's classes. The loaded
  /// list when it is the one asked for; otherwise read from the file on its
  /// own, so planning next week's day never disturbs today's riders.
  /// </summary>
  private IReadOnlyList<string> PlanClassesOf(string? riderList)
  {
    if (string.IsNullOrEmpty(riderList) ||
        string.Equals(riderList, _settings.LastRiderListPath, StringComparison.OrdinalIgnoreCase))
      return RosterClasses();

    try
    {
      if (!File.Exists(riderList)) return Array.Empty<string>();
      var reader = new RiderDataImporter();
      if (riderList.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) reader.ImportFromCsvDetailed(riderList);
      else reader.ImportFromExcelDetailed(riderList);

      return reader.GetAllRiderData().Values
        .Select(r => r.Category)
        .Where(c => !string.IsNullOrWhiteSpace(c))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    }
    catch (Exception)
    {
      return Array.Empty<string>();
    }
  }
}
