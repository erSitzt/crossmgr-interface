using System.Text;
using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// The plan code is a promise to every club that wrote one down: these tests
/// hold its format to the byte, and make sure a mistyped code is refused
/// rather than read as some other day.
/// </summary>
public class DayPlanCodeTests
{
  // Golden codes. Never change these: a club's list made with this version
  // has to read the same in every later one. A change to the format belongs
  // in a new version or the extension block, and new golden codes beside these.
  private const string MxClubDayCode =
    "CMP1-05ZGY-1BR0W-5MTP1-0CDP7-ARH0C-HGQJ1-T7E9Q-QAW10-84450-WK1CD-T6JRV-50X3Q-4VVNE-0G442-JHENG-PRTB6-F5MPW-SR69N-QQ8VS-06434-TVVMD-WG340-0847Y-0E3R1-04122-A0F04-1G48H-J1W0G-21125-07G20-R4MG0-PW501-040GB-9017G-7G208-30PJ0-3W012-G0G20-86MG0-KR3R1-041GC-06DD8";
  private const string MxClubDayShort = "354N-RQVY";

  private const string TrainingDayCode =
    "CMP1-0590Y-1BR0G-658WK-1D5Q6-JVK74-1J62Y-878XS-6YXBG-410GG-M3JC5-HQ8TB-3CM3M-EWKFE-NR20G-G00RG-VG20M-040G4-89W2G-0G60H-17GA0-20824-4Y180-8308G-KR501-04122-F0M04-1G402-PMM";
  private const string TrainingDayShort = "BBJ1-EDFG";

  private const string EnduroCode =
    "CMP1-04Z0Y-3YR0G-2GCHB-ECHTQ-4VR78-XS6YX-BG410-GCMK1-CDJJ0-C878X-S6YXB-G4110-CMK1C-DJJ0C-G008J-BG23R-000G2-0H8R8-1QG01-W041G-803W9-C";
  private const string EnduroShort = "6QS3-G4W6";

  private const string TeamRaceCode =
    "CMP1-04K0Y-2KR08-4N8SB-1DMG7-4RB3C-M450W-K1CDT-6JRV5-00103-C091W-00253-R7G0G-0000T-BMG";
  private const string TeamRaceShort = "47YK-GW5D";

  private const string GermanDayCode =
    "CMP1-06T02-3R5G8-0GM4A-3DHTP-4WK5D-SQ6AV-H0ADQ-PTVB5-E81MT-P1H19-6NGCH-099TP-ESBEC-G7MCW-K5D5J-Q682M-E9GPJ-VK9DS-KGTMV-5DSMP-YWK5D-RGC77-1M602-M8RBD-CNQ0A-E1NCD-HPT32-TCNMQ-8X3JC-5MPWT-BECW3-4RRBN-CRG32-1JCC5-TPC81-J0042-3Z071-W1020-G344M-0Y0R4-0M306-8HJ1W-1020G-748M0-Y0R40-M30F9-01DRA-020G1-084A8-09W1W-0G610-50R4A-80FG0-4A020-G1084-T809W-1W0G6-1050R-4G1HQ-R";
  private const string GermanDayShort = "0CAR-3TWB";

  public static TheoryData<string> Golden => new()
  {
    MxClubDayCode, TrainingDayCode, EnduroCode, TeamRaceCode, GermanDayCode
  };

  // ---- golden codes -----------------------------------------------------------

  [Fact]
  public void TheBuiltInPresetsStillHaveTheirCodes()
  {
    Assert.Equal(new[] { MxClubDayCode, TrainingDayCode, EnduroCode, TeamRaceCode },
      PresetCatalog.All.Select(p => p.Code));
    Assert.Equal(GermanDayCode, DayPlanCode.Encode(GermanDay()));
  }

