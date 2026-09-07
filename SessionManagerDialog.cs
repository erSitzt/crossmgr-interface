namespace CrossMgrInterface;

/// <summary>What the Past sessions window needs from the application.</summary>
public interface ISessionManagerHost
{
  IReadOnlyList<SessionSummary> ListSessions();

  /// <summary>The session on screen right now, if any.</summary>
  int? CurrentSessionId { get; }

  /// <summary>True while a session is scoring, which blocks opening another.</summary>
  bool SessionRunning { get; }

  void PrintResults(SessionSummary session);

  /// <summary>Loads the session into the live tabs. True if it is now the current one.</summary>
  bool OpenSession(SessionSummary session);

  void RenameSession(SessionSummary session, string name);

  /// <summary>Asks the operator, then deletes. True if it was deleted.</summary>
  bool DeleteSession(IWin32Window owner, SessionSummary session);
}

/// <summary>
/// Every session in the database, and what can be done with one: print its
/// sheet, bring it back on screen, rename it, delete it.
///
/// A window rather than a tab. The tab strip is the volunteer's live view and
/// this is not live - it is where the results go after the day, when the
/// secretary wants every moto's sheet printed in one sitting.
///
/// Hand-built like the other dialogs, so the designer never rewrites it.
/// </summary>
public sealed class SessionManagerDialog : Form
{
  private readonly ISessionManagerHost _host;

  private readonly DataGridView _grid = new();
  private readonly Label _empty = new();
  private readonly Label _hint = new();
  private readonly Button _results = new();
  private readonly Button _open = new();
  private readonly Button _rename = new();
  private readonly Button _delete = new();

  private List<SessionSummary> _sessions = new();

  /// <summary>The cell the selection lands on, so the name is what gets the focus ring.</summary>
  private const int ColName = 1;

  public SessionManagerDialog(ISessionManagerHost host)
  {
    _host = host;

    Text = "Past sessions";
    FormBorderStyle = FormBorderStyle.Sizable;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = true;
    ShowInTaskbar = false;
    ClientSize = new Size(900, 520);
    MinimumSize = new Size(700, 360);

    var root = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 2,
      RowCount = 2,
      Padding = new Padding(12)
    };
    root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
    root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

    root.Controls.Add(BuildGrid(), 0, 0);
    root.Controls.Add(BuildButtons(), 1, 0);

    _hint.Dock = DockStyle.Fill;
    _hint.AutoSize = true;
    _hint.ForeColor = Color.DimGray;
    _hint.Padding = new Padding(0, 8, 0, 0);
    root.Controls.Add(_hint, 0, 1);
    root.SetColumnSpan(_hint, 2);

    Controls.Add(root);

