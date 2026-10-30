using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CrossMgrInterface;

/// <summary>What became of one code found in a piece of text.</summary>
/// <param name="Plan">The plan it holds, or null when it could not be read.</param>
/// <param name="Error">Why it could not be read, in words for the volunteer.</param>
/// <param name="Code">The code as found, tidied: as the encoder writes it when it was read.</param>
/// <param name="LineText">What else was on its line - "Summer club race:" - to name it by.</param>
public sealed record FoundPlanCode(DayPlan? Plan, string? Error, string Code, string LineText);

/// <summary>
/// A day plan as a code: a paragraph of letters and digits that holds the
/// whole timetable, so a club can keep its setups in a list, send one in a
/// message, and set the day up from it on any laptop without the internet.
///
/// The code is a promise. A club's list made today has to work in every
/// version of the application to come, so the format is fixed and versioned,
/// and the tests hold the codes of the built-in presets to the byte. What may
/// be added later goes into a new version or into the extension block at the
/// end, never into what version 1 already means.
///
/// The text is "CMP1-" and Crockford Base32 - no I, L, O or U, case does not
/// matter, an O typed for a 0 still reads - in blocks of five. Inside it:
/// version, length, the plan, and a CRC-16 over all of it, so a mistyped
/// character is reported rather than read as a different plan. The length
/// is what lets a code be found in a message: the reader knows where it ends
/// even when an email client has broken it over three lines and the club's
/// name for it follows on the same line.
///
/// What goes in is only what is the same every time the day is run: not the
/// date, the rider list, the circuit or the folder sheets are saved to.
/// </summary>
public static class DayPlanCode
{
  public const string Prefix = "CMP1";
  public const int CurrentVersion = 1;

  private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

  // Bounds a decoder holds a code to, so a corrupt one is refused rather than
  // filling memory. Far beyond anything a club day needs.
  private const int MaxSessions = 200;
  private const int MaxStrings = 1000;
  private const int MaxStringBytes = 400;
  private const int MaxClassesPerSession = 60;
  private const int MaxPayloadBytes = 64 * 1024;

  // Plan flags.
  private const uint PlanPublish = 1, PlanSaveSheets = 2, PlanPdf = 4, PlanLiveTiming = 8;
  private const uint PlanKnownFlags = 15;

  // Session flags: type in bits 0-1, start in 2-3, team in 4, name kind in 5-6, group kind in 7-8.
  private const uint SessionKnownFlags = 0x1FF;

  private enum NameKind { Full = 0, LabelAndClasses = 1, LabelAndGroup = 2 }
  private enum GroupKind { None = 0, SameAsClasses = 1, Text = 2 }

  /// <summary>How the classes of a session read in its name: "Moto 1 - MX1 / MX2".</summary>
  public static string ClassesSuffix(IEnumerable<string> classes) => string.Join(" / ", classes);

  /// <summary>
  /// What decoding gives back: only what the code can carry, in the form the
  /// windows would have produced. Out of range values are brought into the
  /// range the windows allow, settings a session ignores are left at their
  /// defaults, and everything about the day itself - typed times, skips - is
  /// left out.
  /// </summary>
  public static DayPlan Canonical(DayPlan plan)
  {
    var copy = new DayPlan
    {
      Name = Nfc(plan.Name),
      Date = DateTime.Today,
      ChangeoverMinutes = Math.Clamp(plan.ChangeoverMinutes, 0, 60),
      TypicalLapSeconds = Math.Clamp(plan.TypicalLapSeconds, 20, 3600),
      After = new AfterSession
      {
        Publish = plan.After?.Publish ?? false,
        SaveSheets = plan.After?.SaveSheets ?? false,
        Pdf = plan.After?.Pdf ?? false,
        LiveTiming = plan.After?.LiveTiming ?? false
      }
    };

    foreach (var s in plan.Sessions)
    {
      var timed = s.SessionType != SessionType.Race;
      var team = !timed && s.TeamEvent;
      var classes = team ? new List<string>() : s.Classes
        .Where(c => !string.IsNullOrWhiteSpace(c))
        .Select(Nfc)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
      var start = team && s.Start == PlannedStartMode.Waves ? PlannedStartMode.Gate : s.Start;
      var group = timed || string.IsNullOrWhiteSpace(s.OverallGroup) ? null : Nfc(s.OverallGroup);
      var minutes = Math.Clamp((int)Math.Floor(s.Time.TotalMinutes), 0, 23 * 60 + 59);

      copy.Sessions.Add(new PlannedSession
      {
        Time = TimeSpan.FromMinutes(minutes),
        Name = Nfc(s.Name),
        SessionType = Enum.IsDefined(s.SessionType) ? s.SessionType : SessionType.Race,
        Classes = classes,
        DurationMinutes = Math.Clamp(s.DurationMinutes, 1, 600),
        AdditionalLaps = timed ? 0 : Math.Clamp(s.AdditionalLaps, 0, 10),
        Start = Enum.IsDefined(start) ? start : PlannedStartMode.Gate,
        WaveGapSeconds = start == PlannedStartMode.Waves ? Math.Clamp(s.WaveGapSeconds, 10, 1800) : 60,
        TeamEvent = team,
        OverallGroup = group
      });
    }

    return copy;
  }

