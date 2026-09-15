namespace CrossMgrInterface;

/// <summary>
/// The problems demo's checklist: every problem planted in the race, in the order
/// they happen, with what to do about each, ticked off once it has been put right.
///
/// A window beside the race rather than in front of it, and never modal: the
/// operator fixes things in the main window while this one keeps score. Closing
/// it only hides it; the DEMO bar opens it again with nothing forgotten.
/// </summary>
public sealed class DemoChecklistDialog : Form
{
  private const int TextWidth = 380;

  private readonly DemoChecklistTracker _checklist;
  private readonly Label _progress = new();
  private readonly List<Row> _rows = new();
  private readonly Font _boldFont;
  private readonly Font _markFont;

  private sealed record Row(DemoProblem Problem, Label Mark, Label Title, Label HowTo);

  public DemoChecklistDialog(DemoChecklistTracker checklist)
  {
    _checklist = checklist;

    Text = "Problems to fix";
    FormBorderStyle = FormBorderStyle.SizableToolWindow;
    ShowInTaskbar = false;
    MinimizeBox = false;
    MaximizeBox = false;
    Font = new Font("Segoe UI", 9.5F);
    ClientSize = new Size(TextWidth + 76, 660);
    MinimumSize = new Size(TextWidth + 92, 320);

    _boldFont = new Font(Font, FontStyle.Bold);
    _markFont = new Font("Segoe UI", 13F, FontStyle.Bold);

    var intro = new Label
    {
      Text = "Each problem appears here as it happens in the race, with what to do about it, and is ticked off " +
             "once it has been put right.",
      AutoSize = true,
      MaximumSize = new Size(TextWidth + 36, 0),
      Margin = new Padding(0, 0, 0, 6)
    };

    _progress.AutoSize = true;
    _progress.Font = _boldFont;
    _progress.Margin = new Padding(0, 0, 0, 10);

    var list = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill,
      FlowDirection = FlowDirection.TopDown,
      WrapContents = false,
      AutoScroll = true
    };

    foreach (var problem in checklist.Problems)
    {
      var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 0, 0, 12) };
      row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
      row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

      var mark = new Label { AutoSize = false, Size = new Size(28, 28), Font = _markFont, TextAlign = ContentAlignment.TopCenter };
      var title = new Label { AutoSize = true, MaximumSize = new Size(TextWidth, 0), Font = _boldFont, Text = problem.Title };
      var howTo = new Label { AutoSize = true, MaximumSize = new Size(TextWidth, 0), ForeColor = Color.DimGray };

      row.Controls.Add(mark, 0, 0);
      row.SetRowSpan(mark, 2);
      row.Controls.Add(title, 1, 0);
      row.Controls.Add(howTo, 1, 1);

      list.Controls.Add(row);
      _rows.Add(new Row(problem, mark, title, howTo));
    }

    var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
    root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    root.Controls.Add(intro, 0, 0);
    root.Controls.Add(_progress, 0, 1);
    root.Controls.Add(list, 0, 2);
    Controls.Add(root);

    Render();
  }

  /// <summary>Shows the checklist's latest look at the race.</summary>
  public void Render()
  {
    var race = _checklist.Race;

    foreach (var row in _rows)
    {
      var problem = row.Problem;
      var state = _checklist.StateOf(problem);

      var (mark, colour) = state switch
      {
        DemoProblemState.Done => ("✓", Color.FromArgb(0, 130, 60)),
        DemoProblemState.ToFix when problem.JustWatch => ("●", Color.SteelBlue),
        DemoProblemState.ToFix => ("!", Color.DarkOrange),
        DemoProblemState.Missed => ("–", Color.Gray),
        _ => ("·", Color.Silver)
      };

      SetText(row.Mark, mark);
      row.Mark.ForeColor = colour;
      row.Title.ForeColor = state == DemoProblemState.Later ? Color.Silver : SystemColors.ControlText;

      var howTo = state switch
      {
        DemoProblemState.Later => "Later in the race.",
        DemoProblemState.Missed => "Too late for this one now - it is already past.",
        _ => problem.HowTo
      };

      if (state == DemoProblemState.ToFix && problem.Kind == DemoProblemKind.SplitAfterOutage && race != null)
      {
        var left = DemoChecklist.RidersLeft(problem, race);
        howTo += left == 1 ? " 1 rider left." : $" {left} riders left.";
      }

      SetText(row.HowTo, howTo);
    }

    SetText(_progress, $"{_checklist.Fixed} of {_checklist.Fixable} put right" +
                       (_checklist.LeftToFix > 0 ? $"  -  {_checklist.LeftToFix} waiting" : ""));
  }

  /// <summary>Only when it differs: a label relays itself out on every set, and this runs every second.</summary>
  private static void SetText(Label label, string text)
  {
    if (label.Text != text) label.Text = text;
  }

  /// <summary>Hides rather than closes, so the DEMO bar can open it again as it was.</summary>
  protected override void OnFormClosing(FormClosingEventArgs e)
  {
    if (e.CloseReason == CloseReason.UserClosing)
    {
      e.Cancel = true;
      Hide();
      return;
    }

    base.OnFormClosing(e);
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _boldFont.Dispose();
      _markFont.Dispose();
    }

    base.Dispose(disposing);
  }
}