    Load += (_, _) => Reload(selectId: _host.CurrentSessionId);
  }

  private Control BuildGrid()
  {
    var host = new Panel { Dock = DockStyle.Fill };

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
    _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
    _grid.Font = new Font("Segoe UI", 10.5F);
    _grid.RowTemplate.Height = 30;
    _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
    _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(0, 4, 0, 4);
    _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;

    AddColumn("Date", 130);
    AddColumn("Name", 220);
    AddColumn("Type", 120);
    AddColumn("Length", 70);
    AddColumn("Riders", 60);
    AddColumn("Laps", 60);
    AddColumn("Status", 100);

    _grid.SelectionChanged += (_, _) => UpdateButtons();
    _grid.CellDoubleClick += (_, e) =>
    {
      if (e.RowIndex >= 0) PrintSelected();
    };

    // Shown in the grid's place when there is nothing to list, rather than an
    // empty grid that looks like a loading problem.
    _empty.Dock = DockStyle.Fill;
    _empty.TextAlign = ContentAlignment.MiddleCenter;
    _empty.ForeColor = Color.DimGray;
    _empty.Font = new Font("Segoe UI", 11F);
    _empty.Text = "No sessions have been recorded yet.\n\n" +
                  "A session is stored from the moment its clock starts.";
    _empty.Visible = false;

    host.Controls.Add(_grid);
    host.Controls.Add(_empty);
    return host;

    void AddColumn(string header, int width)
    {
      var index = _grid.Columns.Add(header, header);
      _grid.Columns[index].Width = width;
      _grid.Columns[index].SortMode = DataGridViewColumnSortMode.NotSortable;
    }
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

    Configure(_results, "Results...", () => PrintSelected());
    Configure(_open, "Open", () => OpenSelected());
    Configure(_rename, "Rename...", () => RenameSelected());
    Configure(_delete, "Delete...", () => DeleteSelected());

    var close = new Button
    {
      Text = "Close",
      Width = 150,
      Height = 34,
      Margin = new Padding(0, 24, 0, 0),
      DialogResult = DialogResult.Cancel
    };

    column.Controls.AddRange(new Control[] { _results, _open, _rename, _delete, close });
    CancelButton = close;
    return column;

    static void Configure(Button button, string text, Action onClick)
    {
      button.Text = text;
      button.Width = 150;
      button.Height = 34;
      button.Margin = new Padding(0, 0, 0, 8);
      button.Click += (_, _) => onClick();
    }
  }

  // ---- Data ----------------------------------------------------------------

  private void Reload(int? selectId)
  {
    _sessions = _host.ListSessions().ToList();

    _grid.Rows.Clear();
    foreach (var session in _sessions)
    {
      var race = session.Race;
      var index = _grid.Rows.Add(
        race.StartTime.ToString("dd.MM.yyyy HH:mm"),
        string.IsNullOrWhiteSpace(race.Name) ? "(unnamed)" : race.Name,
        DescribeType(race.SessionType),
        $"{race.Duration.TotalMinutes:F0} min",
        session.Riders.ToString(),
        session.Laps.ToString(),
        DescribeStatus(race));

      var row = _grid.Rows[index];
      if (race.Id == _host.CurrentSessionId)
      {
        row.DefaultCellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
      }
      else if (!race.IsFinished)
      {
        row.DefaultCellStyle.ForeColor = Color.DimGray;
      }
    }

    var any = _sessions.Count > 0;
    _grid.Visible = any;
    _empty.Visible = !any;

    if (any)
    {
      var wanted = selectId.HasValue ? _sessions.FindIndex(s => s.Race.Id == selectId.Value) : -1;
      var index = wanted >= 0 ? wanted : 0;
      _grid.ClearSelection();
      _grid.Rows[index].Selected = true;
      _grid.CurrentCell = _grid.Rows[index].Cells[ColName];
    }

    _hint.Text = any
      ? "Double-click a session to print its results. The session in bold is the one on screen now."
      : "";

    UpdateButtons();
  }

  private SessionSummary? Selected =>
    _grid.SelectedRows.Count > 0 && _grid.SelectedRows[0].Index < _sessions.Count
      ? _sessions[_grid.SelectedRows[0].Index]
      : null;

  private void UpdateButtons()
  {
    var selected = Selected;
    var isCurrent = selected != null && selected.Race.Id == _host.CurrentSessionId;

    _results.Enabled = selected != null;

    // Opening is pointless for the session already on screen, and not allowed
    // while another one is scoring.
    _open.Enabled = selected != null && !isCurrent && !_host.SessionRunning;

    _rename.Enabled = selected != null;

    // The running session is the one thing here that must not be deleted from
    // underneath the clock.
    _delete.Enabled = selected != null && !(isCurrent && _host.SessionRunning);
  }

  private string DescribeStatus(DbRace race)
  {
    if (race.Id == _host.CurrentSessionId)
      return _host.SessionRunning ? "Running" : "On screen";
    return race.IsFinished ? "Finished" : "Not finished";
  }

  private static string DescribeType(SessionType type) => type switch
  {
    SessionType.TimedQualifying => "Timed qualifying",
    SessionType.FreePractice => "Free practice",
    _ => "Race"
  };

  // ---- Actions -------------------------------------------------------------

  private void PrintSelected()
  {
    var selected = Selected;
    if (selected == null) return;
    _host.PrintResults(selected);
  }

  private void OpenSelected()
  {
    var selected = Selected;
    if (selected == null) return;

    if (_host.OpenSession(selected))
    {
      // The point of opening is to look at it, which happens behind this window.
      DialogResult = DialogResult.OK;
      Close();
      return;
    }

    Reload(selectId: selected.Race.Id);
  }

  private void RenameSelected()
  {
    var selected = Selected;
    if (selected == null) return;

    var name = TextPrompt.Ask(this, "Rename session", selected.Race.Name,
      "The name appears on the results sheet.");
    if (name == null || name == selected.Race.Name) return;

    _host.RenameSession(selected, name);
    Reload(selectId: selected.Race.Id);
  }

  private void DeleteSelected()
  {
    var selected = Selected;
    if (selected == null) return;

    var index = _sessions.IndexOf(selected);
    if (!_host.DeleteSession(this, selected)) return;

    // Land on the neighbour, so deleting several in a row is one click each.
    var neighbour = index + 1 < _sessions.Count ? _sessions[index + 1]
      : index > 0 ? _sessions[index - 1]
      : null;
    Reload(selectId: neighbour?.Race.Id);
  }
}
