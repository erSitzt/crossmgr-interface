using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// The live publisher's temperament: one try, quickly, and the next tick is
/// the retry. The opposite of the results publisher, on purpose.
/// </summary>
public class LiveTimingPublisherTests
{
  private sealed class StubHandler : HttpMessageHandler
  {
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _answers = new();
    public List<HttpRequestMessage> Requests { get; } = new();
    public List<byte[]> Bodies { get; } = new();

    public StubHandler Answer(HttpStatusCode code)
    {
      _answers.Enqueue(_ => new HttpResponseMessage(code)
      {
        Content = new StringContent("{}", Encoding.UTF8, "application/json")
      });
      return this;
    }

    public StubHandler Throws(Exception ex) { _answers.Enqueue(_ => throw ex); return this; }

    /// <summary>Never answers - what a dead hotspot looks like.</summary>
    public StubHandler Hangs() { _answers.Enqueue(_ => throw new HangException()); return this; }

    private sealed class HangException : Exception { }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
      Requests.Add(request);
      Bodies.Add(request.Content == null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(ct));

      var answer = _answers.Count == 0 ? (Func<HttpRequestMessage, HttpResponseMessage>)(_ =>
        new HttpResponseMessage(HttpStatusCode.OK)) : _answers.Dequeue();

      try { return answer(request); }
      catch (HangException)
      {
        await Task.Delay(Timeout.Infinite, ct);
        throw;
      }
    }
  }

  private static LiveSnapshot Snapshot() =>
    LiveSnapshotBuilder.Build(new LiveInputs
    {
      PublicId = "b3f1c0de0000000000000000000000ff",
      Title = "Moto 1",
      State = RaceDayState.Running,
      StartedAt = RiderBuilder.RaceStart,
      Duration = TimeSpan.FromMinutes(20),
      Now = RiderBuilder.RaceStart.AddMinutes(5),
      Riders = new[] { LiveCapture.Of(RiderBuilder.Rider("A", "1", "Anna Berger").Laps(3, 40).Build()) },
      ClientVersion = "test"
    });

  private static HttpLiveTimingPublisher Publisher(StubHandler handler, Action<string>? log = null,
                                                   TimeSpan? timeout = null) =>
    new("https://live.example.org", "olt_abcd1234_secret_with_underscores", log, handler, timeout);

  [Fact]
  public async Task AWebsiteHavingAMomentIsNotTriedAgainUntilTheNextTick()
  {
    var handler = new StubHandler().Answer(HttpStatusCode.ServiceUnavailable);

    var outcome = await Publisher(handler).PushAsync(Snapshot(), CancellationToken.None);

    Assert.Equal(LiveStatus.ServerError, outcome.Status);
    Assert.Single(handler.Requests);
  }

  [Theory]
  [InlineData(HttpStatusCode.Unauthorized, LiveStatus.Unauthorized)]
  [InlineData(HttpStatusCode.TooManyRequests, LiveStatus.RateLimited)]
  [InlineData(HttpStatusCode.UnprocessableEntity, LiveStatus.Rejected)]
  [InlineData(HttpStatusCode.OK, LiveStatus.Sent)]
  public async Task EachAnswerHasItsOwnName(HttpStatusCode code, LiveStatus expected)
  {
    var handler = new StubHandler().Answer(code);

    Assert.Equal(expected, (await Publisher(handler).PushAsync(Snapshot(), CancellationToken.None)).Status);
  }

  [Fact]
  public async Task ADeadHotspotGivesUpAfterTheTimeoutNotAfterMinutes()
  {
    var handler = new StubHandler().Hangs();
    var started = DateTime.UtcNow;

    var outcome = await Publisher(handler, timeout: TimeSpan.FromMilliseconds(300))
      .PushAsync(Snapshot(), CancellationToken.None);

    Assert.Equal(LiveStatus.TimedOut, outcome.Status);
    Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
  }

  [Fact]
  public async Task NoInternetIsRecognisedAsSuch()
  {
    var handler = new StubHandler().Throws(new HttpRequestException("no route", new SocketException(10051)));

    Assert.Equal(LiveStatus.NoInternet, (await Publisher(handler).PushAsync(Snapshot(), CancellationToken.None)).Status);
  }

  [Fact]
  public async Task TheUpdateGoesGzippedToTheLiveAddressWithTheKey()
  {
    var handler = new StubHandler().Answer(HttpStatusCode.OK);

    await Publisher(handler).PushAsync(Snapshot(), CancellationToken.None);

    var request = handler.Requests.Single();
    Assert.Equal(HttpMethod.Put, request.Method);
    Assert.EndsWith("/api/v1/live/b3f1c0de0000000000000000000000ff", request.RequestUri!.AbsolutePath);
    Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
    Assert.Contains("gzip", request.Content!.Headers.ContentEncoding);

    using var gzip = new GZipStream(new MemoryStream(handler.Bodies[0]), CompressionMode.Decompress);
    Assert.Contains("Anna Berger", new StreamReader(gzip).ReadToEnd());
  }

  [Fact]
  public async Task TheKeyNeverReachesTheLog()
  {
    var written = new List<string>();
    var handler = new StubHandler().Answer(HttpStatusCode.OK);

    await Publisher(handler, written.Add).TestAsync(CancellationToken.None);

    Assert.NotEmpty(written);
    Assert.DoesNotContain(written, l => l.Contains("secret_with_underscores") || l.Contains("olt_"));
  }

  [Fact]
  public async Task NotSetUpMeansNoRequestAtAll()
  {
    var handler = new StubHandler();
    var publisher = new HttpLiveTimingPublisher(null, null, handler: handler);

    Assert.False(publisher.IsConfigured);
    Assert.Equal(LiveStatus.NotConfigured, (await publisher.PushAsync(Snapshot(), CancellationToken.None)).Status);
    Assert.Empty(handler.Requests);
  }

  [Theory]
  [InlineData("http://localhost:8000", true)]
  [InlineData("http://127.0.0.1", true)]
  [InlineData("https://livetiming.openlaptime.de", false)]
  [InlineData("not a url", false)]
  [InlineData(null, false)]
  public void OnlyThisComputerCountsAsLoopback(string? url, bool expected)
  {
    Assert.Equal(expected, HttpLiveTimingPublisher.IsLoopback(url));
  }
}
