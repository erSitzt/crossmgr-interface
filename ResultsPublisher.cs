using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

namespace CrossMgrInterface;

/// <summary>How a publish ended, in terms the dialog can turn into a sentence.</summary>
public enum PublishStatus
{
  Published,
  NotConfigured,
  NoInternet,
  Unauthorized,
  Rejected,
  ServerError,
  TimedOut,
  TooLarge,
  Cancelled
}

/// <summary>What happened, and where the results landed if they did.</summary>
public readonly record struct PublishOutcome(PublishStatus Status, string Message, string? Url = null)
{
  public bool Ok => Status == PublishStatus.Published;
}

public enum PublishPhase { Preparing, Sending, WaitingForServer, Retrying }

/// <summary>
/// Progress is reported by phase, not by byte. A 90 KB body is sent in one go
/// long before a progress bar could say anything useful about it, and "waiting
/// for the website" is the part that actually takes time.
/// </summary>
public readonly record struct PublishProgress(PublishPhase Phase, long Bytes, int Attempt, int OfAttempts);

public interface IResultsPublisher
{
  bool IsConfigured { get; }

  Task<PublishOutcome> PublishAsync(PublishedSession session, IProgress<PublishProgress>? progress,
                                    CancellationToken cancellationToken);

  Task<PublishOutcome> TestAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Sends a finished session to the results website.
///
/// Built like TileFetcher - one client for the process, a real contactable
/// user agent, backoff, and it never throws: every failure comes back as a
/// status and a sentence a volunteer can act on.
///
/// The verb is PUT, addressed by the session's own id, so publishing is
/// idempotent. A timeout on a field with one bar of signal can be retried
/// without any risk of the race appearing twice.
/// </summary>
public sealed class HttpResultsPublisher : IResultsPublisher
{
  public const string DefaultUrl = "https://results.openlaptime.de";

  /// <summary>
  /// Deliberately longer than TileFetcher's 15 seconds. A tile that does not
  /// arrive quickly is not worth waiting for; a whole race is, and on a phone
  /// hotspot in a field an upload regularly takes half a minute. A short cap
  /// would make "slow internet" and "no internet" look identical.
  /// </summary>
  private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(90);

  /// <summary>A button press, not an upload.</summary>
  private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(8);

  private const int MaxAttempts = 3;

  /// <summary>Refused before anything is sent, so nobody watches a bar that cannot finish.</summary>
  private const long MaxPayloadBytes = 8 * 1024 * 1024;

  private static readonly HttpClient Client = CreateClient();

  private readonly string? _baseUrl;
  private readonly string? _key;
  private readonly Action<string>? _log;
  private readonly HttpMessageInvoker? _transport;

  public HttpResultsPublisher(string? baseUrl, string? key, Action<string>? log = null,
                              HttpMessageHandler? handler = null)
  {
    _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.Trim().TrimEnd('/');
    _key = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    _log = log;
    _transport = handler == null ? null : new HttpMessageInvoker(handler);
  }

  /// <summary>Reads what the club has set up. Both halves are needed.</summary>
  public static HttpResultsPublisher FromSettings(AppSettings settings, Action<string>? log = null) =>
    new(settings.ResultsSiteUrl, PublishCredentials.Load(), log);

  public bool IsConfigured => _baseUrl != null && _key != null;

  public async Task<PublishOutcome> TestAsync(CancellationToken cancellationToken)
  {
    if (!IsConfigured)
      return new PublishOutcome(PublishStatus.NotConfigured, "The results website has not been set up yet.");

    using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/v1/ping");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TestTimeout);

    try
    {
      using var response = await SendAsync(request, timeout.Token).ConfigureAwait(false);
      Log($"ping {Host()} -> {(int)response.StatusCode}");

      if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
        return new PublishOutcome(PublishStatus.Unauthorized, "The website did not accept that key.");

      if (!response.IsSuccessStatusCode)
        return new PublishOutcome(PublishStatus.ServerError, "The website answered, but with an error.");

      return new PublishOutcome(PublishStatus.Published, "The website answered. The key is accepted.");
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      return new PublishOutcome(PublishStatus.Cancelled, "Stopped.");
    }
    catch (OperationCanceledException)
    {
      return new PublishOutcome(PublishStatus.TimedOut, "The website did not answer in time.");
    }
    catch (Exception ex)
    {
      return new PublishOutcome(Classify(ex), Describe(Classify(ex)));
    }
  }

  public async Task<PublishOutcome> PublishAsync(PublishedSession session,
    IProgress<PublishProgress>? progress, CancellationToken cancellationToken)
  {
    if (!IsConfigured)
      return new PublishOutcome(PublishStatus.NotConfigured, "The results website has not been set up yet.");

    progress?.Report(new PublishProgress(PublishPhase.Preparing, 0, 1, MaxAttempts));

    var json = PublishPayloadBuilder.Serialise(session);
    var raw = Encoding.UTF8.GetBytes(json);

    if (raw.LongLength > MaxPayloadBytes)
    {
      return new PublishOutcome(PublishStatus.TooLarge,
        $"These results are too big to publish ({raw.LongLength / (1024 * 1024)} MB). Please report this.");
    }

    var body = Compress(raw);
    var url = $"{_baseUrl}/api/v1/sessions/{Uri.EscapeDataString(session.Session.PublicId)}";

    for (var attempt = 1; attempt <= MaxAttempts; attempt++)
    {
      if (cancellationToken.IsCancellationRequested)
        return new PublishOutcome(PublishStatus.Cancelled, "Stopped. Nothing was published.");

      progress?.Report(new PublishProgress(PublishPhase.Sending, body.LongLength, attempt, MaxAttempts));

      var (outcome, retryAfter) = await AttemptAsync(url, body, progress, attempt, cancellationToken)
        .ConfigureAwait(false);

      // A wrong key, or results the website will not accept, will be just as
      // wrong the second time. Three identical failures teach nobody anything.
      if (outcome.Ok || attempt == MaxAttempts || !Retryable(outcome.Status))
        return outcome;

      var wait = retryAfter ?? TimeSpan.FromSeconds(attempt == 1 ? 2 : 6);
      if (wait > TimeSpan.FromSeconds(30)) wait = TimeSpan.FromSeconds(30);

      progress?.Report(new PublishProgress(PublishPhase.Retrying, (long)wait.TotalSeconds, attempt, MaxAttempts));

      try
      {
        await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException)
      {
        return new PublishOutcome(PublishStatus.Cancelled, "Stopped. Nothing was published.");
      }
    }

    return new PublishOutcome(PublishStatus.ServerError, Describe(PublishStatus.ServerError));
  }

