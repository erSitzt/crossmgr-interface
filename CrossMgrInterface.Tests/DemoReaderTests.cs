using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Xunit;

namespace CrossMgrInterface.Tests;

public class DemoReaderTests
{
  private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
  private static readonly TimeSpan Quick = TimeSpan.FromMilliseconds(20);

  [Fact]
  public void AReadIsTheLineAReaderForwarderSends()
  {
    var line = DemoReader.FormatRead("20260007", new DateTime(2026, 9, 14, 9, 5, 7, 42), 3);

    Assert.Equal("DA20260007 09:05:07.042 10 00003 C7 date=20260914\r", line);
  }

  [Fact]
  public async Task ItShakesHandsThenSendsEachReadStampedWithItsPlannedMoment()
  {
    using var app = new FakeApp();
    var reader = new DemoReader(app.Port, new[]
    {
      new DemoCrossing(TimeSpan.FromMilliseconds(100), "A"),
      new DemoCrossing(TimeSpan.FromMilliseconds(250), "B"),
      new DemoCrossing(TimeSpan.FromMilliseconds(400), "A")
    }, _ => null, () => false) { Lead = TimeSpan.Zero, Tick = Quick };

    using var stop = new CancellationTokenSource();
    var running = reader.RunAsync(stop.Token);

    var (name, clock) = await app.HandshakeAsync();
    var reads = new List<string>();
    for (var i = 0; i < 3; i++) reads.Add(await app.NextLineAsync(Patience));

    Assert.Equal("N0001DemoReader", name);
    Assert.Matches(@"^GT\d{9} date=\d{8}$", clock);
    Assert.Equal(new[] { "A", "B", "A" }, reads.Select(TagOf));
    Assert.Equal(new[] { "00001", "00001", "00002" }, reads.Select(r => r.Split(' ')[3]));
    Assert.Equal(TimeSpan.FromMilliseconds(150), TimeOf(reads[1]) - TimeOf(reads[0]));
    Assert.Equal(TimeSpan.FromMilliseconds(300), TimeOf(reads[2]) - TimeOf(reads[0]));

    stop.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
  }

  [Fact]
  public async Task AReadForAClassWaitsForThatClassToLeaveTheGate()
  {
    using var app = new FakeApp();
    DateTime? mx2Away = null;
    var reader = new DemoReader(app.Port,
      new[] { new DemoCrossing(TimeSpan.FromMilliseconds(50), "B", AfterWaveOf: "MX2") },
      cls => cls == "MX2" ? mx2Away : null, () => false) { Lead = TimeSpan.Zero, Tick = Quick };

    using var stop = new CancellationTokenSource();
    var running = reader.RunAsync(stop.Token);
    await app.HandshakeAsync();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.NextLineAsync(TimeSpan.FromMilliseconds(300)));

    var away = DateTime.Now;
    mx2Away = away;
    var read = await app.NextLineAsync(Patience);

    Assert.Equal((away + TimeSpan.FromMilliseconds(50)).ToString("HH:mm:ss.fff"), read.Split(' ')[1]);

