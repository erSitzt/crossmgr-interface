namespace CrossMgrInterface;

/// <summary>What the Master of all classes window may ask of the main window.</summary>
public interface IDayRankingHost
{
  /// <summary>Loads the day's races as stored and ranks every rider. See <see cref="DayRankingScorer"/>.</summary>
  DayRankingResult Score(DbDayRanking ranking);

  /// <summary>Keeps a change of name or measure.</summary>
  void Save(DbDayRanking ranking);

  /// <summary>Prints or exports, after the usual title-and-action question.</summary>
  void PrintResults(IWin32Window owner, DbDayRanking ranking, DayRankingResult result);

  bool PublishingAvailable { get; }
  void Publish(IWin32Window owner, DbDayRanking ranking);
}

/// <summary>
/// Every rider of every class of a race day in one list, worked out afresh
/// every time it opens, so a lap fixed in any race of the day shows here.
///
/// The measure is a choice on this window rather than a setting: both orders
/// are worth a look, and the one picked is what is printed and published.
///
/// Hand-built like the other dialogs, so the designer never rewrites it.
/// </summary>
public sealed class DayRankingDialog : Form
{
  private readonly IDayRankingHost _host;
  private readonly DbDayRanking _ranking;
  private DayRankingResult _result;

  private readonly TextBox _name = new();
  private readonly Label _races = new();
  private readonly RadioButton _best = new();
  private readonly RadioButton _average = new();
  private readonly DataGridView _grid = new();
  private readonly Button _print = new();
  private readonly Button _publish = new();

  public DayRankingDialog(IDayRankingHost host, DbDayRanking ranking)
  {
    _host = host;
    _ranking = ranking;

    Text = $"Master of all classes - {ranking.Day:dd.MM.yyyy}";
    FormBorderStyle = FormBorderStyle.Sizable;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = true;
    ShowInTaskbar = false;
    ClientSize = new Size(1000, 640);
    MinimumSize = new Size(780, 440);
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

    BuildGrid();
    root.Controls.Add(_grid, 0, 1);
    root.Controls.Add(BuildButtons(), 1, 1);

    Controls.Add(root);

    _result = _host.Score(_ranking);
    ShowResult();

    ActiveControl = _grid;
    Shown += (_, _) => ClearSelection();
  }

  /// <summary>The list as last worked out, for the help picture and the tests.</summary>
  public DayRankingResult Result => _result;

  private void ClearSelection()
  {
    _grid.ClearSelection();
    _grid.CurrentCell = null;
  }