  [Fact]
  public void AGoldenCodeStillReadsAsTheDayItWasMadeFrom()
  {
    var plan = DayPlanCode.Decode(GermanDayCode).Plan!;
    AssertSame(DayPlanCode.Canonical(GermanDay()), plan);

    Assert.Equal("Clubrennen Sommer", plan.Name);
    Assert.Equal("Lauf 1 - Senioren Ü40 / Damen / 85ccm", plan.Sessions[5].Name);
    Assert.Equal("Senioren Ü40 / Damen / 85ccm", plan.Sessions[5].OverallGroup);
    Assert.Equal(new TimeSpan(13, 30, 0), plan.Sessions[7].Time);
    Assert.True(plan.After.Publish && plan.After.SaveSheets && plan.After.Pdf && plan.After.LiveTiming);
  }

  [Theory, MemberData(nameof(Golden))]
  public void ReadingAndWritingAGoldenCodeGivesTheSameCode(string code)
  {
    var plan = DayPlanCode.Decode(code).Plan;
    Assert.NotNull(plan);
    Assert.Equal(code, DayPlanCode.Encode(plan!));
  }

  [Fact]
  public void ShortCodesAreTheSameEverywhere()
  {
    // The website works these out too; tests/test_presets.py holds it to the same values.
    Assert.Equal(MxClubDayShort, DayPlanCode.ShortCodeOf(MxClubDayCode));
    Assert.Equal(GermanDayShort, DayPlanCode.ShortCodeOf(GermanDayCode));
    Assert.Equal(GermanDayShort, DayPlanCode.ShortCodeOf(GermanDayCode.ToLowerInvariant().Replace("-", " ")));
    Assert.Matches("^[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$", GermanDayShort);
  }

  [Fact]
  public void CodesStayAParagraphLong()
  {
    Assert.True(TeamRaceCode.Length < 100, TeamRaceCode.Length.ToString());
    Assert.True(MxClubDayCode.Length < 300, MxClubDayCode.Length.ToString());
    Assert.True(GermanDayCode.Length < 400, GermanDayCode.Length.ToString());
  }

  // ---- round trip -------------------------------------------------------------

  [Fact]
  public void AnyPlanComesBackAsItWent()
  {
    var rng = new Random(1234);
    for (var n = 0; n < 3000; n++)
    {
      var plan = RandomPlan(rng);
      var code = DayPlanCode.Encode(plan);
      var back = DayPlanCode.Decode(code);
      Assert.True(back.Plan != null, $"plan {n}: {back.Error}");
      AssertSame(DayPlanCode.Canonical(plan), back.Plan!);
      Assert.Equal(code, DayPlanCode.Encode(back.Plan!));
    }
  }

  [Fact]
  public void WhatTheWindowsIgnoreIsNotCarried()
  {
    var plan = new DayPlan { Name = "x" };
    plan.Sessions.Add(new PlannedSession
    {
      Name = "Q", SessionType = SessionType.TimedQualifying, AdditionalLaps = 3, OverallGroup = "G",
      WaveGapSeconds = 45, Time = new TimeSpan(9, 0, 30), TypedTime = new TimeSpan(9, 30, 0), Skipped = true
    });
    plan.Sessions.Add(new PlannedSession { Name = "T", TeamEvent = true, Classes = { "MX1" }, Start = PlannedStartMode.Waves });

    var back = DayPlanCode.Decode(DayPlanCode.Encode(plan)).Plan!;
    var q = back.Sessions[0];
    Assert.Equal((0, (string?)null, 60, new TimeSpan(9, 0, 0), (TimeSpan?)null, false),
      (q.AdditionalLaps, q.OverallGroup, q.WaveGapSeconds, q.Time, q.TypedTime, q.Skipped));
    Assert.Empty(back.Sessions[1].Classes);
    Assert.Equal(PlannedStartMode.Gate, back.Sessions[1].Start);
  }

  [Fact]
  public void SessionsOutOfTimeOrderAndAnOddWaveGapSurvive()
  {
    var plan = new DayPlan { Name = "x" };
    plan.Sessions.Add(new PlannedSession { Name = "Late", Time = new TimeSpan(15, 0, 0) });
    plan.Sessions.Add(new PlannedSession { Name = "Early", Time = new TimeSpan(8, 0, 0), Start = PlannedStartMode.Waves,
      Classes = { "B", "A" }, WaveGapSeconds = 45 });
    plan.Sessions.Add(new PlannedSession { Name = "Same", Time = new TimeSpan(8, 0, 0) });

    var back = DayPlanCode.Decode(DayPlanCode.Encode(plan)).Plan!;
    Assert.Equal(new[] { 15, 8, 8 }, back.Sessions.Select(s => s.Time.Hours));
    Assert.Equal(new[] { "B", "A" }, back.Sessions[1].Classes);
    Assert.Equal(45, back.Sessions[1].WaveGapSeconds);
  }

