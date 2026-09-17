namespace CrossMgrInterface;

/// <summary>What a session looks like before it is published.</summary>
public sealed record PublishRequest
{
  public required PublishedSession Session { get; init; }
  public required string SiteName { get; init; }

  /// <summary>Riders and laps, for the summary. Counted rather than recomputed.</summary>
  public int Riders { get; init; }
  public int Laps { get; init; }

  public string? CircuitName { get; init; }
  public DateTime? PublishedBefore { get; init; }

  /// <summary>Set when the session cannot be published, with the reason to show.</summary>
  public string? Refusal { get; init; }
}

/// <summary>
/// Quotes before it acts, then reports plainly.
///
/// Shaped like TileCacheProgressDialog: say what is about to happen and how
/// much of it there is, let the operator stop it, and stay open afterwards so a
/// failure can be read and retried without finding the menu item again.
/// </summary>
public sealed class PublishResultsDialog : Form
{
  private readonly PublishRequest _request;
  private readonly IResultsPublisher _publisher;

  private readonly Label _summary = new();
  private readonly Label _status = new();
  private readonly ProgressBar _progress = new();
  private readonly LinkLabel _link = new();
  private readonly Button _publish = new();
  private readonly Button _settings = new();
  private readonly Button _close = new();

  private CancellationTokenSource? _cancel;
  private bool _running;

  /// <summary>Set once the results are on the website, so the caller can record it.</summary>
  public string? PublishedUrl { get; private set; }

  public bool Published => PublishedUrl != null;

  /// <summary>The operator asked to open the settings instead.</summary>
  public bool WantsSettings { get; private set; }

  public PublishResultsDialog(PublishRequest request, IResultsPublisher publisher)
  {
    _request = request;
    _publisher = publisher;

    Text = "Publish results";
    FormBorderStyle = FormBorderStyle.FixedDialog;
    StartPosition = FormStartPosition.CenterParent;
    MinimizeBox = false;
    MaximizeBox = false;
    ClientSize = new Size(470, 290);

    var heading = new Label
    {
      Text = $"Publish \u201c{request.Session.Session.Title}\u201d to {request.SiteName}?",
      Location = new Point(16, 16),
      Size = new Size(438, 38),
      Font = new Font(Font, FontStyle.Bold)
    };

    _summary.Location = new Point(16, 60);
    _summary.Size = new Size(438, 120);
    _summary.Text = BuildSummary();

    _progress.Location = new Point(16, 188);
    _progress.Size = new Size(438, 16);
    _progress.Style = ProgressBarStyle.Marquee;
    _progress.Visible = false;

    _status.Location = new Point(16, 210);
    _status.Size = new Size(438, 34);
    _status.ForeColor = Color.DimGray;

    _link.Location = new Point(16, 210);
    _link.Size = new Size(438, 34);
    _link.Visible = false;
    _link.LinkClicked += (_, _) => OpenPublishedPage();

    _publish.Text = "Publish";
    _publish.Location = new Point(190, 252);
    _publish.Size = new Size(84, 27);
    _publish.Click += async (_, _) => await PublishOrStopAsync();

    _settings.Text = "Settings...";
    _settings.Location = new Point(16, 252);
    _settings.Size = new Size(100, 27);
    _settings.Click += (_, _) =>
    {
      WantsSettings = true;
      DialogResult = DialogResult.Retry;
      Close();
    };

    _close.Text = "Cancel";
    _close.Location = new Point(282, 252);
    _close.Size = new Size(84, 27);
    _close.DialogResult = DialogResult.Cancel;

    var copy = new Button { Text = "Copy link", Location = new Point(374, 252), Size = new Size(80, 27), Visible = false };
    copy.Click += (_, _) =>
    {
      // The volunteer's next act is pasting this into the club's group chat.
      if (PublishedUrl != null) Clipboard.SetText(PublishedUrl);
    };
    CopyButton = copy;

    Controls.AddRange(new Control[] { heading, _summary, _progress, _status, _link, _publish, _settings, _close, copy });

    AcceptButton = _publish;
    CancelButton = _close;

    if (request.Refusal != null || !publisher.IsConfigured)
    {
      _publish.Enabled = false;
      if (!publisher.IsConfigured) AcceptButton = _settings;
    }
  }

