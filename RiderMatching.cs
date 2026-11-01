namespace CrossMgrInterface;

/// <summary>Who a row of a session's sheet is: enough to find the same rider in another session.</summary>
public readonly record struct RiderKey(string TagID, string? Number, string? Name, string? Category, bool IsTeam)
{
  public static RiderKey Of(RiderResult row) =>
    new(row.TagID, row.RiderNumber, row.RiderName, row.Category, row.IsTeam);
}

/// <summary>
/// One rider followed through several sessions: the transponders they were
/// read on, the names and number they were entered under, and which sessions
/// they have been found in already. The overall's entries and the day's list
/// of every class both build on it, so a rider is the same rider in both.
/// </summary>
public abstract class MatchedRider
{
  private readonly bool[] _rode;
  private readonly HashSet<string> _tags = new(StringComparer.OrdinalIgnoreCase);
  private readonly List<string> _names = new();
  private string _number = "";

  protected MatchedRider(int sessionCount)
  {
    _rode = new bool[sessionCount];
  }

  public string Category { get; set; } = "";

  /// <summary>The number as the latest session had it, untrimmed, for printing.</summary>
  public string RawNumber => _number;
  public string Number => _number.Trim();

  /// <summary>The name as the latest session had it: the one last corrected.</summary>
  public string Name { get; private set; } = "";
  public IReadOnlyList<string> Names => _names;
  public bool IsTeam { get; private set; }
  public string DisplayName => Name.Length > 0 ? Name : $"transponder {_tags.FirstOrDefault()}";

  /// <summary>Set when another rider of the class has the same number. See <see cref="RiderMatching.NoteSharedNumbers"/>.</summary>
  public string? SharedNumber { get; set; }

  public bool Has(int session) => _rode[session];
  public bool HasTag(string tag) => _tags.Contains(tag);
  public IEnumerable<string> Tags => _tags;

  public bool HasName(string? name) =>
    !string.IsNullOrWhiteSpace(name) && _names.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);

  /// <summary>Notes the rider as found in <paramref name="session"/> under <paramref name="key"/>.</summary>
  protected void Record(int session, RiderKey key)
  {
    _rode[session] = true;
    _tags.Add(key.TagID);

    // The latest session has the name as last corrected, so it wins.
    _number = key.Number ?? "";
    if (!string.IsNullOrWhiteSpace(key.Name))
    {
      Name = key.Name.Trim();
      if (!_names.Contains(Name, StringComparer.OrdinalIgnoreCase)) _names.Add(Name);
    }
    IsTeam = key.IsTeam;
  }
}

/// <summary>
/// Finds a rider of one session among the riders of the sessions before it.
///
/// On a race day each rider has their own transponder, so the transponder says
/// who it is; a team is followed by its team key the same way. A rider whose
/// transponder failed rides the next session on a spare, and is found by start
/// number and name instead - but only while their old transponder is not out in
/// this session too. The number alone is not enough: at a club day it can be on
/// two riders. On 31 October 2026 two beginners were both #231, matching by
/// number put the second over the first, and a rider on the podium of both
/// motos was missing from the overall.
/// </summary>
public static class RiderMatching
{
  /// <summary>
  /// The rider this row continues, or null for one not seen before. Never one
  /// already found in this session: two rows of one sheet are two riders.
  ///
  /// <paramref name="acrossClasses"/> is for a list of every class of the day,
  /// where a rider is followed whatever class they rode. There a club's spare
  /// transponder can go from a child in one class to a parent in another, so a
  /// transponder seen before under another name in another class is someone
  /// else. Within one class a changed name is a corrected one.
  /// </summary>
  public static T? Find<T>(IEnumerable<T> riders, RiderKey row, int session, IReadOnlySet<string> tagsInSession,
    bool acrossClasses = false) where T : MatchedRider
  {
    var open = riders
      .Where(r => !r.Has(session) && (acrossClasses || SameClass(r.Category, row.Category)))
      .ToList();

    var sameTag = open.FirstOrDefault(r => r.IsTeam == row.IsTeam && r.HasTag(row.TagID)
      && (!acrossClasses || !LentTo(r, row)));
    if (sameTag != null || row.IsTeam) return sameTag;

    var number = row.Number?.Trim() ?? "";
    if (number.Length == 0 || string.IsNullOrWhiteSpace(row.Name)) return null;

    return open.FirstOrDefault(r => !r.IsTeam
      && string.Equals(r.Number, number, StringComparison.OrdinalIgnoreCase)
      && r.HasName(row.Name)
      && !r.Tags.Any(tagsInSession.Contains));
  }

  private static bool SameClass(string? a, string? b) =>
    string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

  private static bool LentTo(MatchedRider rider, RiderKey row) =>
    !SameClass(rider.Category, row.Category)
    && !string.IsNullOrWhiteSpace(row.Name) && rider.Names.Count > 0 && !rider.HasName(row.Name);

  /// <summary>
  /// Riders of one class kept apart under one number are each told about the
  /// other. Per class: the same number in two classes is two riders, as the
  /// club meant.
  /// </summary>
  public static void NoteSharedNumbers<T>(IEnumerable<T> riders) where T : MatchedRider
  {
    var groups = riders.Where(r => !r.IsTeam && r.Number.Length > 0)
      .GroupBy(r => (Category: r.Category.ToUpperInvariant(), Number: r.Number.ToUpperInvariant()))
      .Where(g => g.Count() > 1);

    foreach (var group in groups)
    {
      var names = string.Join(" and ", group.Select(r => r.DisplayName));
      foreach (var rider in group)
        rider.SharedNumber = $"Number {rider.Number} was given to {names} - scored as different riders. " +
                             "Check the rider list.";
    }
  }
}