  [Fact]
  public void NamesAreKeptExactlyEvenWhenTheyNearlyFollowThePattern()
  {
    var names = new[]
    {
      "Moto 1 - MX1 / MX2", "Moto 1 -  MX1 / MX2", "Moto 1 - MX1 / MX2 ", "Moto 1 - MX1/MX2", " - MX1 / MX2",
      "MX1 / MX2", "", "Lauf 1 - Gruppe Ü40", "🏁 Finale", "Lauf 1 - Gruppe U\u0308"
    };
    foreach (var name in names)
    {
      var plan = new DayPlan { Name = name };
      plan.Sessions.Add(new PlannedSession { Name = name, Classes = { "MX1", "MX2" }, OverallGroup = "Gruppe Ü40" });
      var back = DayPlanCode.Decode(DayPlanCode.Encode(plan)).Plan!;
      Assert.Equal(name.Normalize(NormalizationForm.FormC), back.Sessions[0].Name);
      Assert.Equal(name.Normalize(NormalizationForm.FormC), back.Name);
    }
  }

  [Fact]
  public void AnEmptyPlanAndAHugeOneBothFit()
  {
    Assert.Empty(DayPlanCode.Decode(DayPlanCode.Encode(new DayPlan())).Plan!.Sessions);

    var big = new DayPlan { Name = "Big" };
    for (var i = 0; i < 100; i++)
      big.Sessions.Add(new PlannedSession { Name = $"S{i}", Time = TimeSpan.FromMinutes(i * 14), Classes = { $"C{i % 7}" } });
    Assert.Equal(100, DayPlanCode.Decode(DayPlanCode.Encode(big)).Plan!.Sessions.Count);
  }

  // ---- typos ------------------------------------------------------------------

  private const string Base32 = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

  /// <summary>The code's characters after the prefix, without dashes - what a typo can land on.</summary>
  private static string Body(string code) => code[5..].Replace("-", "");
  private static string Code(string body) => "CMP1-" + body;

  [Theory, MemberData(nameof(Golden))]
  public void EveryWrongCharacterIsCaught(string code)
  {
    var body = Body(code);
    for (var i = 0; i < body.Length; i++)
      foreach (var c in Base32)
      {
        if (c == body[i]) continue;
        var typo = body[..i] + c + body[(i + 1)..];
        Assert.Null(DayPlanCode.Decode(Code(typo)).Plan);
      }
  }

  [Theory, MemberData(nameof(Golden))]
  public void EverySwapAddedOrDroppedCharacterIsCaught(string code)
  {
    var body = Body(code);
    for (var i = 0; i < body.Length; i++)
    {
      if (i + 1 < body.Length && body[i] != body[i + 1])
        Assert.Null(DayPlanCode.Decode(Code(body[..i] + body[i + 1] + body[i] + body[(i + 2)..])).Plan);

      Assert.Null(DayPlanCode.Decode(Code(body[..i] + body[(i + 1)..])).Plan);

      foreach (var c in "0AZ")
        Assert.Null(DayPlanCode.Decode(Code(body[..i] + c + body[i..])).Plan);
    }

    var blocks = code.Split('-');
    for (var b = 1; b < blocks.Length; b++)
      Assert.Null(DayPlanCode.Decode(string.Join("-", blocks.Where((_, i) => i != b))).Plan);
  }