  // ---- Writing --------------------------------------------------------------

  public static string Encode(DayPlan plan)
  {
    var p = Canonical(plan);
    var strings = new StringTable();
    var body = new List<byte>();

    uint flags = 0;
    if (p.After.Publish) flags |= PlanPublish;
    if (p.After.SaveSheets) flags |= PlanSaveSheets;
    if (p.After.Pdf) flags |= PlanPdf;
    if (p.After.LiveTiming) flags |= PlanLiveTiming;

    // The sessions first, into a buffer of their own: the string table that
    // goes before them is only complete once they have all been written.
    var sessions = new List<byte>();
    WriteVarint(sessions, (uint)strings.Index(p.Name));
    WriteVarint(sessions, (uint)p.Sessions.Count);
    var previous = 0;
    for (var i = 0; i < p.Sessions.Count; i++)
    {
      var s = p.Sessions[i];
      var suffix = ClassesSuffix(s.Classes);
      var groupKind = s.OverallGroup == null ? GroupKind.None
        : s.Classes.Count > 0 && s.OverallGroup == suffix ? GroupKind.SameAsClasses
        : GroupKind.Text;

      var nameKind = NameKind.Full;
      var label = s.Name;
      if (s.Classes.Count > 0 && s.Name.EndsWith(" - " + suffix, StringComparison.Ordinal))
      {
        nameKind = NameKind.LabelAndClasses;
        label = s.Name[..^(" - " + suffix).Length];
      }
      else if (s.OverallGroup != null && s.Name.EndsWith(" - " + s.OverallGroup, StringComparison.Ordinal))
      {
        nameKind = NameKind.LabelAndGroup;
        label = s.Name[..^(" - " + s.OverallGroup).Length];
      }

      var sessionFlags = (uint)s.SessionType | (uint)s.Start << 2 | (s.TeamEvent ? 1u : 0u) << 4 |
                         (uint)nameKind << 5 | (uint)groupKind << 7;
      WriteVarint(sessions, sessionFlags);

      var minutes = (int)s.Time.TotalMinutes;
      WriteVarint(sessions, Zigzag(i == 0 ? minutes : minutes - previous));
      previous = minutes;

      WriteVarint(sessions, (uint)s.DurationMinutes);
      if (s.SessionType == SessionType.Race) WriteVarint(sessions, (uint)s.AdditionalLaps);
      if (s.Start == PlannedStartMode.Waves) WriteVarint(sessions, (uint)s.WaveGapSeconds);

      WriteVarint(sessions, (uint)s.Classes.Count);
      foreach (var c in s.Classes) WriteVarint(sessions, (uint)strings.Index(c));

      WriteVarint(sessions, (uint)strings.Index(label));
      if (groupKind == GroupKind.Text) WriteVarint(sessions, (uint)strings.Index(s.OverallGroup!));
    }

    WriteVarint(body, flags);
    WriteVarint(body, (uint)p.ChangeoverMinutes);
    WriteVarint(body, (uint)p.TypicalLapSeconds);
    WriteVarint(body, (uint)strings.Items.Count);
    foreach (var text in strings.Items)
    {
      var bytes = Encoding.UTF8.GetBytes(text);
      WriteVarint(body, (uint)bytes.Length);
      body.AddRange(bytes);
    }
    body.AddRange(sessions);
    WriteVarint(body, 0); // the extension block: nothing in version 1

    var payload = new List<byte>();
    WriteVarint(payload, CurrentVersion);
    WriteVarint(payload, (uint)body.Count);
    payload.AddRange(body);

    var crc = Crc16(payload);
    payload.Add((byte)(crc >> 8));
    payload.Add((byte)crc);

    var chars = ToBase32(payload);
    var blocks = new List<string> { Prefix };
    for (var i = 0; i < chars.Length; i += 5) blocks.Add(chars.Substring(i, Math.Min(5, chars.Length - i)));
    return string.Join("-", blocks);
  }

