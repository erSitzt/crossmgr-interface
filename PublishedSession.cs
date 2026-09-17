using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrossMgrInterface;

/// <summary>
/// What the results website is told about a session.
///
/// Deliberately its own set of records rather than RaceReportData, which is a
/// *print* model: it carries PrintLines, MemberLineShort and DisplayName, all of
/// them decisions about a sheet of paper. Serialising it would make the wire
/// format hostage to a layout change, and would mean nobody could say what the
/// website is sent without reading the printing code. One place decides that,
/// and this is it.
///
/// The figures themselves still come from PrepareReportData, the same step both
/// sheets are built from, so the website cannot disagree with what the club
/// handed out at the meeting.
/// </summary>
public static class PublishSchema
{
  /// <summary>
  /// Raised when the shape changes in a way an older server would misread.
  /// The server refuses a version it does not know rather than guessing.
  /// </summary>
  public const int Version = 1;

  /// <summary>
  /// camelCase because the receiving end is Python; nulls dropped because a
  /// 250-rider enduro is mostly empty optional fields, and leaving them out is
  /// worth about a sixth of the payload.
  /// </summary>
  public static readonly JsonSerializerOptions Json = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false
  };
}

public sealed record PublishedSession
{
  public int SchemaVersion { get; init; } = PublishSchema.Version;
  public required PublishedSessionHeader Session { get; init; }
  public PublishedTrack? Track { get; init; }
  public required PublishedStatistics Statistics { get; init; }
  public required IReadOnlyList<PublishedEntry> Entries { get; init; }

  /// <summary>Gate pick order. Only for timed qualifying; null otherwise.</summary>
  public IReadOnlyList<PublishedGatePick>? GatePick { get; init; }

  public required PublishedClient Client { get; init; }
}

public sealed record PublishedSessionHeader
{
  /// <summary>See <see cref="DbRace.PublicId"/>. The website keys on this.</summary>
  public required string PublicId { get; init; }

  public required string Title { get; init; }

  /// <summary>"race", "freePractice" or "timedQualifying".</summary>
  public required string Type { get; init; }

  /// <summary>
  /// Carried with their offset rather than as bare local times. The application
  /// records DateTime.Now, which says nothing about where the club is; a server
  /// in another country would otherwise have to be told separately, and would
  /// get it wrong the one time it was not.
  /// </summary>
  public DateTimeOffset? StartedAt { get; init; }
  public DateTimeOffset? EndedAt { get; init; }

  public long ScheduledDurationMs { get; init; }

  /// <summary>
  /// False while a session is still running. Nothing here assumes true - the
  /// button that sends it does. See PublishPayloadBuilder.
  /// </summary>
  public bool Finished { get; init; }

  public bool TeamEvent { get; init; }
  public DateTimeOffset GeneratedAt { get; init; }

  /// <summary>
  /// What the session was scored under, straight from RaceRules.Describe() -
  /// the same block both sheets print. Passed through as written so there is
  /// never a second wording to keep in step with the first.
  /// </summary>
  public required IReadOnlyList<PublishedCondition> Conditions { get; init; }
}

public sealed record PublishedCondition(string Caption, string Value);

/// <summary>
/// The circuit, thinned for the web: coordinate pairs rather than objects, and
/// six decimal places, which is about 11 cm - finer than the survey behind it.
///
/// The reference image is never included. It is up to three megabytes of base64,
/// the website draws map tiles and the loop instead, and it is usually a
/// screenshot of somebody else's map, which is not ours to republish.
/// </summary>
public sealed record PublishedTrack
{
  public required string Id { get; init; }
  public required string Name { get; init; }
  public double LengthMetres { get; init; }

  /// <summary>The loop as [lat, lon] pairs, in order, not closed by a repeat.</summary>
  public required IReadOnlyList<double[]> Points { get; init; }

  /// <summary>[lat, lon] of the timing loop, or null when it was never placed.</summary>
  public double[]? StartFinish { get; init; }

  public IReadOnlyList<PublishedSector>? Sectors { get; init; }
}

public sealed record PublishedSector
{
  public required string Name { get; init; }

  /// <summary>Where the sector starts, as a fraction of the way round.</summary>
  public double Fraction { get; init; }

  /// <summary>"#RRGGBB". The colour the circuit is drawn in on the Track tab.</summary>
  public required string Colour { get; init; }
}

