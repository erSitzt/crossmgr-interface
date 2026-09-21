using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

namespace CrossMgrInterface;

public enum LiveStatus
{
  Sent,
  NotConfigured,
  Unauthorized,
  Rejected,
  RateLimited,
  ServerError,
  TimedOut,
  NoInternet,
  Cancelled
}

public readonly record struct LiveOutcome(LiveStatus Status, int? HttpCode = null)
{
  public bool Ok => Status == LiveStatus.Sent;
}

public interface ILivePublisher
{
  bool IsConfigured { get; }
  Task<LiveOutcome> PushAsync(LiveSnapshot snapshot, CancellationToken cancellationToken);
  Task<PublishOutcome> TestAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Sends the running race to the live timing website.
///
/// Built like HttpResultsPublisher but with the opposite temperament. That one
/// carries a whole session once and may spend minutes making sure it lands;
/// this one is called every three seconds with a picture that is stale three
/// seconds later, so it tries once, gives up after ten seconds, and never
/// retries - the next tick is the retry. It has a connection of its own, so a
/// results upload in progress can never hold it up.
///
/// Never throws. Every failure is a status the Race Day tile can show.
/// </summary>
public sealed class HttpLiveTimingPublisher : ILivePublisher
{
  public const string DefaultUrl = "https://livetiming.openlaptime.de";

  private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
  private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(8);

  private static readonly HttpClient Client = CreateClient();

  private readonly string? _baseUrl;
  private readonly string? _key;
  private readonly Action<string>? _log;
  private readonly HttpMessageInvoker? _transport;
  private readonly TimeSpan _timeout;

  public HttpLiveTimingPublisher(string? baseUrl, string? key, Action<string>? log = null,
                                 HttpMessageHandler? handler = null, TimeSpan? timeout = null)
  {
    _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.Trim().TrimEnd('/');
    _key = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    _log = log;
    _transport = handler == null ? null : new HttpMessageInvoker(handler);
    _timeout = timeout ?? DefaultTimeout;
  }

  /// <summary>The same key as the results site: one server, one key per laptop.</summary>
  public static HttpLiveTimingPublisher FromSettings(AppSettings settings, Action<string>? log = null) =>
    new(settings.LiveSiteUrl, PublishCredentials.Load(), log);

  public bool IsConfigured => _baseUrl != null && _key != null;

  /// <summary>
  /// True for an address on this computer. A demo may publish live to one of
  /// those - which is how the whole thing is tried out - and to nothing else.
  /// </summary>
  public static bool IsLoopback(string? url) =>
    Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsLoopback;

  public async Task<LiveOutcome> PushAsync(LiveSnapshot snapshot, CancellationToken cancellationToken)
  {
    if (!IsConfigured) return new LiveOutcome(LiveStatus.NotConfigured);

    var body = Compress(Encoding.UTF8.GetBytes(LiveSnapshotBuilder.Serialise(snapshot)));
    var url = $"{_baseUrl}/api/v1/live/{Uri.EscapeDataString(snapshot.PublicId)}";

    using var request = new HttpRequestMessage(HttpMethod.Put, url);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
    var content = new ByteArrayContent(body);
    content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
    content.Headers.ContentEncoding.Add("gzip");
    request.Content = content;

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(_timeout);

    try
    {
      using var response = await SendAsync(request, timeout.Token).ConfigureAwait(false);
      var code = (int)response.StatusCode;

      if (response.IsSuccessStatusCode) return new LiveOutcome(LiveStatus.Sent, code);

      var status = code switch
      {
        401 or 403 => LiveStatus.Unauthorized,
        429 => LiveStatus.RateLimited,
        >= 400 and < 500 => LiveStatus.Rejected,
        _ => LiveStatus.ServerError
      };
      return new LiveOutcome(status, code);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      return new LiveOutcome(LiveStatus.Cancelled);
    }
    catch (OperationCanceledException)
    {
      return new LiveOutcome(LiveStatus.TimedOut);
    }
    catch (Exception ex)
    {
      return new LiveOutcome(Classify(ex));
    }
  }

  public async Task<PublishOutcome> TestAsync(CancellationToken cancellationToken)
  {
    if (!IsConfigured)
      return new PublishOutcome(PublishStatus.NotConfigured, "The live timing address has not been set up yet.");

    using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/v1/ping");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TestTimeout);

    try
    {
      using var response = await SendAsync(request, timeout.Token).ConfigureAwait(false);
      Log($"live ping {Host()} -> {(int)response.StatusCode}");

      if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        return new PublishOutcome(PublishStatus.Unauthorized, "Live timing: the website did not accept that key.");
      if (!response.IsSuccessStatusCode)
        return new PublishOutcome(PublishStatus.ServerError, "Live timing: the website answered, but with an error.");

      return new PublishOutcome(PublishStatus.Published, "Live timing: the website answered. The key is accepted.");
    }
    catch (OperationCanceledException)
    {
      return new PublishOutcome(PublishStatus.TimedOut, "Live timing: the website did not answer in time.");
    }
    catch (Exception ex)
    {
      return Classify(ex) == LiveStatus.NoInternet
        ? new PublishOutcome(PublishStatus.NoInternet, "Live timing: this computer cannot reach the website.")
        : new PublishOutcome(PublishStatus.ServerError, "Live timing: the website could not be reached.");
    }
  }

  /// <summary>A sentence for the tile, per status. Says what to do where there is something to do.</summary>
  public static string Describe(LiveStatus status, string host) => status switch
  {
    LiveStatus.Sent => "Sending",
    LiveStatus.Unauthorized => "Key refused - Race > Results website...",
    LiveStatus.RateLimited => "Website asked for a pause",
    LiveStatus.Rejected => "Website refused the update",
    LiveStatus.TimedOut => $"{host} is slow to answer",
    LiveStatus.NoInternet => "No internet",
    LiveStatus.NotConfigured => "Not set up",
    _ => $"{host} is not answering"
  };

  private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
    _transport != null ? _transport.SendAsync(request, token) : Client.SendAsync(request, token);

  private static byte[] Compress(byte[] raw)
  {
    using var buffer = new MemoryStream();
    using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
      gzip.Write(raw, 0, raw.Length);
    return buffer.ToArray();
  }

  private static LiveStatus Classify(Exception ex) =>
    ex is HttpRequestException http && (http.InnerException is SocketException || http.StatusCode == null)
      ? LiveStatus.NoInternet
      : LiveStatus.ServerError;

  /// <summary>The host and nothing else. A key must never reach the log.</summary>
  public string Host()
  {
    try { return new Uri(_baseUrl!).Host; } catch (Exception) { return "live timing website"; }
  }

  private void Log(string message) => _log?.Invoke($"🌐 {message}");

  private static HttpClient CreateClient()
  {
    // Its own client, not the results publisher's: a results upload holding
    // both of that one's connections would stall the live feed for minutes.
    var handler = new SocketsHttpHandler
    {
      MaxConnectionsPerServer = 1,
      AutomaticDecompression = DecompressionMethods.All,
      PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    };
    var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
      $"CrossMgrInterface/{AppVersion.Display} (+https://github.com/erSitzt/crossmgr-interface)");
    return client;
  }
}
