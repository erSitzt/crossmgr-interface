namespace CrossMgrInterface;

/// <summary>
/// The Track tab: a circuit with the field on it.
///
/// Built in code, like RaceDayView and the lap progression tab, so the form's own
/// designer layout is untouched.
///
/// This screen is a display, not a working grid - the same argument RaceDayView
/// makes about its leaderboard. Everything that edits a circuit lives behind the
/// "Set up circuit..." button in a modal, so a mis-click during a race cannot drag
/// the start/finish line and silently corrupt every rider position on screen.
///
/// Filtering happens here, after the solver, which returns the whole field. The
/// class filter is deliberately separate from the riders grid's: the grid filter
/// is a working tool for the operator, this one is a display for a commentator,
/// and narrowing one must not silently empty the other.
/// </summary>
public sealed class TrackTabView : IDisposable
{
  private const string AllClasses = "All classes";

  private readonly Action<string>? _log;

  private Panel _mapPanel = null!;
  private TileSession _session = null!;
  private TrackMapRenderer _renderer = null!;

  private ComboBox _trackCombo = null!;
  private ComboBox _mapCombo = null!;
  private Button _editButton = null!;
  private Button _renameButton = null!;
  private Button _deleteButton = null!;
  private Button _fitButton = null!;
  private Button _exportButton = null!;
  private ComboBox _classCombo = null!;
  private ComboBox _fieldCombo = null!;
  private CheckBox _sectorPanel = null!;
  private CheckBox _legend = null!;
  private ListView _leaderList = null!;
  private TableLayoutPanel _mapArea = null!;
  private CheckBox _labelPosition = null!;
  private CheckBox _labelNumber = null!;
  private CheckBox _labelName = null!;
  private TextBox _findBox = null!;
  private CheckBox _showFinished = null!;
  private CheckBox _showRetired = null!;
  private CheckBox _showLongOverdue = null!;
  private CheckBox _showNotStarted = null!;
  private Label _countLabel = null!;

  private IReadOnlyList<MapRiderMarker> _field = Array.Empty<MapRiderMarker>();
  private bool _suppressTrackEvent;

  public TrackTabView(TileProvider provider, MapLabelParts labelParts, Action<string>? log = null)
  {
    _log = log;
    _startProvider = provider;
    InitialLabelParts = labelParts;
  }

  private readonly TileProvider _startProvider;

  /// <summary>Read once while the toolbar is built, before the checkboxes exist.</summary>
  private MapLabelParts InitialLabelParts { get; }

  public TabPage? Page { get; private set; }
  public TrackMapRenderer Renderer => _renderer;
  public TileProvider Provider => _session.Provider;
  public double LastPaintMicroseconds => _renderer.LastPaintMicroseconds;

  /// <summary>Supplies the lines for the selection card. Set by the form, which owns rider data.</summary>
  public Func<string, IReadOnlyList<string>>? DescribeRider { get; set; }

  public event EventHandler? SetupRequested;
  public event EventHandler? NewTrackRequested;
  public event EventHandler? RenameRequested;
  public event EventHandler? DeleteRequested;
  public event EventHandler? ExportRequested;
  public event EventHandler<TileProvider>? BasemapChosen;
  public event EventHandler<MapLabelParts>? LabelPartsChanged;
  public event EventHandler<string>? TrackChosen;
  public event EventHandler<string>? RiderActivated;

  public TabPage CreateTrackTab()
  {
    var page = new TabPage("Track") { Name = "tabPageTrack", BackColor = Color.White };

    var root = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 2,
      BackColor = Color.White
    };
    root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

    root.Controls.Add(BuildToolbar(), 0, 0);
    root.Controls.Add(BuildMap(), 0, 1);

