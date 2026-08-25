using System.Text;

namespace CrossMgrInterface;

/// <summary>
/// Reports a problem in words a race-day volunteer can act on, with the
/// technical detail tucked behind an expander rather than thrown at them.
///
/// Replaces MessageBox.Show($"...{ex.Message}") - a .NET exception message is
/// not a useful instruction to someone standing at a start gate.
/// </summary>
public sealed class ErrorDialog : Form
{
  private readonly TextBox _details;
  private readonly LinkLabel _toggle;
  private readonly Button _copy;
  private bool _expanded;

  private const int CollapsedHeight = 210;
  private const int ExpandedHeight = 430;

  private ErrorDialog(string headline, string body, string? technicalDetail)
  {
    Text = "CrossMgr";
    FormBorderStyle = FormBorderStyle.FixedDialog;
    StartPosition = FormStartPosition.CenterParent;
    MaximizeBox = false;
    MinimizeBox = false;
    ShowInTaskbar = false;
    ClientSize = new Size(520, CollapsedHeight);

    var icon = new PictureBox
    {
      Image = SystemIcons.Warning.ToBitmap(),
      SizeMode = PictureBoxSizeMode.AutoSize,
      Location = new Point(18, 20)
    };

    var headlineLabel = new Label
    {
      Text = headline,
      Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
      Location = new Point(70, 18),
      Size = new Size(430, 48),
      AutoSize = false
    };

    var bodyLabel = new Label
    {
      Text = body,
      Location = new Point(70, 68),
      Size = new Size(430, 62),
      AutoSize = false
    };

    _toggle = new LinkLabel
    {
      Text = "Show technical details",
      Location = new Point(70, 136),
      AutoSize = true,
      Visible = technicalDetail != null
    };
    _toggle.LinkClicked += (_, _) => SetExpanded(!_expanded);

    _details = new TextBox
    {
      Text = technicalDetail ?? "",
      Multiline = true,
      ReadOnly = true,
      ScrollBars = ScrollBars.Vertical,
      WordWrap = false,
      Location = new Point(18, 162),
      Size = new Size(484, 190),
      Visible = false,
      Font = new Font(FontFamily.GenericMonospace, 8.5F)
    };

    _copy = new Button
    {
      Text = "Copy details",
      Size = new Size(110, 30),
      Location = new Point(18, 362),
      Visible = false
    };
    _copy.Click += (_, _) =>
    {
      try { Clipboard.SetText(_details.Text); } catch (Exception) { /* clipboard may be locked */ }
    };

    var ok = new Button
    {
      Text = "OK",
      DialogResult = DialogResult.OK,
      Size = new Size(90, 30)
    };

    Controls.AddRange(new Control[] { icon, headlineLabel, bodyLabel, _toggle, _details, _copy, ok });
    AcceptButton = ok;
    CancelButton = ok;

    // Keep OK pinned to the bottom-right through the expand/collapse.
    void PositionOk() => ok.Location = new Point(ClientSize.Width - 108, ClientSize.Height - 42);
    PositionOk();
    Resize += (_, _) => PositionOk();
  }

  private void SetExpanded(bool expanded)
  {
    _expanded = expanded;
    _details.Visible = expanded;
    _copy.Visible = expanded;
    _toggle.Text = expanded ? "Hide technical details" : "Show technical details";
    ClientSize = new Size(ClientSize.Width, expanded ? ExpandedHeight : CollapsedHeight);
  }

  /// <summary>
  /// Shows the dialog. <paramref name="headline"/> says what went wrong,
  /// <paramref name="body"/> says what the operator can do about it.
  /// </summary>
  public static void Show(IWin32Window? owner, string headline, string body, Exception? ex = null)
  {
    var detail = ex == null ? null : BuildDetail(ex);
    using var dialog = new ErrorDialog(headline, body, detail);
    dialog.ShowDialog(owner);
  }

  private static string BuildDetail(Exception ex)
  {
    var sb = new StringBuilder();
    sb.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    sb.AppendLine();
    sb.Append(ex);
    return sb.ToString();
  }
}
