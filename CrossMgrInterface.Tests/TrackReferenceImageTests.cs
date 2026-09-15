using Xunit;

namespace CrossMgrInterface.Tests;

public class TrackReferenceImageTests
{
  private static readonly LatLon Venue = new(50.1, 8.2);

  /// <summary>An 800x600 picture at the venue, half a metre a pixel unless told otherwise.</summary>
  private static TrackReferenceImage Picture(double rotation = 0, double metresPerPixel = 0.5, LatLon? center = null)
  {
    var at = center ?? Venue;

    return new TrackReferenceImage
    {
      ImageData = new byte[] { 1, 2, 3 },
      SourceName = "plan.png",
      PixelWidth = 800,
      PixelHeight = 600,
      Center = at,
      Scale = metresPerPixel / TileMath.MetresPerPixel(at.Lat, 0),
      RotationDegrees = rotation
    };
  }

  private static MapViewport View(int zoom, LatLon? center = null) => new(center ?? Venue, zoom, new Size(1200, 900));

  private static double Pixels(PointD a, PointD b) =>
    Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

  private static double AngleBetween(double a, double b) => Math.Abs(Math.IEEERemainder(a - b, 360));

  // ---- Where it is on screen -------------------------------------------------

  [Fact]
  public void UnrotatedItIsCentredOnItsCentreAndAsWideAsItsScaleSays()
  {
    var picture = Picture();
    var view = View(17);
    var corners = picture.ScreenCorners(view);

    Assert.Equal(800 * picture.Scale * (1L << 17), Pixels(corners[0], corners[1]), 6);

    // Half a metre a pixel across 800 pixels is 400m of ground at any zoom.
    Assert.Equal(400 / view.MetresPerPixel, Pixels(corners[0], corners[1]), 3);

    var centre = view.ToScreenD(picture.Center);
    Assert.Equal(centre.X, (corners[0].X + corners[2].X) / 2, 6);
    Assert.Equal(centre.Y, (corners[0].Y + corners[2].Y) / 2, 6);

    Assert.Equal(corners[0].Y, corners[1].Y, 6);
  }

  [Theory]
  [InlineData(15)]
  [InlineData(19)]
  public void ScreenAndPictureCoordinatesRoundTrip(int zoom)
  {
    var picture = Picture(rotation: 37);
    var view = View(zoom);

    foreach (var p in new[] { new PointD(0, 0), new PointD(800, 600), new PointD(123.5, 456.25), new PointD(400, 300) })
    {
      var back = picture.ScreenToImage(view, picture.ImageToScreen(view, p));
      Assert.True(Pixels(p, back) < 1e-6, $"{p} came back as {back} at zoom {zoom}");
    }
  }

  [Fact]
  public void APicturePixelLandsOnTheSameGroundAtEveryZoom()
  {
    // Zooming the map must never slide the picture across it.
    var picture = Picture(rotation: -20);
    var pixel = new PointD(123, 456);

    var at15 = View(15).ToLatLon(picture.ImageToScreen(View(15), pixel));
    var at19 = View(19).ToLatLon(picture.ImageToScreen(View(19), pixel));

    Assert.True(GeoMath.DistanceMetres(at15, at19) < 0.01, $"{GeoMath.DistanceMetres(at15, at19):F4}m apart");
    Assert.True(GeoMath.DistanceMetres(at19, picture.ImageToLatLon(pixel)) < 0.01);
  }

  [Fact]
  public void AClickIsOnThePictureOnlyInsideItsTurnedOutline()
  {
    var picture = Picture(rotation: 45);
    var view = View(17);
    var centre = view.ToScreenD(picture.Center);
    var k = picture.ScreenScale(view);

    Assert.True(picture.Contains(view, new Point((int)centre.X, (int)centre.Y)));

    // Near the top-right corner of the picture as it would be unturned (it is
    // 800x600): inside that box, but outside the picture once turned 45 degrees.
    var nearUnturnedCorner = new Point((int)(centre.X + 380 * k), (int)(centre.Y - 280 * k));
    Assert.False(picture.Contains(view, nearUnturnedCorner));
  }

  // ---- Moving it by hand -----------------------------------------------------

  [Fact]
  public void MovingItKeepsItsSizeAndRotation()
  {
    var picture = Picture(rotation: 12);
    var view = View(17);

    var moved = picture.MovedBy(view, 300, -200);

    Assert.Equal(picture.Scale, moved.Scale);
    Assert.Equal(picture.RotationDegrees, moved.RotationDegrees);
    Assert.Same(picture.ImageData, moved.ImageData);

    var before = view.ToScreenD(picture.Center);
    var after = view.ToScreenD(moved.Center);
    Assert.Equal(300, after.X - before.X, 6);
    Assert.Equal(-200, after.Y - before.Y, 6);
  }

  [Fact]
  public void ResizingAndTurningKeepTheCentreWhereItIs()
  {
    var picture = Picture();
    var view = View(17);
    var centre = view.ToScreenD(picture.Center);

    var bigger = picture.ScaledAboutCentre(view,
      new PointD(centre.X + 100, centre.Y), new PointD(centre.X + 150, centre.Y));

    Assert.True(Math.Abs(bigger.Scale / picture.Scale - 1.5) < 1e-9, $"scaled by {bigger.Scale / picture.Scale}");
    Assert.Equal(picture.Center, bigger.Center);

    // Screen y points down, so sweeping from east of the centre round to south of
    // it is a quarter turn clockwise.
    var turned = picture.RotatedAboutCentre(view,
      new PointD(centre.X + 100, centre.Y), new PointD(centre.X, centre.Y + 100));

    Assert.Equal(90, turned.RotationDegrees, 9);
    Assert.Equal(picture.Center, turned.Center);
    Assert.Equal(picture.Scale, turned.Scale);
  }

