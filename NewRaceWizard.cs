namespace CrossMgrInterface;

/// <summary>What the operator settled on. Applied only when the wizard finishes.</summary>
public sealed class NewRaceSetup
{
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
  private readonly TextBox _name = new();

  // Step 2
  private readonly Label _importSummary = new();
  private readonly DataGridView _preview = new();
  private readonly Label _skipped = new();
  private string? _importedFile;
  private int _importedCount;

  // Step 3
  private readonly NumericUpDown _duration = new();
  private readonly NumericUpDown _extraLaps = new();

  // Step 4
  private readonly RadioButton _startOnFirstTag = new();
  private readonly RadioButton _startManually = new();

  // Step 5
  private readonly Label _summary = new();
  private readonly CheckBox _startReader = new();

  public NewRaceSetup Result { get; private set; } = new();

  public NewRaceWizard(Func<string, ImportResult> import, int existingRiderCount, bool readerRunning)
  {
    _import = import;

    Text = "Set up a race";
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

  private Panel BuildNameStep()
  {
    var panel = new Panel();

    var prompt = new Label
    {
      Text = "What is this race called?",
      Location = new Point(0, 8),
      Size = new Size(700, 26),
      Font = new Font(Font, FontStyle.Bold)
    };

    _name.Location = new Point(0, 42);
    _name.Width = 420;
    _name.Text = $"Moto 1 - {DateTime.Now:dd.MM.yyyy}";

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
      Text = "How long is the race?",
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

    panel.Controls.AddRange(new Control[]
    {
      prompt, _duration, minutesLabel, extraPrompt, _extraLaps, lapsLabel, hint
    });
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

    _startReader.Text = "Connect to the transponder reader now";
    _startReader.Location = new Point(0, 240);
    _startReader.AutoSize = true;
    _startReader.Checked = !readerRunning;
    _startReader.Enabled = !readerRunning;

    panel.Controls.AddRange(new Control[] { prompt, _summary, _startReader });
    return panel;
  }

  // ---- Navigation ----------------------------------------------------------

  private void Show(int index)
  {
    _current = Math.Clamp(index, 0, _steps.Length - 1);

    for (var i = 0; i < _steps.Length; i++)
      _steps[i].Visible = i == _current;

    var titles = new[] { "Name", "Riders", "Length", "Start", "Ready" };
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
      RaceName = _name.Text.Trim(),
      DurationMinutes = (int)_duration.Value,
      AdditionalLaps = (int)_extraLaps.Value,
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

    _summary.Text =
      $"Race:      {_name.Text.Trim()}\n\n" +
      $"Riders:    {riders}\n\n" +
      $"Length:    {_duration.Value} minutes, then {_extraLaps.Value} more lap(s)\n\n" +
      $"Start:     {start}";
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
