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
  private DemoReader? _demoReader;

  // The problems demo's checklist, and the link on the DEMO bar that opens it.
  private DemoChecklistTracker? _demoChecklist;
  private DemoChecklistDialog? _demoChecklistDialog;
  private LinkLabel? _demoChecklistLink;
  private System.Windows.Forms.Timer? _demoChecklistTimer;

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

    if (_demo is { Scenario.Problems.Count: > 0 })
    {
      _demoChecklistLink = new LinkLabel
      {
        Text = "Problems to fix",
        Dock = DockStyle.Right,
        Width = 170,
        TextAlign = ContentAlignment.MiddleRight,
        Font = new Font("Segoe UI", 9.75F, FontStyle.Bold)
      };
      _demoChecklistLink.LinkClicked += (_, _) => ShowDemoChecklist();
      banner.Controls.Add(_demoChecklistLink);
    }

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
    StartDemoChecklist();
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
    _demoReader = reader;
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

  /// <summary>
  /// The problems demo's checklist. Looks at the race once a second, ticks off
  /// what has been put right, and says in a banner what race control would say
  /// over the radio. Opens beside the race unless the demo runs unattended.
  /// </summary>
  private void StartDemoChecklist()
  {
    var demo = _demo!;
    if (demo.Scenario.Problems.Count == 0) return;

    _demoChecklist = new DemoChecklistTracker(demo.Scenario.Problems);
    _demoChecklistTimer = new System.Windows.Forms.Timer { Interval = 1000 };
    _demoChecklistTimer.Tick += (_, _) => UpdateDemoChecklist();
    _demoChecklistTimer.Start();

    if (!demo.Unattended) ShowDemoChecklist();
  }

  private void UpdateDemoChecklist()
  {
    if (_demoChecklist is not { } checklist) return;

    foreach (var problem in checklist.Update(DemoRace()))
    {
      AddMessage($"🎬 {problem.Announce}");
      RaiseNotice(NoticeLevel.Warning, problem.Announce!);
    }

    if (_demoChecklistLink != null)
    {
      var text = checklist.LeftToFix > 0 ? $"Problems to fix ({checklist.LeftToFix})" : "Problems to fix";
      if (_demoChecklistLink.Text != text) _demoChecklistLink.Text = text;
    }

    if (_demoChecklistDialog is { IsDisposed: false, Visible: true } dialog) dialog.Render();
  }

  private void ShowDemoChecklist()
  {
    if (_demoChecklist is not { } checklist) return;

    if (_demoChecklistDialog is not { IsDisposed: false })
    {
      _demoChecklistDialog = new DemoChecklistDialog(checklist) { StartPosition = FormStartPosition.Manual };

      // Beside the race rather than over it: the Riders tab and Fix laps are what it is about.
      var area = Screen.FromControl(this).WorkingArea;
      _demoChecklistDialog.Location = new Point(
        Math.Max(area.Left, Math.Min(Right - _demoChecklistDialog.Width - 16, area.Right - _demoChecklistDialog.Width)),
        Math.Max(area.Top, Top + 90));

      _demoChecklistDialog.Show(this);
    }
    else if (!_demoChecklistDialog.Visible)
    {
      _demoChecklistDialog.Visible = true;
    }

    _demoChecklistDialog.Render();
    _demoChecklistDialog.Activate();
  }

  /// <summary>What the checklist needs to see of the race, copied under the lock.</summary>
  private DemoRaceView DemoRace()
  {
    lock (ridersLock)
    {
      return new DemoRaceView(
        _demoReader?.StartedAt,
        DateTime.Now,
        riders.ToDictionary(p => p.Key, p => CloneRiderForDisplay(p.Value)),
        ignoredTags.ToHashSet(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, string>(tagAliases),
        rejectedReads.ToList());
    }
  }

  /// <summary>When a class left the gate, asked from the demo reader's thread.</summary>
  private DateTime? DemoWaveStartedAt(string className)
  {
    lock (ridersLock)
    {
      return waves?.StartTimeFor(className);
    }
  }

  private void StopDemoReader()
  {
    _demoReaderStop?.Cancel();
    _demoChecklistTimer?.Stop();
  }
}
