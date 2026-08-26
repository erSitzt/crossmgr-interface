namespace CrossMgrInterface;

/// <summary>What the operator settled on. Applied only when the wizard finishes.</summary>
public sealed class NewRaceSetup
{
  /// <summary>
  /// Practice, qualifying or a race. Defaults to Race so a setup object built
  /// before the operator reaches the first step still describes a race.
  /// </summary>
  public SessionType SessionType { get; init; } = SessionType.Race;

  public string RaceName { get; init; } = "";
  public int DurationMinutes { get; init; } = 20;
  public int AdditionalLaps { get; init; } = 1;
  public bool ManualStart { get; init; }
  public bool StartReader { get; init; } = true;

  /// <summary>The file that was imported during the wizard, if any.</summary>
  public string? ImportedFile { get; init; }

  /// <summary>Press Start Race as soon as the wizard closes.</summary>
  public bool StartRaceImmediately { get; init; }
}

/// <summary>
/// Walks a volunteer through setting up a race.
///
/// Setting up used to be five or six unordered acts spread across two screens
/// with four separate "Set" buttons and no signal that you were done. The
/// problem was never discoverability - it was ordering. This forces the happy
/// path once and then gets out of the way; the checklist on the Race Day view
/// mirrors the same state afterwards.
///
/// Nothing is applied until Finish. Cancel changes nothing.
/// </summary>
public sealed class NewRaceWizard : Form
{
  private readonly Func<string, ImportResult> _import;
  private readonly Panel _host = new();
  private readonly Label _stepLabel = new();
  private readonly Button _back = new();
  private readonly Button _next = new();

  private readonly Panel[] _steps;
  private int _current;

  // Step 1
  private readonly RadioButton _formatRace = new();
  private readonly RadioButton _formatQualifying = new();
  private readonly RadioButton _formatPractice = new();

  // Step 2
  private readonly TextBox _name = new();

  // Step 3
  private readonly Label _importSummary = new();
  private readonly DataGridView _preview = new();
  private readonly Label _skipped = new();
  private string? _importedFile;
  private int _importedCount;

  // Step 4
  private readonly NumericUpDown _duration = new();
  private readonly NumericUpDown _extraLaps = new();

  /// <summary>The extra-laps prompt, spinner, unit label and hint, hidden as a
  /// group for a timed session where the clock is a flag rather than a target.</summary>
  private readonly List<Control> _extraLapControls = new();

  /// <summary>Shown in their place, explaining what the flag does instead.</summary>
  private Label? _flagHint;

  // Step 5
  private readonly RadioButton _startOnFirstTag = new();
  private readonly RadioButton _startManually = new();

  // Step 6
  private readonly Label _summary = new();
  private readonly CheckBox _startReader = new();

  public NewRaceSetup Result { get; private set; } = new();

  public NewRaceWizard(Func<string, ImportResult> import, int existingRiderCount, bool readerRunning,
    SessionType sessionType = SessionType.Race)
  {
    _import = import;

    Text = "Set up a session";
    FormBorderStyle = FormBorderStyle.FixedDialog;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = false;
    ClientSize = new Size(760, 620);

    _stepLabel.Location = new Point(20, 16);
    _stepLabel.Size = new Size(720, 26);
    _stepLabel.Font = new Font(Font.FontFamily, 12F, FontStyle.Bold);

    _host.Location = new Point(20, 52);
    _host.Size = new Size(720, 500);

    _steps = new[]
    {
      // First, not last: the format decides what the later steps ask and what
      // the name should be prefilled with.
      BuildFormatStep(sessionType),
      BuildNameStep(),
      BuildRidersStep(existingRiderCount),
      BuildLengthStep(),
      BuildStartModeStep(),
      BuildReadyStep(readerRunning)
    };

    foreach (var step in _steps)
    {
      step.Dock = DockStyle.Fill;
      step.Visible = false;
      _host.Controls.Add(step);
    }

    _back.Text = "< Back";
    _back.Size = new Size(100, 34);
    _back.Location = new Point(420, ClientSize.Height - 52);
    _back.Click += (_, _) => Show(_current - 1);

    _next.Text = "Next >";
    _next.Size = new Size(110, 34);
    _next.Location = new Point(530, ClientSize.Height - 52);
    _next.Click += (_, _) => Advance();

    var cancel = new Button
    {
      Text = "Cancel",
      DialogResult = DialogResult.Cancel,
      Size = new Size(100, 34),
      Location = new Point(648, ClientSize.Height - 52)
    };

    Controls.AddRange(new Control[] { _stepLabel, _host, _back, _next, cancel });
    CancelButton = cancel;

    Show(0);
  }

