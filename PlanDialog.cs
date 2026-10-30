namespace CrossMgrInterface;

/// <summary>What the Plan the day window needs from the application.</summary>
public interface IPlanHost
{
  PlanStore Store { get; }

  /// <summary>The plan in use today, if any.</summary>
  DayPlan? ActivePlan { get; }

  IReadOnlyList<TrackDefinition> Tracks { get; }

  /// <summary>The rider list loaded now, offered for a new plan.</summary>
  string? LoadedRiderList { get; }

  /// <summary>The classes on a rider list, to tick for a session.</summary>
  IReadOnlyList<string> ClassesOf(string? riderList);

  /// <summary>What has run of a plan, from the race database.</summary>
  IReadOnlyDictionary<string, PlanRun> RunsOf(DayPlan plan);

  void Save(DayPlan plan);

  /// <summary>The club's presets, and the website for short codes (null when none is set up).</summary>
  PresetStore Presets { get; }
  IPresetWebsite? Website { get; }

  /// <summary>A results website / live timing is set up, so those switches mean something.</summary>
  bool PublishingConfigured { get; }
  bool LiveConfigured { get; }

  void Activate(DayPlan plan);
  void Deactivate();
}

/// <summary>
/// The day's timetable, made the evening before - or, better, copied from the
/// last club day and changed where it differs, which is how clubs work: the
/// same sessions in the same order most weekends.
///
/// Every change is saved as it is made; there is no Save button to forget.
/// On the day the same window changes the times, and a typed time is kept
/// beside the planned one (see PlannedSession.TypedTime).
///
/// Hand-built like the other dialogs, so the designer never rewrites it.
/// </summary>
public sealed class PlanDialog : Form
{
  private readonly IPlanHost _host;
  private DayPlan? _plan;
  private bool _loading;

  private readonly ComboBox _picker = new();
  private readonly DateTimePicker _date = new();
  private readonly TextBox _name = new();
  private readonly Label _riderList = new();
  private readonly ComboBox _track = new();
  private readonly NumericUpDown _changeover = new();
  private readonly NumericUpDown _lap = new();
  private readonly Label _afterSummary = new();
  private readonly DataGridView _grid = new();
  private readonly Label _problems = new();
  private readonly Label _empty = new();
  private readonly Panel _editor = new();
  private readonly Button _use = new();
  private readonly Button _add = new();
  private readonly Button _moto2 = new();
  private readonly Button _edit = new();
  private readonly Button _up = new();
  private readonly Button _down = new();
  private readonly Button _skip = new();
  private readonly Button _remove = new();
  private readonly Button _copy = new();
  private readonly Button _export = new();
  private readonly Button _delete = new();
  private readonly Button _savePreset = new();
  private readonly Button _match = new();

  private List<PlannedSession> _rows = new();
  private IReadOnlyDictionary<string, PlanRun> _runs = new Dictionary<string, PlanRun>();

