using Xunit;

namespace CrossMgrInterface.Tests;

public class SpectatorBoardTests
{
  private static SpectatorBoard Race(RaceDayState state, params RiderInfo[] riders) =>
    SpectatorBoardBuilder.Build(new SpectatorInputs
    {
      Field = PositionCalculator.GetSortedRidersFromSnapshot(riders.ToList()),
      SessionType = SessionType.Race,
      Title = "Moto 1",
      State = state,
      Remaining = TimeSpan.FromMinutes(12),
      Duration = TimeSpan.FromMinutes(20)
    });

  [Fact]
  public void A_race_is_listed_in_race_order_with_gaps_and_laps_down()
  {
    var board = Race(RaceDayState.Running,
      RiderBuilder.Rider("B", "12", "Ben Fischer").Laps(3, 50).Build(),
      RiderBuilder.Rider("A", "7", "Anna Berg").Laps(3, 48).Build(),
      RiderBuilder.Rider("C", "31", "Cem Aydin").Laps(2, 55).Build(),
      RiderBuilder.Rider("D", "44", "Dana Roth").Laps(1, 60).Dnf().Build());

    Assert.Equal(new[] { "7", "12", "31", "44" }, board.Rows.Select(r => r.Number));
    Assert.Equal(new[] { "1", "2", "3", "-" }, board.Rows.Select(r => r.Position));
    Assert.Equal(new[] { "-", $"+{6.0:F1}", "-1 lap", "DNF" }, board.Rows.Select(r => r.Gap));
    Assert.Equal(new[] { 1, 2, 3, 0 }, board.Rows.Select(r => r.Podium));
    Assert.True(board.Rows[3].Out);
    Assert.Equal("Race running", board.State);
    Assert.Equal("12:00", board.Clock);
  }

  [Fact]
  public void The_fastest_lap_is_marked_on_its_rider_and_in_the_footer()
  {
    var board = Race(RaceDayState.Running,
      RiderBuilder.Rider("A", "7", "Anna Berg").Lap(0).Lap(48).Lap(49).Build(),
      RiderBuilder.Rider("B", "12", "Ben Fischer").Lap(0).Lap(50).Lap(46.5).Build());

    var fastest = Assert.Single(board.Rows, r => r.FastestLap);
    Assert.Equal("12", fastest.Number);
    Assert.StartsWith("#12 Ben Fischer", board.FastestLap);
    Assert.Contains("0:46.5", board.FastestLap);
  }

  [Fact]
  public void The_last_crossings_come_newest_first()
  {
    var board = Race(RaceDayState.Running,
      RiderBuilder.Rider("A", "7", "Anna Berg").Laps(4, 48).Build(),
      RiderBuilder.Rider("B", "12", "Ben Fischer").Laps(4, 50).Build());

    Assert.Equal(SpectatorBoardBuilder.RecentCount, board.Recent.Count);
    Assert.Equal(("12", 4), (board.Recent[0].Number, board.Recent[0].Lap));
    Assert.Equal(("7", 4), (board.Recent[1].Number, board.Recent[1].Lap));
  }

  [Fact]
  public void The_row_limit_cuts_the_board_and_counts_the_rest()
  {
    var riders = Enumerable.Range(1, 25)
      .Select(i => RiderBuilder.Rider($"T{i}", i.ToString(), $"Rider {i}").Laps(3, 45 + i).Build())
      .ToList();

    var board = SpectatorBoardBuilder.Build(new SpectatorInputs
    {
      Field = PositionCalculator.GetSortedRidersFromSnapshot(riders),
      State = RaceDayState.Running,
      RowLimit = 20
    });

    Assert.Equal(20, board.Rows.Count);
    Assert.Equal(5, board.MoreCount);
  }

  [Fact]
  public void With_classes_each_rider_has_a_place_in_their_class()
  {
    var board = Race(RaceDayState.Running,
      RiderBuilder.Rider("A", "7", "Anna Berg").Laps(3, 48).Category("MX1").Build(),
      RiderBuilder.Rider("B", "12", "Ben Fischer").Laps(3, 49).Category("MX2").Build(),
      RiderBuilder.Rider("C", "31", "Cem Aydin").Laps(3, 50).Category("MX1").Build());

    Assert.True(board.ShowClass);
    Assert.Equal(new[] { "1", "1", "2" }, board.Rows.Select(r => r.ClassPosition));
  }

