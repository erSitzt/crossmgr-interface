namespace CrossMgrInterface;

/// <summary>What happens to one read when a session counts only some classes.</summary>
public enum ClassGateVerdict
{
  /// <summary>Counted as it always was.</summary>
  Count,

  /// <summary>A rider on the list, in a class this session does not count. Kept aside, not counted.</summary>
  OtherClass,

  /// <summary>A transponder that is on nobody's row, read before the clock runs. Not counted.</summary>
  UnknownBeforeStart
}

/// <summary>
/// Which riders a session counts, when one rider list serves the whole day
/// and each session is for some of its classes - Moto 1 for MX1 and MX2,
/// then Moto 1 for the Youth.
///
/// Deliberately narrow. It only ever turns away a rider the session has not
/// counted yet: once someone is in, a class corrected on the rider list or
/// with Change class must never make the rest of their laps disappear. A
/// listed rider with no class is let in - a missing class is a gap in the
/// list, and a rider dropped for it would be far worse than one too many.
/// A transponder on nobody's row cannot be judged by class at all; it is
/// counted as UNKNOWN as always, but not before the clock runs, so a bike
/// from another group rolling over the loop cannot start this session.
/// </summary>
public static class SessionClassGate
{
  /// <summary>True when the session counts only some classes.</summary>
  public static bool Restricts(IReadOnlyCollection<string>? classes) => classes is { Count: > 0 };

  /// <summary>Whether a rider of <paramref name="riderClass"/> belongs in the session. A rider with no class does.</summary>
  public static bool InSession(IReadOnlyCollection<string>? classes, string? riderClass) =>
    !Restricts(classes) ||
    string.IsNullOrWhiteSpace(riderClass) ||
    classes!.Any(c => string.Equals(c.Trim(), riderClass.Trim(), StringComparison.OrdinalIgnoreCase));

  /// <param name="classes">The classes the session counts; null or empty for all of them.</param>
  /// <param name="alreadyCounted">The entry is already in the session, or the operator said to count it.</param>
  /// <param name="onRiderList">The transponder is on a row of the rider list.</param>
  /// <param name="riderClass">That row's class.</param>
  /// <param name="clockRunning">The session's clock has started.</param>
  public static ClassGateVerdict Decide(IReadOnlyCollection<string>? classes, bool alreadyCounted,
    bool onRiderList, string? riderClass, bool clockRunning)
  {
    if (!Restricts(classes) || alreadyCounted) return ClassGateVerdict.Count;

    if (!onRiderList) return clockRunning ? ClassGateVerdict.Count : ClassGateVerdict.UnknownBeforeStart;

    return InSession(classes, riderClass) ? ClassGateVerdict.Count : ClassGateVerdict.OtherClass;
  }

  /// <summary>"MX1, MX2" - as the sheet and the messages name them.</summary>
  public static string Describe(IReadOnlyCollection<string> classes) => string.Join(", ", classes);
}
