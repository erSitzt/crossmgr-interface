using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CrossMgrInterface.Tests;

/// <summary>
/// The screens the help shows, each built from the real control with sample data.
///
/// The riders are the race demo's - fictional, and the same every time - so a
/// regenerated picture only changes where the screen itself changed. The map
/// scenes use the club's GSC circuit, saved beside this file. CLAUDE.md lists
/// which source files each scene depends on.
/// </summary>
internal static class HelpScreenshotScenes
{
  public static IReadOnlyList<string> Names { get; } = new[]
  {
    "race-day", "new-race-wizard", "fix-laps", "unknown-transponder",
    "track-map", "circuit-editor", "past-sessions", "reader-settings", "demo-picker",
    "publish-results", "rider-list", "spectator-screen"
  };

  public static HelpScene Build(string name) => name switch
  {
    "race-day" => RaceDay(),
    "new-race-wizard" => NewRaceWizard(),
    "fix-laps" => FixLaps(),
    "unknown-transponder" => UnknownTransponder(),
    "track-map" => TrackMap(),
    "circuit-editor" => CircuitEditor(),
    "past-sessions" => PastSessions(),
    "reader-settings" => new HelpScene { Form = new ReaderSettingsDialog(53135, true, true, 60) },
    "demo-picker" => new HelpScene { Form = new DemoPickerDialog(DemoScenarios.All) },
    "publish-results" => PublishResults(),
    "rider-list" => RiderList(),
    "spectator-screen" => SpectatorScreen(),
    _ => throw new ArgumentException($"There is no help screenshot scene called {name}.", nameof(name))
  };

  // ---- Sample data -----------------------------------------------------------

  private static readonly DemoScenario RaceDemo = DemoScenarios.Build(DemoScenarios.RaceId);

  /// <summary>The race demo's riders five minutes in, each at their own pace with a little variation.</summary>
  private static List<RiderInfo> RaceField()
  {
    var riders = new List<RiderInfo>();

    for (var i = 0; i < RaceDemo.Roster.Count; i++)
    {
      var entry = RaceDemo.Roster[i];
      var pace = 46.5 + i * 0.8;
      var builder = RiderBuilder.Rider(entry.Tag, entry.Number, entry.Name).Category(entry.Class);

      for (var lap = 0; lap < (int)(320 / pace); lap++)
        builder.Lap(pace + (lap * 7 + i * 3) % 5 * 0.35);

      var rider = builder.Build();
      rider.Team = entry.Team;
      riders.Add(rider);
    }

    return riders;
  }

  private static (string First, string Last) SplitName(string name)
  {
    var parts = name.Split(' ', 2);
    return (parts[0], parts.Length > 1 ? parts[1] : "");
  }

  private static TrackDefinition Gsc() =>
    TrackStore.ImportJson(File.ReadAllText(Path.Combine(
      HelpScreenshotHarness.RepoRoot, "CrossMgrInterface.Tests", "HelpScreenshots", "gsc.cmtrack")))
    ?? throw new InvalidOperationException("gsc.cmtrack could not be read.");

  /// <summary>
  /// Writing: the application's own tile cache, online, so the map looks as it does
  /// on the laptop. Otherwise an empty cache with the network off, so a CI run
  /// never asks the tile servers for anything.
  /// </summary>
  private static TileSessionOptions? TileOptions() => HelpScreenshotHarness.Writing
    ? null
    : new TileSessionOptions(Path.Combine(Path.GetTempPath(), "crossmgr-help-tiles"), AllowNetwork: false);

  /// <summary>A tab page in a window of its own, looking as it does in the application.</summary>
  private static Form Host(string title, TabPage page, Size size)
  {
    var form = new Form { Text = title, ClientSize = size };
    var tabs = new TabControl { Dock = DockStyle.Fill };
    tabs.TabPages.Add(page);
    form.Controls.Add(tabs);
    return form;
  }

  // ---- Scenes ------------------------------------------------------------------