  // ---- Steps ---------------------------------------------------------------

  /// <summary>
  /// What kind of session this is. First, because it changes what the later
  /// steps ask: a timed session has no extra-laps setting to offer, and its
  /// default name is not "Moto 1".
  /// </summary>
  private Panel BuildFormatStep(SessionType sessionType)
  {
    var panel = new Panel();

    var prompt = new Label
    {
      Text = "What is this session?",
      Location = new Point(0, 8),
      Size = new Size(700, 26),
      Font = new Font(Font, FontStyle.Bold)
    };

    Configure(_formatRace, "Race", 46,
      "Scored on laps completed, then on time. Finishes on a laps target.");
    Configure(_formatQualifying, "Timed qualifying", 116,
      "Scored on best lap. The gate pick order for the race comes out of this.");
    Configure(_formatPractice, "Free practice", 186,
      "Timed the same way, but no timing sheet is produced.");

    (sessionType switch
    {
      SessionType.TimedQualifying => _formatQualifying,
      SessionType.FreePractice => _formatPractice,
      _ => _formatRace
    }).Checked = true;

    panel.Controls.AddRange(new Control[] { prompt, _formatRace, _formatQualifying, _formatPractice });
    return panel;

    void Configure(RadioButton radio, string text, int top, string hint)
    {
      radio.Text = text;
      radio.Location = new Point(0, top);
      radio.Size = new Size(400, 26);
      radio.Font = new Font(Font, FontStyle.Bold);
      radio.CheckedChanged += (_, _) => ApplyFormatToSteps();

      panel.Controls.Add(new Label
      {
        Text = hint,
        Location = new Point(20, top + 26),
        Size = new Size(660, 36),
        ForeColor = Color.DimGray
      });
    }
  }

  /// <summary>True unless the operator chose a race.</summary>
  private bool IsTimedSession => !_formatRace.Checked;

  /// <summary>
  /// Keeps the later steps honest about the chosen format: a timed session ends
  /// on a chequered flag, so there is no extra-laps setting to offer and the
  /// default name should not read "Moto 1".
  /// </summary>
  private void ApplyFormatToSteps()
  {
    foreach (var control in _extraLapControls) control.Visible = !IsTimedSession;
    if (_flagHint != null) _flagHint.Visible = IsTimedSession;

    // Only while the operator has not typed over it, so a name they chose is
    // never silently replaced when they step back and change the format.
    if (_defaultNames.Contains(_name.Text))
      _name.Text = DefaultName();
  }

  private string DefaultName() => _formatQualifying.Checked
    ? $"Qualifying - {DateTime.Now:dd.MM.yyyy}"
    : _formatPractice.Checked
      ? $"Practice - {DateTime.Now:dd.MM.yyyy}"
      : $"Moto 1 - {DateTime.Now:dd.MM.yyyy}";

  /// <summary>The three names this wizard offers, so a typed-over name is left alone.</summary>
  private readonly HashSet<string> _defaultNames = new()
  {
    $"Moto 1 - {DateTime.Now:dd.MM.yyyy}",
    $"Qualifying - {DateTime.Now:dd.MM.yyyy}",
    $"Practice - {DateTime.Now:dd.MM.yyyy}"
  };

