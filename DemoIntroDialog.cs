namespace CrossMgrInterface;

/// <summary>
/// What a demo is about to show and what to try while it runs. Shown before the
/// demo's reader starts, and again from What's happening? on the DEMO bar.
/// </summary>
public sealed class DemoIntroDialog : Form
{
  private const int TextWidth = 540;

  /// <param name="canStart">
  /// Start demo and Close demo, before the demo has started; otherwise only a
  /// Close button, since the demo is already running.
  /// </param>
  public DemoIntroDialog(DemoScenario scenario, bool canStart)
  {
    Text = $"Demo: {scenario.Title}";
    FormBorderStyle = FormBorderStyle.FixedDialog;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = false;
    ShowInTaskbar = false;
    AutoSize = true;
    AutoSizeMode = AutoSizeMode.GrowAndShrink;
    Padding = new Padding(18, 14, 18, 14);
    Font = new Font("Segoe UI", 9.75F);

    var page = new FlowLayoutPanel
    {
      FlowDirection = FlowDirection.TopDown,
      WrapContents = false,
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink
    };

    page.Controls.Add(Words(scenario.Title, new Font("Segoe UI", 15F, FontStyle.Bold)));
    page.Controls.Add(Words(scenario.Length, Font, Color.DimGray));
    page.Controls.Add(Words(scenario.Summary, Font, margin: new Padding(0, 8, 0, 0)));

    page.Controls.Add(Heading("What happens"));
    foreach (var line in scenario.WhatHappens) page.Controls.Add(Bullet(line));

    page.Controls.Add(Heading("Things to try"));
    foreach (var line in scenario.WhatToTry) page.Controls.Add(Bullet(line));

    var buttons = new FlowLayoutPanel
    {
      FlowDirection = FlowDirection.RightToLeft,
      Size = new Size(TextWidth, 40),
      Margin = new Padding(0, 16, 0, 0)
    };

    if (canStart)
    {
      var start = new Button { Text = "Start demo", DialogResult = DialogResult.OK, Size = new Size(110, 30) };
      var close = new Button { Text = "Close demo", DialogResult = DialogResult.Cancel, Size = new Size(110, 30) };
      buttons.Controls.AddRange(new Control[] { start, close });
      AcceptButton = start;
      CancelButton = close;
    }
    else
    {
      var close = new Button { Text = "Close", DialogResult = DialogResult.OK, Size = new Size(110, 30) };
      buttons.Controls.Add(close);
      AcceptButton = close;
      CancelButton = close;
    }

    page.Controls.Add(buttons);
    Controls.Add(page);
  }

  private static Label Words(string text, Font font, Color? color = null, Padding? margin = null) => new()
  {
    Text = text,
    Font = font,
    ForeColor = color ?? SystemColors.ControlText,
    AutoSize = true,
    MaximumSize = new Size(TextWidth, 0),
    Margin = margin ?? new Padding(0, 2, 0, 0)
  };

  private Label Heading(string text) =>
    Words(text, new Font("Segoe UI", 10.5F, FontStyle.Bold), margin: new Padding(0, 14, 0, 2));

  private Label Bullet(string text) => new()
  {
    Text = "•  " + text,
    Font = Font,
    AutoSize = true,
    MaximumSize = new Size(TextWidth - 12, 0),
    Margin = new Padding(12, 3, 0, 0)
  };
}
