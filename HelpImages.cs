namespace CrossMgrInterface;

/// <summary>
/// The screenshots the help shows, embedded in the application from help_images/.
///
/// Generated rather than taken by hand: HelpScreenshotTests renders each screen
/// with sample data, so a picture can be brought up to date whenever the screen
/// changes. CLAUDE.md says when that is.
/// </summary>
public static class HelpImages
{
  private const string Prefix = "help_images/";
  private const string Extension = ".png";

  /// <summary>Every embedded picture, by name - the file name without its extension.</summary>
  public static IReadOnlyList<string> Names { get; } = typeof(HelpImages).Assembly
    .GetManifestResourceNames()
    .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) &&
                n.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
    .Select(n => n[Prefix.Length..^Extension.Length])
    .OrderBy(n => n, StringComparer.Ordinal)
    .ToList();

  /// <summary>The PNG bytes of a picture, or null if there is none by that name.</summary>
  public static byte[]? Load(string name)
  {
    using var stream = typeof(HelpImages).Assembly.GetManifestResourceStream(Prefix + name + Extension);
    if (stream is null) return null;

    using var copy = new MemoryStream();
    stream.CopyTo(copy);
    return copy.ToArray();
  }
}
