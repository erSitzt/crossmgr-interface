using System.Drawing.Imaging;
using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// The screenshots in the help: every scene is built and rendered on every test
/// run, and written to help_images/ when asked.
///
/// Rendering always, so CI notices the moment a screen change breaks a scene.
/// Writing only with CROSSMGR_HELP_SCREENSHOTS=write, because an ordinary test run
/// must never rewrite files in the repository. CLAUDE.md says when to write them.
/// </summary>
public class HelpScreenshotTests
{
  public static IEnumerable<object[]> Scenes => HelpScreenshotScenes.Names.Select(name => new object[] { name });

  [Theory]
  [MemberData(nameof(Scenes))]
  public void TheSceneRenders(string name)
  {
    using var picture = HelpScreenshotHarness.Render(name);

    Assert.True(picture.Width >= 300 && picture.Height >= 200, $"{name} came out {picture.Width}x{picture.Height}");
    Assert.True(HelpScreenshotHarness.HasDetail(picture), $"{name} came out blank");

    if (HelpScreenshotHarness.Writing)
      picture.Save(Path.Combine(HelpScreenshotHarness.ImagesFolder, name + ".png"), ImageFormat.Png);
  }

  [Fact]
  public void EveryPictureInTheHelpHasASceneThatMakesIt()
  {
    // A picture with no scene can never be brought up to date, and a scene with
    // no picture is a screenshot nobody sees.
    Assert.Equal(
      HelpScreenshotScenes.Names.OrderBy(n => n, StringComparer.Ordinal),
      HelpImages.Names.OrderBy(n => n, StringComparer.Ordinal));
  }
}

internal static class HelpScreenshotHarness
{
  public const string Variable = "CROSSMGR_HELP_SCREENSHOTS";

  public static bool Writing =>
    string.Equals(Environment.GetEnvironmentVariable(Variable), "write", StringComparison.OrdinalIgnoreCase);

  public static string RepoRoot { get; } = FindRepoRoot();

  public static string ImagesFolder => Path.Combine(RepoRoot, "help_images");

  private static string FindRepoRoot()
  {
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
      if (File.Exists(Path.Combine(dir.FullName, "CrossMgrInterface.csproj")))
        return dir.FullName;

    throw new InvalidOperationException($"No CrossMgrInterface.csproj above {AppContext.BaseDirectory}.");
  }

  /// <summary>
  /// Builds a scene, shows it off screen and captures its client area - the window
  /// without its title bar, which off screen draws in the old flat style.
  ///
  /// On its own single-threaded apartment with visual styles on: test threads are
  /// neither, and without styles every button draws like Windows 95.
  /// </summary>
  public static Bitmap Render(string name)
  {
    Bitmap? picture = null;
    Exception? failure = null;

    var thread = new Thread(() =>
    {
      try
      {
        Application.EnableVisualStyles();
        using var scene = HelpScreenshotScenes.Build(name);
        picture = Capture(scene);
      }
      catch (Exception ex)
      {
        failure = ex;
      }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (failure != null) throw new InvalidOperationException($"The {name} scene failed: {failure.Message}", failure);
    return picture!;
  }

  private static Bitmap Capture(HelpScene scene)
  {
    var form = scene.Form;
    form.StartPosition = FormStartPosition.Manual;
    form.Location = new Point(-20000, -20000);
    form.ShowInTaskbar = false;
    form.Show();

    Pump(TimeSpan.FromMilliseconds(500));
    scene.AfterShown?.Invoke();
    Pump(TimeSpan.FromMilliseconds(300));

    if (scene.Ready != null)
    {
      // Only worth waiting for when writing: map tiles are fetched then, and a
      // verifying run has the network switched off so they never arrive.
      var until = DateTime.UtcNow + (Writing ? TimeSpan.FromSeconds(45) : TimeSpan.FromMilliseconds(500));
      while (!scene.Ready() && DateTime.UtcNow < until) Pump(TimeSpan.FromMilliseconds(100));
      Pump(TimeSpan.FromMilliseconds(500));
    }

    using var whole = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb);
    form.DrawToBitmap(whole, new Rectangle(Point.Empty, form.Size));

    var client = form.RectangleToScreen(form.ClientRectangle);
    var offset = new Point(client.Left - form.Left, client.Top - form.Top);
    return whole.Clone(new Rectangle(offset, form.ClientSize), PixelFormat.Format32bppArgb);
  }

  private static void Pump(TimeSpan duration)
  {
    var until = DateTime.UtcNow + duration;
    do
    {
      Application.DoEvents();
      Thread.Sleep(15);
    } while (DateTime.UtcNow < until);
  }

  /// <summary>More than a flat fill: a scene that failed to draw comes out one colour.</summary>
  public static bool HasDetail(Bitmap picture)
  {
    var colours = new HashSet<int>();
    for (var y = 0; y < picture.Height; y += 7)
      for (var x = 0; x < picture.Width; x += 7)
        colours.Add(picture.GetPixel(x, y).ToArgb());

    return colours.Count >= 8;
  }
}

/// <summary>One screen ready to capture, and whatever has to be cleaned up after it.</summary>
internal sealed class HelpScene : IDisposable
{
  public required Form Form { get; init; }

  /// <summary>Run once the form is on screen, for anything that needs a laid-out window.</summary>
  public Action? AfterShown { get; init; }

  /// <summary>True once the scene has finished loading, such as map tiles arriving.</summary>
  public Func<bool>? Ready { get; init; }

  public List<IDisposable> Owned { get; } = new();

  public void Dispose()
  {
    Form.Close();
    Form.Dispose();
    foreach (var owned in Owned) owned.Dispose();
  }
}