  private static HelpScene RaceDay()
  {
    var view = new RaceDayView();
    var form = Host("Race Day", view.CreateRaceDayTab(), new Size(980, 660));
    var sorted = PositionCalculator.GetSortedRidersFromSnapshot(RaceField());

    view.SetClock(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(38), null, TimeSpan.FromMinutes(20));
    view.SetWaves(null, null);
    view.SetState(RaceDayState.Running, $"Started 14:00 · {sorted.Count} riders");
    view.SetReaderHealth(true, 1, DateTime.Now.AddSeconds(-2), null);
    view.SetLiveStatus(LiveTileState.Sending, "Sending", "sent 3 s ago", canToggle: true, isOn: true);
    view.SetLeaderboard(sorted);
    view.SetChecklist("Moto 1 - MX1 / MX2", sorted.Count, TimeSpan.FromMinutes(20), true);

    return new HelpScene { Form = form };
  }

  private static HelpScene NewRaceWizard()
  {
    var result = new ImportResult
    {
      ImportedCount = RaceDemo.Roster.Count,
      DetectedColumns = new List<string> { "tagid", "number", "firstname", "lastname", "team", "class" }
    };

    foreach (var entry in RaceDemo.Roster)
    {
      var (first, last) = SplitName(entry.Name);
      result.Riders.Add(new RiderDataImporter.RiderImportData
      {
        TagID = entry.Tag, RiderNumber = entry.Number, FirstName = first, LastName = last,
        Team = entry.Team, Category = entry.Class
      });
    }

    var wizard = new NewRaceWizard(_ => result, 0, readerRunning: true, SessionType.Race,
      classes: () => RaceDemo.Classes, rows: () => result.Riders);

    wizard.ShowImport(@"C:\Club\riders-moto1.xlsx", result);
    wizard.Show(2);

    return new HelpScene { Form = wizard };
  }

  /// <summary>
  /// The race demo's list with two mistakes a club secretary makes: a start
  /// number typed twice, and a rider whose class was left blank.
  /// </summary>
  private static HelpScene RiderList()
  {
    var rows = RaceDemo.Roster
      .Select(entry =>
      {
        var (first, last) = SplitName(entry.Name);
        return new RiderDataImporter.RiderImportData
        {
          TagID = entry.Tag, RiderNumber = entry.Number, FirstName = first, LastName = last, Category = entry.Class
        };
      })
      .ToList();

    rows[5].RiderNumber = rows[2].RiderNumber;
    rows[9].Category = "";

    var dialog = new RiderListDialog(new RiderListSource(rows, Array.Empty<(int, string)>()), teamEvent: false,
      save: _ => null, importAgain: () => null)
    {
      ClientSize = new Size(1000, 560)
    };
    dialog.SelectRow(5);

    return new HelpScene { Form = dialog };
  }

  /// <summary>The race demo five minutes in, as the crowd sees it: top 10, the fastest lap, the last crossings.</summary>
  private static HelpScene SpectatorScreen()
  {
    var field = PositionCalculator.GetSortedRidersFromSnapshot(RaceField());
    var window = new SpectatorWindow { RowLimit = 10 };
    window.PlaceOn(Screen.PrimaryScreen!, fullScreen: false);
    window.ClientSize = new Size(1280, 720);

    window.ShowBoard(SpectatorBoardBuilder.Build(new SpectatorInputs
    {
      Field = field,
      SessionType = SessionType.Race,
      Waves = false,
      Title = "Moto 1 - MX1 / MX2",
      State = RaceDayState.Running,
      Remaining = TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(38),
      Duration = TimeSpan.FromMinutes(20),
      RowLimit = 10
    }));

    return new HelpScene { Form = window };
  }