  // ---- Reading --------------------------------------------------------------

  /// <summary>
  /// Reads the one code in <paramref name="text"/> - pasted on its own, with
  /// spaces, line breaks, lower case, an O for a 0 or a name beside it.
  /// </summary>
  public static FoundPlanCode Decode(string text)
  {
    var found = FindAll(text);
    if (found.Count == 0)
      return new FoundPlanCode(null, "That is not a plan code. A plan code starts with CMP1.", "", text.Trim());
    return found[0];
  }

  /// <summary>Every plan code in a piece of text - a message, a list, a printed sheet copied back - in order.</summary>
  public static List<FoundPlanCode> FindAll(string text)
  {
    var found = new List<FoundPlanCode>();
    var i = 0;
    while (i < text.Length)
    {
      var start = FindPrefix(text, i);
      if (start < 0) break;

      var (result, end) = ReadCode(text, start);
      found.Add(result with { LineText = LineAround(text, start, end) });
      i = Math.Max(end, start + 1);
    }
    return found;
  }

  /// <summary>"CMP1", in any case, with an I or L for the 1 - at the start of a word.</summary>
  private static int FindPrefix(string text, int from)
  {
    for (var i = from; i + 4 <= text.Length; i++)
    {
      if (i > 0 && char.IsLetterOrDigit(text[i - 1])) continue;
      if (char.ToUpperInvariant(text[i]) != 'C' || char.ToUpperInvariant(text[i + 1]) != 'M' ||
          char.ToUpperInvariant(text[i + 2]) != 'P') continue;
      var one = char.ToUpperInvariant(text[i + 3]);
      if (one is '1' or 'I' or 'L') return i;
    }
    return -1;
  }

  private static (FoundPlanCode Result, int End) ReadCode(string text, int start)
  {
    var i = start + 4;
    var chars = new StringBuilder();
    int? needed = null;

    while (needed == null || chars.Length < needed)
    {
      // Within a code everything that is not a letter or digit is spacing:
      // dashes of every length, line breaks, the bold stars a chat app adds.
      while (i < text.Length && IsSeparator(text[i])) i++;
      if (i >= text.Length || Value(text[i]) < 0)
      {
        var missing = needed is { } n ? n - chars.Length : 0;
        return (Fail(missing > 0
          ? $"{missing} character{(missing == 1 ? " is" : "s are")} missing from the end of the code."
          : "The code is cut short.", start, i, text), i);
      }

      chars.Append(Alphabet[Value(text[i])]);
      i++;

      // Version and length are at the front, laid out the same in every
      // version: as soon as they are in, how many characters are still to
      // come is known - whatever version wrote the code.
      if (needed == null)
      {
        switch (Needed(chars.ToString(), out var total))
        {
          case NeededState.Known:
            needed = total;
            break;
          case NeededState.Invalid:
            return (Fail(Mistyped, start, i, text), i);
        }
      }
    }

    // A letter or digit straight after the last one - or after a dash - means
    // a character too many was typed somewhere in the middle. A space or a
    // line break after the code ends it, and a name may follow.
    var after = i;
    while (after < text.Length && text[after] is '-' or '‐' or '‑' or '‒' or '–' or '—') after++;
    if (after < text.Length && Value(text[after]) >= 0)
      return (Fail("The code has a character too many - check it.", start, after, text), after);

    var result = DecodeChars(chars.ToString());
    var code = result.Plan != null ? Encode(result.Plan) : Prefix + "-" + chars;
    return (result with { Code = code }, i);
  }

