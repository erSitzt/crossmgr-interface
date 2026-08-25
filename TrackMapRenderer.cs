using System.Drawing.Drawing2D;

namespace CrossMgrInterface;

/// <summary>What a click on the map means right now.</summary>
public enum MapInteractionMode
{
  /// <summary>Pan and zoom only. What the race-day tab always uses.</summary>
  Navigate,

  /// <summary>Clicking empty map appends a track point.</summary>
  PlacePoint,

  /// <summary>Vertices can be dragged; ctrl-clicking a segment inserts one.</summary>
  MoveVertex,

  /// <summary>Clicking places the start/finish line, or a sector boundary.</summary>
  PlaceAnchor
}

/// <summary>
/// The map canvas: tiles, the circuit, and the riders on it.
///
/// Follows LapChartRenderer's pattern - a Panel host, a Paint handler, a list of
/// hit-test rectangles rebuilt every paint, a hand-drawn callout - with one
/// deliberate difference: this class subscribes to the host's mouse events itself
/// rather than having Form1 forward them. Map interaction is a state machine
/// (drag in progress, drag threshold, wheel accumulator, mode), and routing four
/// raw events through the form just to feed it adds a layer for nothing.
///
/// There is NO Graphics transform anywhere in here, and none should be added.
/// MapViewport rounds the world-pixel origin to an integer once, which is what
/// makes tiles land on integer boundaries so GDI+ blits them 1:1 instead of
/// resampling every tile every frame. It also means hit rectangles are already in
/// the same coordinates as the mouse event, and there is no transform to forget
/// to reset before drawing the callout.
/// </summary>
public sealed class TrackMapRenderer : IDisposable
{
  private const int TileSize = TileMath.TileSize;
  private const int AncestorFallbackLevels = 3;
  private const int CullMarginPx = 32;
  private const int VertexHandlePx = 9;
  private const float SegmentPickTolerancePx = 7f;

  private readonly Panel _host;

  // Not readonly: the operator can change basemap, which replaces the whole tile
  // stack underneath - see SetTiles.
  private TileLayer _tiles;
  private string _attribution;
  private readonly MapDrawResources _res = new();
  private readonly List<MapHitElement> _hits = new();

  private PointF[] _screenPolyline = Array.Empty<PointF>();
  private MapViewport _viewport;

  // Interaction state.
  /// <summary>
  /// The circuit the camera is framing, held until the operator moves the camera
  /// themselves. See FitBounds for why this is not a one-shot.
  /// </summary>
  private GeoBounds? _framing;

  private int _framingPadding = 40;

  private Point _dragStart;
  private MapViewport _dragOrigin;
  private bool _dragging;
  private bool _draggingVertex;
  private int _wheelAccumulator;
  private bool _suppressNextClick;
  private bool _disposed;

  public TrackMapRenderer(Panel host, TileSession session)
  {
    _host = host;
    _tiles = session.Layer;
    _attribution = session.Provider.Attribution;
    MaxZoom = session.Provider.MaxZoom;
    _viewport = new MapViewport(new LatLon(51.0, 9.0), 6, host.ClientSize);

    // Same reflection trick as Form1 uses on its panels: DoubleBuffered is
    // protected, and without it eight frames a second flickers badly.
    typeof(Panel)
      .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance |
                                     System.Reflection.BindingFlags.NonPublic)
      ?.SetValue(host, true);

    host.TabStop = true;
    host.Paint += OnPaint;
    host.Resize += OnResize;
    host.MouseDown += OnMouseDown;
    host.MouseMove += OnMouseMove;
    host.MouseUp += OnMouseUp;
    host.MouseWheel += OnMouseWheel;
    host.MouseDoubleClick += OnMouseDoubleClick;
    host.MouseLeave += OnMouseLeave;
    host.KeyDown += OnKeyDown;
    host.PreviewKeyDown += OnPreviewKeyDown;

    // A Panel does not receive MouseWheel unless it has focus. Without this,
    // wheel zoom silently does nothing and it looks like a maths bug.
    host.MouseEnter += (_, _) => { if (!host.Focused) host.Focus(); };