  [Fact]
  public void TwoTyposAtRandomAreAlmostNeverRead()
  {
    var rng = new Random(77);
    var body = Body(GermanDayCode);
    var accepted = 0;
    const int tries = 20000;
    for (var n = 0; n < tries; n++)
    {
      var chars = body.ToCharArray();
      for (var k = 0; k < 2; k++)
      {
        var i = rng.Next(chars.Length);
        chars[i] = Base32[(Base32.IndexOf(chars[i]) + 1 + rng.Next(31)) % 32];
      }
      if (DayPlanCode.Decode(Code(new string(chars))).Plan != null) accepted++;
    }
    // CRC-16 lets about 1 in 65,000 through; the strict reader catches most of those.
    Assert.True(accepted <= 2, $"{accepted} of {tries} double typos were read as a plan");
  }

  [Fact]
  public void AMistypedCodeSaysSo()
  {
    var body = Body(EnduroCode);
    var typo = body[..20] + (body[20] == 'A' ? 'B' : 'A') + body[21..];
    Assert.Contains("mistyped", DayPlanCode.Decode(Code(typo)).Error);
    Assert.Contains("missing", DayPlanCode.Decode(EnduroCode[..^3]).Error);
    Assert.Contains("too many", DayPlanCode.Decode(EnduroCode + "A").Error);
    Assert.Contains("CMP1", DayPlanCode.Decode("hello").Error);
  }

  // ---- what people paste -------------------------------------------------------

  [Fact]
  public void ACodeReadsHoweverItWasPasted()
  {
    var lower = EnduroCode.ToLowerInvariant();
    var oil = EnduroCode.Replace('0', 'O').Replace('1', 'I');
    var noDashes = EnduroCode.Replace("-", "");
    var enDashes = EnduroCode.Replace('-', '–');
    var nbsp = EnduroCode.Replace("-", "\u00A0");
    var zeroWidth = EnduroCode.Replace("-", "-\u200B");
    var wrapped = string.Join("\r\n", Chunks(EnduroCode, 40));
    var bold = "*" + EnduroCode + "*";

    foreach (var text in new[] { lower, oil, noDashes, enDashes, nbsp, zeroWidth, wrapped, bold })
    {
      var found = DayPlanCode.Decode(text);
      Assert.True(found.Plan != null, $"{text}: {found.Error}");
      Assert.Equal(EnduroCode, found.Code);
    }
  }

  [Fact]
  public void CodesInAMessageAreFoundWithTheirNames()
  {
    var message =
      "[30.10.26, 14:02] Dennis: Hier die Codes\n" +
      "Clubrennen Sommer: " + GermanDayCode + "\n" +
      "Enduro = " + string.Join("\n", Chunks(EnduroCode, 50)) + "\n" +
      TeamRaceCode + " Teamrennen Herbst\n" +
      "kaputt: CMP1-00000-00000\n";

    var found = DayPlanCode.FindAll(message);
    Assert.Equal(4, found.Count);
    Assert.Equal(("Clubrennen Sommer", GermanDayCode), (found[0].LineText, found[0].Code));
    Assert.Equal("Enduro", found[1].LineText);
    Assert.NotNull(found[1].Plan);
    Assert.Equal("Teamrennen Herbst", found[2].LineText);
    Assert.Null(found[3].Plan);
    Assert.NotNull(found[3].Error);
  }

  [Fact]
  public void ShortCodesAreFoundInAListButOrdinaryWordsAreNot()
  {
    var list = "Clubrennen Sommer K7Q2-MX4P\nPractice day, Moto-Race and 2026-10-30\n  ab12-cd34  Enduro\n";
    var found = DayPlanCode.FindShortCodes(list);
    Assert.Equal(new[] { "K7Q2-MX4P", "AB12-CD34" }, found.Select(f => f.ShortCode));
    Assert.Equal("Clubrennen Sommer", found[0].LineText);
    Assert.Equal("K7Q2-MX4P", DayPlanCode.AsShortCode(" k7q2 mx4p "));
    Assert.Null(DayPlanCode.AsShortCode(EnduroCode));
  }

  // ---- robustness and later versions -------------------------------------------

