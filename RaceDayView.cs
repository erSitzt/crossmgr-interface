namespace CrossMgrInterface;

/// <summary>Race state in the terms a volunteer thinks in, not the state machine's.</summary>
public enum RaceDayState
{
  WaitingForFirstRider,
  ReadyToStart,
  Running,
  LastLaps,
  Finishing,
  Finished
}

/// <summary>How loudly a notice should be presented.</summary>
public enum NoticeLevel { Info, Warning, Critical }

/// <summary>
/// The screen a volunteer runs a race from.
///
/// Everything on it is either the answer to "is this working?" or an action
/// worth taking mid-race. No port, no transponder codes, no protocol trace, no
/// prediction columns - those all still exist on the advanced tabs for whoever
/// wants them.
///
/// Built in code rather than in the designer, following the pattern
/// LapProgressionManager already uses, so the form's own layout is untouched.
/// </summary>
public sealed class RaceDayView
{
  private const int LeaderboardRows = 10;

  private TableLayoutPanel _banner = null!;
  private Label _bannerText = null!;
  private Button _bannerAction = null!;

  private Label _clockValue = null!;
  private Label _clockSub = null!;
  private Label _stateValue = null!;
  private Label _stateDetail = null!;
  private Panel _readerDot = null!;
  private Label _readerValue = null!;
  private Label _readerSub = null!;

  private DataGridView _leaderboard = null!;
  private Label _leaderboardFooter = null!;

  private Button _startRace = null!;
  private Button _endRace = null!;
  private Button _fixLaps = null!;
  private Button _results = null!;

  private Label _checkName = null!;
  private Label _checkRiders = null!;
  private Label _checkDuration = null!;
  private Label _checkReader = null!;

  private Color _readerDotColor = Color.Gray;

  public event EventHandler? StartRaceClicked;
  public event EventHandler? EndRaceNowClicked;
  public event EventHandler? ResultsClicked;
  public event EventHandler? FixLapsClicked;
  public event EventHandler? SetupClicked;
  public event EventHandler? BannerDismissed;

  public TabPage CreateRaceDayTab()
  {
    var page = new TabPage("Race Day") { Name = "tabPageRaceDay", BackColor = Color.White };

    var root = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 3,
      Padding = new Padding(16),
      BackColor = Color.White
    };
    root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
    root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

    root.Controls.Add(BuildBanner(), 0, 0);
    root.Controls.Add(BuildTiles(), 0, 1);
    root.Controls.Add(BuildBody(), 0, 2);

