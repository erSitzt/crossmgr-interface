namespace CrossMgrInterface;

/// <summary>
/// Starting a day plan: empty, from one of the application's presets, from
/// one of the club's own, or from a code someone sent. Then the day, a name,
/// the time of the first session - every time moves with it - and the rider
/// list. Classes the rider list does not have are matched afterwards (see
/// MatchClassesDialog).
///
/// Hand-built like the other dialogs, so the designer never rewrites it.
/// </summary>
public sealed class NewPlanDialog : Form
{
  private readonly PresetStore _store;
  private readonly Func<string, Task<FoundPlanCode?>>? _resolveShortCode;
  private readonly ListBox _sources = new();
  private readonly Label _title = new();
  private readonly Label _description = new();
  private readonly ListBox _sessions = new();
  private readonly TextBox _paste = new();
  private readonly Label _pasteStatus = new();
  private readonly DateTimePicker _day = new();
  private readonly TextBox _name = new();
  private readonly DateTimePicker _first = new();
  private readonly Label _riderList = new();
  private readonly Button _ok = new();

  private string? _riderListPath;
  private DayPlan? _fromCode;
  private string _fromCodeName = "";
  private int _pasteVersion;

  /// <summary>The new plan, once OK has been pressed.</summary>
  public DayPlan? Result { get; private set; }

  /// <summary>Opens the club's preset list, then comes back here.</summary>
  public event EventHandler? ManagePresets;

  private sealed record Source(string Label, string Name, string Description, DayPlan? Plan)
  {
    public override string ToString() => Label;
  }

  /// <param name="resolveShortCode">Looks a short code up on the website; null when none is set up.</param>
  public NewPlanDialog(PresetStore store, string? riderListPath, Func<string, Task<FoundPlanCode?>>? resolveShortCode = null)
  {
    _store = store;
    _riderListPath = riderListPath;
    _resolveShortCode = resolveShortCode;

    Text = "New day plan";
    FormBorderStyle = FormBorderStyle.FixedDialog;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = false;
    ShowInTaskbar = false;
    ClientSize = new Size(900, 600);
    Font = new Font("Segoe UI", 9.5F);

    var bold = new Font(Font, FontStyle.Bold);
    Controls.Add(new Label { Text = "Start from", Location = new Point(16, 14), AutoSize = true, Font = bold });
    _sources.Location = new Point(16, 38);
    _sources.Size = new Size(300, 250);
    _sources.IntegralHeight = false;
    _sources.Font = new Font("Segoe UI", 10.5F);
    _sources.SelectedIndexChanged += (_, _) =>
    {
      if (_sources.SelectedItem is Source source) ShowSource(source);
    };
    Controls.Add(_sources);

    var manage = new LinkLabel { Text = "The club's presets...", Location = new Point(16, 294), AutoSize = true };
    manage.LinkClicked += (_, _) =>
    {
      ManagePresets?.Invoke(this, EventArgs.Empty);
      LoadSources();
    };
    Controls.Add(manage);

    Controls.Add(new Label { Text = "Or paste a code", Location = new Point(16, 324), AutoSize = true, Font = bold });
    _paste.Location = new Point(16, 348);
    _paste.Size = new Size(300, 74);
    _paste.Multiline = true;
    _paste.ScrollBars = ScrollBars.Vertical;
    _paste.Font = new Font("Consolas", 9F);
    _paste.TextChanged += async (_, _) => await ReadPastedAsync();
    Controls.Add(_paste);
    _pasteStatus.Location = new Point(16, 426);
    _pasteStatus.Size = new Size(300, 40);
    Controls.Add(_pasteStatus);

    _title.Location = new Point(340, 14);
    _title.Size = new Size(540, 26);
    _title.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
    _description.Location = new Point(340, 42);
    _description.Size = new Size(540, 54);
    _description.ForeColor = Color.DimGray;
    _sessions.Location = new Point(340, 100);
    _sessions.Size = new Size(540, 366);
    _sessions.IntegralHeight = false;
    _sessions.SelectionMode = SelectionMode.None;
    _sessions.Font = new Font("Segoe UI", 9.5F);
    Controls.AddRange(new Control[] { _title, _description, _sessions });

    // The day, the name, the first time, the rider list.
    var y = 486;
    Controls.Add(new Label { Text = "Day", Location = new Point(16, y + 4), AutoSize = true, Font = bold });
    _day.Location = new Point(110, y);
    _day.Width = 236;
    _day.Format = DateTimePickerFormat.Long;
    Controls.Add(_day);

    Controls.Add(new Label { Text = "Name", Location = new Point(370, y + 4), AutoSize = true, Font = bold });
    _name.Location = new Point(450, y);
    _name.Width = 300;
    Controls.Add(_name);

    y += 34;
    Controls.Add(new Label { Text = "First session", Location = new Point(16, y + 4), AutoSize = true, Font = bold });
    _first.Location = new Point(110, y);
    _first.Width = 80;
    _first.Format = DateTimePickerFormat.Custom;
    _first.CustomFormat = "HH:mm";
    _first.ShowUpDown = true;
    Controls.Add(_first);
    Controls.Add(new Label { Text = "the others move with it", Location = new Point(196, y + 4), AutoSize = true, ForeColor = Color.DimGray });

    Controls.Add(new Label { Text = "Rider list", Location = new Point(370, y + 4), AutoSize = true, Font = bold });
    _riderList.Location = new Point(450, y + 4);
    _riderList.Size = new Size(190, 20);
    _riderList.AutoEllipsis = true;
    Controls.Add(_riderList);
    var choose = new Button { Text = "Choose...", Location = new Point(646, y), AutoSize = true };
    choose.Click += (_, _) => ChooseRiderList();
    Controls.Add(choose);

    _ok.Text = "Create plan";
    _ok.Size = new Size(110, 30);
    _ok.Location = new Point(ClientSize.Width - 236, ClientSize.Height - 42);
    _ok.Click += (_, _) => Accept();
    var cancel = new Button
    {
      Text = "Cancel", Size = new Size(100, 30), Location = new Point(ClientSize.Width - 116, ClientSize.Height - 42),
      DialogResult = DialogResult.Cancel
    };
    Controls.AddRange(new Control[] { _ok, cancel });
    AcceptButton = _ok;
    CancelButton = cancel;

    ShowRiderList();
    LoadSources();
  }

