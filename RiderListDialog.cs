namespace CrossMgrInterface;

/// <summary>
/// The imported rider list, every row of it, with anything wrong marked - and
/// the place to put it right before the first session.
///
/// Until now the list could only be seen in the wizard's preview, and changed
/// only by fixing the club's file and importing it again. A rider who turned up
/// with a new transponder, or a class typed wrong, meant finding the file on a
/// laptop in a field. Here the operator edits the row, or holds the new
/// transponder at the reader, and saves.
///
/// Works on copies: nothing reaches the list in use until Save.
/// Hand-built like the other dialogs, so the designer never rewrites it.
/// </summary>
public sealed class RiderListDialog : Form
{
  private static readonly Color ErrorBack = Color.FromArgb(255, 226, 222);
  private static readonly Color WarningBack = Color.FromArgb(255, 243, 205);

  private readonly Func<IReadOnlyList<RiderDataImporter.RiderImportData>, string?>? _save;
  private readonly Func<RiderListSource?>? _importAgain;
  private readonly bool _teamEvent;

  private readonly DataGridView _grid = new();
  private readonly Label _summary = new();
  private readonly TextBox _search = new();
  private readonly ComboBox _classFilter = new();
  private readonly Label _problems = new();
  private readonly Label _scanStatus = new();
  private readonly Button _scan = new();
  private readonly Button _remove = new();
  private readonly Button _saveButton = new();

  private List<RiderDataImporter.RiderImportData> _rows = new();
  private IReadOnlyList<(int Row, string Reason)> _skipped = Array.Empty<(int, string)>();
  private bool _dirty;
  private bool _filling;
  private bool _refillingClasses;

  /// <summary>Written by the reader's thread, read by the UI's: see <see cref="IsScanning"/>.</summary>
  private volatile bool _scanning;

  private const string ColNumber = "Number";
  private const string ColFirst = "FirstName";
  private const string ColLast = "LastName";
  private const string ColTeam = "Team";
  private const string ColClass = "Class";
  private const string ColMachine = "Machine";
  private const string ColPublic = "Public";
  private const string ColTag = "Transponder";
  private const string ColEntry = "Entry";
  private const string ColProblem = "Problem";

  /// <param name="save">Saves the list and puts it in use; returns where it went, or null when it could not.</param>
  /// <param name="importAgain">Runs the normal import; returns the new list, or null when nothing was imported.</param>
  public RiderListDialog(
    RiderListSource source,
    bool teamEvent,
    Func<IReadOnlyList<RiderDataImporter.RiderImportData>, string?>? save = null,
    Func<RiderListSource?>? importAgain = null)
  {
    _teamEvent = teamEvent;
    _save = save;
    _importAgain = importAgain;

    Text = "Rider list";
    FormBorderStyle = FormBorderStyle.Sizable;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = true;
    ShowInTaskbar = false;
    ClientSize = new Size(1100, 640);
    MinimumSize = new Size(800, 420);
    KeyPreview = true;

    var root = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 5,
      Padding = new Padding(12)
    };
    root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

    _summary.AutoSize = true;
    _summary.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
    _summary.Margin = new Padding(0, 0, 0, 8);

    // ---- Search and class filter ----
    var filters = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
    var searchLabel = new Label { Text = "Find:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) };
    _search.Width = 220;
    _search.PlaceholderText = "number, name, team or transponder";
    _search.TextChanged += (_, _) => ApplyFilter(leaveCurrentRow: false);
    var classLabel = new Label { Text = "Class:", AutoSize = true, Margin = new Padding(16, 6, 4, 0) };
    _classFilter.DropDownStyle = ComboBoxStyle.DropDownList;
    _classFilter.Width = 160;
    _classFilter.SelectedIndexChanged += (_, _) =>
    {
      if (!_refillingClasses) ApplyFilter(leaveCurrentRow: false);
    };
    filters.Controls.AddRange(new Control[] { searchLabel, _search, classLabel, _classFilter });

    // ---- The list ----
    _grid.Dock = DockStyle.Fill;
    _grid.AllowUserToAddRows = false;
    _grid.AllowUserToDeleteRows = false;
    _grid.AllowUserToResizeRows = false;
    _grid.RowHeadersVisible = false;
    _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
    _grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
    _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
    _grid.BackgroundColor = SystemColors.Window;
    _grid.BorderStyle = BorderStyle.FixedSingle;

    AddColumn(ColNumber, "#", 45);
    AddColumn(ColFirst, "First name", 90);
    AddColumn(ColLast, "Last name", 90);
    AddColumn(ColTeam, "Team", 90);
    AddColumn(ColClass, "Class", 60);
    AddColumn(ColMachine, "Machine", 70);
    AddColumn(ColPublic, "Public name", 55).ToolTipText =
      "yes or no - whether the rider agrees to their full name on the website";
    AddColumn(ColTag, "Transponder", 110).DefaultCellStyle.Font = new Font("Consolas", 9F);
    AddColumn(ColEntry, "Races as", 90, readOnly: true).Visible = teamEvent;
    AddColumn(ColProblem, "Problem", 200, readOnly: true);

    _grid.CellValueChanged += (_, e) => CellEdited(e.RowIndex, e.ColumnIndex);
    _grid.SelectionChanged += (_, _) => UpdateButtons();

    // ---- Problems not on any one row: skipped rows, team issues ----
    _problems.AutoSize = true;
    _problems.MaximumSize = new Size(1060, 0);
    _problems.Margin = new Padding(0, 6, 0, 0);

    // ---- Buttons ----
    var buttons = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill,
      AutoSize = true,
      WrapContents = false,
      Margin = new Padding(0, 10, 0, 0)
    };

