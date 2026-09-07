namespace CrossMgrInterface;

/// <summary>One class's place in a staggered start.</summary>
public sealed class StartWave
{
  public string Class { get; init; } = "";

  /// <summary>Gap after the previous wave leaves. Zero for the first wave, which the operator starts.</summary>
  public TimeSpan Delay { get; init; }

  /// <summary>When this class actually left the gate, or null while it is still waiting.</summary>
  public DateTime? StartedAt { get; set; }
}

/// <summary>
/// The order the classes leave the gate in, and how long after each other.
///
/// An enduro starts its classes one after another, a minute or two apart, and
/// every rider is timed from their own class's gate. Nothing in the race
/// engine needs to know about that except this: who has started, and when.
///
/// Delays are counted from the previous wave's actual start, not from a
/// timetable fixed at the first gate. If the operator sends a class early or
/// late, the gap to the next one is what was configured, which is what the
/// starter at the gate is counting too.
/// </summary>
public sealed class WaveSchedule
{
  private readonly List<StartWave> _waves;

  public IReadOnlyList<StartWave> Waves => _waves;

  public WaveSchedule(IEnumerable<StartWave> waves)
  {
    _waves = waves.ToList();
    if (_waves.Count == 0)
      throw new ArgumentException("A wave schedule needs at least one class.", nameof(waves));
  }

  /// <summary>Classes in the given order, the same gap between each pair.</summary>
  public static WaveSchedule Build(IEnumerable<string> classes, TimeSpan gap)
  {
    var list = classes.Select((c, i) => new StartWave { Class = c, Delay = i == 0 ? TimeSpan.Zero : gap });
    return new WaveSchedule(list);
  }

  /// <summary>From setup, or null when the race is not staggered.</summary>
  public static WaveSchedule? From(IReadOnlyList<(string Class, TimeSpan Delay)>? delays)
  {
    if (delays == null || delays.Count == 0) return null;
    return new WaveSchedule(delays.Select((d, i) => new StartWave
    {
      Class = d.Class,
      Delay = i == 0 ? TimeSpan.Zero : d.Delay
    }));
  }

  public StartWave First => _waves[0];

  /// <summary>The wave waiting to go, or null once every class is away.</summary>
  public StartWave? Next => _waves.FirstOrDefault(w => w.StartedAt == null);

  public bool AllStarted => Next == null;

  /// <summary>
  /// The wave a rider belongs to. A class not in the schedule - or a rider with
  /// no class, which is what an unidentified transponder is - goes with the
  /// first wave: they are timed from the first gate, and counted rather than
  /// ignored.
  /// </summary>
  public StartWave WaveFor(string? category)
  {
    if (string.IsNullOrWhiteSpace(category)) return First;
    return _waves.FirstOrDefault(w => string.Equals(w.Class, category, StringComparison.OrdinalIgnoreCase)) ?? First;
  }

  public bool HasStarted(string? category) => WaveFor(category).StartedAt != null;

  public DateTime? StartTimeFor(string? category) => WaveFor(category).StartedAt;

  /// <summary>
  /// When a wave is due: its delay after the previous wave's actual start.
  /// Null for the first wave until the operator starts it, and for any wave
  /// whose predecessor has not gone.
  /// </summary>
  public DateTime? DueAt(StartWave wave)
  {
    var index = _waves.IndexOf(wave);
    if (index < 0) throw new ArgumentException("Not a wave of this schedule.", nameof(wave));
    if (index == 0) return wave.StartedAt;

    var previous = _waves[index - 1].StartedAt;
    return previous.HasValue ? previous.Value + wave.Delay : null;
  }

  /// <summary>The next wave if its time has come, else null.</summary>
  public StartWave? NextDue(DateTime now)
  {
    var next = Next;
    if (next == null) return null;
    var due = DueAt(next);
    return due.HasValue && due.Value <= now ? next : null;
  }

  /// <summary>Time until the next wave is due, clamped at zero; null when nothing is pending or scheduled.</summary>
  public TimeSpan? Countdown(DateTime now)
  {
    var next = Next;
    if (next == null) return null;
    var due = DueAt(next);
    if (!due.HasValue) return null;
    var left = due.Value - now;
    return left > TimeSpan.Zero ? left : TimeSpan.Zero;
  }

  /// <summary>
  /// Records a wave leaving. Only the next wave in order may go: starting MX3
  /// while MX2 is still on the line would leave MX2's riders timed from
  /// nothing.
  /// </summary>
  public void Start(StartWave wave, DateTime at)
  {
    if (!ReferenceEquals(wave, Next))
      throw new InvalidOperationException($"{wave.Class} is not the next wave to start.");
    wave.StartedAt = at;
  }

  /// <summary>Forgets the actual starts. The schedule is setup; the starts are session state.</summary>
  public void ResetStarts()
  {
    foreach (var wave in _waves) wave.StartedAt = null;
  }

  /// <summary>The schedule as configured: "MX1 at the gate, MX2 +1:00, MX3 +1:00".</summary>
  public string Describe() =>
    string.Join(", ", _waves.Select((w, i) => i == 0 ? $"{w.Class} at the gate" : $"{w.Class} {FormatDelay(w.Delay)}"));

  /// <summary>What actually happened: "MX1 10:00:00, MX2 10:01:02, MX3 not started".</summary>
  public string DescribeStarts() =>
    string.Join(", ", _waves.Select(w => w.StartedAt.HasValue ? $"{w.Class} {w.StartedAt.Value:HH:mm:ss}" : $"{w.Class} not started"));

  public static string FormatDelay(TimeSpan delay) =>
    delay.TotalHours >= 1 ? $"+{delay:h\\:mm\\:ss}" : $"+{delay:m\\:ss}";

  // ---- Persistence ---------------------------------------------------------

  public List<DbStartWave> ToRecords() => _waves.Select(w => new DbStartWave
  {
    Class = w.Class,
    DelaySeconds = w.Delay.TotalSeconds,
    StartedAt = w.StartedAt
  }).ToList();

  public static WaveSchedule? FromRecords(IReadOnlyList<DbStartWave>? records)
  {
    if (records == null || records.Count == 0) return null;
    return new WaveSchedule(records.Select(r => new StartWave
    {
      Class = r.Class,
      Delay = TimeSpan.FromSeconds(r.DelaySeconds),
      StartedAt = r.StartedAt
    }));
  }
}
