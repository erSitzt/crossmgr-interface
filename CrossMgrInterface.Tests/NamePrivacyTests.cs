using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// A rider who would rather not be listed in full is still listed - shorter,
/// never missing - and only on the website. The sheet keeps every name.
/// </summary>
public class NamePrivacyTests
{
  [Theory]
  [InlineData("Lena", "Brandt", NameStyle.FirstNameInitial, "Lena B.")]
  [InlineData("Lena", "Brandt", NameStyle.Starred, "Len* Bra***")]
  [InlineData("Max", "Su", NameStyle.Starred, "Max Su")]              // nothing past three to star
  [InlineData("Anna-Lena", "Meier", NameStyle.Starred, "Ann****** Mei**")]
  [InlineData("Lena", "", NameStyle.FirstNameInitial, "Lena")]
  [InlineData("", "Brandt", NameStyle.FirstNameInitial, "B.")]
  [InlineData("", "", NameStyle.Starred, "")]
  public void AHiddenNameIsShorterNotGone(string first, string last, NameStyle style, string expected)
  {
    Assert.Equal(expected, NamePrivacy.Hide(first, last, style));
  }

  [Theory]
  [InlineData("yes", true)] [InlineData("Ja", true)] [InlineData("1", true)] [InlineData("x", true)]
  [InlineData("no", false)] [InlineData("NEIN", false)] [InlineData("0", false)] [InlineData("hidden", false)]
  [InlineData("", null)] [InlineData("   ", null)] [InlineData("maybe", null)]
  public void TheRiderListColumnIsReadGenerously(string value, bool? expected)
  {
    Assert.Equal(expected, NamePrivacy.ParseShowName(value));
  }

  [Fact]
  public void TheRidersOwnWordWinsOverTheClubsDefault()
  {
    var optedOut = RiderBuilder.Rider("A", "1", "Lena Brandt").Build();
    optedOut.ShowName = false;
    var optedIn = RiderBuilder.Rider("B", "2", "Max Schmidt").Build();
    optedIn.ShowName = true;
    var silent = RiderBuilder.Rider("C", "3", "Eva Lang").Build();

    // Club shows names by default: only the opt-out is shortened.
    Assert.Equal("Lena B.", NamePrivacy.Publish(optedOut, true, NameStyle.FirstNameInitial));
    Assert.Equal("Max Schmidt", NamePrivacy.Publish(optedIn, true, NameStyle.FirstNameInitial));
    Assert.Equal("Eva Lang", NamePrivacy.Publish(silent, true, NameStyle.FirstNameInitial));

    // Club hides by default: only the opt-in goes out in full.
    Assert.Equal("Lena B.", NamePrivacy.Publish(optedOut, false, NameStyle.FirstNameInitial));
    Assert.Equal("Max Schmidt", NamePrivacy.Publish(optedIn, false, NameStyle.FirstNameInitial));
    Assert.Equal("Eva L.", NamePrivacy.Publish(silent, false, NameStyle.FirstNameInitial));
  }

  [Fact]
  public void ATeamsNameIsAClubAndStaysWhateverItsRidersChose()
  {
    var anna = RiderBuilder.Member("11", "Anna Berger", "A01");
    var adler = RiderBuilder.Team("MSC Adler", anna, RiderBuilder.Member("14", "Ben Fischer", "A02")).Build();

    Assert.Equal("MSC Adler", NamePrivacy.Publish(adler, false, NameStyle.Starred));
  }

  // ---- and the two places names leave the laptop ----

  private static RaceReportData Prepare(params RiderInfo[] riders) =>
    new RaceReportGenerator().PrepareReportData(
      riders.ToDictionary(r => r.TagID, r => r),
      RiderBuilder.RaceStart, RiderBuilder.RaceStart.AddMinutes(20), TimeSpan.FromMinutes(20),
      true, "Moto 1", rules: new RaceRules());

  [Fact]
  public void TheResultsPayloadShortensOnlyThoseWhoAsked_AndTheSheetDoesNot()
  {
    var lena = RiderBuilder.Rider("A", "1", "Lena Brandt").Laps(3, 40).Build();
    lena.ShowName = false;
    var max = RiderBuilder.Rider("B", "2", "Max Schmidt").Laps(3, 41).Build();
    var field = new[] { lena, max }.ToDictionary(r => r.TagID, r => r);
    var report = Prepare(lena, max);

    // The sheet is untouched.
    Assert.Contains(report.RiderResults, r => r.RiderName == "Lena Brandt");

    var payload = PublishPayloadBuilder.Build(new PublishInputs
    {
      Report = report, PublicId = "b3f1c0de0000000000000000000000ff",
      Field = field, PublishNamesByDefault = true, HiddenNameStyle = NameStyle.FirstNameInitial,
      ClientVersion = "test"
    });
    var json = PublishPayloadBuilder.Serialise(payload);

    Assert.Contains(payload.Entries, e => e.Name == "Lena B.");
    Assert.Contains(payload.Entries, e => e.Name == "Max Schmidt");
    Assert.DoesNotContain("Lena Brandt", json);
  }

  [Fact]
  public void TheLiveFeedShortensTheSameWay()
  {
    var lena = RiderBuilder.Rider("A", "1", "Lena Brandt").Laps(3, 40).Build();
    lena.ShowName = false;

    var capture = LiveCapture.Of(lena, showNamesByDefault: true, NameStyle.Starred);

    Assert.Equal("Len* Bra***", capture.Name);
  }

  [Fact]
  public void ATeamMemberWhoAskedIsShortenedUnderTheTeamsFullName()
  {
    var anna = RiderBuilder.Member("11", "Anna Berger", "A01");
    var ben = new TeamMember
    {
      RiderNumber = "14", FirstName = "Ben", LastName = "Fischer", Transponders = new[] { "A02" }, ShowName = false
    };
    var adler = RiderBuilder.Team("MSC Adler", anna, ben).LapBy(40, "A01").LapBy(40, "A02").Build();

    var capture = LiveCapture.Of(adler, showNamesByDefault: true, NameStyle.FirstNameInitial);

    Assert.Equal("MSC Adler", capture.Name);
    Assert.Equal(new[] { "#11 Anna Berger", "#14 Ben F." }, capture.Members);
  }
}
