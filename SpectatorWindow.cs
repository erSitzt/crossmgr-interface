using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace CrossMgrInterface;

/// <summary>
/// The leaderboard for the crowd, on a second screen - a TV or projector beside
/// the timing tent.
///
/// Painted rather than a grid: the rows are sized to fill the screen, whatever
/// its size and however many riders it has to hold, and read from across a
/// paddock - white on black, large, nothing that looks like a control. The
/// choices live on a right-click menu so nothing but the board is ever on it.
///
/// It only draws what it is given (<see cref="ShowBoard"/>); the main window
/// builds the board every second.
/// </summary>
public sealed class SpectatorWindow : Form
{
  private static readonly Color Background = Color.FromArgb(12, 12, 16);
  private static readonly Color RowShade = Color.FromArgb(26, 26, 34);
  private static readonly Color Rule = Color.FromArgb(60, 60, 72);
  private static readonly Color Dim = Color.FromArgb(120, 120, 130);
  private static readonly Color Muted = Color.FromArgb(170, 170, 180);
  private static readonly Color Gold = Color.FromArgb(240, 196, 25);
  private static readonly Color Silver = Color.FromArgb(200, 204, 210);
  private static readonly Color Bronze = Color.FromArgb(205, 127, 50);

  /// <summary>Purple for the fastest lap, as on every timing screen at a race track.</summary>
  private static readonly Color Purple = Color.FromArgb(190, 110, 255);

  /// <summary>Below this a row is no longer readable from a distance: split into two columns, then cut.</summary>
  private const int MinRowHeight = 30;

  private SpectatorBoard? _board;
  private int? _rowLimit;
  private bool _fullScreen;
  private Rectangle _windowedBounds;
  private readonly Dictionary<(float, FontStyle), Font> _fonts = new();

  private readonly ToolStripMenuItem _top10 = new("Top 10");
  private readonly ToolStripMenuItem _top20 = new("Top 20");
  private readonly ToolStripMenuItem _all = new("All riders");
  private readonly ToolStripMenuItem _fullScreenItem = new("Full screen");

  /// <summary>How many rows was chosen, or which screen it is on: the main window remembers both.</summary>
  public event EventHandler? PreferencesChanged;

