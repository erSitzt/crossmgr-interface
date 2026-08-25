using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace CrossMgrInterface;

/// <summary>
/// Every GDI+ object the map draws with, created once and disposed once.
///
/// This class exists because of a specific class of bug rather than for tidiness.
/// There is a hard limit of ten thousand GDI handles per process: at eight frames
/// a second, a Font or Pen allocated inside a draw loop reaches it in minutes, and
/// the symptom is not an exception but the application quietly drawing black
/// rectangles. The lap chart allocates a Font per lap column to this day - do not
/// copy that here.
///
/// StringFormat is IDisposable too, and is the one everybody forgets.
/// </summary>
public sealed class MapDrawResources : IDisposable
{
  /// <summary>OSM's own land colour. Never white: a white flash reads as "broken".</summary>
  public static readonly Color LandColor = Color.FromArgb(242, 239, 233);

  public static readonly Color TrackColor = Color.FromArgb(38, 50, 56);
  public static readonly Color OnTrackColor = Color.FromArgb(21, 101, 192);
  public static readonly Color OverdueColor = Color.FromArgb(245, 166, 35);
  public static readonly Color UrgentColor = Color.FromArgb(211, 47, 47);
  public static readonly Color DimColor = Color.FromArgb(120, 124, 128);
  public static readonly Color ClusterColor = Color.FromArgb(55, 71, 79);

  public Font LabelFont { get; }
  public Font BadgeFont { get; }
  public Font ClusterFont { get; }
  public Font AttributionFont { get; }
  public Font StatusFont { get; }
  public Font SectorFont { get; }
  public Font EmptyStateFont { get; }

  /// <summary>White casing under the track line - the cartographic trick that keeps
  /// a thin dark line legible over arbitrary imagery.</summary>
  public Pen TrackCasing { get; }

  public Pen TrackLine { get; }
  public Pen ClosingLine { get; }
  public Pen LeaderLine { get; }
  public Pen VertexPen { get; }
  public Pen SelectedVertexPen { get; }
  public Pen ScaleBarPen { get; }
  public Pen GridPen { get; }

  public SolidBrush LandBrush { get; }
  public SolidBrush LabelBrush { get; }
  public SolidBrush HaloBrush { get; }
  public SolidBrush ClusterBrush { get; }
  public SolidBrush PillBrush { get; }
  public SolidBrush AttributionBrush { get; }
  public SolidBrush WhiteBrush { get; }

  public StringFormat Centred { get; }
  public StringFormat LeftAligned { get; }

  private readonly Dictionary<Color, SolidBrush> _stateBrushes = new();
  private readonly Dictionary<(int Argb, float Width), Pen> _statePens = new();
  private readonly Dictionary<string, SizeF> _measured = new();

  public MapDrawResources()
  {
    LabelFont = new Font("Segoe UI", 8f, FontStyle.Bold);
    BadgeFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);
    ClusterFont = new Font("Segoe UI", 9f, FontStyle.Bold);
    AttributionFont = new Font("Segoe UI", 7.5f);
    StatusFont = new Font("Segoe UI", 8.25f);
    SectorFont = new Font("Segoe UI", 8f, FontStyle.Bold);
    EmptyStateFont = new Font("Segoe UI", 11f);

    TrackCasing = new Pen(Color.White, 7f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
    TrackLine = new Pen(TrackColor, 4f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
    ClosingLine = new Pen(Color.FromArgb(150, TrackColor), 3f) { DashStyle = DashStyle.Dash };
    LeaderLine = new Pen(Color.FromArgb(160, 60, 60, 60), 1f);
    VertexPen = new Pen(Color.FromArgb(60, 60, 60), 1.5f);
    SelectedVertexPen = new Pen(Color.FromArgb(211, 47, 47), 2.5f);
    ScaleBarPen = new Pen(Color.FromArgb(70, 70, 70), 1.5f);
    GridPen = new Pen(Color.FromArgb(228, 224, 216), 1f);

    LandBrush = new SolidBrush(LandColor);
    LabelBrush = new SolidBrush(Color.Black);
    HaloBrush = new SolidBrush(Color.White);
    ClusterBrush = new SolidBrush(ClusterColor);
    PillBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
    AttributionBrush = new SolidBrush(Color.FromArgb(70, 70, 70));
    WhiteBrush = new SolidBrush(Color.White);

    // GenericTypographic, or MeasureString silently adds padding and every
    // centred label sits slightly off.
    Centred = new StringFormat(StringFormat.GenericTypographic)
    {
      Alignment = StringAlignment.Center,
      LineAlignment = StringAlignment.Center
    };

    LeftAligned = new StringFormat(StringFormat.GenericTypographic)
    {
      Alignment = StringAlignment.Near,
      LineAlignment = StringAlignment.Center
    };
  }

  /// <summary>
  /// Anti-aliased, NOT ClearType. The lap chart uses ClearType correctly over a
  /// flat white background; over map imagery its subpixel fringes look dirty and
  /// fight the white halo behind each label.
  /// </summary>
  public static TextRenderingHint TextHint => TextRenderingHint.AntiAliasGridFit;

  public SolidBrush BrushFor(Color color)
  {
    if (_stateBrushes.TryGetValue(color, out var brush)) return brush;
    return _stateBrushes[color] = new SolidBrush(color);
  }

  /// <summary>
  /// Keyed on the full ARGB and the width as a tuple, not on a packed int. The
  /// packed version dropped the alpha channel, so two pens differing only in
  /// transparency would silently share one - and the map draws both opaque and
  /// translucent pens.
  /// </summary>
  public Pen PenFor(Color color, float width)
  {
    var key = (color.ToArgb(), width);
    if (_statePens.TryGetValue(key, out var pen)) return pen;
    return _statePens[key] = new Pen(color, width);
  }

  /// <summary>
  /// Memoised text measurement. MeasureString is a full text layout, not a lookup;
  /// measuring 250 labels a frame is the whole frame budget. Rider numbers are a
  /// tiny fixed set, so the table stays small.
  /// </summary>
  public SizeF Measure(Graphics g, string text, Font font)
  {
    var key = font.Name + font.Size + font.Style + "\u0000" + text;
    if (_measured.TryGetValue(key, out var size)) return size;

    if (_measured.Count > 4000) _measured.Clear();
    return _measured[key] = g.MeasureString(text, font, int.MaxValue, Centred);
  }

  public void Dispose()
  {
    foreach (var d in new IDisposable[]
             {
               LabelFont, BadgeFont, ClusterFont, AttributionFont, StatusFont, SectorFont, EmptyStateFont,
               TrackCasing, TrackLine, ClosingLine, LeaderLine, VertexPen, SelectedVertexPen, ScaleBarPen, GridPen,
               LandBrush, LabelBrush, HaloBrush, ClusterBrush, PillBrush, AttributionBrush, WhiteBrush,
               Centred, LeftAligned
             })
      d.Dispose();

    foreach (var brush in _stateBrushes.Values) brush.Dispose();
    foreach (var pen in _statePens.Values) pen.Dispose();

    _stateBrushes.Clear();
    _statePens.Clear();
    _measured.Clear();
  }
}
