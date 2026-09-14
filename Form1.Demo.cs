namespace CrossMgrInterface;

/// <summary>
/// Demos, from both sides. In the application: the way into one. In a demo's
/// own copy of the application: setting the session up, the DEMO bar, and the
/// simulated reader. See <see cref="DemoLaunch"/> for why a demo runs as a
/// process of its own.
/// </summary>
public partial class Form1
{
  /// <summary>The demo this window runs, or null in the real application.</summary>
  private readonly DemoSession? _demo;

  private bool IsDemo => _demo != null;

  private Panel? _demoBanner;
  private CancellationTokenSource? _demoReaderStop;

  /// <summary>Help > Try a demo race..., and the link on the Race Day screen.</summary>
  private void ShowDemoPicker()
  {
    using var picker = new DemoPickerDialog(DemoScenarios.All);
    if (picker.ShowDialog(this) != DialogResult.OK || picker.Chosen is not { } scenario) return;

    try
    {
      DemoLaunch.Launch(scenario.Id);
      AddMessage($"🎬 Opened the demo \"{scenario.Title}\" in a window of its own.");
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(this, "The demo could not be started.", "Nothing in this window has changed.", ex);
    }
  }

  /// <summary>The bar under the menu that says, on every tab, that none of this is real.</summary>
  private Panel BuildDemoBanner()
  {
    var banner = new Panel
    {
      Dock = DockStyle.Top,
      Height = 32,
      BackColor = Color.FromArgb(255, 214, 102),
      Padding = new Padding(10, 0, 10, 0)
    };

    var words = new Label
    {
      Text = "DEMO  -  simulated riders. Your real races, rider lists and reader settings are not touched. " +
             "Close this window to end the demo.",
      Dock = DockStyle.Fill,
      TextAlign = ContentAlignment.MiddleLeft,
      AutoEllipsis = true,
      Font = new Font("Segoe UI", 9.75F, FontStyle.Bold),
      ForeColor = Color.Black
    };

    var about = new LinkLabel
    {
      Text = "What's happening?",
      Dock = DockStyle.Right,
      Width = 140,
      TextAlign = ContentAlignment.MiddleRight,
      Font = new Font("Segoe UI", 9.75F)
    };
    about.LinkClicked += (_, _) => ShowDemoIntro(canStart: false);

    // Docked children lay out from the highest index down, and the Fill one
    // has to go last.
    banner.Controls.Add(words);
    banner.Controls.Add(about);
    return banner;
  }

  /// <summary>
  /// Sets the demo's session up the way the wizard would, then - once the
  /// operator has read what it is about - starts its reader.
  /// </summary>
  private void StartDemo()
  {
    var demo = _demo!;
    var scenario = demo.Scenario;

    // A real file, imported the normal way, so the Riders step's rules and the
    // SET UP checklist behave exactly as they would with a club's own list.
    var riderList = Path.Combine(Path.GetDirectoryName(AppPaths.SettingsFile)!, "riders.csv");
    File.WriteAllText(riderList, scenario.ToRiderCsv());
    _riderDataImporter.ImportFromCsvDetailed(riderList);

    // Not 53135: that may be the real reader's, in the window behind this one.
    readerPort = DemoLaunch.FreeLoopbackPort();
    ApplyNewRaceSetup(scenario.ToSetup(riderList));

    AddMessage($"🎬 Demo: {scenario.Title}. The riders are simulated; nothing here touches your real races.");

    if (!demo.Unattended && !ShowDemoIntro(canStart: true))
    {
      BeginInvoke(new Action(Close));
      return;
    }

    StartDemoReader();
  }

  /// <summary>The demo's intro card. True when the operator pressed Start demo.</summary>
  private bool ShowDemoIntro(bool canStart)
  {
    using var intro = new DemoIntroDialog(_demo!.Scenario, canStart);
    return intro.ShowDialog(this) == DialogResult.OK;
  }

  private void StartDemoReader()
  {
    var demo = _demo!;
    _demoReaderStop = new CancellationTokenSource();
    var stop = _demoReaderStop.Token;

    var reader = new DemoReader(readerPort, demo.Scenario.Crossings, DemoWaveStartedAt, () => raceFinished);
    reader.Connected += () =>
    {
      AddMessage(demo.Scenario.ManualStart
        ? "🎬 Demo reader connected. Press START RACE to send the first class off."
        : "🎬 Demo reader connected. The first riders are on their way to the line.");

      if (demo.Unattended && demo.Scenario.ManualStart)
        BeginInvoke(new Action(() => buttonStartRace_Click(this, EventArgs.Empty)));
    };

    _ = Task.Run(async () =>
    {
      try
      {
        await reader.RunAsync(stop);
      }
      catch (Exception ex)
      {
        // Closing the window cancels the reader and drops its connection;
        // either way that is the end of the demo, not a fault.
        if (stop.IsCancellationRequested) return;

        AddMessage($"⚠️ The demo reader stopped: {ex.Message}");
        RaiseNotice(NoticeLevel.Warning, "The demo reader stopped. Close this window and start the demo again.");
      }
    });
  }

  /// <summary>When a class left the gate, asked from the demo reader's thread.</summary>
  private DateTime? DemoWaveStartedAt(string className)
  {
    lock (ridersLock)
    {
      return waves?.StartTimeFor(className);
    }
  }

  private void StopDemoReader() => _demoReaderStop?.Cancel();
}
