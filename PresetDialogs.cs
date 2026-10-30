using System.Drawing.Printing;

namespace CrossMgrInterface;

/// <summary>
/// The results website as the preset windows need it: putting a code there
/// under its short code, and fetching one by its short code. Null where no
/// website is set up - short codes are then not offered.
/// </summary>
public interface IPresetWebsite
{
  /// <summary>Puts the code on the website. Returns the short code, or the reason it did not work.</summary>
  Task<(string? ShortCode, string? Error)> PublishAsync(string name, string code);

  /// <summary>The code behind a short code, or null when the website has none.</summary>
  Task<FoundPlanCode?> FetchAsync(string shortCode);
}

/// <summary>
/// One preset's code, to copy into a message or a list - and its short code,
/// for typing, once it is on the website.
/// </summary>
public sealed class PresetCodeDialog : Form
{
  private readonly string _code;
  private readonly StoredPreset? _stored;
  private readonly PresetStore? _store;
  private readonly IPresetWebsite? _website;
  private readonly Label _short = new();
  private readonly Button _getShort = new();
  private readonly Button _copyShort = new();

  public PresetCodeDialog(string name, DayPlan plan, string code, StoredPreset? stored = null, PresetStore? store = null,
    IPresetWebsite? website = null)
  {
    _code = code;
    _stored = stored;
    _store = store;
    _website = website;

    Text = "Plan code";
    FormBorderStyle = FormBorderStyle.FixedDialog;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = false;
    ShowInTaskbar = false;
    ClientSize = new Size(620, 400);
    Font = new Font("Segoe UI", 9.5F);

    Controls.Add(new Label { Text = name, Location = new Point(16, 14), AutoSize = true, Font = new Font("Segoe UI", 12F, FontStyle.Bold) });
    Controls.Add(new Label { Text = DayPlanCode.Summary(plan), Location = new Point(16, 42), AutoSize = true, ForeColor = Color.DimGray });
    Controls.Add(new Label
    {
      Location = new Point(16, 68),
      Size = new Size(588, 40),
      Text = "This code is the whole plan. Copy it into a message, an email or the club's list; Paste a code in " +
             "Plan the day > New... makes the plan from it again, on any laptop, without the internet."
    });

    var box = new TextBox
    {
      Location = new Point(16, 112),
      Size = new Size(588, 150),
      Multiline = true,
      ReadOnly = true,
      ScrollBars = ScrollBars.Vertical,
      Font = new Font("Consolas", 10F),
      Text = code
    };
    Controls.Add(box);

    var copy = new Button { Text = "Copy code", Location = new Point(16, 270), Size = new Size(120, 30) };
    copy.Click += (_, _) => Clipboard.SetText(_code);
    Controls.Add(copy);

    _short.Location = new Point(16, 316);
    _short.Size = new Size(400, 40);
    _getShort.Text = "Get short code";
    _getShort.Location = new Point(420, 312);
    _getShort.Size = new Size(184, 30);
    _getShort.Click += async (_, _) => await GetShortAsync();
    _copyShort.Text = "Copy short code";
    _copyShort.Location = new Point(420, 312);
    _copyShort.Size = new Size(184, 30);
    _copyShort.Click += (_, _) => Clipboard.SetText(_stored?.ShortCode ?? "");
    Controls.AddRange(new Control[] { _short, _getShort, _copyShort });

    var close = new Button { Text = "Close", Size = new Size(100, 30), Location = new Point(504, 360), DialogResult = DialogResult.Cancel };
    Controls.Add(close);
    CancelButton = close;
    ShowShort();
  }

  private void ShowShort()
  {
    if (_stored?.ShortCode is { } shortCode)
    {
      _short.Text = $"Short code: {shortCode}\nOn the results website, for typing in instead of the long code.";
      _short.Font = new Font(Font, FontStyle.Bold);
      _getShort.Visible = false;
      _copyShort.Visible = true;
      return;
    }

    _copyShort.Visible = false;
    if (_website == null || _stored == null)
    {
      _short.Text = _stored == null
        ? "Save it as a preset to get a short code for it."
        : "A short code needs the club's results website (Race > Results website...).";
      _short.ForeColor = Color.DimGray;
      _getShort.Visible = false;
      return;
    }

    _short.Text = "A short code puts the plan on the results website, so it can be typed in as eight characters.";
    _short.ForeColor = Color.DimGray;
    _getShort.Visible = true;
  }

