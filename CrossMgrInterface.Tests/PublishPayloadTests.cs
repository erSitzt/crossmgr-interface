using System.Text.Json;
using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// What the results website is told about a session - and, just as much, what
/// it is not. A transponder code that slipped into the payload would be on the
/// open web before anyone noticed, so its absence is asserted rather than
/// assumed.
/// </summary>
public class PublishPayloadTests
{
  private static RaceReportData Prepare(RaceRules rules, params RiderInfo[] riders) =>
    new RaceReportGenerator().PrepareReportData(
      riders.ToDictionary(r => r.TagID, r => r),
      RiderBuilder.RaceStart, RiderBuilder.RaceStart.AddMinutes(20), TimeSpan.FromMinutes(20),
      true, "Moto 1 - 250cc", rules: rules);

  private static PublishedSession Build(RaceReportData report, SessionType type = SessionType.Race,
    TrackDefinition? track = null, IReadOnlyList<QualifyingEntry>? gatePick = null) =>
    PublishPayloadBuilder.Build(new PublishInputs
    {
      Report = report,
      PublicId = "b3f1c0de0000000000000000000000ff",
      SessionType = type,
      Track = track,
      GatePick = gatePick,
      ClientVersion = "v0.11.0"
    });

  private static string Json(PublishedSession session) => PublishPayloadBuilder.Serialise(session);

  // ---- what is sent -------------------------------------------------------

  [Fact]
  public void EntriesKeepTheOrderTheSheetPrintsThemIn()
  {
    var winner = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(5, 40).Build();
    var dns = RiderBuilder.Rider("B", "2", "Ben Fischer").Dns().Build();
    var dnf = RiderBuilder.Rider("C", "3", "Carla Hoff").Laps(3, 40).Dnf().Build();

    var payload = Build(Prepare(new RaceRules(), dns, dnf, winner));

    Assert.Equal(new[] { "1", "DNF", "DNS" }, payload.Entries.Select(e => e.Position));
    Assert.Equal(new[] { "finished", "dnf", "dns" }, payload.Entries.Select(e => e.Status));
  }

  [Fact]
  public void APositionThatIsNotANumberIsSentAsNoRankRatherThanZero()
  {
    var winner = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(5, 40).Build();
    var dnf = RiderBuilder.Rider("C", "3", "Carla Hoff").Laps(3, 40).Dnf().Build();

    var payload = Build(Prepare(new RaceRules(), dnf, winner));

    Assert.Equal(1, payload.Entries[0].Rank);
    Assert.Null(payload.Entries[1].Rank);
  }

  [Fact]
  public void TimesAreWholeMillisecondsAndAMissingOneStaysMissing()
  {
    var rider = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(3, 40).Build();
    var neverOut = RiderBuilder.Rider("B", "2", "Ben Fischer").Dns().Build();

    var payload = Build(Prepare(new RaceRules(), rider, neverOut));

    Assert.Equal(40_000, payload.Entries[0].BestLapMs);
    Assert.Null(payload.Entries[1].BestLapMs);
  }

  [Fact]
  public void LapOneCarriesNoLapTimeBecauseItIsNotOne()
  {
    var rider = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(3, 40).Build();

    // A real first lap runs from the start of the race, so it has no lap time.
    // It must reach the website as nothing, never as zero - every best, average
    // and spread on the page is computed from these.
    rider.Laps[0].LapTime = null;

    var laps = Build(Prepare(new RaceRules(), rider)).Entries[0].LapTimes;

    Assert.Null(laps[0].TimeMs);
    Assert.All(laps.Skip(1), l => Assert.NotNull(l.TimeMs));
  }

  [Fact]
  public void EachLapIsTimedFromTheRidersOwnStartNotTheClock()
  {
    var rider = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(3, 40).Build();

    var laps = Build(Prepare(new RaceRules(), rider)).Entries[0].LapTimes;

    Assert.Equal(new long[] { 40_000, 80_000, 120_000 }, laps.Select(l => l.AtMs));
  }

  [Fact]
  public void APositionNobodyRecordedIsSentAsUnknownNotAsTheLead()
  {
    var rider = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(3, 40).Build();
    rider.Laps[0].PositionAtCompletion = 0;
    rider.Laps[1].PositionAtCompletion = 4;

    var laps = Build(Prepare(new RaceRules(), rider)).Entries[0].LapTimes;

    Assert.Null(laps[0].Position);
    Assert.Equal(4, laps[1].Position);
  }

  [Fact]
  public void TheConditionsAreTheSheetsOwnWordsUnchanged()
  {
    var rules = new RaceRules { SessionType = SessionType.Race, Duration = TimeSpan.FromMinutes(20) };
    var rider = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(3, 40).Build();

    var payload = Build(Prepare(rules, rider));

    Assert.Equal(
      rules.Describe().Select(l => (l.Caption, l.Value)),
      payload.Session.Conditions.Select(c => (c.Caption, c.Value)));
  }