  /// <summary>The number of rows, from the menu or the 1/2/A keys; null for every rider.</summary>
  [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
  public int? RowLimit
  {
    get => _rowLimit;
    set
    {
      _rowLimit = value;
      _top10.Checked = value == 10;
      _top20.Checked = value == 20;
      _all.Checked = value == null;
      Invalidate();
    }
  }

  /// <summary>The screen the window is on, by its device name.</summary>
  public string ScreenName => Screen.FromControl(this).DeviceName;

  public bool IsFullScreen => _fullScreen;

  public SpectatorWindow()
  {
    Text = "Spectator screen";
    BackColor = Background;
    ForeColor = Color.White;
    StartPosition = FormStartPosition.Manual;
    ShowInTaskbar = true;
    KeyPreview = true;
    MinimumSize = new Size(640, 360);
    ClientSize = new Size(1280, 720);
    SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
             ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

    _top10.Click += (_, _) => ChooseRows(10);
    _top20.Click += (_, _) => ChooseRows(20);
    _all.Click += (_, _) => ChooseRows(null);
    _fullScreenItem.ShortcutKeyDisplayString = "F11";
    _fullScreenItem.Click += (_, _) => SetFullScreen(!_fullScreen);

    var nextScreen = new ToolStripMenuItem("Move to next screen") { ShortcutKeyDisplayString = "Ctrl+Right" };
    nextScreen.Click += (_, _) => MoveToNextScreen();
    var close = new ToolStripMenuItem("Close");
    close.Click += (_, _) => Close();

    _top10.ShortcutKeyDisplayString = "1";
    _top20.ShortcutKeyDisplayString = "2";
    _all.ShortcutKeyDisplayString = "A";

    ContextMenuStrip = new ContextMenuStrip();
    ContextMenuStrip.Items.AddRange(new ToolStripItem[]
    {
      _top10, _top20, _all, new ToolStripSeparator(), _fullScreenItem, nextScreen, new ToolStripSeparator(), close
    });
    ContextMenuStrip.Opening += (_, _) => _fullScreenItem.Checked = _fullScreen;

    RowLimit = 10;
  }

  /// <summary>New standings. Call on the UI thread.</summary>
  public void ShowBoard(SpectatorBoard board)
  {
    _board = board;
    Invalidate();
  }

  // ---- Placing the window -------------------------------------------------------------

  /// <summary>
  /// Puts the window on <paramref name="screen"/>: full screen when asked,
  /// otherwise a window in the middle of it.
  /// </summary>
  public void PlaceOn(Screen screen, bool fullScreen)
  {
    var area = screen.WorkingArea;
    var size = new Size(Math.Min(1280, area.Width), Math.Min(720, area.Height));
    _windowedBounds = new Rectangle(
      area.Left + (area.Width - size.Width) / 2,
      area.Top + (area.Height - size.Height) / 2,
      size.Width, size.Height);

    _fullScreen = false;
    FormBorderStyle = FormBorderStyle.Sizable;
    WindowState = FormWindowState.Normal;
    Bounds = _windowedBounds;

    if (fullScreen) SetFullScreen(true);
  }

  private void SetFullScreen(bool on)
  {
    if (on == _fullScreen) return;

    if (on)
    {
      _windowedBounds = Bounds;
      var screen = Screen.FromControl(this);
      _fullScreen = true;
      WindowState = FormWindowState.Normal;
      FormBorderStyle = FormBorderStyle.None;
      Bounds = screen.Bounds;
    }
    else
    {
      _fullScreen = false;
      FormBorderStyle = FormBorderStyle.Sizable;
      Bounds = _windowedBounds;
    }

    Invalidate();
  }

  private void MoveToNextScreen()
  {
    var screens = Screen.AllScreens;
    if (screens.Length < 2) return;

    var at = Array.FindIndex(screens, s => s.DeviceName == ScreenName);
    var next = screens[(at + 1) % screens.Length];
    PlaceOn(next, _fullScreen);
    PreferencesChanged?.Invoke(this, EventArgs.Empty);
  }

  private void ChooseRows(int? limit)
  {
    RowLimit = limit;
    PreferencesChanged?.Invoke(this, EventArgs.Empty);
  }

  protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
  {
    switch (keyData)
    {
      case Keys.F11:
        SetFullScreen(!_fullScreen);
        return true;
      case Keys.Escape when _fullScreen:
        SetFullScreen(false);
        return true;
      case Keys.Control | Keys.Right:
        MoveToNextScreen();
        return true;
      case Keys.D1 or Keys.NumPad1:
        ChooseRows(10);
        return true;
      case Keys.D2 or Keys.NumPad2:
        ChooseRows(20);
        return true;
      case Keys.A:
        ChooseRows(null);
        return true;
    }

    return base.ProcessCmdKey(ref msg, keyData);
  }

  protected override void OnDoubleClick(EventArgs e)
  {
    base.OnDoubleClick(e);
    SetFullScreen(!_fullScreen);
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      foreach (var font in _fonts.Values) font.Dispose();
      _fonts.Clear();
    }
    base.Dispose(disposing);
  }

  // ---- Painting -----------------------------------------------------------------------

  private Font FontOf(float pixels, FontStyle style = FontStyle.Regular)
  {
    var key = (MathF.Round(Math.Max(8, pixels)), style);
    if (!_fonts.TryGetValue(key, out var font))
      _fonts[key] = font = new Font("Segoe UI", key.Item1, style, GraphicsUnit.Pixel);
    return font;
  }

  protected override void OnPaint(PaintEventArgs e)
  {
    var g = e.Graphics;
    g.Clear(Background);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

    var area = ClientRectangle;
    if (area.Width < 50 || area.Height < 50) return;

    var margin = Math.Max(12, area.Width / 80);
    var headerHeight = Math.Clamp(area.Height / 7, 70, 180);
    var footerHeight = Math.Clamp(area.Height / 11, 44, 110);

    var header = new Rectangle(margin, margin, area.Width - 2 * margin, headerHeight);
    var footer = new Rectangle(margin, area.Bottom - margin - footerHeight, area.Width - 2 * margin, footerHeight);
    var body = Rectangle.FromLTRB(margin, header.Bottom + margin / 2, area.Right - margin, footer.Top - margin / 2);

    var board = _board;
    if (board == null)
    {
      DrawCentered(g, "Waiting for the session...", FontOf(area.Height / 14f), Muted, area);
      return;
    }

    DrawHeader(g, board, header);
    DrawBody(g, board, body);
    DrawFooter(g, board, footer);
  }

