namespace CrossMgrInterface;

/// <summary>
/// Draws and edits a circuit.
///
/// A modal, not an edit mode on the Track tab, for the same reason RaceDayView
/// keeps its leaderboard read-only: that tab is a race-day display, and an edit
/// mode on it means one mis-click drags the start/finish line and silently
/// corrupts every rider position with no obvious undo. Setting a circuit up is a
/// once-per-venue job done off the clock, and the house pattern for those is a
/// code-built modal - NewRaceWizard, LapCorrectionDialog, AssignTagDialog.
///
/// Everything here edits a deep clone. Nothing is applied until OK; Cancel
/// changes nothing.
/// </summary>
public sealed class TrackEditorDialog : Form
{
  private enum Tool { Draw, Move, StartFinish, Sector, AlignImage, MatchPoints }

  private const int MaxUndo = 50;

  /// <summary>
  /// Below this the map shows a region rather than a venue. A picture dropped on
  /// it is sized to the region, and there is nothing there to line it up against.
  /// </summary>
  private const int MinZoomForPicture = 12;

  private static readonly Color PicturePinColor = Color.FromArgb(230, 81, 0);
  private static readonly Color GroundPinColor = Color.FromArgb(25, 118, 210);

  private readonly TrackStore _store;
  private readonly Action<string>? _log;
  private readonly Stack<TrackDefinition> _undo = new();

  /// <summary>
  /// A decoded picture for every image this session has seen, keyed by the very
  /// byte array a placement holds. Kept after Remove, so Ctrl+Z brings the
  /// picture back without decoding it again.
  /// </summary>
  private readonly Dictionary<byte[], ReferenceImageLayer> _layers = new(ReferenceEqualityComparer.Instance);

  private TileSession _session = null!;
  private ComboBox _mapCombo = null!;

  private TrackDefinition _draft;
  private Tool _tool = Tool.Move;

  private Panel _mapPanel = null!;
  private TrackMapRenderer _renderer = null!;

  private TextBox _nameBox = null!;
  private Label _stats = null!;
  private Label _hint = null!;
  private ListBox _sectorList = null!;
  private Button _undoButton = null!;
  private readonly Dictionary<Tool, Button> _toolButtons = new();

  private Label _pictureName = null!;
  private Button _pictureImport = null!;
  private Button _pictureRemove = null!;
  private CheckBox _pictureShow = null!;
  private CheckBox _pictureLock = null!;
  private TrackBar _pictureOpacity = null!;

  // A two-point match in progress. Step 0 waits for landmark A on the picture,
  // 1 for A on the map, 2 for B on the picture, 3 for B on the map.
  private int _matchStep;
  private PointD _matchPictureA;
  private LatLon _matchGroundA;
  private PointD _matchPictureB;
  private readonly List<MapPin> _pins = new();

  /// <summary>The edited circuit, or null if the operator cancelled.</summary>
  public TrackDefinition? Result { get; private set; }

  /// <summary>
  /// The basemap in use when the dialog closed. Tracing is usually done on
  /// satellite and watched on a street map, but if the operator deliberately
  /// switched here it should carry back rather than silently revert.
  /// </summary>
  public TileProvider Provider => _session.Provider;

  private readonly TileProvider _startProvider;
  private readonly TileSessionOptions? _tileOptions;

  /// <param name="openAt">
  /// Where to point the camera for a circuit that has no geometry yet. Without it
  /// a new circuit opens over whatever the default view is and the operator has to
  /// find their own venue again, having just been looking straight at it.
  /// </param>
  /// <param name="tileOptions">Where map tiles come from. Left out, the application's own cache, online.</param>
  public TrackEditorDialog(
    TrackStore store, TrackDefinition? existing, TileProvider provider, Action<string>? log = null,
    (LatLon Center, int Zoom)? openAt = null, TileSessionOptions? tileOptions = null)
  {
    _store = store;
    _log = log;
    _startProvider = provider;
    _tileOptions = tileOptions;

    _draft = existing?.Clone() ?? new TrackDefinition { Name = "New circuit" };

    Text = "Set up circuit";
    StartPosition = FormStartPosition.CenterParent;
    MinimumSize = new Size(900, 700);
    ClientSize = new Size(1060, 760);

    BuildLayout();

    _renderer.Track = _draft;
    _renderer.ShowVertices = true;
    _nameBox.Text = _draft.Name;

    var pictureReadable = AcceptPicture(_draft);
    RefreshReferenceImage();

    if (_draft.IsUsable)
    {
      _renderer.FitTrack();
      SetTool(Tool.Move);
    }
    else
    {
      // Nothing drawn yet, so the only useful thing to do is start drawing - and
      // start it looking at wherever the operator already was.
      if (openAt is { } view) _renderer.SetCenter(view.Center, view.Zoom);
      SetTool(Tool.Draw);
    }

    RefreshSectorList();
    RefreshStats();

    if (!pictureReadable)
      _hint.Text = "The reference image saved with this circuit could not be read, so it has been left out.";
  }

  /// <summary>The map, so the help screenshots can wait for its tiles.</summary>
  internal TrackMapRenderer Renderer => _renderer;

  /// <summary>Switches to Align image, which is how the help's picture of this dialog shows it.</summary>
  internal void ShowAlignImageTool() => SetTool(Tool.AlignImage);

  // ---- Layout --------------------------------------------------------------

