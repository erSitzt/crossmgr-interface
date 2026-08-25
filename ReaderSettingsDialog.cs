namespace CrossMgrInterface;

/// <summary>
/// The reader connection settings, moved off the main window.
///
/// The port used to be a textbox that was the first control on the form. It is
/// set once when the timing kit is configured and never touched again on race
/// day, so it belongs behind a menu.
/// </summary>
public sealed class ReaderSettingsDialog : Form
{
  private readonly NumericUpDown _port = new();
  private readonly CheckBox _verbose = new();

  public int Port => (int)_port.Value;
  public bool VerboseLogging => _verbose.Checked;

  public ReaderSettingsDialog(int currentPort, bool readerRunning)
  {
    Text = "Reader connection";
    FormBorderStyle = FormBorderStyle.FixedDialog;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = false;
    ClientSize = new Size(440, 220);

    var portLabel = new Label
    {
      Text = "The reader connects to this computer on port:",
      Location = new Point(16, 20),
      AutoSize = true
    };

    _port.Location = new Point(16, 46);
    _port.Width = 110;
    _port.Minimum = 1;
    _port.Maximum = 65535;
    _port.Value = Math.Clamp(currentPort, 1, 65535);

    var hint = new Label
    {
      Text = "53135 is the standard CrossMgr port. Only change it if something " +
             "else on this computer is already using it.",
      Location = new Point(16, 78),
      Size = new Size(408, 40),
      ForeColor = Color.DimGray
    };

    _verbose.Text = "Log raw reader traffic (slow - for diagnosing problems)";
    _verbose.Location = new Point(16, 126);
    _verbose.AutoSize = true;

    var restartNote = new Label
    {
      Text = readerRunning
        ? "The reader is connected. Stop and start it for a new port to take effect."
        : "",
      Location = new Point(16, 152),
      Size = new Size(408, 20),
      ForeColor = Color.DarkOrange
    };

    var ok = new Button
    {
      Text = "OK",
      DialogResult = DialogResult.OK,
      Location = new Point(ClientSize.Width - 200, ClientSize.Height - 44),
      Size = new Size(88, 30)
    };
    var cancel = new Button
    {
      Text = "Cancel",
      DialogResult = DialogResult.Cancel,
      Location = new Point(ClientSize.Width - 104, ClientSize.Height - 44),
      Size = new Size(88, 30)
    };

    Controls.AddRange(new Control[] { portLabel, _port, hint, _verbose, restartNote, ok, cancel });
    AcceptButton = ok;
    CancelButton = cancel;
  }
}
