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
  private enum Tool { Draw, Move, StartFinish, Sector }

  private const int MaxUndo = 50;

  private readonly TrackStore _store;
  private readonly Action<string>? _log;
  private readonly Stack<TrackDefinition> _undo = new();

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

  /// <summary>The edited circuit, or null if the operator cancelled.</summary>
  public TrackDefinition? Result { get; private set; }

  /// <summary>
  /// The basemap in use when the dialog closed. Tracing is usually done on
  /// satellite and watched on a street map, but if the operator deliberately
  /// switched here it should carry back rather than silently revert.
  /// </summary>
  public TileProvider Provider => _session.Provider;

  private readonly TileProvider _startProvider;

  /// <param name="openAt">
  /// Where to point the camera for a circuit that has no geometry yet. Without it
  /// a new circuit opens over whatever the default view is and the operator has to
  /// find their own venue again, having just been looking straight at it.
  /// </param>
  public TrackEditorDialog(
    TrackStore store, TrackDefinition? existing, TileProvider provider, Action<string>? log = null,
    (LatLon Center, int Zoom)? openAt = null)
  {
    _store = store;
    _log = log;
    _startProvider = provider;

    _draft = existing?.Clone() ?? new TrackDefinition { Name = "New circuit" };

    Text = "Set up circuit";
    StartPosition = FormStartPosition.CenterParent;
    MinimumSize = new Size(900, 640);
    ClientSize = new Size(1060, 720);

    BuildLayout();

    _renderer.Track = _draft;
    _renderer.ShowVertices = true;
    _nameBox.Text = _draft.Name;

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
  }

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
    root.Controls.Add(BuildSectorPanel(), 2, 0);

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
    fit.Click += (_, _) => _renderer.FitTrack();
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
    _session = new TileSession(_mapPanel, _startProvider, _log);
    _renderer = new TrackMapRenderer(_mapPanel, _session)
    {
      EmptyStateText = "Click \"Draw loop\", then click round the circuit."
    };

    _renderer.MapClicked += OnMapClicked;
    _renderer.VertexDragged += OnVertexDragged;
    _renderer.Picked += (_, e) =>
    {
      if (e.Element.Kind == MapHitKind.TrackVertex) _renderer.SelectedVertexIndex = e.Element.VertexIndex;
    };

    container.Controls.Add(_mapPanel);
    return container;
  }

  private Control BuildSectorPanel()
  {
    var panel = new TableLayoutPanel
    {
      Dock = DockStyle.Fill,
      ColumnCount = 1,
      RowCount = 3,
      Padding = new Padding(8, 8, 8, 0)
    };
    panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
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
    _session = new TileSession(_mapPanel, provider, _log);
    _renderer.SetTiles(_session);
    old.Dispose();

    _hint.Text = provider.Caveat ?? "";
    _mapPanel.Invalidate();
  }

  // ---- Tools ---------------------------------------------------------------

  private void SetTool(Tool tool)
  {
    _tool = tool;

    foreach (var (key, button) in _toolButtons)
      button.BackColor = key == tool ? Color.FromArgb(210, 228, 245) : SystemColors.Control;

    _renderer.Mode = tool switch
    {
      Tool.Draw => MapInteractionMode.PlacePoint,
      Tool.Move => MapInteractionMode.MoveVertex,
      _ => MapInteractionMode.PlaceAnchor
    };

    // Dashed only while points are still being laid down, so the operator can
    // always see the loop the application will actually use.
    _renderer.DashClosingSegment = tool == Tool.Draw;

    _hint.Text = tool switch
    {
      Tool.Draw => "Click round the circuit. Backspace removes the last point.",
      Tool.Move => "Drag a point to move it. Ctrl+click the line to add one. Delete removes the selected point.",
      Tool.StartFinish => "Click where the start/finish line is painted on the ground.",
      Tool.Sector => "Click where a new sector begins.",
      _ => ""
    };

    _mapPanel.Invalidate();
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

    _undoButton.Enabled = _undo.Count > 0;
    AfterMutate();
  }

  private void AfterMutate()
  {
    _dragUndoPushed = false;
    RefreshStats();
    RefreshSectorList();
    _mapPanel.Invalidate();
  }

  private void RefreshStats()
  {
    _stats.Text = _draft.Points.Count == 0
      ? "No points yet."
      : $"{_draft.Points.Count} points  -  {_draft.LengthMetres:F0} m round" +
        (_draft.Sectors.Count > 0 ? $"  -  {_draft.Sectors.Count} sectors" : "");

    var problems = _draft.Validate();
    if (problems.Count > 0 && _tool != Tool.Draw) _hint.Text = problems[0];
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

    PushUndo();

    // A new id, so importing somebody else's circuit adds one rather than
    // silently overwriting a circuit of yours that happens to share its id.
    imported.Id = Guid.NewGuid().ToString("N");

    _draft = imported;
    _renderer.Track = _draft;
    _nameBox.Text = _draft.Name;
    _renderer.FitTrack();

    AfterMutate();
    SetTool(Tool.Move);
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
    _draft = result.Track!;
    if (!string.IsNullOrWhiteSpace(keepName) && keepName != "New circuit") _draft.Name = keepName;

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

      // GPX first because it is the one other software reads. The circuit file
      // is the lossless one, and the note in the dialog title bar is not enough
      // to say so - hence the confirmation below.
      Filter = "GPX track (*.gpx)|*.gpx|CrossMgr circuit, keeps sectors (*.cmtrack)|*.cmtrack",
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

    var caveat = asCircuitFile
      ? "Sectors and the start/finish line are all preserved."
      : "GPX carries the shape of the loop. It has no way to record a start/finish " +
        "line or sectors, so those are written as waypoints for reference only and " +
        "will need setting again after importing.";

    MessageBox.Show(this, $"Saved to {Path.GetFileName(save.FileName)}.\n\n{caveat}",
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
    base.OnFormClosed(e);
  }
}
