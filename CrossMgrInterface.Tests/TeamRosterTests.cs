using Xunit;

namespace CrossMgrInterface.Tests;

public class TeamRosterTests
{
  private static RiderDataImporter.RiderImportData Row(string tag, string number, string name, string team,
    string category = "MX1")
  {
    var parts = name.Split(' ', 2);
    return new RiderDataImporter.RiderImportData
    {
      TagID = tag,
      RiderNumber = number,
      FirstName = parts[0],
      LastName = parts.Length > 1 ? parts[1] : "",
      Team = team,
      Category = category
    };
  }

  [Fact]
  public void RowsSharingATeamNameFormOneEntry()
  {
    var roster = TeamRoster.Build(new[]
    {
      Row("A01", "11", "Anna Berger", "MSC Adler"),
      Row("A02", "12", "Ben Fischer", "MSC Adler")
    });

    var team = Assert.Single(roster.Teams);
    Assert.Equal(2, team.Members.Count);
    Assert.Empty(roster.Solos);
    Assert.Equal(TeamRoster.KeyFor("MSC Adler"), roster.EntryKeyFor("A01"));
    Assert.Equal(TeamRoster.KeyFor("MSC Adler"), roster.EntryKeyFor("A02"));
  }

  [Fact]
  public void TeamNamesMatchIgnoringCaseAndSpacing()
  {
    var roster = TeamRoster.Build(new[]
    {
      Row("B01", "21", "Carla Hoff", "RC Falke"),
      Row("B02", "22", "David Kern", "  rc   falke ")
    });

    var team = Assert.Single(roster.Teams);
    Assert.Equal("RC Falke", team.Name);
    Assert.Equal(2, team.Members.Count);
  }

  [Fact]
  public void ATeamNameUsedByOneRiderIsASoloRider()
  {
    var roster = TeamRoster.Build(new[]
    {
      Row("A01", "11", "Anna Berger", "MSC Adler"),
      Row("A02", "12", "Ben Fischer", "MSC Adler"),
      Row("S01", "41", "Greta Lang", "Team Sued")
    });

    Assert.Single(roster.Teams);
    Assert.Equal("S01", Assert.Single(roster.Solos).TagID);
    Assert.Null(roster.EntryKeyFor("S01"));
  }

  [Fact]
  public void ARiderWithNoTeamIsASoloRider()
  {
    var roster = TeamRoster.Build(new[]
    {
      Row("S01", "41", "Greta Lang", ""),
      Row("S02", "42", "Hugo Reiter", "")
    });

    Assert.Empty(roster.Teams);
    Assert.Equal(2, roster.Solos.Count);
  }

  [Fact]
  public void TheKeyDoesNotDependOnTheOrderOfTheList()
  {
    var rows = new[]
    {
      Row("A01", "11", "Anna Berger", "MSC Adler"),
      Row("A02", "12", "Ben Fischer", "msc adler")
    };

    var forwards = TeamRoster.Build(rows);
    var backwards = TeamRoster.Build(rows.Reverse());

    Assert.Equal(forwards.EntryKeyFor("A01"), backwards.EntryKeyFor("A01"));
    Assert.Equal(forwards.Teams[0].Key, backwards.Teams[0].Key);
  }

  [Fact]
  public void ATeamSharingOneTransponderKeepsEveryMember()
  {
    // The way teams were entered before team events: every member on one tag.
    // The importer's lookup keeps only the last of these rows.
    var roster = TeamRoster.Build(new[]
    {
      Row("B00", "21", "Carla Hoff", "RC Falke"),
      Row("B00", "22", "David Kern", "RC Falke")
    });

    var team = Assert.Single(roster.Teams);
    Assert.Equal(2, team.Members.Count);
    Assert.True(team.SharesTransponder);
    Assert.False(roster.HasErrors);
    Assert.Equal(team.Key, roster.EntryKeyFor("B00"));
  }

