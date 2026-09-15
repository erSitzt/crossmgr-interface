using System.Text.Json.Serialization;

namespace CrossMgrInterface;

/// <summary>
/// A picture of the circuit laid over the map while the loop is traced - usually
/// a screenshot of an online map with the track drawn on it, or the club's plan.
///
/// Placed by where its centre is, how big it is and which way round: a move, a
/// uniform resize and a rotation, and nothing else. A north-up map screenshot is
/// itself Web Mercator, so lining it up with this map is exactly that transform
/// at any latitude. A skew or perspective term would only give the operator more
/// ways to get it wrong.
///
/// <see cref="Scale"/> is in Mercator world pixels rather than metres for the same
/// reason. Ground metres per pixel change with latitude, so a picture placed by
/// metres would quietly grow or shrink as it was dragged north or south; in world
/// pixels it is one number at every latitude and every zoom.
///
/// The bytes travel inside the circuit's own record. A .cmtrack file then carries
/// the picture with no extra code, undo snapshots share one array, and there is no
/// separate file to go missing. Imports are shrunk first - see
/// <see cref="ReferenceImageLayer.PrepareForStorage(byte[])"/> - so tracks.json
/// stays a sensible size.
/// </summary>
public sealed class TrackReferenceImage
{
  /// <summary>Smallest a resize may make the picture on screen. Below this its handles sit on top of each other.</summary>
  public const double MinScreenPixels = 24;

  /// <summary>Sanity bounds on how much ground the picture's longest side covers.</summary>
  public const double MinGroundMetres = 5;
  public const double MaxGroundMetres = 2_000_000;

  /// <summary>
  /// How far apart the two landmarks of a match must be. Closer than this and a
  /// pixel of clicking error turns into degrees of rotation.
  /// </summary>
  public const double MinMatchImagePixels = 30;
  public const double MinMatchGroundMetres = 5;

  /// <summary>World-pixel zoom the placement maths run at. Any zoom gives the same answer; the deepest keeps the numbers large.</summary>
  private const int WorkZoom = TileMath.MaxZoom;

  /// <summary>
  /// The stored picture. Never modified in place, only replaced - which is what
  /// lets <see cref="Clone"/> and every undo snapshot share it.
  /// </summary>
  public byte[] ImageData { get; set; } = Array.Empty<byte>();

  /// <summary>The file it came from, for the editor to show. Nothing depends on it.</summary>
  public string? SourceName { get; set; }

  public int PixelWidth { get; set; }
  public int PixelHeight { get; set; }

  /// <summary>Where the centre of the picture sits on the ground.</summary>
  public LatLon Center { get; set; }

  /// <summary>Web Mercator world pixels at zoom 0 per picture pixel.</summary>
  public double Scale { get; set; }

  /// <summary>Clockwise, as seen on screen.</summary>
  public double RotationDegrees { get; set; }

  /// <summary>
  /// Set once the picture is lined up, so a stray drag while tracing cannot knock
  /// it out of place. The editor will not move, match, replace or remove a locked
  /// picture. Saved with the circuit, so it is still locked the next time.
  /// </summary>
  public bool Locked { get; set; }

  [JsonIgnore] public double MetresPerPixel => Scale * TileMath.MetresPerPixel(Center.Lat, 0);

  [JsonIgnore] public double GroundMetres => Math.Max(PixelWidth, PixelHeight) * MetresPerPixel;

  /// <summary>The ground the picture covers, as a box round its (possibly rotated) corners.</summary>
  [JsonIgnore] public GeoBounds Bounds => GeoBounds.FromPoints(Corners.Select(ImageToLatLon));

  /// <summary>Top-left, top-right, bottom-right, bottom-left, in picture pixels.</summary>
  private PointD[] Corners => new[]
  {
    new PointD(0, 0), new PointD(PixelWidth, 0),
    new PointD(PixelWidth, PixelHeight), new PointD(0, PixelHeight)
  };

  // ---- Where things are ----------------------------------------------------

  /// <summary>Screen pixels per picture pixel at this viewport's zoom.</summary>
  public double ScreenScale(MapViewport viewport) => Scale * (1L << viewport.Zoom);

  public PointD ImageToScreen(MapViewport viewport, PointD image)
  {
    var centre = viewport.ToScreenD(Center);
    var offset = Turn(image.X - PixelWidth / 2.0, image.Y - PixelHeight / 2.0, ScreenScale(viewport));
    return new PointD(centre.X + offset.X, centre.Y + offset.Y);
  }

