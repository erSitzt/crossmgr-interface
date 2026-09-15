using Xunit;

namespace CrossMgrInterface.Tests;

public class HelpTopicsTests
{
  [Fact]
  public void EveryTopicHasATitleASummaryAndSomethingToRead()
  {
    Assert.All(HelpTopics.All, topic =>
    {
      Assert.False(string.IsNullOrWhiteSpace(topic.Title), topic.Id);
      Assert.False(string.IsNullOrWhiteSpace(topic.Summary), topic.Id);
      Assert.NotEmpty(topic.Blocks);
      Assert.All(topic.Blocks, block =>
        Assert.True(block.Kind is HelpBlockKind.Steps or HelpBlockKind.Bullets or HelpBlockKind.Keys
          ? block.Items.Count > 0 && block.Items.All(i => !string.IsNullOrWhiteSpace(i))
          : !string.IsNullOrWhiteSpace(block.Text), $"{topic.Id}: an empty {block.Kind}"));
    });
  }

  [Fact]
  public void TopicIdsAreUnique()
  {
    var duplicates = HelpTopics.All.GroupBy(t => t.Id).Where(g => g.Count() > 1).Select(g => g.Key);

    Assert.Empty(duplicates);
  }

  [Fact]
  public void EveryTopicIsInAGroupTheListShows()
  {
    Assert.All(HelpTopics.All, topic => Assert.Contains(topic.Group, HelpTopics.Groups));
  }

  [Fact]
  public void SeeAlsoOnlyPointsAtTopicsThatExist()
  {
    Assert.All(HelpTopics.All, topic =>
      Assert.All(topic.SeeAlso, id => Assert.True(HelpTopics.Find(id) != null, $"{topic.Id} -> {id}")));
  }

  [Fact]
  public void EveryTopicTheHelpMenuOpensExists()
  {
    var menuTopics = new[]
    {
      HelpTopicIds.QuickStart, HelpTopicIds.Race, HelpTopicIds.Qualifying, HelpTopicIds.Practice,
      HelpTopicIds.Waves, HelpTopicIds.Teams, HelpTopicIds.Shortcuts
    };

    Assert.All(menuTopics, id => Assert.NotNull(HelpTopics.Find(id)));
  }

  [Fact]
  public void TheDemoTopicNamesEveryDemo()
  {
    var topic = HelpTopics.Find(HelpTopicIds.Demo)!;
    var words = string.Join("\n", topic.Blocks.Select(b => b.Text).Concat(topic.Blocks.SelectMany(b => b.Items)));

    Assert.All(DemoScenarios.All, demo => Assert.Contains(demo.Title, words));
  }

  [Fact]
  public void EverySessionTypeHasItsOwnTopic()
  {
    Assert.Equal("Session types", HelpTopics.Find(HelpTopicIds.Race)!.Group);
    Assert.Equal("Session types", HelpTopics.Find(HelpTopicIds.Qualifying)!.Group);
    Assert.Equal("Session types", HelpTopics.Find(HelpTopicIds.Practice)!.Group);
  }

  [Fact]
  public void EveryPictureTheHelpShowsIsEmbeddedAndReadable()
  {
    var pictures = HelpTopics.All.SelectMany(t => t.Blocks).Where(b => b.Kind == HelpBlockKind.Picture).ToList();
    Assert.NotEmpty(pictures);

    Assert.All(pictures, block =>
    {
      var name = Assert.Single(block.Items);
      var png = HelpImages.Load(name);
      Assert.True(png != null, $"help_images/{name}.png is not embedded in the application");

      using var image = Image.FromStream(new MemoryStream(png!));
      Assert.True(image.Width > 100 && image.Height > 100, $"{name} is only {image.Width}x{image.Height}");
    });
  }

  [Fact]
  public void EveryEmbeddedPictureIsShownInSomeTopic()
  {
    var shown = HelpTopics.All
      .SelectMany(t => t.Blocks)
      .Where(b => b.Kind == HelpBlockKind.Picture)
      .Select(b => b.Items[0])
      .ToHashSet();

    Assert.All(HelpImages.Names, name => Assert.Contains(name, shown));
  }

  [Fact]
  public void APictureReallyEndsUpInTheHelpText()
  {
    // The rich edit control drops a picture it does not understand without a
    // word - the insert succeeds and nothing is there - so look for it.
    string? rtf = null;
    Exception? failure = null;

    var thread = new Thread(() =>
    {
      try
      {
        using var dialog = new HelpDialog();
        dialog.CreateControl();
        dialog.ShowTopic(HelpTopicIds.Track);
        rtf = dialog.Content.Rtf;
      }
      catch (Exception ex)
      {
        failure = ex;
      }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    Assert.Null(failure);
    Assert.Contains(@"\pict", rtf);
  }

  [Fact]
  public void EveryTopicRendersInTheHelpWindow()
  {
    // Rich text needs a single-threaded apartment, which test threads are not.
    Exception? failure = null;
    var thread = new Thread(() =>
    {
      try
      {
        using var dialog = new HelpDialog();
        dialog.CreateControl();
        foreach (var topic in HelpTopics.All)
          dialog.ShowTopic(topic.Id);
      }
      catch (Exception ex)
      {
        failure = ex;
      }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    Assert.Null(failure);
  }
}