  private static FoundPlanCode Fail(string error, int start, int end, string text) =>
    new(null, error, text[start..Math.Min(end, text.Length)].Trim(), "");

  private const string Mistyped = "The code is mistyped - check it character by character.";

  private enum NeededState { NotYet, Known, Invalid }

  /// <summary>Once the first characters are in: how many characters the whole code has.</summary>
  private static NeededState Needed(string chars, out int total)
  {
    total = 0;
    try
    {
      var bytes = FromBase32(chars, exact: false);
      var position = 0;
      if (!TryReadVarint(bytes, ref position, out _)) return NeededState.NotYet;
      if (!TryReadVarint(bytes, ref position, out var length)) return NeededState.NotYet;
      if (length > MaxPayloadBytes) return NeededState.Invalid;

      var totalBytes = position + (int)length + 2;
      total = (totalBytes * 8 + 4) / 5;
      return NeededState.Known;
    }
    catch (FormatException)
    {
      return NeededState.Invalid;
    }
  }

  private static FoundPlanCode DecodeChars(string chars)
  {
    const string mistyped = Mistyped;
    byte[] bytes;
    try
    {
      bytes = FromBase32(chars, exact: true);
    }
    catch (FormatException)
    {
      return new FoundPlanCode(null, mistyped, "", "");
    }

    if (bytes.Length < 4) return new FoundPlanCode(null, mistyped, "", "");
    var crc = Crc16(bytes.Take(bytes.Length - 2));
    if (bytes[^2] != (byte)(crc >> 8) || bytes[^1] != (byte)crc) return new FoundPlanCode(null, mistyped, "", "");

    try
    {
      var plan = Read(bytes.AsSpan(0, bytes.Length - 2).ToArray());
      return new FoundPlanCode(plan, null, "", "");
    }
    catch (NewerVersionException)
    {
      return new FoundPlanCode(null,
        "This code was made with a newer version of the application. Update this laptop to read it.", "", "");
    }
    catch (Exception)
    {
      return new FoundPlanCode(null, "That is not a valid plan code.", "", "");
    }
  }

  private sealed class NewerVersionException : Exception { }