  public PointD ScreenToImage(MapViewport viewport, PointD screen)
  {
    var centre = viewport.ToScreenD(Center);
    var k = ScreenScale(viewport);
    var (cos, sin) = CosSin();

    var sx = screen.X - centre.X;
    var sy = screen.Y - centre.Y;

    return new PointD(
      PixelWidth / 2.0 + (cos * sx + sin * sy) / k,
      PixelHeight / 2.0 + (cos * sy - sin * sx) / k);
  }

  /// <summary>
  /// Whether a screen point is on the picture. Not a rectangle test: a rotated
  /// picture's bounding box is mostly not picture.
  /// </summary>
  public bool Contains(MapViewport viewport, Point screen)
  {
    var p = ScreenToImage(viewport, new PointD(screen.X, screen.Y));
    return p.X >= 0 && p.X <= PixelWidth && p.Y >= 0 && p.Y <= PixelHeight;
  }

  /// <summary>Top-left, top-right, bottom-right, bottom-left, in screen pixels.</summary>
  public PointD[] ScreenCorners(MapViewport viewport) =>
    Corners.Select(c => ImageToScreen(viewport, c)).ToArray();

  public LatLon ImageToLatLon(PointD image)
  {
    var centre = TileMath.ToWorldPixel(Center, WorkZoom);
    var offset = Turn(image.X - PixelWidth / 2.0, image.Y - PixelHeight / 2.0, Scale * (1L << WorkZoom));
    return TileMath.FromWorldPixel(new PointD(centre.X + offset.X, centre.Y + offset.Y), WorkZoom);
  }

  // ---- Placing it ----------------------------------------------------------

  /// <summary>
  /// A freshly imported picture: unrotated, centred, filling most of the view. The
  /// operator is looking at the venue, so that is where it belongs to start with.
  /// </summary>
  public static TrackReferenceImage FitToView(
    MapViewport viewport, byte[] data, int width, int height, string? sourceName)
  {
    var screenScale = Math.Min(
      viewport.ViewSize.Width * 0.8 / width,
      viewport.ViewSize.Height * 0.8 / height);

    var image = new TrackReferenceImage
    {
      ImageData = data,
      SourceName = sourceName,
      PixelWidth = width,
      PixelHeight = height,
      Center = viewport.Center,
      Scale = screenScale / (1L << viewport.Zoom)
    };

    image.Scale = image.ClampScale(image.Scale, null);
    return image;
  }

  /// <summary>
  /// Dragged by a screen offset. Always called on the placement from the START of
  /// the drag with the whole offset so far - never step by step - for the same
  /// reason the map pans that way: rounding accumulates into visible drift.
  /// </summary>
  public TrackReferenceImage MovedBy(MapViewport viewport, double dx, double dy)
  {
    var centre = viewport.ToScreenD(Center);
    return With(viewport.ToLatLon(new PointD(centre.X + dx, centre.Y + dy)), Scale, RotationDegrees);
  }

  /// <summary>Resized about its centre by how much further from the centre the pointer now is. Start-of-drag placement, as for <see cref="MovedBy"/>.</summary>
  public TrackReferenceImage ScaledAboutCentre(MapViewport viewport, PointD from, PointD to)
  {
    var centre = viewport.ToScreenD(Center);
    var before = Distance(centre, from);
    if (before < 1) return With(Center, Scale, RotationDegrees);

    return With(Center, ClampScale(Scale * Distance(centre, to) / before, viewport), RotationDegrees);
  }

  /// <summary>Turned about its centre by the angle the pointer has swept round it. Start-of-drag placement, as for <see cref="MovedBy"/>.</summary>
  public TrackReferenceImage RotatedAboutCentre(MapViewport viewport, PointD from, PointD to)
  {
    var centre = viewport.ToScreenD(Center);
    if (Distance(centre, from) < 1 || Distance(centre, to) < 1) return With(Center, Scale, RotationDegrees);

    var swept = Math.Atan2(to.Y - centre.Y, to.X - centre.X) - Math.Atan2(from.Y - centre.Y, from.X - centre.X);
    return With(Center, Scale, NormaliseDegrees(RotationDegrees + swept * 180 / Math.PI));
  }

