namespace CrossMgrInterface.Tests;

/// <summary>
/// Builds RiderInfo instances with consistent lap timing, so tests read as
/// "rider #12 did three laps of 40 seconds" rather than as arithmetic.
/// </summary>
internal sealed class RiderBuilder
{
  internal static readonly DateTime RaceStart = new(2025, 8, 6, 14, 0, 0);

  private readonly RiderInfo _rider;
  private DateTime _cursor = RaceStart;

  private RiderBuilder(string tagId, string number, string name)
  {
    var parts = name.Split(' ', 2);
    _rider = new RiderInfo
    {
      TagID = tagId,
      RiderNumber = number,
      FirstName = parts[0],
      LastName = parts.Length > 1 ? parts[1] : "",
      RaceStartTime = RaceStart,
      FirstCrossing = RaceStart,
      LastCrossing = RaceStart
    };
  }

  public static RiderBuilder Rider(string tagId, string number = "1", string name = "Test Rider")
    => new(tagId, number, name);

  /// <summary>A team entry as a team event scores it: one entry, keyed by the team.</summary>
  public static RiderBuilder Team(string name, params TeamMember[] members)
  {
    var builder = new RiderBuilder(TeamRoster.KeyFor(name), TeamEntry.NumbersOf(members), name);
    builder._rider.FirstName = name;
    builder._rider.LastName = "";
    builder._rider.Team = name;
    builder._rider.Members = members;
    return builder;
  }

  public static TeamMember Member(string number, string name, params string[] transponders)
  {
    var parts = name.Split(' ', 2);
    return new TeamMember
    {
      RiderNumber = number,
      FirstName = parts[0],
      LastName = parts.Length > 1 ? parts[1] : "",
      Transponders = transponders
    };
  }

  /// <summary>Adds one lap ended by a read of <paramref name="transponder"/>.</summary>
  public RiderBuilder LapBy(double seconds, string? transponder)
  {
    Lap(seconds);
    _rider.Laps[^1].CrossedBy = transponder;
    return this;
  }

  /// <summary>Adds one lap taking <paramref name="seconds"/> from the previous crossing.</summary>
  public RiderBuilder Lap(double seconds)
  {
    var previous = _cursor;
    _cursor = _cursor.AddSeconds(seconds);

    _rider.Laps.Add(new RiderLap
    {
      TagID = _rider.TagID,
      CrossingTime = _cursor,
      LapNumber = _rider.Laps.Count + 1,
      LapTime = _cursor - previous
    });

    _rider.LastCrossing = _cursor;
    _rider.LastCrossingTime = _cursor;
    return this;
  }

  public RiderBuilder Laps(int count, double seconds)
  {
    for (var i = 0; i < count; i++) Lap(seconds);
    return this;
  }

  public RiderBuilder Dnf()
  {
    _rider.IsDNF = true;
    return this;
  }

  public RiderBuilder Dns()
  {
    _rider.IsDNS = true;
    return this;
  }

  public RiderBuilder Category(string category)
  {
    _rider.Category = category;
    return this;
  }

  public RiderInfo Build() => _rider;
}