  [Fact]
  public void DraggingACornerOntoTheCentreDoesNotShrinkThePictureToNothing()
  {
    // Scale zero would divide by zero on every later paint.
    var picture = Picture();
    var view = View(17);
    var centre = view.ToScreenD(picture.Center);

    var squashed = picture.ScaledAboutCentre(view,
      new PointD(centre.X + 200, centre.Y), new PointD(centre.X + 0.01, centre.Y));

    Assert.True(squashed.IsValid());

    var corners = squashed.ScreenCorners(view);
    Assert.True(Pixels(corners[0], corners[1]) >= TrackReferenceImage.MinScreenPixels - 1e-6);
  }

  [Fact]
  public void ALockIsNotDroppedByAnythingThatMakesANewPlacement()
  {
    // Each gesture returns a new placement. One that forgot the lock would
    // silently unlock the picture on the next save.
    var picture = Picture(rotation: 10);
    picture.Locked = true;

    var view = View(17);
    var centre = view.ToScreenD(picture.Center);
    var east = new PointD(centre.X + 100, centre.Y);

    Assert.True(picture.Clone().Locked);
    Assert.True(picture.MovedBy(view, 10, 10).Locked);
    Assert.True(picture.ScaledAboutCentre(view, east, new PointD(centre.X + 120, centre.Y)).Locked);
    Assert.True(picture.RotatedAboutCentre(view, east, new PointD(centre.X, centre.Y + 100)).Locked);
    Assert.True(picture.FitTwoPoints(new PointD(100, 80), new LatLon(50.1, 8.2), new PointD(700, 520), new LatLon(50.102, 8.205))!.Locked);
  }

  // ---- Lining it up from two landmarks ----------------------------------------

  [Theory]
  [InlineData(-120)]
  [InlineData(0)]
  [InlineData(95)]
  public void TwoLandmarksLineThePictureUpExactly(double rotation)
  {
    var truth = Picture(rotation, metresPerPixel: 0.37, center: new LatLon(50.13, 8.24));

    // Where the landmarks really are, found through the map rather than through
    // the fitting maths under test.
    var view = View(18, truth.Center);
    var pictureA = new PointD(100, 80);
    var pictureB = new PointD(700, 520);
    var groundA = view.ToLatLon(truth.ImageToScreen(view, pictureA));
    var groundB = view.ToLatLon(truth.ImageToScreen(view, pictureB));

    // Starting from somewhere quite wrong: unrotated, twice the size, a kilometre off.
    var guess = Picture(0, metresPerPixel: 0.8, center: new LatLon(50.12, 8.23));
    var fitted = guess.FitTwoPoints(pictureA, groundA, pictureB, groundB);

    Assert.NotNull(fitted);
    Assert.True(GeoMath.DistanceMetres(truth.Center, fitted!.Center) < 0.01,
      $"centre {GeoMath.DistanceMetres(truth.Center, fitted.Center):F4}m out");
    Assert.True(AngleBetween(truth.RotationDegrees, fitted.RotationDegrees) < 0.01,
      $"rotation {fitted.RotationDegrees} for {rotation}");
    Assert.True(Math.Abs(fitted.Scale / truth.Scale - 1) < 1e-6,
      $"scale out by {fitted.Scale / truth.Scale - 1:E2}");
  }

  [Fact]
  public void LandmarksTooCloseTogetherAreRefused()
  {
    // A pixel of clicking error between two close landmarks is degrees of rotation.
    var picture = Picture();
    var a = new LatLon(50.1, 8.2);

    Assert.Null(picture.FitTwoPoints(new PointD(100, 100), a, new PointD(110, 100), new LatLon(50.101, 8.2)));
    Assert.Null(picture.FitTwoPoints(new PointD(100, 100), a, new PointD(700, 500), new LatLon(50.100001, 8.2)));
  }

  // ---- Housekeeping ------------------------------------------------------------

  [Fact]
  public void AFreshPictureFitsInsideTheView()
  {
    var view = View(16);
    var picture = TrackReferenceImage.FitToView(view, new byte[] { 1 }, 1600, 900, "wide.png");

    foreach (var corner in picture.ScreenCorners(view))
    {
      Assert.InRange(corner.X, 0, view.ViewSize.Width);
      Assert.InRange(corner.Y, 0, view.ViewSize.Height);
    }

    Assert.Equal(0, picture.RotationDegrees);
    Assert.True(picture.IsValid());
  }

  [Fact]
  public void NonsensePlacementsAreNotValid()
  {
    // Anything that arrives from disk or another club's file is checked with this,
    // because a NaN would stop both painting and saving.
    Assert.True(Picture().IsValid());

    Assert.False(Changed(p => p.Scale = double.NaN).IsValid());
    Assert.False(Changed(p => p.Scale = 0).IsValid());
    Assert.False(Changed(p => p.Scale = -p.Scale).IsValid());
    Assert.False(Changed(p => p.RotationDegrees = double.PositiveInfinity).IsValid());
    Assert.False(Changed(p => p.Center = new LatLon(double.NaN, 8)).IsValid());
    Assert.False(Changed(p => p.ImageData = Array.Empty<byte>()).IsValid());
    Assert.False(Changed(p => p.ImageData = null!).IsValid());
    Assert.False(Changed(p => p.PixelWidth = 0).IsValid());

    static TrackReferenceImage Changed(Action<TrackReferenceImage> change)
    {
      var picture = Picture();
      change(picture);
      return picture;
    }
  }
}
