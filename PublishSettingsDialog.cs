namespace CrossMgrInterface;

/// <summary>
/// Where the club's websites and the key are entered.
///
/// Set up once, in the club house, and then never touched on a race day - so
/// it lives behind a menu item, like the reader's connection settings. Two
/// addresses, one key: the results site and the live timing site are the same
/// server, and a second key would be a second thing to email, paste and lose.
///
/// The key is a password. This dialog never shows a saved one back: it says
/// which one is saved, by its first letters, and offers to replace or forget it.
/// </summary>
public sealed class PublishSettingsDialog : Form
{
  private readonly TextBox _url = new();
  private readonly TextBox _liveUrl = new();
  private readonly TextBox _key = new();
  private readonly CheckBox _show = new();
  private readonly Label _status = new();
  private readonly Button _test = new();
  private readonly Button _testLive = new();
  private readonly RadioButton _namesShown = new();
  private readonly RadioButton _namesHidden = new();
  private readonly ComboBox _nameStyle = new();
  private readonly Label _nameExample = new();
  private readonly bool _hasKey;

  public string SiteUrl => _url.Text.Trim().TrimEnd('/');

  /// <summary>Empty means live timing is not set up on this computer.</summary>
  public string? LiveSiteUrl =>
    string.IsNullOrWhiteSpace(_liveUrl.Text) ? null : _liveUrl.Text.Trim().TrimEnd('/');

  /// <summary>The key as typed, or null when the saved one was left alone.</summary>
  public string? NewKey { get; private set; }

  /// <summary>The operator asked for the saved key to be removed.</summary>
  public bool ForgetKey { get; private set; }

  public bool PublishNamesByDefault => _namesShown.Checked;
  public NameStyle HiddenNameStyle => (NameStyle)Math.Max(0, _nameStyle.SelectedIndex);

