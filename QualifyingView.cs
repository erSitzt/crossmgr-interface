using System.Reflection;

namespace CrossMgrInterface;

/// <summary>
/// The Qualifying tab: the field ranked by best lap, which is the order riders
/// pick their starting gate in.
///
/// Built in code and handed back as a TabPage, like RaceDayView and
/// TrackTabView, so the WinForms designer never rewrites it.
///
/// The grid runs in virtual mode for the same reason the riders grid does: a
/// 250-rider field pushed through DataGridView cells takes the better part of a
/// second, and this refreshes on every lap.
/// </summary>
public sealed class QualifyingView
{
  private const string AllClasses = "All Classes";

  private DataGridView _grid = null!;
  private ComboBox _classFilter = null!;
  private Label _summary = null!;
  private Font? _boldFont;

  private List<QualifyingRowData> _rows = new();

  /// <summary>Suppresses the filter event while the class list is repopulated.</summary>
  private bool _populatingClasses;

  /// <summary>The operator asked for the gate pick sheet.</summary>
  public event EventHandler? PrintRequested;

  /// <summary>A different class was chosen. Carries the class name, or "All Classes".</summary>
  public event EventHandler<string>? ClassFilterChanged;

  /// <summary>A row was double-clicked. Carries the transponder ID.</summary>
  public event EventHandler<string>? RiderActivated;

  public TabPage CreateQualifyingTab()
  {
    var page = new TabPage
    {
      Name = "tabPageQualifying",
      Text = "Qualifying",
      BackColor = Color.White,
      Padding = new Padding(8)
    };

    var layout = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 3
    };
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

    layout.Controls.Add(BuildTopStrip(), 0, 0);
    layout.Controls.Add(BuildGrid(), 0, 1);

    _summary = new Label
    {
      Dock = DockStyle.Fill,
      AutoSize = true,
      ForeColor = Color.DimGray,
      Padding = new Padding(4, 6, 0, 0)
    };
    layout.Controls.Add(_summary, 0, 2);

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

    var caption = new Label
    {
      Text = "Gate pick order - ranked by best lap",
      AutoSize = true,
      Font = new Font("Segoe UI", 11F, FontStyle.Bold),
      Margin = new Padding(0, 8, 16, 0)
    };

    var classCaption = new Label
    {
      Text = "Class:",
      AutoSize = true,
      Margin = new Padding(0, 10, 4, 0)
    };

    _classFilter = new ComboBox
    {
      DropDownStyle = ComboBoxStyle.DropDownList,
      Width = 160,
      Margin = new Padding(0, 6, 16, 0)
    };
    _classFilter.Items.Add(AllClasses);
    _classFilter.SelectedIndex = 0;
    _classFilter.SelectedIndexChanged += (_, _) =>
    {
      if (_populatingClasses) return;
      ClassFilterChanged?.Invoke(this, _classFilter.SelectedItem as string ?? AllClasses);
    };

    var print = new Button
    {
      Text = "Print gate pick order...",
      AutoSize = true,
      Height = 32,
      Margin = new Padding(0, 4, 0, 0)
    };
    print.Click += (s, e) => PrintRequested?.Invoke(s, e);

    strip.Controls.AddRange(new Control[] { caption, classCaption, _classFilter, print });
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

    // DataGridView exposes DoubleBuffered only as a protected property; the
    // riders grid and the lap chart panel use the same trick.
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

      if (e.ColumnIndex == QualifyingRowData.ColStatus && row.NeedsCheck)
      {
        e.CellStyle.ForeColor = Color.DarkOrange;
        e.CellStyle.Font = _boldFont ??= new Font(_grid.Font, FontStyle.Bold);
      }
    };

    _grid.CellToolTipTextNeeded += (_, e) =>
    {
      if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
      e.ToolTipText = _rows[e.RowIndex].Tooltip;
    };

    // A missed read in qualifying costs someone a gate pick, so corrections are
    // one double-click from the sheet, exactly as on the riders grid.
    _grid.CellDoubleClick += (_, e) =>
    {
      if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
      RiderActivated?.Invoke(this, _rows[e.RowIndex].TagID);
    };

    AddColumn("GatePick", "Pick", 55);
    AddColumn("Number", "#", 55);
    AddColumn("Rider", "Rider", 200);
    AddColumn("Class", "Class", 90);
    AddColumn("BestLap", "Best lap", 95);
    AddColumn("Gap", "Gap", 85);
    AddColumn("Interval", "Int", 85);
    AddColumn("OnLap", "On lap", 65);
    AddColumn("Laps", "Laps", 55);
    AddColumn("Status", "Status", 150);

    return _grid;

    void AddColumn(string name, string header, int width)
    {
      var index = _grid.Columns.Add(name, header);
      _grid.Columns[index].Width = width;
    }
  }

  /// <summary>Fills the class filter, preserving the current choice where it still exists.</summary>
  public void SetClasses(IReadOnlyList<string> classes, string selected)
  {
    var wanted = new List<string> { AllClasses };
    wanted.AddRange(classes);

    var current = _classFilter.Items.Cast<string>().ToList();
    if (current.SequenceEqual(wanted, StringComparer.Ordinal))
    {
      SelectWithoutEvent(selected);
      return;
    }

    _populatingClasses = true;
    try
    {
      _classFilter.Items.Clear();
      foreach (var name in wanted) _classFilter.Items.Add(name);
      _classFilter.SelectedItem = wanted.Contains(selected, StringComparer.Ordinal)
        ? selected
        : AllClasses;
    }
    finally
    {
      _populatingClasses = false;
    }
  }

  private void SelectWithoutEvent(string selected)
  {
    if (Equals(_classFilter.SelectedItem, selected)) return;

    _populatingClasses = true;
    try { _classFilter.SelectedItem = selected; }
    finally { _populatingClasses = false; }
  }

  /// <summary>
  /// Replaces the sheet. Keeps the selected rider rather than the selected row
  /// index, because a rider's pick moves as the session runs.
  /// </summary>
  public void SetRows(List<QualifyingRowData> rows, string summary)
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

    // After CurrentCell, which scrolls the selection into view on its own.
    if (firstVisible >= 0 && firstVisible < _grid.RowCount)
      _grid.FirstDisplayedScrollingRowIndex = firstVisible;

    _summary.Text = summary;
  }
}
