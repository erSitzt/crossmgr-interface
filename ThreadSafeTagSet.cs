using System.Collections;
using System.Collections.Concurrent;

namespace CrossMgrInterface;

/// <summary>
/// A set of transponder IDs that is safe to read from the network thread while
/// the UI thread adds to or removes from it.
///
/// This replaces a plain HashSet that was written on the UI thread (when the
/// operator ignored a tag) and read on the TCP receive thread (on every single
/// tag read) with no synchronisation at all - concurrent read/write on a
/// HashSet is undefined behaviour, and the failure mode is a corrupted lookup
/// during a race rather than a clean exception.
///
/// The surface deliberately mirrors HashSet so call sites read unchanged.
/// </summary>
public sealed class ThreadSafeTagSet : IEnumerable<string>
{
  private readonly ConcurrentDictionary<string, byte> _tags = new(StringComparer.Ordinal);

  public int Count => _tags.Count;

  public bool Contains(string tag) => _tags.ContainsKey(tag);

  /// <summary>Returns false if the tag was already present.</summary>
  public bool Add(string tag) => _tags.TryAdd(tag, 0);

  /// <summary>Returns false if the tag was not present.</summary>
  public bool Remove(string tag) => _tags.TryRemove(tag, out _);

  public void Clear() => _tags.Clear();

  public IEnumerator<string> GetEnumerator() => _tags.Keys.GetEnumerator();

  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