  private static HelpScene FixLaps()
  {
    // #101 is the demo rider whose transponder is missed once.
    var entry = RaceDemo.Roster.First(r => r.Number == "101");
    var rider = RiderBuilder.Rider(entry.Tag, entry.Number, entry.Name).Category(entry.Class)
      .Lap(52.4).Lap(51.8).Lap(52.9).Lap(52.1).Lap(104.6).Lap(52.3)
      .Build();

    var missed = rider.Laps[4];
    missed.IsSuggestedForSplit = true;
    missed.SuggestedSplitCount = 2;
    missed.SuggestedSplitLapTime = TimeSpan.FromSeconds(52.3);

    var riders = new Dictionary<string, RiderInfo> { [rider.TagID] = rider };
    var rejected = new[]
    {
      new RejectedRead
      {
        TagID = rider.TagID,
        CrossingTime = rider.Laps[1].CrossingTime.AddSeconds(4),
        GapToPrevious = TimeSpan.FromSeconds(4),
        Reason = $"Only {4.0:F1}s after the previous read"
      }
    };

    var service = new RaceCorrectionService(riders, new object(), () => RiderBuilder.RaceStart, _ => { });
    // Changing a class is the main window's job, and there is no main window
    // here; the button only has to be on screen, not to work.
    var dialog = new LapCorrectionDialog(service, rider.TagID,
      tag => riders.GetValueOrDefault(tag), _ => rejected, () => RiderBuilder.RaceStart,
      _ => false);

    return new HelpScene { Form = dialog };
  }

  private static HelpScene UnknownTransponder()
  {
    // #171 left his transponder at home and rides on the demo's spare.
    var roster = RaceDemo.Roster
      .Select(r =>
      {
        var (first, last) = SplitName(r.Name);
        return new RiderImportRosterEntry(r.Tag, r.Number, first, last, r.Team, r.Class) { Unused = r.Number == "171" };
      })
      .ToList();

    var active = RaceField().Where(r => r.RiderNumber != "171").ToList();
    var dialog = new AssignTagDialog(DemoScenarios.SpareTransponder, 5, roster, active);

    return new HelpScene { Form = dialog };
  }

  private static HelpScene TrackMap()
  {
    var track = Gsc();
    var view = new TrackTabView(TileProvider.OpenStreetMap, MapLabelParts.Position | MapLabelParts.Number,
      null, TileOptions());
    var form = Host("Track", view.CreateTrackTab(), new Size(1100, 700));

    view.SetTracks(new[] { track }, track.Id);
    view.SetTrack(track);
    view.SetClasses(RaceDemo.Classes);

    var field = PositionCalculator.GetSortedRidersFromSnapshot(RaceField());
    var markers = new List<MapRiderMarker>();
    var counts = new int[track.Sectors.Count];
    var leaders = new string?[track.Sectors.Count];

    for (var i = 0; i < field.Count; i++)
    {
      var rider = field[i];

      // Strung out round the loop behind the leader, as a field is five minutes in.
      var fraction = ((0.64 - i * 0.047) % 1 + 1) % 1;
      var state = rider.RiderNumber == "150" ? TrackPositionState.Overdue : TrackPositionState.OnTrack;
      if (state == TrackPositionState.Overdue) fraction = 0.995;

      var at = track.Geometry.PointAtFraction(fraction);

      markers.Add(new MapRiderMarker(
        rider.TagID, rider.RiderNumber, rider.Label, rider.LastName, rider.Category,
        at.Location, at.HeadingDegrees, i + 1, state, fraction,
        state == TrackPositionState.Overdue ? "+14s" : null, Highlighted: false));

      var sector = track.SectorIndexAt(fraction);
      if (sector < 0) continue;

      counts[sector]++;
      leaders[sector] ??= rider.RiderNumber;
    }

    view.SetField(markers);
    view.SetSectorInfo(track.Sectors
      .Select((s, i) => new MapSectorInfo(i, s.Name, s.Color, counts[i], leaders[i]))
      .ToList());
    view.SetWatermark(null);

    var scene = new HelpScene
    {
      Form = form,

      // Framed again once the window exists. Tiles asked for while the tab was
      // still being built come back before there is a handle to deliver them to;
      // the layer lets them go, and only a camera move asks for them again.
      AfterShown = () => view.Renderer.FitTrack(),
      Ready = () => view.Renderer.PendingTileCount == 0
    };
    scene.Owned.Add(view);
    return scene;
  }