  private async Task GetShortAsync()
  {
    if (_website == null || _stored == null || _store == null) return;
    _getShort.Enabled = false;
    _short.Text = "Putting it on the website...";
    var (shortCode, error) = await _website.PublishAsync(_stored.Name, _code);
    _getShort.Enabled = true;

    if (shortCode == null)
    {
      _short.ForeColor = Color.DarkRed;
      _short.Text = error ?? "The website did not take it.";
      return;
    }

    _stored.ShortCode = shortCode;
    _store.Save();
    _short.ForeColor = SystemColors.ControlText;
    ShowShort();
  }
}

/// <summary>
/// Codes pasted in, one or many - a message from the club's chat, an email,
/// a list exported from another laptop - checked as they are pasted, and
/// added to the club's presets.
/// </summary>
public sealed class AddCodesDialog : Form
{
  private readonly PresetStore _store;
  private readonly IPresetWebsite? _website;
  private readonly TextBox _paste = new();
  private readonly ListBox _found = new();
  private readonly Button _add = new();
  private List<FoundPlanCode> _codes = new();
  private int _version;

  public AddCodesDialog(PresetStore store, IPresetWebsite? website)
  {
    _store = store;
    _website = website;

    Text = "Add codes";
    FormBorderStyle = FormBorderStyle.FixedDialog;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = false;
    ShowInTaskbar = false;
    ClientSize = new Size(700, 520);
    Font = new Font("Segoe UI", 9.5F);

    Controls.Add(new Label
    {
      Location = new Point(16, 12),
      Size = new Size(668, 40),
      Text = "Paste one or more codes - a whole message or list is fine. Each line's other words become the " +
             "preset's name." + (website != null ? " Short codes (XXXX-XXXX) are looked up on the results website." : "")
    });
    _paste.Location = new Point(16, 56);
    _paste.Size = new Size(668, 200);
    _paste.Multiline = true;
    _paste.ScrollBars = ScrollBars.Vertical;
    _paste.Font = new Font("Consolas", 9F);
    _paste.TextChanged += async (_, _) => await ReadAsync();
    Controls.Add(_paste);

    Controls.Add(new Label { Text = "Found", Location = new Point(16, 266), AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
    _found.Location = new Point(16, 290);
    _found.Size = new Size(668, 170);
    _found.IntegralHeight = false;
    _found.SelectionMode = SelectionMode.None;
    Controls.Add(_found);

    _add.Text = "Add";
    _add.Size = new Size(100, 30);
    _add.Location = new Point(ClientSize.Width - 226, ClientSize.Height - 42);
    _add.Enabled = false;
    _add.Click += (_, _) => AddAll();
    var cancel = new Button
    {
      Text = "Cancel", Size = new Size(100, 30), Location = new Point(ClientSize.Width - 116, ClientSize.Height - 42),
      DialogResult = DialogResult.Cancel
    };
    Controls.AddRange(new Control[] { _add, cancel });
    CancelButton = cancel;
  }

  private async Task ReadAsync()
  {
    var version = ++_version;
    var text = _paste.Text;
    var codes = DayPlanCode.FindAll(text);

    var shortCodes = DayPlanCode.FindShortCodes(text);
    if (codes.Count == 0 && shortCodes.Count == 0 && DayPlanCode.AsShortCode(text) is { } single)
      shortCodes.Add((single, ""));

    foreach (var (shortCode, line) in shortCodes)
    {
      FoundPlanCode? found = null;
      if (_store.FindShort(shortCode) is { } own) found = DayPlanCode.Decode(own.Code) with { LineText = line };
      else if (_website != null)
      {
        found = await _website.FetchAsync(shortCode);
        if (version != _version) return;
      }
      codes.Add(found is { Plan: not null }
        ? found with { LineText = line.Length > 0 ? line : found.LineText }
        : new FoundPlanCode(null, _website == null
          ? $"{shortCode}: a short code is looked up on the results website, which is not set up here."
          : $"{shortCode}: no preset with this short code.", shortCode, line));
    }

    if (version != _version) return;
    _codes = codes;
    _found.Items.Clear();
    foreach (var code in codes)
    {
      var name = NameFor(code);
      _found.Items.Add(code.Plan == null
        ? $"✗ {(code.LineText.Length > 0 ? code.LineText + ": " : "")}{code.Error}"
        : _store.FindSame(code.Code) is { } existing
          ? $"= {name}: already in the list as {existing.Name}"
          : $"✓ {name}: {DayPlanCode.Summary(code.Plan)}");
    }
    _add.Enabled = codes.Any(c => c.Plan != null && _store.FindSame(c.Code) == null);
  }

  private static string NameFor(FoundPlanCode code) =>
    code.LineText.Length > 0 ? code.LineText : code.Plan?.Name is { Length: > 0 } name ? name : "Preset";

  private void AddAll()
  {
    var added = 0;
    foreach (var code in _codes.Where(c => c.Plan != null))
      if (_store.Add(NameFor(code), "", code.Code).Added) added++;

    try
    {
      _store.Save();
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(this, "The presets could not be saved.", "Check the disk is not full, then try again.", ex);
      return;
    }

    DialogResult = added > 0 ? DialogResult.OK : DialogResult.Cancel;
    Close();
  }
}

/// <summary>
/// Race > Presets...: the application's presets and the club's own, with
/// their codes - the list a club keeps of its race days. Codes are added by
/// pasting, and the list can be exported for email or printed to pin up.
/// </summary>
public sealed class PresetsDialog : Form
{
  private readonly PresetStore _store;
  private readonly IPresetWebsite? _website;
  private readonly DataGridView _grid = new();
  private readonly Button _show = new();
  private readonly Button _copy = new();
  private readonly Button _rename = new();
  private readonly Button _delete = new();
  private List<PlanPreset> _rows = new();

  private sealed record Printed(string Name, string Description, string Summary, string Code, string? ShortCode);
  private List<Printed> _printing = new();
  private int _printIndex;

  public PresetsDialog(PresetStore store, IPresetWebsite? website)
  {
    _store = store;
    _website = website;

    Text = "Presets";
    FormBorderStyle = FormBorderStyle.Sizable;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    ShowInTaskbar = false;
    ClientSize = new Size(960, 480);
    MinimumSize = new Size(760, 360);

    var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(12) };
    root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
    root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

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
    _grid.Font = new Font("Segoe UI", 10F);
    _grid.RowTemplate.Height = 28;
    _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
    foreach (var (header, width) in new[] { ("Name", 200), ("Sessions", 150), ("Short code", 100), ("Description", 320) })
    {
      var index = _grid.Columns.Add(header, header);
      _grid.Columns[index].Width = width;
      _grid.Columns[index].SortMode = DataGridViewColumnSortMode.NotSortable;
    }
    _grid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
    _grid.SelectionChanged += (_, _) => UpdateButtons();
    _grid.CellDoubleClick += (_, e) =>
    {
      if (e.RowIndex >= 0) ShowCode();
    };
    root.Controls.Add(_grid, 0, 0);

    var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12, 0, 0, 0) };
    Configure(_show, "Show code...", ShowCode);
    Configure(_copy, "Copy code", () =>
    {
      if (Selected is { } p) Clipboard.SetText(p.Code);
    });
    var add = new Button();
    Configure(add, "Add codes...", AddCodes);
    Configure(_rename, "Rename...", RenameSelected);
    Configure(_delete, "Delete...", DeleteSelected);
    var export = new Button();
    Configure(export, "Export list...", ExportList);
    var print = new Button();
    Configure(print, "Print list...", PrintList);
    var close = new Button { Text = "Close", Width = 160, Height = 34, Margin = new Padding(0, 24, 0, 0), DialogResult = DialogResult.Cancel };
    buttons.Controls.AddRange(new Control[] { _show, _copy, add, _rename, _delete, export, print, close });
    CancelButton = close;
    root.Controls.Add(buttons, 1, 0);

