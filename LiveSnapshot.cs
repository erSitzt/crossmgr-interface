namespace CrossMgrInterface;

/// <summary>
/// What the live timing website is told every few seconds while a race runs.
///
/// Small on purpose: the running order and the last few crossings, not every
/// lap of every rider - that is what the results publish carries, once, after
/// the flag. A 250-rider field is about 35 KB, 7 KB gzipped, and it goes out
/// every three seconds over whatever signal a field has.
///
/// There is no transponder field to fill. Nothing here can reach the page
/// that was not already on the printed sheet.
/// </summary>
public static class LiveSchema
{
  public const int Version = 1;
}

public sealed record LiveSnapshot
{
  public int SchemaVersion { get; init; } = LiveSchema.Version;

  /// <summary>See <see cref="DbRace.PublicId"/>. The same id the final results carry.</summary>
  public required string PublicId { get; init; }

  public required string Title { get; init; }

  /// <summary>"race", "freePractice" or "timedQualifying".</summary>
  public required string Type { get; init; }

  /// <summary>"waiting", "running", "lastLaps", "finishing" or "finished" - what the Race Day screen shows.</summary>
  public required string State { get; init; }

  public bool TeamEvent { get; init; }
  public required LiveClock Clock { get; init; }
  public DateTimeOffset SentAt { get; init; }

  /// <summary>Counts up for the life of the application, so the website can ignore one that arrives late.</summary>
  public long Seq { get; init; }

  public required IReadOnlyList<LiveEntry> Entries { get; init; }

  /// <summary>The last crossings, newest first.</summary>
  public required IReadOnlyList<LiveCrossing> Recent { get; init; }

  public required PublishedClient Client { get; init; }
}

public sealed record LiveClock(DateTimeOffset? StartedAt, long ElapsedMs, long? RemainingMs, long ScheduledMs);

public sealed record LiveEntry
{
  /// <summary>Running order, 1-based. Riders out of the race sort last, as the leaderboard has them.</summary>
  public int Rank { get; init; }

  public required string Number { get; init; }

  /// <summary>The team's name, for a team entry.</summary>
  public required string Name { get; init; }

  public string? Category { get; init; }
  public string? Team { get; init; }

  /// <summary>A team's riders, as the sheet names them. Null for a solo rider.</summary>
  public IReadOnlyList<string>? Members { get; init; }

  public int Laps { get; init; }
  public long? LastLapMs { get; init; }
  public long? BestLapMs { get; init; }

  /// <summary>Null for the leader, and for anyone a whole lap down.</summary>
  public long? GapToLeaderMs { get; init; }

  public int LapsDown { get; init; }

  /// <summary>"racing", "dnf" or "dns".</summary>
  public required string Status { get; init; }
}

/// <summary><paramref name="AtMs"/> is measured from the session start.</summary>
public sealed record LiveCrossing(string Number, string Name, int Lap, long? LapMs, long AtMs);
