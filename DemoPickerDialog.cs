namespace CrossMgrInterface;

/// <summary>
/// The list of demos. Choosing one says what it shows and how long it takes;
/// Start demo opens it in a window of its own.
/// </summary>
public sealed class DemoPickerDialog : Form
{
  private readonly ListBox _list;
  private readonly FlowLayoutPanel _details;

  /// <summary>The demo to start, once the dialog has closed with OK.</summary>
  public DemoScenario? Chosen => _list.SelectedItem as DemoScenario;

  public DemoPickerDialog(IReadOnlyList<DemoScenario> scenarios)
  {
    Text = "Try a demo race";
    FormBorderStyle = FormBorderStyle.FixedDialog;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = false;
    ShowInTaskbar = false;
    Font = new Font("Segoe UI", 9.75F);
    ClientSize = new Size(700, 430);

    var caption = new Label
    {
      Text = "Watch the application time a whole session with simulated riders - no reader and no rider list " +
             "needed. The demo opens in a window of its own and runs in real time. Nothing in it touches your " +
             "real races, rider lists or reader settings.",
      Location = new Point(14, 12),

      // Three lines of text. At 44 the last one was cut in half.
      Size = new Size(672, 54)
    };

    _list = new ListBox
    {
      Location = new Point(14, 70),
      Size = new Size(210, 296),
      IntegralHeight = false,
      Font = new Font("Segoe UI", 11F),
      ItemHeight = 26,
      DisplayMember = nameof(DemoScenario.Title)
    };
    _list.Items.AddRange(scenarios.Cast<object>().ToArray());

    _details = new FlowLayoutPanel
    {
      Location = new Point(238, 70),
      Size = new Size(448, 296),
      FlowDirection = FlowDirection.TopDown,
      WrapContents = false,
      AutoScroll = true
    };

    var start = new Button { Text = "Start demo", DialogResult = DialogResult.OK, Location = new Point(478, 384), Size = new Size(100, 30) };
    var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(586, 384), Size = new Size(100, 30) };

    _list.SelectedIndexChanged += (_, _) =>
    {
      start.Enabled = Chosen != null;
      ShowDetails(Chosen);
    };
    _list.DoubleClick += (_, _) =>
    {
      if (Chosen == null) return;
      DialogResult = DialogResult.OK;
    };

    Controls.AddRange(new Control[] { caption, _list, _details, start, cancel });
    AcceptButton = start;
    CancelButton = cancel;

    if (_list.Items.Count > 0) _list.SelectedIndex = 0;
  }

  private void ShowDetails(DemoScenario? scenario)
  {
    _details.SuspendLayout();
    _details.Controls.Clear();

    if (scenario != null)
    {
      const int width = 420;
      _details.Controls.Add(new Label
      {
        Text = scenario.Title, AutoSize = true, MaximumSize = new Size(width, 0),
        Font = new Font("Segoe UI", 13F, FontStyle.Bold)
      });
      _details.Controls.Add(new Label
      {
        Text = scenario.Length, AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 2, 3, 8)
      });
      _details.Controls.Add(new Label { Text = scenario.Summary, AutoSize = true, MaximumSize = new Size(width, 0) });

      foreach (var line in scenario.WhatHappens)
      {
        _details.Controls.Add(new Label
        {
          Text = "•  " + line, AutoSize = true, MaximumSize = new Size(width - 12, 0), Margin = new Padding(15, 6, 3, 0)
        });
      }
    }

    _details.ResumeLayout();
  }
}
