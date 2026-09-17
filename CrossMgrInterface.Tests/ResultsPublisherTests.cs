using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// How publishing behaves when the internet does not cooperate - which, at a
/// motocross track, is most of the time.
/// </summary>
public class ResultsPublisherTests
{
  /// <summary>Answers with whatever the test lines up, and remembers what it was asked.</summary>
  private sealed class StubHandler : HttpMessageHandler
  {
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _answers = new();

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<byte[]> Bodies { get; } = new();

    public StubHandler Answer(HttpStatusCode code, string body = "{}")
    {
      _answers.Enqueue(_ => new HttpResponseMessage(code)
      {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
      });
      return this;
    }

    public StubHandler Throws(Exception ex)
    {
      _answers.Enqueue(_ => throw ex);
      return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
      CancellationToken cancellationToken)
    {
      Requests.Add(request);
      Bodies.Add(request.Content == null
        ? Array.Empty<byte>()
        : await request.Content.ReadAsByteArrayAsync(cancellationToken));

      cancellationToken.ThrowIfCancellationRequested();

      if (_answers.Count == 0) return new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent("{}", Encoding.UTF8, "application/json")
      };

      return _answers.Dequeue()(request);
    }
  }

  private static PublishedSession Session()
  {
    var rider = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(3, 40).Build();
    var report = new RaceReportGenerator().PrepareReportData(
      new Dictionary<string, RiderInfo> { [rider.TagID] = rider },
      RiderBuilder.RaceStart, RiderBuilder.RaceStart.AddMinutes(20), TimeSpan.FromMinutes(20),
      true, "Moto 1", rules: new RaceRules());

    return PublishPayloadBuilder.Build(new PublishInputs
    {
      Report = report,
      PublicId = "b3f1c0de0000000000000000000000ff",
      ClientVersion = "v0.12.0"
    });
  }

  private static HttpResultsPublisher Publisher(StubHandler handler, Action<string>? log = null) =>
    new("https://results.example.org", "olt_abcd1234_secret_with_underscores", log, handler);

  private static Task<PublishOutcome> Publish(HttpResultsPublisher publisher) =>
    publisher.PublishAsync(Session(), null, CancellationToken.None);

  [Fact]
  public async Task APublishedSessionComesBackWithItsAddress()
  {
    var handler = new StubHandler().Answer(HttpStatusCode.OK,
      "{\"url\":\"https://results.example.org/races/moto-1\"}");

    var outcome = await Publish(Publisher(handler));

    Assert.True(outcome.Ok);
    Assert.Equal("https://results.example.org/races/moto-1", outcome.Url);
  }

  [Fact]
  public async Task AWrongKeyIsNeverRetried()
  {
    // Retrying teaches the volunteer nothing: it will be just as wrong.
    var handler = new StubHandler().Answer(HttpStatusCode.Unauthorized);

    var outcome = await Publish(Publisher(handler));

    Assert.Equal(PublishStatus.Unauthorized, outcome.Status);
    Assert.Single(handler.Requests);
  }

  [Fact]
  public async Task AWebsiteHavingAMomentIsTriedAgain()
  {
    var handler = new StubHandler()
      .Answer(HttpStatusCode.ServiceUnavailable)
      .Answer(HttpStatusCode.OK, "{\"url\":\"https://results.example.org/races/moto-1\"}");

    var outcome = await Publish(Publisher(handler));

    Assert.True(outcome.Ok);
    Assert.Equal(2, handler.Requests.Count);
  }

  [Fact]
  public async Task AWebsiteThatStaysDownGivesUpAndSaysNothingChanged()
  {
    var handler = new StubHandler()
      .Answer(HttpStatusCode.ServiceUnavailable)
      .Answer(HttpStatusCode.ServiceUnavailable)
      .Answer(HttpStatusCode.ServiceUnavailable);

    var outcome = await Publish(Publisher(handler));

    Assert.Equal(PublishStatus.ServerError, outcome.Status);
    Assert.Equal(3, handler.Requests.Count);
    Assert.Contains("Nothing has been changed", outcome.Message);
  }

  [Fact]
  public async Task NoInternetSaysSoAndPointsAtPublishingLater()
  {
    var handler = new StubHandler()
      .Throws(new HttpRequestException("no route", new SocketException(10051)))
      .Throws(new HttpRequestException("no route", new SocketException(10051)))
      .Throws(new HttpRequestException("no route", new SocketException(10051)));

    var outcome = await Publish(Publisher(handler));

    Assert.Equal(PublishStatus.NoInternet, outcome.Status);
    Assert.Contains("Past sessions", outcome.Message);
  }

  [Fact]
  public async Task StoppingLeavesNothingPublished()
  {
    var handler = new StubHandler();
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();

    var outcome = await Publisher(handler).PublishAsync(Session(), null, cancelled.Token);

    Assert.Equal(PublishStatus.Cancelled, outcome.Status);
    Assert.Empty(handler.Requests);
  }

  [Fact]
  public async Task ASessionIsSentGzippedToItsOwnAddressWithTheKey()
  {
    var handler = new StubHandler().Answer(HttpStatusCode.OK);

    await Publish(Publisher(handler));

    var request = handler.Requests[0];
    Assert.Equal(HttpMethod.Put, request.Method);
    Assert.Contains("b3f1c0de0000000000000000000000ff", request.RequestUri!.ToString());
    Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
    Assert.Contains("gzip", request.Content!.Headers.ContentEncoding);

    using var gzip = new GZipStream(new MemoryStream(handler.Bodies[0]), CompressionMode.Decompress);
    using var reader = new StreamReader(gzip);
    Assert.Contains("Anna Berger", reader.ReadToEnd());
  }

  [Fact]
  public async Task TheKeyNeverReachesTheLog()
  {
    // The log is read out over the phone and pasted into bug reports.
    var written = new List<string>();
    var handler = new StubHandler().Answer(HttpStatusCode.OK);

    await Publish(Publisher(handler, written.Add));

    Assert.NotEmpty(written);
    Assert.DoesNotContain(written, line => line.Contains("secret_with_underscores"));
    Assert.DoesNotContain(written, line => line.Contains("olt_"));
  }

  [Fact]
  public async Task ATestConnectionSaysPlainlyWhetherTheKeyWorks()
  {
    var accepted = new StubHandler().Answer(HttpStatusCode.OK, "{\"key\":\"Trackside laptop\"}");
    var refused = new StubHandler().Answer(HttpStatusCode.Unauthorized);

    Assert.True((await Publisher(accepted).TestAsync(CancellationToken.None)).Ok);

    var outcome = await Publisher(refused).TestAsync(CancellationToken.None);
    Assert.Equal(PublishStatus.Unauthorized, outcome.Status);
  }

  [Fact]
  public async Task AClubThatHasNotSetUpAWebsiteIsToldSoRatherThanFailing()
  {
    var publisher = new HttpResultsPublisher(null, null);

    Assert.False(publisher.IsConfigured);

    var outcome = await publisher.PublishAsync(Session(), null, CancellationToken.None);
    Assert.Equal(PublishStatus.NotConfigured, outcome.Status);
  }
}