  private Panel BuildNameStep()
  {
    var panel = new Panel();

    var prompt = new Label
    {
      Text = "What is this session called?",
      Location = new Point(0, 8),
      Size = new Size(700, 26),
      Font = new Font(Font, FontStyle.Bold)
    };

    _name.Location = new Point(0, 42);
    _name.Width = 420;
    _name.Text = DefaultName();

    var hint = new Label
    {
      Text = "The name appears on the results sheet and in the status bar, so you can " +
             "tell one heat's results from another's.",
      Location = new Point(0, 78),
      Size = new Size(680, 44),
      ForeColor = Color.DimGray
    };

    panel.Controls.AddRange(new Control[] { prompt, _name, hint });
    return panel;
  }

  private Panel BuildRidersStep(int existingRiderCount)
  {
    var panel = new Panel();

    var prompt = new Label
    {
      Text = "Which riders are in this race?",
      Location = new Point(0, 8),
      Size = new Size(700, 26),
      Font = new Font(Font, FontStyle.Bold)
    };

    var choose = new Button { Text = "Choose file...", Location = new Point(0, 42), Size = new Size(140, 32) };
    choose.Click += (_, _) => ChooseRiderFile();

    _importSummary.Location = new Point(154, 48);
    _importSummary.Size = new Size(540, 24);
    _importSummary.Text = existingRiderCount > 0
      ? $"{existingRiderCount} riders already loaded - choose a file to replace them."
      : "An Excel (.xlsx) or CSV file with a column called tagid.";
    _importSummary.ForeColor = Color.DimGray;

    _preview.Location = new Point(0, 86);
    _preview.Size = new Size(700, 300);
    _preview.ReadOnly = true;
    _preview.AllowUserToAddRows = false;
    _preview.RowHeadersVisible = false;
    _preview.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
    _preview.Columns.Add("Number", "#");
    _preview.Columns.Add("Rider", "Rider");
    _preview.Columns.Add("Team", "Team");
    _preview.Columns.Add("Class", "Class");
    _preview.Columns.Add("Transponder", "Transponder");

    _skipped.Location = new Point(0, 394);
    _skipped.Size = new Size(700, 90);
    _skipped.ForeColor = Color.Firebrick;

    panel.Controls.AddRange(new Control[] { prompt, choose, _importSummary, _preview, _skipped });
    return panel;
  }

  private Panel BuildLengthStep()
  {
    var panel = new Panel();

    var prompt = new Label
    {
      Text = "How long is the session?",
      Location = new Point(0, 8),
      Size = new Size(700, 26),
      Font = new Font(Font, FontStyle.Bold)
    };

    _duration.Location = new Point(0, 42);
    _duration.Width = 90;
    _duration.Minimum = 1;
    _duration.Maximum = 600;
    _duration.Value = 20;
    var minutesLabel = new Label { Text = "minutes", Location = new Point(98, 46), AutoSize = true };

    var extraPrompt = new Label
    {
      Text = "Extra laps after the clock runs out",
      Location = new Point(0, 96),
      Size = new Size(700, 24),
      Font = new Font(Font, FontStyle.Bold)
    };

    _extraLaps.Location = new Point(0, 126);
    _extraLaps.Width = 90;
    _extraLaps.Maximum = 10;
    _extraLaps.Value = 1;
    var lapsLabel = new Label { Text = "laps", Location = new Point(98, 130), AutoSize = true };

    var hint = new Label
    {
      Text = "When the clock hits zero the leader still rides this many more laps before " +
             "the flag. Everyone else finishes the lap they are on.",
      Location = new Point(0, 160),
      Size = new Size(680, 44),
      ForeColor = Color.DimGray
    };

    // A timed session ends on the flag, so there is no extra-laps rule to set.
    var flagHint = new Label
    {
      Text = "When the clock hits zero the flag comes out. Everyone finishes the lap " +
             "they are on, and that lap still counts.",
      Location = new Point(0, 96),
      Size = new Size(680, 44),
      ForeColor = Color.DimGray
    };

    _extraLapControls.AddRange(new Control[] { extraPrompt, _extraLaps, lapsLabel, hint });
    _flagHint = flagHint;

    panel.Controls.AddRange(new Control[]
    {
      prompt, _duration, minutesLabel, extraPrompt, _extraLaps, lapsLabel, hint, flagHint
    });

    // The flag hint occupies the space the extra-laps controls vacate.
    ApplyFormatToSteps();
    return panel;
  }