  private void BuildLayout()
  {
    var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
    root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
    root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
    root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

    root.Controls.Add(BuildToolRail(), 0, 0);
    root.Controls.Add(BuildMap(), 1, 0);
    root.Controls.Add(BuildSidePanel(), 2, 0);

    var footer = BuildFooter();
    root.Controls.Add(footer, 0, 1);
    root.SetColumnSpan(footer, 3);

    Controls.Add(root);
  }

  private Control BuildToolRail()
  {
    var rail = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill,
      FlowDirection = FlowDirection.TopDown,
      WrapContents = false,
      Padding = new Padding(8)
    };

    rail.Controls.Add(RailButton(Tool.Draw, "Draw loop"));
    rail.Controls.Add(RailButton(Tool.Move, "Move points"));
    rail.Controls.Add(RailButton(Tool.StartFinish, "Start / finish"));
    rail.Controls.Add(RailButton(Tool.Sector, "Add sector"));

    // The picture's own tools, set a little apart: they move the picture, never the circuit.
    var align = RailButton(Tool.AlignImage, "Align image");
    align.Margin = new Padding(0, 12, 0, 6);
    rail.Controls.Add(align);
    rail.Controls.Add(RailButton(Tool.MatchPoints, "Match 2 points"));

    var reverse = new Button { Text = "Reverse direction", Width = 118, Height = 30, Margin = new Padding(0, 18, 0, 4) };
    reverse.Click += (_, _) =>
    {
      // A GPX ridden anticlockwise on a clockwise circuit is a coin flip, and
      // getting it wrong sends every rider dot backwards round the loop.
      Mutate(t => t.ReverseDirection());
    };
    rail.Controls.Add(reverse);

    _undoButton = new Button { Text = "Undo", Width = 118, Height = 30, Margin = new Padding(0, 4, 0, 4), Enabled = false };
    _undoButton.Click += (_, _) => Undo();
    rail.Controls.Add(_undoButton);

    var fit = new Button { Text = "Fit to screen", Width = 118, Height = 28, Margin = new Padding(0, 14, 0, 4) };
    fit.Click += (_, _) => FitToScreen();
    rail.Controls.Add(fit);

