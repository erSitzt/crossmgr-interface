namespace CrossMgrInterface;

/// <summary>
/// One session of a day plan: the wizard's questions on one page, answered in
/// advance. On the day itself - when the plan is in use - the time box sets a
/// new time beside the planned one rather than replacing it, so the sessions
/// after it know how far it moved (see <see cref="PlannedSession.TypedTime"/>).
///
/// Hand-built like the other dialogs, so the designer never rewrites it.
/// </summary>
public sealed class PlannedSessionDialog : Form
{
  private readonly PlannedSession _session;
  private readonly bool _onTheDay;

  private readonly DateTimePicker _time = new();
  private readonly Label _plannedNote = new();
  private readonly Button _backToPlanned = new();
  private readonly TextBox _name = new();
  private readonly ComboBox _type = new();
  private readonly CheckedListBox _classes = new();
  private readonly NumericUpDown _length = new();
  private readonly NumericUpDown _extraLaps = new();
  private readonly ComboBox _start = new();
  private readonly NumericUpDown _waveGap = new();
  private readonly Label _waveGapLabel = new();
  private readonly CheckBox _teamEvent = new();
  private readonly ComboBox _overall = new();
  private bool _typedTimeCleared;

  private const string NoOverall = "(scored on its own)";

  /// <param name="onTheDay">The plan is in use today: a changed time is typed beside the planned one.</param>
  /// <param name="listClasses">The classes on the day's rider list, to tick from.</param>
  /// <param name="groups">The overalls already named in the plan, to choose from.</param>
  public PlannedSessionDialog(PlannedSession session, bool onTheDay, IReadOnlyList<string> listClasses,
    IReadOnlyList<string> groups, string title)
  {
    _session = session;
    _onTheDay = onTheDay;

    Text = title;
    FormBorderStyle = FormBorderStyle.FixedDialog;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = false;
    ShowInTaskbar = false;
    AutoScaleMode = AutoScaleMode.Font;
    ClientSize = new Size(560, 600);
    Font = new Font("Segoe UI", 9.5F);

    var grid = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 2,
      Padding = new Padding(14),
      AutoScroll = true
    };
    grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
    grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

    // Time, and on the day what it was planned as.
    _time.Format = DateTimePickerFormat.Custom;
    _time.CustomFormat = "HH:mm";
    _time.ShowUpDown = true;
    _time.Width = 90;
    _time.Value = DateTime.Today + (onTheDay ? session.TypedTime ?? session.Time : session.Time);
    var timeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
    timeRow.Controls.Add(_time);
    if (onTheDay)
    {
      _plannedNote.AutoSize = true;
      _plannedNote.ForeColor = Color.DimGray;
      _plannedNote.Margin = new Padding(10, 6, 0, 0);
      _plannedNote.Text = $"planned {session.Time:hh\\:mm}";
      _backToPlanned.Text = "Back to the planned time";
      _backToPlanned.AutoSize = true;
      _backToPlanned.Visible = session.TypedTime.HasValue;
      _backToPlanned.Click += (_, _) =>
      {
        _time.Value = DateTime.Today + session.Time;
        _typedTimeCleared = true;
        _backToPlanned.Visible = false;
      };
      timeRow.Controls.Add(_plannedNote);
      timeRow.Controls.Add(_backToPlanned);
    }
    Row(onTheDay ? "Time today" : "Time", timeRow);
    if (onTheDay)
      Hint("A time typed today is kept as it is when the day runs late, and the sessions after it move with it.");

    _name.Text = session.Name;
    _name.Width = 340;
    Row("Name", _name);
    Hint("Printed on the results sheet, and shown on the Race Day screen and the websites.");

    _type.DropDownStyle = ComboBoxStyle.DropDownList;
    _type.Items.AddRange(new object[] { "Race", "Timed qualifying", "Free practice" });
    _type.SelectedIndex = session.SessionType switch
    {
      SessionType.TimedQualifying => 1,
      SessionType.FreePractice => 2,
      _ => 0
    };
    _type.Width = 200;
    _type.SelectedIndexChanged += (_, _) => ApplyType();
    Row("Type", _type);

    _classes.CheckOnClick = true;
    _classes.Height = 130;
    _classes.Width = 340;
    var known = listClasses.ToList();
    foreach (var c in session.Classes.Where(c => !known.Contains(c, StringComparer.OrdinalIgnoreCase))) known.Add(c);
    foreach (var c in known)
    {
      var onList = listClasses.Contains(c, StringComparer.OrdinalIgnoreCase);
      _classes.Items.Add(onList ? c : $"{c} (not on the rider list)",
        session.Classes.Contains(c, StringComparer.OrdinalIgnoreCase));
    }
    Row("Classes", _classes);
    Hint(listClasses.Count == 0
      ? "Choose the plan's rider list first, then tick the classes that ride in this session."
      : "Tick the classes that ride in this session. None ticked: every rider on the list counts.");

    _length.Minimum = 1;
    _length.Maximum = 600;
    _length.Value = Math.Clamp(session.DurationMinutes, 1, 600);
    _length.Width = 80;
    Row("Length (minutes)", _length);

    _extraLaps.Minimum = 0;
    _extraLaps.Maximum = 10;
    _extraLaps.Value = Math.Clamp(session.AdditionalLaps, 0, 10);
    _extraLaps.Width = 80;
    Row("Extra laps", _extraLaps);
    Hint("Races only: laps the leader rides after the clock, on top of the lap in progress.");