  [Fact]
  public void GarbageNeverThrows()
  {
    var rng = new Random(5);
    for (var n = 0; n < 3000; n++)
    {
      var length = rng.Next(0, 80);
      var bytes = new byte[length];
      rng.NextBytes(bytes);
      bytes = new[] { (byte)1 }.Concat(DayPlanCode.Varint((uint)Math.Max(0, length))).Concat(bytes).ToArray();
      var result = DayPlanCode.Decode(DayPlanCode.Wrap(bytes));
      Assert.True(result.Plan != null || result.Error != null);

      var text = new string(Enumerable.Range(0, rng.Next(0, 300)).Select(_ => (char)rng.Next(32, 0x2100)).ToArray());
      DayPlanCode.FindAll("CMP1-" + text);
    }
  }

  /// <summary>The Team race payload with something in its extension block instead of nothing.</summary>
  private static string WithExtension(uint tag)
  {
    var payload = DayPlanCode.PayloadOf(TeamRaceCode);
    var position = 1;
    var oldLength = payload[position] < 0x80 ? payload[position] : payload[position] & 0x7F | payload[position + 1] << 7;
    var lengthBytes = payload[position] < 0x80 ? 1 : 2;
    var body = payload[(1 + lengthBytes)..^1]; // without the 0 that ends the extension block
    var extension = DayPlanCode.Varint(tag).Concat(new byte[] { 3, 7, 8, 9 }).Concat(new byte[] { 0 }).ToArray();
    var newBody = body.Concat(extension).ToArray();
    Assert.Equal(oldLength, body.Length + 1);
    return DayPlanCode.Wrap(new byte[] { 1 }.Concat(DayPlanCode.Varint((uint)newBody.Length)).Concat(newBody).ToArray());
  }

  [Fact]
  public void SomethingALaterVersionAddsIsPassedOverOrRefusedPlainly()
  {
    var ignorable = DayPlanCode.Decode(WithExtension(5));
    Assert.NotNull(ignorable.Plan);
    Assert.Equal("Team race", ignorable.Plan!.Name);

    var critical = DayPlanCode.Decode(WithExtension(4));
    Assert.Null(critical.Plan);
    Assert.Contains("newer version", critical.Error);

    var payload = DayPlanCode.PayloadOf(TeamRaceCode);
    payload[0] = 2;
    var version2 = DayPlanCode.Decode(DayPlanCode.Wrap(payload));
    Assert.Contains("newer version", version2.Error);
  }

  // ---- helpers ------------------------------------------------------------------

  private static IEnumerable<string> Chunks(string text, int size)
  {
    for (var i = 0; i < text.Length; i += size) yield return text.Substring(i, Math.Min(size, text.Length - i));
  }

  private static void AssertSame(DayPlan expected, DayPlan actual)
  {
    Assert.Equal(expected.Name, actual.Name);
    Assert.Equal(expected.ChangeoverMinutes, actual.ChangeoverMinutes);
    Assert.Equal(expected.TypicalLapSeconds, actual.TypicalLapSeconds);
    Assert.Equal((expected.After.Publish, expected.After.SaveSheets, expected.After.Pdf, expected.After.LiveTiming),
      (actual.After.Publish, actual.After.SaveSheets, actual.After.Pdf, actual.After.LiveTiming));
    Assert.Equal(expected.Sessions.Count, actual.Sessions.Count);
    for (var i = 0; i < expected.Sessions.Count; i++)
    {
      var e = expected.Sessions[i];
      var a = actual.Sessions[i];
      Assert.Equal((e.Time, e.Name, e.SessionType, e.DurationMinutes, e.AdditionalLaps, e.Start, e.WaveGapSeconds,
          e.TeamEvent, e.OverallGroup),
        (a.Time, a.Name, a.SessionType, a.DurationMinutes, a.AdditionalLaps, a.Start, a.WaveGapSeconds,
          a.TeamEvent, a.OverallGroup));
      Assert.Equal(e.Classes, a.Classes);
    }
  }

  private static readonly string[] Words =
    { "MX1", "MX2", "Jugend", "Senioren Ü40", "Damen", "85ccm", "Gruppe A", "x", "Moto", "Lauf", "🏁", "U\u0308" };

