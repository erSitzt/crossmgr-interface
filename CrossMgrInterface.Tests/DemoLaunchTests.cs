using Xunit;

namespace CrossMgrInterface.Tests;

public class DemoLaunchTests
{
  [Fact]
  public void NoArgumentsIsTheRealApplication()
  {
    Assert.Null(DemoLaunch.Parse(Array.Empty<string>(), out var error));
    Assert.Null(error);
  }

  [Fact]
  public void DemoNamesTheScenario()
  {
    var demo = DemoLaunch.Parse(new[] { "--demo", "race" }, out var error);

    Assert.Null(error);
    Assert.Equal(DemoScenarios.RaceId, demo!.Scenario.Id);
    Assert.False(demo.Unattended);
  }

  [Fact]
  public void UnattendedCanComeEitherSideAndCaseDoesNotMatter()
  {
    Assert.True(DemoLaunch.Parse(new[] { "--unattended", "--demo", "enduro" }, out _)!.Unattended);
    Assert.True(DemoLaunch.Parse(new[] { "--DEMO", "Enduro", "--unattended" }, out _)!.Unattended);
  }

  [Fact]
  public void AnUnknownDemoIsAnErrorRatherThanTheRealApplication()
  {
    Assert.Null(DemoLaunch.Parse(new[] { "--demo", "rally" }, out var error));
    Assert.Contains("rally", error);

    Assert.Null(DemoLaunch.Parse(new[] { "--demo" }, out error));
    Assert.NotNull(error);
  }

  [Fact]
  public void ANewDemoClearsAwayFinishedDemosButNotOneStillRunning()
  {
    var root = Path.Combine(Path.GetTempPath(), "demo-sandboxes-" + Guid.NewGuid().ToString("N"));
    var at = DateTime.Now;

    try
    {
      string finishedFolder;
      using (var finished = DemoSandbox.Create(root, "race", at))
      {
        finishedFolder = finished.Folder;
        File.WriteAllText(Path.Combine(finished.Folder, "races.db"), "done");
      }

      using var running = DemoSandbox.Create(root, "teams", at.AddSeconds(1));
      File.WriteAllText(Path.Combine(running.Folder, "races.db"), "in use");

      // Both old enough that only the lock can keep one of them.
      Directory.SetCreationTime(finishedFolder, at.AddMinutes(-10));
      Directory.SetCreationTime(running.Folder, at.AddMinutes(-9));

      using var next = DemoSandbox.Create(root, "race", at.AddSeconds(2));

      Assert.False(Directory.Exists(finishedFolder));
      Assert.True(File.Exists(Path.Combine(running.Folder, "races.db")));
      Assert.True(Directory.Exists(next.Folder));
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }
  }

  [Fact]
  public void AFolderJustMadeIsLeftAloneEvenBeforeItsDemoHasTakenTheLock()
  {
    var root = Path.Combine(Path.GetTempPath(), "demo-sandboxes-" + Guid.NewGuid().ToString("N"));

    try
    {
      var starting = Path.Combine(root, "teams-starting");
      Directory.CreateDirectory(starting);

      using var next = DemoSandbox.Create(root, "race", DateTime.Now);

      Assert.True(Directory.Exists(starting));
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }
  }

  [Fact]
  public void TwoDemosStartedInTheSameSecondGetFoldersOfTheirOwn()
  {
    var root = Path.Combine(Path.GetTempPath(), "demo-sandboxes-" + Guid.NewGuid().ToString("N"));
    var at = new DateTime(2026, 9, 14, 10, 0, 0);

    try
    {
      using var first = DemoSandbox.Create(root, "race", at);
      using var second = DemoSandbox.Create(root, "race", at);

      Assert.NotEqual(first.Folder, second.Folder);
      Assert.True(Directory.Exists(first.Folder));
      Assert.True(Directory.Exists(second.Folder));
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }
  }
}
