namespace CrossMgrInterface;

/// <summary>
/// Riders the session did not count because of their class, and the one thing
/// to do about one of them: count them here after all, with every read kept
/// for them.
///
/// Usually this list is the next group rolling over the loop on the way to the
/// gate, and needs nothing. It matters when the rider list has someone in the
/// wrong class - then they are riding in this session and losing every lap.
///
/// Hand-built like the other dialogs, so the designer never rewrites it.
/// </summary>
public sealed class HeldRidersDialog : Form
{
  private readonly Func<IReadOnlyList<HeldRider>> _list;
  private readonly Action<string> _count;
  private readonly DataGridView _grid = new();
  private readonly Button _countButton = new();
  private readonly Label _empty = new();
  private List<HeldRider> _riders = new();

  public HeldRidersDialog(Func<IReadOnlyList<HeldRider>> list, Action<string> count, string sessionClasses)
  {
    _list = list;
    _count = count;

    Text = "Riders from other classes";
    FormBorderStyle = FormBorderStyle.Sizable;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    ShowInTaskbar = false;
    ClientSize = new Size(760, 420);
    MinimumSize = new Size(600, 300);

    var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(12) };
    root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
    root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

    var intro = new Label
    {
      AutoSize = true,
      MaximumSize = new Size(700, 0),
      Padding = new Padding(0, 0, 0, 10),
      Text = $"This session counts {sessionClasses}. These riders crossed the line but are in another class " +
             "on the rider list, so their reads were kept aside and not counted. Usually that is the next " +
             "group on its way to the gate. If one of them is really riding in this session, select them " +
             "and press Count in this session."
    };
    root.Controls.Add(intro, 0, 0);
    root.SetColumnSpan(intro, 2);

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
    _grid.Font = new Font("Segoe UI", 10.5F);
    _grid.RowTemplate.Height = 30;
    _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
    foreach (var (header, width) in new[] { ("#", 60), ("Name", 220), ("Class", 110), ("Reads", 70), ("Last read", 90) })
    {
      var index = _grid.Columns.Add(header, header);
      _grid.Columns[index].Width = width;
      _grid.Columns[index].SortMode = DataGridViewColumnSortMode.NotSortable;
    }
    _grid.SelectionChanged += (_, _) => _countButton.Enabled = Selected != null;

    _empty.Dock = DockStyle.Fill;
    _empty.TextAlign = ContentAlignment.MiddleCenter;
    _empty.ForeColor = Color.DimGray;
    _empty.Text = "No rider of another class has crossed the line in this session.";

    host.Controls.Add(_grid);
    host.Controls.Add(_empty);
    root.Controls.Add(host, 0, 1);

    var buttons = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill,
      FlowDirection = FlowDirection.TopDown,
      WrapContents = false,
      Padding = new Padding(12, 0, 0, 0)
    };
    _countButton.Text = "Count in this session";
    _countButton.Size = new Size(170, 34);
    _countButton.Margin = new Padding(0, 0, 0, 8);
    _countButton.Click += (_, _) => CountSelected();

    var close = new Button { Text = "Close", Size = new Size(170, 34), Margin = new Padding(0, 24, 0, 0), DialogResult = DialogResult.Cancel };
    buttons.Controls.AddRange(new Control[] { _countButton, close });
    root.Controls.Add(buttons, 1, 1);
    CancelButton = close;

    Controls.Add(root);
    Load += (_, _) => Reload();
  }

  private HeldRider? Selected =>
    _grid.SelectedRows.Count == 1 && _grid.SelectedRows[0].Index < _riders.Count
      ? _riders[_grid.SelectedRows[0].Index]
      : null;

  private void Reload()
  {
    _riders = _list().ToList();
    _grid.Rows.Clear();
    foreach (var rider in _riders)
      _grid.Rows.Add(rider.Number, rider.Name, rider.Class, rider.Reads.ToString(), rider.LastRead.ToString("HH:mm:ss"));

    _grid.Visible = _riders.Count > 0;
    _empty.Visible = _riders.Count == 0;
    _grid.ClearSelection();
    _countButton.Enabled = false;
  }

  private void CountSelected()
  {
    if (Selected is not { } rider) return;

    var answer = MessageBox.Show(this,
      $"Count #{rider.Number} {rider.Name} ({rider.Class}) in this session?\n\n" +
      $"Their {rider.Reads} read(s) so far are counted as laps, and so is every read from now on.",
      "Count in this session", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
    if (answer != DialogResult.Yes) return;

    _count(rider.Tag);
    Reload();
  }
}