    return rail;
  }

  private Button RailButton(Tool tool, string text)
  {
    var button = new Button
    {
      Text = text,
      Width = 118,
      Height = 32,
      Margin = new Padding(0, 0, 0, 6),
      FlatStyle = FlatStyle.Standard
    };
    button.Click += (_, _) => SetTool(tool);

    _toolButtons[tool] = button;
    return button;
  }

  private Control BuildMap()
  {
    var container = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };

    _mapPanel = new Panel { Dock = DockStyle.Fill, BackColor = MapDrawResources.LandColor };
    _session = new TileSession(_mapPanel, _startProvider, _log, _tileOptions);
    _renderer = new TrackMapRenderer(_mapPanel, _session)
    {
      EmptyStateText = "Click \"Draw loop\", then click round the circuit."
    };

    _renderer.MapClicked += OnMapClicked;
    _renderer.VertexDragged += OnVertexDragged;
    _renderer.ReferenceImageChanged += OnReferenceImageChanged;
    _renderer.Picked += (_, e) =>
    {
      if (e.Element.Kind == MapHitKind.TrackVertex) _renderer.SelectedVertexIndex = e.Element.VertexIndex;
    };

    container.Controls.Add(_mapPanel);
    return container;
  }

  private Control BuildSidePanel()
  {
    var panel = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 4,
      Padding = new Padding(8, 8, 8, 0)
    };
    panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

    panel.Controls.Add(new Label { Text = "Sectors", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);

    _sectorList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
    _sectorList.DoubleClick += (_, _) => RenameSector();
    panel.Controls.Add(_sectorList, 0, 1);

    var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 6, 0, 6) };

    var rename = new Button { Text = "Rename", Width = 84 };
    rename.Click += (_, _) => RenameSector();

    var colour = new Button { Text = "Colour", Width = 84 };
    colour.Click += (_, _) => RecolourSector();

    var remove = new Button { Text = "Remove", Width = 84 };
    remove.Click += (_, _) => RemoveSector();

    buttons.Controls.AddRange(new Control[] { rename, colour, remove });
    panel.Controls.Add(buttons, 0, 2);

    panel.Controls.Add(BuildPicturePanel(), 0, 3);

    return panel;
  }

  private Control BuildPicturePanel()
  {
    var panel = new FlowLayoutPanel
    {
      Dock = DockStyle.Fill,
      AutoSize = true,
      FlowDirection = FlowDirection.TopDown,
      WrapContents = false,
      Padding = new Padding(0, 8, 0, 6)
    };

    panel.Controls.Add(new Label { Text = "Reference image", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });

    _pictureName = new Label
    {
      AutoSize = true,
      MaximumSize = new Size(190, 0),
      ForeColor = Color.DimGray,
      Margin = new Padding(3, 4, 3, 4)
    };
    panel.Controls.Add(_pictureName);

    var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };

    _pictureImport = new Button { Text = "Import image...", Width = 104 };
    _pictureImport.Click += (_, _) => ImportPictureFile();

    _pictureRemove = new Button { Text = "Remove", Width = 70 };
    _pictureRemove.Click += (_, _) => SetReferenceImage(null);

    buttons.Controls.AddRange(new Control[] { _pictureImport, _pictureRemove });
    panel.Controls.Add(buttons);

    var toggles = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };

    _pictureShow = new CheckBox { Text = "Show", Checked = true, AutoSize = true, Margin = new Padding(3, 6, 12, 0) };
    _pictureShow.CheckedChanged += (_, _) => RefreshReferenceImage();

    _pictureLock = new CheckBox { Text = "Lock", AutoSize = true, Margin = new Padding(3, 6, 3, 0) };
    _pictureLock.CheckedChanged += (_, _) => SetPictureLocked(_pictureLock.Checked);

    toggles.Controls.AddRange(new Control[] { _pictureShow, _pictureLock });
    panel.Controls.Add(toggles);

    panel.Controls.Add(new Label { Text = "Opacity", AutoSize = true, Margin = new Padding(3, 6, 3, 0) });

    // Over the map or under it is the same question: at full opacity only the
    // picture shows, and turned down mostly the map does. One slider answers both.
    _pictureOpacity = new TrackBar
    {
      Minimum = 10,
      Maximum = 100,
      Value = 60,
      TickFrequency = 10,
      SmallChange = 5,
      LargeChange = 10,
      AutoSize = false,
      Width = 190,
      Height = 32
    };
    _pictureOpacity.ValueChanged += (_, _) => RefreshReferenceImage();
    panel.Controls.Add(_pictureOpacity);

    return panel;
  }

  private Control BuildFooter()
  {
    var footer = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 2,
      RowCount = 2,
      AutoSize = true,
      Padding = new Padding(8)
    };
    footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

    // Wrapping, with an explicit maximum width so it knows where to wrap. This
    // row holds a name box, a basemap picker and four buttons; with wrapping off
    // the last of them was simply clipped off the right edge of the dialog -
    // present, laid out, and impossible to click.
    var left = new FlowLayoutPanel
    {
      AutoSize = true,
      AutoSizeMode = AutoSizeMode.GrowAndShrink,
      WrapContents = true,
      MaximumSize = new Size(ClientSize.Width - 240, 0)
    };
    left.Controls.Add(new Label { Text = "Name:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });

    _nameBox = new TextBox { Width = 200, Margin = new Padding(0, 3, 16, 0) };
    _nameBox.TextChanged += (_, _) => _draft.Name = _nameBox.Text;
    left.Controls.Add(_nameBox);

    left.Controls.Add(new Label { Text = "Map:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });

    // Satellite makes tracing a circuit enormously easier than a street map does,
    // so the picker belongs here as much as on the tab.
    _mapCombo = new ComboBox
    {
      DropDownStyle = ComboBoxStyle.DropDownList,
      Width = 165,
      Margin = new Padding(0, 3, 16, 0)
    };
    foreach (var provider in TileProvider.All) _mapCombo.Items.Add(provider);
    _mapCombo.SelectedItem = TileProvider.All.FirstOrDefault(p => p.Id == _startProvider.Id) ?? TileProvider.OpenStreetMap;
    _mapCombo.SelectedIndexChanged += (_, _) => SwitchBasemap();
    left.Controls.Add(_mapCombo);

    var import = new Button { Text = "Import...", AutoSize = true, Margin = new Padding(0, 1, 4, 0) };
    import.Click += (_, _) => ImportCircuit();
    left.Controls.Add(import);

    var export = new Button { Text = "Export...", AutoSize = true, Margin = new Padding(0, 1, 8, 0) };
    export.Click += (_, _) => ExportCircuit();
    left.Controls.Add(export);

    var offline = new Button { Text = "Offline map...", AutoSize = true, Margin = new Padding(0, 1, 8, 0) };
    offline.Click += (_, _) => DownloadOffline();
    left.Controls.Add(offline);

    footer.Controls.Add(left, 0, 0);

    var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };

    var ok = new Button { Text = "Save circuit", Width = 110, Height = 30, Margin = new Padding(0, 0, 8, 0) };
    ok.Click += (_, _) => Save();

    var cancel = new Button { Text = "Cancel", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };

    right.Controls.AddRange(new Control[] { ok, cancel });
    footer.Controls.Add(right, 1, 0);

    _stats = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 4, 0, 0) };
    footer.Controls.Add(_stats, 0, 1);

    _hint = new Label { AutoSize = true, ForeColor = Color.FromArgb(180, 90, 0), Margin = new Padding(0, 4, 0, 0) };
    footer.Controls.Add(_hint, 1, 1);

    CancelButton = cancel;
    return footer;
  }

  private void SwitchBasemap()
  {
    if (_mapCombo.SelectedItem is not TileProvider provider || provider.Id == _session.Provider.Id) return;

    var old = _session;
    _session = new TileSession(_mapPanel, provider, _log, _tileOptions);
    _renderer.SetTiles(_session);
    old.Dispose();

    _hint.Text = provider.Caveat ?? "";
    _mapPanel.Invalidate();
  }

  /// <summary>The loop when there is one; otherwise the picture, which is often all there is to start with.</summary>
  private void FitToScreen()
  {
    if (_draft.Points.Count >= 2) _renderer.FitTrack();
    else if (_draft.ReferenceImage is { } picture) _renderer.FitBounds(picture.Bounds);
  }

  // ---- Tools ---------------------------------------------------------------

  private void SetTool(Tool tool)
  {
    // Changing tool abandons a match half done, rather than resuming it later
    // with landmarks the operator has long forgotten picking.
    ResetMatch();

    _tool = tool;

    foreach (var (key, button) in _toolButtons)
      button.BackColor = key == tool ? Color.FromArgb(210, 228, 245) : SystemColors.Control;

    _renderer.Mode = tool switch
    {
      Tool.Draw => MapInteractionMode.PlacePoint,
      Tool.Move => MapInteractionMode.MoveVertex,
      Tool.AlignImage => MapInteractionMode.AlignImage,
      _ => MapInteractionMode.PlaceAnchor
    };

    // Dashed only while points are still being laid down, so the operator can
    // always see the loop the application will actually use.
    _renderer.DashClosingSegment = tool == Tool.Draw;
    _renderer.ShowReferenceHandles = tool == Tool.AlignImage;

    _hint.Text = tool switch
    {
      Tool.Draw => "Click round the circuit. Backspace removes the last point.",
      Tool.Move => "Drag a point to move it. Ctrl+click the line to add one. Delete removes the selected point.",
      Tool.StartFinish => "Click where the start/finish line is painted on the ground.",
      Tool.Sector => "Click where a new sector begins.",
      Tool.AlignImage => "Drag the picture to move it, a corner to resize it, the round handle to turn it. Right-drag moves the map.",
      Tool.MatchPoints => MatchHint(),
      _ => ""
    };

    RefreshMatchView();
  }

  private void OnMapClicked(object? sender, MapClickEventArgs e)
  {
    switch (_tool)
    {
      case Tool.Draw:
        Mutate(t => t.AddPoint(e.Location));
        break;

      case Tool.Move when e.CtrlHeld:
      {
        // Ctrl+click on the line inserts a vertex there: the standard idiom for
        // refining one corner without redrawing the whole loop.
        var segment = _renderer.HitTestSegment(e.Screen);
        if (segment >= 0) Mutate(t => t.InsertPoint(segment + 1, e.Location));
        break;
      }

      case Tool.StartFinish:
        if (!_draft.IsUsable) return;
        Mutate(t => t.StartFinish.PlaceAt(t.Geometry, e.Location));
        break;

      case Tool.Sector:
      {
        if (!_draft.IsUsable) return;

        var name = TextPrompt.Ask(this, "Sector name", $"Sector {_draft.Sectors.Count + 1}", "What is this part of the circuit called?");
        if (name is null) return;

        Mutate(t => t.AddSector(name, e.Location));
        RefreshSectorList();
        break;
      }

      case Tool.MatchPoints:
        OnMatchClicked(e);
        break;
    }
  }

  private bool _dragUndoPushed;

  private void OnVertexDragged(object? sender, MapVertexDragEventArgs e)
  {
    // One undo entry for the whole gesture, pushed on the first move rather than
    // on every mouse-move - otherwise a single drag fills the undo stack.
    if (!_dragUndoPushed)
    {
      PushUndo();
      _dragUndoPushed = true;
    }

    _draft.MovePoint(e.Index, e.Location);

    if (e.Finished)
    {
      _dragUndoPushed = false;
      RefreshStats();
      RefreshSectorList();
    }

    _mapPanel.Invalidate();
  }

  protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
  {
    // Ahead of everything, the dialog's CancelButton included: Esc while matching
    // means "stop matching", not "throw the whole circuit away".
    if (keyData == Keys.Escape && _tool == Tool.MatchPoints)
    {
      SetTool(Tool.AlignImage);
      _hint.Text = "Matching stopped. " + _hint.Text;
      return true;
    }

    // Typing the circuit's name must not edit the circuit: Backspace deleted the
    // last point, and Ctrl+V would paste a picture instead of text.
    if (ActiveControl is TextBoxBase) return base.ProcessCmdKey(ref msg, keyData);

    switch (keyData)
    {
      case Keys.Back when _tool == Tool.Draw && _draft.Points.Count > 0:
        Mutate(t => t.RemoveLastPoint());
        return true;

      case Keys.Delete when _renderer.SelectedVertexIndex is { } index:
        if (!_draft.RemovePointAt(index))
        {
          _hint.Text = "A circuit needs at least three points.";
          return true;
        }

        PushUndo();
        _renderer.SelectedVertexIndex = null;
        AfterMutate();
        return true;

      case Keys.Control | Keys.Z:
        Undo();
        return true;

      case Keys.Control | Keys.V:
        if (PastePicture()) return true;
        break;
    }

    return base.ProcessCmdKey(ref msg, keyData);
  }

  // ---- Undo ----------------------------------------------------------------

  /// <summary>
  /// Twenty lines that take all the fear out of an editor: any mutation can be
  /// tried and thrown away.
  /// </summary>
  private void Mutate(Action<TrackDefinition> change)
  {
    PushUndo();
    change(_draft);
    _draft.GeometryChanged();
    AfterMutate();
  }

  private void PushUndo()
  {
    _undo.Push(_draft.Clone());

    if (_undo.Count > MaxUndo)
    {
      var kept = _undo.Take(MaxUndo).Reverse().ToList();
      _undo.Clear();
      foreach (var state in kept) _undo.Push(state);
    }

    _undoButton.Enabled = true;
  }

  private void Undo()
  {
    if (_undo.Count == 0) return;

    _draft = _undo.Pop();
    _draft.InvalidateGeometry();
    _renderer.Track = _draft;
    _renderer.SelectedVertexIndex = null;
    _dragUndoPushed = false;
    _nameBox.Text = _draft.Name;

    // Landmarks picked on the picture belong to where it was; after an undo it
    // may be somewhere else, so a match in progress starts over.
    if (_tool == Tool.MatchPoints)
    {
      ResetMatch();
      _hint.Text = MatchHint();
      RefreshMatchView();
    }

    _undoButton.Enabled = _undo.Count > 0;
    AfterMutate();
  }

  private void AfterMutate()
  {
    _dragUndoPushed = false;
    RefreshStats();
    RefreshSectorList();
    RefreshReferenceImage();
    _mapPanel.Invalidate();
  }

  private void RefreshStats()
  {
    _stats.Text = _draft.Points.Count == 0
      ? "No points yet."
      : $"{_draft.Points.Count} points  -  {_draft.LengthMetres:F0} m round" +
        (_draft.Sectors.Count > 0 ? $"  -  {_draft.Sectors.Count} sectors" : "");

    // Not over the picture tools' own instructions. A picture is usually lined up
    // before there are any points, and "needs at least three points" in place of
    // "now click the same spot on the map" leaves the operator stranded mid-match.
    var problems = _draft.Validate();
    if (problems.Count > 0 && _tool is not (Tool.Draw or Tool.AlignImage or Tool.MatchPoints))
      _hint.Text = problems[0];
  }

  // ---- Sectors -------------------------------------------------------------

  private void RefreshSectorList()
  {
    var selected = _sectorList.SelectedIndex;

    _sectorList.BeginUpdate();
    _sectorList.Items.Clear();

    for (var i = 0; i < _draft.Sectors.Count; i++)
    {
      var sector = _draft.Sectors[i];
      var name = string.IsNullOrWhiteSpace(sector.Name) ? $"Sector {i + 1}" : sector.Name;
      _sectorList.Items.Add($"{name}  ({sector.Start.Fraction * 100:F0}%)");
    }

    if (selected >= 0 && selected < _sectorList.Items.Count) _sectorList.SelectedIndex = selected;
    _sectorList.EndUpdate();
  }

  private void RenameSector()
  {
    var index = _sectorList.SelectedIndex;
    if (index < 0 || index >= _draft.Sectors.Count) return;

    var name = TextPrompt.Ask(this, "Sector name", _draft.Sectors[index].Name, "What is this part of the circuit called?");
    if (name is null) return;

    PushUndo();
    _draft.Sectors[index].Name = name;
    AfterMutate();
  }

  private void RecolourSector()
  {
    var index = _sectorList.SelectedIndex;
    if (index < 0 || index >= _draft.Sectors.Count) return;

    using var picker = new ColorDialog { Color = _draft.Sectors[index].Color };
    if (picker.ShowDialog(this) != DialogResult.OK) return;

    PushUndo();
    _draft.Sectors[index].ColorArgb = picker.Color.ToArgb();
    AfterMutate();
  }

  private void RemoveSector()
  {
    var index = _sectorList.SelectedIndex;
    if (index < 0) return;

    // Removing a boundary merges that sector into the one before it - under a
    // start-only model that is the only thing deletion can consistently mean.
    PushUndo();
    _draft.RemoveSectorAt(index);
    AfterMutate();
  }

  // ---- Reference image -----------------------------------------------------

  /// <summary>
  /// Points the renderer and the panel at whatever picture the draft has now -
  /// after an import, a Remove, or an undo that swaps one back. Opacity and Show
  /// are read from the panel every time: they are how the editor looks, not part
  /// of the circuit, so undo never changes them.
  /// </summary>
  private void RefreshReferenceImage()
  {
    var picture = _draft.ReferenceImage;
    var layer = picture is null ? null : LayerFor(picture);

    _pictureName.Text = picture is null
      ? "None. Import a picture of the circuit to trace over."
      : string.IsNullOrWhiteSpace(picture.SourceName) ? "Picture" : picture.SourceName;

    var locked = picture?.Locked == true;

    _pictureImport.Enabled = !locked;
    _pictureRemove.Enabled = picture is not null && !locked;
    _pictureShow.Enabled = _pictureOpacity.Enabled = _pictureLock.Enabled = layer is not null;
    _toolButtons[Tool.AlignImage].Enabled = _toolButtons[Tool.MatchPoints].Enabled = layer is not null && !locked;

    // Follows the draft rather than the other way round, so an undo shows the lock
    // as it was. Setting it comes back through SetPictureLocked, which does
    // nothing when the two already agree.
    _pictureLock.Checked = locked;

    _renderer.ReferenceLayer = layer is not null && _pictureShow.Checked ? layer : null;
    _renderer.ReferenceOpacity = _pictureOpacity.Value / 100f;

    if ((layer is null || locked) && _tool is Tool.AlignImage or Tool.MatchPoints)
      SetTool(_draft.Points.Count >= 3 ? Tool.Move : Tool.Draw);

    _mapPanel.Invalidate();
  }

  /// <summary>The decoded picture for a placement, decoding it the first time. Null if it is not a picture that can be drawn.</summary>
  private ReferenceImageLayer? LayerFor(TrackReferenceImage picture)
  {
    if (!picture.IsValid()) return null;
    if (_layers.TryGetValue(picture.ImageData, out var known)) return known;

    try
    {
      var layer = new ReferenceImageLayer(picture.ImageData);

      if (layer.Width != picture.PixelWidth || layer.Height != picture.PixelHeight)
      {
        // Placed against some other picture than the one stored. Drawing it would
        // put the wrong thing in the wrong place.
        layer.Dispose();
        return null;
      }

      _layers[picture.ImageData] = layer;
      return layer;
    }
    catch (Exception)
    {
      return null;
    }
  }

  /// <summary>
  /// Drops a circuit's picture if it cannot be drawn; false when it had to. Every
  /// picture arriving from disk or from another club's file comes through here: a
  /// placement that will not draw fails every paint, and a NaN in one stops
  /// tracks.json saving at all.
  /// </summary>
  private bool AcceptPicture(TrackDefinition track)
  {
    if (track.ReferenceImage is not { } picture || LayerFor(picture) is not null) return true;

    _log?.Invoke($"Circuit editor: the reference image of \"{track.Name}\" could not be read and was left out.");
    track.ReferenceImage = null;
    return false;
  }

  private void SetReferenceImage(TrackReferenceImage? picture)
  {
    PushUndo();
    _draft.ReferenceImage = picture;
    AfterMutate();
  }

  /// <summary>
  /// Undoable like any other change, so Ctrl+Z straight after ticking Lock unlocks
  /// again rather than undoing whatever came before it.
  /// </summary>
  private void SetPictureLocked(bool locked)
  {
    if (_draft.ReferenceImage is not { } picture || picture.Locked == locked) return;

    var changed = picture.Clone();
    changed.Locked = locked;
    SetReferenceImage(changed);

    _hint.Text = locked
      ? "Reference image locked: it cannot be moved, matched, replaced or removed until Lock is unticked."
      : "Reference image unlocked.";
  }

  /// <summary>True, with the reason in the hint, when the picture is locked against being replaced.</summary>
  private bool PictureLocked()
  {
    if (_draft.ReferenceImage?.Locked != true) return false;

    _hint.Text = "The reference image is locked. Untick Lock to replace it.";
    return true;
  }

  private void OnReferenceImageChanged(object? sender, MapReferenceImageEventArgs e)
  {
    if (_draft.ReferenceImage?.Locked == true) return;

    // One undo entry per drag, as for dragging a point.
    if (!_dragUndoPushed)
    {
      PushUndo();
      _dragUndoPushed = true;
    }

    _draft.ReferenceImage = e.Placement;

    if (e.Finished) _dragUndoPushed = false;

    _mapPanel.Invalidate();
  }

  /// <summary>
  /// Refuses while the map shows a region rather than a venue: a picture placed
  /// over half the country is sized to half the country, and there is nothing
  /// at that scale to line a circuit plan up with.
  /// </summary>
  private bool ReadyForPicture()
  {
    if (_renderer.Viewport.Zoom >= MinZoomForPicture) return true;

    MessageBox.Show(this, "Zoom the map in to the venue first, so the picture can be placed there.",
      "Zoom in first", MessageBoxButtons.OK, MessageBoxIcon.Information);
    return false;
  }

  private void ImportPictureFile()
  {
    if (PictureLocked() || !ReadyForPicture()) return;

    using var open = new OpenFileDialog
    {
      Title = "Import a reference image",
      Filter = "Pictures (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*"
    };

    if (open.ShowDialog(this) != DialogResult.OK) return;

    ImportPicture(open.FileName);
  }

  private void ImportPicture(string path)
  {
    byte[] bytes;

    try
    {
      bytes = File.ReadAllBytes(path);
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, $"That file could not be read: {ex.Message}",
        "Could not import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
      return;
    }

    AdoptPicture(() => ReferenceImageLayer.PrepareForStorage(bytes), Path.GetFileName(path));
  }

  /// <summary>
  /// Ctrl+V. Tries the clipboard's formats best first: the PNG that browsers and
  /// the Snipping Tool put there (lossless, with sane transparency), then a copied
  /// picture file, then the plain bitmap. False when there is no picture at all,
  /// so the key can do whatever else it would.
  /// </summary>
  private bool PastePicture()
  {
    IDataObject clipboard;

    try
    {
      if (Clipboard.GetDataObject() is not { } data) return false;
      clipboard = data;
    }
    catch (Exception)
    {
      return false;
    }

    if (ReadClipboard(() => clipboard.GetDataPresent("PNG") && clipboard.GetData("PNG") is MemoryStream png
          ? png.ToArray()
          : null) is { } pngBytes)
    {
      AdoptPicture(() => ReferenceImageLayer.PrepareForStorage(pngBytes), "Pasted picture");
      return true;
    }

    if (ReadClipboard(() => clipboard.GetDataPresent(DataFormats.FileDrop) &&
                            clipboard.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files &&
                            IsPictureFile(files[0])
          ? files[0]
          : null) is { } file)
    {
      if (ReadyForPicture()) ImportPicture(file);
      return true;
    }

    if (ReadClipboard(() => Clipboard.ContainsImage() ? Clipboard.GetImage() : null) is { } bitmap)
    {
      using (bitmap)
      {
        using var opaque = ReferenceImageLayer.Opaque(bitmap);
        AdoptPicture(() => ReferenceImageLayer.PrepareForStorage(opaque), "Pasted picture");
      }

      return true;
    }

    return false;
  }

  /// <summary>The clipboard belongs to every other application too, and any format on it can fail to read.</summary>
  private static T? ReadClipboard<T>(Func<T?> read) where T : class
  {
    try
    {
      return read();
    }
    catch (Exception)
    {
      return null;
    }
  }

  private static bool IsPictureFile(string path) =>
    Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";

  private void AdoptPicture(Func<PreparedImage> prepare, string sourceName)
  {
    if (PictureLocked() || !ReadyForPicture()) return;

    PreparedImage prepared;
    ReferenceImageLayer layer;

    try
    {
      prepared = prepare();
      layer = new ReferenceImageLayer(prepared.Data);
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, $"That is not a picture this application can read: {ex.Message}",
        "Could not import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
      return;
    }

    _layers[prepared.Data] = layer;

    // Shown even if the last picture had been hidden: importing one and seeing
    // nothing reads as an import that failed.
    _pictureShow.Checked = true;

    SetReferenceImage(TrackReferenceImage.FitToView(
      _renderer.Viewport, prepared.Data, layer.Width, layer.Height, sourceName));

    SetTool(Tool.AlignImage);
    _hint.Text = "Now line the picture up: Match 2 points does it from two landmarks, or drag, resize and turn it here.";

    _log?.Invoke($"Circuit editor: reference image \"{sourceName}\" added, " +
                 $"{layer.Width}x{layer.Height}px, {prepared.Data.Length / 1024} KB stored.");
  }

  // ---- Two-point match -----------------------------------------------------

  private string MatchHint() => _matchStep switch
  {
    0 => "Match 2 points, 1 of 4: click a landmark on the picture - a corner, a jump, a building. Esc stops.",
    1 => "2 of 4: now click the same spot on the map.",
    2 => "3 of 4: click a second landmark on the picture, well away from the first.",
    _ => "4 of 4: click that spot on the map."
  };

  private void ResetMatch()
  {
    _matchStep = 0;
    _pins.Clear();
  }

  private void RefreshMatchView()
  {
    _renderer.Pins = _pins.ToArray();

    // Faded while a spot on the MAP is wanted, or the picture hides the very
    // landmark the operator is looking for.
    _renderer.ReferenceFaded = _tool == Tool.MatchPoints && _matchStep is 1 or 3;

    _mapPanel.Invalidate();
  }

  private void OnMatchClicked(MapClickEventArgs e)
  {
    if (_draft.ReferenceImage is not { Locked: false } picture) return;

    var viewport = _renderer.Viewport;

    if (_matchStep is 0 or 2)
    {
      if (!_pictureShow.Checked)
      {
        _hint.Text = "Tick Show, so the landmark can be clicked on the picture.";
        return;
      }

      if (!picture.Contains(viewport, e.Screen))
      {
        _hint.Text = "That is off the picture. Click the landmark on the picture itself.";
        return;
      }

      var onPicture = picture.ScreenToImage(viewport, new PointD(e.Screen.X, e.Screen.Y));
      if (_matchStep == 0) _matchPictureA = onPicture;
      else _matchPictureB = onPicture;

      _pins.Add(new MapPin(e.Location, _matchStep == 0 ? "A" : "B", PicturePinColor));
    }
    else if (_matchStep == 1)
    {
      _matchGroundA = e.Location;
      _pins.Add(new MapPin(e.Location, "A", GroundPinColor));
    }
    else
    {
      var fitted = picture.FitTwoPoints(_matchPictureA, _matchGroundA, _matchPictureB, e.Location);

      if (fitted is null)
      {
        ResetMatch();
        RefreshMatchView();
        _hint.Text = "Those landmarks are too close together to line the picture up from. " +
                     "Start again with two further apart.";
        return;
      }

      SetReferenceImage(fitted);
      SetTool(Tool.AlignImage);
      _hint.Text = "Picture lined up. Fine-tune it with the handles if needed; Ctrl+Z undoes the match.";
      return;
    }

    _matchStep++;
    _hint.Text = MatchHint();
    RefreshMatchView();
  }

  // ---- Import and offline --------------------------------------------------

  private void ImportCircuit()
  {
    using var open = new OpenFileDialog
    {
      Title = "Import a circuit",
      Filter = "All circuit files (*.gpx;*.cmtrack)|*.gpx;*.cmtrack|" +
               "GPX track (*.gpx)|*.gpx|" +
               "CrossMgr circuit (*.cmtrack)|*.cmtrack|" +
               "All files (*.*)|*.*"
    };

    if (open.ShowDialog(this) != DialogResult.OK) return;

    if (Path.GetExtension(open.FileName)
        .Equals(TrackGpxExporter.CircuitFileExtension, StringComparison.OrdinalIgnoreCase))
    {
      ImportCircuitFile(open.FileName);
      return;
    }

    ImportGpx(open.FileName);
  }

  /// <summary>A circuit file carries everything, so it replaces the draft wholesale.</summary>
  private void ImportCircuitFile(string path)
  {
    TrackDefinition? imported;

    try
    {
      imported = TrackStore.ImportJson(File.ReadAllText(path));
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, $"That file could not be read: {ex.Message}",
        "Could not import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
      return;
    }

    if (imported is null || !imported.IsUsable)
    {
      MessageBox.Show(this, "That file does not contain a usable circuit.",
        "Could not import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
      return;
    }

    // A picture that will not draw is left out, rather than refusing the circuit it came with.
    var pictureReadable = AcceptPicture(imported);

    PushUndo();

    // A new id, so importing somebody else's circuit adds one rather than
    // silently overwriting a circuit of yours that happens to share its id.
    imported.Id = Guid.NewGuid().ToString("N");

    // A picture is usually lined up first and a loop imported to go with it, so
    // the one already here stays unless the file brings its own.
    imported.ReferenceImage ??= _draft.ReferenceImage;

    _draft = imported;
    _renderer.Track = _draft;
    _nameBox.Text = _draft.Name;
    _renderer.FitTrack();

    AfterMutate();
    SetTool(Tool.Move);

    if (!pictureReadable)
      _hint.Text = "The reference image in that file could not be read, so it was left out.";
  }

  private void ImportGpx(string path)
  {
    var result = GpxTrackImporter.Import(path);

    if (!result.Success)
    {
      MessageBox.Show(this, result.Summary, "Could not import that file",
        MessageBoxButtons.OK, MessageBoxIcon.Warning);
      return;
    }

    PushUndo();

    var keepName = _nameBox.Text;
    var keepPicture = _draft.ReferenceImage;

    _draft = result.Track!;
    if (!string.IsNullOrWhiteSpace(keepName) && keepName != "New circuit") _draft.Name = keepName;

    // A GPX has no picture of its own; the one being traced against stays.
    _draft.ReferenceImage ??= keepPicture;

    _renderer.Track = _draft;
    _nameBox.Text = _draft.Name;
    _renderer.FitTrack();

    AfterMutate();
    SetTool(Tool.StartFinish);

    MessageBox.Show(this,
      result.Summary + "\n\n" + string.Join("\n", result.Warnings),
      "Circuit imported", MessageBoxButtons.OK, MessageBoxIcon.Information);
  }

  private void ExportCircuit()
  {
    if (!_draft.IsUsable)
    {
      MessageBox.Show(this, "Draw or import the loop first - there is nothing to export yet.",
        "Nothing to export", MessageBoxButtons.OK, MessageBoxIcon.Information);
      return;
    }

    var safeName = string.Join("_", (_nameBox.Text.Trim().Length > 0 ? _nameBox.Text.Trim() : "circuit")
      .Split(Path.GetInvalidFileNameChars()));

    using var save = new SaveFileDialog
    {
      Title = "Export circuit",
      FileName = safeName,
      Filter = TrackGpxExporter.ExportFilter,
      DefaultExt = "gpx",
      AddExtension = true
    };

    if (save.ShowDialog(this) != DialogResult.OK) return;

    var asCircuitFile = Path.GetExtension(save.FileName)
      .Equals(TrackGpxExporter.CircuitFileExtension, StringComparison.OrdinalIgnoreCase);

    try
    {
      _draft.Name = _nameBox.Text.Trim();

      if (asCircuitFile) TrackGpxExporter.SaveCircuitFile(_draft, save.FileName);
      else TrackGpxExporter.SaveGpx(_draft, save.FileName);
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, $"The circuit could not be written: {ex.Message}",
        "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
      return;
    }

    MessageBox.Show(this,
      $"Saved to {Path.GetFileName(save.FileName)}.\n\n{TrackGpxExporter.ExportCaveat(_draft, asCircuitFile)}",
      "Circuit exported", MessageBoxButtons.OK, MessageBoxIcon.Information);
  }

  private void DownloadOffline()
  {
    if (!_draft.IsUsable)
    {
      MessageBox.Show(this, "Draw or import the loop first, so there is an area to download.",
        "Nothing to download", MessageBoxButtons.OK, MessageBoxIcon.Information);
      return;
    }

    var prefetcher = new MapTilePrefetcher(_session.Store, _session.Fetcher);
    using var dialog = new TileCacheProgressDialog(prefetcher, _draft.Bounds.Pad(150));
    dialog.ShowDialog(this);

    _mapPanel.Invalidate();
  }

  // ---- Saving --------------------------------------------------------------

  private void Save()
  {
    _draft.Name = _nameBox.Text.Trim();

    if (string.IsNullOrWhiteSpace(_draft.Name))
    {
      MessageBox.Show(this, "Give the circuit a name so it can be picked out later.",
        "Name needed", MessageBoxButtons.OK, MessageBoxIcon.Information);
      _nameBox.Focus();
      return;
    }

    if (!_draft.IsUsable)
    {
      MessageBox.Show(this, "A circuit needs at least three points and a loop over 50m round.",
        "Not a circuit yet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
      return;
    }

    if (_draft.StartFinish.NeedsReview)
    {
      var proceed = MessageBox.Show(this,
        "The start/finish line has not been placed on the loop yet.\n\n" +
        "Every rider position is measured from it, so a circuit saved without it " +
        "will show riders in the wrong place.\n\nSave anyway?",
        "Start/finish not placed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

      if (proceed != DialogResult.Yes)
      {
        SetTool(Tool.StartFinish);
        return;
      }
    }

    _store.AddOrUpdate(_draft);

    try
    {
      _store.Save();
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, $"The circuit could not be saved: {ex.Message}",
        "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
      return;
    }

    Result = _draft;
    DialogResult = DialogResult.OK;
    Close();
  }

  protected override void OnFormClosed(FormClosedEventArgs e)
  {
    _renderer.Dispose();
    _session.Dispose();

    foreach (var layer in _layers.Values) layer.Dispose();
    _layers.Clear();

    base.OnFormClosed(e);
  }
}
