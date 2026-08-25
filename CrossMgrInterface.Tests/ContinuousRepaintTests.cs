using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// Pins the contract that makes the track map animate.
///
/// This is worth a test rather than a comment because the first attempt at it was
/// wrong in a way that looked right: the view called Invalidate on itself from
/// inside Render, and RenderOne's finally block cleared the bit again immediately
/// afterwards. The map ran at the once-a-second heartbeat instead of eight frames
/// a second, and nothing failed - it just looked slightly broken on screen.
/// </summary>
public class ContinuousRepaintTests
{
  private sealed class CountingView : IRaceView
  {
    public int Renders;
    public int Heartbeats;
    public bool Animating;

    public RaceViewKind Kind => RaceViewKind.Track;

    /// <summary>Null means always visible, so the test needs no real tab.</summary>
    public TabPage? HostTab => null;

    public bool NeedsHeartbeat => true;
    public bool WantsContinuousRepaint => Animating;

    public void Render() => Renders++;
    public void RenderHeartbeat() => Heartbeats++;
  }

  /// <summary>
  /// RenderNow(None) invalidates nothing, so it renders if and only if a dirty
  /// bit was still set from before. That is exactly the thing under test.
  /// </summary>
  private static (UiRefreshCoordinator Coordinator, CountingView View) Setup(bool animating)
  {
    var tabs = new TabControl();
    var coordinator = new UiRefreshCoordinator(tabs);
    var view = new CountingView { Animating = animating };

    coordinator.Register(view);
    return (coordinator, view);
  }

  [Fact]
  public void AnAnimatingViewStaysDirtySoThePumpRepaintsItEveryTick()
  {
    var (coordinator, view) = Setup(animating: true);
    using var _ = coordinator;

    coordinator.RenderNow(RaceViewKind.Track);
    Assert.Equal(1, view.Renders);

    // Nothing new has been invalidated. An animating view must still repaint,
    // because its content moves with the clock rather than with the data.
    coordinator.RenderNow(RaceViewKind.None);
    coordinator.RenderNow(RaceViewKind.None);

    Assert.Equal(3, view.Renders);
  }

  [Fact]
  public void AnOrdinaryViewGoesQuietOnceItHasRendered()
  {
    var (coordinator, view) = Setup(animating: false);
    using var _ = coordinator;

    coordinator.RenderNow(RaceViewKind.Track);
    Assert.Equal(1, view.Renders);

    coordinator.RenderNow(RaceViewKind.None);
    coordinator.RenderNow(RaceViewKind.None);

    Assert.Equal(1, view.Renders);
  }

  [Fact]
  public void AViewStopsAnimatingAsSoonAsItSaysSo()
  {
    // The race finishing must actually stop the pump spinning, or a finished
    // race would run at full rate for as long as the window stays open.
    var (coordinator, view) = Setup(animating: true);
    using var _ = coordinator;

    coordinator.RenderNow(RaceViewKind.Track);
    coordinator.RenderNow(RaceViewKind.None);
    Assert.Equal(2, view.Renders);

    view.Animating = false;

    // One more render clears the bit that was left set, and then it goes quiet.
    coordinator.RenderNow(RaceViewKind.None);
    coordinator.RenderNow(RaceViewKind.None);
    coordinator.RenderNow(RaceViewKind.None);

    Assert.Equal(3, view.Renders);
  }

  [Fact]
  public void ViewsThatDoNotOptInAreUnaffectedByDefault()
  {
    // Every existing view inherits the default, so none of them started spinning.
    // Reached through the interface, because a default interface member is not
    // visible on the concrete type.
    IRaceView view = new DefaultView();

    Assert.False(view.WantsContinuousRepaint);
  }

  private sealed class DefaultView : IRaceView
  {
    public RaceViewKind Kind => RaceViewKind.Riders;
    public TabPage? HostTab => null;
    public bool NeedsHeartbeat => false;
    public void Render() { }
  }
}
