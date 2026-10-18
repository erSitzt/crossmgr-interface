namespace CrossMgrInterface;

/// <summary>
/// The switches for what happens by itself around a session - for every
/// session set up by hand (Race > After each session...), or for one day
/// plan's sessions (its own copy, from the plan window).
///
/// Hand-built like the other dialogs, so the designer never rewrites it.
/// </summary>
public sealed class AfterSessionDialog : Form
{
  private readonly AfterSession _after;
  private readonly CheckBox _publish = new();
  private readonly CheckBox _save = new();
  private readonly TextBox _folder = new();
  private readonly Button _choose = new();
  private readonly CheckBox _pdf = new();
  private readonly CheckBox _live = new();

  /// <param name="forPlan">Says which sessions the switches are for: today's plan's, or every one set up by hand.</param>
  public AfterSessionDialog(AfterSession after, bool publishingConfigured, bool liveConfigured, string? forPlan)
  {
    _after = after;

    Text = "After each session";
    FormBorderStyle = FormBorderStyle.FixedDialog;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = false;
    ShowInTaskbar = false;
    ClientSize = new Size(560, 400);
    Font = new Font("Segoe UI", 9.5F);

    var flow = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill,
      FlowDirection = FlowDirection.TopDown,
      WrapContents = false,
      Padding = new Padding(16),
      AutoScroll = true
    };

    flow.Controls.Add(Note(forPlan != null
      ? $"For the sessions of the plan '{forPlan}'. Sessions set up by hand follow Race > After each session...."
      : "For every session set up by hand. A day plan has switches of its own, in Race > Plan the day...."));

    _publish.Text = "Publish the results to the website when a session finishes";
    _publish.Checked = after.Publish;
    _publish.Enabled = publishingConfigured;
    flow.Controls.Add(Box(_publish));
    flow.Controls.Add(Note(publishingConfigured
      ? "Two minutes after the finish, and again after a correction. Without internet it waits and goes up " +
        "when the connection is back. An overall goes up once its last moto has. Riders' names, numbers, " +
        "classes, teams and lap times are sent; transponder IDs are not."
      : "Set up the results website first: Race > Results website...."));

    _save.Text = "Save the sheets into a folder for the day";
    _save.Checked = after.SaveSheets;
    _save.CheckedChanged += (_, _) => ApplyEnabled();
    flow.Controls.Add(Box(_save));

    var folderRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(20, 0, 0, 0) };
    _folder.Width = 360;
    _folder.Text = after.Folder ?? SheetSaver.DefaultRoot;
    _choose.Text = "Choose...";
    _choose.AutoSize = true;
    _choose.Click += (_, _) => ChooseFolder();
    folderRow.Controls.Add(_folder);
    folderRow.Controls.Add(_choose);
    flow.Controls.Add(folderRow);

    _pdf.Text = "Also as PDF";
    _pdf.Checked = after.Pdf;
    _pdf.Margin = new Padding(20, 4, 0, 0);
    _pdf.AutoSize = true;
    flow.Controls.Add(_pdf);
    flow.Controls.Add(Note("One folder per day, and in it one per session, numbered in the order they ran: the " +
                           "results sheet and one per class, as Excel" +
                           (SheetSaver.PdfPrinterInstalled() ? " and PDF." : ". (PDF needs Microsoft Print to PDF, which is not installed.)") +
                           " Saved again after a correction."));

    _live.Text = "Switch live timing on when a session's clock starts";
    _live.Checked = after.LiveTiming;
    _live.Enabled = liveConfigured;
    flow.Controls.Add(Box(_live));
    flow.Controls.Add(Note(liveConfigured
      ? "It switches itself off at the finish, as always. Switched off by hand, it stays off for that session."
      : "Set up live timing first: Race > Results website...."));

    var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 46, Padding = new Padding(10, 6, 10, 6) };
    var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(90, 30) };
    var ok = new Button { Text = "OK", Size = new Size(90, 30) };
    ok.Click += (_, _) => Accept();
    buttons.Controls.Add(cancel);
    buttons.Controls.Add(ok);
    AcceptButton = ok;
    CancelButton = cancel;

    Controls.Add(flow);
    Controls.Add(buttons);
    ApplyEnabled();
    return;

    static CheckBox Box(CheckBox box)
    {
      box.AutoSize = true;
      box.Font = new Font(box.Font, FontStyle.Bold);
      box.Margin = new Padding(0, 10, 0, 2);
      return box;
    }

    static Label Note(string text) => new()
    {
      Text = text,
      AutoSize = true,
      MaximumSize = new Size(510, 0),
      ForeColor = Color.DimGray,
      Margin = new Padding(20, 0, 0, 4)
    };
  }

  private void ApplyEnabled()
  {
    _folder.Enabled = _save.Checked;
    _choose.Enabled = _save.Checked;
    _pdf.Enabled = _save.Checked;
  }

  private void ChooseFolder()
  {
    using var dialog = new FolderBrowserDialog
    {
      Description = "Where the day folders of saved sheets go",
      UseDescriptionForTitle = true,
      SelectedPath = Directory.Exists(_folder.Text) ? _folder.Text : SheetSaver.DefaultRoot
    };
    if (dialog.ShowDialog(this) == DialogResult.OK) _folder.Text = dialog.SelectedPath;
  }

  private void Accept()
  {
    _after.Publish = _publish.Enabled ? _publish.Checked : _after.Publish;
    _after.SaveSheets = _save.Checked;
    var folder = _folder.Text.Trim();
    _after.Folder = folder.Length == 0 || string.Equals(folder, SheetSaver.DefaultRoot, StringComparison.OrdinalIgnoreCase)
      ? null
      : folder;
    _after.Pdf = _pdf.Checked;
    _after.LiveTiming = _live.Enabled ? _live.Checked : _after.LiveTiming;

    DialogResult = DialogResult.OK;
    Close();
  }
}
