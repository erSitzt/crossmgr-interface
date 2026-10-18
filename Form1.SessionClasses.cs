namespace CrossMgrInterface;

/// <summary>
/// A session that counts only some of the rider list's classes - the day's
/// list loaded once, Moto 1 for MX1 and MX2, then Moto 1 for the Youth. See
/// <see cref="SessionClassGate"/> for who is turned away and why, and
/// <see cref="HeldReads"/> for what happens to their reads.
///
/// Its own partial file for the usual reason: Form1.cs is very large and the
/// designer rewrites Form1.Designer.cs wholesale.
/// </summary>
public partial class Form1
{
  /// <summary>The classes the session counts, or null for everyone. Read and written under ridersLock.</summary>
  private IReadOnlyList<string>? sessionClasses;

  /// <summary>Reads of riders of other classes, kept so they can still be counted. Under ridersLock.</summary>
  private readonly HeldReads _heldReads = new();

  /// <summary>Riders of another class the operator said to count in this session anyway. Under ridersLock.</summary>
  private readonly HashSet<string> _countedOtherClass = new(StringComparer.OrdinalIgnoreCase);

  /// <summary>
  /// Set while one session is put away and the next set up. Between the two
  /// the app holds neither session's rules: a read in that moment, with the
  /// clock set to start on the first rider, used to start an unnamed race
  /// under half the old settings. Under ridersLock.
  /// </summary>
  private bool _settingUpSession;

  /// <summary>Holds every read until the returned scope ends. For reset-then-set-up in one go.</summary>
  private IDisposable HoldReadsWhileSettingUp()
  {
    lock (ridersLock) _settingUpSession = true;
    return new Scope(() =>
    {
      lock (ridersLock) _settingUpSession = false;
    });
  }

  private sealed class Scope : IDisposable
  {
    private Action? _end;
    public Scope(Action end) => _end = end;
    public void Dispose() => Interlocked.Exchange(ref _end, null)?.Invoke();
  }

  /// <summary>
  /// The class check, for a read that has got past the ignore list and the
  /// finish. True when the read is not counted; the caller then records it
  /// as a lap-0 read, as it does every other refused read. Under ridersLock.
  /// </summary>
  private bool NotCountedForClass(string entryKey, string crossedBy, DateTime at,
    List<(string message, bool isRaceEvent)> messages, ref string? notice)
  {
    // Every entry of a team event is the team's own; teams are not split by class.
    if (teamEvent) return false;

    var listed = _riderDataImporter.GetRiderData(entryKey);
    var verdict = SessionClassGate.Decide(sessionClasses,
      riders.ContainsKey(entryKey) || _countedOtherClass.Contains(entryKey),
      listed != null, listed?.Category, raceStarted);

    switch (verdict)
    {
      case ClassGateVerdict.UnknownBeforeStart:
        messages.Add(($"⏳ Read not counted: {entryKey} is not on the rider list, and the clock starts " +
                      "only for a rider of this session", false));
        return true;

      case ClassGateVerdict.OtherClass:
        var label = ListedLabel(listed!);
        messages.Add(($"⛔ Not in this session: {label} ({listed!.Category}) - this session counts " +
                      $"{SessionClassGate.Describe(sessionClasses!)}", false));

        var held = _heldReads.Add(entryKey, listed.RiderNumber, listed.FullName, listed.Category, at, crossedBy);
        if (held != null)
          notice = held.ManyRiders
            ? $"Riders of other classes keep crossing - is this the right session? It counts " +
              $"{SessionClassGate.Describe(sessionClasses!)}. Race > Riders from other classes... lists them."
            : $"{label} ({listed.Category}) is going round but is not in this session - not counted. " +
              "Race > Riders from other classes... counts them.";
        return true;

      default:
        return false;
    }
  }

  private static string ListedLabel(RiderDataImporter.RiderImportData listed)
  {
    var name = listed.FullName;
    if (listed.RiderNumber.Length == 0) return name.Length > 0 ? name : listed.TagID;
    return name.Length > 0 ? $"#{listed.RiderNumber} {name}" : $"#{listed.RiderNumber}";
  }

  /// <summary>Riders on the list this session counts: the whole list, or the session's classes.</summary>
  private int SessionRosterCount()
  {
    if (teamEvent) return _teams.EntryCount;

    IReadOnlyList<string>? classes;
    lock (ridersLock) classes = sessionClasses;

    return _riderDataImporter.GetAllRiderData().Values
      .Count(r => SessionClassGate.InSession(classes, r.Category));
  }

  private IReadOnlyList<HeldRider> HeldRiders()
  {
    lock (ridersLock) return _heldReads.Riders();
  }

  /// <summary>
  /// Counts a rider of another class in this session after all, with every
  /// read kept for them, in the order they crossed - as though the class had
  /// been right from the start.
  /// </summary>
  private void CountOtherClassRider(string tag)
  {
    IReadOnlyList<HeldRead> reads;
    lock (ridersLock)
    {
      _countedOtherClass.Add(tag);
      reads = _heldReads.Take(tag);
    }

    // Through the same path a read from the reader takes, so the lap rules,
    // the clock and the database all see them as they would have.
    foreach (var read in reads)
      ProcessRiderCrossing(read.CrossedBy, read.At);

    AddMessage($"✅ {GetRiderDisplayText(tag)} is counted in this session from now on - " +
               $"{reads.Count} read(s) kept for them were counted.");
    _refresh.Invalidate(RaceViewKind.All);
  }

  /// <summary>Race > Riders from other classes...</summary>
  private void ShowHeldRiders()
  {
    using var dialog = new HeldRidersDialog(HeldRiders, CountOtherClassRider, SessionClassesText());
    dialog.ShowDialog(this);
  }

  private string SessionClassesText()
  {
    lock (ridersLock)
      return sessionClasses is { Count: > 0 } classes ? SessionClassGate.Describe(classes) : "";
  }
}
