using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>Short codes: a plan code put on the results website, and fetched back by eight characters.</summary>
public class PresetShortCodeTests
{
  private sealed class Handler : HttpMessageHandler
  {
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _answer;
    public Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) => _answer = answer;

    public HttpRequestMessage? Request { get; private set; }
    public byte[] Body { get; private set; } = Array.Empty<byte>();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
      Request = request;
      if (request.Content != null) Body = await request.Content.ReadAsByteArrayAsync(token);
      return _answer(request);
    }
  }

  private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
    new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

  private static readonly string Code = PresetCatalog.All[3].Code;

  [Fact]
  public async Task APresetGoesUpUnderItsShortCodeWithItsName()
  {
    var handler = new Handler(_ => Json(HttpStatusCode.OK, "{\"shortCode\":\"47YK-GW5D\"}"));
    var publisher = new HttpResultsPublisher("https://results.example.org", "olt_abcd1234_secret", null, handler);

    var outcome = await publisher.PublishPresetAsync("Teamrennen Herbst", Code, CancellationToken.None);

    Assert.True(outcome.Ok);
    Assert.Equal(HttpMethod.Put, handler.Request!.Method);
    Assert.Equal("/api/v1/presets/47YK-GW5D", handler.Request.RequestUri!.AbsolutePath);
    Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);

    using var gzip = new GZipStream(new MemoryStream(handler.Body), CompressionMode.Decompress);
    using var json = JsonDocument.Parse(gzip);
    Assert.Equal(new[] { "schemaVersion", "code", "name", "client" }, json.RootElement.EnumerateObject().Select(p => p.Name));
    Assert.Equal(Code, json.RootElement.GetProperty("code").GetString());
    Assert.Equal("Teamrennen Herbst", json.RootElement.GetProperty("name").GetString());
  }

  [Fact]
  public async Task AShortCodeIsFetchedWithoutAKey()
  {
    var handler = new Handler(_ => Json(HttpStatusCode.OK,
      JsonSerializer.Serialize(new { shortCode = "47YK-GW5D", code = Code, name = "Teamrennen Herbst" })));
    var publisher = new HttpResultsPublisher("https://results.example.org", null, null, handler);

    var (code, name, status) = await publisher.FetchPresetAsync("47YK-GW5D", CancellationToken.None);

    Assert.Equal((Code, "Teamrennen Herbst", PublishStatus.Published), (code, name, status));
    Assert.Equal(HttpMethod.Get, handler.Request!.Method);
    Assert.Null(handler.Request.Headers.Authorization);
    Assert.Equal("/api/v1/presets/47YK-GW5D", handler.Request.RequestUri!.AbsolutePath);
  }

  [Fact]
  public async Task AShortCodeTheWebsiteDoesNotKnowIsSaidPlainly()
  {
    var handler = new Handler(_ => Json(HttpStatusCode.NotFound, "{}"));
    var publisher = new HttpResultsPublisher("https://results.example.org", null, null, handler);
    var (code, _, status) = await publisher.FetchPresetAsync("ZZZZ-ZZZZ", CancellationToken.None);
    Assert.Null(code);
    Assert.Equal(PublishStatus.Rejected, status);
  }

  [Fact]
  public async Task NoInternetIsNotMistakenForAnUnknownShortCode()
  {
    var handler = new Handler(_ => throw new HttpRequestException("no route", new System.Net.Sockets.SocketException()));
    var publisher = new HttpResultsPublisher("https://results.example.org", null, null, handler);
    var (_, _, status) = await publisher.FetchPresetAsync("47YK-GW5D", CancellationToken.None);
    Assert.Equal(PublishStatus.NoInternet, status);
  }
}