  public PlanDialog(IPlanHost host, DayPlan? select, bool focusNext)
  {
    _host = host;

    Text = "Plan the day";
    FormBorderStyle = FormBorderStyle.Sizable;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = true;
    ShowInTaskbar = false;
    ClientSize = new Size(1120, 660);
    MinimumSize = new Size(900, 480);
    Font = new Font("Segoe UI", 9.5F);

    var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12) };
    root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    root.Controls.Add(BuildPicker(), 0, 0);

    _editor.Dock = DockStyle.Fill;
    _editor.Controls.Add(BuildEditor());
    _empty.Dock = DockStyle.Fill;
    _empty.TextAlign = ContentAlignment.MiddleCenter;
    _empty.ForeColor = Color.DimGray;
    _empty.Font = new Font("Segoe UI", 11F);
    _empty.Text = "No day has been planned yet.\n\nPress New for an empty plan. Next time, Copy last time's plan " +
                  "and change only what is different.";
    var body = new Panel { Dock = DockStyle.Fill };
    body.Controls.Add(_editor);
    body.Controls.Add(_empty);
    root.Controls.Add(body, 0, 1);

    Controls.Add(root);

    Load += (_, _) =>
    {
      ReloadPicker(select ?? host.ActivePlan ?? host.Store.Newest().FirstOrDefault());
      if (focusNext) SelectNext();
    };
  }

  /// <summary>The plan being edited, for the help picture and the tests.</summary>
  public DayPlan? Plan => _plan;

  // ---- Layout ---------------------------------------------------------------

  private Control BuildPicker()
  {
    var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Padding = new Padding(0, 0, 0, 8) };
    row.Controls.Add(new Label { Text = "Plan:", AutoSize = true, Margin = new Padding(0, 7, 6, 0), Font = new Font(Font, FontStyle.Bold) });

    _picker.DropDownStyle = ComboBoxStyle.DropDownList;
    _picker.Width = 330;
    _picker.SelectedIndexChanged += (_, _) =>
    {
      if (!_loading && _picker.SelectedItem is PlanItem item) Show(item.Plan);
    };
    row.Controls.Add(_picker);

    var create = SmallButton("New...", (_, _) => NewPlan());
    Configure(_copy, "Copy...", (_, _) => CopyPlan());
    var import = SmallButton("Import...", (_, _) => ImportPlan());
    Configure(_export, "Export...", (_, _) => ExportPlan());
    Configure(_delete, "Delete...", (_, _) => DeletePlan());
    Configure(_savePreset, "Save as preset...", (_, _) => SaveAsPreset());
    row.Controls.AddRange(new Control[] { create, _copy, import, _export, _delete, _savePreset });
    return row;

    Button SmallButton(string text, EventHandler click)
    {
      var button = new Button();
      Configure(button, text, click);
      return button;
    }

    static void Configure(Button button, string text, EventHandler click)
    {
      button.Text = text;
      button.AutoSize = true;
      button.MinimumSize = new Size(80, 30);
      button.Margin = new Padding(6, 0, 0, 0);
      button.Click += click;
    }
  }

  private Control BuildEditor()
  {
    var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

    var header = BuildHeader();
    layout.Controls.Add(header, 0, 0);
    layout.SetColumnSpan(header, 2);
    layout.Controls.Add(BuildGrid(), 0, 1);
    layout.Controls.Add(BuildButtons(), 1, 1);

    _problems.AutoSize = true;
    _problems.MaximumSize = new Size(900, 0);
    _problems.ForeColor = Color.FromArgb(160, 60, 0);
    _problems.Padding = new Padding(0, 6, 0, 0);
    layout.Controls.Add(_problems, 0, 2);
    layout.SetColumnSpan(_problems, 2);
    return layout;
  }

  private Control BuildHeader()
  {
    var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, Padding = new Padding(0, 0, 0, 8) };
    header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
    header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

    _date.Format = DateTimePickerFormat.Long;
    _date.Width = 220;
    _date.ValueChanged += (_, _) => Change(p => p.Date = _date.Value.Date);
    _name.Dock = DockStyle.Fill;
    _name.Leave += (_, _) => Change(p => p.Name = _name.Text.Trim(), refreshPicker: true);
    Add(0, "Day", _date);
    Add(2, "Name", _name);

    var listRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
    _riderList.AutoSize = true;
    _riderList.MaximumSize = new Size(360, 0);
    _riderList.AutoEllipsis = true;
    _riderList.Margin = new Padding(0, 6, 8, 0);
    var choose = new Button { Text = "Choose...", AutoSize = true };
    choose.Click += (_, _) => ChooseRiderList();
    listRow.Controls.Add(_riderList);
    listRow.Controls.Add(choose);
    Add(0, "Rider list", listRow);

    _track.DropDownStyle = ComboBoxStyle.DropDownList;
    _track.Width = 260;
    _track.SelectedIndexChanged += (_, _) =>
      Change(p => p.TrackId = _track.SelectedItem is TrackItem t ? t.Track?.Id : null);
    Add(2, "Circuit", _track);

    _changeover.Minimum = 0;
    _changeover.Maximum = 60;
    _changeover.Width = 70;
    _changeover.ValueChanged += (_, _) => Change(p => p.ChangeoverMinutes = (int)_changeover.Value);
    Add(0, "Between sessions", WithUnit(_changeover, "minutes at least, from one finish to the next start"));

    _lap.Minimum = 20;
    _lap.Maximum = 3600;
    _lap.Increment = 10;
    _lap.Width = 70;
    _lap.ValueChanged += (_, _) => Change(p => p.TypicalLapSeconds = (int)_lap.Value);
    Add(2, "Typical lap", WithUnit(_lap, "seconds - for guessing when a race ends"));

    var afterRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
    _afterSummary.AutoSize = true;
    _afterSummary.Margin = new Padding(0, 6, 8, 0);
    var afterButton = new Button { Text = "Change...", AutoSize = true };
    afterButton.Click += (_, _) => ChangeAfterSession();
    afterRow.Controls.Add(_afterSummary);
    afterRow.Controls.Add(afterButton);
    Add(0, "After each session", afterRow);
    header.SetColumnSpan(afterRow, 3);
    return header;

    void Add(int column, string caption, Control control)
    {
      header.Controls.Add(new Label
      {
        Text = caption,
        AutoSize = true,
        Margin = new Padding(column == 0 ? 0 : 16, 7, 8, 3),
        Font = new Font(Font, FontStyle.Bold)
      });
      control.Margin = new Padding(0, 3, 0, 3);
      header.Controls.Add(control);
    }

    static Control WithUnit(Control box, string unit)
    {
      var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
      row.Controls.Add(box);
      row.Controls.Add(new Label { Text = unit, AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(6, 6, 0, 0) });
      return row;
    }
  }

  private Control BuildGrid()
  {
    _grid.Dock = DockStyle.Fill;
    _grid.ReadOnly = true;
    _grid.AllowUserToAddRows = false;
    _grid.AllowUserToDeleteRows = false;
    _grid.AllowUserToResizeRows = false;
    _grid.RowHeadersVisible = false;
    _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
    _grid.MultiSelect = false;
    _grid.BackgroundColor = Color.White;
    _grid.BorderStyle = BorderStyle.FixedSingle;
    _grid.Font = new Font("Segoe UI", 10F);
    _grid.RowTemplate.Height = 28;
    _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
    _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;

    foreach (var (header, width) in new[]
             {
               ("Time", 60), ("Name", 200), ("Type", 110), ("Classes", 120), ("Length", 70),
               ("Extra laps", 70), ("Clock starts", 105), ("Overall", 100), ("Today", 120)
             })
    {
      var index = _grid.Columns.Add(header, header);
      _grid.Columns[index].Width = width;
      _grid.Columns[index].SortMode = DataGridViewColumnSortMode.NotSortable;
    }

    _grid.SelectionChanged += (_, _) => UpdateButtons();
    _grid.CellDoubleClick += (_, e) =>
    {
      if (e.RowIndex >= 0) EditSelected();
    };
    return _grid;
  }

  private Control BuildButtons()
  {
    var column = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill,
      FlowDirection = FlowDirection.TopDown,
      WrapContents = false,
      Padding = new Padding(12, 0, 0, 0)
    };

    Configure(_add, "Add session...", AddSession);
    Configure(_moto2, "Add Moto 2", AddNextMoto);
    Configure(_edit, "Edit...", EditSelected);
    Configure(_up, "Move up", () => MoveSelected(-1));
    Configure(_down, "Move down", () => MoveSelected(+1));
    Configure(_skip, "Skip", ToggleSkip);
    Configure(_remove, "Remove...", RemoveSelected);

    _use.Width = 160;
    _use.Height = 44;
    _use.Margin = new Padding(0, 24, 0, 8);
    _use.Font = new Font(Font, FontStyle.Bold);
    _use.Click += (_, _) => UseOrStop();

    var close = new Button { Text = "Close", Width = 160, Height = 34, DialogResult = DialogResult.Cancel };
    Configure(_match, "Match classes...", MatchClasses);
    _match.Visible = false;
    column.Controls.AddRange(new Control[] { _add, _moto2, _edit, _up, _down, _skip, _remove, _match, _use, close });
    CancelButton = close;
    return column;

    static void Configure(Button button, string text, Action click)
    {
      button.Text = text;
      button.Width = 160;
      button.Height = 34;
      button.Margin = new Padding(0, 0, 0, 8);
      button.Click += (_, _) => click();
    }
  }

  // ---- Showing a plan ---------------------------------------------------------

  private sealed record PlanItem(DayPlan Plan)
  {
    public override string ToString() =>
      $"{Plan.Date:ddd dd.MM.yyyy}  ·  {(Plan.Name.Length > 0 ? Plan.Name : "(no name)")}";
  }

  private sealed record TrackItem(TrackDefinition? Track)
  {
    public override string ToString() => Track?.Name ?? "(no circuit)";
  }

  private void ReloadPicker(DayPlan? select)
  {
    _loading = true;
    _picker.Items.Clear();
    foreach (var plan in _host.Store.Newest()) _picker.Items.Add(new PlanItem(plan));
    _loading = false;

    var index = select == null ? -1 : _picker.Items.Cast<PlanItem>().ToList().FindIndex(i => i.Plan == select);
    if (index < 0 && _picker.Items.Count > 0) index = 0;

    if (index >= 0) _picker.SelectedIndex = index;
    else Show(null);
  }

  private void Show(DayPlan? plan)
  {
    _plan = plan;
    _editor.Visible = plan != null;
    _empty.Visible = plan == null;
    _copy.Enabled = _export.Enabled = _delete.Enabled = _savePreset.Enabled = plan != null;
    if (plan == null) return;

    _loading = true;
    _date.Value = plan.Date.Date;
    _name.Text = plan.Name;
    _riderList.Text = string.IsNullOrEmpty(plan.RiderListPath) ? "(none chosen)" : Path.GetFileName(plan.RiderListPath);
    _track.Items.Clear();
    _track.Items.Add(new TrackItem(null));
    foreach (var track in _host.Tracks) _track.Items.Add(new TrackItem(track));
    _track.SelectedIndex = Math.Max(0, _host.Tracks.ToList().FindIndex(t => t.Id == plan.TrackId) + 1);
    _changeover.Value = Math.Clamp(plan.ChangeoverMinutes, 0, 60);
    _lap.Value = Math.Clamp(plan.TypicalLapSeconds, 20, 3600);
    _afterSummary.Text = AfterSummary(plan.After);
    _loading = false;

    ShowSessions();
  }

  private bool InUse => _plan != null && _plan == _host.ActivePlan;

  private void ShowSessions(PlannedSession? select = null)
  {
    if (_plan == null) return;

    var keep = select ?? Selected;
    _runs = _host.RunsOf(_plan);
    var forecast = PlanForecast.Forecast(_plan, _runs, null, null, DateTime.Now);
    _rows = _plan.Sessions.ToList();

    _grid.Rows.Clear();
    foreach (var entry in forecast)
    {
      var s = entry.Session;
      var index = _grid.Rows.Add(
        s.TypedTime is { } typed ? $"{typed:hh\\:mm}*" : $"{s.Time:hh\\:mm}",
        s.Name,
        s.SessionType switch
        {
          SessionType.TimedQualifying => "Timed qualifying",
          SessionType.FreePractice => "Free practice",
          _ => s.TeamEvent ? "Race (teams)" : "Race"
        },
        s.TeamEvent ? "Teams" : s.ClassesText,
        $"{s.DurationMinutes} min",
        s.IsTimed ? "-" : s.AdditionalLaps.ToString(),
        s.Start switch
        {
          PlannedStartMode.FirstRider => "First rider",
          PlannedStartMode.Waves => $"Waves, {s.WaveGapSeconds} s",
          _ => "START RACE"
        },
        s.OverallGroup ?? "",
        Today(entry));

      var row = _grid.Rows[index];
      if (s.Skipped) row.DefaultCellStyle.ForeColor = Color.Silver;
      else if (entry.Status == PlanEntryStatus.Finished) row.DefaultCellStyle.ForeColor = Color.FromArgb(0, 110, 45);
      else if (entry.Status == PlanEntryStatus.Interrupted) row.DefaultCellStyle.ForeColor = Color.FromArgb(160, 30, 20);
    }

    var at = keep == null ? -1 : _rows.IndexOf(keep);
    _grid.ClearSelection();
    if (at >= 0 && at < _grid.Rows.Count)
    {
      _grid.Rows[at].Selected = true;
      _grid.CurrentCell = _grid.Rows[at].Cells[1];
    }

    var listClasses = _host.ClassesOf(_plan.RiderListPath);
    var problems = _plan.Problems(listClasses);
    _match.Visible = listClasses.Count > 0 && ClassMapping.Unmatched(PendingPart(_plan), listClasses).Count > 0;
    _problems.Text = problems.Count == 0 ? "" : "⚠ " + string.Join("\n⚠ ", problems);

    var today = _plan.Date.Date == DateTime.Today;
    _use.Text = InUse ? "Stop using this plan" : today ? "Use this plan today" : "Use it today...";
    UpdateButtons();
  }

  /// <summary>What has become of a session so far, when the plan is for today.</summary>
  private string Today(ForecastEntry entry) => entry.Status switch
  {
    PlanEntryStatus.Finished => $"✓ ran {entry.ExpectedStart:HH:mm}",
    PlanEntryStatus.Running => $"▶ running since {entry.ExpectedStart:HH:mm}",
    PlanEntryStatus.Interrupted => "⚠ interrupted",
    PlanEntryStatus.Skipped => "skipped",
    _ when InUse && entry.Late >= TimeSpan.FromMinutes(1) => $"~{entry.ExpectedStart:HH:mm}",
    _ => ""
  };

  private PlannedSession? Selected =>
    _grid.SelectedRows.Count == 1 && _grid.SelectedRows[0].Index < _rows.Count ? _rows[_grid.SelectedRows[0].Index] : null;

  /// <summary>Whether a session has run: what ran stays as it ran, and is changed in Past sessions.</summary>
  private bool HasRun(PlannedSession session) => _runs.ContainsKey(session.Id);

  private void UpdateButtons()
  {
    var selected = Selected;
    var editable = selected != null && !HasRun(selected);
    _edit.Enabled = editable;
    _moto2.Enabled = selected is { IsTimed: false };
    _remove.Enabled = editable;
    _skip.Enabled = selected != null && (!HasRun(selected) || _runs[selected.Id] is { Finished: false });
    _skip.Text = selected?.Skipped == true ? "Unskip" : "Skip";

    var index = selected == null ? -1 : _rows.IndexOf(selected);
    _up.Enabled = editable && index > 0 && !HasRun(_rows[index - 1]);
    _down.Enabled = editable && index >= 0 && index < _rows.Count - 1;
  }

  private void SelectNext()
  {
    if (_plan == null) return;
    var next = PlanForecast.Next(PlanForecast.Forecast(_plan, _host.RunsOf(_plan), null, null, DateTime.Now));
    if (next != null) ShowSessions(next.Session);
  }

  // ---- Changing a plan --------------------------------------------------------

  private void Change(Action<DayPlan> change, bool refreshPicker = false)
  {
    if (_loading || _plan == null) return;
    change(_plan);
    _host.Save(_plan);
    if (refreshPicker) ReloadPicker(_plan);
    else ShowSessions();
  }

  private IReadOnlyList<string> Groups() =>
    _plan?.Sessions.Select(s => s.OverallGroup).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList()
    ?? (IReadOnlyList<string>)Array.Empty<string>();

  private bool EditSession(PlannedSession session, string title)
  {
    using var dialog = new PlannedSessionDialog(session, InUse, _host.ClassesOf(_plan!.RiderListPath), Groups(), title);
    return dialog.ShowDialog(this) == DialogResult.OK;
  }

  /// <summary>Half past the end of the last session, rounded up to five minutes: where a new one usually goes.</summary>
  private TimeSpan AfterLast()
  {
    var last = _plan!.Sessions.LastOrDefault(s => !s.Skipped);
    if (last == null) return TimeSpan.FromHours(9);

    var end = (last.TypedTime ?? last.Time) + _plan.PlannedLength(last) + TimeSpan.FromMinutes(_plan.ChangeoverMinutes);
    var minutes = (int)Math.Ceiling(end.TotalMinutes / 5) * 5;
    return TimeSpan.FromMinutes(Math.Min(minutes, 23 * 60 + 55));
  }

  private void AddSession()
  {
    if (_plan == null) return;
    var session = new PlannedSession { Time = AfterLast(), Name = "" };
    if (_plan.Sessions.LastOrDefault() is { } last)
    {
      session.DurationMinutes = last.DurationMinutes;
      session.AdditionalLaps = last.AdditionalLaps;
      session.Start = last.Start;
    }

    if (!EditSession(session, "Add a session")) return;
    _plan.Sessions.Add(session);
    _host.Save(_plan);
    ShowSessions(session);
  }

  private void AddNextMoto()
  {
    if (_plan == null || Selected is not { } moto1) return;

    var moto2 = moto1.NextMoto();
    moto2.Time = AfterLast();

    // The first moto joins the overall too, if it had none.
    moto1.OverallGroup ??= moto2.OverallGroup;
    _plan.Sessions.Add(moto2);
    _host.Save(_plan);
    ShowSessions(moto2);
  }

  private void EditSelected()
  {
    if (_plan == null || Selected is not { } session) return;
    if (HasRun(session))
    {
      MessageBox.Show(this, $"{session.Name} has already run. Its results are changed in Race > Past sessions...",
        Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
      return;
    }

    if (!EditSession(session, $"Change {session.Name}")) return;
    _host.Save(_plan);
    ShowSessions(session);
  }

  private void MoveSelected(int by)
  {
    if (_plan == null || Selected is not { } session) return;
    var index = _plan.Sessions.IndexOf(session);
    var to = index + by;
    if (to < 0 || to >= _plan.Sessions.Count) return;

    (_plan.Sessions[index], _plan.Sessions[to]) = (_plan.Sessions[to], _plan.Sessions[index]);
    _host.Save(_plan);
    ShowSessions(session);
  }

  private void ToggleSkip()
  {
    if (_plan == null || Selected is not { } session) return;
    session.Skipped = !session.Skipped;
    _host.Save(_plan);
    ShowSessions(session);
  }

  private void RemoveSelected()
  {
    if (_plan == null || Selected is not { } session) return;
    var answer = MessageBox.Show(this, $"Remove {session.Name} from the plan?", Text,
      MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
    if (answer != DialogResult.Yes) return;

    _plan.Sessions.Remove(session);
    _host.Save(_plan);
    ShowSessions();
  }

  /// <summary>"publish · save sheets (PDF) · live timing", or "nothing by itself".</summary>
  private string AfterSummary(AfterSession after)
  {
    var parts = new List<string>();
    if (after.Publish && _host.PublishingConfigured) parts.Add("publish");
    if (after.SaveSheets) parts.Add(after.Pdf ? "save sheets and PDF" : "save sheets");
    if (after.LiveTiming && _host.LiveConfigured) parts.Add("live timing");
    return parts.Count == 0 ? "nothing by itself" : string.Join(" · ", parts);
  }

  private void ChangeAfterSession()
  {
    if (_plan == null) return;
    using var dialog = new AfterSessionDialog(_plan.After, _host.PublishingConfigured, _host.LiveConfigured, _plan.Name);
    if (dialog.ShowDialog(this) != DialogResult.OK) return;
    _host.Save(_plan);
    _afterSummary.Text = AfterSummary(_plan.After);
  }

  private void ChooseRiderList()
  {
    if (_plan == null) return;
    using var open = new OpenFileDialog
    {
      Title = "The day's rider list",
      Filter = "Rider lists (*.xlsx;*.csv)|*.xlsx;*.csv|All files (*.*)|*.*"
    };
    if (!string.IsNullOrEmpty(_plan.RiderListPath)) open.FileName = _plan.RiderListPath;
    if (open.ShowDialog(this) != DialogResult.OK) return;

    _plan.RiderListPath = open.FileName;
    _host.Save(_plan);
    Show(_plan);

    if (InUse)
      MessageBox.Show(this, "The new rider list is loaded when the next session is set up. " +
                            "Stop using the plan and use it again to load it now.",
        Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
  }

  // ---- Plans as a whole ---------------------------------------------------------

  /// <summary>Empty, from a preset, or from a code - then the classes matched to the rider list.</summary>
  private void NewPlan()
  {
    using var dialog = new NewPlanDialog(_host.Presets, _host.LoadedRiderList,
      _host.Website == null ? null : _host.Website.FetchAsync);
    dialog.ManagePresets += (_, _) =>
    {
      using var presets = new PresetsDialog(_host.Presets, _host.Website);
      presets.ShowDialog(dialog);
    };
    if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is not { } plan) return;

    plan.TrackId ??= _host.Tracks.Count == 1 ? _host.Tracks[0].Id : null;
    _host.Save(plan);
    ReloadPicker(plan);
    if (ClassMapping.Unmatched(plan, _host.ClassesOf(plan.RiderListPath)).Count > 0) MatchClasses();
  }

  /// <summary>The sessions still to run: those that have run keep the classes they ran with.</summary>
  private DayPlan PendingPart(DayPlan plan)
  {
    var runs = _host.RunsOf(plan);
    var pending = new DayPlan { Name = plan.Name };
    pending.Sessions.AddRange(plan.Sessions.Where(s => !runs.ContainsKey(s.Id)));
    return pending;
  }

  private void MatchClasses()
  {
    if (_plan == null) return;
    var listClasses = _host.ClassesOf(_plan.RiderListPath);
    if (listClasses.Count == 0)
    {
      MessageBox.Show(this, "Choose the plan's rider list first: its classes are what the plan's are matched to.",
        Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
      return;
    }

    var pending = PendingPart(_plan);
    using var dialog = new MatchClassesDialog(pending, listClasses);
    if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is not { } result) return;

    // The matched sessions back in their places; any taken out go.
    var matched = result.Plan.Sessions.ToDictionary(s => s.Id);
    var pendingIds = pending.Sessions.Select(s => s.Id).ToHashSet();
    _plan.Sessions = _plan.Sessions
      .Where(s => !pendingIds.Contains(s.Id) || matched.ContainsKey(s.Id))
      .Select(s => matched.GetValueOrDefault(s.Id) ?? s)
      .ToList();
    _host.Save(_plan);
    ShowSessions();
  }

  /// <summary>The plan into the club's presets, and its code to send.</summary>
  private void SaveAsPreset()
  {
    if (_plan == null) return;
    var name = TextPrompt.Ask(this, "Save as preset", _plan.Name, "The name the club's preset list shows for it.");
    if (name == null) return;
    var description = TextPrompt.Ask(this, "Save as preset", "", "A line to say what it is for. May be empty.") ?? "";

    var (preset, added) = _host.Presets.Add(name, description, DayPlanCode.Encode(_plan));
    if (added)
    {
      try
      {
        _host.Presets.Save();
      }
      catch (Exception ex)
      {
        ErrorDialog.Show(this, "The preset could not be saved.", "Check the disk is not full, then try again.", ex);
        return;
      }
    }
    else
    {
      MessageBox.Show(this, $"This plan is already in the preset list as '{preset.Name}'.", Text,
        MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    using var dialog = new PresetCodeDialog(preset.Name, DayPlanCode.Decode(preset.Code).Plan ?? _plan, preset.Code,
      preset, _host.Presets, _host.Website);
    dialog.ShowDialog(this);
  }


  private void CopyPlan()
  {
    if (_plan == null) return;
    var copy = _plan.CopyFor(DateTime.Today);
    _host.Save(copy);
    ReloadPicker(copy);
    MessageBox.Show(this, $"A copy for today has been made. Change the day, the name and whatever else is " +
                          "different this time.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
  }

  private void ImportPlan()
  {
    using var open = new OpenFileDialog { Title = "Import a day plan", Filter = "Day plans (*.cmplan)|*.cmplan" };
    if (open.ShowDialog(this) != DialogResult.OK) return;

    var plan = PlanStore.ImportJson(File.ReadAllText(open.FileName));
    if (plan == null)
    {
      MessageBox.Show(this, "That file is not a day plan.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
      return;
    }

    if (_host.Store.Find(plan.Id) is { } existing)
    {
      if (existing == _host.ActivePlan)
      {
        MessageBox.Show(this, "That plan is in use today. Stop using it first, then import it again.", Text,
          MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
      }

      var answer = MessageBox.Show(this, $"This computer already has '{existing.Name}'. Replace it with the imported one?",
        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
      if (answer != DialogResult.Yes) return;
      _host.Store.Delete(existing);
    }

    _host.Save(plan);
    ReloadPicker(plan);
  }

  private void ExportPlan()
  {
    if (_plan == null) return;
    using var save = new SaveFileDialog
    {
      Title = "Export the day plan",
      Filter = "Day plans (*.cmplan)|*.cmplan",
      FileName = ReportHelpers.SanitizeFileName($"{_plan.Date:yyyy-MM-dd} {_plan.Name}") + PlanStore.Extension
    };
    if (save.ShowDialog(this) != DialogResult.OK) return;

    File.WriteAllText(save.FileName, PlanStore.ExportJson(_plan));
  }

  private void DeletePlan()
  {
    if (_plan == null) return;
    if (InUse)
    {
      MessageBox.Show(this, "This plan is in use today. Stop using it first.", Text,
        MessageBoxButtons.OK, MessageBoxIcon.Information);
      return;
    }

    var answer = MessageBox.Show(this,
      $"Delete the plan '{_plan.Name}' for {_plan.Date:dd.MM.yyyy}?\n\nSessions it ran stay in Past sessions.",
      Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
    if (answer != DialogResult.Yes) return;

    _host.Store.Delete(_plan);
    ReloadPicker(null);
  }

  private void UseOrStop()
  {
    if (_plan == null) return;

    if (InUse)
    {
      var stop = MessageBox.Show(this,
        "Stop using this plan? Sessions are then set up by hand again. The plan itself is kept.",
        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
      if (stop != DialogResult.Yes) return;
      _host.Deactivate();
      ShowSessions();
      return;
    }

    if (_plan.Date.Date != DateTime.Today)
    {
      var move = MessageBox.Show(this,
        $"This plan is for {_plan.Date:dddd dd.MM.yyyy}. Use it today? Its day is changed to today.",
        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
      if (move != DialogResult.Yes) return;
      _plan.Date = DateTime.Today;
      _host.Save(_plan);
    }

    var problems = _plan.Problems(_host.ClassesOf(_plan.RiderListPath));
    if (problems.Count > 0)
    {
      var anyway = MessageBox.Show(this,
        "The plan has something worth fixing first:\n\n" + string.Join("\n", problems) + "\n\nUse it anyway?",
        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
      if (anyway != DialogResult.Yes) return;
    }

    if (_plan.Sessions.All(s => s.Skipped))
    {
      MessageBox.Show(this, "The plan has no sessions to run. Add some first.", Text,
        MessageBoxButtons.OK, MessageBoxIcon.Information);
      return;
    }

    _host.Activate(_plan);
    DialogResult = DialogResult.OK;
    Close();
  }
}