  private Panel BuildStartModeStep()
  {
    var panel = new Panel();

    var prompt = new Label
    {
      Text = "When does the clock start?",
      Location = new Point(0, 8),
      Size = new Size(700, 26),
      Font = new Font(Font, FontStyle.Bold)
    };

    _startOnFirstTag.Text = "When the first rider crosses the line";
    _startOnFirstTag.Location = new Point(0, 48);
    _startOnFirstTag.AutoSize = true;
    _startOnFirstTag.Checked = true;

    var autoHint = new Label
    {
      Text = "Nothing to press. Right for a start line at the timing loop.",
      Location = new Point(24, 74),
      Size = new Size(660, 24),
      ForeColor = Color.DimGray
    };

    _startManually.Text = "I will press Start Race myself";
    _startManually.Location = new Point(0, 112);
    _startManually.AutoSize = true;

    var manualHint = new Label
    {
      Text = "Right when the gate drops somewhere other than the timing loop, so the " +
             "first crossing is already part-way through a lap.",
      Location = new Point(24, 138),
      Size = new Size(660, 44),
      ForeColor = Color.DimGray
    };

    panel.Controls.AddRange(new Control[]
    {
      prompt, _startOnFirstTag, autoHint, _startManually, manualHint
    });
    return panel;
  }

  private Panel BuildReadyStep(bool readerRunning)
  {
    var panel = new Panel();

    var prompt = new Label
    {
      Text = "Ready",
      Location = new Point(0, 8),
      Size = new Size(700, 26),
      Font = new Font(Font, FontStyle.Bold)
    };

    _summary.Location = new Point(0, 44);
    _summary.Size = new Size(700, 180);
    _summary.Font = new Font(Font.FontFamily, 11F);

    _startReader.Location = new Point(0, 240);
    _startReader.AutoSize = true;

    if (readerRunning)
    {
      // Already connected, so there is nothing to do - but a greyed-out,
      // unchecked box reads as "this will not happen", which is the opposite of
      // the truth. Show it as the settled state it is.
      _startReader.Text = "The reader is already connected";
      _startReader.Checked = true;
      _startReader.Enabled = false;
      _startReader.ForeColor = Color.FromArgb(0, 120, 50);
    }
    else
    {
      _startReader.Text = "Connect to the transponder reader now";
      _startReader.Checked = true;
      _startReader.Enabled = true;
    }

    panel.Controls.AddRange(new Control[] { prompt, _summary, _startReader });
    return panel;
  }

  // ---- Navigation ----------------------------------------------------------

  private void Show(int index)
  {
    _current = Math.Clamp(index, 0, _steps.Length - 1);

    for (var i = 0; i < _steps.Length; i++)
      _steps[i].Visible = i == _current;

    var titles = new[] { "Session", "Name", "Riders", "Length", "Start", "Ready" };
    _stepLabel.Text = $"Step {_current + 1} of {_steps.Length}  -  {titles[_current]}";

    _back.Enabled = _current > 0;
    _next.Text = _current == _steps.Length - 1 ? "Finish" : "Next >";

    if (_current == _steps.Length - 1) UpdateSummary();
  }

