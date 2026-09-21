using System.Globalization;
using System.Text.Json;

namespace CrossMgrInterface;

/// <summary>What the builder needs to describe a session to the website.</summary>
public sealed record PublishInputs
{
  /// <summary>The same figures both sheets are printed from. See RaceReportGenerator.PrepareReportData.</summary>
  public required RaceReportData Report { get; init; }

  /// <summary>See <see cref="DbRace.PublicId"/>.</summary>
  public required string PublicId { get; init; }

  public SessionType SessionType { get; init; } = SessionType.Race;

  /// <summary>The circuit, or null when the race was not run on a surveyed one.</summary>
  public TrackDefinition? Track { get; init; }

  /// <summary>Gate pick order, for timed qualifying only. See QualifyingRanking.Rank.</summary>
  public IReadOnlyList<QualifyingEntry>? GatePick { get; init; }

  public string ClientVersion { get; init; } = CrossMgrInterface.AppVersion.Display;

  /// <summary>
  /// The riders the report was built from, keyed by tag, so a name can be
  /// shortened for the website without touching the sheet. Null means every
  /// name goes out as the sheet prints it.
  /// </summary>
  public IReadOnlyDictionary<string, RiderInfo>? Field { get; init; }

  public bool PublishNamesByDefault { get; init; } = true;
  public NameStyle HiddenNameStyle { get; init; } = NameStyle.FirstNameInitial;
}

/// <summary>
/// Turns a finished session into what the results website is told about it.
///
/// Pure on purpose: no Form1, no HttpClient, no database. Everything the
/// website will show can therefore be checked in a test, which matters most for
/// the things that are deliberately *absent* - a transponder code that slipped
/// into the payload would be published to the open web before anyone noticed.
/// </summary>
public static class PublishPayloadBuilder
{
  /// <summary>
  /// Coordinates are rounded to six decimal places, about 11 cm. The survey
  /// behind them is a phone walking round a field, so more digits would only
  /// be a longer way of writing the same bend.
  /// </summary>
  private const int CoordinateDecimals = 6;

  /// <summary>
  /// Enough to draw any circuit a club rides. A loop with more points than this
  /// is thinned rather than refused: a coarse map beats no map.
  /// </summary>
  private const int MaxTrackPoints = 2000;

  /// <summary>
  /// What a rider nobody identified is called on the website.
  ///
  /// The sheet prints their transponder code, which is the one thing never
  /// sent - so without this they would reach the page as a blank row. They
  /// still raced and still hold their place, and a reader deserves to be told
  /// why there is no name rather than left looking at an empty line.
  /// </summary>
  private const string UnidentifiedRider = "Unidentified rider";

  public static PublishedSession Build(PublishInputs inputs)
  {
    var report = inputs.Report;
    var entries = report.RiderResults.Select(r => Entry(r, inputs)).ToList();

    return new PublishedSession
    {
      Session = Header(inputs),
      Track = Track(inputs.Track),
      Statistics = Statistics(report, inputs),
      Entries = entries,
      // Only qualifying has one. A race publishes nothing rather than an empty
      // list, so the website can tell "no gate pick" from "gate pick of nobody".
      GatePick = inputs.SessionType == SessionType.TimedQualifying && inputs.GatePick != null
        ? inputs.GatePick.Select(e => GatePickEntry(e, inputs)).ToList()
        : null,
      Client = new PublishedClient("CrossMgrInterface", inputs.ClientVersion)
    };
  }

  /// <summary>The payload as it goes on the wire.</summary>
  public static string Serialise(PublishedSession session) =>
    JsonSerializer.Serialize(session, PublishSchema.Json);

  private static PublishedSessionHeader Header(PublishInputs inputs)
  {
    var report = inputs.Report;

    return new PublishedSessionHeader
    {
      PublicId = inputs.PublicId,
      Title = report.RaceTitle,
      Type = inputs.SessionType switch
      {
        SessionType.TimedQualifying => "timedQualifying",
        SessionType.FreePractice => "freePractice",
        _ => "race"
      },
      StartedAt = Moment(report.RaceStartTime),
      EndedAt = Moment(report.RaceEndTime),
      ScheduledDurationMs = Ms(report.RaceDuration),
      Finished = report.RaceFinished,
      TeamEvent = report.TeamEvent,
      GeneratedAt = Moment(report.GeneratedAt) ?? DateTimeOffset.Now,
      Conditions = report.Rules?.Describe()
        .Select(line => new PublishedCondition(line.Caption, line.Value))
        .ToList() ?? new List<PublishedCondition>()
    };
  }

