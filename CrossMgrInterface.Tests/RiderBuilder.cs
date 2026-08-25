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

  public RiderInfo Build() => _rider;
}
