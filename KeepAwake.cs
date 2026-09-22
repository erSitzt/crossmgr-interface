using System.Runtime.InteropServices;

namespace CrossMgrInterface;

/// <summary>
/// Keeps Windows from going to sleep, switching the screen off or locking
/// itself while the application is open.
///
/// A race is two hours during which nobody touches the laptop: the operator
/// watches the board. On the laptop's own power settings that is long enough
/// to blank the screen, lock, and then sleep - and a sleeping laptop reads no
/// transponders, so the field's laps are simply gone. Telling Windows the
/// application needs the system and the display awake, for as long as it is
/// running, is what stops that.
///
/// It cannot stop everything: closing the lid, the power button and a lock
/// forced by a company policy still do what they are set to do.
/// </summary>
public static class KeepAwake
{
  // SetThreadExecutionState flags. ES_CONTINUOUS makes the request stand until
  // it is cleared or the thread ends; the other two are what it asks for.
  private const uint EsContinuous = 0x80000000;
  private const uint EsSystemRequired = 0x00000001;
  private const uint EsDisplayRequired = 0x00000002;

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern uint SetThreadExecutionState(uint esFlags);

  /// <summary>Whether the request is currently standing.</summary>
  public static bool IsOn { get; private set; }

  /// <summary>
  /// Asks Windows to stay awake and keep the screen on. The request belongs to
  /// the calling thread, so call it from the UI thread, which lives as long as
  /// the window does. True when Windows accepted it.
  /// </summary>
  public static bool On()
  {
    IsOn = SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired) != 0;
    return IsOn;
  }

  /// <summary>Hands the power settings back to Windows. From the same thread as <see cref="On"/>.</summary>
  public static void Off()
  {
    if (!IsOn) return;
    SetThreadExecutionState(EsContinuous);
    IsOn = false;
  }
}