  /// <summary>
  /// Lines the picture up from two landmarks: where each one is on the picture and
  /// where it really is on the ground.
  ///
  /// Two pairs of points fix a move, a resize and a rotation exactly, so this is
  /// the whole answer rather than a least-squares estimate. It works in world
  /// pixels, where a map screenshot is exactly such a transform. Null when the
  /// landmarks are too close together to trust.
  /// </summary>
  public TrackReferenceImage? FitTwoPoints(PointD imageA, LatLon groundA, PointD imageB, LatLon groundB)
  {
    var imageDx = imageB.X - imageA.X;
    var imageDy = imageB.Y - imageA.Y;
    var imageDistance = Math.Sqrt(imageDx * imageDx + imageDy * imageDy);

    if (imageDistance < MinMatchImagePixels) return null;
    if (GeoMath.DistanceMetres(groundA, groundB) < MinMatchGroundMetres) return null;

    var worldA = TileMath.ToWorldPixel(groundA, WorkZoom);
    var worldB = TileMath.ToWorldPixel(groundB, WorkZoom);
    var worldDx = worldB.X - worldA.X;
    var worldDy = worldB.Y - worldA.Y;

    var scale = Math.Sqrt(worldDx * worldDx + worldDy * worldDy) / imageDistance;
    var rotation = Math.Atan2(worldDy, worldDx) - Math.Atan2(imageDy, imageDx);

    // Landmark A is known on both; the centre is a fixed picture offset from it.
    var cos = Math.Cos(rotation);
    var sin = Math.Sin(rotation);
    var ox = PixelWidth / 2.0 - imageA.X;
    var oy = PixelHeight / 2.0 - imageA.Y;

    var centre = new PointD(
      worldA.X + scale * (cos * ox - sin * oy),
      worldA.Y + scale * (sin * ox + cos * oy));

    var fitted = With(
      TileMath.FromWorldPixel(centre, WorkZoom),
      scale / (1L << WorkZoom),
      NormaliseDegrees(rotation * 180 / Math.PI));

    return fitted.IsValid() ? fitted : null;
  }

  // ---- Housekeeping --------------------------------------------------------

  /// <summary>
  /// Whether this placement can be drawn and saved. A NaN anywhere breaks both:
  /// GDI+ throws from Paint, and System.Text.Json refuses to write the file at all.
  /// Checked on everything that arrives from disk or another club's file.
  /// </summary>
  public bool IsValid() =>
    ImageData is { Length: > 0 } &&
    PixelWidth > 0 && PixelHeight > 0 &&
    double.IsFinite(Center.Lat) && double.IsFinite(Center.Lon) &&
    Math.Abs(Center.Lat) <= TileMath.MaxLatitude && Math.Abs(Center.Lon) <= 180 &&
    double.IsFinite(Scale) && Scale > 0 &&
    double.IsFinite(RotationDegrees) &&
    GroundMetres >= MinGroundMetres * 0.999 && GroundMetres <= MaxGroundMetres * 1.001;

  /// <summary>Copies the placement and shares the bytes, which are never modified in place.</summary>
  public TrackReferenceImage Clone() => (TrackReferenceImage)MemberwiseClone();

  private TrackReferenceImage With(LatLon center, double scale, double rotationDegrees) => new()
  {
    ImageData = ImageData,
    SourceName = SourceName,
    PixelWidth = PixelWidth,
    PixelHeight = PixelHeight,
    Center = new LatLon(
      Math.Clamp(center.Lat, -TileMath.MaxLatitude, TileMath.MaxLatitude),
      Math.Clamp(center.Lon, -180.0, 180.0)),
    Scale = scale,
    RotationDegrees = rotationDegrees,
    Locked = Locked
  };

  /// <summary>
  /// Keeps a resize sensible: never so small on screen that the handles overlap
  /// (dragging a corner onto the centre would otherwise scale to zero, and every
  /// later paint would divide by it), and never an absurd amount of ground.
  /// </summary>
  private double ClampScale(double scale, MapViewport? viewport)
  {
    var longest = Math.Max(PixelWidth, PixelHeight);
    var metresPerWorldPixel = TileMath.MetresPerPixel(Center.Lat, 0);

    var min = MinGroundMetres / (longest * metresPerWorldPixel);
    var max = MaxGroundMetres / (longest * metresPerWorldPixel);

    if (viewport is { } view)
      min = Math.Max(min, MinScreenPixels / (longest * (double)(1L << view.Zoom)));

    if (!double.IsFinite(scale)) return Scale;
    return Math.Clamp(scale, Math.Min(min, max), max);
  }

  /// <summary>A picture-space offset turned clockwise by the rotation and scaled into screen or world pixels.</summary>
  private PointD Turn(double dx, double dy, double scale)
  {
    var (cos, sin) = CosSin();
    return new PointD((cos * dx - sin * dy) * scale, (sin * dx + cos * dy) * scale);
  }

  private (double Cos, double Sin) CosSin()
  {
    var theta = RotationDegrees * Math.PI / 180;
    return (Math.Cos(theta), Math.Sin(theta));
  }

  private static double Distance(PointD a, PointD b) =>
    Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

  /// <summary>Into (-180, 180], so a picture turned round twice does not read 720 degrees.</summary>
  private static double NormaliseDegrees(double degrees)
  {
    var d = Math.IEEERemainder(degrees, 360);
    return d <= -180 ? d + 360 : d;
  }
}
