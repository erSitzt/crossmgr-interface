using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// Finding riders whose transponder is not being read reliably, during practice,
/// while there is still time to re-fit it.
/// </summary>
public class TransponderCheckTests
{
  private static readonly Dictionary<string, int> NoDuplicates = new();
  private static readonly TimeSpan Pace = TimeSpan.FromSeconds(40);

  private static RiderInfo Missing(string tag, string number) =>
    RiderBuilder.Rider(tag, number).Build();

  [Fact]
  public void ARiderWithNoReadsAtAllIsTheHeadline()
  {
    var seen = RiderBuilder.Rider("SEEN", "1").Laps(6, 40).Build();
    var unseen = Missing("GONE", "2");

    var findings = TransponderCheck.Run(new[] { seen, unseen }, NoDuplicates, Pace);

    Assert.Equal(TransponderVerdict.NeverRead, findings[0].Verdict);
    Assert.Equal("GONE", findings[0].Rider.TagID);
    Assert.Equal(0, findings[0].Laps);
  }

  [Fact]
  public void ARiderWhoFinishesALittleEarlyIsNotAccusedOfAFaultyTag()
  {
    // The whole field does not come in on the same lap. Measuring silence
    // against the clock would flag everyone who parked up first, which would
    // make the sheet worthless - so it is measured against the rest of the field.
    var late = RiderBuilder.Rider("LATE", "1").Laps(8, 40).Build();
    var early = RiderBuilder.Rider("EARLY", "2").Laps(6, 40).Build();

    var findings = TransponderCheck.Run(new[] { late, early }, NoDuplicates, Pace);

    Assert.All(findings, f => Assert.Equal(TransponderVerdict.Clean, f.Verdict));
  }

  [Fact]
  public void ARiderWhoGoesSilentWhileTheFieldRidesOnIsFlagged()
  {
    // Two laps in and then nothing for the remaining six, while everyone else
    // kept crossing. Might be a dead tag, might be a rider who pulled in - the
    // sheet says which it saw and lets a human ask.
    var circulating = RiderBuilder.Rider("ON", "1").Laps(10, 40).Build();
    var silent = RiderBuilder.Rider("QUIET", "2").Laps(2, 40).Build();

    var findings = TransponderCheck.Run(new[] { circulating, silent }, NoDuplicates, Pace);

    var quiet = findings.Single(f => f.Rider.TagID == "QUIET");
    Assert.Equal(TransponderVerdict.WentQuiet, quiet.Verdict);
    Assert.Equal(TimeSpan.FromSeconds(320), quiet.QuietFor);
  }

  [Fact]
  public void ALongLapCountsAsASuspectedMissedRead()
  {
    var rider = RiderBuilder.Rider("MISS", "1").Laps(6, 40).Build();
    // A lap flagged as two laps' worth means one read went unseen.
    rider.Laps[3].IsSuggestedForSplit = true;
    rider.Laps[3].SuggestedSplitCount = 2;

    var finding = TransponderCheck.Run(new[] { rider }, NoDuplicates, Pace).Single();

    Assert.Equal(TransponderVerdict.Intermittent, finding.Verdict);
    Assert.Equal(1, finding.SuspectedMisses);
  }

  [Fact]
  public void ALapWorthThreeCountsAsTwoMissedReads()
  {
    // The count is reads missed, not laps flagged - a triple-length lap hid two
    // crossings, and reporting it as one would understate how bad the tag is.
    var rider = RiderBuilder.Rider("MISS", "1").Laps(6, 40).Build();
    rider.Laps[2].IsSuggestedForSplit = true;
    rider.Laps[2].SuggestedSplitCount = 3;

    var finding = TransponderCheck.Run(new[] { rider }, NoDuplicates, Pace).Single();

    Assert.Equal(2, finding.SuspectedMisses);
  }

  [Fact]
  public void TheMissRateIsAShareOfWhatTheyShouldHaveDone()
  {
    // Six recorded plus two missed is eight expected, so a quarter went unseen.
    var rider = RiderBuilder.Rider("MISS", "1").Laps(6, 40).Build();
    rider.Laps[2].IsSuggestedForSplit = true;
    rider.Laps[2].SuggestedSplitCount = 3;

    var finding = TransponderCheck.Run(new[] { rider }, NoDuplicates, Pace).Single();

    Assert.Equal(0.25, finding.MissRate, 3);
  }

  [Fact]
  public void DuplicateReadsAreReportedSeparately()
  {
    var rider = RiderBuilder.Rider("DOUBLE", "1").Laps(6, 40).Build();
    var duplicates = new Dictionary<string, int> { ["DOUBLE"] = 3 };

    var finding = TransponderCheck.Run(new[] { rider }, duplicates, Pace).Single();

    Assert.Equal(TransponderVerdict.DoubleReads, finding.Verdict);
    Assert.Equal(3, finding.DuplicateReads);
  }