  private static HelpScene CircuitEditor()
  {
    var track = Gsc();
    track.ReferenceImage = ClubPlan(track);

    // Never saved: the store only writes when Save circuit is pressed.
    var store = TrackStore.Load(Path.Combine(Path.GetTempPath(), "crossmgr-help-" + Guid.NewGuid().ToString("N"), "tracks.json"));
    var editor = new TrackEditorDialog(store, track, TileProvider.OpenStreetMap, null, null, TileOptions());

    return new HelpScene
    {
      Form = editor,
      AfterShown = () =>
      {
        editor.ShowAlignImageTool();
        editor.Renderer.FitTrack();
      },
      Ready = () => editor.Renderer.PendingTileCount == 0
    };
  }

  // Wider than it opens: at its opening size the Status column is cut off, and
  // that column is half of what the picture is there to show.
  private static HelpScene PastSessions() =>
    new() { Form = new SessionManagerDialog(new SampleSessions()) { ClientSize = new Size(1100, 420) } };

  /// <summary>
  /// The publish window as it looks before anything is sent - the screen a club
  /// needs to be able to point at when asked what leaves the laptop.
  ///
  /// Built from the race demo's fictional riders and a stub publisher, so the
  /// picture needs no network, no key, and shows no credential.
  /// </summary>
  private static HelpScene PublishResults()
  {
    var field = RaceField().ToDictionary(r => r.TagID, r => r);
    var rules = new RaceRules
    {
      SessionType = SessionType.Race,
      Duration = TimeSpan.FromMinutes(20),
      AdditionalLaps = 2,
      DnfTimeoutMinutes = 2,
      MinimumLapSeconds = 10,
      ManualStart = false
    };

    var start = new DateTime(2026, 9, 12, 13, 30, 0);
    var report = new RaceReportGenerator().PrepareReportData(
      field, start, start.AddMinutes(23), TimeSpan.FromMinutes(20),
      true, "Moto 1 - MX1 / MX2", rules: rules);

    var session = PublishPayloadBuilder.Build(new PublishInputs
    {
      Report = report,
      PublicId = "b3f1c0de4a7f4e2b9d1c8e5f0a6b3d20",
      SessionType = SessionType.Race,
      Track = Gsc()
    });

    var request = new PublishRequest
    {
      Session = session,
      SiteName = "results.openlaptime.de",
      Riders = session.Entries.Count,
      Laps = session.Entries.Sum(e => e.LapTimes.Count),
      CircuitName = "GSC"
    };

    return new HelpScene { Form = new PublishResultsDialog(request, new StubPublisher()) };
  }

  /// <summary>Configured, so the picture shows the summary rather than the set-up message. Never sends.</summary>
  private sealed class StubPublisher : IResultsPublisher
  {
    public bool IsConfigured => true;

    public Task<PublishOutcome> PublishAsync(PublishedSession session, IProgress<PublishProgress>? progress,
      CancellationToken cancellationToken) =>
      Task.FromResult(new PublishOutcome(PublishStatus.Published, "Published."));

    public Task<PublishOutcome> TestAsync(CancellationToken cancellationToken) =>
      Task.FromResult(new PublishOutcome(PublishStatus.Published, "The website answered."));
  }

  // ---- Helpers for the scenes ----------------------------------------------------