    var add = MakeButton("Add rider", (_, _) => AddRider());
    _remove.Text = "Remove rider";
    _remove.Size = new Size(120, 32);
    _remove.Click += (_, _) => RemoveRiders();
    _scan.Text = "Scan transponder";
    _scan.Size = new Size(140, 32);
    _scan.Click += (_, _) => ToggleScan();

    _scanStatus.AutoSize = true;
    _scanStatus.Margin = new Padding(8, 9, 8, 0);
    _scanStatus.ForeColor = Color.DimGray;
    _scanStatus.MaximumSize = new Size(360, 0);

    var importAgainButton = MakeButton("Import again...", (_, _) => ImportAgain());
    importAgainButton.Visible = importAgain != null;

    _saveButton.Text = "Save";
    _saveButton.Size = new Size(100, 32);
    _saveButton.Click += (_, _) => Save();
    _saveButton.Visible = save != null;

    var close = MakeButton("Close", (_, _) => Close());
    CancelButton = close;

    buttons.Controls.AddRange(new Control[] { add, _remove, _scan, _scanStatus });

    var right = new FlowLayoutPanel
    {
      AutoSize = true,
      WrapContents = false,
      Anchor = AnchorStyles.Right,
      FlowDirection = FlowDirection.LeftToRight
    };
    right.Controls.AddRange(new Control[] { importAgainButton, _saveButton, close });