  private static DayPlan Read(byte[] payload)
  {
    var position = 0;
    var version = Varint(payload, ref position);
    if (version > CurrentVersion) throw new NewerVersionException();
    if (version != 1) throw new FormatException();

    var length = Varint(payload, ref position);
    if (position + length != payload.Length) throw new FormatException();

    var flags = Varint(payload, ref position);
    if ((flags & ~PlanKnownFlags) != 0) throw new NewerVersionException();

    var plan = new DayPlan
    {
      Date = DateTime.Today,
      ChangeoverMinutes = (int)Bounded(Varint(payload, ref position), 0, 60),
      TypicalLapSeconds = (int)Bounded(Varint(payload, ref position), 20, 3600),
      After = new AfterSession
      {
        Publish = (flags & PlanPublish) != 0,
        SaveSheets = (flags & PlanSaveSheets) != 0,
        Pdf = (flags & PlanPdf) != 0,
        LiveTiming = (flags & PlanLiveTiming) != 0
      }
    };

    var stringCount = Bounded(Varint(payload, ref position), 0, MaxStrings);
    var strings = new List<string>((int)stringCount);
    for (var i = 0; i < stringCount; i++)
    {
      var bytes = (int)Bounded(Varint(payload, ref position), 0, MaxStringBytes);
      if (position + bytes > payload.Length) throw new FormatException();
      var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(payload, position, bytes);
      if (text != Nfc(text)) throw new FormatException();
      strings.Add(text);
      position += bytes;
    }
    string String(uint index) => index < strings.Count ? strings[(int)index] : throw new FormatException();

    plan.Name = String(Varint(payload, ref position));

    var sessionCount = Bounded(Varint(payload, ref position), 0, MaxSessions);
    var minutes = 0;
    for (var i = 0; i < sessionCount; i++)
    {
      var sf = Varint(payload, ref position);
      if ((sf & ~SessionKnownFlags) != 0) throw new NewerVersionException();

      var type = (SessionType)(sf & 3);
      var start = (PlannedStartMode)((sf >> 2) & 3);
      var team = ((sf >> 4) & 1) != 0;
      var nameKind = (NameKind)((sf >> 5) & 3);
      var groupKind = (GroupKind)((sf >> 7) & 3);
      if (!Enum.IsDefined(type) || !Enum.IsDefined(start) || !Enum.IsDefined(nameKind) || !Enum.IsDefined(groupKind))
        throw new FormatException();

      var timed = type != SessionType.Race;
      if (timed && (team || groupKind != GroupKind.None)) throw new FormatException();
      if (team && start == PlannedStartMode.Waves) throw new FormatException();

      var delta = Unzigzag(Varint(payload, ref position));
      minutes = i == 0 ? delta : minutes + delta;
      if (minutes < 0 || minutes > 23 * 60 + 59) throw new FormatException();

      var session = new PlannedSession
      {
        Time = TimeSpan.FromMinutes(minutes),
        SessionType = type,
        Start = start,
        TeamEvent = team,
        DurationMinutes = (int)Bounded(Varint(payload, ref position), 1, 600),
        AdditionalLaps = timed ? 0 : (int)Bounded(Varint(payload, ref position), 0, 10),
        WaveGapSeconds = start == PlannedStartMode.Waves ? (int)Bounded(Varint(payload, ref position), 10, 1800) : 60
      };

      var classCount = Bounded(Varint(payload, ref position), 0, MaxClassesPerSession);
      if (team && classCount > 0) throw new FormatException();
      for (var c = 0; c < classCount; c++)
      {
        var name = String(Varint(payload, ref position));
        if (string.IsNullOrWhiteSpace(name)) throw new FormatException();
        session.Classes.Add(name);
      }
      if (session.Classes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != session.Classes.Count)
        throw new FormatException();

      var label = String(Varint(payload, ref position));
      var suffix = ClassesSuffix(session.Classes);
      session.OverallGroup = groupKind switch
      {
        GroupKind.SameAsClasses when session.Classes.Count > 0 => suffix,
        GroupKind.Text => String(Varint(payload, ref position)) is { } g && !string.IsNullOrWhiteSpace(g)
          ? g
          : throw new FormatException(),
        GroupKind.None => null,
        _ => throw new FormatException()
      };

      session.Name = nameKind switch
      {
        NameKind.Full => label,
        NameKind.LabelAndClasses when session.Classes.Count > 0 => label + " - " + suffix,
        NameKind.LabelAndGroup when session.OverallGroup != null => label + " - " + session.OverallGroup,
        _ => throw new FormatException()
      };

      plan.Sessions.Add(session);
    }

    // The extension block: tagged additions from later versions. An odd tag
    // may be passed over by a version that does not know it; an even one
    // changes what the plan means, and only a newer version can read it.
    while (true)
    {
      var tag = Varint(payload, ref position);
      if (tag == 0) break;
      if (tag % 2 == 0) throw new NewerVersionException();
      var size = Varint(payload, ref position);
      if (position + size > payload.Length) throw new FormatException();
      position += (int)size;
    }

    if (position != payload.Length) throw new FormatException();
    return plan;
  }

  /// <summary>The words on the line around a code, without the code: what the club called it.</summary>
  private static string LineAround(string text, int start, int end)
  {
    var lineStart = text.LastIndexOfAny(new[] { '\n', '\r' }, Math.Max(0, start - 1)) + 1;
    if (start == 0) lineStart = 0;
    var lineEnd = text.IndexOfAny(new[] { '\n', '\r' }, Math.Min(end, text.Length));
    if (lineEnd < 0) lineEnd = text.Length;

    var before = text[lineStart..start];
    var after = end < lineEnd ? text[end..lineEnd] : "";
    var words = (before + " " + after).Trim().Trim(':', '=', '-', '–', '—', '|', '*', '"', '\'', ',', ';').Trim();
    return words;
  }

  // ---- Short codes ----------------------------------------------------------