    page.Controls.Add(root);
    Page = page;
    return page;
  }

  // ---- Construction --------------------------------------------------------

  private Control BuildToolbar()
  {
    // A flow panel across the top rather than a side panel: the map wants the
    // width, and this wraps rather than clipping on a smaller screen.
    var bar = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill,
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      FlowDirection = FlowDirection.LeftToRight,
      WrapContents = true,
      Padding = new Padding(8, 6, 8, 6),
      BackColor = Color.FromArgb(248, 248, 248)
    };

    bar.Controls.Add(Caption("Circuit:"));

    _trackCombo = new ComboBox
    {
      DropDownStyle = ComboBoxStyle.DropDownList,
      Width = 190,
      Margin = new Padding(0, 2, 10, 2)
    };
    _trackCombo.SelectedIndexChanged += (_, _) =>
    {
      if (_suppressTrackEvent) return;
      if (_trackCombo.SelectedItem is TrackChoice choice) TrackChosen?.Invoke(this, choice.Id);
    };
    bar.Controls.Add(_trackCombo);

    _editButton = new Button
    {
      Text = "Edit circuit...",
      AutoSize = true,
      Margin = new Padding(0, 1, 4, 1),
      Enabled = false
    };
    _editButton.Click += (_, _) => SetupRequested?.Invoke(this, EventArgs.Empty);
    bar.Controls.Add(_editButton);

    var create = new Button
    {
      Text = "New circuit...",
      AutoSize = true,
      Margin = new Padding(0, 1, 4, 1)
    };
    create.Click += (_, _) => NewTrackRequested?.Invoke(this, EventArgs.Empty);
    bar.Controls.Add(create);

    _renameButton = new Button { Text = "Rename...", AutoSize = true, Margin = new Padding(0, 1, 4, 1), Enabled = false };
    _renameButton.Click += (_, _) => RenameRequested?.Invoke(this, EventArgs.Empty);
    bar.Controls.Add(_renameButton);

    _deleteButton = new Button { Text = "Delete...", AutoSize = true, Margin = new Padding(0, 1, 4, 1), Enabled = false };
    _deleteButton.Click += (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty);
    bar.Controls.Add(_deleteButton);

    // Sharing a circuit should not mean opening an editor first.
    _exportButton = new Button { Text = "Export...", AutoSize = true, Margin = new Padding(0, 1, 18, 1), Enabled = false };
    _exportButton.Click += (_, _) => ExportRequested?.Invoke(this, EventArgs.Empty);
    bar.Controls.Add(_exportButton);

    _fitButton = new Button
    {
      Text = "Fit to circuit",
      AutoSize = true,
      Margin = new Padding(0, 1, 18, 1),
      Enabled = false
    };
    _fitButton.Click += (_, _) => _renderer.FitTrack();
    bar.Controls.Add(_fitButton);

    bar.Controls.Add(Caption("Map:"));

    _mapCombo = new ComboBox
    {
      DropDownStyle = ComboBoxStyle.DropDownList,
      Width = 165,
      Margin = new Padding(0, 2, 18, 2)
    };
    foreach (var provider in TileProvider.All) _mapCombo.Items.Add(provider);
    _mapCombo.SelectedIndexChanged += (_, _) => SwitchBasemap();
    bar.Controls.Add(_mapCombo);

    bar.Controls.Add(Caption("Showing:"));

    _classCombo = new ComboBox
    {
      DropDownStyle = ComboBoxStyle.DropDownList,
      Width = 150,
      Margin = new Padding(0, 2, 14, 2)
    };
    _classCombo.Items.Add(AllClasses);
    _classCombo.SelectedIndex = 0;
    _classCombo.SelectedIndexChanged += (_, _) => Repaint();
    bar.Controls.Add(_classCombo);

    bar.Controls.Add(Caption("Field:"));

    // Past about a hundred riders the dots stop distinguishing anybody, so being
    // able to cut straight to the front is what keeps the screen worth looking at.
    _fieldCombo = new ComboBox
    {
      DropDownStyle = ComboBoxStyle.DropDownList,
      Width = 105,
      Margin = new Padding(0, 2, 14, 2)
    };
    _fieldCombo.Items.AddRange(new object[] { "Everyone", "Top 3", "Top 10", "Top 20", "Top 50" });
    _fieldCombo.SelectedIndex = 0;
    _fieldCombo.SelectedIndexChanged += (_, _) => Repaint();
    bar.Controls.Add(_fieldCombo);

    _sectorPanel = Check("Sector counts", true);
    _legend = Check("Legend", true);

    _showFinished = Check("Finished", true);
    _showRetired = Check("Retired", false);
    _showLongOverdue = Check("Long overdue", false);
    _showNotStarted = Check("Not started", false);

    foreach (var box in new[] { _sectorPanel, _legend, _showFinished, _showRetired, _showLongOverdue, _showNotStarted })
      bar.Controls.Add(box);

    bar.Controls.Add(Caption("Find:"));

    // "Where is 27?" - the question a commentator actually asks.
    _findBox = new TextBox { Width = 70, Margin = new Padding(0, 2, 14, 2) };
    _findBox.TextChanged += (_, _) => Repaint();
    bar.Controls.Add(_findBox);

    bar.Controls.Add(Caption("Label with:"));

    _labelPosition = Check("Position", InitialLabelParts.HasFlag(MapLabelParts.Position));
    _labelNumber = Check("Number", InitialLabelParts.HasFlag(MapLabelParts.Number));
    _labelName = Check("Name", InitialLabelParts.HasFlag(MapLabelParts.Name));

    foreach (var box in new[] { _labelPosition, _labelNumber, _labelName })
      bar.Controls.Add(box);

    _countLabel = new Label
    {
      AutoSize = true,
      ForeColor = Color.DimGray,
      Margin = new Padding(0, 6, 0, 0)
    };
    bar.Controls.Add(_countLabel);

    return bar;
  }

  private static Label Caption(string text) => new()
  {
    Text = text,
    AutoSize = true,
    Margin = new Padding(0, 6, 4, 0)
  };

  private CheckBox Check(string text, bool checkedByDefault)
  {
    var box = new CheckBox
    {
      Text = text,
      Checked = checkedByDefault,
      AutoSize = true,
      Margin = new Padding(0, 4, 10, 0)
    };
    box.CheckedChanged += (_, _) => Repaint();
    return box;
  }

  /// <summary>A running order is only worth the width when the field is narrowed to one.</summary>
  public bool WantsLeaderboard => FieldLimit() is > 0 and <= 20;

  /// <summary>
  /// How many rows the running order should hold, or 0 for none.
  ///
  /// The list has to apply the same limit as the map: it is built from the full
  /// solved field, not from the filtered markers, so without this it happily
  /// listed all 250 riders while the map showed ten.
  /// </summary>
  public int LeaderboardLimit => WantsLeaderboard ? FieldLimit() : 0;

  private const int LeaderboardWidth = 320;

  /// <summary>Twice a second is quick enough to feel live and slow enough not to flicker.</summary>
  private const int LeaderboardIntervalMs = 500;

  private DateTime _lastLeaderboardUpdate = DateTime.MinValue;

  private Control BuildMap()
  {
    // A two-column grid rather than docking: with Dock, the fill and the docked
    // panel resolve in Controls-collection order, which is easy to get subtly
    // wrong. A column whose width goes to zero is unambiguous.
    _mapArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
    _mapArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    _mapArea.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
    _mapArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

    _leaderList = new ListView
    {
      Dock = DockStyle.Fill,
      View = View.Details,
      FullRowSelect = true,
      HeaderStyle = ColumnHeaderStyle.Nonclickable,
      MultiSelect = false,
      HideSelection = false,
      Visible = false
    };

    // ListView repaints itself on any change and has no public double-buffering,
    // so a list updating several times a second tears visibly without this.
    typeof(ListView)
      .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance |
                                     System.Reflection.BindingFlags.NonPublic)
      ?.SetValue(_leaderList, true);
    _leaderList.Columns.Add("Pos", 38);
    _leaderList.Columns.Add("No", 46);
    _leaderList.Columns.Add("Rider", 96);
    _leaderList.Columns.Add("Lap", 38);
    _leaderList.Columns.Add("Status", 92);

    _mapPanel = new Panel { Dock = DockStyle.Fill, BackColor = MapDrawResources.LandColor };

    _session = new TileSession(_mapPanel, _startProvider, _log);
    _mapCombo.SelectedItem = TileProvider.All.First(p => p.Id == _session.Provider.Id);

    _renderer = new TrackMapRenderer(_mapPanel, _session)
    {
      EmptyStateText = "No circuit yet. Pan and zoom to your venue, then click \"New circuit...\"."
    };

    _mapPanel.MouseEnter += (_, _) => _mapPanel.Focus();

    _renderer.Picked += OnPicked;
    _renderer.MapClicked += (_, _) =>
    {
      // Clicking empty map clears the selection card, which is what makes it a
      // sticky panel rather than something that has to be dismissed.
      _renderer.SelectedTagId = null;
      _renderer.Callout = null;
      _mapPanel.Invalidate();
    };

    // Picking a row selects that rider on the map, so "who is P4?" is one click.
    _leaderList.SelectedIndexChanged += (_, _) =>
    {
      if (_leaderList.SelectedItems.Count == 0) return;
      if (_leaderList.SelectedItems[0].Tag is not string tagId) return;

      _renderer.SelectedTagId = tagId;
      _renderer.Callout = DescribeRider?.Invoke(tagId);
      _mapPanel.Invalidate();
    };

    _mapArea.Controls.Add(_mapPanel, 0, 0);
    _mapArea.Controls.Add(_leaderList, 1, 0);

    return _mapArea;
  }

  private void ShowLeaderboardColumn(bool show)
  {
    var width = show ? LeaderboardWidth : 0;
    if (Math.Abs(_mapArea.ColumnStyles[1].Width - width) < 0.5f) return;

    _mapArea.ColumnStyles[1].Width = width;
    _leaderList.Visible = show;
  }

  /// <summary>
  /// Refreshes the running order in place.
  ///
  /// Rows are never added or removed for an ordinary update - position N is
  /// always row N, so only the text changes. Rebuilding the list eight times a
  /// second would flicker and would throw away the selection every frame.
  /// </summary>
  public void SetLeaderboard(IReadOnlyList<TrackLeaderRow> rows)
  {
    if (!_leaderList.Visible) return;

    // The map wants eight frames a second; a table of names does not, and
    // redrawing text that often is what reads as flicker rather than as liveness.
    if ((DateTime.Now - _lastLeaderboardUpdate).TotalMilliseconds < LeaderboardIntervalMs) return;
    _lastLeaderboardUpdate = DateTime.Now;

    _leaderList.BeginUpdate();
    try
    {
      while (_leaderList.Items.Count > rows.Count)
        _leaderList.Items.RemoveAt(_leaderList.Items.Count - 1);

      while (_leaderList.Items.Count < rows.Count)
        _leaderList.Items.Add(new ListViewItem(new[] { "", "", "", "", "" }));

      for (var i = 0; i < rows.Count; i++)
      {
        var row = rows[i];
        var item = _leaderList.Items[i];

        Set(item, 0, row.Position.ToString());
        Set(item, 1, row.Number);
        Set(item, 2, row.Name);
        Set(item, 3, row.Laps.ToString());
        Set(item, 4, row.State);

        if (item.Tag as string != row.TagId) item.Tag = row.TagId;

        var selected = row.TagId == _renderer.SelectedTagId;
        if (item.Selected != selected) item.Selected = selected;
      }
    }
    finally
    {
      _leaderList.EndUpdate();
    }
  }

  /// <summary>Only touches the control when the text differs - a ListView redraws on every set.</summary>
  private static void Set(ListViewItem item, int index, string text)
  {
    if (item.SubItems[index].Text != text) item.SubItems[index].Text = text;
  }

  private void OnPicked(object? sender, MapPickEventArgs e)
  {
    if (e.Element.Kind == MapHitKind.RiderCluster)
    {
      // Clicking a bunch zooms into it rather than picking an arbitrary member.
      _renderer.ZoomBy(1, e.Screen);
      return;
    }

    if (e.Element.Kind != MapHitKind.RiderDot) return;

    if (e.DoubleClick)
    {
      RiderActivated?.Invoke(this, e.Element.TagId);
      return;
    }

    _renderer.SelectedTagId = e.Element.TagId;
    _renderer.Callout = DescribeRider?.Invoke(e.Element.TagId);
    _mapPanel.Invalidate();
  }

  // ---- What the form sets --------------------------------------------------

  private sealed record TrackChoice(string Id, string Name)
  {
    public override string ToString() => Name;
  }

  public void SetTracks(IReadOnlyList<TrackDefinition> tracks, string? selectedId)
  {
    _suppressTrackEvent = true;
    try
    {
      _trackCombo.Items.Clear();
      foreach (var track in tracks)
        _trackCombo.Items.Add(new TrackChoice(track.Id, string.IsNullOrWhiteSpace(track.Name) ? "(unnamed)" : track.Name));

      var index = tracks.ToList().FindIndex(t => t.Id == selectedId);
      _trackCombo.SelectedIndex = index >= 0 ? index : tracks.Count > 0 ? 0 : -1;
      _trackCombo.Enabled = tracks.Count > 0;
    }
    finally
    {
      _suppressTrackEvent = false;
    }
  }

  public void SetTrack(TrackDefinition? track)
  {
    var changed = !ReferenceEquals(_renderer.Track, track);
    _renderer.Track = track;

    _editButton.Enabled = _renameButton.Enabled = _deleteButton.Enabled = track is not null;
    _exportButton.Enabled = track is { IsUsable: true };
    _fitButton.Enabled = track is { IsUsable: true };

    _renderer.EmptyStateText = track is null
      ? "No circuit yet. Pan and zoom to your venue, then click \"New circuit...\"."
      : null;

    // Only on a genuine change of circuit. The renderer keeps the loop framed from
    // there until the operator pans or zooms, so this must not fire every render.
    if (changed && track is { IsUsable: true }) _renderer.FitTrack();

    _mapPanel.Invalidate();
  }

  public void SetClasses(IReadOnlyList<string> classes)
  {
    var current = _classCombo.SelectedItem as string ?? AllClasses;

    _classCombo.BeginUpdate();
    _classCombo.Items.Clear();
    _classCombo.Items.Add(AllClasses);
    foreach (var name in classes.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c))
      _classCombo.Items.Add(name);

    var index = _classCombo.Items.IndexOf(current);
    _classCombo.SelectedIndex = index >= 0 ? index : 0;
    _classCombo.EndUpdate();
  }

  /// <summary>The whole field, unfiltered. Filtering is this class's job.</summary>
  public void SetField(IReadOnlyList<MapRiderMarker> field)
  {
    _field = field;
    ApplyFilters();
  }

  public void SetWatermark(string? text)
  {
    if (_renderer.Watermark == text) return;

    _renderer.Watermark = text;
    _mapPanel.Invalidate();
  }

  public void Invalidate() => _mapPanel.Invalidate();

  /// <summary>Adopts a basemap chosen elsewhere - the editor has its own picker.</summary>
  public void SetBasemap(TileProvider provider)
  {
    if (provider.Id == _session.Provider.Id) return;

    _mapCombo.SelectedItem = TileProvider.All.First(p => p.Id == provider.Id);
  }

  private void SwitchBasemap()
  {
    if (_mapCombo.SelectedItem is not TileProvider provider) return;
    if (_session is null || provider.Id == _session.Provider.Id) return;

    // The whole tile stack goes at once - cache folder, backoff state and decoded
    // tiles are all per-provider, and a half-swapped map draws the old imagery.
    var previous = _session;
    _session = new TileSession(_mapPanel, provider, _log);
    _renderer.SetTiles(_session);
    previous.Dispose();

    BasemapChosen?.Invoke(this, provider);
    _mapPanel.Invalidate();
  }

  private void Repaint()
  {
    ApplyFilters();
    _mapPanel.Invalidate();
  }

  private void ApplyFilters()
  {
    var wantedClass = _classCombo.SelectedItem as string ?? AllClasses;
    var find = _findBox.Text.Trim();
    var limit = FieldLimit();

    var visible = new List<MapRiderMarker>(_field.Count);

    foreach (var rider in _field)
    {
      if (!StateVisible(rider.State)) continue;

      // An unranked rider has no position to be inside the top N.
      if (limit > 0 && (rider.Rank <= 0 || rider.Rank > limit)) continue;

      if (wantedClass != AllClasses &&
          !string.Equals(rider.Category, wantedClass, StringComparison.OrdinalIgnoreCase))
        continue;

      var highlighted = find.Length > 0 &&
                        string.Equals(rider.RiderNumber, find, StringComparison.OrdinalIgnoreCase);

      visible.Add(highlighted ? rider with { Highlighted = true } : rider);
    }

    _renderer.Riders = visible;
    _renderer.ShowSectorPanel = _sectorPanel.Checked;
    _renderer.ShowLegend = _legend.Checked;
    ShowLeaderboardColumn(WantsLeaderboard);

    var parts = MapLabelParts.None;
    if (_labelPosition.Checked) parts |= MapLabelParts.Position;
    if (_labelNumber.Checked) parts |= MapLabelParts.Number;
    if (_labelName.Checked) parts |= MapLabelParts.Name;

    if (parts != _renderer.LabelParts)
    {
      _renderer.LabelParts = parts;
      LabelPartsChanged?.Invoke(this, parts);
    }

    var hidden = _field.Count - visible.Count;
    _countLabel.Text = hidden > 0
      ? $"{visible.Count} shown, {hidden} hidden"
      : $"{visible.Count} rider{(visible.Count == 1 ? "" : "s")}";
  }

  /// <summary>0 means no limit.</summary>
  private int FieldLimit() => _fieldCombo.SelectedIndex switch
  {
    1 => 3,
    2 => 10,
    3 => 20,
    4 => 50,
    _ => 0
  };

  /// <summary>Sector occupancy, supplied by the form each frame.</summary>
  public void SetSectorInfo(IReadOnlyList<MapSectorInfo> sectors) => _renderer.SectorInfo = sectors;

  private bool StateVisible(TrackPositionState state) => state switch
  {
    TrackPositionState.Finished => _showFinished.Checked,
    TrackPositionState.Retired => _showRetired.Checked,
    TrackPositionState.LongOverdue => _showLongOverdue.Checked,
    TrackPositionState.DidNotStart => _showNotStarted.Checked,
    _ => true
  };

  public void Dispose()
  {
    _renderer?.Dispose();
    _session?.Dispose();
  }
}
