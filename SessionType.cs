namespace CrossMgrInterface;

/// <summary>
/// What kind of session the clock is timing.
///
/// A race is scored on laps completed and finishes on a laps target. The two
/// practice formats are scored on the clock: when it runs out the flag comes
/// out, every rider finishes the lap they are on, and that lap counts. Only
/// timed qualifying derives a gate pick order from it.
/// </summary>
public enum SessionType
{
  /// <summary>
  /// Scored on laps, finishes on a laps target.
  ///
  /// MUST be 0. LiteDB is schemaless and System.Text.Json leaves a missing
  /// property at the CLR default, so every DbRace document and every settings
  /// file written before this type existed reads back as this member. All of
  /// them were races. Making a practice format the zero value would bring a
  /// crash-recovered race back under the wrong finishing rules.
  /// </summary>
  Race = 0,

  /// <summary>Timed, but no timing sheet comes out of it.</summary>
  FreePractice = 1,

  /// <summary>Timed, and the gate pick order is derived from the best laps.</summary>
  TimedQualifying = 2
}
