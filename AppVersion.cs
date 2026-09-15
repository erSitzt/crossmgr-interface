using System.Reflection;

namespace CrossMgrInterface;

/// <summary>
/// Which build this is, in words an operator can read out over the phone.
///
/// A release is built with -p:Version from its tag (release.yml), so it reports
/// the tag. Anything built on a developer's machine carries the csproj's
/// 0.0.0-dev and says so, rather than passing itself off as a release: "which
/// version is the timing laptop running?" needs an honest answer on race day.
/// </summary>
public static class AppVersion
{
  /// <summary>The whole informational version, e.g. "0.9.0+3f9ac1d...", commit included when the SDK knew it.</summary>
  public static string Full { get; } =
    typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? "unknown";

  /// <summary>The short form for the status bar: "v0.9.0", or "dev build 3f9ac1d".</summary>
  public static string Display { get; } = Describe(Full);

  public static string Describe(string informationalVersion)
  {
    var plus = informationalVersion.IndexOf('+');
    var version = plus >= 0 ? informationalVersion[..plus] : informationalVersion;
    var revision = plus >= 0 ? informationalVersion[(plus + 1)..] : "";
    if (revision.Length > 7) revision = revision[..7];

    var isRelease = version.Length > 0 && char.IsDigit(version[0]) &&
                    !version.EndsWith("-dev", StringComparison.OrdinalIgnoreCase);

    if (isRelease) return $"v{version}";

    return revision.Length > 0 ? $"dev build {revision}" : "dev build";
  }
}
