using Xunit;

namespace CrossMgrInterface.Tests;

public class AppVersionTests
{
  [Theory]
  [InlineData("0.9.0+3f9ac1d0b2e4f6a8", "v0.9.0")]
  [InlineData("0.10.0", "v0.10.0")]
  [InlineData("1.2.3-beta1+abc", "v1.2.3-beta1")]
  public void AReleaseShowsItsTag(string informational, string shown)
  {
    Assert.Equal(shown, AppVersion.Describe(informational));
  }

  [Theory]
  [InlineData("0.0.0-dev+ba60c68e1234567", "dev build ba60c68")]
  [InlineData("0.0.0-dev", "dev build")]
  [InlineData("unknown", "dev build")]
  [InlineData("", "dev build")]
  public void ALocalBuildSaysSoRatherThanPosingAsARelease(string informational, string shown)
  {
    Assert.Equal(shown, AppVersion.Describe(informational));
  }

  [Fact]
  public void ThisBuildDescribesItself()
  {
    // The test project builds the app locally, so this is always the csproj's dev version.
    Assert.StartsWith("dev build", AppVersion.Display);
    Assert.StartsWith("0.0.0-dev", AppVersion.Full);
  }
}
