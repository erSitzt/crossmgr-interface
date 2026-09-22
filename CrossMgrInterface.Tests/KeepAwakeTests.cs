using Xunit;

namespace CrossMgrInterface.Tests;

/// <summary>
/// The tests run on Windows, so the real call is made: what is checked is that
/// Windows takes the request and that it is cleared again, since a request
/// left standing by a test runner would keep this machine awake.
/// </summary>
public class KeepAwakeTests
{
  [Fact]
  public void WindowsAcceptsTheRequestAndItCanBeWithdrawn()
  {
    try
    {
      Assert.True(KeepAwake.On());
      Assert.True(KeepAwake.IsOn);
    }
    finally
    {
      KeepAwake.Off();
    }

    Assert.False(KeepAwake.IsOn);
  }

  [Fact]
  public void SwitchingOffWhenNothingIsOnIsHarmless()
  {
    KeepAwake.Off();
    Assert.False(KeepAwake.IsOn);
  }
}
