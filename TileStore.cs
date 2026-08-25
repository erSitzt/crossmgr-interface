using System.Drawing.Imaging;

namespace CrossMgrInterface;

/// <summary>
/// Map tiles on disk, under %LOCALAPPDATA%\CrossMgrInterface\tiles.
///
/// Background threads only - the render path never touches this class. See
/// TileLayer for the threading rules.
///
/// Tiles are kept indefinitely. The OSM tile usage policy asks for a minimum of
/// seven days' caching, so keeping them forever is compliant and strictly fewer
/// requests; a circuit's basemap does not change during a season, and a timing
/// laptop is regularly on a field with no usable internet. "Clear tile cache"
/// replaces a expiry policy.
/// </summary>
public sealed class TileStore
{
  private readonly string _root;

  public string Root => _root;

  /// <summary>
  /// The cache is partitioned by tile server. Without that, changing provider
  /// would silently keep serving the previous one's imagery from disk forever.
  /// </summary>
  public TileStore(string urlTemplate, string? rootFolder = null)
  {
    _root = Path.Combine(rootFolder ?? AppPaths.TileCacheFolder, HostSlug(urlTemplate));
    Directory.CreateDirectory(_root);
  }

  public static string HostSlug(string urlTemplate)
  {
    string host;
    try
    {
      var probe = urlTemplate.Replace("{z}", "0").Replace("{x}", "0").Replace("{y}", "0");
      host = new Uri(probe).Host;
    }
    catch (Exception)
    {
      host = "unknown";
    }

    var slug = new string(host.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    return string.IsNullOrWhiteSpace(slug) ? "unknown" : slug;
  }

  /// <summary>
  /// Cache path for a tile. The .png suffix is this cache's own naming and says
  /// nothing about the content - the satellite layer serves JPEG, and Decode reads
  /// the bytes rather than the extension.
  /// </summary>
  public string PathFor(TileId t) =>
    Path.Combine(_root, t.Z.ToString(), t.X.ToString(), t.Y + ".png");

  public bool Exists(TileId t)
  {
    try
    {
      return File.Exists(PathFor(t));
    }
    catch (Exception)
    {
      return false;
    }
  }

  public async Task<byte[]?> ReadAsync(TileId t, CancellationToken ct)
  {
    try
    {
      var path = PathFor(t);
      if (!File.Exists(path)) return null;

      var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
      return bytes.Length > 0 ? bytes : null;
    }
    catch (Exception)
    {
      // A tile that will not read is a tile we re-download, not a crash.
      return null;
    }
  }

  /// <summary>
  /// Writes via a temporary file in the same folder.
  ///
  /// Not belt and braces: a crash or two writers part way through a direct write
  /// leaves a truncated PNG, and a truncated PNG throws from the decoder on every
  /// single subsequent paint, forever. File.Move on one volume is atomic.
  /// </summary>
  public async Task WriteAsync(TileId t, byte[] png, CancellationToken ct)
  {
    try
    {
      var path = PathFor(t);
      Directory.CreateDirectory(Path.GetDirectoryName(path)!);

      var temp = path + ".tmp";
      await File.WriteAllBytesAsync(temp, png, ct).ConfigureAwait(false);
      File.Move(temp, path, overwrite: true);
    }
    catch (Exception)
    {
      // A cache that cannot be written is slow, not broken.
    }
  }

  /// <summary>
  /// Decodes PNG bytes into a bitmap detached from any stream or file.
  ///
  /// Image.FromFile holds a lock on the file for the lifetime of the Image, which
  /// is the single most common System.Drawing bug - the cache would then be
  /// unable to overwrite or clear its own tiles. Hence bytes, a MemoryStream, and
  /// a copy into a bitmap we own.
  ///
  /// The copy is also converted to 32bppPArgb, which is not cosmetic: it removes
  /// a pixel-format conversion from every DrawImage, and we blit around fifty
  /// tiles per frame.
  /// </summary>
  public static Bitmap Decode(byte[] png)
  {
    using var stream = new MemoryStream(png, writable: false);
    using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);

    var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
    using (var g = Graphics.FromImage(bitmap))
      g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));

    return bitmap;
  }

  public long GetCacheSizeBytes()
  {
    try
    {
      return new DirectoryInfo(_root)
        .EnumerateFiles("*.png", SearchOption.AllDirectories)
        .Sum(f => f.Length);
    }
    catch (Exception)
    {
      return 0;
    }
  }

  /// <summary>How many of these tiles are already on disk. Drives the "63% cached" hint.</summary>
  public int CountCached(IEnumerable<TileId> tiles) => tiles.Count(Exists);

  public void Clear()
  {
    try
    {
      if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
      Directory.CreateDirectory(_root);
    }
    catch (Exception)
    {
      // Best effort: files being read right now simply survive to the next attempt.
    }
  }
}
