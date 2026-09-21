namespace CrossMgrInterface;

/// <summary>
/// One rider as the live feed needs them - and nothing more.
///
/// Taken under the riders lock every few seconds, so it has to be cheap:
/// a handful of values and the last few crossings, not the deep copy
/// CloneRiderForDisplay makes for the screen, which would copy every lap of
/// every rider while the crossing path waits for the lock.
/// </summary>
public sealed record LiveCapture
{
  public required string Number { get; init; }
  public required string Name { get; init; }
  public string? Category { get; init; }
  public string? Team { get; init; }
  public IReadOnlyList<string>? Members { get; init; }
  public int Laps { get; init; }
  public TimeSpan TotalTime { get; init; }
  public TimeSpan? LastLap { get; init; }
  public TimeSpan? BestLap { get; init; }
  public bool IsDnf { get; init; }
  public bool IsDns { get; init; }

  /// <summary>The rider's most recent crossings: when, which lap, how long it took.</summary>
  public required IReadOnlyList<(DateTime At, int Lap, TimeSpan? LapTime)> Recent { get; init; }

  /// <summary>Reads what the feed needs off a live rider. Call under the riders lock.</summary>
  public static LiveCapture Of(RiderInfo r, int recentLaps = 3) =>
    Of(r, showNamesByDefault: true, NameStyle.FirstNameInitial, recentLaps);

  /// <summary>
  /// As above, naming each rider as they agreed to be named on the website.
  /// The screen this is read from keeps every name in full.
  /// </summary>
  public static LiveCapture Of(RiderInfo r, bool showNamesByDefault, NameStyle style, int recentLaps = 3)
  {
    var laps = r.Laps;
    var start = Math.Max(0, laps.Count - recentLaps);
    var recent = new List<(DateTime, int, TimeSpan?)>(laps.Count - start);
    for (var i = start; i < laps.Count; i++)
      if (!laps[i].IsDeleted)
        recent.Add((laps[i].CrossingTime, laps[i].LapNumber, laps[i].LapNumber > 1 ? laps[i].LapTime : null));

    return new LiveCapture
    {
      Number = r.RiderNumber,
      Name = NamePrivacy.Publish(r, showNamesByDefault, style),
      Category = r.Category,
      Team = r.Team,
      Members = r.IsTeam
        ? r.Members?.Select(m => $"#{m.RiderNumber} {NamePrivacy.Publish(m, showNamesByDefault, style)}".Trim()).ToList()
        : null,
      Laps = r.TotalLaps,
      TotalTime = r.TotalTime,
      // The first crossing ends the run from the start, which is not a lap.
      // BestLapTime already knows that; LastLapTime does not.
      LastLap = laps.Count > 1 ? r.LastLapTime : null,
      BestLap = r.BestLapTime,
      IsDnf = r.IsDNF,
      IsDns = r.IsDNS,
      Recent = recent
    };
  }
}

public sealed record LiveInputs
{
  public required string PublicId { get; init; }
  public required string Title { get; init; }
  public SessionType SessionType { get; init; } = SessionType.Race;
  public required RaceDayState State { get; init; }
  public bool TeamEvent { get; init; }
  public bool Demo { get; init; }
  public DateTime? StartedAt { get; init; }
  public TimeSpan Duration { get; init; }
  public TimeSpan? Remaining { get; init; }
  public DateTime Now { get; init; }
  public long Seq { get; init; }
  public required IReadOnlyList<LiveCapture> Riders { get; init; }
  public string ClientVersion { get; init; } = CrossMgrInterface.AppVersion.Display;
}

/// <summary>
/// Turns a capture of the field into what the live website is sent.
///
/// Pure, so the ordering, the gaps and the feed can be checked in a test.
/// The order is the leaderboard's: everyone still racing first by laps then
/// time, then the retired, then those who never started.
/// </summary>
public static class LiveSnapshotBuilder
{
  public const int RecentCrossings = 20;