    _start.DropDownStyle = ComboBoxStyle.DropDownList;
    _start.Items.AddRange(new object[]
    {
      "When the first rider crosses the line",
      "I press START RACE when the gate drops",
      "The classes start in waves"
    });
    _start.SelectedIndex = (int)session.Start;
    _start.Width = 300;
    _start.SelectedIndexChanged += (_, _) => ApplyType();
    Row("Clock starts", _start);

    _waveGap.Minimum = 10;
    _waveGap.Maximum = 1800;
    _waveGap.Increment = 10;
    _waveGap.Value = Math.Clamp(session.WaveGapSeconds, 10, 1800);
    _waveGap.Width = 80;
    _waveGapLabel.Text = "Gap between classes (s)";
    _waveGapLabel.AutoSize = true;
    _waveGapLabel.Margin = new Padding(3, 6, 3, 3);
    grid.Controls.Add(_waveGapLabel);
    grid.Controls.Add(_waveGap);

    _teamEvent.Text = "Team event - riders sharing a team name score as one";
    _teamEvent.AutoSize = true;
    _teamEvent.Checked = session.TeamEvent;
    _teamEvent.CheckedChanged += (_, _) => ApplyType();
    Row("", _teamEvent);

    _overall.DropDownStyle = ComboBoxStyle.DropDown;
    _overall.Items.Add(NoOverall);
    foreach (var g in groups) _overall.Items.Add(g);
    _overall.Text = session.OverallGroup ?? NoOverall;
    _overall.Width = 240;
    Row("Counts towards overall", _overall);
    Hint("Motos with the same overall name are added up into one result - Moto 1 and Moto 2 of MX1/MX2. " +
         "Type a new name, or choose one already in the plan.");

    var buttons = new FlowLayoutPanel
    {
      Dock = DockStyle.Bottom,
      FlowDirection = FlowDirection.RightToLeft,
      Height = 46,
      Padding = new Padding(10, 6, 10, 6)
    };
    var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(90, 30) };
    var ok = new Button { Text = "OK", Size = new Size(90, 30) };
    ok.Click += (_, _) => Accept();
    buttons.Controls.Add(cancel);
    buttons.Controls.Add(ok);
    AcceptButton = ok;
    CancelButton = cancel;

    Controls.Add(grid);
    Controls.Add(buttons);
    ApplyType();
    return;

    void Row(string caption, Control control)
    {
      grid.Controls.Add(new Label
      {
        Text = caption,
        AutoSize = true,
        Margin = new Padding(3, 6, 3, 3),
        Font = new Font(Font, FontStyle.Bold)
      });
      control.Margin = new Padding(3, 3, 3, 3);
      grid.Controls.Add(control);
    }

    void Hint(string text)
    {
      grid.Controls.Add(new Label());
      grid.Controls.Add(new Label
      {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(360, 0),
        ForeColor = Color.DimGray,
        Margin = new Padding(3, 0, 3, 8)
      });
    }
  }

  private bool IsTimed => _type.SelectedIndex != 0;

  /// <summary>Keeps the questions honest about the type: no extra laps, teams or overalls for a timed session.</summary>
  private void ApplyType()
  {
    _extraLaps.Enabled = !IsTimed;
    _teamEvent.Enabled = !IsTimed;
    if (IsTimed) _teamEvent.Checked = false;

    // A team event starts together and scores teams, not classes.
    _classes.Enabled = !_teamEvent.Checked;
    var waves = _start.SelectedIndex == (int)PlannedStartMode.Waves && !IsTimed && !_teamEvent.Checked;
    _waveGap.Visible = waves;
    _waveGapLabel.Visible = waves;
    _overall.Enabled = !IsTimed;
  }

  private void Accept()
  {
    var name = _name.Text.Trim();
    if (name.Length == 0)
    {
      MessageBox.Show(this, "Give the session a name - it goes on the results sheet.", Text,
        MessageBoxButtons.OK, MessageBoxIcon.Information);
      return;
    }

    var classes = _classes.CheckedItems.Cast<string>()
      .Select(c => c.Replace(" (not on the rider list)", ""))
      .ToList();

    if (_start.SelectedIndex == (int)PlannedStartMode.Waves && !IsTimed && !_teamEvent.Checked && classes.Count < 2)
    {
      MessageBox.Show(this, "Starting in waves needs at least two classes ticked: they leave in the order listed.",
        Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
      return;
    }

    var time = _time.Value.TimeOfDay;
    if (_onTheDay)
    {
      if (_typedTimeCleared && time == _session.Time) _session.TypedTime = null;
      else if (time != (_session.TypedTime ?? _session.Time)) _session.TypedTime = time;
    }
    else
    {
      _session.Time = time;
    }

    _session.Name = name;
    _session.SessionType = _type.SelectedIndex switch
    {
      1 => SessionType.TimedQualifying,
      2 => SessionType.FreePractice,
      _ => SessionType.Race
    };
    _session.TeamEvent = !IsTimed && _teamEvent.Checked;
    _session.Classes = _session.TeamEvent ? new List<string>() : classes;
    _session.DurationMinutes = (int)_length.Value;
    _session.AdditionalLaps = (int)_extraLaps.Value;
    _session.Start = (PlannedStartMode)_start.SelectedIndex;
    _session.WaveGapSeconds = (int)_waveGap.Value;

    var group = _overall.Text.Trim();
    _session.OverallGroup = IsTimed || group.Length == 0 || group == NoOverall ? null : group;

    DialogResult = DialogResult.OK;
    Close();
  }
}
