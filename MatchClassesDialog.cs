namespace CrossMgrInterface;

/// <summary>
/// Saying which of the rider list's classes a plan's classes are - Group A of
/// a preset is MX1 and MX2 - before the day, so every session counts the
/// right riders. See ClassMapping for what changes and what is warned about.
///
/// Hand-built like the other dialogs, so the designer never rewrites it.
/// </summary>
public sealed class MatchClassesDialog : Form
{
  private readonly DayPlan _plan;
  private readonly IReadOnlyList<string> _listClasses;
  private readonly Dictionary<string, List<string>> _map = new(StringComparer.OrdinalIgnoreCase);

  private readonly ListBox _from = new();
  private readonly CheckedListBox _to = new();
  private readonly Label _toLabel = new();
  private readonly ListBox _preview = new();
  private readonly Label _warnings = new();
  private bool _filling;

  /// <summary>What matching came to, once OK has been pressed.</summary>
  public ClassMappingResult? Result { get; private set; }

  public MatchClassesDialog(DayPlan plan, IReadOnlyList<string> listClasses)
  {
    _plan = plan;
    _listClasses = listClasses;
    foreach (var c in ClassMapping.Unmatched(plan, listClasses)) _map[c] = new List<string>();

    Text = "Match classes";
    FormBorderStyle = FormBorderStyle.FixedDialog;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = false;
    ShowInTaskbar = false;
    ClientSize = new Size(860, 560);
    Font = new Font("Segoe UI", 9.5F);
    var bold = new Font(Font, FontStyle.Bold);

    Controls.Add(new Label
    {
      Location = new Point(16, 12),
      Size = new Size(828, 40),
      Text = "These classes of the plan are not on the rider list. For each, tick the rider list's classes it " +
             "stands for. In a session started in waves they leave in the order ticked here; Move up and Move down change it."
    });

    Controls.Add(new Label { Text = "In the plan", Location = new Point(16, 60), AutoSize = true, Font = bold });
    _from.Location = new Point(16, 84);
    _from.Size = new Size(200, 200);
    _from.IntegralHeight = false;
    foreach (var c in _map.Keys) _from.Items.Add(c);
    _from.SelectedIndexChanged += (_, _) => FillTo();
    Controls.Add(_from);

    _toLabel.Location = new Point(236, 60);
    _toLabel.AutoSize = true;
    _toLabel.Font = bold;
    Controls.Add(_toLabel);
    _to.Location = new Point(236, 84);
    _to.Size = new Size(240, 200);
    _to.IntegralHeight = false;
    _to.CheckOnClick = true;
    _to.ItemCheck += (_, _) => BeginInvoke(CaptureChoice);
    Controls.Add(_to);

    var up = new Button { Text = "Move up", Location = new Point(484, 84), AutoSize = true };
    var down = new Button { Text = "Move down", Location = new Point(484, 118), AutoSize = true };
    up.Click += (_, _) => MoveChoice(-1);
    down.Click += (_, _) => MoveChoice(+1);
    Controls.AddRange(new Control[] { up, down });

    Controls.Add(new Label { Text = "The plan then", Location = new Point(16, 296), AutoSize = true, Font = bold });
    _preview.Location = new Point(16, 320);
    _preview.Size = new Size(828, 140);
    _preview.IntegralHeight = false;
    _preview.SelectionMode = SelectionMode.None;
    Controls.Add(_preview);

    _warnings.Location = new Point(16, 466);
    _warnings.Size = new Size(828, 48);
    _warnings.ForeColor = Color.FromArgb(160, 60, 0);
    Controls.Add(_warnings);

    var ok = new Button { Text = "OK", Size = new Size(100, 30), Location = new Point(ClientSize.Width - 226, ClientSize.Height - 42) };
    ok.Click += (_, _) => Accept();
    var cancel = new Button
    {
      Text = "Not now", Size = new Size(100, 30), Location = new Point(ClientSize.Width - 116, ClientSize.Height - 42),
      DialogResult = DialogResult.Cancel
    };
    Controls.AddRange(new Control[] { ok, cancel });
    AcceptButton = ok;
    CancelButton = cancel;

    if (_from.Items.Count > 0) _from.SelectedIndex = 0;
    Preview();
  }

  private string? Current => _from.SelectedItem as string;

  /// <summary>The right-hand list for the class on the left: its choices first, in order, then the rest.</summary>
  private void FillTo()
  {
    if (Current is not { } from) return;
    _filling = true;
    _toLabel.Text = $"{from} is";
    _to.Items.Clear();
    var chosen = _map[from];
    foreach (var c in chosen) _to.Items.Add(c, true);
    foreach (var c in _listClasses.Where(c => !chosen.Contains(c, StringComparer.OrdinalIgnoreCase))) _to.Items.Add(c, false);
    _filling = false;
  }

  private void CaptureChoice()
  {
    if (_filling || Current is not { } from) return;
    _map[from] = _to.CheckedItems.Cast<string>().ToList();
    Preview();
  }

  private void MoveChoice(int by)
  {
    var index = _to.SelectedIndex;
    var to = index + by;
    if (index < 0 || to < 0 || to >= _to.Items.Count) return;
    _filling = true;
    var item = _to.Items[index];
    var ticked = _to.GetItemChecked(index);
    _to.Items.RemoveAt(index);
    _to.Items.Insert(to, item);
    _to.SetItemChecked(to, ticked);
    _to.SelectedIndex = to;
    _filling = false;
    CaptureChoice();
  }

  /// <summary>Only the classes given something: one left with nothing ticked stays as it is.</summary>
  private Dictionary<string, IReadOnlyList<string>> Map() =>
    _map.Where(p => p.Value.Count > 0).ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value, StringComparer.OrdinalIgnoreCase);

  private void Preview()
  {
    var result = ClassMapping.Apply(_plan, Map(), _listClasses);
    _preview.Items.Clear();
    foreach (var s in result.Plan.Sessions)
      _preview.Items.Add($"{s.Time:hh\\:mm}   {s.Name}   -   {(s.TeamEvent ? "teams" : s.ClassesText)}");

    var notes = result.Warnings.ToList();
    notes.AddRange(_map.Where(p => p.Value.Count == 0).Select(p => $"{p.Key} is not matched yet: its sessions would count nobody."));
    _warnings.Text = string.Join("\n", notes.Select(n => "⚠ " + n));
  }

  private void Accept()
  {
    var result = ClassMapping.Apply(_plan, Map(), _listClasses);
    if (result.Dropped.Count > 0)
    {
      var answer = MessageBox.Show(this,
        "These sessions would be left with no class and are taken out of the plan:\n\n" +
        string.Join("\n", result.Dropped) + "\n\nGo ahead?",
        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
      if (answer != DialogResult.Yes) return;
    }

    Result = result;
    DialogResult = DialogResult.OK;
    Close();
  }
}