    _tiles.TilesChanged += OnTilesChanged;
  }

  private void OnTilesChanged(object? sender, EventArgs e) => _host.Invalidate();

  /// <summary>
  /// Switches to a different basemap.
  ///
  /// The attribution has to change with it - it is a condition of using the
  /// imagery, not decoration - and so does the maximum zoom, since not every
  /// provider goes as deep. If the camera is already past the new provider's
  /// limit it is pulled back, or the map would sit on a level that has no tiles
  /// and show nothing but the neutral fill.
  /// </summary>
  public void SetTiles(TileSession session)
  {
    _tiles.TilesChanged -= OnTilesChanged;

    _tiles = session.Layer;
    _attribution = session.Provider.Attribution;
    MaxZoom = session.Provider.MaxZoom;

    _tiles.TilesChanged += OnTilesChanged;

    if (_viewport.Zoom > MaxZoom)
      _viewport = _viewport.WithZoomAnchored(MaxZoom,
        new Point(_viewport.ViewSize.Width / 2, _viewport.ViewSize.Height / 2));

    AfterViewChange();
  }

  // ---- Camera --------------------------------------------------------------

  public MapViewport Viewport => _viewport;
  public int MinZoom { get; set; } = 3;
  public int MaxZoom { get; set; } = TileMath.MaxZoom;

  public event EventHandler? ViewChanged;
  public event EventHandler<MapClickEventArgs>? MapClicked;
  public event EventHandler<MapPickEventArgs>? Picked;
  public event EventHandler<MapVertexDragEventArgs>? VertexDragged;

  // ---- Content (owned by the host; the renderer only reads) ----------------

  public TrackDefinition? Track { get; set; }
  public IReadOnlyList<MapRiderMarker> Riders { get; set; } = Array.Empty<MapRiderMarker>();

  /// <summary>Per-sector occupancy, drawn as a panel. Empty hides the panel.</summary>
  public IReadOnlyList<MapSectorInfo> SectorInfo { get; set; } = Array.Empty<MapSectorInfo>();

  /// <summary>Draw the sector occupancy panel at all.</summary>
  public bool ShowSectorPanel { get; set; } = true;

  /// <summary>Draw the key explaining what each marker means.</summary>
  public bool ShowLegend { get; set; } = true;
  public MapInteractionMode Mode { get; set; } = MapInteractionMode.Navigate;
  public MapLabelParts LabelParts { get; set; } = MapLabelParts.Position | MapLabelParts.Number;
  public int LabelTopN { get; set; } = 3;
  public string? SelectedTagId { get; set; }
  public string? HoveredTagId { get; private set; }
  public int? SelectedVertexIndex { get; set; }
  public bool ShowVertices { get; set; }
  public bool DashClosingSegment { get; set; }

  /// <summary>Shown centred when there is no circuit to draw.</summary>
  public string? EmptyStateText { get; set; }

  /// <summary>Shown across the top, e.g. "Race not started".</summary>
  public string? Watermark { get; set; }

  /// <summary>Sticky selection card. Cleared by clicking empty map.</summary>
  public IReadOnlyList<string>? Callout { get; set; }

  /// <summary>
  /// Wall time of the last paint. The refresh coordinator measures Render, not
  /// OnPaint, so this is how the real cost gets into the diagnostics.
  /// </summary>
  public double LastPaintMicroseconds { get; private set; }

  /// <summary>
  /// What the last frame actually put on screen. The difference between the field
  /// size and these is the whole question of whether a big field is still legible:
  /// 250 riders that collapse into 20 clusters is a readable map, 250 separate
  /// dots is a caterpillar.
  /// </summary>
  public int LastDotCount { get; private set; }
  public int LastClusterCount { get; private set; }
  public int LastLabelCount { get; private set; }

  public void SetCenter(LatLon center, int zoom)
  {
    ReleaseFraming();
    _viewport = new MapViewport(center, Math.Clamp(zoom, MinZoom, MaxZoom), _host.ClientSize);
    AfterViewChange();
  }

  /// <summary>
  /// Frames a circuit, and keeps framing it until the operator moves the camera.
  ///
  /// Deliberately not a one-shot. A circuit is applied while the tab is still
  /// being built, when the panel is still at its constructed 200x100 rather than
  /// its laid-out size - fitting against that picks far too low a zoom and leaves
  /// the whole circuit as a dot in the middle of a continent. Re-fitting on every
  /// resize fixes that without having to guess when layout has "really" happened,
  /// and it also means resizing the window keeps the circuit framed, which is what
  /// anyone would expect. The first pan or zoom hands control over for good.
  /// </summary>
  public void FitBounds(GeoBounds bounds, int paddingPx = 40)
  {
    if (bounds.IsEmpty) return;

    _framing = bounds;
    _framingPadding = paddingPx;

    _viewport = MapViewport.Fit(bounds.Pad(30), _host.ClientSize, paddingPx, MaxZoom);
    AfterViewChange();
  }

  /// <summary>Stops the camera following the circuit. Any deliberate camera move calls this.</summary>
  private void ReleaseFraming() => _framing = null;

  public void FitTrack()
  {
    if (Track is { Points.Count: >= 2 }) FitBounds(Track.Bounds);
  }

  public void ZoomBy(int steps, Point? anchor = null)
  {
    var target = Math.Clamp(_viewport.Zoom + steps, MinZoom, MaxZoom);
    if (target == _viewport.Zoom) return;

    ReleaseFraming();

    var at = anchor ?? new Point(_viewport.ViewSize.Width / 2, _viewport.ViewSize.Height / 2);
    _viewport = _viewport.WithZoomAnchored(target, at);

    // A zoom change is also the moment to forgive transient tile failures, so a
    // brief outage does not leave grey squares for the rest of the session.
    _tiles.ForgetTransientFailures();
    AfterViewChange();
  }

  private void AfterViewChange()
  {
    // Requested here and never from Draw: asking for tiles inside a paint handler
    // builds a request/repaint feedback loop.
    _tiles.EnsureVisible(_viewport.VisibleTiles);
    _host.Invalidate();
    ViewChanged?.Invoke(this, EventArgs.Empty);
  }

  // ---- Painting ------------------------------------------------------------

  private void OnResize(object? sender, EventArgs e)
  {
    if (_host.ClientSize.Width <= 0 || _host.ClientSize.Height <= 0) return;

    if (_framing is { } framed)
    {
      // Still framing the circuit, so re-fit rather than just restretching a zoom
      // that was chosen for a different size.
      _viewport = MapViewport.Fit(framed.Pad(30), _host.ClientSize, _framingPadding, MaxZoom);
      AfterViewChange();
      return;
    }

    _viewport = _viewport.WithSize(_host.ClientSize);
    AfterViewChange();
  }

  private void OnPaint(object? sender, PaintEventArgs e)
  {
    // Stopwatch, not Environment.TickCount64: the system tick is about 15.6ms on
    // Windows, so a frame costing 3ms and one costing 15ms both measure as either
    // 0 or 16. That is useless for judging headroom on a screen budgeted at 125ms.
    var started = System.Diagnostics.Stopwatch.GetTimestamp();

    try
    {
      Draw(e.Graphics, _host.ClientRectangle);
    }
    catch (Exception ex)
    {
      // One bad frame must not take the application down mid-race.
      using var brush = new SolidBrush(Color.DimGray);
      e.Graphics.DrawString($"Map could not be drawn: {ex.Message}",
        _res.StatusFont, brush, 8, 8);
    }
    finally
    {
      LastPaintMicroseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMicroseconds;
    }
  }

  public void Draw(Graphics g, Rectangle bounds)
  {
    _hits.Clear();

    g.SetClip(bounds);
    g.FillRectangle(_res.LandBrush, bounds);

    DrawTiles(g, bounds);

    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.CompositingMode = CompositingMode.SourceOver;
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.TextRenderingHint = MapDrawResources.TextHint;

    if (Track is { Points.Count: >= 2 })
    {
      DrawTrack(g, bounds);
      DrawSectors(g);
      DrawStartFinish(g);
      if (ShowVertices) DrawVertices(g, bounds);
    }
    else
    {
      _screenPolyline = Array.Empty<PointF>();
      DrawEmptyState(g, bounds);
    }

    DrawRiders(g);
    DrawWatermark(g, bounds);
    DrawScaleBar(g, bounds);
    DrawLegend(g, bounds);
    DrawAttribution(g, bounds);
    DrawStatus(g, bounds);
    DrawSectorPanel(g, bounds);
    DrawCallout(g, bounds);
  }

  private void DrawTiles(Graphics g, Rectangle bounds)
  {
    // 1:1 blits: no smoothing, nearest neighbour, and SourceCopy on opaque tiles.
    // Anti-aliasing an unscaled image copy costs real time and achieves nothing.
    g.SmoothingMode = SmoothingMode.None;
    g.InterpolationMode = InterpolationMode.NearestNeighbor;
    g.PixelOffsetMode = PixelOffsetMode.Half;
    g.CompositingMode = CompositingMode.SourceCopy;

    var range = _viewport.VisibleTiles;

    for (var ty = range.MinY; ty <= range.MaxY; ty++)
    {
      for (var tx = range.MinX; tx <= range.MaxX; tx++)
      {
        var id = new TileId(_viewport.Zoom, tx, ty);

        var dest = new Rectangle(
          (int)(tx * (long)TileSize - _viewport.OriginX),
          (int)(ty * (long)TileSize - _viewport.OriginY),
          TileSize, TileSize);

        if (!dest.IntersectsWith(bounds)) continue;

        var tile = _tiles.Peek(id);
        if (tile is not null)
        {
          g.DrawImage(tile, dest);
          continue;
        }

        if (DrawAncestor(g, id, dest)) continue;

        // Neutral fill rather than white: a white flash reads as broken, and a
        // per-tile spinner tiled across the map reads as a crash.
        g.CompositingMode = CompositingMode.SourceOver;
        g.FillRectangle(_res.LandBrush, dest);
        g.DrawRectangle(_res.GridPen, dest);
        g.CompositingMode = CompositingMode.SourceCopy;
      }
    }

    g.PixelOffsetMode = PixelOffsetMode.Default;
  }

  /// <summary>
  /// Borrows imagery from a coarser zoom level while the real tile loads.
  ///
  /// Nearly always available, because you just zoomed in from it - and it is what
  /// makes integer-only zoom feel smooth rather than stepped.
  /// </summary>
  private bool DrawAncestor(Graphics g, TileId id, Rectangle dest)
  {
    var ancestor = id;

    for (var level = 1; level <= AncestorFallbackLevels; level++)
    {
      ancestor = ancestor.Parent;
      if (ancestor.Z < TileMath.MinZoom) return false;

      var bitmap = _tiles.Peek(ancestor);
      if (bitmap is null) continue;

      var scale = 1 << level;
      var sub = TileSize / scale;
      var source = new Rectangle((id.X % scale) * sub, (id.Y % scale) * sub, sub, sub);

      // Blurry beats blocky for a placeholder that is on screen for a moment.
      var previous = g.InterpolationMode;
      g.InterpolationMode = InterpolationMode.HighQualityBilinear;
      g.DrawImage(bitmap, dest, source, GraphicsUnit.Pixel);
      g.InterpolationMode = previous;

      return true;
    }

    return false;
  }

  private void DrawTrack(Graphics g, Rectangle bounds)
  {
    var points = Track!.Points;
    _screenPolyline = new PointF[points.Count];
    for (var i = 0; i < points.Count; i++) _screenPolyline[i] = _viewport.ToScreen(points[i]);

    var cull = Rectangle.Inflate(bounds, CullMarginPx, CullMarginPx);

    // Drawn twice: a white casing under the dark line. Without it the track
    // vanishes wherever the imagery underneath happens to be pale.
    foreach (var pen in new[] { _res.TrackCasing, _res.TrackLine })
      for (var i = 0; i < _screenPolyline.Length - 1; i++)
        if (SegmentVisible(_screenPolyline[i], _screenPolyline[i + 1], cull))
          g.DrawLine(pen, _screenPolyline[i], _screenPolyline[i + 1]);

    // The closing segment back to point zero. Dashed only while the operator is
    // still laying points down, so they can always see the loop the app will use.
    var first = _screenPolyline[0];
    var last = _screenPolyline[^1];

    if (SegmentVisible(first, last, cull))
    {
      if (DashClosingSegment)
      {
        g.DrawLine(_res.ClosingLine, last, first);
      }
      else
      {
        g.DrawLine(_res.TrackCasing, last, first);
        g.DrawLine(_res.TrackLine, last, first);
      }
    }
  }

  private void DrawSectors(Graphics g)
  {
    var sectors = Track!.Sectors;
    if (sectors.Count == 0) return;

    var geometry = Track.Geometry;
    if (!geometry.IsUsable) return;

    for (var i = 0; i < sectors.Count; i++)
    {
      var from = sectors[i].Start.Fraction;
      var to = sectors[(i + 1) % sectors.Count].Start.Fraction;

      var span = geometry.PointsBetween(from, to);
      if (span.Count < 2) continue;

      var screen = span.Select(_viewport.ToScreen).ToArray();
      g.DrawLines(_res.PenFor(sectors[i].Color, 4f), screen);

      // A tick and a name at each boundary, so the colours mean something.
      var name = string.IsNullOrWhiteSpace(sectors[i].Name) ? $"Sector {i + 1}" : sectors[i].Name;
      DrawHaloedText(g, name, screen[0], _res.SectorFont, sectors[i].Color, above: true);
    }
  }

  private void DrawStartFinish(Graphics g)
  {
    var geometry = Track!.Geometry;
    if (!geometry.IsUsable) return;

    var at = geometry.PointAtFraction(Track.StartFinish.Fraction);
    var centre = _viewport.ToScreen(at.Location);

    var (fx, fy) = RiderDotLayout.Forward(at.HeadingDegrees);
    var (px, py) = RiderDotLayout.Perpendicular(at.HeadingDegrees);

    // A chequered bar drawn across the track, plus an arrow showing which way
    // riders go - getting the direction wrong sends every dot backwards.
    const float half = 13f;
    const int squares = 6;
    var step = half * 2 / squares;

    for (var i = 0; i < squares; i++)
    {
      var t0 = -half + i * step;
      var a = new PointF(centre.X + px * t0, centre.Y + py * t0);

      var quad = new[]
      {
        new PointF(a.X - fx * 4, a.Y - fy * 4),
        new PointF(a.X + fx * 4, a.Y + fy * 4),
        new PointF(a.X + fx * 4 + px * step, a.Y + fy * 4 + py * step),
        new PointF(a.X - fx * 4 + px * step, a.Y - fy * 4 + py * step)
      };

      g.FillPolygon(i % 2 == 0 ? _res.LabelBrush : _res.WhiteBrush, quad);
    }

    g.DrawLine(_res.PenFor(Color.Black, 1f),
      new PointF(centre.X + px * half, centre.Y + py * half),
      new PointF(centre.X - px * half, centre.Y - py * half));

    var tip = new PointF(centre.X + fx * 26, centre.Y + fy * 26);
    g.FillPolygon(_res.BrushFor(Color.FromArgb(211, 47, 47)), new[]
    {
      tip,
      new PointF(tip.X - fx * 9 + px * 5, tip.Y - fy * 9 + py * 5),
      new PointF(tip.X - fx * 9 - px * 5, tip.Y - fy * 9 - py * 5)
    });

    _hits.Add(new MapHitElement
    {
      Kind = MapHitKind.StartFinish,
      Bounds = MapHitElement.Around(centre, half),
      Location = at.Location
    });
  }

  private void DrawVertices(Graphics g, Rectangle bounds)
  {
    var cull = Rectangle.Inflate(bounds, CullMarginPx, CullMarginPx);

    for (var i = 0; i < _screenPolyline.Length; i++)
    {
      var p = _screenPolyline[i];
      if (!cull.Contains((int)p.X, (int)p.Y)) continue;

      var selected = SelectedVertexIndex == i;
      var size = selected ? VertexHandlePx + 3 : VertexHandlePx;
      var rect = new RectangleF(p.X - size / 2f, p.Y - size / 2f, size, size);

      g.FillRectangle(_res.WhiteBrush, rect);
      g.DrawRectangle(selected ? _res.SelectedVertexPen : _res.VertexPen,
        rect.X, rect.Y, rect.Width, rect.Height);

      _hits.Add(new MapHitElement
      {
        Kind = MapHitKind.TrackVertex,
        Bounds = MapHitElement.Around(p, size),
        VertexIndex = i,
        Location = Track!.Points[i]
      });
    }
  }

  private void DrawRiders(Graphics g)
  {
    if (Riders.Count == 0)
    {
      LastDotCount = LastClusterCount = LastLabelCount = 0;
      return;
    }

    var layout = RiderDotLayout.Build(Riders, _viewport, SelectedTagId, HoveredTagId, LabelParts, LabelTopN);

    LastDotCount = layout.Dots.Count;
    LastClusterCount = layout.Clusters.Count;
    LastLabelCount = layout.Dots.Count(d => d.Label is { Length: > 0 });

    foreach (var cluster in layout.Clusters)
    {
      g.FillEllipse(_res.ClusterBrush,
        cluster.Centre.X - cluster.Radius, cluster.Centre.Y - cluster.Radius,
        cluster.Radius * 2, cluster.Radius * 2);
      g.DrawEllipse(_res.PenFor(Color.White, 1.5f),
        cluster.Centre.X - cluster.Radius, cluster.Centre.Y - cluster.Radius,
        cluster.Radius * 2, cluster.Radius * 2);

      // "27+11" rather than "12": the leading rider is the one worth naming, and
      // the group size is context for them.
      var text = cluster.LeaderNumber is { Length: > 0 } leader
        ? $"{leader}+{cluster.Count - 1}"
        : cluster.Count.ToString();

      g.DrawString(text, _res.ClusterFont, _res.WhiteBrush, cluster.Centre, _res.Centred);

      _hits.Add(new MapHitElement
      {
        Kind = MapHitKind.RiderCluster,
        Bounds = MapHitElement.Around(cluster.Centre, cluster.Radius),
        ClusterTagIds = cluster.TagIds
      });
    }

    // Overdue last, so an overdue rider is never buried under the pile of dots
    // that always forms at the start/finish line.
    foreach (var dot in layout.Dots.OrderBy(d => Priority(d.State)))
      DrawDot(g, dot);

    foreach (var dot in layout.Dots)
      _hits.Add(new MapHitElement
      {
        Kind = MapHitKind.RiderDot,
        Bounds = MapHitElement.Around(dot.Centre, dot.Radius),
        TagId = dot.TagId
      });
  }

  private static int Priority(TrackPositionState state) => state switch
  {
    TrackPositionState.LongOverdue => 0,
    TrackPositionState.Retired or TrackPositionState.DidNotStart => 1,
    TrackPositionState.NoPrediction or TrackPositionState.OnGrid => 2,
    TrackPositionState.Finished => 3,
    TrackPositionState.OnTrack => 4,
    TrackPositionState.Overdue => 5,
    _ => 4
  };

  private void DrawDot(Graphics g, PlacedDot dot)
  {
    var (fill, outline, hollow) = Appearance(dot.State, dot.Fraction);

    var r = dot.Radius;
    var box = new RectangleF(dot.Centre.X - r, dot.Centre.Y - r, r * 2, r * 2);

    if (hollow)
    {
      // A hollow ring says "no fix on this rider" honestly, rather than inventing
      // a position and drawing it with the same confidence as a real one.
      g.FillEllipse(_res.PillBrush, box);
      g.DrawEllipse(_res.PenFor(outline, 2f), box);
    }
    else
    {
      g.FillEllipse(_res.BrushFor(fill), box);
      g.DrawEllipse(_res.PenFor(outline, dot.Highlighted ? 2.5f : 1.5f), box);
    }

    if (dot.Badge is { Length: > 0 })
      DrawHaloedText(g, dot.Badge, new PointF(dot.Centre.X, dot.Centre.Y + r + 8),
        _res.BadgeFont, MapDrawResources.UrgentColor, above: false);

    if (dot.Label is { Length: > 0 })
    {
      if (dot.NeedsLeaderLine) g.DrawLine(_res.LeaderLine, dot.Centre, dot.LabelAnchor);
      DrawHaloedText(g, dot.Label, dot.LabelAnchor, _res.LabelFont, Color.Black, above: false);
    }
  }

  private static (Color Fill, Color Outline, bool Hollow) Appearance(TrackPositionState state, double fraction) =>
    state switch
    {
      TrackPositionState.OnTrack => (MapDrawResources.OnTrackColor, Color.White, false),
      TrackPositionState.Overdue when fraction > TrackPositionSolver.MildOverdueFraction =>
        (MapDrawResources.UrgentColor, Color.White, false),
      TrackPositionState.Overdue => (MapDrawResources.OverdueColor, Color.White, false),
      TrackPositionState.LongOverdue => (Color.Transparent, MapDrawResources.DimColor, true),
      TrackPositionState.NoPrediction or TrackPositionState.OnGrid =>
        (Color.Transparent, MapDrawResources.OnTrackColor, true),
      TrackPositionState.Finished => (MapDrawResources.TrackColor, Color.White, false),
      TrackPositionState.Retired or TrackPositionState.DidNotStart =>
        (Color.Transparent, MapDrawResources.DimColor, true),
      _ => (MapDrawResources.OnTrackColor, Color.White, false)
    };

  // ---- Chrome --------------------------------------------------------------

  private void DrawEmptyState(Graphics g, Rectangle bounds)
  {
    if (string.IsNullOrEmpty(EmptyStateText)) return;

    var size = g.MeasureString(EmptyStateText, _res.EmptyStateFont);
    var box = new RectangleF(
      bounds.Width / 2f - size.Width / 2 - 16, bounds.Height / 2f - size.Height / 2 - 10,
      size.Width + 32, size.Height + 20);

    g.FillRectangle(_res.PillBrush, box);
    g.DrawString(EmptyStateText, _res.EmptyStateFont, _res.AttributionBrush,
      new PointF(bounds.Width / 2f, bounds.Height / 2f), _res.Centred);
  }

  private void DrawWatermark(Graphics g, Rectangle bounds)
  {
    if (string.IsNullOrEmpty(Watermark)) return;

    var size = g.MeasureString(Watermark, _res.EmptyStateFont);
    var box = new RectangleF(bounds.Width / 2f - size.Width / 2 - 12, 8, size.Width + 24, size.Height + 8);

    g.FillRectangle(_res.PillBrush, box);
    g.DrawString(Watermark, _res.EmptyStateFont, _res.AttributionBrush,
      new PointF(bounds.Width / 2f, box.Y + box.Height / 2), _res.Centred);
  }

  /// <summary>A map without a scale bar is guesswork.</summary>
  private void DrawScaleBar(Graphics g, Rectangle bounds)
  {
    var metresPerPixel = _viewport.MetresPerPixel;
    if (metresPerPixel <= 0) return;

    // Snap to a 1/2/5 series so the label is a round number.
    var target = metresPerPixel * 110;
    var magnitude = Math.Pow(10, Math.Floor(Math.Log10(target)));
    var metres = new[] { 1.0, 2.0, 5.0, 10.0 }.Select(m => m * magnitude).First(m => m >= target);

    var width = (float)(metres / metresPerPixel);
    var y = bounds.Bottom - 14f;
    var x = bounds.Left + 10f;

    g.FillRectangle(_res.PillBrush, x - 4, y - 16, width + 8, 26);
    g.DrawLine(_res.ScaleBarPen, x, y, x + width, y);
    g.DrawLine(_res.ScaleBarPen, x, y - 4, x, y + 4);
    g.DrawLine(_res.ScaleBarPen, x + width, y - 4, x + width, y + 4);

    var text = metres >= 1000 ? $"{metres / 1000:0.#} km" : $"{metres:0} m";
    g.DrawString(text, _res.AttributionFont, _res.AttributionBrush, new PointF(x, y - 9), _res.LeftAligned);
  }

  /// <summary>
  /// Attribution. Drawn unconditionally, outside every clip and cull path, because
  /// the tile usage policy requires it. If a map image ever gets exported to a
  /// report, this has to travel with it.
  /// </summary>
  private void DrawAttribution(Graphics g, Rectangle bounds)
  {
    var size = g.MeasureString(_attribution, _res.AttributionFont);
    var box = new RectangleF(
      bounds.Right - size.Width - 12, bounds.Bottom - size.Height - 8,
      size.Width + 8, size.Height + 4);

    g.FillRectangle(_res.PillBrush, box);
    g.DrawString(_attribution, _res.AttributionFont, _res.AttributionBrush, box.X + 4, box.Y + 2);
  }

  /// <summary>
  /// The key to the markers.
  ///
  /// Adaptive on purpose: it lists only the states actually on screen. A legend
  /// with a "Retired" row before anybody has retired teaches the operator to
  /// ignore it, and the map has no screen space to spare for rows that describe
  /// nothing.
  /// </summary>
  private void DrawLegend(Graphics g, Rectangle bounds)
  {
    if (!ShowLegend) return;

    var present = new List<(string Text, TrackPositionState State, double Fraction)>();

    void Consider(string text, TrackPositionState state, Func<MapRiderMarker, bool> match, double fraction = 0)
    {
      for (var i = 0; i < Riders.Count; i++)
        if (match(Riders[i]))
        {
          present.Add((text, state, fraction));
          return;
        }
    }

    Consider("On track", TrackPositionState.OnTrack, r => r.State == TrackPositionState.OnTrack);
    Consider("Overdue", TrackPositionState.Overdue,
      r => r.State == TrackPositionState.Overdue && r.Fraction <= TrackPositionSolver.MildOverdueFraction, 1.05);
    Consider("Well overdue", TrackPositionState.Overdue,
      r => r.State == TrackPositionState.Overdue && r.Fraction > TrackPositionSolver.MildOverdueFraction, 2.0);
    Consider("No pace yet", TrackPositionState.NoPrediction,
      r => r.State is TrackPositionState.NoPrediction or TrackPositionState.OnGrid);
    Consider("Finished", TrackPositionState.Finished, r => r.State == TrackPositionState.Finished);
    Consider("Retired / not started", TrackPositionState.Retired,
      r => r.State is TrackPositionState.Retired or TrackPositionState.DidNotStart);
    Consider("Long overdue", TrackPositionState.LongOverdue, r => r.State == TrackPositionState.LongOverdue);

    var hasCluster = LastClusterCount > 0;
    if (present.Count == 0 && !hasCluster) return;

    const int rowHeight = 18;
    var rows = present.Count + (hasCluster ? 1 : 0);

    var width = 0f;
    foreach (var (text, _, _) in present) width = Math.Max(width, _res.Measure(g, text, _res.StatusFont).Width);
    if (hasCluster) width = Math.Max(width, _res.Measure(g, "Group of riders", _res.StatusFont).Width);

    var box = new RectangleF(
      bounds.Left + 10,
      bounds.Bottom - 34 - (rows * rowHeight + 10),
      width + 34,
      rows * rowHeight + 10);

    g.FillRectangle(_res.PillBrush, box);
    g.DrawRectangle(_res.PenFor(Color.FromArgb(70, 60, 60, 60), 1f), box.X, box.Y, box.Width, box.Height);

    var y = box.Y + 5;

    foreach (var (text, state, fraction) in present)
    {
      DrawLegendMarker(g, new PointF(box.X + 14, y + rowHeight / 2f), state, fraction);
      g.DrawString(text, _res.StatusFont, _res.LabelBrush,
        new PointF(box.X + 26, y + rowHeight / 2f), _res.LeftAligned);
      y += rowHeight;
    }

    if (hasCluster)
    {
      var centre = new PointF(box.X + 14, y + rowHeight / 2f);
      g.FillEllipse(_res.ClusterBrush, centre.X - 7, centre.Y - 7, 14, 14);
      g.DrawEllipse(_res.PenFor(Color.White, 1.5f), centre.X - 7, centre.Y - 7, 14, 14);

      g.DrawString("Group of riders", _res.StatusFont, _res.LabelBrush,
        new PointF(box.X + 26, centre.Y), _res.LeftAligned);
    }
  }

  /// <summary>Draws one swatch using exactly the same rules as a real dot.</summary>
  private void DrawLegendMarker(Graphics g, PointF centre, TrackPositionState state, double fraction)
  {
    var (fill, outline, hollow) = Appearance(state, fraction);
    var box = new RectangleF(centre.X - 6, centre.Y - 6, 12, 12);

    if (hollow)
    {
      g.FillEllipse(_res.PillBrush, box);
      g.DrawEllipse(_res.PenFor(outline, 2f), box);
    }
    else
    {
      g.FillEllipse(_res.BrushFor(fill), box);
      g.DrawEllipse(_res.PenFor(outline, 1.5f), box);
    }
  }

  /// <summary>
  /// Occupancy per sector.
  ///
  /// The one part of this screen that does not get less useful as the field
  /// grows: individual dots become a chain of blobs somewhere past a hundred
  /// riders, but "58 in the Back Straight" reads identically at any field size.
  /// </summary>
  private void DrawSectorPanel(Graphics g, Rectangle bounds)
  {
    if (!ShowSectorPanel || SectorInfo.Count == 0) return;

    const int rowHeight = 19;
    const int swatch = 9;

    var width = 0f;
    foreach (var sector in SectorInfo)
      width = Math.Max(width, _res.Measure(g, RowText(sector), _res.StatusFont).Width);

    var box = new RectangleF(
      bounds.Left + 10,
      bounds.Top + (string.IsNullOrEmpty(_tiles.StatusText) ? 8 : 34),
      width + swatch + 22,
      SectorInfo.Count * rowHeight + 10);

    g.FillRectangle(_res.PillBrush, box);
    g.DrawRectangle(_res.PenFor(Color.FromArgb(70, 60, 60, 60), 1f), box.X, box.Y, box.Width, box.Height);

    var y = box.Y + 5;

    foreach (var sector in SectorInfo)
    {
      g.FillRectangle(_res.BrushFor(sector.Color), box.X + 7, y + 5, swatch, swatch);
      g.DrawString(RowText(sector), _res.StatusFont, _res.LabelBrush,
        new PointF(box.X + 7 + swatch + 6, y + rowHeight / 2f), _res.LeftAligned);

      y += rowHeight;
    }
  }

  private static string RowText(MapSectorInfo sector) =>
    sector.LeaderNumber is { Length: > 0 } leader
      ? $"{sector.Name}   {sector.RiderCount}   (lead {leader})"
      : $"{sector.Name}   {sector.RiderCount}";

  private void DrawStatus(Graphics g, Rectangle bounds)
  {
    var status = _tiles.StatusText;
    if (string.IsNullOrEmpty(status)) return;

    var size = g.MeasureString(status, _res.StatusFont);
    var box = new RectangleF(bounds.Left + 10, bounds.Top + 8, size.Width + 10, size.Height + 4);

    g.FillRectangle(_res.PillBrush, box);
    g.DrawString(status, _res.StatusFont, _res.AttributionBrush, box.X + 5, box.Y + 2);
  }

  /// <summary>
  /// The selection card. A sticky panel rather than a tooltip: "who is that?" is
  /// answered by something that stays put, not by something that vanishes the
  /// moment the cursor moves off.
  /// </summary>
  private void DrawCallout(Graphics g, Rectangle bounds)
  {
    if (Callout is not { Count: > 0 }) return;

    float width = 0, height = 6;
    foreach (var line in Callout)
    {
      var size = g.MeasureString(line, _res.StatusFont);
      width = Math.Max(width, size.Width);
      height += size.Height + 2;
    }

    var box = new RectangleF(bounds.Right - width - 34, bounds.Top + 8, width + 20, height + 6);

    g.FillRectangle(_res.BrushFor(Color.FromArgb(238, 255, 255, 255)), box);
    g.DrawRectangle(_res.PenFor(Color.FromArgb(120, 60, 60, 60), 1f),
      box.X, box.Y, box.Width, box.Height);

    var y = box.Y + 6;
    foreach (var line in Callout)
    {
      g.DrawString(line, _res.StatusFont, _res.LabelBrush, box.X + 10, y);
      y += g.MeasureString(line, _res.StatusFont).Height + 2;
    }
  }

  /// <summary>
  /// White halo then black text: legible over any imagery. It costs five
  /// DrawString calls, which is the other reason the label budget exists.
  /// </summary>
  private void DrawHaloedText(Graphics g, string text, PointF at, Font font, Color color, bool above)
  {
    var point = above ? new PointF(at.X, at.Y - 10) : at;

    for (var dx = -1; dx <= 1; dx++)
      for (var dy = -1; dy <= 1; dy++)
        if (dx != 0 || dy != 0)
          g.DrawString(text, font, _res.HaloBrush, new PointF(point.X + dx, point.Y + dy), _res.Centred);

    g.DrawString(text, font, _res.BrushFor(color), point, _res.Centred);
  }

  private static bool SegmentVisible(PointF a, PointF b, Rectangle cull)
  {
    var left = Math.Min(a.X, b.X);
    var top = Math.Min(a.Y, b.Y);
    var box = new RectangleF(left, top, Math.Abs(a.X - b.X) + 1, Math.Abs(a.Y - b.Y) + 1);
    return box.IntersectsWith(cull);
  }

  // ---- Hit-testing ---------------------------------------------------------

  /// <summary>
  /// The topmost drawn element under a point, or null.
  ///
  /// Walks BACKWARDS on purpose. The lap chart's FirstOrDefault returns the
  /// bottom-most match, which is wrong here: rider dots overlap constantly and the
  /// one on top is the one the operator is pointing at.
  /// </summary>
  public MapHitElement? HitTest(Point p, params MapHitKind[] kinds)
  {
    for (var i = _hits.Count - 1; i >= 0; i--)
    {
      var element = _hits[i];
      if (!element.Bounds.Contains(p)) continue;
      if (kinds.Length > 0 && !kinds.Contains(element.Kind)) continue;
      return element;
    }

    return null;
  }

  /// <summary>
  /// Index of the track segment under a point, or -1. Segments cannot go in the
  /// rectangle list: a diagonal segment's bounding box is most of the screen.
  /// </summary>
  public int HitTestSegment(Point p, float tolerancePx = SegmentPickTolerancePx)
  {
    if (_screenPolyline.Length < 2) return -1;

    var best = -1;
    var bestDistance = tolerancePx;

    for (var i = 0; i < _screenPolyline.Length; i++)
    {
      var a = _screenPolyline[i];
      var b = _screenPolyline[(i + 1) % _screenPolyline.Length];

      var d = DistanceToSegment(p, a, b);
      if (d >= bestDistance) continue;

      bestDistance = d;
      best = i;
    }

    return best;
  }

  private static float DistanceToSegment(PointF p, PointF a, PointF b)
  {
    float dx = b.X - a.X, dy = b.Y - a.Y;
    var lengthSquared = dx * dx + dy * dy;

    if (lengthSquared <= 0) return (float)Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));

    var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0f, 1f);
    float cx = p.X - (a.X + dx * t), cy = p.Y - (a.Y + dy * t);
    return (float)Math.Sqrt(cx * cx + cy * cy);
  }

  // ---- Mouse ---------------------------------------------------------------

  private void OnMouseDown(object? sender, MouseEventArgs e)
  {
    if (e.Button != MouseButtons.Left) return;

    _dragStart = e.Location;
    _dragOrigin = _viewport;
    _dragging = false;
    _draggingVertex = false;

    if (Mode == MapInteractionMode.MoveVertex)
    {
      var vertex = HitTest(e.Location, MapHitKind.TrackVertex);
      if (vertex is not null)
      {
        SelectedVertexIndex = vertex.VertexIndex;
        _draggingVertex = true;
        _host.Invalidate();
      }
    }
  }

  private void OnMouseMove(object? sender, MouseEventArgs e)
  {
    if (e.Button == MouseButtons.Left && (_dragging || _draggingVertex || PastDragThreshold(e.Location)))
    {
      _dragging = true;

      if (_draggingVertex && SelectedVertexIndex is { } index)
      {
        VertexDragged?.Invoke(this, new MapVertexDragEventArgs
        {
          Index = index,
          Location = _viewport.ToLatLon(e.Location),
          Finished = false
        });
        return;
      }

      _host.Cursor = Cursors.SizeAll;
      ReleaseFraming();

      // Recomputed from the ORIGINAL viewport and the cumulative delta, never
      // incrementally: integer rounding accumulates visible drift over a long drag.
      _viewport = _dragOrigin.PannedByPixels(_dragStart.X - e.X, _dragStart.Y - e.Y);
      _host.Invalidate();
      return;
    }

    var hovered = HitTest(e.Location, MapHitKind.RiderDot, MapHitKind.RiderCluster);
    var tag = hovered?.Kind == MapHitKind.RiderDot ? hovered.TagId : null;

    if (tag != HoveredTagId)
    {
      HoveredTagId = tag;
      _host.Invalidate();
    }

    _host.Cursor = hovered is not null || (Mode == MapInteractionMode.MoveVertex &&
                                           HitTest(e.Location, MapHitKind.TrackVertex) is not null)
      ? Cursors.Hand
      : Cursors.Default;
  }

  private void OnMouseUp(object? sender, MouseEventArgs e)
  {
    if (e.Button != MouseButtons.Left) return;

    _host.Cursor = Cursors.Default;

    if (_draggingVertex && SelectedVertexIndex is { } index)
    {
      if (_dragging)
        VertexDragged?.Invoke(this, new MapVertexDragEventArgs
        {
          Index = index,
          Location = _viewport.ToLatLon(e.Location),
          Finished = true
        });

      _draggingVertex = false;
      _dragging = false;
      return;
    }

    if (_dragging)
    {
      // A gesture that moved further than the system drag threshold was a pan and
      // consumes itself. That is what lets click-to-place and drag-to-pan coexist
      // with no modifier key: someone who meant to click does not move five pixels.
      _dragging = false;
      AfterViewChange();
      return;
    }

    if (_suppressNextClick)
    {
      _suppressNextClick = false;
      return;
    }

    var element = HitTest(e.Location);
    if (element is not null && element.Kind is MapHitKind.RiderDot or MapHitKind.RiderCluster)
    {
      Picked?.Invoke(this, new MapPickEventArgs
      {
        Element = element, Screen = e.Location, DoubleClick = false
      });
      return;
    }

    MapClicked?.Invoke(this, new MapClickEventArgs
    {
      Location = _viewport.ToLatLon(e.Location),
      Screen = e.Location,
      CtrlHeld = Control.ModifierKeys.HasFlag(Keys.Control)
    });
  }

  private void OnMouseDoubleClick(object? sender, MouseEventArgs e)
  {
    if (e.Button != MouseButtons.Left) return;

    var element = HitTest(e.Location, MapHitKind.RiderDot);
    if (element is not null)
    {
      _suppressNextClick = true;
      Picked?.Invoke(this, new MapPickEventArgs
      {
        Element = element, Screen = e.Location, DoubleClick = true
      });
      return;
    }

    // Only in Navigate mode: in a placing mode a double-click would otherwise
    // drop two points on top of each other.
    if (Mode != MapInteractionMode.Navigate) return;

    _suppressNextClick = true;
    ZoomBy(1, e.Location);
  }

  private void OnMouseWheel(object? sender, MouseEventArgs e)
  {
    // Not every wheel sends 120 per notch; high-resolution ones send less. Keep
    // the remainder or fine scrolling never reaches a zoom step.
    _wheelAccumulator += e.Delta;

    var steps = _wheelAccumulator / 120;
    if (steps == 0) return;

    _wheelAccumulator -= steps * 120;
    ZoomBy(steps, e.Location);
  }

  /// <summary>
  /// Arrow keys would otherwise be swallowed by the containing layout as
  /// navigation between controls, so the panel has to claim them explicitly.
  /// </summary>
  private void OnPreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
  {
    if (e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down) e.IsInputKey = true;
  }

  private void OnKeyDown(object? sender, KeyEventArgs e)
  {
    const int nudge = 60;

    switch (e.KeyCode)
    {
      case Keys.Home:
      case Keys.F:
        FitTrack();
        break;

      case Keys.Add:
      case Keys.Oemplus:
        ZoomBy(1);
        break;

      case Keys.Subtract:
      case Keys.OemMinus:
        ZoomBy(-1);
        break;

      case Keys.Left: Pan(-nudge, 0); break;
      case Keys.Right: Pan(nudge, 0); break;
      case Keys.Up: Pan(0, -nudge); break;
      case Keys.Down: Pan(0, nudge); break;

      default: return;
    }

    e.Handled = true;
  }

  private void Pan(int dx, int dy)
  {
    ReleaseFraming();
    _viewport = _viewport.PannedByPixels(dx, dy);
    AfterViewChange();
  }

  private void OnMouseLeave(object? sender, EventArgs e)
  {
    if (HoveredTagId is null) return;

    HoveredTagId = null;
    _host.Invalidate();
  }

  /// <summary>
  /// SystemInformation.DragSize, not a hardcoded five: it honours the user's
  /// accessibility settings, and it is what every other drag in Windows uses.
  /// </summary>
  private bool PastDragThreshold(Point p) =>
    Math.Abs(p.X - _dragStart.X) > SystemInformation.DragSize.Width / 2 ||
    Math.Abs(p.Y - _dragStart.Y) > SystemInformation.DragSize.Height / 2;

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;

    _host.Paint -= OnPaint;
    _host.Resize -= OnResize;
    _host.MouseDown -= OnMouseDown;
    _host.MouseMove -= OnMouseMove;
    _host.MouseUp -= OnMouseUp;
    _host.MouseWheel -= OnMouseWheel;
    _host.MouseDoubleClick -= OnMouseDoubleClick;
    _host.MouseLeave -= OnMouseLeave;
    _host.KeyDown -= OnKeyDown;
    _host.PreviewKeyDown -= OnPreviewKeyDown;
    _tiles.TilesChanged -= OnTilesChanged;

    _res.Dispose();
  }
}