  private async Task<(PublishOutcome, TimeSpan?)> AttemptAsync(string url, byte[] body,
    IProgress<PublishProgress>? progress, int attempt, CancellationToken cancellationToken)
  {
    using var request = new HttpRequestMessage(HttpMethod.Put, url);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);

    var content = new ByteArrayContent(body);
    content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
    content.Headers.ContentEncoding.Add("gzip");
    request.Content = content;

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(AttemptTimeout);

    try
    {
      progress?.Report(new PublishProgress(PublishPhase.WaitingForServer, body.LongLength, attempt, MaxAttempts));

      using var response = await SendAsync(request, timeout.Token).ConfigureAwait(false);
      var code = (int)response.StatusCode;
      Log($"put {Host()} {body.LongLength} bytes, attempt {attempt} -> {code}");

      if (response.IsSuccessStatusCode)
      {
        var page = await ReadUrlAsync(response).ConfigureAwait(false);
        return (new PublishOutcome(PublishStatus.Published, "Published.", page), null);
      }

      var status = code switch
      {
        401 or 403 => PublishStatus.Unauthorized,
        413 => PublishStatus.TooLarge,
        >= 400 and < 500 and not 408 and not 429 => PublishStatus.Rejected,
        _ => PublishStatus.ServerError
      };

      return (new PublishOutcome(status, Describe(status)), response.Headers.RetryAfter?.Delta);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      return (new PublishOutcome(PublishStatus.Cancelled, "Stopped. Nothing was published."), null);
    }
    catch (OperationCanceledException)
    {
      Log($"put {Host()} attempt {attempt} timed out");
      return (new PublishOutcome(PublishStatus.TimedOut, Describe(PublishStatus.TimedOut)), null);
    }
    catch (Exception ex)
    {
      var status = Classify(ex);
      Log($"put {Host()} attempt {attempt} failed: {ex.GetType().Name}");
      return (new PublishOutcome(status, Describe(status)), null);
    }
  }

  private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
    _transport != null
      ? _transport.SendAsync(request, token)
      : Client.SendAsync(request, token);

  private static async Task<string?> ReadUrlAsync(HttpResponseMessage response)
  {
    try
    {
      using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
      using var document = await System.Text.Json.JsonDocument.ParseAsync(stream).ConfigureAwait(false);
      return document.RootElement.TryGetProperty("url", out var url) ? url.GetString() : null;
    }
    catch (Exception)
    {
      // The results are published either way; not knowing the address only
      // costs the volunteer a link to paste.
      return null;
    }
  }

  private static byte[] Compress(byte[] raw)
  {
    using var buffer = new MemoryStream();
    using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
      gzip.Write(raw, 0, raw.Length);
    return buffer.ToArray();
  }

  private static bool Retryable(PublishStatus status) =>
    status is PublishStatus.ServerError or PublishStatus.TimedOut or PublishStatus.NoInternet;

  private static PublishStatus Classify(Exception ex) =>
    ex is HttpRequestException http &&
    (http.InnerException is SocketException || http.StatusCode == null)
      ? PublishStatus.NoInternet
      : PublishStatus.ServerError;

  private static string Describe(PublishStatus status) => status switch
  {
    PublishStatus.NoInternet =>
      "This computer cannot reach the internet. The results are safe here - you can " +
      "publish them later from Race > Past sessions.",
    PublishStatus.Unauthorized => "The website did not accept the key.",
    PublishStatus.Rejected => "The website would not accept these results. Please report this.",
    PublishStatus.TimedOut => "The website is taking too long to answer.",
    PublishStatus.TooLarge => "These results are too big to publish. Please report this.",
    _ => "The website is not answering. Nothing has been changed. Try again in a few minutes."
  };

  private string Host()
  {
    // The host and nothing else. A key must never reach the log.
    try { return new Uri(_baseUrl!).Host; } catch (Exception) { return "results website"; }
  }

  private void Log(string message) => _log?.Invoke($"🌐 {message}");

  private static HttpClient CreateClient()
  {
    // One client per process, as in TileFetcher: a new HttpClient per request
    // exhausts sockets.
    var handler = new SocketsHttpHandler
    {
      MaxConnectionsPerServer = 2,
      AutomaticDecompression = DecompressionMethods.All,
      PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    };

    // No client-wide timeout: each attempt carries its own, so a retry is not
    // cut short by the budget the previous one already spent.
    var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
      $"CrossMgrInterface/{AppVersion.Display} (+https://github.com/erSitzt/crossmgr-interface)");
    return client;
  }
}