  private void LoadSources()
  {
    var keep = (_sources.SelectedItem as Source)?.Label;
    _sources.Items.Clear();
    _sources.Items.Add(new Source("Empty plan", "Race day", "Start with no sessions and add them one by one.", null));
    foreach (var preset in PresetCatalog.All)
      _sources.Items.Add(new Source($"{preset.Name}  (built in)", preset.Name, preset.Description, preset.Plan));
    foreach (var preset in _store.Presets)
    {
      var plan = DayPlanCode.Decode(preset.Code).Plan;
      _sources.Items.Add(new Source(plan == null ? $"{preset.Name}  (cannot be read)" : preset.Name, preset.Name,
        plan == null ? "This preset was made with a newer version of the application." : preset.Description, plan));
    }

    var index = _sources.Items.Cast<Source>().ToList().FindIndex(s => s.Label == keep);
    _sources.SelectedIndex = index >= 0 ? index : 0;
  }

  /// <summary>The day the plan is for. Today unless set.</summary>
  [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
  public DateTime Day
  {
    get => _day.Value.Date;
    set => _day.Value = value.Date;
  }

  /// <summary>Selects one of the built-in presets by name, for the help picture.</summary>
  public void Select(string presetName)
  {
    var index = _sources.Items.Cast<Source>().ToList().FindIndex(s => s.Name == presetName);
    if (index >= 0) _sources.SelectedIndex = index;
  }

  private void ShowSource(Source source)
  {
    _fromCode = null;
    Show(source.Name, source.Description, source.Plan);
  }

  private void Show(string name, string description, DayPlan? plan)
  {
    _title.Text = name;
    _description.Text = description;
    _sessions.Items.Clear();
    foreach (var s in plan?.Sessions ?? new List<PlannedSession>())
      _sessions.Items.Add($"{s.Time:hh\\:mm}   {s.Name}   -   {Describe(s)}");
    if (plan is { Sessions.Count: 0 } || plan == null) _sessions.Items.Add("(no sessions)");

    _name.Text = plan == null ? "Race day" : name;
    var earliest = plan?.Sessions.Select(s => s.Time).DefaultIfEmpty(TimeSpan.FromHours(9)).Min() ?? TimeSpan.FromHours(9);
    _first.Value = DateTime.Today + earliest;
    _first.Enabled = plan is { Sessions.Count: > 0 };
    _ok.Enabled = plan != null || (_sources.SelectedItem as Source)?.Plan == null;
  }

  private static string Describe(PlannedSession s)
  {
    var type = s.SessionType switch
    {
      SessionType.TimedQualifying => "timed qualifying",
      SessionType.FreePractice => "free practice",
      _ => s.TeamEvent ? "team race" : "race"
    };
    var laps = s.IsTimed ? "" : s.AdditionalLaps == 0 ? ", no extra laps" : $" + {s.AdditionalLaps} lap{(s.AdditionalLaps == 1 ? "" : "s")}";
    var start = s.Start switch
    {
      PlannedStartMode.Waves => ", in waves",
      PlannedStartMode.Gate => ", START RACE",
      _ => ""
    };
    return $"{type}, {s.DurationMinutes} min{laps}{start}";
  }

  /// <summary>Reads what is pasted as it is typed: a plan code, or a short code from the website.</summary>
  private async Task ReadPastedAsync()
  {
    var version = ++_pasteVersion;
    var text = _paste.Text;
    _fromCode = null;

    if (string.IsNullOrWhiteSpace(text))
    {
      _pasteStatus.Text = "";
      if (_sources.SelectedItem is Source source) ShowSource(source);
      return;
    }

    FoundPlanCode? found = null;
    if (DayPlanCode.AsShortCode(text) is { } shortCode)
    {
      if (_store.FindShort(shortCode) is { } own)
        found = DayPlanCode.Decode(own.Code) with { LineText = own.Name };
      else if (PresetCatalog.All.FirstOrDefault(p => DayPlanCode.ShortCodeOf(p.Code) == shortCode) is { } builtIn)
        found = DayPlanCode.Decode(builtIn.Code) with { LineText = builtIn.Name };
      else if (_resolveShortCode != null)
      {
        Status(Color.DimGray, $"Looking {shortCode} up on the website...");
        found = await _resolveShortCode(shortCode);
        if (version != _pasteVersion) return;
      }
      else
      {
        Status(Color.DarkRed, "✗ A short code is looked up on the results website, which is not set up here. Paste the full code instead.");
        return;
      }

      if (found == null)
      {
        Status(Color.DarkRed, $"✗ No preset with the short code {shortCode} - check it, or paste the full code.");
        return;
      }
    }
    else
    {
      found = DayPlanCode.Decode(text);
    }

    if (found.Plan == null)
    {
      Status(Color.DarkRed, "✗ " + found.Error);
      return;
    }

    _fromCode = found.Plan;
    _fromCodeName = found.LineText.Length > 0 ? found.LineText : found.Plan.Name;
    Status(Color.FromArgb(0, 110, 45), $"✓ {_fromCodeName}: {DayPlanCode.Summary(found.Plan)}");
    _sources.ClearSelected();
    Show(_fromCodeName, "From the code.", found.Plan);
  }

  private void Status(Color colour, string text)
  {
    _pasteStatus.ForeColor = colour;
    _pasteStatus.Text = text;
  }

  private void ShowRiderList() =>
    _riderList.Text = string.IsNullOrEmpty(_riderListPath) ? "(none chosen)" : Path.GetFileName(_riderListPath);

  private void ChooseRiderList()
  {
    using var open = new OpenFileDialog
    {
      Title = "The day's rider list",
      Filter = "Rider lists (*.xlsx;*.csv)|*.xlsx;*.csv|All files (*.*)|*.*"
    };
    if (!string.IsNullOrEmpty(_riderListPath)) open.FileName = _riderListPath;
    if (open.ShowDialog(this) != DialogResult.OK) return;
    _riderListPath = open.FileName;
    ShowRiderList();
  }

  private void Accept()
  {
    var source = _fromCode ?? (_sources.SelectedItem as Source)?.Plan;
    var plan = PresetCatalog.NewPlan(source, _day.Value.Date, _name.Text, _first.Value.TimeOfDay, _riderListPath);
    if (plan == null)
    {
      MessageBox.Show(this, "With the first session that late, the last one would be after midnight. " +
                            "Choose an earlier time.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
      return;
    }

    Result = plan;
    DialogResult = DialogResult.OK;
    Close();
  }
}
