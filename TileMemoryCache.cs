namespace CrossMgrInterface;

/// <summary>
/// Decoded tiles held in memory, most recently used first.
///
/// UI THREAD ONLY. That is the whole concurrency design: a Paint is synchronous,
/// so if only the UI thread ever touches this, no eviction can possibly interleave
/// with a DrawImage. One rule instead of a lock, a reference count, and a
/// use-after-dispose bug that only shows up under load. See TileLayer.
///
/// The bound is not optional. A 256x256 32bpp bitmap is unmanaged memory: the GC
/// sees a 24-byte wrapper and feels no pressure at all from the 256 KiB behind it,
/// so an unbounded cache reaches gigabytes within a few minutes of panning. Every
/// eviction disposes.
/// </summary>
public sealed class TileMemoryCache : IDisposable
{
  /// <summary>
  /// 256 tiles is 64 MiB. A 1920x1080 viewport needs about 54, so this holds
  /// roughly five screens - enough to pan freely and to keep the parent zoom
  /// level resident for the stretch-while-loading fallback.
  /// </summary>
  public const int DefaultCapacity = 256;

  private readonly Dictionary<TileId, LinkedListNode<Entry>> _index = new();
  private readonly LinkedList<Entry> _order = new();
  private readonly int _capacity;

  private sealed class Entry
  {
    public TileId Id;
    public Bitmap Bitmap = null!;
  }

  public TileMemoryCache(int capacity = DefaultCapacity) =>
    _capacity = Math.Max(8, capacity);

  public int Count => _index.Count;
  public int Capacity => _capacity;

  /// <summary>
  /// A bitmap for drawing RIGHT NOW. The caller must not retain it: the next Put
  /// may evict and dispose it.
  /// </summary>
  public bool TryGet(TileId id, out Bitmap bitmap)
  {
    if (_index.TryGetValue(id, out var node))
    {
      _order.Remove(node);
      _order.AddFirst(node);
      bitmap = node.Value.Bitmap;
      return true;
    }

    bitmap = null!;
    return false;
  }

  public bool Contains(TileId id) => _index.ContainsKey(id);

  public void Put(TileId id, Bitmap bitmap)
  {
    if (_index.TryGetValue(id, out var existing))
    {
      // Replacing the same tile: dispose the copy we are dropping, not the new one.
      if (!ReferenceEquals(existing.Value.Bitmap, bitmap))
      {
        existing.Value.Bitmap.Dispose();
        existing.Value.Bitmap = bitmap;
      }

      _order.Remove(existing);
      _order.AddFirst(existing);
      return;
    }

    _order.AddFirst(new Entry { Id = id, Bitmap = bitmap });
    _index[id] = _order.First!;

    while (_index.Count > _capacity)
    {
      var oldest = _order.Last!;
      _order.RemoveLast();
      _index.Remove(oldest.Value.Id);
      oldest.Value.Bitmap.Dispose();
    }
  }

  public void Clear()
  {
    foreach (var entry in _order) entry.Bitmap.Dispose();
    _order.Clear();
    _index.Clear();
  }

  public void Dispose() => Clear();
}