  private void Advance()
  {
    if (_current < _steps.Length - 1)
    {
      Show(_current + 1);
      return;
    }

    Result = new NewRaceSetup
    {
      SessionType = _formatQualifying.Checked
        ? SessionType.TimedQualifying
        : _formatPractice.Checked
          ? SessionType.FreePractice
          : SessionType.Race,
      RaceName = _name.Text.Trim(),
      DurationMinutes = (int)_duration.Value,
      // Forced to zero for a timed session. The state machine ignores the value
      // there anyway, but a stale one would be persisted to settings and shown
      // back on the Race Settings tab as though it applied.
      AdditionalLaps = IsTimedSession ? 0 : (int)_extraLaps.Value,
      ManualStart = _startManually.Checked,
      StartReader = _startReader.Checked && _startReader.Enabled,
      ImportedFile = _importedFile,
      StartRaceImmediately = false
    };

    DialogResult = DialogResult.OK;
    Close();
  }

  private void UpdateSummary()
  {
    var riders = _importedCount > 0 ? $"{_importedCount} riders imported" : "no riders imported yet";
    var start = _startManually.Checked
      ? "You will press Start Race"
      : "The clock starts on the first rider";

    var reader = _startReader.Enabled
      ? "will be connected when you finish"
      : "already connected";

    var format = _formatQualifying.Checked
      ? "Timed qualifying - ranked by best lap"
      : _formatPractice.Checked
        ? "Free practice - no timing sheet"
        : "Race - ranked by laps, then time";

    var length = IsTimedSession
      ? $"{_duration.Value} minutes, then the flag"
      : $"{_duration.Value} minutes, then {_extraLaps.Value} more lap(s)";

    _summary.Text =
      $"Session:   {format}\n\n" +
      $"Name:      {_name.Text.Trim()}\n\n" +
      $"Riders:    {riders}\n\n" +
      $"Length:    {length}\n\n" +
      $"Start:     {start}\n\n" +
      $"Reader:    {reader}";
  }

  private void ChooseRiderFile()
  {
    using var dialog = new OpenFileDialog
    {
      Title = "Choose the rider list",
      Filter = "Excel files (*.xlsx)|*.xlsx|CSV files (*.csv)|*.csv|All files (*.*)|*.*"
    };

    if (dialog.ShowDialog(this) != DialogResult.OK) return;

    ImportResult result;
    try
    {
      result = _import(dialog.FileName);
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(this, "The rider list could not be read.",
        "Check that the file is not open in another program, then try again.", ex);
      return;
    }

    _importedFile = dialog.FileName;
    _importedCount = result.ImportedCount;

    _preview.Rows.Clear();
    foreach (var rider in result.Riders.Take(200))
    {
      _preview.Rows.Add(
        rider.RiderNumber,
        $"{rider.FirstName} {rider.LastName}".Trim(),
        rider.Team,
        rider.Category,
        Shorten(rider.TagID));
    }

    if (result.ImportedCount == 0)
    {
      var columns = result.DetectedColumns.Count > 0
        ? string.Join(", ", result.DetectedColumns)
        : "none";

      _importSummary.Text = "No riders found in that file.";
      _importSummary.ForeColor = Color.Firebrick;
      _skipped.Text = result.HasTagColumn
        ? $"The file has a transponder column but no usable rows. Columns found: {columns}."
        : $"The file needs a column called 'tagid'. Columns found: {columns}.";
      return;
    }

    _importSummary.Text = $"{result.ImportedCount} riders read from {Path.GetFileName(dialog.FileName)}";
    _importSummary.ForeColor = Color.DarkGreen;

    // Skipped rows used to be written to a console nobody sees, so a
    // half-readable roster reported a clean success.
    _skipped.Text = result.Skipped.Count == 0
      ? ""
      : $"{result.Skipped.Count} row(s) skipped:\n" +
        string.Join("\n", result.Skipped.Take(4).Select(s => $"  row {s.Row} - {s.Reason}")) +
        (result.Skipped.Count > 4 ? $"\n  ...and {result.Skipped.Count - 4} more" : "");
  }

  /// <summary>Transponder codes are long and meaningless; show just enough to compare.</summary>
  private static string Shorten(string tagId) =>
    tagId.Length <= 8 ? tagId : "..." + tagId[^6..];
}