  private Control BuildSettings()
  {
    var panel = new TableLayoutPanel
    {
      Dock = DockStyle.Top,
      AutoSize = true,
      ColumnCount = 2,
      Padding = new Padding(0, 0, 0, 8)
    };
    panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

    panel.Controls.Add(Caption("Name"), 0, 0);
    _name.Text = _ranking.Name;
    _name.Dock = DockStyle.Fill;
    _name.Leave += (_, _) => SaveName();
    panel.Controls.Add(_name, 1, 0);

    panel.Controls.Add(Caption("Races"), 0, 1);
    _races.AutoSize = true;
    _races.Margin = new Padding(3, 6, 3, 6);
    panel.Controls.Add(_races, 1, 1);

    panel.Controls.Add(Caption("Rank by"), 0, 2);
    var measures = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
    _best.Text = "Best lap";
    _best.AutoSize = true;
    _average.Text = "Average lap";
    _average.AutoSize = true;
    _average.Margin = new Padding(16, 3, 3, 3);
    _best.Checked = _ranking.Measure == DayRankingMeasure.BestLap;
    _average.Checked = _ranking.Measure == DayRankingMeasure.AverageLap;
    _best.CheckedChanged += (_, _) => { if (_best.Checked) SetMeasure(DayRankingMeasure.BestLap); };
    _average.CheckedChanged += (_, _) => { if (_average.Checked) SetMeasure(DayRankingMeasure.AverageLap); };
    measures.Controls.AddRange(new Control[] { _best, _average });
    panel.Controls.Add(measures, 1, 2);

    var note = new Label
    {
      AutoSize = true,
      ForeColor = Color.DimGray,
      Margin = new Padding(3, 2, 3, 0),
      Text = "Every rider of every race today, all classes. Lap 1 of each race is left out; " +
             $"the average needs at least {DayRankingScorer.MinimumLapsForAverage} laps."
    };
    panel.Controls.Add(note, 1, 3);

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

    Configure(_print, "Results...", () => _host.PrintResults(this, _ranking, _result));
    Configure(_publish, "Publish...", () => _host.Publish(this, _ranking));
    _publish.Visible = _host.PublishingAvailable;

    var close = new Button
    {
      Text = "Close",
      Width = 150,
      Height = 34,
      Margin = new Padding(0, 24, 0, 0),
      DialogResult = DialogResult.Cancel
    };

    column.Controls.AddRange(new Control[] { _print, _publish, close });
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

  private void BuildGrid()
  {
    _grid.Dock = DockStyle.Fill;
    _grid.ReadOnly = true;
    _grid.AllowUserToAddRows = false;
    _grid.AllowUserToDeleteRows = false;
    _grid.AllowUserToResizeRows = false;
    _grid.RowHeadersVisible = false;
    _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
    _grid.BackgroundColor = Color.White;
    _grid.BorderStyle = BorderStyle.None;
    _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
    _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
    _grid.RowTemplate.Height = 26;
    _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

    var headers = DayRankingReportGenerator.Headers;
    for (var i = 0; i < headers.Count; i++)
    {
      var column = _grid.Columns[_grid.Columns.Add($"c{i}", headers[i])];
      column.SortMode = DataGridViewColumnSortMode.NotSortable;
      column.Width = i switch { 0 => 48, 1 => 52, 2 => 200, 3 => 100, 5 => 190, 7 => 52, _ => 86 };
      if (i is 0 or 1 or 4 or 6 or 7 or 8) column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
    }

    _grid.VisibleChanged += (_, _) => ClearSelection();
  }

  private void SaveName()
  {
    var name = _name.Text.Trim();
    if (name.Length == 0 || name == _ranking.Name)
    {
      _name.Text = _ranking.Name;
      return;
    }

    _ranking.Name = name;
    _host.Save(_ranking);
    Rescore();
  }

  private void SetMeasure(DayRankingMeasure measure)
  {
    if (_ranking.Measure == measure) return;
    _ranking.Measure = measure;
    _host.Save(_ranking);
    Rescore();
  }

  private void Rescore()
  {
    _result = _host.Score(_ranking);
    ShowResult();
  }

  private void ShowResult()
  {
    _races.Text = _result.SessionTitles.Count == 0
      ? "none yet - the list fills as the day's races finish"
      : string.Join("   ·   ", _result.SessionTitles);
    if (_result.LeftOut.Count > 0) _races.Text += $"\n(not counted: {string.Join(", ", _result.LeftOut)})";

    var measure = _ranking.Measure;
    var leader = DayRankingReportGenerator.Leader(_result, measure);
    var bold = new Font("Segoe UI", 9F, FontStyle.Bold);
    var timeColumns = measure == DayRankingMeasure.AverageLap ? 6 : 4;
    for (var i = 0; i < _grid.Columns.Count; i++)
      _grid.Columns[i].DefaultCellStyle.Font = i == timeColumns ? bold : null;

    _grid.Rows.Clear();
    foreach (var entry in _result.Ordered(measure))
    {
      var cells = DayRankingReportGenerator.Cells(_result, entry, measure, leader);
      var row = _grid.Rows[_grid.Rows.Add(cells.Cast<object>().ToArray())];
      if (!entry.RankBy(measure).HasValue) row.DefaultCellStyle.ForeColor = Color.DimGray;
      if (entry.Warning != null)
      {
        row.DefaultCellStyle.BackColor = Color.FromArgb(255, 243, 205);
        row.Cells[2].ToolTipText = entry.Warning;
        row.Cells[2].Value = "⚠ " + row.Cells[2].Value;
      }
    }
    ClearSelection();

    var any = _result.Entries.Count > 0;
    _print.Enabled = any;
    _publish.Enabled = any;
  }
}