  private void DrawHeader(Graphics g, SpectatorBoard board, Rectangle r)
  {
    // Clock on the right, as large as the band allows.
    var clockFont = FontOf(r.Height * 0.62f, FontStyle.Bold);
    var clockColour = board.ClockUrgency switch { 2 => Color.FromArgb(255, 70, 70), 1 => Gold, _ => Color.White };
    var clockSize = g.MeasureString("00:00:00", clockFont);
    var clockBox = new RectangleF(r.Right - clockSize.Width, r.Top, clockSize.Width, r.Height * 0.72f);
    DrawText(g, board.Clock, clockFont, clockColour, clockBox, StringAlignment.Far);
    DrawText(g, board.ClockSub, FontOf(r.Height * 0.17f), Muted,
      new RectangleF(clockBox.Left, clockBox.Bottom, clockBox.Width, r.Height * 0.28f), StringAlignment.Far);

    var left = new RectangleF(r.Left, r.Top, clockBox.Left - r.Left - r.Height * 0.3f, r.Height);

    DrawText(g, board.Title, FontOf(r.Height * 0.36f, FontStyle.Bold), Color.White,
      new RectangleF(left.Left, left.Top, left.Width, left.Height * 0.52f), StringAlignment.Near);

    // The state as a coloured tag under the title: the flag's colours when it is out.
    if (board.State.Length == 0) return;

    var stateFont = FontOf(r.Height * 0.24f, FontStyle.Bold);
    var text = board.State.ToUpperInvariant();
    var size = g.MeasureString(text, stateFont);
    var pad = r.Height * 0.08f;
    var tag = new RectangleF(left.Left, left.Top + left.Height * 0.58f, size.Width + 2 * pad, left.Height * 0.36f);

    var (back, fore) = board.FlagOut
      ? (Color.White, Color.Black)
      : board.State is "Finished" or "Session over"
        ? (Color.FromArgb(60, 60, 70), Color.White)
        : board.State == "Starting soon"
          ? (Color.FromArgb(40, 90, 160), Color.White)
          : (Color.FromArgb(0, 140, 70), Color.White);

    using (var brush = new SolidBrush(back)) g.FillRectangle(brush, tag);
    if (board.FlagOut) DrawChequer(g, new RectangleF(tag.Right + pad, tag.Top, tag.Height * 1.4f, tag.Height));
    DrawText(g, text, stateFont, fore, tag, StringAlignment.Center);
  }

  private sealed record Column(string Header, float Weight, StringAlignment Align, Func<SpectatorRow, string> Value);

  private IReadOnlyList<Column> Columns(SpectatorBoard board)
  {
    var columns = new List<Column>
    {
      new(board.PositionHeader, 0.7f, StringAlignment.Center, r => r.Position),
      new("#", 0.8f, StringAlignment.Center, r => r.Number),
      new("Rider", 4.2f, StringAlignment.Near, r => r.Name)
    };

    if (board.ShowClass)
    {
      columns.Add(new("Class", 1.1f, StringAlignment.Near, r => r.Class));
      columns.Add(new("In class", 0.9f, StringAlignment.Center, r => r.ClassPosition));
    }

    columns.Add(new("Laps", 0.8f, StringAlignment.Center, r => r.Laps));

    if (board.Timed)
    {
      columns.Add(new("Best lap", 1.4f, StringAlignment.Far, r => r.BestLap));
      columns.Add(new("Last lap", 1.4f, StringAlignment.Far, r => r.LastLap));
    }
    else
    {
      columns.Add(new("Last lap", 1.4f, StringAlignment.Far, r => r.LastLap));
      columns.Add(new("Best lap", 1.4f, StringAlignment.Far, r => r.BestLap));
    }

    columns.Add(new(board.GapHeader, 1.4f, StringAlignment.Far, r => r.Gap));
    return columns;
  }

