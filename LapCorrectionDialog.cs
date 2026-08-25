namespace CrossMgrInterface;

/// <summary>
/// The one place an operator fixes a rider's laps.
///
/// It replaces a MessageBox that dumped a lap table as text - unscrollable,
/// unsortable, uncopyable, and offering no way to act on what it showed. Every
/// button here applies immediately rather than staging changes behind OK:
/// the operator needs to watch the standings behind the dialog react, and the
/// undo stack makes a staging layer redundant.
/// </summary>
public sealed class LapCorrectionDialog : Form
{
  private readonly RaceCorrectionService _service;
  private readonly string _tagId;
  private readonly Func<string, RiderInfo?> _lookupRider;
  private readonly Func<string, IReadOnlyList<RejectedRead>> _getRejectedReads;
  private readonly Func<DateTime?> _getRaceStartTime;

  private readonly Label _header = new();
  private readonly DataGridView _laps = new();
  private readonly Label _hint = new();

  private readonly Button _addLap = new();
  private readonly Button _editTime = new();
  private readonly Button _deleteLap = new();
  private readonly Button _splitLap = new();
  private readonly Button _dismiss = new();
  private readonly Button _restore = new();
  private readonly Button _markDnf = new();
  private readonly Button _markDns = new();
  private readonly Button _clearStatus = new();
  private readonly Button _undo = new();

  /// <summary>True if at least one correction was applied while the dialog was open.</summary>
  public bool AnyChangesApplied { get; private set; }

  public LapCorrectionDialog(
    RaceCorrectionService service,
    string tagId,
    Func<string, RiderInfo?> lookupRider,
    Func<string, IReadOnlyList<RejectedRead>> getRejectedReads,
    Func<DateTime?> getRaceStartTime)
  {
    _service = service;
    _tagId = tagId;
    _lookupRider = lookupRider;
    _getRejectedReads = getRejectedReads;
    _getRaceStartTime = getRaceStartTime;

    BuildLayout();
    Reload();
  }

  private void BuildLayout()
  {
    Text = "Fix laps";
    FormBorderStyle = FormBorderStyle.Sizable;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    ClientSize = new Size(880, 560);
    MinimumSize = new Size(760, 420);

    var root = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 2,
      RowCount = 3,
      Padding = new Padding(12)
    };
    root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
    root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

    _header.Dock = DockStyle.Fill;
    _header.AutoSize = true;
    _header.Font = new Font(Font.FontFamily, 12F, FontStyle.Bold);
    _header.Padding = new Padding(0, 0, 0, 8);
    root.Controls.Add(_header, 0, 0);
    root.SetColumnSpan(_header, 2);

    ConfigureGrid();
    root.Controls.Add(_laps, 0, 1);

    root.Controls.Add(BuildActionColumn(), 1, 1);

    _hint.Dock = DockStyle.Fill;
    _hint.AutoSize = true;
    _hint.ForeColor = Color.DimGray;
    _hint.Padding = new Padding(0, 8, 0, 0);
    _hint.Text = "Select a lap, then choose what to do with it. Every change can be undone.";
    root.Controls.Add(_hint, 0, 2);

    var close = new Button
    {
      Text = "Close",
      DialogResult = DialogResult.OK,
      Dock = DockStyle.Fill,
      Height = 34
    };
    root.Controls.Add(close, 1, 2);

