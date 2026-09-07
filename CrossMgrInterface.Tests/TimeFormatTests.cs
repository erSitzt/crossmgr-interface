using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// Durations are written with their hours once they have any. A results
/// sheet for a two-hour enduro printed "05:12" for a rider's 2:05:12.
/// </summary>
public class TimeFormatTests
{
  [Fact]
  public void UnderAnHourReadsAsItAlwaysDid()
  {
    var t = new TimeSpan(0, 5, 12) + TimeSpan.FromMilliseconds(345);

    Assert.Equal("05:12.345", TimeFormat.Precise(t));
    Assert.Equal("05:12.3", TimeFormat.Tenths(t));
    Assert.Equal("05:12", TimeFormat.Clock(t));
  }

  [Fact]
  public void FromAnHourOnTheHoursAreShown()
  {
    var t = new TimeSpan(2, 5, 12) + TimeSpan.FromMilliseconds(345);

    Assert.Equal("2:05:12.345", TimeFormat.Precise(t));
    Assert.Equal("2:05:12.3", TimeFormat.Tenths(t));
    Assert.Equal("2:05:12", TimeFormat.Clock(t));
  }

  [Fact]
  public void ExactlyAnHourIsNotZero()
  {
    // The wrap-around case: "00:00" is what this used to print.
    Assert.Equal("1:00:00", TimeFormat.Clock(TimeSpan.FromHours(1)));
    Assert.Equal("2:00:00", TimeFormat.Clock(TimeSpan.FromMinutes(120)));
  }

  [Fact]
  public void MissingValuesUseTheCallersFallback()
  {
    Assert.Equal("N/A", TimeFormat.Precise(null, "N/A"));
    Assert.Equal("00:40.000", TimeFormat.Precise(TimeSpan.FromSeconds(40), "N/A"));
  }
}