  /// <summary>
  /// The short code of a plan code: "K7Q2-MX4P". The first 40 bits of the
  /// SHA-256 of the code as written, so the same plan always has the same
  /// short code, worked out the same way here and on the website without
  /// either asking the other. The website keeps the long code under it.
  /// </summary>
  public static string ShortCodeOf(string code)
  {
    var hash = SHA256.HashData(Encoding.ASCII.GetBytes(Normalise(code)));
    var chars = ToBase32(hash.Take(5).ToList());
    return chars[..4] + "-" + chars[4..8];
  }

  /// <summary>The code as a fingerprint: capitals, letters and digits only, O/I/L read as 0/1/1.</summary>
  public static string Normalise(string code)
  {
    var b = new StringBuilder(code.Length);
    foreach (var ch in code)
    {
      if (!char.IsAsciiLetterOrDigit(ch)) continue;
      var c = char.ToUpperInvariant(ch);
      b.Append(c switch { 'O' => '0', 'I' or 'L' => '1', _ => c });
    }
    return b.ToString();
  }

  /// <summary>
  /// The short code in <paramref name="text"/>, when that is all it holds:
  /// eight letters and digits, a dash or a space in the middle or not.
  /// </summary>
  public static string? AsShortCode(string text)
  {
    var chars = new StringBuilder();
    foreach (var ch in text)
    {
      if (IsSeparator(ch)) continue;
      var v = Value(ch);
      if (v < 0) return null;
      chars.Append(Alphabet[v]);
    }
    return chars.Length == 8 ? chars.ToString(0, 4) + "-" + chars.ToString(4, 4) : null;
  }