    Controls.Add(root);
    AcceptButton = close;
    CancelButton = close;
  }

  private void ConfigureGrid()
  {
    _laps.Dock = DockStyle.Fill;
    _laps.ReadOnly = true;
    _laps.AllowUserToAddRows = false;
    _laps.AllowUserToDeleteRows = false;
    _laps.AllowUserToResizeRows = false;
    _laps.RowHeadersVisible = false;
    _laps.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
    _laps.MultiSelect = false;
    _laps.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
    _laps.RowTemplate.Height = 28;

    _laps.Columns.Add("Lap", "Lap");
    _laps.Columns.Add("Crossing", "Crossing");
    _laps.Columns.Add("RaceTime", "Race time");
    _laps.Columns.Add("LapTime", "Lap time");
    _laps.Columns.Add("Note", "Note");

    // Relative widths: the note column carries the explanation, so give it room.
    SetWidth("Lap", 40);
    SetWidth("Crossing", 75);
    SetWidth("RaceTime", 65);
    SetWidth("LapTime", 70);
    SetWidth("Note", 170);

    void SetWidth(string name, float weight)
    {
      var column = _laps.Columns[name];
      if (column != null) column.FillWeight = weight;
    }

    _laps.SelectionChanged += (_, _) => UpdateButtonStates();
  }

  private Control BuildActionColumn()
  {
    var column = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill,
      FlowDirection = FlowDirection.TopDown,
      WrapContents = false,
      Padding = new Padding(12, 0, 0, 0)
    };

    void Add(Button button, string text, EventHandler onClick, bool spaceAbove = false)
    {
      button.Text = text;
      button.Width = 170;
      button.Height = 34;
      button.Margin = new Padding(0, spaceAbove ? 14 : 4, 0, 0);
      button.Click += onClick;
      column.Controls.Add(button);
    }

    Add(_addLap, "Add a missing lap...", (_, _) => OnAddLap());
    Add(_editTime, "Change lap time...", (_, _) => OnEditLapTime());
    Add(_deleteLap, "Delete this lap", (_, _) => OnDeleteLap());
    Add(_splitLap, "Split this lap...", (_, _) => OnSplitLap());
    Add(_dismiss, "Keep lap as is", (_, _) => OnDismissSuggestion());
    Add(_restore, "Count this read", (_, _) => OnRestoreRejected());

    Add(_markDnf, "Mark as DNF", (_, _) => OnSetStatus(RiderStatus.DNF), spaceAbove: true);
    Add(_markDns, "Mark as DNS", (_, _) => OnSetStatus(RiderStatus.DNS));
    Add(_clearStatus, "Back in the race", (_, _) => OnSetStatus(RiderStatus.Racing));

    Add(_undo, "Undo last change", (_, _) => OnUndo(), spaceAbove: true);

    return column;
  }

  // ---- Rendering -----------------------------------------------------------

  /// <summary>What a grid row refers to: either a recorded lap or a rejected read.</summary>
  private sealed record RowRef(RiderLap? Lap, RejectedRead? Rejected);

  private void Reload()
  {
    var rider = _lookupRider(_tagId);
    if (rider == null)
    {
      _header.Text = "This rider is no longer in the race.";
      _laps.Rows.Clear();
      UpdateButtonStates();
      return;
    }

    var status = rider.StatusText.Length > 0 ? $" - {rider.StatusText}" : "";
    var best = rider.BestLapTime?.ToString(@"mm\:ss\.fff") ?? "no timed lap yet";
    _header.Text = $"{rider.Label}{status}   |   {rider.TotalLaps} lap(s)   |   best {best}";

    var raceStart = _getRaceStartTime();
    var previouslySelected = SelectedRow();

    _laps.Rows.Clear();

    // Laps and rejected reads interleaved in time order, so a rejected read
    // appears where it actually happened rather than in a separate list.
    var entries = rider.Laps
      .Select(l => (Time: l.CrossingTime, Row: new RowRef(l, null)))
      .Concat(_getRejectedReads(_tagId)
        .Where(r => !r.Restored)
        .Select(r => (Time: r.CrossingTime, Row: new RowRef(null, r))))
      .OrderBy(e => e.Time)
      .ToList();

    foreach (var entry in entries)
    {
      if (entry.Row.Lap is { } lap)
      {
        var raceTime = raceStart.HasValue
          ? (lap.CrossingTime - raceStart.Value).ToString(@"mm\:ss\.f")
          : "-";

        var index = _laps.Rows.Add(
          lap.LapNumber.ToString(),
          lap.CrossingTime.ToString("HH:mm:ss.fff"),
          raceTime,
          lap.LapTime?.ToString(@"mm\:ss\.fff") ?? "-",
          DescribeLap(lap));

        var row = _laps.Rows[index];
        row.Tag = entry.Row;

        if (lap.IsSuggestedForSplit && !lap.SuggestionDismissed)
          row.DefaultCellStyle.BackColor = Color.PapayaWhip;
        else if (lap.IsSplitLap)
          row.DefaultCellStyle.BackColor = Color.Lavender;
        else if (lap.WasCorrected)
          row.DefaultCellStyle.BackColor = Color.LightYellow;
      }
      else if (entry.Row.Rejected is { } rejected)
      {
        var raceTime = raceStart.HasValue
          ? (rejected.CrossingTime - raceStart.Value).ToString(@"mm\:ss\.f")
          : "-";

        var index = _laps.Rows.Add(
          "-",
          rejected.CrossingTime.ToString("HH:mm:ss.fff"),
          raceTime,
          rejected.GapToPrevious.ToString(@"mm\:ss\.fff"),
          $"Not counted: {rejected.Reason}");

        var row = _laps.Rows[index];
        row.Tag = entry.Row;
        row.DefaultCellStyle.BackColor = Color.WhiteSmoke;
        row.DefaultCellStyle.ForeColor = Color.DimGray;
      }
    }

    RestoreSelection(previouslySelected);
    UpdateButtonStates();
  }

  private static string DescribeLap(RiderLap lap)
  {
    if (lap.IsSuggestedForSplit && !lap.SuggestionDismissed)
    {
      var each = lap.SuggestedSplitLapTime?.TotalSeconds ?? 0;
      return $"Looks like {lap.SuggestedSplitCount} laps of about {each:F0}s - a read was probably missed";
    }

    return lap.Source switch
    {
      LapSource.Split => "Created by splitting a long lap",
      LapSource.ManualInsert => "Added by hand",
      LapSource.RestoredShortRead => "A rejected read that was put back",
      LapSource.Merged => "Merged from another transponder",
      _ => lap.OriginalCrossingTime.HasValue
        ? $"Time changed from {lap.OriginalCrossingTime:HH:mm:ss.fff}"
        : ""
    };
  }

  private RowRef? SelectedRow() =>
    _laps.SelectedRows.Count > 0 ? _laps.SelectedRows[0].Tag as RowRef : null;

  private void RestoreSelection(RowRef? previous)
  {
    if (previous?.Lap == null)
    {
      if (_laps.Rows.Count > 0) _laps.Rows[0].Selected = true;
      return;
    }

    foreach (DataGridViewRow row in _laps.Rows)
    {
      if (row.Tag is RowRef r && r.Lap?.LapNumber == previous.Lap.LapNumber)
      {
        row.Selected = true;
        return;
      }
    }

    if (_laps.Rows.Count > 0) _laps.Rows[0].Selected = true;
  }

  private void UpdateButtonStates()
  {
    var rider = _lookupRider(_tagId);
    var selected = SelectedRow();
    var lap = selected?.Lap;
    var rejected = selected?.Rejected;
    var pendingSuggestion = lap is { IsSuggestedForSplit: true, SuggestionDismissed: false };

    _addLap.Enabled = rider != null;
    _editTime.Enabled = lap != null;
    _deleteLap.Enabled = lap != null;
    _splitLap.Enabled = lap is { LapTime: not null };
    _dismiss.Enabled = pendingSuggestion;
    _restore.Enabled = rejected != null;

    _markDnf.Enabled = rider is { IsDNF: false };
    _markDns.Enabled = rider is { IsDNS: false };
    _clearStatus.Enabled = rider is not null && (rider.IsDNF || rider.IsDNS);

    _undo.Enabled = _service.History.CanUndo;
    _undo.Text = _service.History.CanUndo ? "Undo last change" : "Nothing to undo";
  }

  // ---- Actions -------------------------------------------------------------

  private int CurrentRevision() => _lookupRider(_tagId)?.Revision ?? -1;

  private void Apply(CorrectionResult result)
  {
    if (!result.Ok)
    {
      MessageBox.Show(this, result.Error, "Could not make that change",
        MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
    else if (result.Command != null)
    {
      AnyChangesApplied = true;
    }

    Reload();
  }

  private void OnAddLap()
  {
    var rider = _lookupRider(_tagId);
    if (rider == null) return;

    // Default to halfway through the selected lap, which is where a missed read
    // most often belongs.
    var suggested = SuggestedInsertTime(rider);

    using var prompt = new CrossingTimePrompt(
      "When did this lap finish?", suggested, _getRaceStartTime());
    if (prompt.ShowDialog(this) != DialogResult.OK) return;

    Apply(_service.AddLap(_tagId, prompt.CrossingTime, CurrentRevision()));
  }

  private DateTime SuggestedInsertTime(RiderInfo rider)
  {
    var lap = SelectedRow()?.Lap;
    if (lap?.LapTime is { } duration)
      return lap.CrossingTime - TimeSpan.FromMilliseconds(duration.TotalMilliseconds / 2);

    return rider.Laps.Count > 0 ? rider.LastCrossing : (_getRaceStartTime() ?? DateTime.Now);
  }

  private void OnEditLapTime()
  {
    var lap = SelectedRow()?.Lap;
    if (lap == null) return;

    using var prompt = new CrossingTimePrompt(
      $"When did lap {lap.LapNumber} actually finish?", lap.CrossingTime, _getRaceStartTime());
    if (prompt.ShowDialog(this) != DialogResult.OK) return;

    Apply(_service.EditLapTime(_tagId, lap.LapNumber, prompt.CrossingTime, CurrentRevision()));
  }

  private void OnDeleteLap()
  {
    var lap = SelectedRow()?.Lap;
    var rider = _lookupRider(_tagId);
    if (lap == null || rider == null) return;

    var answer = MessageBox.Show(this,
      $"Delete lap {lap.LapNumber} of {rider.Label}?\n\n" +
      $"{rider.TotalLaps} lap(s) becomes {rider.TotalLaps - 1}. You can undo this.",
      "Delete lap", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
      MessageBoxDefaultButton.Button2);

    if (answer != DialogResult.Yes) return;

    Apply(_service.DeleteLap(_tagId, lap.LapNumber, CurrentRevision()));
  }

  private void OnSplitLap()
  {
    var lap = SelectedRow()?.Lap;
    if (lap?.LapTime == null) return;

    // Default to the detector's suggestion when there is one, otherwise to
    // however many typical laps fit inside this one.
    var suggested = lap.SuggestedSplitCount > 1 ? lap.SuggestedSplitCount : 2;

    using var prompt = new SplitCountPrompt(lap.LapNumber, lap.LapTime.Value, suggested);
    if (prompt.ShowDialog(this) != DialogResult.OK) return;

    Apply(_service.SplitLap(_tagId, lap.LapNumber, prompt.SplitCount, CurrentRevision()));
  }

  private void OnDismissSuggestion()
  {
    var lap = SelectedRow()?.Lap;
    if (lap == null) return;

    Apply(_service.DismissSplitSuggestion(_tagId, lap.LapNumber));
  }

  private void OnRestoreRejected()
  {
    var rejected = SelectedRow()?.Rejected;
    if (rejected == null) return;

    var result = _service.RestoreRejectedRead(_tagId, rejected.CrossingTime);
    if (result.Ok) rejected.Restored = true;
    Apply(result);
  }

  private void OnSetStatus(RiderStatus status)
  {
    Apply(_service.SetRiderStatus(_tagId, status));
  }

  private void OnUndo()
  {
    Apply(_service.Undo());
  }

  // ---- Prompts -------------------------------------------------------------

  /// <summary>
  /// Asks for a crossing time. Offers race time as well as clock time, because
  /// an operator watching a race thinks in "twelve minutes in", not "14:32:07".
  /// </summary>
  private sealed class CrossingTimePrompt : Form
  {
    private readonly DateTimePicker _clock = new();
    private readonly NumericUpDown _milliseconds = new();
    private readonly NumericUpDown _raceMinutes = new();
    private readonly NumericUpDown _raceSeconds = new();
    private readonly RadioButton _useClock = new();
    private readonly RadioButton _useRaceTime = new();
    private readonly DateTime? _raceStart;

    public DateTime CrossingTime { get; private set; }

    public CrossingTimePrompt(string question, DateTime initial, DateTime? raceStart)
    {
      _raceStart = raceStart;
      CrossingTime = initial;

      Text = "Crossing time";
      FormBorderStyle = FormBorderStyle.FixedDialog;
      StartPosition = FormStartPosition.CenterParent;
      MinimizeBox = false;
      MaximizeBox = false;
      ClientSize = new Size(430, raceStart.HasValue ? 220 : 150);

      var prompt = new Label
      {
        Text = question,
        Location = new Point(16, 16),
        Size = new Size(400, 24),
        Font = new Font(Font, FontStyle.Bold)
      };

      _useRaceTime.Text = "Time into the race";
      _useRaceTime.Location = new Point(16, 48);
      _useRaceTime.AutoSize = true;
      _useRaceTime.Checked = raceStart.HasValue;
      _useRaceTime.Visible = raceStart.HasValue;

      _raceMinutes.Location = new Point(40, 76);
      _raceMinutes.Width = 70;
      _raceMinutes.Maximum = 600;
      _raceSeconds.Location = new Point(150, 76);
      _raceSeconds.Width = 70;
      _raceSeconds.Maximum = 59;
      _raceMinutes.Visible = _raceSeconds.Visible = raceStart.HasValue;

      var minutesLabel = new Label { Text = "min", Location = new Point(116, 80), AutoSize = true, Visible = raceStart.HasValue };
      var secondsLabel = new Label { Text = "sec", Location = new Point(226, 80), AutoSize = true, Visible = raceStart.HasValue };

      if (raceStart.HasValue)
      {
        var into = initial - raceStart.Value;
        if (into > TimeSpan.Zero)
        {
          _raceMinutes.Value = Math.Min((int)into.TotalMinutes, _raceMinutes.Maximum);
          _raceSeconds.Value = into.Seconds;
        }
      }

      _useClock.Text = "Clock time";
      _useClock.Location = new Point(16, raceStart.HasValue ? 112 : 48);
      _useClock.AutoSize = true;
      _useClock.Checked = !raceStart.HasValue;

      _clock.Format = DateTimePickerFormat.Custom;
      _clock.CustomFormat = "HH:mm:ss";
      _clock.ShowUpDown = true;
      _clock.Location = new Point(40, raceStart.HasValue ? 140 : 76);
      _clock.Width = 110;
      _clock.Value = initial;

      _milliseconds.Location = new Point(160, raceStart.HasValue ? 140 : 76);
      _milliseconds.Width = 70;
      _milliseconds.Maximum = 999;
      _milliseconds.Value = initial.Millisecond;

      var msLabel = new Label { Text = "ms", Location = new Point(236, raceStart.HasValue ? 144 : 80), AutoSize = true };

      var ok = new Button
      {
        Text = "OK",
        DialogResult = DialogResult.OK,
        Location = new Point(ClientSize.Width - 200, ClientSize.Height - 44),
        Size = new Size(88, 30)
      };
      var cancel = new Button
      {
        Text = "Cancel",
        DialogResult = DialogResult.Cancel,
        Location = new Point(ClientSize.Width - 104, ClientSize.Height - 44),
        Size = new Size(88, 30)
      };

      ok.Click += (_, _) => CrossingTime = Resolve(initial);

      Controls.AddRange(new Control[]
      {
        prompt, _useRaceTime, _raceMinutes, minutesLabel, _raceSeconds, secondsLabel,
        _useClock, _clock, _milliseconds, msLabel, ok, cancel
      });

      AcceptButton = ok;
      CancelButton = cancel;
    }

    private DateTime Resolve(DateTime fallback)
    {
      if (_useRaceTime.Checked && _raceStart.HasValue)
      {
        return _raceStart.Value
          + TimeSpan.FromMinutes((double)_raceMinutes.Value)
          + TimeSpan.FromSeconds((double)_raceSeconds.Value);
      }

      if (_useClock.Checked)
      {
        return _clock.Value.Date
          + _clock.Value.TimeOfDay.Subtract(TimeSpan.FromMilliseconds(_clock.Value.Millisecond))
          + TimeSpan.FromMilliseconds((double)_milliseconds.Value);
      }

      return fallback;
    }
  }

  /// <summary>Asks how many laps a long lap should become, previewing the result.</summary>
  private sealed class SplitCountPrompt : Form
  {
    private readonly NumericUpDown _count = new();
    private readonly Label _preview = new();
    private readonly TimeSpan _lapTime;

    public int SplitCount => (int)_count.Value;

    public SplitCountPrompt(int lapNumber, TimeSpan lapTime, int suggested)
    {
      _lapTime = lapTime;

      Text = "Split a lap";
      FormBorderStyle = FormBorderStyle.FixedDialog;
      StartPosition = FormStartPosition.CenterParent;
      MinimizeBox = false;
      MaximizeBox = false;
      ClientSize = new Size(400, 180);

      var prompt = new Label
      {
        Text = $"Lap {lapNumber} took {lapTime.TotalSeconds:F1}s.\nHow many laps should it become?",
        Location = new Point(16, 16),
        Size = new Size(370, 44)
      };

      _count.Location = new Point(16, 72);
      _count.Width = 80;
      _count.Minimum = 2;
      _count.Maximum = 6;
      _count.Value = Math.Clamp(suggested, 2, 6);
      _count.ValueChanged += (_, _) => UpdatePreview();

      _preview.Location = new Point(112, 76);
      _preview.Size = new Size(270, 24);
      _preview.ForeColor = Color.DimGray;

      var ok = new Button
      {
        Text = "Split",
        DialogResult = DialogResult.OK,
        Location = new Point(196, 130),
        Size = new Size(88, 30)
      };
      var cancel = new Button
      {
        Text = "Cancel",
        DialogResult = DialogResult.Cancel,
        Location = new Point(292, 130),
        Size = new Size(88, 30)
      };

      Controls.AddRange(new Control[] { prompt, _count, _preview, ok, cancel });
      AcceptButton = ok;
      CancelButton = cancel;

      UpdatePreview();
    }

    private void UpdatePreview()
      => _preview.Text = $"= {_lapTime.TotalSeconds / (double)_count.Value:F1}s each";
  }
}
