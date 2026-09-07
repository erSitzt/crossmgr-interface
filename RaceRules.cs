namespace CrossMgrInterface;

/// <summary>
/// The settings a session was scored under, as printed on its sheets.
///
/// A results sheet that says "20 minutes" and nothing else cannot settle a
/// protest about whether the leader owed two more laps or one, or why a read
/// four seconds after the last was thrown away. This carries the rest, from
/// the live fields for the session on screen and from the race row for one
/// reprinted later.
///
/// Nullable members mean "not recorded": sessions stored before this existed
/// have no value for them, and the sheet says so rather than inventing one.
/// </summary>
public sealed class RaceRules
{
  public SessionType SessionType { get; init; }
  public TimeSpan Duration { get; init; }

  /// <summary>Laps the leader rides after the clock, beyond the lap in progress. Races only.</summary>
  public int? AdditionalLaps { get; init; }

  /// <summary>How long a rider has to finish their last lap once the leader is home.</summary>
  public int? DnfTimeoutMinutes { get; init; }

  /// <summary>A read closer than this to the previous one was not counted. Zero means every read counted.</summary>
  public double? MinimumLapSeconds { get; init; }

  /// <summary>The operator pressed Start, rather than the first crossing starting the clock.</summary>
  public bool? ManualStart { get; init; }

  public bool IsTimedSession => SessionType != SessionType.Race;

  public static RaceRules FromRace(DbRace race) => new()
  {
    SessionType = race.SessionType,
    Duration = race.Duration,
    AdditionalLaps = race.AdditionalLaps,
    DnfTimeoutMinutes = race.DnfTimeoutMinutes,
    MinimumLapSeconds = race.MinimumLapSeconds,
    ManualStart = race.ManualStart
  };

  /// <summary>
  /// Caption/value pairs for the block above the table. The scheduled length
  /// is deliberately not here: both sheets already print it beside the start
  /// and end times.
  /// </summary>
  public List<(string Caption, string Value)> Describe()
  {
    var lines = new List<(string, string)>
    {
      ("Session", SessionType switch
      {
        SessionType.TimedQualifying => "Timed qualifying - ranked by best lap",
        SessionType.FreePractice => "Free practice - timed, no ranking",
        _ => "Race - ranked by laps completed, then by time"
      })
    };

    if (IsTimedSession)
    {
      lines.Add(("Clock", "Chequered flag when it runs out - every rider finishes the lap they are on, and it counts"));
      lines.Add(("Grace after flag", DnfTimeoutMinutes.HasValue
        ? $"{Minutes(DnfTimeoutMinutes.Value)}, or 1.5 laps of the field's pace if that is longer"
        : "not recorded"));
    }
    else
    {
      lines.Add(("Extra laps", AdditionalLaps switch
      {
        null => "not recorded",
        0 => "none - the flag comes out when the clock runs out",
        1 => "the leader rides the lap in progress plus 1 more lap after the clock",
        var n => $"the leader rides the lap in progress plus {n} more laps after the clock"
      }));
      lines.Add(("DNF timeout", DnfTimeoutMinutes.HasValue
        ? $"{Minutes(DnfTimeoutMinutes.Value)} after the leader finishes to complete the last lap"
        : "not recorded"));
    }

    lines.Add(("Minimum lap", MinimumLapSeconds switch
    {
      null => "not recorded",
      <= 0 => "off - every read counted as a lap",
      var s => $"{s:0.#} s - a read sooner than that after the previous one was not counted"
    }));

    lines.Add(("Start", ManualStart switch
    {
      null => "not recorded",
      true => "clock started by the operator",
      false => "clock started on the first crossing"
    }));

    return lines;
  }

  private static string Minutes(int minutes) => minutes == 1 ? "1 minute" : $"{minutes} minutes";
}