  /// <summary>Short codes in a list: "XXXX-XXXX" standing on its own, with the dash.</summary>
  public static List<(string ShortCode, string LineText)> FindShortCodes(string text)
  {
    var found = new List<(string, string)>();
    foreach (var raw in text.Split('\n'))
    {
      var line = raw.TrimEnd('\r');
      foreach (var token in line.Split(new[] { ' ', '\t', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
      {
        var t = token.Trim('*', '"', '\'', '(', ')', '[', ']', ':', '.');
        if (t.Length != 9 || t[4] != '-') continue;
        if (t.Where((c, i) => i != 4).Any(c => Value(c) < 0)) continue;
        if (!t.Any(char.IsDigit) || !t.Any(char.IsLetter)) continue;
        if (FindPrefix(t, 0) == 0) continue;
        found.Add((AsShortCode(t)!, line.Replace(token, "").Trim().Trim(':', '=', '-', '|', '*').Trim()));
      }
    }
    return found;
  }

  // ---- For the tests: codes from hand-made payloads -------------------------------

  /// <summary>The payload of a code - version to extension block - without its CRC.</summary>
  internal static byte[] PayloadOf(string code)
  {
    var chars = new StringBuilder();
    foreach (var ch in code[(FindPrefix(code, 0) + 4)..])
      if (!IsSeparator(ch)) chars.Append(Alphabet[Value(ch)]);
    var bytes = FromBase32(chars.ToString(), exact: true);
    return bytes[..^2];
  }

  /// <summary>A code around any payload, with a correct CRC: what a later version might write.</summary>
  internal static string Wrap(byte[] payload)
  {
    var bytes = payload.ToList();
    var crc = Crc16(bytes);
    bytes.Add((byte)(crc >> 8));
    bytes.Add((byte)crc);
    var chars = ToBase32(bytes);
    var blocks = new List<string> { Prefix };
    for (var i = 0; i < chars.Length; i += 5) blocks.Add(chars.Substring(i, Math.Min(5, chars.Length - i)));
    return string.Join("-", blocks);
  }

  internal static byte[] Varint(uint value)
  {
    var bytes = new List<byte>();
    WriteVarint(bytes, value);
    return bytes.ToArray();
  }

  // ---- Bits and pieces --------------------------------------------------------

  private static bool IsSeparator(char c) =>
    char.IsWhiteSpace(c) || c is '-' or '‐' or '‑' or '‒' or '–' or '—' or '_' or '*' or '"' or '\'' or '“' or '”' or
      '„' or '​' or '‌' or '‍' or '⁠' or '﻿' or ' ';

  /// <summary>A character's value in Crockford Base32, or -1. O reads as 0, I and L as 1.</summary>
  private static int Value(char c)
  {
    c = char.ToUpperInvariant(c);
    if (c == 'O') return 0;
    if (c is 'I' or 'L') return 1;
    return Alphabet.IndexOf(c);
  }

  private static string ToBase32(IReadOnlyList<byte> bytes)
  {
    var b = new StringBuilder((bytes.Count * 8 + 4) / 5);
    int buffer = 0, bits = 0;
    foreach (var value in bytes)
    {
      buffer = (buffer << 8) | value;
      bits += 8;
      while (bits >= 5)
      {
        b.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
        bits -= 5;
      }
    }
    if (bits > 0) b.Append(Alphabet[(buffer << (5 - bits)) & 31]);
    return b.ToString();
  }

  /// <param name="exact">The whole code: the bits left over at the end must be zero, as the encoder leaves them.</param>
  private static byte[] FromBase32(string chars, bool exact)
  {
    var bytes = new List<byte>(chars.Length * 5 / 8);
    int buffer = 0, bits = 0;
    foreach (var c in chars)
    {
      var v = Alphabet.IndexOf(c);
      if (v < 0) throw new FormatException();
      buffer = (buffer << 5) | v;
      bits += 5;
      if (bits >= 8)
      {
        bytes.Add((byte)(buffer >> (bits - 8)));
        bits -= 8;
      }
      buffer &= (1 << bits) - 1;
    }
    if (exact && (bits >= 5 || buffer != 0)) throw new FormatException();
    return bytes.ToArray();
  }

  private static ushort Crc16(IEnumerable<byte> bytes)
  {
    ushort crc = 0xFFFF;
    foreach (var b in bytes)
    {
      crc ^= (ushort)(b << 8);
      for (var i = 0; i < 8; i++)
        crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
    }
    return crc;
  }

  private static void WriteVarint(List<byte> to, uint value)
  {
    while (value >= 0x80)
    {
      to.Add((byte)(value | 0x80));
      value >>= 7;
    }
    to.Add((byte)value);
  }

  private static bool TryReadVarint(byte[] from, ref int position, out uint value)
  {
    value = 0;
    var start = position;
    for (var shift = 0; shift < 35; shift += 7)
    {
      if (position >= from.Length)
      {
        position = start;
        return false;
      }
      var b = from[position++];
      value |= (uint)(b & 0x7F) << shift;
      if ((b & 0x80) == 0) return true;
    }
    throw new FormatException();
  }

  private static uint Varint(byte[] from, ref int position) =>
    TryReadVarint(from, ref position, out var value) ? value : throw new FormatException();

  private static uint Bounded(uint value, uint min, uint max) =>
    value >= min && value <= max ? value : throw new FormatException();

  private static uint Zigzag(int value) => (uint)((value << 1) ^ (value >> 31));
  private static int Unzigzag(uint value) => (int)(value >> 1) ^ -(int)(value & 1);

  private static string Nfc(string? text) => (text ?? "").Normalize(NormalizationForm.FormC);

  /// <summary>Strings in the order they are first used; the same one twice is written once.</summary>
  private sealed class StringTable
  {
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);
    public List<string> Items { get; } = new();

    public int Index(string text)
    {
      if (_index.TryGetValue(text, out var index)) return index;
      index = Items.Count;
      Items.Add(text);
      _index[text] = index;
      return index;
    }
  }

  /// <summary>"8 sessions, 08:30-13:30" - a code's contents in one line, for lists and the paste box.</summary>
  public static string Summary(DayPlan plan)
  {
    var count = plan.Sessions.Count;
    if (count == 0) return "no sessions";
    var first = plan.Sessions.Min(s => s.Time);
    var last = plan.Sessions.Max(s => s.Time);
    var times = first == last
      ? first.ToString(@"hh\:mm", CultureInfo.InvariantCulture)
      : $"{first.ToString(@"hh\:mm", CultureInfo.InvariantCulture)}-{last.ToString(@"hh\:mm", CultureInfo.InvariantCulture)}";
    return $"{count} session{(count == 1 ? "" : "s")}, {times}";
  }
}