  [Fact]
  public void AnEmptyFieldIsLeftOutOfThePayloadRatherThanSentAsNothing()
  {
    var rider = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(3, 40).Build();

    var json = Json(Build(Prepare(new RaceRules(), rider)));

    // No team, machine or class was entered, so none of them should appear.
    Assert.DoesNotContain("\"team\"", json);
    Assert.DoesNotContain("\"machine\"", json);
    Assert.DoesNotContain("\"riddenBy\"", json);
  }

  // ---- what is deliberately not sent --------------------------------------

  [Fact]
  public void NoTransponderCodeReachesTheWebsite()
  {
    var adler = RiderBuilder.Team("MSC Adler",
        RiderBuilder.Member("11", "Anna Berger", "TAG-ANNA"),
        RiderBuilder.Member("14", "Ben Fischer", "TAG-BEN"))
      .LapBy(40, "TAG-ANNA").LapBy(40, "TAG-BEN").Build();
    var solo = RiderBuilder.Rider("TAG-SOLO", "7", "Carla Hoff").Laps(2, 40).Build();

    var json = Json(Build(Prepare(new RaceRules { TeamEvent = true }, adler, solo)));

    Assert.DoesNotContain("TAG-ANNA", json);
    Assert.DoesNotContain("TAG-BEN", json);
    Assert.DoesNotContain("TAG-SOLO", json);
  }

  [Fact]
  public void NoWallClockCrossingTimeIsSentForALap()
  {
    var rider = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(3, 40).Build();

    var json = Json(Build(Prepare(new RaceRules(), rider)));

    Assert.DoesNotContain("crossingTime", json);
  }

  // ---- teams --------------------------------------------------------------

  [Fact]
  public void ATeamNamesItsRidersAndSaysWhoRodeWhat()
  {
    var adler = RiderBuilder.Team("MSC Adler",
        RiderBuilder.Member("11", "Anna Berger", "A01"),
        RiderBuilder.Member("14", "Ben Fischer", "A02"))
      .LapBy(40, "A01").LapBy(40, "A01").LapBy(46, "A02").Build();

    var entry = Build(Prepare(new RaceRules { TeamEvent = true }, adler)).Entries[0];

    Assert.True(entry.IsTeam);
    Assert.Equal(new[] { "#11 Anna Berger", "#14 Ben Fischer" }, entry.Members);
    Assert.NotNull(entry.MemberBreakdown);
    Assert.Contains(entry.MemberBreakdown!, m => m.Label == "#11 Anna Berger");
  }

  [Fact]
  public void ASoloRiderCarriesNoTeamBreakdownAtAll()
  {
    var rider = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(3, 40).Build();

    var entry = Build(Prepare(new RaceRules(), rider)).Entries[0];

    Assert.False(entry.IsTeam);
    Assert.Null(entry.Members);
    Assert.Null(entry.MemberBreakdown);
  }

  [Fact]
  public void LapsNobodyCanBeCreditedWithAreShownRatherThanDropped()
  {
    var falke = RiderBuilder.Team("RC Falke",
        RiderBuilder.Member("21", "Carla Hoff", "B00"),
        RiderBuilder.Member("22", "David Kern", "B00"))
      .LapBy(40, "B00").LapBy(40, null).Build();

    var entry = Build(Prepare(new RaceRules { TeamEvent = true }, falke)).Entries[0];

    Assert.Contains(entry.MemberBreakdown!, m => m.SharedTransponder || m.Unattributed);
  }

  // ---- qualifying ---------------------------------------------------------

  [Fact]
  public void QualifyingCarriesTheGatePickOrder()
  {
    var quick = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(3, 40).Build();
    var slower = RiderBuilder.Rider("B", "2", "Ben Fischer").Laps(3, 50).Build();
    var rules = new RaceRules { SessionType = SessionType.TimedQualifying };

    var ranked = QualifyingRanking.Rank(new[] { slower, quick });
    var payload = Build(Prepare(rules, quick, slower), SessionType.TimedQualifying, gatePick: ranked);

    Assert.NotNull(payload.GatePick);
    Assert.Equal(ranked.Select(e => e.GatePick), payload.GatePick!.Select(g => g.GatePick));
    Assert.Equal("1", payload.GatePick[0].Number);
    Assert.Equal("timedQualifying", payload.Session.Type);
  }

  [Fact]
  public void ARaceSendsNoGatePickAtAllRatherThanAnEmptyOne()
  {
    var rider = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(3, 40).Build();

    var payload = Build(Prepare(new RaceRules(), rider));

    Assert.Null(payload.GatePick);
    Assert.Equal("race", payload.Session.Type);
  }

