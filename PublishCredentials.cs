using System.Security.Cryptography;
using System.Text;

namespace CrossMgrInterface;

/// <summary>
/// The results website key, kept where only this Windows user can read it.
///
/// Encrypted with DPAPI into a file of its own rather than written into
/// settings.json. A key is a password: it lets a computer publish results as
/// this club, so it must not travel in the file people email each other when
/// something needs sorting out.
///
/// Per user and per machine on purpose. A profile copied to another laptop
/// cannot decrypt it, which is exactly the behaviour wanted - the new laptop
/// asks for the key rather than silently inheriting the right to publish.
/// </summary>
public static class PublishCredentials
{
  // Ties the encrypted blob to this application, so another program running as
  // the same user cannot simply hand the bytes to DPAPI and read the key.
  private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CrossMgrInterface.ResultsKey.v1");

  /// <summary>The saved key, or null when there is none or it cannot be read here.</summary>
  public static string? Load()
  {
    try
    {
      var path = AppPaths.ResultsKeyFile;
      if (!File.Exists(path)) return null;

      var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
      var key = Encoding.UTF8.GetString(plain);
      return string.IsNullOrWhiteSpace(key) ? null : key;
    }
    catch (CryptographicException)
    {
      // Written by a different user, or the profile was copied to another
      // machine. The dialog asks for the key again rather than showing a
      // stack trace to a volunteer.
      return null;
    }
    catch (IOException)
    {
      return null;
    }
  }

  /// <summary>True when a key is saved on this computer.</summary>
  public static bool HasKey() => Load() != null;

  /// <summary>
  /// Enough of the saved key to recognise it - "olt_667aedc2…" - and never the
  /// rest. The same prefix the website lists, so an operator can match the
  /// laptop to the key by eye without either side revealing the secret.
  /// </summary>
  public static string? Hint()
  {
    var key = Load();
    if (key == null) return null;

    var secondUnderscore = key.IndexOf('_', key.IndexOf('_') + 1);
    return secondUnderscore > 0 ? key[..secondUnderscore] + "\u2026" : key[..Math.Min(8, key.Length)] + "\u2026";
  }

  public static bool Save(string key)
  {
    try
    {
      var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), Entropy, DataProtectionScope.CurrentUser);
      File.WriteAllBytes(AppPaths.ResultsKeyFile, blob);
      return true;
    }
    catch (Exception)
    {
      return false;
    }
  }

  public static void Forget()
  {
    try
    {
      if (File.Exists(AppPaths.ResultsKeyFile)) File.Delete(AppPaths.ResultsKeyFile);
    }
    catch (IOException)
    {
      // Nothing useful to do, and nothing worth stopping a race day for.
    }
  }
}
