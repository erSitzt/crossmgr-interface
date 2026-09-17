namespace CrossMgrInterface;

/// <summary>
/// Where the club's results website and its key are entered.
///
/// Set up once, in the club house, and then never touched on a race day - so
/// it lives behind a menu item, like the reader's connection settings.
///
/// The key is a password. This dialog never shows a saved one back: it says
/// that one is saved, and offers to replace or forget it.
/// </summary>
public sealed class PublishSettingsDialog : Form
{
  private readonly TextBox _url = new();
  private readonly TextBox _key = new();
  private readonly CheckBox _show = new();
  private readonly Label _status = new();
  private readonly Button _test = new();

  public string SiteUrl => _url.Text.Trim().TrimEnd('/');

  /// <summary>The key as typed, or null when the saved one was left alone.</summary>
  public string? NewKey { get; private set; }

  /// <summary>The operator asked for the saved key to be removed.</summary>
  public bool ForgetKey { get; private set; }

  public PublishSettingsDialog(string? currentUrl, bool hasKey)
  {
    Text = "Results website";
    FormBorderStyle = FormBorderStyle.FixedDialog;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = false;
    ClientSize = new Size(460, 340);

    var intro = new Label
    {
      Text = "Published results appear on a website riders can open on their phones.",
      Location = new Point(16, 16),
      Size = new Size(428, 20)
    };

    var urlLabel = new Label { Text = "Website address:", Location = new Point(16, 50), AutoSize = true };
    _url.Location = new Point(16, 72);
    _url.Width = 428;
    _url.Text = string.IsNullOrWhiteSpace(currentUrl) ? HttpResultsPublisher.DefaultUrl : currentUrl;

    var urlHint = new Label
    {
      Text = "Leave this as it is unless your club was told otherwise.",
      Location = new Point(16, 98),
      Size = new Size(428, 18),
      ForeColor = Color.DimGray
    };

    var keyLabel = new Label { Text = "Key:", Location = new Point(16, 128), AutoSize = true };
    _key.Location = new Point(16, 150);
    _key.Width = 340;
    _key.UseSystemPasswordChar = true;

    // A key arrives by email and is too long to retype without a mistake.
    var paste = new Button { Text = "Paste", Location = new Point(364, 149), Size = new Size(80, 25) };
    paste.Click += (_, _) =>
    {
      if (Clipboard.ContainsText()) _key.Text = Clipboard.GetText().Trim();
    };

    _show.Text = "Show the key";
    _show.Location = new Point(16, 180);
    _show.AutoSize = true;
    _show.CheckedChanged += (_, _) => _key.UseSystemPasswordChar = !_show.Checked;

    _status.Location = new Point(16, 206);
    _status.Size = new Size(428, 36);
    _status.ForeColor = Color.DimGray;
    _status.Text = hasKey
      ? "A key is already saved on this computer. Leave this box empty to keep it."
      : "The key identifies your club. Treat it like a password - do not email it " +
        "or put it in a screenshot.";

    _test.Text = "Test connection";
    _test.Location = new Point(16, 248);
    _test.Size = new Size(130, 27);
    _test.Click += async (_, _) => await TestAsync(hasKey);

    var forget = new Button { Text = "Forget this key", Location = new Point(154, 248), Size = new Size(130, 27) };
    forget.Enabled = hasKey;
    forget.Click += (_, _) =>
    {
      ForgetKey = true;
      NewKey = null;
      _key.Clear();
      _status.ForeColor = Color.DimGray;
      _status.Text = "The saved key will be removed when you press OK.";
      forget.Enabled = false;
    };

    var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(268, 296), Size = new Size(84, 27) };
    var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(360, 296), Size = new Size(84, 27) };

    ok.Click += (_, _) =>
    {
      if (!string.IsNullOrWhiteSpace(_key.Text))
      {
        NewKey = _key.Text.Trim();
        ForgetKey = false;
      }
    };

    Controls.AddRange(new Control[]
    {
      intro, urlLabel, _url, urlHint, keyLabel, _key, paste, _show, _status, _test, forget, ok, cancel
    });

    AcceptButton = ok;
    CancelButton = cancel;
  }

  /// <summary>
  /// Asks the website whether the key works.
  ///
  /// The single most useful control here: it turns "the results would not
  /// publish" at the track into "that key is wrong" in the club house.
  /// </summary>
  private async Task TestAsync(bool hasKey)
  {
    var key = string.IsNullOrWhiteSpace(_key.Text)
      ? (ForgetKey ? null : PublishCredentials.Load())
      : _key.Text.Trim();

    if (string.IsNullOrWhiteSpace(_url.Text) || key == null)
    {
      _status.ForeColor = Color.DimGray;
      _status.Text = "Enter the website address and the key first.";
      return;
    }

    _test.Enabled = false;
    _status.ForeColor = Color.DimGray;
    _status.Text = "Asking the website...";

    try
    {
      var publisher = new HttpResultsPublisher(SiteUrl, key);
      var outcome = await publisher.TestAsync(CancellationToken.None);

      _status.ForeColor = outcome.Ok ? Color.DarkGreen : Color.DarkRed;
      _status.Text = outcome.Message;
    }
    finally
    {
      _test.Enabled = true;
    }
  }
}