  [Fact]
  public void ARiderWhoSetNoTimeStillPicksAGate()
  {
    var quick = RiderBuilder.Rider("A", "1", "Anna Berger").Laps(3, 40).Build();
    var noTime = RiderBuilder.Rider("B", "2", "Ben Fischer").Build();
    var rules = new RaceRules { SessionType = SessionType.TimedQualifying };

    var ranked = QualifyingRanking.Rank(new[] { quick, noTime });
    var payload = Build(Prepare(rules, quick, noTime), SessionType.TimedQualifying, gatePick: ranked);

    var last = payload.GatePick![^1];
    Assert.Null(last.BestLapMs);
    Assert.NotEqual("timed", last.Status);
  }

  // ---- the circuit --------------------------------------------------------

  private static TrackDefinition Circuit()
  {
    var track = new TrackDefinition { Name = "GSC" };
    track.SetPoints(new List<LatLon>
    {
      new(52.123456789, 9.123456789), new(52.124, 9.124),
      new(52.125, 9.125), new(52.124, 9.126)
    });
    return track;
  }

  [Fact]
  public void TheCircuitIsSentAsCoordinatePairsRoundedToTheSurveysAccuracy()
  {
    var payload = Build(Prepare(new RaceRules(),
      RiderBuilder.Rider("A", "1", "Anna Berger").Laps(3, 40).Build()), track: Circuit());

    Assert.NotNull(payload.Track);
    Assert.Equal("GSC", payload.Track!.Name);
    Assert.Equal(4, payload.Track.Points.Count);
    Assert.Equal(52.123457, payload.Track.Points[0][0]);
    Assert.Equal(9.123457, payload.Track.Points[0][1]);
  }

  [Fact]
  public void TheCircuitPictureIsNeverSent()
  {
    var track = Circuit();
    track.ReferenceImage = new TrackReferenceImage
    {
      ImageData = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
      SourceName = "club-map.png",
      PixelWidth = 100,
      PixelHeight = 100
    };

    var json = Json(Build(Prepare(new RaceRules(),
      RiderBuilder.Rider("A", "1", "Anna Berger").Laps(3, 40).Build()), track: track));

    Assert.DoesNotContain("referenceImage", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("imageData", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("club-map.png", json);
  }

  [Fact]
  public void ARaceWithNoCircuitPublishesWithoutOneRatherThanFailing()
  {
    var payload = Build(Prepare(new RaceRules(),
      RiderBuilder.Rider("A", "1", "Anna Berger").Laps(3, 40).Build()));

    Assert.Null(payload.Track);
  }

  // ---- size ---------------------------------------------------------------

  [Fact]
  public void AFullEnduroFieldStaysWellWithinWhatCanBeSent()
  {
    // 250 riders, 30 laps each - the biggest race the club actually runs.
    var field = Enumerable.Range(1, 250)
      .Select(i => RiderBuilder.Rider($"T{i:D3}", i.ToString(), $"Rider {i}").Laps(30, 120).Build())
      .ToArray();

    var json = Json(Build(Prepare(new RaceRules(), field), track: Circuit()));

    var compressed = new MemoryStream();
    using (var gzip = new System.IO.Compression.GZipStream(compressed,
             System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
    {
      var bytes = System.Text.Encoding.UTF8.GetBytes(json);
      gzip.Write(bytes, 0, bytes.Length);
    }

    Assert.True(json.Length < 8 * 1024 * 1024, $"raw payload was {json.Length / 1024} KB");
    Assert.True(compressed.Length < 512 * 1024, $"gzipped payload was {compressed.Length / 1024} KB");
  }

  [Fact]
  public void ARiderNobodyIdentifiedIsNamedRatherThanLeftBlank()
  {
    // Their transponder is the one thing never sent, so without a stand-in
    // they would reach the website as an empty row. It happens on any race day
    // where a tag was not identified before the flag.
    var unknown = RiderBuilder.Rider("20269990", "", "").Laps(2, 40).Build();
    var known = RiderBuilder.Rider("A", "7", "Anna Berger").Laps(3, 40).Build();

    var payload = Build(Prepare(new RaceRules(), unknown, known));
    var entry = payload.Entries.Single(e => e.Number == "");

    Assert.Equal("Unidentified rider", entry.Name);
    Assert.DoesNotContain("20269990", Json(payload));
  }

  [Fact]
  public void ThePayloadSaysWhichBuildSentIt()
  {
    var payload = Build(Prepare(new RaceRules(),
      RiderBuilder.Rider("A", "1", "Anna Berger").Laps(3, 40).Build()));

    Assert.Equal("CrossMgrInterface", payload.Client.App);
    Assert.Equal("v0.11.0", payload.Client.Version);
    Assert.Equal(PublishSchema.Version, payload.SchemaVersion);
  }

  [Fact]
  public void TheWholePayloadIsValidJson()
  {
    var json = Json(Build(Prepare(new RaceRules(),
      RiderBuilder.Rider("A", "1", "Anna Berger").Laps(3, 40).Build()), track: Circuit()));

    var parsed = JsonDocument.Parse(json);

    Assert.Equal("Moto 1 - 250cc", parsed.RootElement.GetProperty("session").GetProperty("title").GetString());
  }
}