  private Button CopyButton { get; }

  private string BuildSummary()
  {
    if (_request.Refusal != null) return _request.Refusal;

    if (!_publisher.IsConfigured)
      return "The results website has not been set up yet.\r\n\r\n" +
             "Press Settings... to enter the address and the key your club was given.";

    var lines = new List<string>
    {
      $"{_request.Riders} riders, {_request.Laps} laps."
    };

    lines.Add(_request.CircuitName != null
      ? $"Circuit: {_request.CircuitName}."
      : "No circuit - the website will show the results without a map.");

    lines.Add("");
    lines.Add("Riders' names, numbers, classes, teams and lap times are sent.");
    lines.Add("Transponder IDs are not.");

    if (_request.PublishedBefore is { } before)
    {
      lines.Add("");
      lines.Add($"Published before at {before:HH:mm}. Publishing again replaces what is on the website.");
    }

    return string.Join("\r\n", lines);
  }

  private async Task PublishOrStopAsync()
  {
    if (_running)
    {
      _cancel?.Cancel();
      return;
    }

    _running = true;
    _cancel = new CancellationTokenSource();

    _publish.Text = "Stop";
    _settings.Enabled = false;
    _close.Enabled = false;
    _progress.Visible = true;
    _status.ForeColor = Color.DimGray;

    var progress = new Progress<PublishProgress>(Report);

    try
    {
      var outcome = await _publisher.PublishAsync(_request.Session, progress, _cancel.Token);
      Finish(outcome);
    }
    catch (Exception ex)
    {
      // PublishAsync is written not to throw; if it ever does, the operator
      // still gets something they can report rather than a silent dialog.
      _status.ForeColor = Color.DarkRed;
      _status.Text = "Something went wrong while publishing.";
      ErrorDialog.Show(this, "Publish results",
        "The results could not be published because of an unexpected problem.", ex);
    }
    finally
    {
      _running = false;
      _progress.Visible = false;
      _settings.Enabled = true;
      _close.Enabled = true;
      _cancel?.Dispose();
      _cancel = null;
    }
  }

  private void Report(PublishProgress progress)
  {
    _status.Text = progress.Phase switch
    {
      PublishPhase.Preparing => "Preparing the results...",
      PublishPhase.Sending => $"Sending {Kilobytes(progress.Bytes)}...",
      PublishPhase.WaitingForServer => "Waiting for the website...",
      PublishPhase.Retrying =>
        $"No answer - trying again in {progress.Bytes} seconds ({progress.Attempt + 1} of {progress.OfAttempts}).",
      _ => ""
    };
  }

  private void Finish(PublishOutcome outcome)
  {
    if (outcome.Ok)
    {
      PublishedUrl = outcome.Url;
      _summary.Text = "";
      _status.Visible = false;
      _link.Visible = true;
      _link.Text = outcome.Url != null
        ? $"Published. Riders can see the results at\r\n{outcome.Url}"
        : "Published.";
      _link.LinkArea = outcome.Url != null
        ? new LinkArea(_link.Text.IndexOf(outcome.Url, StringComparison.Ordinal), outcome.Url.Length)
        : new LinkArea(0, 0);

      _publish.Visible = false;
      _settings.Visible = false;
      CopyButton.Visible = outcome.Url != null;
      _close.Text = "Close";
      _close.DialogResult = DialogResult.OK;
      AcceptButton = _close;
      return;
    }

    _status.ForeColor = outcome.Status == PublishStatus.Cancelled ? Color.DimGray : Color.DarkRed;
    _status.Text = outcome.Message;

    // Leave Publish alive so trying again is one click, not a hunt for the menu.
    _publish.Text = "Publish";
    if (outcome.Status == PublishStatus.Unauthorized) AcceptButton = _settings;
  }

  private void OpenPublishedPage()
  {
    if (PublishedUrl == null) return;

    try
    {
      System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(PublishedUrl)
      {
        UseShellExecute = true
      });
    }
    catch (Exception)
    {
      // No browser, or it refused. The link is on screen to copy either way.
    }
  }

  private static string Kilobytes(long bytes) =>
    bytes < 1024 ? $"{bytes} bytes" : $"{bytes / 1024} KB";
}