    page.Controls.Add(root);
    return page;
  }

  // ---- Construction --------------------------------------------------------

  private Control BuildBanner()
  {
    _banner = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 2,
      RowCount = 1,
      Height = 54,
      AutoSize = true,
      Visible = false,
      BackColor = Color.Gold,
      Padding = new Padding(12, 8, 12, 8),
      Margin = new Padding(0, 0, 0, 12)
    };
    _banner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    _banner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

    _bannerText = new Label
    {
      Dock = DockStyle.Fill,
      AutoSize = false,
      TextAlign = ContentAlignment.MiddleLeft,
      Font = new Font("Segoe UI", 15F, FontStyle.Bold)
    };

    _bannerAction = new Button
    {
      Text = "OK",
      Width = 110,
      Height = 34,
      Anchor = AnchorStyles.Right
    };
    _bannerAction.Click += (s, e) =>
    {
      ClearBanner();
      BannerDismissed?.Invoke(s, e);
    };

    _banner.Controls.Add(_bannerText, 0, 0);
    _banner.Controls.Add(_bannerAction, 1, 0);
    return _banner;
  }

  private Control BuildTiles()
  {
    var tiles = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 3,
      RowCount = 1,
      BackColor = Color.White
    };
    for (var i = 0; i < 3; i++)
      tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));

    // Time remaining, big enough to read from a few steps back.
    var clockTile = Tile("TIME LEFT", out _clockValue, out _clockSub);
    _clockValue.Font = new Font("Segoe UI Semibold", 54F);
    _clockValue.Text = "--:--";

    var stateTile = Tile("RACE", out _stateValue, out _stateDetail);
    _stateValue.Font = new Font("Segoe UI Semibold", 22F);
    _stateValue.Text = "Not started";

    var readerTile = ReaderTile();

    tiles.Controls.Add(clockTile, 0, 0);
    tiles.Controls.Add(stateTile, 1, 0);
    tiles.Controls.Add(readerTile, 2, 0);
    return tiles;
  }

  private static Panel Tile(string caption, out Label value, out Label sub)
  {
    var panel = new Panel
    {
      Dock = DockStyle.Fill,
      Margin = new Padding(4),
      Padding = new Padding(12, 8, 12, 8),
      BorderStyle = BorderStyle.FixedSingle,
      BackColor = Color.White
    };

    var captionLabel = new Label
    {
      Text = caption,
      Dock = DockStyle.Top,
      Height = 22,
      ForeColor = Color.DimGray,
      Font = new Font("Segoe UI", 10F, FontStyle.Bold)
    };

    sub = new Label
    {
      Dock = DockStyle.Bottom,
      Height = 26,
      TextAlign = ContentAlignment.MiddleCenter,
      Font = new Font("Segoe UI", 11F),
      ForeColor = Color.DimGray
    };

    value = new Label
    {
      Dock = DockStyle.Fill,
      AutoSize = false,
      TextAlign = ContentAlignment.MiddleCenter
    };

    // Added fill-last so docking resolves in the intended order.
    panel.Controls.Add(value);
    panel.Controls.Add(sub);
    panel.Controls.Add(captionLabel);
    return panel;
  }

  private Panel ReaderTile()
  {
    var panel = Tile("READER", out _readerValue, out _readerSub);
    _readerValue.Font = new Font("Segoe UI Semibold", 17F);
    _readerValue.Text = "Not connected";
    _readerValue.Padding = new Padding(28, 0, 0, 0);

    _readerDot = new Panel
    {
      Size = new Size(20, 20),
      Location = new Point(16, 62),
      BackColor = Color.Transparent
    };
    _readerDot.Paint += (_, e) =>
    {
      using var brush = new SolidBrush(_readerDotColor);
      e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
      e.Graphics.FillEllipse(brush, 0, 0, 18, 18);
    };

    panel.Controls.Add(_readerDot);
    _readerDot.BringToFront();
    return panel;
  }

  private Control BuildBody()
  {
    var body = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 2,
      RowCount = 1,
      BackColor = Color.White,
      Margin = new Padding(0, 12, 0, 0)
    };
    body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));

    body.Controls.Add(BuildLeaderboard(), 0, 0);
    body.Controls.Add(BuildSideColumn(), 1, 0);
    return body;
  }

  private Control BuildLeaderboard()
  {
    var host = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 2,
      Margin = new Padding(4)
    };
    host.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    host.RowStyles.Add(new RowStyle(SizeType.AutoSize));

    _leaderboard = new DataGridView
    {
      Dock = DockStyle.Fill,
      ReadOnly = true,
      AllowUserToAddRows = false,
      AllowUserToDeleteRows = false,
      AllowUserToResizeRows = false,
      AllowUserToResizeColumns = false,
      RowHeadersVisible = false,
      // Display-only: this is a scoreboard, not a working grid. Corrections
      // happen on the Riders tab and in the Fix laps dialog.
      Enabled = false,
      ScrollBars = ScrollBars.None,
      AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
      BackgroundColor = Color.White,
      BorderStyle = BorderStyle.None,
      CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
      Font = new Font("Segoe UI", 14F),
      RowTemplate = { Height = 42 }
    };
    _leaderboard.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
    _leaderboard.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;

    // Six columns. Deliberately not the sixteen the Riders tab carries.
    _leaderboard.Columns.Add("Pos", "Pos");
    _leaderboard.Columns.Add("Number", "#");
    _leaderboard.Columns.Add("Rider", "Rider");
    _leaderboard.Columns.Add("Laps", "Laps");
    _leaderboard.Columns.Add("LastLap", "Last lap");
    _leaderboard.Columns.Add("Gap", "Gap");

    SetFill("Pos", 40);
    SetFill("Number", 45);
    SetFill("Rider", 200);
    SetFill("Laps", 50);
    SetFill("LastLap", 80);
    SetFill("Gap", 75);

    void SetFill(string name, float weight)
    {
      var column = _leaderboard.Columns[name];
      if (column != null) column.FillWeight = weight;
    }

    _leaderboardFooter = new Label
    {
      Dock = DockStyle.Fill,
      AutoSize = true,
      ForeColor = Color.DimGray,
      Padding = new Padding(4, 6, 0, 0),
      Text = ""
    };

    host.Controls.Add(_leaderboard, 0, 0);
    host.Controls.Add(_leaderboardFooter, 0, 1);
    return host;
  }

  private Control BuildSideColumn()
  {
    var column = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill,
      FlowDirection = FlowDirection.TopDown,
      WrapContents = false,
      Padding = new Padding(12, 4, 0, 0),
      BackColor = Color.White
    };

    _startRace = ActionButton("START RACE", Color.FromArgb(0, 140, 60), Color.White);
    _startRace.Click += (s, e) => StartRaceClicked?.Invoke(s, e);

    _endRace = ActionButton("End race now", Color.FromArgb(214, 137, 16), Color.White);
    _endRace.Click += (s, e) => EndRaceNowClicked?.Invoke(s, e);

    _fixLaps = ActionButton("Fix laps...", SystemColors.Control, SystemColors.ControlText);
    _fixLaps.Click += (s, e) => FixLapsClicked?.Invoke(s, e);

    _results = ActionButton("Results...", SystemColors.Control, SystemColors.ControlText);
    _results.Click += (s, e) => ResultsClicked?.Invoke(s, e);

    column.Controls.AddRange(new Control[] { _startRace, _endRace, _fixLaps, _results });

    var setupCaption = new Label
    {
      Text = "SET UP",
      ForeColor = Color.DimGray,
      Font = new Font("Segoe UI", 9F, FontStyle.Bold),
      AutoSize = true,
      Margin = new Padding(0, 18, 0, 4)
    };
    column.Controls.Add(setupCaption);

    _checkName = ChecklistLabel();
    _checkRiders = ChecklistLabel();
    _checkDuration = ChecklistLabel();
    _checkReader = ChecklistLabel();
    column.Controls.AddRange(new Control[] { _checkName, _checkRiders, _checkDuration, _checkReader });

    var setup = new Button
    {
      Text = "Set up race...",
      Width = 210,
      Height = 34,
      Margin = new Padding(0, 10, 0, 0)
    };
    setup.Click += (s, e) => SetupClicked?.Invoke(s, e);
    column.Controls.Add(setup);

    return column;
  }

  private static Button ActionButton(string text, Color back, Color fore) => new()
  {
    Text = text,
    Width = 210,
    Height = 56,
    Margin = new Padding(0, 0, 0, 8),
    Font = new Font("Segoe UI", 13F, FontStyle.Bold),
    BackColor = back,
    ForeColor = fore,
    FlatStyle = FlatStyle.System,
    UseVisualStyleBackColor = back == SystemColors.Control
  };

  private static Label ChecklistLabel() => new()
  {
    AutoSize = true,
    Font = new Font("Segoe UI", 10F),
    Margin = new Padding(0, 2, 0, 2)
  };

  // ---- Updates -------------------------------------------------------------

  public void SetClock(TimeSpan? remaining, TimeSpan? finalElapsed, TimeSpan duration)
  {
    if (finalElapsed.HasValue)
    {
      _clockValue.Text = FormatClock(finalElapsed.Value);
      _clockValue.ForeColor = Color.Black;
      _clockSub.Text = "final time";
      return;
    }

    if (!remaining.HasValue)
    {
      _clockValue.Text = "--:--";
      _clockValue.ForeColor = Color.Black;
      _clockSub.Text = $"{duration.TotalMinutes:F0} minute race";
      return;
    }

    _clockValue.Text = FormatClock(remaining.Value);
    _clockValue.ForeColor = remaining.Value.TotalMinutes switch
    {
      <= 1 => Color.Red,
      <= 5 => Color.DarkRed,
      _ => Color.Black
    };
    _clockSub.Text = $"of {duration.TotalMinutes:F0}:00";
  }

  private static string FormatClock(TimeSpan value) =>
    value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"mm\:ss");

  public void SetState(RaceDayState state, string detail)
  {
    (_stateValue.Text, _stateValue.ForeColor) = state switch
    {
      RaceDayState.WaitingForFirstRider => ("Waiting for first rider", Color.DimGray),
      RaceDayState.ReadyToStart => ("Ready to start", Color.FromArgb(214, 137, 16)),
      RaceDayState.Running => ("Race running", Color.FromArgb(0, 130, 55)),
      RaceDayState.LastLaps => ("Last laps", Color.FromArgb(20, 90, 180)),
      RaceDayState.Finishing => ("Finishing", Color.FromArgb(20, 90, 180)),
      _ => ("Race finished", Color.FromArgb(20, 60, 140))
    };

    _stateDetail.Text = detail;

    // Only offer the actions that make sense right now.
    _startRace.Visible = state == RaceDayState.ReadyToStart;
    _endRace.Visible = state is RaceDayState.Running or RaceDayState.LastLaps or RaceDayState.Finishing;

    var finished = state == RaceDayState.Finished;
    _results.BackColor = finished ? Color.FromArgb(0, 140, 60) : SystemColors.Control;
    _results.ForeColor = finished ? Color.White : SystemColors.ControlText;
  }

  public void SetReaderHealth(bool serverRunning, int connections, DateTime lastReadTime, bool raceRunning)
  {
    if (!serverRunning)
    {
      Reader(Color.Gray, "Not connected", "Reader > Start reader connection");
      return;
    }

    if (connections == 0)
    {
      Reader(Color.FromArgb(214, 137, 16), "Waiting for reader", "The reader has not connected yet");
      return;
    }

    if (lastReadTime == DateTime.MinValue)
    {
      Reader(Color.FromArgb(0, 160, 70), "Reader connected", "No reads yet");
      return;
    }

    var since = DateTime.Now - lastReadTime;
    if (raceRunning && since.TotalSeconds > 60)
      Reader(Color.Red, $"NO READS FOR {FormatClock(since)}", "Check the reader and the loop");
    else if (raceRunning && since.TotalSeconds > 30)
      Reader(Color.FromArgb(214, 137, 16), $"No reads for {since.TotalSeconds:F0}s", "Quiet - is that expected?");
    else
      Reader(Color.FromArgb(0, 160, 70), "Reader OK", $"last read {since.TotalSeconds:F0}s ago");
  }

  private void Reader(Color dot, string value, string sub)
  {
    _readerDotColor = dot;
    _readerValue.Text = value;
    _readerValue.ForeColor = dot == Color.Gray ? Color.DimGray : Color.Black;
    _readerSub.Text = sub;
    _readerDot.Invalidate();
  }

  /// <summary>
  /// Fills the scoreboard. Takes riders already in finishing order and renders
  /// their labels - this view never sees or shows a transponder code.
  /// </summary>
  public void SetLeaderboard(IReadOnlyList<RiderInfo> sortedRiders)
  {
    var shown = Math.Min(sortedRiders.Count, LeaderboardRows);

    while (_leaderboard.Rows.Count > shown)
      _leaderboard.Rows.RemoveAt(_leaderboard.Rows.Count - 1);
    if (_leaderboard.Rows.Count < shown)
      _leaderboard.Rows.Add(shown - _leaderboard.Rows.Count);

    var leader = sortedRiders.FirstOrDefault();

    for (var i = 0; i < shown; i++)
    {
      var rider = sortedRiders[i];
      var row = _leaderboard.Rows[i];

      row.Cells["Pos"].Value = rider.IsDNF || rider.IsDNS ? "-" : (i + 1).ToString();
      row.Cells["Number"].Value = rider.RiderNumber;
      row.Cells["Rider"].Value = RiderNameOnly(rider);
      row.Cells["Laps"].Value = rider.TotalLaps.ToString();
      row.Cells["LastLap"].Value = rider.LastLapTime?.ToString(@"m\:ss\.f") ?? "-";
      row.Cells["Gap"].Value = DescribeGap(rider, leader, i);

      row.DefaultCellStyle.BackColor = (rider.IsDNF || rider.IsDNS) switch
      {
        true => Color.WhiteSmoke,
        false => i switch
        {
          0 => Color.Gold,
          1 => Color.Gainsboro,
          2 => Color.FromArgb(233, 205, 175),
          _ => Color.White
        }
      };
      row.DefaultCellStyle.ForeColor = rider.IsDNF || rider.IsDNS ? Color.Gray : Color.Black;
    }

    var remaining = sortedRiders.Count - shown;
    _leaderboardFooter.Text = remaining > 0
      ? $"+ {remaining} more - see the Riders tab"
      : "";
  }

  private static string RiderNameOnly(RiderInfo rider)
  {
    var name = $"{rider.FirstName} {rider.LastName}".Trim();
    if (name.Length > 0) return rider.StatusText.Length > 0 ? $"{name} ({rider.StatusText})" : name;

    // No name known. Say so plainly rather than showing a transponder code.
    return rider.RiderNumber.Length > 0 ? "" : "unidentified transponder";
  }

  private static string DescribeGap(RiderInfo rider, RiderInfo? leader, int index)
  {
    if (rider.IsDNS) return "DNS";
    if (rider.IsDNF) return "DNF";
    if (leader == null || index == 0) return "-";

    var lapsDown = leader.TotalLaps - rider.TotalLaps;
    if (lapsDown > 0) return lapsDown == 1 ? "-1 lap" : $"-{lapsDown} laps";

    var gap = rider.TotalTime - leader.TotalTime;
    return gap > TimeSpan.Zero ? $"+{gap.TotalSeconds:F1}" : "-";
  }

  public void SetChecklist(string? raceName, int riderCount, TimeSpan duration, bool readerConnected)
  {
    Check(_checkName, raceName is { Length: > 0 }, raceName is { Length: > 0 } ? $"Named \"{raceName}\"" : "Race not named");
    Check(_checkRiders, riderCount > 0, riderCount > 0 ? $"{riderCount} riders imported" : "No riders imported");
    Check(_checkDuration, duration > TimeSpan.Zero, $"{duration.TotalMinutes:F0} minutes");
    Check(_checkReader, readerConnected, readerConnected ? "Reader connected" : "Reader not connected");
  }

  private static void Check(Label label, bool done, string text)
  {
    label.Text = (done ? "✓  " : "○  ") + text;
    label.ForeColor = done ? Color.FromArgb(0, 120, 50) : Color.DimGray;
  }

  /// <summary>
  /// Shows something the operator must not miss. Never a modal: a modal would
  /// stop the clock and the leaderboard, which is exactly wrong mid-race.
  /// </summary>
  public void ShowBanner(NoticeLevel level, string message)
  {
    _bannerText.Text = level switch
    {
      NoticeLevel.Critical => "⚠  " + message,
      NoticeLevel.Warning => "⚠  " + message,
      _ => message
    };

    (_banner.BackColor, _bannerText.ForeColor) = level switch
    {
      NoticeLevel.Critical => (Color.FromArgb(200, 30, 30), Color.White),
      NoticeLevel.Warning => (Color.Gold, Color.Black),
      _ => (Color.FromArgb(215, 232, 245), Color.Black)
    };

    _banner.Visible = true;
  }

  public void ClearBanner() => _banner.Visible = false;
}