  /// <summary>
  /// A plan of the circuit as a club might draw it, standing in for the picture an
  /// operator imports. Drawn here rather than taken from a real map screenshot,
  /// whose imagery is not ours to publish. Placed a little off and turned a few
  /// degrees, because the picture shows it being lined up.
  /// </summary>
  private static TrackReferenceImage ClubPlan(TrackDefinition track)
  {
    const int zoom = 17;
    const int margin = 90;

    var world = track.Points.Select(p => TileMath.ToWorldPixel(p, zoom)).ToList();
    var minX = world.Min(p => p.X) - margin;
    var minY = world.Min(p => p.Y) - margin;
    var width = (int)Math.Ceiling(world.Max(p => p.X) + margin - minX);
    var height = (int)Math.Ceiling(world.Max(p => p.Y) + margin - minY);

    byte[] png;
    using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
    {
      using (var g = Graphics.FromImage(bitmap))
      {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(247, 242, 226));

        using var grid = new Pen(Color.FromArgb(222, 212, 184), 1);
        for (var x = 0; x < width; x += 40) g.DrawLine(grid, x, 0, x, height);
        for (var y = 0; y < height; y += 40) g.DrawLine(grid, 0, y, width, y);

        var loop = world.Select(p => new PointF((float)(p.X - minX), (float)(p.Y - minY))).ToArray();
        using var line = new Pen(Color.FromArgb(196, 42, 30), 8) { LineJoin = LineJoin.Round };
        g.DrawPolygon(line, loop);

        using var title = new Font("Segoe UI", 18, FontStyle.Bold);
        using var label = new Font("Segoe UI", 12, FontStyle.Bold);
        g.DrawString("Club plan", title, Brushes.SaddleBrown, 14, 10);
        g.DrawString("START", label, Brushes.SaddleBrown, loop[0].X + 12, loop[0].Y + 8);
      }

      using var stream = new MemoryStream();
      bitmap.Save(stream, ImageFormat.Png);
      png = stream.ToArray();
    }

    var centre = TileMath.FromWorldPixel(new PointD(minX + width / 2.0 + 22, minY + height / 2.0 - 16), zoom);

    return new TrackReferenceImage
    {
      ImageData = png,
      SourceName = "club-plan.png",
      PixelWidth = width,
      PixelHeight = height,
      Center = centre,
      Scale = 1.0 / (1L << zoom),
      RotationDegrees = 5
    };
  }

  /// <summary>A club's Saturday: practice, qualifying and two motos.</summary>
  private sealed class SampleSessions : ISessionManagerHost
  {
    private readonly List<SessionSummary> _sessions;

    public SampleSessions()
    {
      var day = new DateTime(2026, 9, 12);
      _sessions = new List<SessionSummary>
      {
        // The morning's sessions are already on the website and the afternoon's
        // are not, which is what the Published column is there to show.
        Session(4, "Moto 2 - MX1 / MX2", day.AddHours(15).AddMinutes(10), 20, SessionType.Race, 16, 214),
        Session(3, "Moto 1 - MX1 / MX2", day.AddHours(13).AddMinutes(30), 20, SessionType.Race, 16, 221),
        Session(2, "Timed qualifying", day.AddHours(11), 15, SessionType.TimedQualifying, 16, 187,
          published: day.AddHours(11).AddMinutes(22)),
        Session(1, "Free practice", day.AddHours(9).AddMinutes(30), 15, SessionType.FreePractice, 15, 164,
          published: day.AddHours(9).AddMinutes(51))
      };
    }

    private static SessionSummary Session(int id, string name, DateTime start, int minutes, SessionType type,
      int riders, int laps, DateTime? published = null) =>
      new(new DbRace
      {
        Id = id,
        Name = name,
        StartTime = start,
        EndTime = start.AddMinutes(minutes + 3),
        Duration = TimeSpan.FromMinutes(minutes),
        IsFinished = true,
        SessionType = type,
        PublishedAt = published
      }, riders, laps);

    public IReadOnlyList<SessionSummary> ListSessions() => _sessions;
    public int? CurrentSessionId => 4;
    public bool SessionRunning => false;
    public void PrintResults(SessionSummary session) { }
    public void PublishResults(SessionSummary session) { }
    public bool PublishingAvailable => true;
    public bool OpenSession(SessionSummary session) => false;
    public void RenameSession(SessionSummary session, string name) { }
    public bool DeleteSession(IWin32Window owner, SessionSummary session) => false;
  }
}
