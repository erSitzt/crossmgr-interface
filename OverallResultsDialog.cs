namespace CrossMgrInterface;

/// <summary>What the Overall results window needs from the application.</summary>
public interface IOverallResultsHost
{
  /// <summary>Loads the motos as stored and adds them up. See <see cref="OverallScorer"/>.</summary>
  OverallResult Score(DbOverall overall);

  /// <summary>Keeps a change of name or points table.</summary>
  void Save(DbOverall overall);

  /// <summary>Prints or exports, after the usual title-and-action question.</summary>
  void PrintResults(IWin32Window owner, DbOverall overall, OverallResult result);

  bool PublishingAvailable { get; }
  void Publish(IWin32Window owner, DbOverall overall);

  /// <summary>Stops the motos counting together. The motos themselves are untouched.</summary>
  bool Delete(IWin32Window owner, DbOverall overall);
}

/// <summary>
/// The overall of a day's motos, class by class, worked out afresh every time
/// it opens - a lap fixed in Moto 1 after Moto 2 has run shows up here without
/// anybody remembering to redo the sums.
///
/// The points table sits on this window rather than in the settings, because
/// it belongs to the overall: a club may score its championship rounds one way
/// and a fun day another.
///
/// Hand-built like the other dialogs, so the designer never rewrites it.
/// </summary>
public sealed class OverallResultsDialog : Form
{
  private readonly IOverallResultsHost _host;
  private readonly DbOverall _overall;
  private OverallResult _result;

  private readonly TextBox _name = new();
  private readonly Label _motos = new();
  private readonly TextBox _points = new();
  private readonly Label _rulesNote = new();
  private readonly TabControl _classes = new();
  private readonly Button _print = new();
  private readonly Button _publish = new();
  private readonly Button _delete = new();

  public OverallResultsDialog(IOverallResultsHost host, DbOverall overall)
  {
    _host = host;
    _overall = overall;

    Text = "Overall results";
    FormBorderStyle = FormBorderStyle.Sizable;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = true;
    ShowInTaskbar = false;
    ClientSize = new Size(900, 600);
    MinimumSize = new Size(720, 440);
    Font = new Font("Segoe UI", 9F);

    var root = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 2,
      RowCount = 2,
      Padding = new Padding(12)
    };
    root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
    root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

    var settings = BuildSettings();
    root.Controls.Add(settings, 0, 0);
    root.SetColumnSpan(settings, 2);

    _classes.Dock = DockStyle.Fill;
    root.Controls.Add(_classes, 0, 1);
    root.Controls.Add(BuildButtons(), 1, 1);

    Controls.Add(root);

    _result = _host.Score(_overall);
    ShowResult();

