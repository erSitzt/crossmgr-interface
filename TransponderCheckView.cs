using System.Reflection;

namespace CrossMgrInterface;

/// <summary>
/// The transponder check: which riders the timing loop is seeing reliably, and
/// which are not being read.
///
/// Its value is entirely in being looked at during practice, while a rider is
/// still in the paddock and their tag can be moved. Afterwards it is only an
/// explanation of a result that has already gone wrong.
///
/// Built in code and returned as a TabPage, like the other views here.
/// </summary>
public sealed class TransponderCheckView
{
  private DataGridView _grid = null!;
  private Label _headline = null!;
  private Font? _headlineFont;

  private List<TransponderCheckRowData> _rows = new();

  /// <summary>The operator asked for the printable sheet.</summary>
  public event EventHandler? PrintRequested;

  /// <summary>A row was double-clicked. Carries the transponder ID.</summary>
  public event EventHandler<string>? RiderActivated;

  public TabPage CreateTransponderTab()
  {
    var page = new TabPage
    {
      Name = "tabPageTransponder",
      Text = "Transponders",
      BackColor = Color.White,
      Padding = new Padding(8)
    };

    var layout = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 2
    };
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

    layout.Controls.Add(BuildTopStrip(), 0, 0);
    layout.Controls.Add(BuildGrid(), 0, 1);

    page.Controls.Add(layout);
    return page;
  }

  private Control BuildTopStrip()
  {
    var strip = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill,
      AutoSize = true,
      FlowDirection = FlowDirection.LeftToRight,
      WrapContents = false,
      Padding = new Padding(0, 0, 0, 6)
    };

    _headlineFont = new Font("Segoe UI", 12F, FontStyle.Bold);
    _headline = new Label
    {
      Text = "Nobody has been out yet.",
      AutoSize = true,
      Font = _headlineFont,
      Margin = new Padding(0, 8, 24, 0)
    };

    var print = new Button
    {
      Text = "Print transponder check...",
      AutoSize = true,
      Height = 32,
      Margin = new Padding(0, 4, 0, 0)
    };
    print.Click += (s, e) => PrintRequested?.Invoke(s, e);

    strip.Controls.AddRange(new Control[] { _headline, print });
    return strip;
  }

  private Control BuildGrid()
  {
    _grid = new DataGridView
    {
      Dock = DockStyle.Fill,
      ReadOnly = true,
      AllowUserToAddRows = false,
      AllowUserToDeleteRows = false,
      AllowUserToResizeRows = false,
      RowHeadersVisible = false,
      SelectionMode = DataGridViewSelectionMode.FullRowSelect,
      MultiSelect = false,
      BackgroundColor = Color.White,
      BorderStyle = BorderStyle.None,
      AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
      Font = new Font("Segoe UI", 11F),
      RowTemplate = { Height = 30 },
      VirtualMode = true
    };

    // Headers get their own font and an auto height. Without this they inherit
    // the grid's 11pt into the default 23px header row, which clips the bottom
    // of the header text - worse on a scaled display, where it renders taller.
    _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
    _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(0, 4, 0, 4);
    _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;

    typeof(DataGridView).InvokeMember("DoubleBuffered",
      BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
      null, _grid, new object[] { true });

    _grid.CellValueNeeded += (_, e) =>
    {
      if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
      var row = _rows[e.RowIndex];
      e.Value = e.ColumnIndex >= 0 && e.ColumnIndex < row.Cells.Length
        ? row.Cells[e.ColumnIndex] ?? ""
        : "";
    };

    _grid.CellFormatting += (_, e) =>
    {
      if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
      var row = _rows[e.RowIndex];
      if (!row.RowBackColor.IsEmpty) e.CellStyle.BackColor = row.RowBackColor;
      if (!row.RowForeColor.IsEmpty) e.CellStyle.ForeColor = row.RowForeColor;
    };

    _grid.CellToolTipTextNeeded += (_, e) =>
    {
      if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
      var row = _rows[e.RowIndex];
      e.ToolTipText = row.Tooltip.Length > 0
        ? $"{row.Cells[TransponderCheckRowData.ColDetail]}\n\n{row.Tooltip}"
        : row.Cells[TransponderCheckRowData.ColDetail];
    };

    // A tag that is misbehaving usually also needs its laps looking at.
    _grid.CellDoubleClick += (_, e) =>
    {
      if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
      RiderActivated?.Invoke(this, _rows[e.RowIndex].TagID);
    };

    AddColumn("Number", "#", 55);
    AddColumn("Rider", "Rider", 180);
    AddColumn("Class", "Class", 90);
    AddColumn("Laps", "Laps", 55);
    AddColumn("Misses", "Missed", 70);
    AddColumn("Duplicates", "Double", 70);
    AddColumn("Detail", "What the loop saw", 420);

    // The detail is the point of this view, so it takes whatever width is left
    // rather than a fixed 420px that clipped the longer lines.
    _grid.Columns["Detail"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

    return _grid;

    void AddColumn(string name, string header, int width)
    {
      var index = _grid.Columns.Add(name, header);
      _grid.Columns[index].Width = width;
    }
  }

  public void SetRows(List<TransponderCheckRowData> rows, string headline, bool anyProblems)
  {
    var selectedTag = _grid.CurrentRow is { Index: >= 0 } current && current.Index < _rows.Count
      ? _rows[current.Index].TagID
      : null;
    var firstVisible = _grid.RowCount > 0 && _grid.FirstDisplayedScrollingRowIndex >= 0
      ? _grid.FirstDisplayedScrollingRowIndex
      : -1;

    _rows = rows;
    _grid.RowCount = rows.Count;
    _grid.Invalidate();

    if (selectedTag != null)
    {
      var index = _rows.FindIndex(r => r.TagID == selectedTag);
      if (index >= 0 && index < _grid.RowCount)
        _grid.CurrentCell = _grid.Rows[index].Cells[0];
    }

    if (firstVisible >= 0 && firstVisible < _grid.RowCount)
      _grid.FirstDisplayedScrollingRowIndex = firstVisible;

    _headline.Text = headline;
    _headline.ForeColor = anyProblems ? Color.FromArgb(180, 40, 20) : Color.FromArgb(0, 120, 50);
  }
}
