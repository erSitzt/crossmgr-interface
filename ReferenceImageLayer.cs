using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CrossMgrInterface;

/// <summary>A picture ready to be stored with a circuit, and its size in pixels.</summary>
public readonly record struct PreparedImage(byte[] Data, int Width, int Height);

/// <summary>
/// Draws a circuit's reference image under the loop being traced.
///
/// Owned by the circuit editor rather than the renderer: the editor decides which
/// picture is current - undo can bring back one that was removed - and it disposes
/// the bitmaps when it closes.
///
/// It is redrawn on every mouse move while the picture is dragged, and three
/// things keep that affordable:
///
///   - Only the part of the picture that is on screen is drawn. That is also what
///     keeps the destination coordinates finite at zoom 19, where a two-kilometre
///     picture is tens of thousands of pixels across.
///   - It is drawn from a copy halved as often as its on-screen size allows, so
///     shrinking a 3000-pixel picture into 400 does not filter nine million pixels
///     a frame.
///   - Opacity is a colour matrix over that small cropped source, rather than a
///     faded copy of the whole picture rebuilt on every tick of the slider.
/// </summary>
public sealed class ReferenceImageLayer : IDisposable
{
  /// <summary>Longest side kept. Plenty to trace a circuit from, and it keeps tracks.json in megabytes rather than tens of them.</summary>
  public const int MaxStoredSide = 3072;

  public const int MaxStoredBytes = 3 * 1024 * 1024;

  private const int SmallestLevelSide = 256;

  private static readonly Guid[] StoredFormats =
  {
    ImageFormat.Png.Guid, ImageFormat.Jpeg.Guid, ImageFormat.Gif.Guid, ImageFormat.Bmp.Guid
  };

  /// <summary>Level 0 is the picture itself; each level after it is half the size of the one before.</summary>
  private readonly List<Bitmap> _levels = new();

  private bool _disposed;

  /// <summary>The stored bytes this was decoded from - how the editor finds the right layer for a placement after an undo.</summary>
  public byte[] Data { get; }

  public int Width { get; }
  public int Height { get; }

  /// <summary>Throws if the bytes are not a picture GDI+ can read.</summary>
  public ReferenceImageLayer(byte[] data)
  {
    Data = data;

    // TileStore's decoder: bytes, not a file, so nothing is locked, and a
    // premultiplied copy that blits without a per-frame format conversion.
    var full = TileStore.Decode(data);
    Width = full.Width;
    Height = full.Height;
    _levels.Add(full);

    try
    {
      var level = full;
      while (Math.Max(level.Width, level.Height) / 2 >= SmallestLevelSide)
      {
        level = Resample(level, Math.Max(1, level.Width / 2), Math.Max(1, level.Height / 2),
          PixelFormat.Format32bppPArgb, InterpolationMode.HighQualityBilinear);
        _levels.Add(level);
      }
    }
    catch
    {
      Dispose();
      throw;
    }
  }

  /// <summary>
  /// Draws the picture where <paramref name="placement"/> puts it. False when none
  /// of it is on screen.
  ///
  /// Sets its own compositing and pixel offset and puts them back afterwards. The
  /// tile pass leaves SourceCopy behind, under which a see-through picture would
  /// replace the map instead of blending with it.
  /// </summary>
  public bool Draw(Graphics g, MapViewport viewport, TrackReferenceImage placement, float opacity,
    Rectangle bounds, bool fast)
  {
    if (_disposed || opacity <= 0.005f) return false;

    var scale = placement.ScreenScale(viewport);
    if (!double.IsFinite(scale) || scale <= 0) return false;

    // The part of the picture under the screen, in the picture's own pixels. The
    // mapping is affine, so the box round the four corners' images covers it.
    double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;

    foreach (var corner in new[]
             {
               new PointD(bounds.Left, bounds.Top), new PointD(bounds.Right, bounds.Top),
               new PointD(bounds.Right, bounds.Bottom), new PointD(bounds.Left, bounds.Bottom)
             })
    {
      var p = placement.ScreenToImage(viewport, corner);
      left = Math.Min(left, p.X);
      top = Math.Min(top, p.Y);
      right = Math.Max(right, p.X);
      bottom = Math.Max(bottom, p.Y);
    }

    // A pixel of slack so the filter has neighbours to read at the screen edge.
    left = Math.Max(0, Math.Floor(left) - 1);
    top = Math.Max(0, Math.Floor(top) - 1);
    right = Math.Min(Width, Math.Ceiling(right) + 1);
    bottom = Math.Min(Height, Math.Ceiling(bottom) + 1);

    if (!(right - left >= 1 && bottom - top >= 1)) return false;

    var topLeft = placement.ImageToScreen(viewport, new PointD(left, top));
    var topRight = placement.ImageToScreen(viewport, new PointD(right, top));
    var bottomLeft = placement.ImageToScreen(viewport, new PointD(left, bottom));

    if (!Drawable(topLeft) || !Drawable(topRight) || !Drawable(bottomLeft)) return false;

    // The smallest level that still has at least one pixel per screen pixel.
    var level = _levels[0];
    for (var i = 1; i < _levels.Count; i++)
    {
      if ((double)_levels[i].Width / Width < scale) break;
      level = _levels[i];
    }

    // Per axis: halving an odd size is not exactly half.
    var fx = (double)level.Width / Width;
    var fy = (double)level.Height / Height;

    var source = new RectangleF(
      (float)(left * fx), (float)(top * fy),
      (float)((right - left) * fx), (float)((bottom - top) * fy));

    var destination = new[]
    {
      new PointF((float)topLeft.X, (float)topLeft.Y),
      new PointF((float)topRight.X, (float)topRight.Y),
      new PointF((float)bottomLeft.X, (float)bottomLeft.Y)
    };

    var state = g.Save();
    try
    {
      g.CompositingMode = CompositingMode.SourceOver;
      g.InterpolationMode = fast ? InterpolationMode.Bilinear : InterpolationMode.HighQualityBilinear;

      // Pixel edges, not pixel centres, to match the placement maths: otherwise
      // the picture sits half a picture pixel off where the landmark was clicked.
      g.PixelOffsetMode = PixelOffsetMode.Half;

      using var attributes = new ImageAttributes();

      // Without it the filter reads transparent pixels beyond the source edge and
      // draws a faint fringe round the picture and along the crop.
      attributes.SetWrapMode(WrapMode.TileFlipXY);

      if (opacity < 0.995f) attributes.SetColorMatrix(new ColorMatrix { Matrix33 = opacity });

      g.DrawImage(level, destination, source, GraphicsUnit.Pixel, attributes);
    }
    finally
    {
      g.Restore(state);
    }

    return true;
  }