  private void DrawBody(Graphics g, SpectatorBoard board, Rectangle body)
  {
    var rows = board.Rows;
    if (rows.Count == 0)
    {
      DrawCentered(g, "No riders yet", FontOf(body.Height / 10f), Muted, body);
      return;
    }

    // Top 10 / Top 20 size the rows for that many, so a short field does not
    // come out in giant letters; All sizes them for everyone.
    var slots = Math.Max(rows.Count, _rowLimit ?? 0) + (board.MoreCount > 0 ? 1 : 0);
    var headerRow = Math.Clamp(body.Height / 22, 18, 40);
    var usable = body.Height - headerRow;

    var blocks = 1;
    if (usable / Math.Max(1, slots) < MinRowHeight && body.Width > 1400) blocks = 2;

    var perBlock = (int)Math.Ceiling(slots / (double)blocks);
    var rowHeight = Math.Clamp(usable / Math.Max(1, perBlock), 16, 110);
    var fits = Math.Max(1, usable / rowHeight) * blocks;

    // Still too many: cut, and say how many are not shown.
    var shown = rows.ToList();
    var more = board.MoreCount;
    if (shown.Count + (more > 0 ? 1 : 0) > fits)
    {
      var keep = Math.Max(1, fits - 1);
      more += shown.Count - keep;
      shown = shown.Take(keep).ToList();
    }

    var gap = blocks > 1 ? margin(body) : 0;
    var blockWidth = (body.Width - gap * (blocks - 1)) / blocks;
    var columns = Columns(board);
    var perColumn = (int)Math.Ceiling((shown.Count + (more > 0 ? 1 : 0)) / (double)blocks);

    for (var b = 0; b < blocks; b++)
    {
      var block = new Rectangle(body.Left + b * (blockWidth + gap), body.Top, blockWidth, body.Height);
      var edges = ColumnEdges(columns, block);

      var headerFont = FontOf(headerRow * 0.62f, FontStyle.Bold);
      for (var c = 0; c < columns.Count; c++)
        DrawText(g, columns[c].Header.ToUpperInvariant(), headerFont, Dim,
          Cell(edges, c, block.Top, headerRow), columns[c].Align);

      var y = block.Top + headerRow;
      var start = b * perColumn;
      for (var i = start; i < Math.Min(shown.Count, start + perColumn); i++)
      {
        DrawRow(g, shown[i], columns, edges, new Rectangle(block.Left, y, block.Width, rowHeight), i);
        y += rowHeight;
      }

      if (b == blocks - 1 && more > 0)
        DrawText(g, $"+ {more} more", FontOf(rowHeight * 0.42f), Muted,
          new RectangleF(block.Left, y, block.Width, rowHeight), StringAlignment.Near);
    }

    static int margin(Rectangle r) => Math.Max(16, r.Width / 60);
  }