    stop.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
  }

  [Fact]
  public async Task InARaceStartedByHandAReadWaitsForStartRace()
  {
    using var app = new FakeApp();
    DateTime? started = null;
    var reader = new DemoReader(app.Port,
      new[] { new DemoCrossing(TimeSpan.FromMilliseconds(50), "A") },
      cls => cls == null ? started : throw new InvalidOperationException($"asked for the class {cls}"),
      () => false) { Lead = TimeSpan.Zero, Tick = Quick, AfterTheStart = true };

    using var stop = new CancellationTokenSource();
    var running = reader.RunAsync(stop.Token);
    await app.HandshakeAsync();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.NextLineAsync(TimeSpan.FromMilliseconds(300)));

    var pressed = DateTime.Now;
    started = pressed;
    var read = await app.NextLineAsync(Patience);

    Assert.Equal((pressed + TimeSpan.FromMilliseconds(50)).ToString("HH:mm:ss.fff"), read.Split(' ')[1]);

    stop.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
  }

  [Fact]
  public async Task APassReadTwiceGoesOutWithTheCountItAlreadyHad()
  {
    using var app = new FakeApp();
    var reader = new DemoReader(app.Port, new[]
    {
      new DemoCrossing(TimeSpan.FromMilliseconds(50), "A"),
      new DemoCrossing(TimeSpan.FromMilliseconds(100), "A", DemoReadKind.TooSoon),
      new DemoCrossing(TimeSpan.FromMilliseconds(200), "A")
    }, _ => null, () => false) { Lead = TimeSpan.Zero, Tick = Quick };

    using var stop = new CancellationTokenSource();
    var running = reader.RunAsync(stop.Token);
    await app.HandshakeAsync();

    var counts = new List<string>();
    for (var i = 0; i < 3; i++) counts.Add((await app.NextLineAsync(Patience)).Split(' ')[3]);

    Assert.Equal(new[] { "00001", "00001", "00002" }, counts);

    stop.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
  }

  [Fact]
  public async Task ItStopsSendingOnceTheSessionIsOverButStaysConnected()
  {
    using var app = new FakeApp();
    var over = false;
    var reader = new DemoReader(app.Port, new[]
    {
      new DemoCrossing(TimeSpan.FromMilliseconds(50), "A"),
      new DemoCrossing(TimeSpan.FromMilliseconds(500), "A")
    }, _ => null, () => over) { Lead = TimeSpan.Zero, Tick = Quick };

    using var stop = new CancellationTokenSource();
    var running = reader.RunAsync(stop.Token);
    await app.HandshakeAsync();

    await app.NextLineAsync(Patience);
    over = true;

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.NextLineAsync(TimeSpan.FromMilliseconds(900)));
    Assert.False(running.IsCompleted);

    stop.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
  }

  private static string TagOf(string read) => read.Split(' ')[0][2..];

  private static TimeSpan TimeOf(string read) => TimeSpan.ParseExact(read.Split(' ')[1], @"hh\:mm\:ss\.fff", null);

  /// <summary>The application's end of the reader connection, as much of it as a reader needs.</summary>
  private sealed class FakeApp : IDisposable
  {
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();
    private TcpClient? _client;

    public FakeApp() => _listener.Start();

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Accepts the reader and plays the application's half of the handshake.</summary>
    public async Task<(string Name, string Clock)> HandshakeAsync()
    {
      using var timeout = new CancellationTokenSource(Patience);
      _client = await _listener.AcceptTcpClientAsync(timeout.Token);
      var stream = _client.GetStream();
      _ = PumpAsync(stream);

      var name = await NextLineAsync(Patience);
      await stream.WriteAsync(Encoding.ASCII.GetBytes("GT\r"));
      var clock = await NextLineAsync(Patience);
      await stream.WriteAsync(Encoding.ASCII.GetBytes("S0000\r"));
      return (name, clock);
    }

    public async Task<string> NextLineAsync(TimeSpan timeout)
    {
      using var cancel = new CancellationTokenSource(timeout);
      return await _lines.Reader.ReadAsync(cancel.Token);
    }

    /// <summary>Reads lines off the socket into a queue, so waiting for one can time out without breaking the socket.</summary>
    private async Task PumpAsync(NetworkStream stream)
    {
      var text = new StringBuilder();
      var chunk = new byte[512];
      try
      {
        int read;
        while ((read = await stream.ReadAsync(chunk)) > 0)
        {
          text.Append(Encoding.ASCII.GetString(chunk, 0, read));
          int end;
          while ((end = text.ToString().IndexOf('\r')) >= 0)
          {
            _lines.Writer.TryWrite(text.ToString(0, end));
            text.Remove(0, end + 1);
          }
        }
      }
      catch (IOException)
      {
      }
      catch (ObjectDisposedException)
      {
      }
    }

    public void Dispose()
    {
      _client?.Dispose();
      _listener.Stop();
    }
  }
}
