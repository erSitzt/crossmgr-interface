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
  private readonly RadioButton _quietFromLapTimes = new();
  private readonly RadioButton _quietFixed = new();
  private readonly NumericUpDown _quietSeconds = new();

  public int Port => (int)_port.Value;
  public bool VerboseLogging => _verbose.Checked;
  public bool QuietFromLapTimes => _quietFromLapTimes.Checked;
  public int QuietSeconds => (int)_quietSeconds.Value;

  public ReaderSettingsDialog(int currentPort, bool readerRunning, bool quietFromLapTimes, int quietSeconds)
  {
    Text = "Reader connection";
    FormBorderStyle = FormBorderStyle.FixedDialog;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = false;
    ClientSize = new Size(440, 400);

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

    var quietHeading = new Label
    {
      Text = "Warn that nothing is being read",
      Location = new Point(16, 190),
      AutoSize = true,
      Font = new Font(Font, FontStyle.Bold)
    };

    _quietFromLapTimes.Text = "When riders due at the line have not come (from their lap times)";
    _quietFromLapTimes.Location = new Point(16, 214);
    _quietFromLapTimes.AutoSize = true;
    _quietFromLapTimes.Checked = quietFromLapTimes;

    _quietFixed.Text = "After this long without a read:";
    _quietFixed.Location = new Point(16, 242);
    _quietFixed.AutoSize = true;
    _quietFixed.Checked = !quietFromLapTimes;

    _quietSeconds.Location = new Point(236, 240);
    _quietSeconds.Width = 70;
    _quietSeconds.Minimum = 15;
    _quietSeconds.Maximum = 1800;
    _quietSeconds.Value = Math.Clamp(quietSeconds, 15, 1800);

    var secondsLabel = new Label { Text = "seconds", Location = new Point(312, 244), AutoSize = true };

    var quietHint = new Label
    {
      Text = "Either way it stays quiet while nobody is still expected at the line - after the flag, for " +
             "example, while the finish waits for a rider who has retired. Until the riders have lap times, " +
             "the number of seconds is used.",
      Location = new Point(16, 272),
      Size = new Size(408, 58),
      ForeColor = Color.DimGray
    };

    Controls.AddRange(new Control[]
    {
      portLabel, _port, hint, _verbose, restartNote,
      quietHeading, _quietFromLapTimes, _quietFixed, _quietSeconds, secondsLabel, quietHint,
      ok, cancel
    });
    AcceptButton = ok;
    CancelButton = cancel;
  }
}