  private static PublishedStatistics Statistics(RaceReportData report, PublishInputs inputs)
  {
    var stats = report.RaceStatistics;
    if (stats == null) return new PublishedStatistics();

    var fastest = stats.FastestLap;

    return new PublishedStatistics
    {
      Riders = stats.TotalRiders,
      Finished = stats.FinishedRiders,
      Dnf = stats.DNFRiders,
      Dns = stats.DNSRiders,
      LapsCompleted = stats.TotalLapsCompleted,
      WinningTimeMs = Ms(stats.ActualRaceDuration),
      AdditionalLaps = stats.AdditionalLapsCount,
      FastestLap = fastest?.BestLapTime == null ? null : new PublishedFastestLap(
        Ms(fastest.BestLapTime.Value),
        FastestLapBy(fastest, inputs),
        fastest.RiderNumber)
    };
  }

  private static PublishedEntry Entry(RiderResult rider, PublishInputs inputs)
  {
    var start = EntryStart(rider);
    var live = inputs.Field?.GetValueOrDefault(rider.TagID);

    return new PublishedEntry
    {
      Rank = int.TryParse(rider.Position, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rank)
        ? rank
        : null,
      Position = rider.Position,
      Status = rider.IsDNS ? "dns" : rider.IsDNF ? "dnf" : "finished",
      // Empty rather than absent for a rider nobody got round to identifying.
      Number = rider.RiderNumber,
      Name = PublishedName(rider, live, inputs),
      Team = Text(rider.Team),
      Category = Text(rider.Category),
      Machine = Text(rider.Machine),
      IsTeam = rider.IsTeam,
      // One name per rider, each as that rider agreed to be named.
      Members = rider.IsTeam && live?.Members != null
        ? live.Members.Select(m => MemberLabel(m, inputs)).ToList()
        : rider.IsTeam && !string.IsNullOrEmpty(rider.MemberLine)
          ? rider.MemberLine.Split(" · ", StringSplitOptions.RemoveEmptyEntries).ToList()
          : null,
      Laps = rider.TotalLaps,
      TotalTimeMs = Ms(rider.TotalTime),
      BestLapMs = Ms(rider.BestLapTime),
      AverageLapMs = Ms(rider.AverageLapTime),
      BestLapBy = Text(rider.BestLapBy),
      GapToLeaderMs = Ms(rider.GapToLeader),
      LapsDownToLeader = rider.LapGapToLeader,
      LapTimes = rider.LapTimes.Select(l => Lap(l, start)).ToList(),
      MemberBreakdown = rider.IsTeam && rider.MemberBreakdown.Count > 0
        ? rider.MemberBreakdown.Select(line => Member(line, inputs)).ToList()
        : null
    };
  }

  /// <summary>
  /// Who set the fastest lap, named as they agreed to be. A team's fastest lap
  /// belongs to whoever rode it, when that is known.
  /// </summary>
  private static string FastestLapBy(RiderResult fastest, PublishInputs inputs)
  {
    var live = inputs.Field?.GetValueOrDefault(fastest.TagID);
    if (live == null)
      return string.IsNullOrEmpty(fastest.BestLapBy) ? fastest.RiderName : fastest.BestLapBy!;

    if (live.IsTeam && !string.IsNullOrEmpty(fastest.BestLapBy))
    {
      // BestLapBy is the member's label; find them to apply their own choice.
      var member = live.Members?.FirstOrDefault(m => m.Label == fastest.BestLapBy);
      return member != null ? MemberLabel(member, inputs) : fastest.BestLapBy!;
    }

    return PublishedName(fastest, live, inputs);
  }

  /// <summary>
  /// The name that goes out: the sheet's, unless the rider asked otherwise.
  /// The website sees a shorter name; the sheet in the tent keeps the full one.
  /// </summary>
  private static string PublishedName(RiderResult rider, RiderInfo? live, PublishInputs inputs)
  {
    if (string.IsNullOrWhiteSpace(rider.RiderName)) return UnidentifiedRider;
    if (live == null) return rider.RiderName;
    return NamePrivacy.Publish(live, inputs.PublishNamesByDefault, inputs.HiddenNameStyle);
  }

  private static string MemberLabel(TeamMember m, PublishInputs inputs) =>
    $"#{m.RiderNumber} {NamePrivacy.Publish(m, inputs.PublishNamesByDefault, inputs.HiddenNameStyle)}".Trim();

  /// <summary>
  /// When this entry's own clock started - their wave's gate, not the race's.
  ///
  /// Worked back from the figures the sheet already carries, because TotalTime
  /// is defined as the last crossing minus that start. Taking it from the race
  /// instead would put every later wave's laps minutes out on a chart.
  /// </summary>
  private static DateTime? EntryStart(RiderResult rider)
  {
    if (rider.LapTimes.Count == 0) return null;
    return rider.LapTimes[^1].CrossingTime - rider.TotalTime;
  }