    root.Controls.Add(new Label
    {
      AutoSize = true,
      ForeColor = Color.DimGray,
      Padding = new Padding(0, 8, 0, 0),
      Text = "Plan the day > New... makes a plan from any of these. Save a plan as a preset from the plan window."
    }, 0, 1);

    Controls.Add(root);
    Load += (_, _) => Reload(null);
    return;

    static void Configure(Button button, string text, Action click)
    {
      button.Text = text;
      button.Width = 160;
      button.Height = 34;
      button.Margin = new Padding(0, 0, 0, 8);
      button.Click += (_, _) => click();
    }
  }

  private void Reload(string? select)
  {
    _rows = PresetCatalog.All.ToList();
    var stored = _store.Presets.ToList();
    _rows.AddRange(stored.Select(s => new PlanPreset(s.Name, s.Description,
      DayPlanCode.Decode(s.Code).Plan ?? new DayPlan { Name = s.Name }, BuiltIn: false)));

    _grid.Rows.Clear();
    for (var i = 0; i < _rows.Count; i++)
    {
      var preset = _rows[i];
      var own = preset.BuiltIn ? null : stored[i - PresetCatalog.All.Count];
      var readable = own == null || DayPlanCode.Decode(own.Code).Plan != null;
      var row = _grid.Rows[_grid.Rows.Add(
        preset.BuiltIn ? $"{preset.Name}  (built in)" : preset.Name,
        readable ? DayPlanCode.Summary(preset.Plan) : "cannot be read",
        own?.ShortCode ?? "",
        preset.Description)];
      if (preset.BuiltIn) row.DefaultCellStyle.ForeColor = Color.DimGray;
    }

    _grid.ClearSelection();
    var index = select == null ? -1 : _rows.FindIndex(r => r.Name == select && !r.BuiltIn);
    if (index >= 0) _grid.Rows[index].Selected = true;
    UpdateButtons();
  }

  private int SelectedIndex => _grid.SelectedRows.Count == 1 ? _grid.SelectedRows[0].Index : -1;
  private PlanPreset? Selected => SelectedIndex >= 0 && SelectedIndex < _rows.Count ? _rows[SelectedIndex] : null;
  private StoredPreset? SelectedStored =>
    Selected is { BuiltIn: false } ? _store.Presets[SelectedIndex - PresetCatalog.All.Count] : null;

  private void UpdateButtons()
  {
    _show.Enabled = _copy.Enabled = Selected != null;
    _rename.Enabled = _delete.Enabled = SelectedStored != null;
  }

  private void ShowCode()
  {
    if (Selected is not { } preset) return;
    var stored = SelectedStored;
    var code = stored?.Code ?? preset.Code;
    using var dialog = new PresetCodeDialog(preset.Name, preset.Plan, code, stored, _store, _website);
    dialog.ShowDialog(this);
    Reload(preset.Name);
  }

  private void AddCodes()
  {
    using var dialog = new AddCodesDialog(_store, _website);
    if (dialog.ShowDialog(this) == DialogResult.OK) Reload(null);
  }

  private void RenameSelected()
  {
    if (SelectedStored is not { } stored) return;
    var name = TextPrompt.Ask(this, "Preset name", stored.Name, "What the club calls this race day.");
    if (name == null) return;
    var description = TextPrompt.Ask(this, "Description", stored.Description, "A line to say what it is for. May be empty.");
    stored.Name = name.Trim().Length > 0 ? name.Trim() : stored.Name;
    if (description != null) stored.Description = description.Trim();
    Save();
    Reload(stored.Name);
  }

  private void DeleteSelected()
  {
    if (SelectedStored is not { } stored) return;
    var answer = MessageBox.Show(this,
      $"Delete the preset '{stored.Name}'?\n\nPlans made from it are not affected. Its code still works wherever it was sent.",
      Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
    if (answer != DialogResult.Yes) return;
    _store.Remove(stored);
    Save();
    Reload(null);
  }

  private void Save()
  {
    try
    {
      _store.Save();
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(this, "The presets could not be saved.", "Check the disk is not full, then try again.", ex);
    }
  }

  private IEnumerable<(string Name, string Description, string Code, string? ShortCode)> OwnList() =>
    _store.Presets.Select(p => (p.Name, p.Description, p.Code, p.ShortCode));

  private void ExportList()
  {
    if (_store.Presets.Count == 0)
    {
      MessageBox.Show(this, "The club has no presets of its own yet. Save a plan as a preset first.", Text,
        MessageBoxButtons.OK, MessageBoxIcon.Information);
      return;
    }

    using var save = new SaveFileDialog
    {
      Title = "Export the presets",
      Filter = "Text (*.txt)|*.txt|Excel / CSV (*.csv)|*.csv",
      FileName = $"Presets {DateTime.Today:yyyy-MM-dd}"
    };
    if (save.ShowDialog(this) != DialogResult.OK) return;

    var csv = Path.GetExtension(save.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase);
    File.WriteAllText(save.FileName, csv ? PresetStore.ExportCsv(OwnList()) : PresetStore.ExportText(OwnList()),
      new System.Text.UTF8Encoding(true));
  }

  private void PrintList()
  {
    _printing = _store.Presets
      .Select(p => (p, plan: DayPlanCode.Decode(p.Code).Plan))
      .Select(x => new Printed(x.p.Name, x.p.Description, x.plan == null ? "" : DayPlanCode.Summary(x.plan), x.p.Code, x.p.ShortCode))
      .ToList();
    if (_printing.Count == 0)
    {
      MessageBox.Show(this, "The club has no presets of its own yet. Save a plan as a preset first.", Text,
        MessageBoxButtons.OK, MessageBoxIcon.Information);
      return;
    }

    _printIndex = 0;
    using var document = new PrintDocument();
    document.DocumentName = "Presets";
    document.PrintPage += PrintPage;
    using var dialog = new PrintPreviewDialog { Document = document, WindowState = FormWindowState.Maximized };
    dialog.ShowDialog(this);
  }

  private void PrintPage(object? sender, PrintPageEventArgs e)
  {
    var g = e.Graphics!;
    var area = e.MarginBounds;
    using var title = new Font("Segoe UI", 14F, FontStyle.Bold);
    using var nameFont = new Font("Segoe UI", 11F, FontStyle.Bold);
    using var text = new Font("Segoe UI", 9.5F);
    using var mono = new Font("Consolas", 9.5F);
    var y = (float)area.Top;

    if (_printIndex == 0)
    {
      g.DrawString($"Race day presets - {DateTime.Today:dd.MM.yyyy}", title, Brushes.Black, area.Left, y);
      y += title.GetHeight(g) + 12;
    }

    while (_printIndex < _printing.Count)
    {
      var p = _printing[_printIndex];
      var heading = p.ShortCode != null ? $"{p.Name}     {p.ShortCode}" : p.Name;
      var details = string.Join("  ·  ", new[] { p.Summary, p.Description }.Where(s => s.Length > 0));
      var codeSize = g.MeasureString(p.Code, mono, area.Width);
      var detailsSize = g.MeasureString(details, text, area.Width);
      var height = nameFont.GetHeight(g) + detailsSize.Height + codeSize.Height + 22;

      if (y + height > area.Bottom && y > area.Top + 40)
      {
        e.HasMorePages = true;
        return;
      }

      g.DrawString(heading, nameFont, Brushes.Black, area.Left, y);
      y += nameFont.GetHeight(g) + 2;
      if (details.Length > 0)
      {
        g.DrawString(details, text, Brushes.DimGray, new RectangleF(area.Left, y, area.Width, detailsSize.Height));
        y += detailsSize.Height + 2;
      }
      g.DrawString(p.Code, mono, Brushes.Black, new RectangleF(area.Left, y, area.Width, codeSize.Height + 2));
      y += codeSize.Height + 18;
      _printIndex++;
    }

    e.HasMorePages = false;
    _printIndex = 0;
  }
}
