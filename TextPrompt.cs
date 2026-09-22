namespace CrossMgrInterface;

/// <summary>
/// A one-line text prompt. WinForms has no InputBox, and the alternative is
/// another hand-built dialog every time something needs a name.
/// </summary>
public sealed class TextPrompt : Form
{
  private readonly Control _input;

  private TextPrompt(string title, string caption, string initial, IReadOnlyList<string>? choices)
  {
    Text = title;
    FormBorderStyle = FormBorderStyle.FixedDialog;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = false;
    ShowInTaskbar = false;
    ClientSize = new Size(340, string.IsNullOrEmpty(caption) ? 96 : 118);

    var top = 14;

    if (!string.IsNullOrEmpty(caption))
    {
      Controls.Add(new Label { Text = caption, Location = new Point(14, top), Width = 312, AutoSize = false, Height = 18 });
      top += 24;
    }

    // A list to pick from where there is one, still typeable so a value that is
    // not on it can be entered. Picking beats typing for something like a class,
    // where a typo does not fail - it quietly makes a second class of one rider.
    _input = choices is { Count: > 0 }
      ? new ComboBox
      {
        Location = new Point(14, top),
        Width = 312,
        Text = initial,
        DropDownStyle = ComboBoxStyle.DropDown,
        AutoCompleteMode = AutoCompleteMode.SuggestAppend,
        AutoCompleteSource = AutoCompleteSource.ListItems
      }
      : new TextBox { Location = new Point(14, top), Width = 312, Text = initial };

    if (_input is ComboBox combo) combo.Items.AddRange(choices!.ToArray());
    top += 34;

    var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(160, top), Width = 80 };
    var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(246, top), Width = 80 };

    Controls.AddRange(new Control[] { _input, ok, cancel });
    AcceptButton = ok;
    CancelButton = cancel;

    Shown += (_, _) =>
    {
      _input.Focus();
      if (_input is TextBox box) box.SelectAll();
      else if (_input is ComboBox combo) combo.SelectAll();
    };
  }

  /// <summary>The trimmed text, or null if cancelled or left blank.</summary>
  /// <param name="choices">
  /// Offered in a drop-down the operator can still type into. Null or empty for
  /// a plain text box.
  /// </param>
  public static string? Ask(IWin32Window owner, string title, string initial, string caption = "",
    IReadOnlyList<string>? choices = null)
  {
    using var prompt = new TextPrompt(title, caption, initial, choices);
    if (prompt.ShowDialog(owner) != DialogResult.OK) return null;

    var text = prompt._input.Text.Trim();
    return text.Length == 0 ? null : text;
  }
}