    // Opens on the table, not in the name box with its text selected, and with
    // no row picked out: a highlighted winner reads like a verdict on the row.
    ActiveControl = _classes;
    Shown += (_, _) => ClearGridSelections();
  }

  private void ClearGridSelections()
  {
    foreach (TabPage page in _classes.TabPages)
      foreach (var grid in page.Controls.OfType<DataGridView>())
      {
        grid.ClearSelection();
        grid.CurrentCell = null;
      }
  }

  /// <summary>The overall as last worked out, for the help picture and the tests.</summary>
  public OverallResult Result => _result;

  private Control BuildSettings()
  {
    var panel = new TableLayoutPanel
    {
      Dock = DockStyle.Top,
      AutoSize = true,
      ColumnCount = 4,
      Padding = new Padding(0, 0, 0, 8)
    };
    panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

    panel.Controls.Add(Caption("Name"), 0, 0);
    _name.Text = _overall.Name;
    _name.Dock = DockStyle.Fill;
    _name.Leave += (_, _) => SaveName();
    panel.Controls.Add(_name, 1, 0);
    panel.SetColumnSpan(_name, 3);

    panel.Controls.Add(Caption("Motos"), 0, 1);
    _motos.AutoSize = true;
    _motos.Margin = new Padding(3, 6, 3, 6);
    panel.Controls.Add(_motos, 1, 1);
    panel.SetColumnSpan(_motos, 3);

    panel.Controls.Add(Caption("Points"), 0, 2);
    _points.Dock = DockStyle.Fill;
    _points.Text = OverallRules.FromOverall(_overall).PointsText;
    _points.Leave += (_, _) => ApplyRules();
    _points.KeyDown += (_, e) =>
    {
      if (e.KeyCode != Keys.Enter) return;
      e.SuppressKeyPress = true;
      ApplyRules();
    };
    panel.Controls.Add(_points, 1, 2);
    panel.SetColumnSpan(_points, 3);

    _rulesNote.AutoSize = true;
    _rulesNote.ForeColor = Color.DimGray;
    _rulesNote.Margin = new Padding(3, 2, 3, 0);
    panel.Controls.Add(_rulesNote, 1, 3);
    panel.SetColumnSpan(_rulesNote, 3);

    return panel;

    static Label Caption(string text) => new()
    {
      Text = text,
      AutoSize = true,
      Margin = new Padding(3, 6, 12, 3),
      Font = new Font("Segoe UI", 9F, FontStyle.Bold)
    };
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

    Configure(_print, "Results...", () => _host.PrintResults(this, _overall, _result));
    Configure(_publish, "Publish...", () => _host.Publish(this, _overall));
    _publish.Visible = _host.PublishingAvailable;
    Configure(_delete, "Stop counting together...", () =>
    {
      if (!_host.Delete(this, _overall)) return;
      DialogResult = DialogResult.OK;
      Close();
    });
    _delete.Height = 48;

    var close = new Button
    {
      Text = "Close",
      Width = 150,
      Height = 34,
      Margin = new Padding(0, 24, 0, 0),
      DialogResult = DialogResult.Cancel
    };

    column.Controls.AddRange(new Control[] { _print, _publish, _delete, close });
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

  private void SaveName()
  {
    var name = _name.Text.Trim();
    if (name.Length == 0 || name == _overall.Name)
    {
      _name.Text = _overall.Name;
      return;
    }

    _overall.Name = name;
    _host.Save(_overall);
    Rescore();
  }

  private void ApplyRules()
  {
    var points = OverallRules.ParsePoints(_points.Text);
    if (points == null)
    {
      // Put back what is in force rather than scoring the day on nothing.
      _points.Text = OverallRules.FromOverall(_overall).PointsText;
      _rulesNote.ForeColor = Color.DarkRed;
      _rulesNote.Text = "Type the points for 1st, 2nd, 3rd ... separated by commas.";
      return;
    }

    // The standard table is stored as none, so it reads as "the standard" and
    // not as a club's own table that happens to match it.
    var newTable = points.SequenceEqual(OverallRules.FimPoints) ? null : points;
    if (Same(_overall.PointsTable, newTable))
    {
      ShowRulesNote();
      return;
    }

    _overall.PointsTable = newTable;
    _host.Save(_overall);
    _points.Text = OverallRules.FromOverall(_overall).PointsText;
    Rescore();

    static bool Same(List<int>? a, List<int>? b) =>
      a == null ? b == null : b != null && a.SequenceEqual(b);
  }

  private void Rescore()
  {
    _result = _host.Score(_overall);
    ShowResult();
  }

  private void ShowResult()
  {
    _motos.Text = _result.MotoTitles.Count == 0
      ? "none - the motos of this overall have been deleted"
      : string.Join("   ·   ", _result.MotoTitles.Select((t, i) => $"{i + 1}. {t}"));
    if (_result.UnfinishedMotos.Count > 0)
    {
      _motos.Text += $"\n(provisional - not finished yet: {string.Join(", ", _result.UnfinishedMotos)})";
      _motos.ForeColor = Color.DarkOrange;
    }
    else
    {
      _motos.ForeColor = SystemColors.ControlText;
    }

    ShowRulesNote();

    var selected = _classes.SelectedIndex;
    _classes.TabPages.Clear();

    if (_result.Classes.Count == 0)
    {
      var empty = new TabPage("Overall");
      empty.Controls.Add(new Label
      {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Color.DimGray,
        Text = "Nobody has been scored in these motos yet."
      });
      _classes.TabPages.Add(empty);
    }

    foreach (var cls in _result.Classes)
    {
      var page = new TabPage(OverallReportGenerator.ClassTitle(cls));
      page.Controls.Add(BuildTable(cls));
      _classes.TabPages.Add(page);
    }

    if (selected >= 0 && selected < _classes.TabPages.Count) _classes.SelectedIndex = selected;
    ClearGridSelections();

    _print.Enabled = _result.Classes.Count > 0;
    _publish.Enabled = _result.Classes.Count > 0;
  }

  private void ShowRulesNote()
  {
    _rulesNote.ForeColor = Color.DimGray;
    var rules = _result.Rules;
    _rulesNote.Text = (rules.IsFim ? "FIM / DMSB points. " : "") +
                      "A rider who retired keeps the points of their place. " +
                      "Ties go to the better place in the last moto.";
  }

  private DataGridView BuildTable(OverallClass cls)
  {
    var grid = new DataGridView
    {
      Dock = DockStyle.Fill,
      ReadOnly = true,
      AllowUserToAddRows = false,
      AllowUserToDeleteRows = false,
      AllowUserToResizeRows = false,
      RowHeadersVisible = false,
      SelectionMode = DataGridViewSelectionMode.FullRowSelect,
      BackgroundColor = Color.White,
      BorderStyle = BorderStyle.None,
      AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
      ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
    };
    grid.RowTemplate.Height = 26;
    grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

    var headers = OverallReportGenerator.Headers(_result);
    for (var i = 0; i < headers.Count; i++)
    {
      var index = grid.Columns.Add($"c{i}", headers[i]);
      var column = grid.Columns[index];
      column.SortMode = DataGridViewColumnSortMode.NotSortable;
      column.Width = i switch
      {
        0 => 48,
        1 => 52,
        2 => 230,
        _ when i == headers.Count - 1 => 60,
        _ => 62
      };
      if (i != 2) column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
    }
    var total = grid.Columns[headers.Count - 1];
    total.DefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

    // The points columns are the lesser half of each moto: greyed, so the eye
    // runs down the places.
    for (var m = 0; m < _result.MotoTitles.Count; m++)
      grid.Columns[4 + m * 2].DefaultCellStyle.ForeColor = Color.DimGray;

    foreach (var entry in cls.Entries)
    {
      var row = grid.Rows[grid.Rows.Add(OverallReportGenerator.Cells(entry).Cast<object>().ToArray())];
      if (!entry.Rank.HasValue) row.DefaultCellStyle.ForeColor = Color.DimGray;
      if (entry.Warning != null)
      {
        row.DefaultCellStyle.BackColor = Color.FromArgb(255, 243, 205);
        row.Cells[2].ToolTipText = entry.Warning;
        row.Cells[2].Value = "⚠ " + row.Cells[2].Value;
      }
    }

    // A grid selects its first row as it is shown; that is undone once it is.
    grid.VisibleChanged += (_, _) =>
    {
      grid.ClearSelection();
      grid.CurrentCell = null;
    };
    return grid;
  }
}