  private static PublishedLap Lap(LapResult lap, DateTime? entryStart) => new()
  {
    Lap = lap.LapNumber,
    TimeMs = Ms(lap.LapTime),
    AtMs = entryStart == null ? 0 : Ms(lap.CrossingTime - entryStart.Value),
    // 0 means nobody wrote it down - an old session, or one never saved. It must
    // not reach the website as a claim that the rider was leading.
    Position = lap.PositionAtCompletion > 0 ? lap.PositionAtCompletion : null,
    RiddenBy = Text(lap.RiddenBy),
    Note = Text(lap.Note)
  };

  private static PublishedMember Member(TeamMemberLine line, PublishInputs inputs) => new()
  {
    // A shared-transponder line names several riders; each gets their own say.
    Label = line.Group == null
      ? line.Label
      : string.Join(" / ", line.Group.Members.Select(m => MemberLabel(m, inputs))),
    LapsRidden = line.LapsRidden,
    TimedLaps = line.TimedLaps,
    BestLapMs = Ms(line.BestLap),
    AverageLapMs = Ms(line.AverageLap),
    SharedTransponder = line.SharedTransponder,
    Unattributed = line.IsUnattributed
  };

  private static PublishedGatePick GatePickEntry(QualifyingEntry entry, PublishInputs inputs) => new()
  {
    GatePick = entry.GatePick,
    Number = entry.Rider.RiderNumber,
    Name = $"#{entry.Rider.RiderNumber} {NamePrivacy.Publish(entry.Rider, inputs.PublishNamesByDefault, inputs.HiddenNameStyle)}".Trim(),
    Category = Text(entry.Rider.Category),
    BestLapMs = Ms(entry.BestLapTime),
    BestLapNumber = entry.BestLapNumber > 0 ? entry.BestLapNumber : null,
    TimedLaps = entry.TimedLaps,
    TotalLaps = entry.TotalLaps,
    GapToPoleMs = Ms(entry.GapToPole),
    IntervalToAheadMs = Ms(entry.IntervalToAhead),
    Status = entry.Status switch
    {
      QualifyingStatus.NoTime => "noTime",
      QualifyingStatus.DidNotGoOut => "didNotGoOut",
      _ => "timed"
    }
  };

  private static PublishedTrack? Track(TrackDefinition? track)
  {
    if (track == null || track.Points.Count < 2) return null;

    var points = Thin(track.Points).Select(p => new[] { Round(p.Lat), Round(p.Lon) }).ToList();

    // Only where an operator actually put it. An anchor that was never placed
    // has no ground position, and guessing one would draw the timing loop in
    // the wrong bend.
    var sf = track.StartFinish;
    var startFinish = sf.Lat.HasValue && sf.Lon.HasValue
      ? new[] { Round(sf.Lat.Value), Round(sf.Lon.Value) }
      : null;

    return new PublishedTrack
    {
      Id = track.Id,
      Name = track.Name,
      LengthMetres = Math.Round(track.LengthMetres, 1),
      Points = points,
      StartFinish = startFinish,
      Sectors = track.Sectors.Count == 0
        ? null
        : track.Sectors.Select(s => new PublishedSector
        {
          Name = s.Name,
          Fraction = Math.Round(s.Start.Fraction, 6),
          Colour = $"#{s.ColorArgb & 0xFFFFFF:X6}"
        }).ToList()
    };
  }

  /// <summary>Keeps every nth point of an unusually detailed loop, and the last one.</summary>
  private static List<LatLon> Thin(List<LatLon> points)
  {
    if (points.Count <= MaxTrackPoints) return points;

    var step = (int)Math.Ceiling(points.Count / (double)MaxTrackPoints);
    var kept = new List<LatLon>();
    for (var i = 0; i < points.Count; i += step) kept.Add(points[i]);
    if (kept[^1] != points[^1]) kept.Add(points[^1]);
    return kept;
  }

  private static double Round(double value) => Math.Round(value, CoordinateDecimals);

  /// <summary>
  /// Whole milliseconds. Finer than any transponder read, and a plain number
  /// the far end can sort and average without parsing anything.
  /// </summary>
  private static long Ms(TimeSpan span) => (long)span.TotalMilliseconds;

  private static long? Ms(TimeSpan? span) => span.HasValue ? Ms(span.Value) : null;

  /// <summary>
  /// A local time with its offset. The application records DateTime.Now, whose
  /// Kind is unspecified; treating that as local is correct, because it is.
  /// </summary>
  private static DateTimeOffset? Moment(DateTime? value) =>
    value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Local)) : null;

  /// <summary>Empty reads as absent, so it is left out of the payload entirely.</summary>
  private static string? Text(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