    var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1 };
    bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
    bottom.Controls.Add(buttons, 0, 0);
    bottom.Controls.Add(right, 1, 0);

    root.Controls.Add(_summary, 0, 0);
    root.Controls.Add(filters, 0, 1);
    root.Controls.Add(_grid, 0, 2);
    root.Controls.Add(_problems, 0, 3);
    root.Controls.Add(bottom, 0, 4);
    Controls.Add(root);

    FormClosing += (_, e) => e.Cancel = !MayClose();

    ShowRows(source);
  }

  /// <summary>True while the next read at the reader should fill in the selected rider's transponder.</summary>
  public bool IsScanning => _scanning;

  /// <summary>Whether anything has been changed since the list was opened or last saved.</summary>
  public bool HasUnsavedChanges => _dirty;

  /// <summary>The grid, for tests that edit it the way the operator would.</summary>
  internal DataGridView Grid => _grid;

  /// <summary>The class filter, for tests.</summary>
  internal ComboBox ClassFilter => _classFilter;

  /// <summary>
  /// Fills the grid from <paramref name="source"/>, as copies. Internal so the
  /// help's screenshot can show a list without an import.
  /// </summary>
  internal void ShowRows(RiderListSource source)
  {
    _rows = source.Rows.Select(RiderDataImporter.Copy).ToList();
    _skipped = source.Skipped;
    _dirty = false;

    _filling = true;
    _grid.Rows.Clear();
    foreach (var row in _rows)
      _grid.Rows[_grid.Rows.Add()].Tag = row;
    foreach (DataGridViewRow gridRow in _grid.Rows)
      Fill(gridRow);
    _filling = false;

    FillClassFilter();
    Recheck();
  }

  /// <summary>Selects the <paramref name="index"/>th rider, for the help's screenshot.</summary>
  internal void SelectRow(int index)
  {
    if (index < 0 || index >= _grid.Rows.Count) return;
    _grid.CurrentCell = _grid.Rows[index].Cells[ColNumber];
  }

  // ---- Editing ---------------------------------------------------------------------

  private void Fill(DataGridViewRow gridRow)
  {
    var row = (RiderDataImporter.RiderImportData)gridRow.Tag!;
    gridRow.Cells[ColNumber].Value = row.RiderNumber;
    gridRow.Cells[ColFirst].Value = row.FirstName;
    gridRow.Cells[ColLast].Value = row.LastName;
    gridRow.Cells[ColTeam].Value = row.Team;
    gridRow.Cells[ColClass].Value = row.Category;
    gridRow.Cells[ColMachine].Value = row.Machine;
    gridRow.Cells[ColPublic].Value = row.ShowName switch { true => "yes", false => "no", null => "" };
    gridRow.Cells[ColTag].Value = row.TagID;
  }

  private void CellEdited(int rowIndex, int columnIndex)
  {
    if (_filling || rowIndex < 0 || columnIndex < 0) return;

    var gridRow = _grid.Rows[rowIndex];
    var row = (RiderDataImporter.RiderImportData)gridRow.Tag!;
    var column = _grid.Columns[columnIndex].Name;
    var text = (gridRow.Cells[columnIndex].Value?.ToString() ?? "").Trim();

    switch (column)
    {
      case ColNumber: row.RiderNumber = text; break;
      case ColFirst: row.FirstName = text; break;
      case ColLast: row.LastName = text; break;
      case ColTeam: row.Team = text; break;
      case ColClass: row.Category = text; FillClassFilter(); break;
      case ColMachine: row.Machine = text; break;
      case ColTag: row.TagID = text; break;
      case ColPublic:
        row.ShowName = NamePrivacy.ParseShowName(text);
        // Show what it was read as, so "maybe" does not look accepted.
        _filling = true;
        gridRow.Cells[ColPublic].Value = row.ShowName switch { true => "yes", false => "no", null => "" };
        _filling = false;
        break;
      default: return;
    }

    _dirty = true;
    Recheck();
  }

  private void AddRider()
  {
    StopScan(null);

    var row = new RiderDataImporter.RiderImportData();
    _rows.Add(row);

    // A filter would hide the new, empty row the moment it appears.
    _search.Text = "";
    if (_classFilter.Items.Count > 0) _classFilter.SelectedIndex = 0;

    var index = _grid.Rows.Add();
    _grid.Rows[index].Tag = row;
    _filling = true;
    Fill(_grid.Rows[index]);
    _filling = false;

    _dirty = true;
    Recheck();

    _grid.CurrentCell = _grid.Rows[index].Cells[ColNumber];
    _grid.BeginEdit(true);
  }

  private void RemoveRiders()
  {
    var selected = SelectedGridRows();
    if (selected.Count == 0) return;

    var what = selected.Count == 1
      ? RiderListCheck.Describe((RiderDataImporter.RiderImportData)selected[0].Tag!)
      : $"{selected.Count} riders";

    var answer = MessageBox.Show(this,
      $"Remove {what} from the rider list?\n\nLaps already recorded are kept; their transponder will show " +
      "as UNKNOWN from then on.",
      "Remove rider", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
    if (answer != DialogResult.Yes) return;

    StopScan(null);
    foreach (var gridRow in selected)
    {
      _rows.Remove((RiderDataImporter.RiderImportData)gridRow.Tag!);
      _grid.Rows.Remove(gridRow);
    }

    _dirty = true;
    FillClassFilter();
    Recheck();
  }

  // ---- Scanning a transponder --------------------------------------------------------

  /// <summary>Scan transponder pressed. Internal so a test can press it.</summary>
  internal void ToggleScan()
  {
    if (_scanning)
    {
      StopScan(null);
      return;
    }

    if (SelectedGridRows().Count != 1)
    {
      _scanStatus.ForeColor = Color.Firebrick;
      _scanStatus.Text = "Select one rider first.";
      return;
    }

    _scanning = true;
    _scan.Text = "Stop scanning";
    _scanStatus.ForeColor = Color.DarkBlue;
    _scanStatus.Text = "Hold the transponder at the reader - the next one it reads goes on " +
                       RiderListCheck.Describe((RiderDataImporter.RiderImportData)SelectedGridRows()[0].Tag!) + ".";
  }

  private void StopScan(string? message, Color? colour = null)
  {
    _scanning = false;
    _scan.Text = "Scan transponder";
    _scanStatus.ForeColor = colour ?? Color.DimGray;
    _scanStatus.Text = message ?? "";
  }

  /// <summary>
  /// A read from the reader, while scanning. Call on the UI thread. Puts the
  /// transponder on the selected rider - unless another rider already has it,
  /// which is said rather than quietly listing it twice.
  /// </summary>
  public void OfferRead(string tagId)
  {
    if (!_scanning) return;

    var selected = SelectedGridRows();
    if (selected.Count != 1)
    {
      StopScan("Select one rider first.", Color.Firebrick);
      return;
    }

    var gridRow = selected[0];
    var row = (RiderDataImporter.RiderImportData)gridRow.Tag!;

    var owner = _rows.FirstOrDefault(r => r != row &&
                                          string.Equals(r.TagID.Trim(), tagId, StringComparison.OrdinalIgnoreCase));
    if (owner != null)
    {
      StopScan($"Transponder {tagId} is already on {RiderListCheck.Describe(owner)}. Nothing was changed.",
        Color.Firebrick);
      return;
    }

    gridRow.Cells[ColTag].Value = tagId;   // CellEdited takes it from here
    StopScan($"Transponder {tagId} is now on {RiderListCheck.Describe(row)}.", Color.DarkGreen);
  }

  // ---- Checks ----------------------------------------------------------------------

  private void Recheck()
  {
    var problems = RiderListCheck.Run(_rows, _teamEvent);
    var byRow = problems.ToLookup(p => p.Row);

    var roster = _teamEvent ? TeamRoster.Build(_rows) : TeamRoster.Empty;

    _filling = true;
    foreach (DataGridViewRow gridRow in _grid.Rows)
    {
      var index = _rows.IndexOf((RiderDataImporter.RiderImportData)gridRow.Tag!);
      var mine = byRow[index].ToList();

      gridRow.Cells[ColProblem].Value = string.Join("; ", mine.Select(p => p.Message));
      gridRow.DefaultCellStyle.BackColor =
        mine.Any(p => p.Severity == RiderListSeverity.Error) ? ErrorBack
        : mine.Count > 0 ? WarningBack
        : SystemColors.Window;

      if (_teamEvent)
      {
        var key = roster.EntryKeyFor(_rows[index].TagID.Trim());
        var team = key != null ? roster.TeamFor(key) : null;
        gridRow.Cells[ColEntry].Value = team != null ? $"{team.Name} ({team.Members.Count})" : "solo";
      }
    }
    _filling = false;

    _summary.Text = RiderListCheck.Summary(_rows, problems) +
                    (_teamEvent ? $"  ·  races as {roster.Summary}" : "") +
                    (_dirty ? "  ·  not saved yet" : "");
    _summary.ForeColor = problems.Any(p => p.Severity == RiderListSeverity.Error) ? Color.Firebrick
      : problems.Count > 0 ? Color.DarkOrange
      : Color.DarkGreen;

    var lines = new List<string>();
    if (_skipped.Count > 0)
    {
      lines.Add($"{_skipped.Count} row(s) of the file were not read, so they are not in this list: " +
                string.Join(", ", _skipped.Take(6).Select(s => $"row {s.Row} ({s.Reason})")) +
                (_skipped.Count > 6 ? $" and {_skipped.Count - 6} more" : "") +
                ". Add them with Add rider, or fix the file and import it again.");
    }
    lines.AddRange(roster.Issues.Select(i => (i.Severity == TeamRosterSeverity.Error ? "Team: " : "Team (warning): ") + i.Message));

    _problems.Text = string.Join("\n", lines);
    _problems.ForeColor = roster.HasErrors || _skipped.Count > 0 ? Color.Firebrick : Color.DarkOrange;
    _problems.Visible = lines.Count > 0;

    ApplyFilter(leaveCurrentRow: true);
    UpdateButtons();
  }

  private void FillClassFilter()
  {
    var current = _classFilter.SelectedItem as string;
    var classes = _rows
      .Select(r => r.Category.Trim())
      .Where(c => c.Length > 0)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
      .ToList();

    // Refilled while a class cell is being committed, too: re-filtering from
    // here would move the current cell inside the grid's own event.
    _refillingClasses = true;
    _classFilter.BeginUpdate();
    _classFilter.Items.Clear();
    _classFilter.Items.Add("All classes");
    _classFilter.Items.Add("Needs a look");
    foreach (var c in classes) _classFilter.Items.Add(c);
    var at = current == null ? 0 : _classFilter.Items.IndexOf(current);
    _classFilter.SelectedIndex = at < 0 ? 0 : at;
    _classFilter.EndUpdate();
    _refillingClasses = false;
  }

  /// <param name="leaveCurrentRow">
  /// After an edit: the row being edited stays in view even if it no longer
  /// matches - fixing the class of a "Needs a look" rider must not snatch it
  /// away mid-edit. Moving the current cell from inside the grid's own
  /// CellValueChanged would also throw.
  /// </param>
  private void ApplyFilter(bool leaveCurrentRow)
  {
    var search = _search.Text.Trim();
    var filter = _classFilter.SelectedIndex;
    var chosenClass = _classFilter.SelectedItem as string;

    var current = _grid.CurrentRow;
    // A row with the current cell cannot be hidden; move off it first.
    if (!leaveCurrentRow) _grid.CurrentCell = null;

    foreach (DataGridViewRow gridRow in _grid.Rows)
    {
      if (leaveCurrentRow && gridRow == current) continue;

      var row = (RiderDataImporter.RiderImportData)gridRow.Tag!;

      var matchesSearch = search.Length == 0 ||
                          new[] { row.RiderNumber, row.FullName, row.Team, row.TagID, row.Category }
                            .Any(v => v.Contains(search, StringComparison.OrdinalIgnoreCase));

      var matchesClass = filter <= 0
                         || (filter == 1 && gridRow.DefaultCellStyle.BackColor != SystemColors.Window)
                         || (filter > 1 && string.Equals(row.Category.Trim(), chosenClass, StringComparison.OrdinalIgnoreCase));

      gridRow.Visible = matchesSearch && matchesClass;
    }
  }

  private void UpdateButtons()
  {
    var count = SelectedGridRows().Count;
    _remove.Enabled = count > 0;
    _scan.Enabled = _scanning || count == 1;
  }

  private List<DataGridViewRow> SelectedGridRows() =>
    _grid.SelectedCells.Cast<DataGridViewCell>()
      .Select(c => _grid.Rows[c.RowIndex])
      .Where(r => r.Visible)
      .Distinct()
      .ToList();

  // ---- Saving -----------------------------------------------------------------------

  private bool Save()
  {
    if (_save == null) return true;

    _grid.EndEdit();

    var untimed = _rows.FindIndex(r => r.TagID.Trim().Length == 0);
    if (untimed >= 0)
    {
      var gridRow = _grid.Rows.Cast<DataGridViewRow>().First(r => r.Tag == _rows[untimed]);
      _search.Text = "";
      if (_classFilter.Items.Count > 0) _classFilter.SelectedIndex = 0;
      _grid.CurrentCell = gridRow.Cells[ColTag];

      MessageBox.Show(this,
        $"{RiderListCheck.Describe(_rows[untimed])} has no transponder, so it cannot be timed.\n\n" +
        "Type the transponder in, use Scan transponder, or remove the rider.",
        "Save rider list", MessageBoxButtons.OK, MessageBoxIcon.Warning);
      return false;
    }

    var savedTo = _save(_rows.Select(RiderDataImporter.Copy).ToList());
    if (savedTo == null) return false;

    // What was skipped from the old file is no longer the list in use.
    _skipped = Array.Empty<(int, string)>();
    _dirty = false;
    Recheck();
    StopScan($"Saved as {Path.GetFileName(savedTo)} - used from now on.", Color.DarkGreen);
    return true;
  }

  private void ImportAgain()
  {
    if (_importAgain == null) return;
    if (_dirty)
    {
      var answer = MessageBox.Show(this,
        "Importing a file replaces the whole list, and the changes made here have not been saved.\n\nImport anyway?",
        "Import again", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
      if (answer != DialogResult.Yes) return;
    }

    StopScan(null);
    var source = _importAgain();
    if (source != null) ShowRows(source);
  }

  private bool MayClose()
  {
    StopScan(null);
    _grid.EndEdit();
    if (!_dirty || _save == null) return true;

    var answer = MessageBox.Show(this,
      "The rider list has changes that are not saved.\n\nSave them before closing?",
      "Rider list", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

    return answer switch
    {
      DialogResult.Yes => Save(),
      DialogResult.No => true,
      _ => false
    };
  }

  // ---- Building --------------------------------------------------------------------

  private DataGridViewTextBoxColumn AddColumn(string name, string header, int weight, bool readOnly = false)
  {
    var column = new DataGridViewTextBoxColumn
    {
      Name = name,
      HeaderText = header,
      FillWeight = weight,
      ReadOnly = readOnly,
      SortMode = DataGridViewColumnSortMode.NotSortable
    };
    if (readOnly) column.DefaultCellStyle.ForeColor = Color.DimGray;
    _grid.Columns.Add(column);
    return column;
  }

  private static Button MakeButton(string text, EventHandler click)
  {
    var button = new Button { Text = text, Size = new Size(120, 32) };
    button.Click += click;
    return button;
  }
}

/// <summary>A rider list and the file rows that could not be read into it.</summary>
public sealed record RiderListSource(
  IReadOnlyList<RiderDataImporter.RiderImportData> Rows,
  IReadOnlyList<(int Row, string Reason)> Skipped);
