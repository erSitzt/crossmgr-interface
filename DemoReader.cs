using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CrossMgrInterface;

/// <summary>
/// A transponder reader that exists only in software, for the demos.
///
/// It connects to the application's own reader port and speaks the same
/// CrossMgr/JChip protocol a real reader forwarder does - the handshake, then
/// one DA line per crossing - so everything a demo shows comes through the
/// path a race day uses: the reader tile, the tag log, the filters, the lap
/// rules. Nothing in the timing code knows a demo is running.
///
/// Every read carries the moment it was planned for rather than the moment it
/// went out, as the Python harnesses do, so lap times come out exactly as the
/// scenario wrote them. The application measures time expiry against its own
/// clock, so the reads are sent in real time, never ahead of it.
/// </summary>
public sealed class DemoReader
{
  public const string ReaderName = "DemoReader";

  private readonly int _port;
  private readonly IReadOnlyList<DemoCrossing> _crossings;
  private readonly Func<string, DateTime?> _waveStartedAt;
  private readonly Func<bool> _finished;

  /// <summary>From the end of the handshake to the reader's own start, the moment un-waved reads count from.</summary>
  public TimeSpan Lead { get; init; } = TimeSpan.FromSeconds(3);

  /// <summary>How often the reader looks for reads that have come due.</summary>
  public TimeSpan Tick { get; init; } = TimeSpan.FromMilliseconds(200);

  /// <summary>Raised once the handshake is done, off the UI thread.</summary>
  public event Action? Connected;

  /// <param name="waveStartedAt">When a class left the gate, or null while it is still waiting.</param>
  /// <param name="finished">True once the session is over and nothing more would count.</param>
  public DemoReader(int port, IReadOnlyList<DemoCrossing> crossings,
    Func<string, DateTime?> waveStartedAt, Func<bool> finished)
  {
    _port = port;
    _crossings = crossings;
    _waveStartedAt = waveStartedAt;
    _finished = finished;
  }

  /// <summary>
  /// Connects, sends every read as it comes due, then stays connected - so the
  /// reader shows as connected until the demo window closes - until cancelled.
  /// </summary>
  public async Task RunAsync(CancellationToken cancel)
  {
    using var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, _port, cancel);
    var stream = client.GetStream();
    var incoming = new StringBuilder();

    await SendAsync(stream, $"N0001{ReaderName}\r", cancel);
    await ReadLineAsync(stream, incoming, cancel);   // GT: the application asks for our clock

    var now = DateTime.Now;
    await SendAsync(stream, $"GT{now.ToString("HHmmssfff", CultureInfo.InvariantCulture)} " +
                            $"date={now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}\r", cancel);
    await ReadLineAsync(stream, incoming, cancel);   // S0000: ready for reads

    Connected?.Invoke();

    var start = DateTime.Now + Lead;
    var pending = _crossings.ToList();
    var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    while (pending.Count > 0 && !_finished())
    {
      now = DateTime.Now;
      var due = new List<(DateTime At, DemoCrossing Crossing)>();

      foreach (var crossing in pending)
      {
        var anchor = crossing.AfterWaveOf == null ? start : _waveStartedAt(crossing.AfterWaveOf);
        if (anchor is not { } from) continue;

        var at = from + crossing.At;
        if (at <= now) due.Add((at, crossing));
      }

      foreach (var (at, crossing) in due.OrderBy(d => d.At))
      {
        // A pass read twice goes out with the count it already had, which is
        // what a tag seen twice in one pass looks like on the wire.
        counts.TryGetValue(crossing.Tag, out var count);
        if (crossing.Kind != DemoReadKind.TooSoon || count == 0) counts[crossing.Tag] = ++count;

        await SendAsync(stream, FormatRead(crossing.Tag, at, count), cancel);
        pending.Remove(crossing);
      }

      await Task.Delay(Tick, cancel);
    }

    await Task.Delay(Timeout.Infinite, cancel);
  }

  /// <summary>One tag read as a reader forwarder sends it: <c>DA{tag} {HH:mm:ss.fff} 10 {count} C7 date={yyyyMMdd}</c>.</summary>
  public static string FormatRead(string tag, DateTime at, int count) =>
    $"DA{tag} {at.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)} 10 " +
    $"{count.ToString("00000", CultureInfo.InvariantCulture)} C7 " +
    $"date={at.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}\r";

  private static async Task SendAsync(NetworkStream stream, string line, CancellationToken cancel)
  {
    var bytes = Encoding.ASCII.GetBytes(line);
    await stream.WriteAsync(bytes, cancel);
  }

  /// <summary>
  /// The next line up to a CR or LF. A read from the socket can hold half a
  /// line or two of them, so whatever follows the line stays in
  /// <paramref name="buffer"/> for the next call.
  /// </summary>
  private static async Task<string> ReadLineAsync(NetworkStream stream, StringBuilder buffer, CancellationToken cancel)
  {
    var chunk = new byte[256];
    while (true)
    {
      var text = buffer.ToString();
      var end = text.IndexOfAny(new[] { '\r', '\n' });
      if (end >= 0)
      {
        buffer.Remove(0, end + 1);
        if (end == 0) continue;
        return text[..end];
      }

      var read = await stream.ReadAsync(chunk, cancel);
      if (read == 0) throw new IOException("The application closed the reader connection.");
      buffer.Append(Encoding.ASCII.GetString(chunk, 0, read));
    }
  }
}
