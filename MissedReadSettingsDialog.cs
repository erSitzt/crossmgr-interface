namespace CrossMgrInterface;

/// <summary>
/// Tunes how hard the app looks for a missed transponder read.
///
/// Every field shows what the value originally was, so it is always clear which
/// of them have been moved away from what the app shipped with - and what to put
/// back if a change turns out to be wrong.
///
/// Hand-built like the other dialogs here, so the designer never rewrites it.
/// </summary>
public sealed class MissedReadSettingsDialog : Form
{
  private readonly NumericUpDown _minRatio = new();
  private readonly NumericUpDown _maxRatio = new();
  private readonly NumericUpDown _minPriorLaps = new();
  private readonly NumericUpDown _paceWindow = new();

  public LapAnomalySettings Result { get; private set; } = LapAnomalySettings.Default;

  public MissedReadSettingsDialog(LapAnomalySettings current)
  {
    Text = "Missed read detection";
    FormBorderStyle = FormBorderStyle.FixedDialog;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = false;
    ClientSize = new Size(660, 470);

    var intro = new Label
    {
      Text = "A missed read shows up as a lap that is a near multiple of the rider's " +
             "own pace. These decide how confident the app has to be before it says so.",
      Location = new Point(16, 14),
      Size = new Size(620, 40),
      ForeColor = Color.DimGray
    };

    var y = 66;
    Row(_minRatio, "A lap counts as long at", "x the rider's recent pace",
      (decimal)current.MinRatio, 1.1m, 5.0m, 0.1m, 1,
      LapAnomalySettings.Original.MinRatio.ToString("0.0"),
      "Lower finds more missed reads, and starts mistaking a crash for one.");

    Row(_maxRatio, "Stop looking above", "x pace",
      (decimal)current.MaxRatio, 1.6m, 20.0m, 0.5m, 1,
      LapAnomalySettings.Original.MaxRatio.ToString("0.0"),
      "Longer than this is a rider who stopped, not a read that went missing.");

    Row(_minPriorLaps, "Laps needed before judging", "of the rider's own laps",
      current.MinPriorLaps, 1, 10, 1, 0,
      LapAnomalySettings.Original.MinPriorLaps.ToString(),
      "The out-lap does not count. At 2 a read missed on a rider's third " +
      "crossing could never be found, and hid the next one too.");

    Row(_paceWindow, "Average pace over the last", "laps",
      current.PaceWindow, 1, 20, 1, 0,
      LapAnomalySettings.Original.PaceWindow.ToString(),
      "Laps already flagged as long are left out, so one miss cannot hide another.");

    var restore = new Button
    {
      Text = "Restore original values",
      Location = new Point(16, y + 6),
      Size = new Size(190, 34)
    };
    restore.Click += (_, _) => Load(LapAnomalySettings.Original);

    var defaults = new Button
    {
      Text = "Restore defaults",
      Location = new Point(214, y + 6),
      Size = new Size(150, 34)
    };
    defaults.Click += (_, _) => Load(LapAnomalySettings.Default);

    var ok = new Button
    {
      Text = "OK",
      DialogResult = DialogResult.OK,
      Location = new Point(470, y + 6),
      Size = new Size(80, 34)
    };
    ok.Click += (_, _) =>
    {
      Result = new LapAnomalySettings
      {
        MinRatio = (double)_minRatio.Value,
        MaxRatio = (double)_maxRatio.Value,
        MinPriorLaps = (int)_minPriorLaps.Value,
        PaceWindow = (int)_paceWindow.Value
      }.Validated();
    };

    var cancel = new Button
    {
      Text = "Cancel",
      DialogResult = DialogResult.Cancel,
      Location = new Point(558, y + 6),
      Size = new Size(80, 34)
    };

    AcceptButton = ok;
    CancelButton = cancel;
    Controls.AddRange(new Control[] { intro, restore, defaults, ok, cancel });

    void Row(NumericUpDown box, string caption, string unit, decimal value,
      decimal min, decimal max, decimal step, int decimals, string original, string hint)
    {
      Controls.Add(new Label
      {
        Text = caption,
        Location = new Point(16, y + 4),
        Size = new Size(210, 24)
      });

      box.Location = new Point(232, y);
      box.Size = new Size(80, 30);
      box.Minimum = min;
      box.Maximum = max;
      box.Increment = step;
      box.DecimalPlaces = decimals;
      box.Value = Math.Clamp(value, min, max);
      Controls.Add(box);

      Controls.Add(new Label
      {
        Text = unit,
        Location = new Point(320, y + 4),
        Size = new Size(170, 24)
      });

      Controls.Add(new Label
      {
        Text = $"originally {original}",
        Location = new Point(492, y + 4),
        Size = new Size(150, 24),
        ForeColor = Color.FromArgb(150, 90, 0)
      });

      Controls.Add(new Label
      {
        Text = hint,
        Location = new Point(32, y + 32),
        Size = new Size(610, 34),
        ForeColor = Color.DimGray
      });

      y += 76;
    }

    void Load(LapAnomalySettings settings)
    {
      _minRatio.Value = Math.Clamp((decimal)settings.MinRatio, _minRatio.Minimum, _minRatio.Maximum);
      _maxRatio.Value = Math.Clamp((decimal)settings.MaxRatio, _maxRatio.Minimum, _maxRatio.Maximum);
      _minPriorLaps.Value = Math.Clamp(settings.MinPriorLaps, _minPriorLaps.Minimum, _minPriorLaps.Maximum);
      _paceWindow.Value = Math.Clamp(settings.PaceWindow, _paceWindow.Minimum, _paceWindow.Maximum);
    }
  }
}
