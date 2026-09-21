namespace CrossMgrInterface;

/// <summary>How a hidden name is written on the website.</summary>
public enum NameStyle
{
  /// <summary>"Lena B." - the rider still recognises themselves; a search for the full name does not.</summary>
  FirstNameInitial,

  /// <summary>"Len*** Bra***" - the first three letters of each part, the rest starred.</summary>
  Starred
}

/// <summary>
/// What a rider's name becomes on the website when they would rather not be
/// listed in full.
///
/// Applied only to what leaves the laptop. The screen, the printed sheet and
/// the Excel export keep full names: the operator has to be able to tell
/// riders apart on race day, and a sheet handed to the rider is not public.
///
/// Never empty. A row that only says "#42" is no use to the rider it is
/// about - they still want to find themselves - so a hidden name is a
/// shortened one, not a missing one.
/// </summary>
public static class NamePrivacy
{
  /// <summary>Stars everything past this many characters of each part.</summary>
  public const int StarredKeeps = 3;

  /// <summary>
  /// Whether this rider's name goes out in full. Their own choice from the
  /// rider list wins; a rider the list said nothing about follows the club's
  /// default.
  /// </summary>
  public static bool Shows(RiderInfo rider, bool showByDefault) => rider.ShowName ?? showByDefault;

  /// <summary>The name to publish for a solo rider.</summary>
  public static string Publish(RiderInfo rider, bool showByDefault, NameStyle style)
  {
    if (rider.IsTeam) return rider.FirstName;   // a club, not a person; see TeamRoster
    var full = $"{rider.FirstName} {rider.LastName}".Trim();
    return Shows(rider, showByDefault) ? full : Hide(rider.FirstName, rider.LastName, style);
  }

  /// <summary>The name to publish for a team member.</summary>
  public static string Publish(TeamMember member, bool showByDefault, NameStyle style)
  {
    var shows = member.ShowName ?? showByDefault;
    return shows ? $"{member.FirstName} {member.LastName}".Trim() : Hide(member.FirstName, member.LastName, style);
  }

  public static string Hide(string firstName, string lastName, NameStyle style)
  {
    firstName = (firstName ?? "").Trim();
    lastName = (lastName ?? "").Trim();

    if (firstName.Length == 0 && lastName.Length == 0) return "";

    return style switch
    {
      NameStyle.Starred => Join(Star(firstName), Star(lastName)),
      _ => Join(firstName, Initial(lastName))
    };
  }

  private static string Initial(string name) => name.Length == 0 ? "" : name[..1] + ".";

  private static string Star(string name) =>
    name.Length <= StarredKeeps ? name : name[..StarredKeeps] + new string('*', name.Length - StarredKeeps);

  private static string Join(string a, string b) => $"{a} {b}".Trim();

  /// <summary>
  /// Reads a yes/no from the rider list. Generous, because these come from
  /// whatever a club secretary typed: yes/no, ja/nein, true/false, 1/0, x.
  /// Anything else - including blank - means "not said", so the default applies.
  /// </summary>
  public static bool? ParseShowName(string? value)
  {
    var v = (value ?? "").Trim().ToLowerInvariant();
    if (v.Length == 0) return null;
    if (v is "yes" or "y" or "ja" or "j" or "true" or "1" or "x" or "public" or "show") return true;
    if (v is "no" or "n" or "nein" or "false" or "0" or "private" or "hide" or "hidden") return false;
    return null;
  }
}