  private void DrawRow(Graphics g, SpectatorRow row, IReadOnlyList<Column> columns, float[] edges, Rectangle r, int index)
  {
    if (index % 2 == 1)
      using (var shade = new SolidBrush(RowShade)) g.FillRectangle(shade, r);

    var font = FontOf(r.Height * 0.52f);
    var bold = FontOf(r.Height * 0.52f, FontStyle.Bold);
    var text = row.Out ? Dim : Color.White;

    for (var c = 0; c < columns.Count; c++)
    {
      var cell = Cell(edges, c, r.Top, r.Height);
      var value = columns[c].Value(row);
      var column = columns[c];

      if (c == 0 && row.Podium > 0)
      {
        // A medal-coloured badge behind the position.
        var size = r.Height * 0.8f;
        var badge = new RectangleF(cell.Left + (cell.Width - size) / 2, cell.Top + (cell.Height - size) / 2, size, size);
        using var brush = new SolidBrush(row.Podium switch { 1 => Gold, 2 => Silver, _ => Bronze });
        g.FillEllipse(brush, badge);
        DrawText(g, value, bold, Color.Black, cell, StringAlignment.Center);
        continue;
      }

      var colour = text;
      var style = font;
      if (column.Header == "Best lap" && row.FastestLap) { colour = Purple; style = bold; }
      if (column.Header == "#" || column.Header == "Pos" || column.Header == "Pick") style = bold;

      if (column.Header == "Rider" && row.Finished)
      {
        var flag = new RectangleF(cell.Left, cell.Top + r.Height * 0.28f, r.Height * 0.62f, r.Height * 0.44f);
        DrawChequer(g, flag);
        cell = RectangleF.FromLTRB(flag.Right + r.Height * 0.2f, cell.Top, cell.Right, cell.Bottom);
      }

      DrawText(g, value, style, colour, cell, column.Align);
    }

    using var rule = new Pen(Rule);
    g.DrawLine(rule, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
  }

  private void DrawFooter(Graphics g, SpectatorBoard board, Rectangle r)
  {
    using (var rule = new Pen(Rule, 2)) g.DrawLine(rule, r.Left, r.Top, r.Right, r.Top);

    var labelFont = FontOf(r.Height * 0.24f, FontStyle.Bold);
    var valueFont = FontOf(r.Height * 0.34f);
    var inner = Rectangle.FromLTRB(r.Left, r.Top + r.Height / 8, r.Right, r.Bottom);

    // Fastest lap on the left, a third of the width; the last crossings after it.
    var fastestBox = new RectangleF(inner.Left, inner.Top, inner.Width * 0.34f, inner.Height);
    DrawText(g, "FASTEST LAP", labelFont, Purple,
      new RectangleF(fastestBox.Left, fastestBox.Top, fastestBox.Width, fastestBox.Height * 0.4f), StringAlignment.Near);
    DrawText(g, board.FastestLap ?? "-", valueFont, Color.White,
      new RectangleF(fastestBox.Left, fastestBox.Top + fastestBox.Height * 0.4f, fastestBox.Width, fastestBox.Height * 0.6f),
      StringAlignment.Near);

    var recentBox = RectangleF.FromLTRB(fastestBox.Right + r.Height * 0.4f, inner.Top, inner.Right, inner.Bottom);
    DrawText(g, "LAST ACROSS THE LINE", labelFont, Dim,
      new RectangleF(recentBox.Left, recentBox.Top, recentBox.Width, recentBox.Height * 0.4f), StringAlignment.Near);

    // As many crossings as fit whole, newest first: a name cut in half reads as a different rider.
    var valueBox = new RectangleF(recentBox.Left, recentBox.Top + recentBox.Height * 0.4f, recentBox.Width,
      recentBox.Height * 0.6f);
    var spacing = g.MeasureString("     ", valueFont).Width;
    var x = valueBox.Left;
    foreach (var crossing in board.Recent)
    {
      var text = $"#{crossing.Number} {crossing.Name.Split(' ').LastOrDefault() ?? ""}  {crossing.LapTime}".Trim();
      var width = g.MeasureString(text, valueFont).Width;
      if (x + width > valueBox.Right) break;
      DrawText(g, text, valueFont, x == valueBox.Left ? Color.White : Muted,
        new RectangleF(x, valueBox.Top, width + 4, valueBox.Height), StringAlignment.Near);
      x += width + spacing;
    }

    if (board.Recent.Count == 0)
      DrawText(g, "-", valueFont, Color.White, valueBox, StringAlignment.Near);
  }

  // ---- Helpers ------------------------------------------------------------------------

  private static float[] ColumnEdges(IReadOnlyList<Column> columns, Rectangle block)
  {
    var total = columns.Sum(c => c.Weight);
    var edges = new float[columns.Count + 1];
    edges[0] = block.Left;
    for (var i = 0; i < columns.Count; i++)
      edges[i + 1] = edges[i] + block.Width * columns[i].Weight / total;
    return edges;
  }

  private static RectangleF Cell(float[] edges, int column, float top, float height)
  {
    var pad = height * 0.18f;
    return new RectangleF(edges[column] + pad, top, edges[column + 1] - edges[column] - 2 * pad, height);
  }

  private static void DrawText(Graphics g, string text, Font font, Color colour, RectangleF box, StringAlignment align)
  {
    using var format = new StringFormat(StringFormatFlags.NoWrap)
    {
      Alignment = align,
      LineAlignment = StringAlignment.Center,
      Trimming = StringTrimming.EllipsisCharacter
    };
    using var brush = new SolidBrush(colour);
    g.DrawString(text, font, brush, box, format);
  }

  private static void DrawCentered(Graphics g, string text, Font font, Color colour, Rectangle box) =>
    DrawText(g, text, font, colour, box, StringAlignment.Center);

  /// <summary>A small chequered flag: four by three squares.</summary>
  private static void DrawChequer(Graphics g, RectangleF r)
  {
    const int cols = 4, rows = 3;
    var w = r.Width / cols;
    var h = r.Height / rows;
    g.FillRectangle(Brushes.White, r);
    for (var y = 0; y < rows; y++)
      for (var x = 0; x < cols; x++)
        if ((x + y) % 2 == 0)
          g.FillRectangle(Brushes.Black, r.Left + x * w, r.Top + y * h, w, h);
    g.DrawRectangle(Pens.Gray, r.Left, r.Top, r.Width, r.Height);
  }
}
