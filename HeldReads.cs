namespace CrossMgrInterface;

/// <summary>A rider of another class whose reads were kept aside, as the window lists them.</summary>
public sealed record HeldRider(string Tag, string Number, string Name, string Class, int Reads, DateTime LastRead);

/// <summary>One read kept aside: when, and which transponder was actually read.</summary>
public readonly record struct HeldRead(DateTime At, string CrossedBy);

/// <summary>What to tell the operator about a rider of another class who keeps crossing.</summary>
public sealed record HeldReadNotice(string Tag, bool ManyRiders);

/// <summary>
/// The reads of riders a session does not count because of their class (see
/// <see cref="SessionClassGate"/>), kept rather than thrown away.
///
/// Kept because the rider list can be wrong: a Youth rider entered as Youth
/// who is really riding with MX2 would otherwise lose every lap of the moto,
/// with nothing left to put back. With the reads here, "Count in this
/// session" replays them in order and the rider has all their laps.
///
/// It also decides when to say something. A rider rolling over the loop on
/// the way to the gate crosses once and is no business of the operator's; a
/// rider read again a lap later is riding in this session, and that is worth
/// a warning. When several are, it is more likely the wrong session was set
/// up, and one warning saying so replaces a warning per rider.
///
/// Not thread-safe: Form1 uses it under ridersLock.
/// </summary>
public sealed class HeldReads
{
  /// <summary>Two reads this far apart are a rider going round, not one rolling to the gate.</summary>
  public static readonly TimeSpan LappingGap = TimeSpan.FromSeconds(60);

  /// <summary>This many riders of other classes going round means the session itself may be the wrong one.</summary>
  public const int ManyRiders = 5;

  private sealed class Entry
  {
    public required string Number { get; init; }
    public required string Name { get; init; }
    public required string Class { get; init; }
    public List<HeldRead> Reads { get; } = new();
    public bool Lapping { get; set; }
  }

  private readonly Dictionary<string, Entry> _byTag = new(StringComparer.OrdinalIgnoreCase);
  private bool _toldAboutMany;

  public bool Any => _byTag.Count > 0;

  /// <summary>Keeps a read. Returns something to tell the operator, at most once per rider.</summary>
  public HeldReadNotice? Add(string tag, string number, string name, string riderClass, DateTime at, string crossedBy)
  {
    if (!_byTag.TryGetValue(tag, out var entry))
    {
      entry = new Entry { Number = number, Name = name, Class = riderClass };
      _byTag[tag] = entry;
    }

    entry.Reads.Add(new HeldRead(at, crossedBy));
    if (entry.Lapping || at - entry.Reads.Min(r => r.At) < LappingGap) return null;

    entry.Lapping = true;
    if (_toldAboutMany) return null;

    if (_byTag.Values.Count(e => e.Lapping) >= ManyRiders)
    {
      _toldAboutMany = true;
      return new HeldReadNotice(tag, ManyRiders: true);
    }

    return new HeldReadNotice(tag, ManyRiders: false);
  }

  /// <summary>Every rider with reads kept aside, most recently read first.</summary>
  public IReadOnlyList<HeldRider> Riders() =>
    _byTag.Select(p => new HeldRider(p.Key, p.Value.Number, p.Value.Name, p.Value.Class,
        p.Value.Reads.Count, p.Value.Reads.Max(r => r.At)))
      .OrderByDescending(r => r.LastRead)
      .ToList();

  /// <summary>Takes a rider's reads out, oldest first, to be counted after all.</summary>
  public IReadOnlyList<HeldRead> Take(string tag)
  {
    if (!_byTag.Remove(tag, out var entry)) return Array.Empty<HeldRead>();
    return entry.Reads.OrderBy(r => r.At).ToList();
  }

  public void Clear()
  {
    _byTag.Clear();
    _toldAboutMany = false;
  }
}