  /// <summary>GDI+ throws on coordinates past a few million; nothing that large is on screen anyway.</summary>
  private static bool Drawable(PointD p) =>
    double.IsFinite(p.X) && double.IsFinite(p.Y) && Math.Abs(p.X) < 1e6 && Math.Abs(p.Y) < 1e6;

  // ---- Preparing a picture for storage -------------------------------------

  /// <summary>
  /// The bytes to keep for an imported file. A picture that is already small
  /// enough, in a format worth keeping, is kept exactly as it came; anything else
  /// is shrunk to <see cref="MaxStoredSide"/> and re-encoded.
  ///
  /// Throws if the bytes are not a picture.
  /// </summary>
  public static PreparedImage PrepareForStorage(byte[] original)
  {
    using var stream = new MemoryStream(original, writable: false);
    using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);

    var keepAsItIs = original.Length <= MaxStoredBytes &&
                     Math.Max(source.Width, source.Height) <= MaxStoredSide &&
                     StoredFormats.Contains(source.RawFormat.Guid);

    return keepAsItIs ? new PreparedImage(original, source.Width, source.Height) : Encode(source);
  }

  /// <summary>The bytes to keep for a picture that only exists in memory, such as one pasted from the clipboard.</summary>
  public static PreparedImage PrepareForStorage(Image picture) => Encode(picture);

  /// <summary>
  /// A copy with the alpha channel thrown away. For the plain clipboard bitmap,
  /// whose alpha is often garbage - frequently all zero - which would otherwise
  /// paste as a completely transparent picture.
  /// </summary>
  public static Bitmap Opaque(Image picture)
  {
    using var copy = new Bitmap(picture);
    return copy.Clone(new Rectangle(0, 0, copy.Width, copy.Height), PixelFormat.Format24bppRgb);
  }

  /// <summary>PNG when that is small enough - lossless keeps thin drawn track lines crisp - otherwise JPEG.</summary>
  private static PreparedImage Encode(Image source)
  {
    var shrink = Math.Min(1.0, (double)MaxStoredSide / Math.Max(source.Width, source.Height));
    var width = Math.Max(1, (int)Math.Round(source.Width * shrink));
    var height = Math.Max(1, (int)Math.Round(source.Height * shrink));

    using var resized = Resample(source, width, height, PixelFormat.Format32bppArgb, InterpolationMode.HighQualityBicubic);

    var png = Save(resized, ImageFormat.Png);
    if (png.Length <= MaxStoredBytes) return new PreparedImage(png, width, height);

    // A photograph or a busy satellite view does not compress as PNG. JPEG has
    // no transparency, so flatten onto white first rather than onto black.
    using var flat = new Bitmap(width, height, PixelFormat.Format24bppRgb);
    using (var g = Graphics.FromImage(flat))
    {
      g.Clear(Color.White);
      g.DrawImage(resized, new Rectangle(0, 0, width, height));
    }

    return new PreparedImage(SaveJpeg(flat, 90), width, height);
  }

  private static Bitmap Resample(Image source, int width, int height, PixelFormat format, InterpolationMode interpolation)
  {
    var target = new Bitmap(width, height, format);

    using var g = Graphics.FromImage(target);
    g.CompositingMode = CompositingMode.SourceCopy;
    g.InterpolationMode = interpolation;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

    using var attributes = new ImageAttributes();
    attributes.SetWrapMode(WrapMode.TileFlipXY);

    g.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height,
      GraphicsUnit.Pixel, attributes);

    return target;
  }

  private static byte[] Save(Image image, ImageFormat format)
  {
    using var stream = new MemoryStream();
    image.Save(stream, format);
    return stream.ToArray();
  }

  private static byte[] SaveJpeg(Image image, long quality)
  {
    var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

    using var parameters = new EncoderParameters(1);
    parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);

    using var stream = new MemoryStream();
    image.Save(stream, codec, parameters);
    return stream.ToArray();
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;

    foreach (var level in _levels) level.Dispose();
    _levels.Clear();
  }
}