  [Fact]
  public void A_rider_who_has_taken_the_flag_is_marked_finished()
  {
    var winner = RiderBuilder.Rider("A", "7", "Anna Berg").Laps(10, 48).Build();
    winner.FinalAllowedLap = 10;
    var chasing = RiderBuilder.Rider("B", "12", "Ben Fischer").Laps(9, 52).Build();
    chasing.FinalAllowedLap = 10;

    var board = Race(RaceDayState.Finishing, winner, chasing);

    Assert.True(board.Rows[0].Finished);
    Assert.False(board.Rows[1].Finished);
    Assert.Equal("Chequered flag", board.State);
    Assert.True(board.FlagOut);
  }

  [Fact]
  public void Laps_to_go_are_counted_down_once_time_is_up()
  {
    var board = SpectatorBoardBuilder.Build(new SpectatorInputs
    {
      Field = Array.Empty<RiderInfo>(),
      State = RaceDayState.LastLaps,
      LeaderLapsToGo = 2
    });

    Assert.Equal("2 laps to go", board.State);
  }

  [Fact]
  public void Qualifying_is_ranked_on_best_lap_with_riders_without_a_time_last()
  {
    var slowButLong = RiderBuilder.Rider("A", "7", "Anna Berg").Lap(0).Laps(6, 52).Build();
    var fastest = RiderBuilder.Rider("B", "12", "Ben Fischer").Lap(0).Lap(47).Build();
    var neverOut = new RiderInfo { TagID = "C", RiderNumber = "31", FirstName = "Cem", LastName = "Aydin" };

    var board = SpectatorBoardBuilder.Build(new SpectatorInputs
    {
      Field = new[] { slowButLong, fastest, neverOut },
      SessionType = SessionType.TimedQualifying,
      State = RaceDayState.Running
    });

    Assert.Equal(new[] { "12", "7", "31" }, board.Rows.Select(r => r.Number));
    Assert.Equal("Pick", board.PositionHeader);
    Assert.Equal("NO TIME", board.Rows[2].BestLap);
    Assert.True(board.Rows[2].Out);
    Assert.Equal($"+{5.0:F2}", board.Rows[1].Gap);
  }

  [Fact]
  public void Before_the_start_the_entered_riders_are_listed_by_number()
  {
    var board = SpectatorBoardBuilder.Build(new SpectatorInputs
    {
      Field = new[]
      {
        new RiderInfo { TagID = "B", RiderNumber = "12", FirstName = "Ben", LastName = "Fischer" },
        new RiderInfo { TagID = "A", RiderNumber = "7", FirstName = "Anna", LastName = "Berg" }
      },
      State = RaceDayState.ReadyToStart,
      Duration = TimeSpan.FromMinutes(20)
    });

    Assert.Equal("Starting soon", board.State);
    Assert.Equal(new[] { "7", "12" }, board.Rows.Select(r => r.Number));
    Assert.Empty(board.Recent);
  }

  [Fact]
  public void The_first_crossing_is_never_the_fastest_lap()
  {
    // A team away first, 1.0s from the gate to the line: the run from the
    // start, not a lap - and it was shown as the fastest lap of the race.
    var team = RiderBuilder.Team("RC Falke",
        RiderBuilder.Member("21", "Carla Hoff", "T21"), RiderBuilder.Member("22", "David Kern", "T22"))
      .Lap(1.0).Build();
    var rider = RiderBuilder.Rider("A", "7", "Anna Berg").Lap(5).Lap(48).Build();

    var board = SpectatorBoardBuilder.Build(new SpectatorInputs
    {
      Field = PositionCalculator.GetSortedRidersFromSnapshot(new List<RiderInfo> { team, rider }),
      TeamEvent = true,
      State = RaceDayState.Running
    });

    Assert.StartsWith("#7 Anna Berg", board.FastestLap);
    Assert.Contains("0:48.0", board.FastestLap);

    var teamRow = board.Rows.Single(r => r.Number == "21/22");
    Assert.False(teamRow.FastestLap);
    Assert.Equal("-", teamRow.LastLap);
    Assert.Equal("-", teamRow.BestLap);
    Assert.Equal("first lap", board.Recent.Single(c => c.Number == "21/22").LapTime);
  }
}