public sealed record PublishedStatistics
{
  public int Riders { get; init; }
  public int Finished { get; init; }
  public int Dnf { get; init; }
  public int Dns { get; init; }
  public int LapsCompleted { get; init; }

  /// <summary>The winner's elapsed time. Called "Winning Time" on the sheet.</summary>
  public long? WinningTimeMs { get; init; }

  public PublishedFastestLap? FastestLap { get; init; }

  /// <summary>Extra laps ridden after the clock. Races only.</summary>
  public int AdditionalLaps { get; init; }
}

public sealed record PublishedFastestLap(long Ms, string By, string Number);

public sealed record PublishedEntry
{
  /// <summary>
  /// The finishing position as a number, or null for a rider who did not
  /// finish or did not start. Sent beside the printed Position string so the
  /// website never has to parse "DNF" as a number.
  /// </summary>
  public int? Rank { get; init; }

  /// <summary>Exactly what the sheet prints: "1", "DNF" or "DNS".</summary>
  public required string Position { get; init; }

  /// <summary>"finished", "dnf" or "dns".</summary>
  public required string Status { get; init; }

  public required string Number { get; init; }
  public required string Name { get; init; }
  public string? Team { get; init; }
  public string? Category { get; init; }
  public string? Machine { get; init; }

  public bool IsTeam { get; init; }

  /// <summary>A team's riders, as the sheet names them. Null for a solo rider.</summary>
  public IReadOnlyList<string>? Members { get; init; }

  public int Laps { get; init; }
  public long TotalTimeMs { get; init; }
  public long? BestLapMs { get; init; }
  public long? AverageLapMs { get; init; }

  /// <summary>Which member of a team set its best lap, when that is known.</summary>
  public string? BestLapBy { get; init; }

  public long? GapToLeaderMs { get; init; }
  public int LapsDownToLeader { get; init; }

  public required IReadOnlyList<PublishedLap> LapTimes { get; init; }

  /// <summary>Who rode a team's laps and how fast. Null for a solo rider.</summary>
  public IReadOnlyList<PublishedMember>? MemberBreakdown { get; init; }
}

public sealed record PublishedLap
{
  public int Lap { get; init; }

  /// <summary>Null on lap 1, which runs from the start and is not a lap time.</summary>
  public long? TimeMs { get; init; }

  /// <summary>
  /// Milliseconds from this entry's own start to the crossing that ended the
  /// lap - their own gate, not the clock, so an enduro started in waves charts
  /// correctly. A wall-clock time is not sent: it would be a record of where a
  /// named person was all afternoon, and nothing on the page needs one.
  /// </summary>
  public long AtMs { get; init; }

  /// <summary>Position once the lap was complete, or null when it was not recorded.</summary>
  public int? Position { get; init; }

  /// <summary>For a team: which rider's transponder ended the lap.</summary>
  public string? RiddenBy { get; init; }

  /// <summary>"handover" or "two riders on track?", as the sheet says it.</summary>
  public string? Note { get; init; }
}

public sealed record PublishedMember
{
  public required string Label { get; init; }
  public int LapsRidden { get; init; }
  public int TimedLaps { get; init; }
  public long? BestLapMs { get; init; }
  public long? AverageLapMs { get; init; }

  /// <summary>Riders sharing one transponder: their laps count, their times cannot.</summary>
  public bool SharedTransponder { get; init; }

  /// <summary>The laps nobody can be credited with.</summary>
  public bool Unattributed { get; init; }
}

public sealed record PublishedGatePick
{
  public int GatePick { get; init; }
  public required string Number { get; init; }
  public required string Name { get; init; }
  public string? Category { get; init; }
  public long? BestLapMs { get; init; }

  /// <summary>Which lap of their session it was. Null when there is no time.</summary>
  public int? BestLapNumber { get; init; }

  public int TimedLaps { get; init; }
  public int TotalLaps { get; init; }
  public long? GapToPoleMs { get; init; }
  public long? IntervalToAheadMs { get; init; }

  /// <summary>"timed", "noTime" or "didNotGoOut".</summary>
  public required string Status { get; init; }
}

/// <summary>Which build sent this, so a wrong-looking result can be traced.</summary>
public sealed record PublishedClient(string App, string Version);