  private static DayPlan RandomPlan(Random rng)
  {
    string Word() => Words[rng.Next(Words.Length)];
    var plan = new DayPlan
    {
      Name = rng.Next(4) == 0 ? "" : Word() + " " + Word(),
      ChangeoverMinutes = rng.Next(-5, 70),
      TypicalLapSeconds = rng.Next(0, 4000),
      After = new AfterSession { Publish = rng.Next(2) == 0, SaveSheets = rng.Next(2) == 0, Pdf = rng.Next(2) == 0,
        LiveTiming = rng.Next(2) == 0, Folder = "C:\\x" }
    };

    var count = rng.Next(0, 15);
    for (var i = 0; i < count; i++)
    {
      var classes = Enumerable.Range(0, rng.Next(0, 4)).Select(_ => Word()).ToList();
      var suffix = string.Join(" / ", classes);
      var group = rng.Next(3) switch { 0 => null, 1 => suffix, _ => Word() };
      var name = rng.Next(4) switch
      {
        0 => Word() + " - " + suffix,
        1 => group == null ? Word() : Word() + " - " + group,
        2 => "",
        _ => Word() + " " + rng.Next(10)
      };
      plan.Sessions.Add(new PlannedSession
      {
        Time = TimeSpan.FromMinutes(rng.Next(0, 24 * 60)) + TimeSpan.FromSeconds(rng.Next(60)),
        Name = name,
        SessionType = (SessionType)rng.Next(3),
        Classes = classes,
        DurationMinutes = rng.Next(0, 700),
        AdditionalLaps = rng.Next(-1, 12),
        Start = (PlannedStartMode)rng.Next(3),
        WaveGapSeconds = rng.Next(0, 2000),
        TeamEvent = rng.Next(5) == 0,
        OverallGroup = group,
        Skipped = rng.Next(5) == 0
      });
    }
    return plan;
  }

  /// <summary>A club day as a German club would name it: the longest kind of code there is.</summary>
  internal static DayPlan GermanDay()
  {
    var plan = new DayPlan { Name = "Clubrennen Sommer", ChangeoverMinutes = 5, TypicalLapSeconds = 130 };
    string[] a = { "MX1", "MX2 Jugend" }, b = { "Senioren Ü40", "Damen", "85ccm" };
    PlannedSession S(int h, int m, string name, SessionType t, string[] c, string? group, int len, int laps, PlannedStartMode st) =>
      new() { Time = new TimeSpan(h, m, 0), Name = name, SessionType = t, Classes = c.ToList(), OverallGroup = group,
        DurationMinutes = len, AdditionalLaps = laps, Start = st };
    plan.Sessions.AddRange(new[]
    {
      S(8, 30, "Freies Training - MX1 / MX2 Jugend", SessionType.FreePractice, a, null, 15, 0, PlannedStartMode.FirstRider),
      S(8, 50, "Freies Training - Senioren Ü40 / Damen / 85ccm", SessionType.FreePractice, b, null, 15, 0, PlannedStartMode.FirstRider),
      S(9, 15, "Zeittraining - MX1 / MX2 Jugend", SessionType.TimedQualifying, a, null, 15, 0, PlannedStartMode.FirstRider),
      S(9, 35, "Zeittraining - Senioren Ü40 / Damen / 85ccm", SessionType.TimedQualifying, b, null, 15, 0, PlannedStartMode.FirstRider),
      S(10, 30, "Lauf 1 - MX1 / MX2 Jugend", SessionType.Race, a, "MX1 / MX2 Jugend", 20, 1, PlannedStartMode.Gate),
      S(11, 0, "Lauf 1 - Senioren Ü40 / Damen / 85ccm", SessionType.Race, b, "Senioren Ü40 / Damen / 85ccm", 15, 1, PlannedStartMode.Gate),
      S(13, 0, "Lauf 2 - MX1 / MX2 Jugend", SessionType.Race, a, "MX1 / MX2 Jugend", 20, 1, PlannedStartMode.Gate),
      S(13, 30, "Lauf 2 - Senioren Ü40 / Damen / 85ccm", SessionType.Race, b, "Senioren Ü40 / Damen / 85ccm", 15, 1, PlannedStartMode.Gate)
    });
    return plan;
  }
}
