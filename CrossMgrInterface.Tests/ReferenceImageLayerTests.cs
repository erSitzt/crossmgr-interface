using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Xunit;

namespace CrossMgrInterface.Tests;

public class ReferenceImageLayerTests
{
  private static readonly Size Canvas = new(400, 300);

  private static byte[] Png(Bitmap bitmap)
  {
    using var stream = new MemoryStream();
    bitmap.Save(stream, ImageFormat.Png);
    return stream.ToArray();
  }

  private static byte[] SolidPng(int width, int height, Color colour)
  {
    using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bitmap)) g.Clear(colour);
    return Png(bitmap);
  }

  /// <summary>A placement where one picture pixel is one screen pixel, centred on the canvas.</summary>
  private static (MapViewport View, TrackReferenceImage Placement) Centred(
    byte[] data, int width, int height, double rotation, double screenPixelsPerPicturePixel = 1)
  {
    var view = new MapViewport(new LatLon(50, 8), 17, Canvas);

    var placement = new TrackReferenceImage
    {
      ImageData = data,
      PixelWidth = width,
      PixelHeight = height,
      Center = view.ToLatLon(new PointD(Canvas.Width / 2.0, Canvas.Height / 2.0)),
      Scale = screenPixelsPerPicturePixel / (1L << 17),
      RotationDegrees = rotation
    };

    return (view, placement);
  }

  private static Bitmap WhiteCanvas()
  {
    var bitmap = new Bitmap(Canvas.Width, Canvas.Height, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bitmap);
    g.Clear(Color.White);
    return bitmap;
  }

  [Fact]
  public void AHalfTransparentPictureBlendsWithTheMapRatherThanReplacingIt()
  {
    var data = SolidPng(200, 100, Color.Red);
    using var layer = new ReferenceImageLayer(data);
    var (view, placement) = Centred(data, 200, 100, rotation: 30);

    using var target = WhiteCanvas();
    using (var g = Graphics.FromImage(target))
    {
      // What the tile pass leaves behind. Under it a see-through picture would
      // overwrite the map with half-transparent pixels instead of blending.
      g.CompositingMode = CompositingMode.SourceCopy;

      Assert.True(layer.Draw(g, view, placement, 0.5f, new Rectangle(Point.Empty, Canvas), fast: false));
      Assert.Equal(CompositingMode.SourceCopy, g.CompositingMode);
    }

    var centre = target.GetPixel(200, 150);
    Assert.Equal(255, centre.A);
    Assert.InRange(centre.R, 245, 255);
    Assert.InRange(centre.G, 110, 145);
    Assert.InRange(centre.B, 110, 145);

    // 80px along the picture's own x axis, turned 30 degrees clockwise: still on it.
    var along = target.GetPixel(
      200 + (int)Math.Round(80 * Math.Cos(Math.PI / 6)),
      150 + (int)Math.Round(80 * Math.Sin(Math.PI / 6)));
    Assert.InRange(along.G, 110, 145);

    // 90px straight up: inside the unturned picture's box, outside the turned picture.
    Assert.Equal(Color.White.ToArgb(), target.GetPixel(200, 60).ToArgb());
    Assert.Equal(Color.White.ToArgb(), target.GetPixel(5, 5).ToArgb());
  }

  [Fact]
  public void APictureWhollyOffScreenDrawsNothing()
  {
    var data = SolidPng(200, 100, Color.Red);
    using var layer = new ReferenceImageLayer(data);
    var (view, placement) = Centred(data, 200, 100, rotation: 0);
    placement.Center = view.ToLatLon(new PointD(5000, 150));

    using var target = WhiteCanvas();
    using (var g = Graphics.FromImage(target))
      Assert.False(layer.Draw(g, view, placement, 1f, new Rectangle(Point.Empty, Canvas), fast: false));

    Assert.Equal(Color.White.ToArgb(), target.GetPixel(399, 150).ToArgb());
  }

  [Fact]
  public void AHugelyMagnifiedPictureStillDraws()
  {
    // At zoom 19 a picture pixel can be hundreds of screen pixels. Drawing the
    // whole picture then means coordinates GDI+ refuses; only the visible part
    // should be drawn.
    var data = SolidPng(400, 300, Color.Blue);
    using var layer = new ReferenceImageLayer(data);
    var (view, placement) = Centred(data, 400, 300, rotation: 20, screenPixelsPerPicturePixel: 5000);

    using var target = WhiteCanvas();
    using (var g = Graphics.FromImage(target))
      Assert.True(layer.Draw(g, view, placement, 1f, new Rectangle(Point.Empty, Canvas), fast: true));

    var centre = target.GetPixel(200, 150);
    Assert.InRange(centre.B, 245, 255);
    Assert.InRange(centre.R, 0, 10);
  }

  [Fact]
  public void AShrunkPictureIsDrawnFromASmallerCopyAndLooksTheSame()
  {
    var data = SolidPng(2400, 1600, Color.Green);
    using var layer = new ReferenceImageLayer(data);
    var (view, placement) = Centred(data, 2400, 1600, rotation: 0, screenPixelsPerPicturePixel: 0.1);

    using var target = WhiteCanvas();
    using (var g = Graphics.FromImage(target))
      Assert.True(layer.Draw(g, view, placement, 1f, new Rectangle(Point.Empty, Canvas), fast: false));

    var centre = target.GetPixel(200, 150);
    Assert.InRange(centre.G, 118, 138);
    Assert.InRange(centre.R, 0, 10);
    Assert.Equal(Color.White.ToArgb(), target.GetPixel(200, 20).ToArgb());
  }

  // ---- Preparing for storage ---------------------------------------------------

  [Fact]
  public void ASmallPictureIsKeptExactlyAsItCame()
  {
    var data = SolidPng(300, 200, Color.Green);

    var prepared = ReferenceImageLayer.PrepareForStorage(data);

    Assert.Same(data, prepared.Data);
    Assert.Equal(300, prepared.Width);
    Assert.Equal(200, prepared.Height);
  }

  [Fact]
  public void ABigPictureIsShrunkForStorage()
  {
    var prepared = ReferenceImageLayer.PrepareForStorage(SolidPng(5000, 3000, Color.SteelBlue));

    Assert.Equal(3072, prepared.Width);
    Assert.Equal(1843, prepared.Height);

    using var layer = new ReferenceImageLayer(prepared.Data);
    Assert.Equal(3072, layer.Width);
    Assert.Equal(1843, layer.Height);
  }

  [Fact]
  public void APictureThatWillNotCompressAsPngIsStoredAsJpeg()
  {
    // Noise is the worst case - a busy satellite view is not far off it.
    using var noise = new Bitmap(2000, 2000, PixelFormat.Format32bppArgb);
    var bits = noise.LockBits(new Rectangle(0, 0, noise.Width, noise.Height), ImageLockMode.WriteOnly, noise.PixelFormat);
    try
    {
      var bytes = new byte[Math.Abs(bits.Stride) * bits.Height];
      new Random(3).NextBytes(bytes);
      for (var i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
      Marshal.Copy(bytes, 0, bits.Scan0, bytes.Length);
    }
    finally
    {
      noise.UnlockBits(bits);
    }

    var prepared = ReferenceImageLayer.PrepareForStorage(Png(noise));

    Assert.Equal(0xFF, prepared.Data[0]);
    Assert.Equal(0xD8, prepared.Data[1]);
    Assert.Equal(2000, prepared.Width);
    Assert.True(prepared.Data.Length < 16 * 1024 * 1024);
  }

  [Fact]
  public void SomethingThatIsNotAPictureIsRefused()
  {
    Assert.ThrowsAny<Exception>(() => ReferenceImageLayer.PrepareForStorage(new byte[] { 1, 2, 3, 4 }));
    Assert.ThrowsAny<Exception>(() => new ReferenceImageLayer(new byte[] { 1, 2, 3, 4 }));
  }
}