  public static LiveSnapshot Build(LiveInputs inputs)
  {
    var ordered = inputs.Riders
      .OrderBy(r => r.IsDns ? 2 : r.IsDnf ? 1 : 0)
      .ThenByDescending(r => r.Laps)
      .ThenBy(r => r.TotalTime)
      .ToList();

    var leader = ordered.FirstOrDefault(r => !r.IsDnf && !r.IsDns);

    var entries = new List<LiveEntry>(ordered.Count);
    for (var i = 0; i < ordered.Count; i++)
    {
      var r = ordered[i];
      var racing = !r.IsDnf && !r.IsDns;

      long? gap = null;
      var lapsDown = 0;
      if (racing && leader != null && r != leader)
      {
        lapsDown = Math.Max(0, leader.Laps - r.Laps);
        if (lapsDown == 0) gap = Ms(r.TotalTime - leader.TotalTime);
      }

      entries.Add(new LiveEntry
      {
        Rank = i + 1,
        Number = r.Number,
        Name = string.IsNullOrWhiteSpace(r.Name) ? "Unidentified rider" : r.Name,
        Category = Text(r.Category),
        Team = Text(r.Team),
        Members = r.Members,
        Laps = r.Laps,
        LastLapMs = Ms(r.LastLap),
        BestLapMs = Ms(r.BestLap),
        GapToLeaderMs = gap,
        LapsDown = lapsDown,
        Status = r.IsDns ? "dns" : r.IsDnf ? "dnf" : "racing"
      });
    }

    var start = inputs.StartedAt;
    var recent = inputs.Riders
      .SelectMany(r => r.Recent.Select(c => (r, c)))
      .OrderByDescending(x => x.c.At)
      .Take(RecentCrossings)
      .Select(x => new LiveCrossing(
        x.r.Number,
        string.IsNullOrWhiteSpace(x.r.Name) ? "Unidentified rider" : x.r.Name,
        x.c.Lap,
        Ms(x.c.LapTime),
        start.HasValue ? Ms(x.c.At - start.Value) : 0))
      .ToList();

    var elapsed = start.HasValue && inputs.State != RaceDayState.WaitingForFirstRider
      ? Math.Max(0, Ms(inputs.Now - start.Value))
      : 0;

    return new LiveSnapshot
    {
      PublicId = inputs.PublicId,
      Title = inputs.Title,
      Type = inputs.SessionType switch
      {
        SessionType.TimedQualifying => "timedQualifying",
        SessionType.FreePractice => "freePractice",
        _ => "race"
      },
      State = StateName(inputs.State),
      TeamEvent = inputs.TeamEvent,
      Demo = inputs.Demo,
      Clock = new LiveClock(
        start.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(start.Value, DateTimeKind.Local)) : null,
        elapsed,
        inputs.Remaining.HasValue ? Math.Max(0, Ms(inputs.Remaining.Value)) : null,
        Ms(inputs.Duration)),
      SentAt = new DateTimeOffset(DateTime.SpecifyKind(inputs.Now, DateTimeKind.Local)),
      Seq = inputs.Seq,
      Entries = entries,
      Recent = recent,
      Client = new PublishedClient("CrossMgrInterface", inputs.ClientVersion)
    };
  }

  /// <summary>The Race Day states, in the words the website expects.</summary>
  public static string StateName(RaceDayState state) => state switch
  {
    RaceDayState.WaitingForFirstRider or RaceDayState.ReadyToStart => "waiting",
    RaceDayState.LastLaps => "lastLaps",
    RaceDayState.Finishing => "finishing",
    RaceDayState.Finished => "finished",
    _ => "running"
  };

  public static string Serialise(LiveSnapshot snapshot) =>
    System.Text.Json.JsonSerializer.Serialize(snapshot, PublishSchema.Json);

  private static long Ms(TimeSpan span) => (long)span.TotalMilliseconds;
  private static long? Ms(TimeSpan? span) => span.HasValue ? Ms(span.Value) : null;
  private static string? Text(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