  public PublishSettingsDialog(string? currentUrl, string? liveUrl, bool hasKey, string? keyHint = null,
                               bool publishNamesByDefault = true, NameStyle hiddenNameStyle = NameStyle.FirstNameInitial)
  {
    _hasKey = hasKey;

    Text = "Results website";
    FormBorderStyle = FormBorderStyle.FixedDialog;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = false;
    ClientSize = new Size(460, 548);

    var intro = new Label
    {
      Text = "Published results appear on a website riders can open on their phones.",
      Location = new Point(16, 16),
      Size = new Size(428, 20)
    };

    var urlLabel = new Label { Text = "Results website address:", Location = new Point(16, 48), AutoSize = true };
    _url.Location = new Point(16, 70);
    _url.Width = 428;
    _url.Text = string.IsNullOrWhiteSpace(currentUrl) ? HttpResultsPublisher.DefaultUrl : currentUrl;

    var urlHint = new Label
    {
      Text = "Leave these as they are unless your club was told otherwise.",
      Location = new Point(16, 96),
      Size = new Size(428, 18),
      ForeColor = Color.DimGray
    };

    // The live site: same key, its own address, its own Test - so a wrong host
    // or a certificate not yet issued shows up here and not at the track.
    var liveLabel = new Label { Text = "Live timing address:", Location = new Point(16, 122), AutoSize = true };
    _liveUrl.Location = new Point(16, 144);
    _liveUrl.Width = 340;
    _liveUrl.Text = string.IsNullOrWhiteSpace(liveUrl) ? HttpLiveTimingPublisher.DefaultUrl : liveUrl;

    _testLive.Text = "Test";
    _testLive.Location = new Point(364, 143);
    _testLive.Size = new Size(80, 25);
    _testLive.Click += async (_, _) => await TestAsync(live: true);

    var keyLabel = new Label { Text = "Key:", Location = new Point(16, 176), AutoSize = true };
    _key.Location = new Point(16, 198);
    _key.Width = 340;
    _key.UseSystemPasswordChar = true;
    _key.PlaceholderText = hasKey ? "Paste a new key here to replace the saved one" : "";

    // A key arrives by email and is too long to retype without a mistake.
    var paste = new Button { Text = "Paste", Location = new Point(364, 197), Size = new Size(80, 25) };
    paste.Click += (_, _) =>
    {
      if (Clipboard.ContainsText()) _key.Text = Clipboard.GetText().Trim();
    };

    _show.Text = "Show the key";
    _show.Location = new Point(16, 228);
    _show.AutoSize = true;
    _show.CheckedChanged += (_, _) => _key.UseSystemPasswordChar = !_show.Checked;

    _status.Location = new Point(16, 254);
    _status.Size = new Size(428, 50);
    _status.ForeColor = Color.DimGray;
    // A saved key is never shown back - it is a password - but the operator
    // must be able to see that one is there and which one, or an empty box
    // reads as "the key is gone".
    _status.Text = hasKey
      ? $"Key {keyHint ?? "saved"} is on this computer and will stay unless you paste a new one " +
        "or press Forget this key. It works for both websites."
      : "The key identifies your club. Treat it like a password - do not email it " +
        "or put it in a screenshot. It works for both websites.";

    _test.Text = "Test connection";
    _test.Location = new Point(16, 316);
    _test.Size = new Size(130, 27);
    _test.Click += async (_, _) => await TestAsync(live: false);

    var forget = new Button { Text = "Forget this key", Location = new Point(154, 316), Size = new Size(130, 27) };
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

    // Rider names on the websites. The sheet always carries full names; this
    // is only about what the world sees. A rider's own yes/no in the rider
    // list wins over the default chosen here.
    var namesHeading = new Label
    {
      Text = "Rider names on the websites",
      Location = new Point(16, 358),
      AutoSize = true,
      Font = new Font(Font, FontStyle.Bold)
    };

    _namesShown.Text = "Show full names, unless a rider's list entry says no";
    _namesShown.Location = new Point(16, 382);
    _namesShown.AutoSize = true;
    _namesShown.Checked = publishNamesByDefault;

    _namesHidden.Text = "Shorten every name, unless a rider's list entry says yes";
    _namesHidden.Location = new Point(16, 404);
    _namesHidden.AutoSize = true;
    _namesHidden.Checked = !publishNamesByDefault;

    var styleLabel = new Label { Text = "A shortened name looks like:", Location = new Point(16, 432), AutoSize = true };
    _nameStyle.Location = new Point(16, 454);
    _nameStyle.Width = 200;
    _nameStyle.DropDownStyle = ComboBoxStyle.DropDownList;
    _nameStyle.Items.AddRange(new object[] { "First name and initial", "First three letters, then stars" });
    _nameStyle.SelectedIndex = (int)hiddenNameStyle;
    _nameStyle.SelectedIndexChanged += (_, _) => ShowNameExample();

    _nameExample.Location = new Point(228, 457);
    _nameExample.Size = new Size(216, 20);
    _nameExample.ForeColor = Color.DimGray;
    ShowNameExample();

    var namesHint = new Label
    {
      Text = "A column called \"public\" (yes/no) in the rider list sets it per rider.",
      Location = new Point(16, 482),
      Size = new Size(428, 18),
      ForeColor = Color.DimGray
    };

    var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(268, 504), Size = new Size(84, 27) };
    var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(360, 504), Size = new Size(84, 27) };

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
      intro, urlLabel, _url, urlHint, liveLabel, _liveUrl, _testLive,
      keyLabel, _key, paste, _show, _status, _test, forget,
      namesHeading, _namesShown, _namesHidden, styleLabel, _nameStyle, _nameExample, namesHint,
      ok, cancel
    });

    AcceptButton = ok;
    CancelButton = cancel;
  }

  /// <summary>The style, shown on a name so nobody has to imagine it.</summary>
  private void ShowNameExample() =>
    _nameExample.Text = "e.g. " + NamePrivacy.Hide("Lena", "Brandt", HiddenNameStyle);

  /// <summary>
  /// Asks a website whether the key works.
  ///
  /// The single most useful control here: it turns "the results would not
  /// publish" at the track into "that key is wrong" in the club house.
  /// </summary>
  private async Task TestAsync(bool live)
  {
    var key = string.IsNullOrWhiteSpace(_key.Text)
      ? (ForgetKey || !_hasKey ? null : PublishCredentials.Load())
      : _key.Text.Trim();

    var url = live ? LiveSiteUrl : SiteUrl;
    if (string.IsNullOrWhiteSpace(url) || key == null)
    {
      _status.ForeColor = Color.DimGray;
      _status.Text = "Enter the website address and the key first.";
      return;
    }

    _test.Enabled = false;
    _testLive.Enabled = false;
    _status.ForeColor = Color.DimGray;
    _status.Text = live ? "Asking the live timing website..." : "Asking the results website...";

    try
    {
      var outcome = live
        ? await new HttpLiveTimingPublisher(url, key).TestAsync(CancellationToken.None)
        : await new HttpResultsPublisher(url, key).TestAsync(CancellationToken.None);

      _status.ForeColor = outcome.Ok ? Color.DarkGreen : Color.DarkRed;
      _status.Text = outcome.Message;
    }
    finally
    {
      _test.Enabled = true;
      _testLive.Enabled = true;
    }
  }
}