  [Fact]
  public void ATransponderInTwoTeamsIsAnError()
  {
    var roster = TeamRoster.Build(new[]
    {
      Row("A01", "11", "Anna Berger", "MSC Adler"),
      Row("A02", "12", "Ben Fischer", "MSC Adler"),
      Row("C01", "31", "Elif Yilmaz", "Team West"),
      Row("A01", "32", "Frank Weber", "Team West")
    });

    Assert.True(roster.HasErrors);
    Assert.Contains(roster.Issues, i => i.Severity == TeamRosterSeverity.Error && i.Message.Contains("A01"));
    Assert.Equal(TeamRoster.KeyFor("MSC Adler"), roster.EntryKeyFor("A01"));
  }

  [Fact]
  public void ATransponderOnATeamRowAndASoloRowIsAnError()
  {
    var roster = TeamRoster.Build(new[]
    {
      Row("A01", "11", "Anna Berger", "MSC Adler"),
      Row("A02", "12", "Ben Fischer", "MSC Adler"),
      Row("A01", "41", "Greta Lang", "")
    });

    Assert.True(roster.HasErrors);
    Assert.Empty(roster.Solos);
  }

  [Fact]
  public void TheSameSoloRiderListedTwiceIsNotAConflict()
  {
    var roster = TeamRoster.Build(new[]
    {
      Row("S01", "41", "Greta Lang", ""),
      Row("S01", "41", "Greta Lang", "")
    });

    Assert.False(roster.HasErrors);
    Assert.Single(roster.Solos);
  }

  [Fact]
  public void MixedClassesWarnAndTheMostCommonClassWins()
  {
    var roster = TeamRoster.Build(new[]
    {
      Row("C01", "31", "Elif Yilmaz", "Team West", "MX2"),
      Row("C02", "32", "Frank Weber", "Team West", "MX1"),
      Row("C03", "33", "Gina Roth", "Team West", "MX1")
    });

    Assert.Equal("MX1", roster.Teams[0].Category);
    Assert.Contains(roster.Issues, i => i.Severity == TeamRosterSeverity.Warning && i.Message.Contains("class"));
    Assert.False(roster.HasErrors);
  }

  [Fact]
  public void ATieBetweenClassesGoesToTheFirstRow()
  {
    var roster = TeamRoster.Build(new[]
    {
      Row("C01", "31", "Elif Yilmaz", "Team West", "MX2"),
      Row("C02", "32", "Frank Weber", "Team West", "MX1")
    });

    Assert.Equal("MX2", roster.Teams[0].Category);
  }

  [Fact]
  public void TheSameRiderOnTwoRowsIsOneMemberWithTwoTransponders()
  {
    var roster = TeamRoster.Build(new[]
    {
      Row("A01", "11", "Anna Berger", "MSC Adler"),
      Row("SPARE", "11", "Anna Berger", "MSC Adler"),
      Row("A02", "12", "Ben Fischer", "MSC Adler")
    });

    var team = Assert.Single(roster.Teams);
    Assert.Equal(2, team.Members.Count);
    Assert.Equal(new[] { "A01", "SPARE" }, team.Members[0].Transponders);
    Assert.Equal(team.Key, roster.EntryKeyFor("SPARE"));
    Assert.False(team.SharesTransponder);
  }

  [Fact]
  public void ASpareTransponderDoesNotMakeARiderATeam()
  {
    var roster = TeamRoster.Build(new[]
    {
      Row("S01", "41", "Greta Lang", "Team Sued"),
      Row("S01B", "41", "Greta Lang", "Team Sued")
    });

    Assert.Empty(roster.Teams);
    Assert.Equal(2, roster.Solos.Count);
  }

  [Fact]
  public void AVeryLargeTeamWarnsThatItMayBeAClub()
  {
    var rows = Enumerable.Range(1, TeamRoster.LargeTeamSize + 1)
      .Select(i => Row($"M{i:00}", i.ToString(), $"Rider {i}", "MSC Adler"));

    var roster = TeamRoster.Build(rows);

    Assert.Contains(roster.Issues, i => i.Severity == TeamRosterSeverity.Warning && i.Message.Contains("club"));
  }

  [Fact]
  public void TransponderLookupIgnoresCase()
  {
    var roster = TeamRoster.Build(new[]
    {
      Row("a01", "11", "Anna Berger", "MSC Adler"),
      Row("A02", "12", "Ben Fischer", "MSC Adler")
    });

    Assert.Equal(TeamRoster.KeyFor("MSC Adler"), roster.EntryKeyFor("A01"));
    Assert.Equal("Anna", roster.MemberFor("A01")!.FirstName);
  }

