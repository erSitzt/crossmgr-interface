namespace CrossMgrInterface;

/// <summary>
/// The in-application help: every topic in a list on the left, the chosen one on
/// the right.
///
/// It replaced a quick start in a message box, which could hold eight lines and
/// had to be closed before the operator could do the first of them. This window
/// stays open beside the application, so the steps can be followed as they are
/// read. The words live in <see cref="HelpTopics"/>.
/// </summary>
public sealed class HelpDialog : Form
{
  private readonly TreeView _topics = new();
  private readonly RichTextBox _text = new();

  private readonly Font _titleFont;
  private readonly Font _summaryFont;
  private readonly Font _headingFont;
  private readonly Font _bodyFont;
  private readonly Font _tipFont;
  private readonly Font _keyFont;
  private readonly Font _spacerFont;
  private readonly Font _listFont;
  private readonly Font _groupFont;

  public HelpDialog()
  {
    Text = "Help";
    StartPosition = FormStartPosition.CenterParent;
    ClientSize = new Size(1000, 720);
    MinimumSize = new Size(700, 480);
    ShowInTaskbar = false;
    MinimizeBox = false;

    _titleFont = new Font("Segoe UI", 16F, FontStyle.Bold);
    _summaryFont = new Font("Segoe UI", 11F, FontStyle.Italic);
    _headingFont = new Font("Segoe UI", 12F, FontStyle.Bold);
    _bodyFont = new Font("Segoe UI", 10.5F);
    _tipFont = new Font("Segoe UI", 10.5F, FontStyle.Italic);
    _keyFont = new Font("Segoe UI", 10.5F, FontStyle.Bold);
    _spacerFont = new Font("Segoe UI", 4F);
    _listFont = new Font("Segoe UI", 10.5F);
    _groupFont = new Font("Segoe UI", 10.5F, FontStyle.Bold);

    var split = new SplitContainer
    {
      Dock = DockStyle.Fill,
      FixedPanel = FixedPanel.Panel1
    };

    // Only once the window has its real size: set in the constructor the splitter
    // was clamped against the container's default width, squeezing the topic list
    // to a sliver that cut the group names off.
    Load += (_, _) =>
    {
      split.Panel1MinSize = 240;
      split.SplitterDistance = 300;
    };

    _topics.Dock = DockStyle.Fill;
    _topics.HideSelection = false;
    _topics.FullRowSelect = true;
    _topics.ShowLines = false;
    // The tree measures every row in its own font, so it gets the bold one the
    // group names use and the topics are set back to regular. The other way round
    // clipped the end off each bold group name.
    _topics.Font = _groupFont;
    _topics.ItemHeight = 26;
    // Narrow indent and a wide list, so no title is wider than the list: a wider
    // one scrolled the whole tree sideways and cut the start off the group names.
    _topics.Indent = 12;
    _topics.Scrollable = true;
    _topics.AfterSelect += (_, e) =>
    {
      if (e.Node?.Tag is HelpTopic topic) Render(topic);
    };

    foreach (var group in HelpTopics.Groups)
    {
      var groupNode = new TreeNode(group);
      foreach (var topic in HelpTopics.All.Where(t => t.Group == group))
        groupNode.Nodes.Add(new TreeNode(topic.Title) { Tag = topic, Name = topic.Id, NodeFont = _listFont });
      _topics.Nodes.Add(groupNode);
    }
    _topics.ExpandAll();

    _text.Dock = DockStyle.Fill;
    _text.ReadOnly = true;
    _text.BorderStyle = BorderStyle.None;
    _text.BackColor = Color.White;
    _text.DetectUrls = false;
    _text.ScrollBars = RichTextBoxScrollBars.Vertical;

    // A margin round the text; the control has none of its own.
    var textHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 16, 12, 8), BackColor = Color.White };
    textHost.Controls.Add(_text);

    split.Panel1.Controls.Add(_topics);
    split.Panel2.Controls.Add(textHost);

    var close = new Button { Text = "Close", Size = new Size(100, 34), Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
    close.Click += (_, _) => Close();

    var buttons = new Panel { Dock = DockStyle.Bottom, Height = 50 };
    close.Location = new Point(buttons.Width - close.Width - 12, 8);
    buttons.Controls.Add(close);
    buttons.Resize += (_, _) => close.Location = new Point(buttons.Width - close.Width - 12, 8);

    Controls.Add(split);
    Controls.Add(buttons);
    CancelButton = close;

    ShowTopic(HelpTopicIds.QuickStart);
  }

  /// <summary>Selects and shows one topic. An unknown id leaves the current one on screen.</summary>
  public void ShowTopic(string id)
  {
    var nodes = _topics.Nodes.Find(id, searchAllChildren: true);
    if (nodes.Length == 0) return;

    _topics.SelectedNode = nodes[0];
    nodes[0].EnsureVisible();
  }

  private void Render(HelpTopic topic)
  {
    _text.SuspendLayout();
    _text.Clear();

    Append(topic.Title, _titleFont, Color.Black);
    Spacer();
    Append(topic.Summary, _summaryFont, Color.DimGray);
    Spacer();

    foreach (var block in topic.Blocks)
    {
      switch (block.Kind)
      {
        case HelpBlockKind.Heading:
          Append("", _bodyFont, Color.Black);
          Append(block.Text, _headingFont, Color.FromArgb(0, 70, 140));
          Spacer();
          break;

        case HelpBlockKind.Paragraph:
          Append(block.Text, _bodyFont, Color.Black);
          Spacer();
          break;

        case HelpBlockKind.Tip:
          Append(block.Text, _tipFont, Color.FromArgb(90, 90, 90), indent: 16);
          Spacer();
          break;

        case HelpBlockKind.Steps:
          // "1." then a tab to where the text starts; wrapped lines hang there too.
          for (var i = 0; i < block.Items.Count; i++)
          {
            Append($"{i + 1}.\t{block.Items[i]}", _bodyFont, Color.Black, indent: 8, hanging: 26, tab: 34);
            Spacer();
          }
          break;

        case HelpBlockKind.Bullets:
          // Drawn like the steps rather than as rich text bullets, whose wrapped
          // lines went back under the bullet instead of under the text.
          foreach (var item in block.Items)
          {
            Append($"\u2022\t{item}", _bodyFont, Color.Black, indent: 10, hanging: 18, tab: 28);
            Spacer();
          }
          break;

        case HelpBlockKind.Keys:
          foreach (var row in block.Items)
          {
            var parts = row.Split('\t', 2);
            AppendKeyRow(parts[0], parts.Length > 1 ? parts[1] : "");
          }
          Spacer();
          break;
      }
    }

    var seeAlso = topic.SeeAlso
      .Select(HelpTopics.Find)
      .Where(t => t != null)
      .Select(t => t!.Title)
      .ToList();
    if (seeAlso.Count > 0)
    {
      Append("", _bodyFont, Color.Black);
      Append($"See also: {string.Join(", ", seeAlso)} - in the list on the left.", _tipFont, Color.DimGray);
    }

    _text.Select(0, 0);
    _text.ScrollToCaret();
    _text.ResumeLayout();
  }

  /// <summary>
  /// Adds one paragraph. Formatted after the text is in place: formatting set on
  /// an empty insertion point is not reliably carried onto the paragraph it
  /// becomes, which lost every hanging indent.
  /// </summary>
  private void Append(string text, Font font, Color color, int indent = 0, int hanging = 0, int tab = 0)
  {
    var start = _text.TextLength;
    _text.Select(start, 0);
    _text.SelectedText = text + "\n";

    _text.Select(start, text.Length + 1);
    _text.SelectionFont = font;
    _text.SelectionColor = color;
    _text.SelectionIndent = indent;
    _text.SelectionHangingIndent = hanging;
    _text.SelectionTabs = tab > 0 ? new[] { tab } : Array.Empty<int>();
  }

  /// <summary>A short blank line: the rich text box has no paragraph spacing of its own.</summary>
  private void Spacer() => Append("", _spacerFont, Color.Black);

  /// <summary>A word or key in bold, then what it means, wrapping under the meaning.</summary>
  private void AppendKeyRow(string key, string action)
  {
    const int column = 190;
    var start = _text.TextLength;
    _text.Select(start, 0);
    _text.SelectedText = $"{key}\t{action}\n";

    _text.Select(start, key.Length + action.Length + 2);
    _text.SelectionFont = _bodyFont;
    _text.SelectionColor = Color.Black;
    _text.SelectionBullet = false;
    _text.SelectionIndent = 8;
    _text.SelectionHangingIndent = column - 8;
    _text.SelectionTabs = new[] { column };

    _text.Select(start, key.Length);
    _text.SelectionFont = _keyFont;
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _titleFont.Dispose();
      _summaryFont.Dispose();
      _headingFont.Dispose();
      _bodyFont.Dispose();
      _tipFont.Dispose();
      _keyFont.Dispose();
      _spacerFont.Dispose();
      _listFont.Dispose();
      _groupFont.Dispose();
    }
    base.Dispose(disposing);
  }
}