  [Fact]
  public void MissedReadsOutrankDuplicatesButBothAreCounted()
  {
    // A tag doing both is worse than one doing either, and the sheet has to show
    // both numbers or the operator fixes half the problem.
    var rider = RiderBuilder.Rider("BOTH", "1").Laps(6, 40).Build();
    rider.Laps[3].IsSuggestedForSplit = true;
    rider.Laps[3].SuggestedSplitCount = 2;

    var finding = TransponderCheck.Run(
      new[] { rider }, new Dictionary<string, int> { ["BOTH"] = 2 }, Pace).Single();

    Assert.Equal(TransponderVerdict.Intermittent, finding.Verdict);
    Assert.Equal(1, finding.SuspectedMisses);
    Assert.Equal(2, finding.DuplicateReads);
    Assert.Contains("double", finding.Detail);
  }

  [Fact]
  public void ACleanRiderIsStillListed()
  {
    // The sheet is also the evidence that everyone else is fine.
    var rider = RiderBuilder.Rider("OK", "1").Laps(8, 40).Build();

    var finding = TransponderCheck.Run(new[] { rider }, NoDuplicates, Pace).Single();

    Assert.Equal(TransponderVerdict.Clean, finding.Verdict);
    Assert.Equal(8, finding.Laps);
  }

  [Fact]
  public void TheWorstProblemsComeFirst()
  {
    var clean = RiderBuilder.Rider("OK", "1").Laps(10, 40).Build();
    var doubled = RiderBuilder.Rider("DUP", "2").Laps(10, 40).Build();
    var missing = RiderBuilder.Rider("MISS", "3").Laps(10, 40).Build();
    missing.Laps[4].IsSuggestedForSplit = true;
    missing.Laps[4].SuggestedSplitCount = 2;
    var never = Missing("NONE", "4");

    var findings = TransponderCheck.Run(
      new[] { clean, doubled, missing, never },
      new Dictionary<string, int> { ["DUP"] = 1 }, Pace);

    Assert.Equal(new[] { "NONE", "MISS", "DUP", "OK" },
      findings.Select(f => f.Rider.TagID).ToArray());
  }

  [Fact]
  public void AFieldWhereNobodyWentOutFlagsEveryoneAsUnread()
  {
    // Reader unplugged, or the loop is dead. Everyone reads as never seen, which
    // is the right answer and points at the equipment rather than the riders.
    var findings = TransponderCheck.Run(
      new[] { Missing("A", "1"), Missing("B", "2") }, NoDuplicates, Pace);

    Assert.All(findings, f => Assert.Equal(TransponderVerdict.NeverRead, f.Verdict));
  }

  [Fact]
  public void WithNoPaceAtAllNobodyIsAccusedOfGoingQuiet()
  {
    // Too early in the session to know what a lap costs, so there is no basis
    // for calling anyone silent.
    var a = RiderBuilder.Rider("A", "1").Lap(40).Build();
    var b = RiderBuilder.Rider("B", "2").Laps(3, 40).Build();

    var findings = TransponderCheck.Run(new[] { a, b }, NoDuplicates, null);

    Assert.DoesNotContain(findings, f => f.Verdict == TransponderVerdict.WentQuiet);
  }

  [Fact]
  public void AnEmptyFieldProducesNothing()
  {
    Assert.Empty(TransponderCheck.Run(Array.Empty<RiderInfo>(), NoDuplicates, Pace));
  }

  [Fact]
  public void EveryDetailFitsThePrintedColumn()
  {
    // The printed column is about 41 characters at 9pt on A4. It used to carry
    // the advice as well and ran off the right of the page; the advice now
    // prints once as a legend. Keep the facts short or that comes back.
    var riders = new List<RiderInfo>
    {
      Missing("NONE", "1"),
      RiderBuilder.Rider("QUIET", "234").Laps(2, 40).Build(),
      RiderBuilder.Rider("BUSY", "345").Laps(12, 40).Build(),
      RiderBuilder.Rider("CLEAN", "456").Laps(12, 40).Build()
    };
    // Worst case: double-digit laps, several misses and several duplicates.
    riders[2].Laps[3].IsSuggestedForSplit = true;
    riders[2].Laps[3].SuggestedSplitCount = 4;

    var findings = TransponderCheck.Run(
      riders, new Dictionary<string, int> { ["BUSY"] = 12 }, Pace);

    Assert.All(findings, f =>
      Assert.True(f.Detail.Length <= 41,
        $"\"{f.Detail}\" is {f.Detail.Length} characters and will be clipped"));
  }

  [Fact]
  public void EveryVerdictHasAdviceAttached()
  {
    // The legend is the only place the advice appears now, so a verdict without
    // any would leave a flagged rider with nothing to act on.
    foreach (TransponderVerdict verdict in Enum.GetValues<TransponderVerdict>())
      Assert.False(string.IsNullOrWhiteSpace(TransponderCheck.Advice(verdict)));
  }
}