  [Fact]
  public void TheTeamsNumbersAreListedLowestFirst()
  {
    var roster = TeamRoster.Build(new[]
    {
      Row("A14", "14", "Anna Berger", "MSC Adler"),
      Row("A11", "11", "Ben Fischer", "MSC Adler"),
      Row("A09", "9", "Carl Roth", "MSC Adler")
    });

    Assert.Equal("9/11/14", roster.Teams[0].NumbersText);
  }

  [Fact]
  public void AScoredTeamIsNamedAfterTheTeamAndNeverShowsItsKeyAsATransponder()
  {
    var roster = TeamRoster.Build(new[]
    {
      Row("A01", "11", "Anna Berger", "MSC Adler"),
      Row("A02", "14", "Ben Fischer", "MSC Adler")
    });

    var entry = roster.Teams[0].ToRiderInfo();

    Assert.True(entry.IsTeam);
    Assert.Equal("#11/14 MSC Adler", entry.Label);
    Assert.Equal("MSC Adler", entry.DisplayName);
    Assert.Equal("A01, A02", entry.TransponderText);
    Assert.True(TeamRoster.IsTeamKey(entry.TagID));
  }

  [Fact]
  public void ATeamAlreadyRacingKeepsItsTranspondersAcrossAReimport()
  {
    // Anna's spare was joined to the team during the race; the list imported
    // afterwards puts the same tag on a solo rider.
    var racing = RiderBuilder.Team("MSC Adler",
      RiderBuilder.Member("11", "Anna Berger", "A01", "SPARE"),
      RiderBuilder.Member("12", "Ben Fischer", "A02")).Build();

    var reimported = TeamRoster.Build(new[]
    {
      Row("A01", "11", "Anna Berger", "MSC Adler"),
      Row("A02", "12", "Ben Fischer", "MSC Adler"),
      Row("SPARE", "41", "Greta Lang", "")
    }).WithLiveEntries(new[] { racing });

    Assert.Equal(racing.TagID, reimported.EntryKeyFor("SPARE"));
    Assert.DoesNotContain(reimported.Solos, s => s.TagID == "SPARE");
    Assert.Equal(new[] { "A01", "SPARE" }, reimported.TeamFor(racing.TagID)!.Members[0].Transponders);
  }

  [Fact]
  public void ATeamRacingWithoutARiderListStillResolves()
  {
    // After a restart the rider list may not be loaded yet; the stored team must
    // still catch its members' reads.
    var restored = RiderBuilder.Team("RC Falke",
      RiderBuilder.Member("21", "Carla Hoff", "B00"),
      RiderBuilder.Member("22", "David Kern", "B00")).Build();

    var roster = TeamRoster.Empty.WithLiveEntries(new[] { restored });

    Assert.Equal(restored.TagID, roster.EntryKeyFor("B00"));
    Assert.NotNull(roster.TeamFor(restored.TagID));
  }

  [Fact]
  public void SummaryCountsTeamsAndSoloRiders()
  {
    var roster = TeamRoster.Build(new[]
    {
      Row("A01", "11", "Anna Berger", "MSC Adler"),
      Row("A02", "12", "Ben Fischer", "MSC Adler"),
      Row("S01", "41", "Greta Lang", "Team Sued"),
      Row("S02", "42", "Hugo Reiter", "")
    });

    Assert.Equal("1 team, 2 solo riders", roster.Summary);
    Assert.Equal(3, roster.EntryCount);
  }

  [Fact]
  public void SharingATransponderLinksMembersTransitively()
  {
    var members = new[]
    {
      RiderBuilder.Member("1", "A Rider", "X"),
      RiderBuilder.Member("2", "B Rider", "X", "Y"),
      RiderBuilder.Member("3", "C Rider", "Y"),
      RiderBuilder.Member("4", "D Rider", "Z")
    };

    var groups = TransponderGroup.Of(members);

    Assert.Equal(2, groups.Count);
    Assert.Equal(3, groups[0].Members.Count);
    Assert.True(groups[0].IsShared);
    Assert.False(groups[1].IsShared);
  }
}
