using System.Text.RegularExpressions;

namespace CrossMgrInterface;

/// <summary>
/// One person on a team, as the rider list names them.
///
/// A member owns a list of transponders rather than one: a spare tag listed on a
/// second row, or a stray joined to the member later, is still that member.
/// Several members may also list the same transponder - which is how teams were
/// entered before team events existed - and then their laps cannot be told apart
/// (see <see cref="TransponderGroup"/>).
///
/// Immutable, so a rider snapshot or a display copy can share the instance.
/// </summary>
public sealed class TeamMember
{
  public string RiderNumber { get; init; } = "";
  public string FirstName { get; init; } = "";
  public string LastName { get; init; } = "";
  public string Category { get; init; } = "";
  public IReadOnlyList<string> Transponders { get; init; } = Array.Empty<string>();

  /// <summary>See <see cref="RiderInfo.ShowName"/>.</summary>
  public bool? ShowName { get; init; }

  public string Name => $"{FirstName} {LastName}".Trim();

  /// <summary>"#14 Ben Fischer", falling back the way <see cref="RiderInfo.Label"/> does.</summary>
  public string Label
  {
    get
    {
      var hasNumber = RiderNumber.Length > 0;
      if (hasNumber && Name.Length > 0) return $"#{RiderNumber} {Name}";
      if (hasNumber) return $"#{RiderNumber}";
      if (Name.Length > 0) return Name;
      return Transponders.Count > 0 ? Transponders[0] : "unnamed rider";
    }
  }

  /// <summary>"#14 Fischer": for where a team's riders share one line.</summary>
  public string ShortLabel => RiderNumber.Length > 0 && LastName.Length > 0 ? $"#{RiderNumber} {LastName}" : Label;

  public bool Owns(string? transponder) =>
    transponder != null &&
    Transponders.Any(t => string.Equals(t, transponder, StringComparison.OrdinalIgnoreCase));

  /// <summary>A copy that also owns <paramref name="transponder"/>.</summary>
  public TeamMember WithTransponder(string transponder) => Owns(transponder)
    ? this
    : new TeamMember
    {
      RiderNumber = RiderNumber,
      FirstName = FirstName,
      LastName = LastName,
      Category = Category,
      ShowName = ShowName,
      Transponders = Transponders.Append(transponder).ToList()
    };

  /// <summary>Who this is, for matching the same person across two imports or two rows.</summary>
  internal string PersonKey => TeamRoster.PersonKey(RiderNumber, Name);
}

/// <summary>
/// Members whose laps cannot be told apart because they share a transponder.
///
/// A member with a transponder of their own is a group of one, and every lap
/// read from it is theirs. A group of several - the whole team on one tag, or
/// two of three members sharing - still scores for the team, but nothing can
/// say which of them rode a lap, so it gets no per-rider times and can never be
/// suspected of having two riders out.
/// </summary>
public sealed class TransponderGroup
{
  public IReadOnlyList<TeamMember> Members { get; }
  public IReadOnlyList<string> Transponders { get; }

  public bool IsShared => Members.Count > 1;

  private TransponderGroup(IReadOnlyList<TeamMember> members)
  {
    Members = members;
    Transponders = members.SelectMany(m => m.Transponders)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  public bool Owns(string? transponder) =>
    transponder != null &&
    Transponders.Any(t => string.Equals(t, transponder, StringComparison.OrdinalIgnoreCase));

  /// <summary>"#21 Carla Hoff / #22 David Kern".</summary>
  public string Label => string.Join(" / ", Members.Select(m => m.Label));

  /// <summary>Which of <paramref name="groups"/> owns a transponder, or -1 for none or unknown.</summary>
  public static int IndexOf(IReadOnlyList<TransponderGroup> groups, string? transponder)
  {
    if (transponder == null) return -1;
    for (var i = 0; i < groups.Count; i++)
      if (groups[i].Owns(transponder)) return i;
    return -1;
  }

  /// <summary>
  /// Splits a team into groups of members linked by a shared transponder, in the
  /// order their first member appears. Sharing is transitive: if A shares with B
  /// and B with C, all three are one group.
  /// </summary>
  public static IReadOnlyList<TransponderGroup> Of(IReadOnlyList<TeamMember>? members)
  {
    if (members == null || members.Count == 0) return Array.Empty<TransponderGroup>();

    var parent = Enumerable.Range(0, members.Count).ToArray();
    int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);

    for (var i = 0; i < members.Count; i++)
      for (var j = i + 1; j < members.Count; j++)
        if (members[i].Transponders.Any(members[j].Owns))
          parent[Find(j)] = Find(i);

    return Enumerable.Range(0, members.Count)
      .GroupBy(Find)
      .OrderBy(g => g.Min())
      .Select(g => new TransponderGroup(g.OrderBy(i => i).Select(i => members[i]).ToList()))
      .ToList();
  }
}

/// <summary>A team as the rider list describes it, before or after it has crossed the loop.</summary>
public sealed class TeamEntry
{
  public string Key { get; init; } = "";
  public string Name { get; init; } = "";
  public string Category { get; init; } = "";
  public IReadOnlyList<TeamMember> Members { get; init; } = Array.Empty<TeamMember>();

  public string NumbersText => NumbersOf(Members);

  public bool SharesTransponder => TransponderGroup.Of(Members).Any(g => g.IsShared);

  /// <summary>
  /// The scored entry for this team.
  ///
  /// The team name goes in FirstName deliberately: every place that shows a
  /// rider composes first and last name, so a team reads as its name everywhere
  /// without each of them learning about teams. RiderNumber carries the members'
  /// numbers, so the label reads "#11/14 MSC Adler".
  /// </summary>
  public RiderInfo ToRiderInfo() => new()
  {
    TagID = Key,
    RiderNumber = NumbersText,
    FirstName = Name,
    LastName = "",
    Team = Name,
    Category = Category,
    Members = Members
  };

  /// <summary>The members' start numbers, lowest first: "11/14".</summary>
  public static string NumbersOf(IEnumerable<TeamMember> members) =>
    string.Join("/", members
      .Select(m => m.RiderNumber)
      .Where(n => n.Length > 0)
      .Distinct()
      .OrderBy(n => int.TryParse(n, out var value) ? value : int.MaxValue)
      .ThenBy(n => n, StringComparer.OrdinalIgnoreCase));
}

public enum TeamRosterSeverity
{
  Warning,
  Error
}

public sealed record TeamRosterIssue(TeamRosterSeverity Severity, string Message);

/// <summary>
/// Which riders on the list race as a team in a team event, and which transponder
/// belongs to which team.
///
/// Rows that share a team name form a team; a name used by one rider only, or no
/// name, is a solo rider racing as they always have. This is only ever built for
/// a session the operator has marked as a team event: on an ordinary rider list
/// the team column holds club names, and grouping by it would score thirty-five
/// club members as one rider.
///
/// A team is scored as one entry under a key of its own (<see cref="KeyFor"/>),
/// never under a member's transponder - the key does not depend on which member
/// crosses first or on the order of the list, and a stored entry says plainly
/// that it is a team.
/// </summary>
public sealed class TeamRoster
{
  public const string KeyPrefix = "TEAM:";

  /// <summary>
  /// Above this many riders a "team" is more likely a club name typed into the
  /// team column than a team taking turns on one bike, so the import says so.
  /// </summary>
  public const int LargeTeamSize = 6;

  public static TeamRoster Empty { get; } = new(
    Array.Empty<TeamEntry>(), Array.Empty<RiderDataImporter.RiderImportData>(), Array.Empty<TeamRosterIssue>());

  private readonly Dictionary<string, TeamEntry> _byKey = new(StringComparer.Ordinal);
  private readonly Dictionary<string, string> _keyByTransponder = new(StringComparer.OrdinalIgnoreCase);

  public IReadOnlyList<TeamEntry> Teams { get; }
  public IReadOnlyList<RiderDataImporter.RiderImportData> Solos { get; }
  public IReadOnlyList<TeamRosterIssue> Issues { get; }

  public bool HasErrors => Issues.Any(i => i.Severity == TeamRosterSeverity.Error);
  public int EntryCount => Teams.Count + Solos.Count;

  public string Summary =>
    $"{Teams.Count} {(Teams.Count == 1 ? "team" : "teams")}, " +
    $"{Solos.Count} solo {(Solos.Count == 1 ? "rider" : "riders")}";

  private TeamRoster(IReadOnlyList<TeamEntry> teams, IReadOnlyList<RiderDataImporter.RiderImportData> solos,
    IReadOnlyList<TeamRosterIssue> issues)
  {
    Teams = teams;
    Solos = solos;
    Issues = issues;

    foreach (var team in teams)
    {
      _byKey[team.Key] = team;
      foreach (var member in team.Members)
        foreach (var transponder in member.Transponders)
          _keyByTransponder.TryAdd(transponder, team.Key);
    }
  }

  // ---- Lookups ---------------------------------------------------------------

  /// <summary>The team key a transponder scores for, or null for a solo or unknown transponder.</summary>
  public string? EntryKeyFor(string transponder) =>
    _keyByTransponder.TryGetValue(transponder, out var key) ? key : null;

  public TeamEntry? TeamFor(string key) => _byKey.TryGetValue(key, out var team) ? team : null;

  /// <summary>The member a transponder belongs to, in whichever team.</summary>
  public TeamMember? MemberFor(string transponder)
  {
    var team = EntryKeyFor(transponder) is { } key ? TeamFor(key) : null;
    return team?.Members.FirstOrDefault(m => m.Owns(transponder));
  }

  public static string KeyFor(string teamName) => KeyPrefix + NormaliseName(teamName).ToUpperInvariant();

  public static bool IsTeamKey(string? key) => key != null && key.StartsWith(KeyPrefix, StringComparison.Ordinal);

  /// <summary>Trimmed, with runs of spaces collapsed, so "RC  Falke " and "RC Falke" are one team.</summary>
  public static string NormaliseName(string? name) =>
    string.IsNullOrWhiteSpace(name) ? "" : Regex.Replace(name.Trim(), @"\s+", " ");

  internal static string PersonKey(string riderNumber, string name) =>
    $"{riderNumber.Trim()}|{NormaliseName(name).ToUpperInvariant()}";

  // ---- Building ------------------------------------------------------------

  /// <summary>
  /// Groups a rider list into teams and solo riders.
  ///
  /// Takes every imported row, not the importer's lookup by transponder: that
  /// lookup keeps only the last row for a shared transponder, which is exactly
  /// the shape of a team entered on one tag.
  /// </summary>
  public static TeamRoster Build(IEnumerable<RiderDataImporter.RiderImportData> rows)
  {
    var list = rows.Where(r => !string.IsNullOrWhiteSpace(r.TagID)).ToList();
    var issues = new List<TeamRosterIssue>();

    // A name is a team when it covers two different people. The same person on
    // two rows - a spare transponder - does not make a team.
    var teamKeys = list
      .Where(r => NormaliseName(r.Team).Length > 0)
      .GroupBy(r => KeyFor(r.Team))
      .Where(g => g.Select(RowPerson).Distinct().Count() >= 2)
      .Select(g => g.Key)
      .ToHashSet(StringComparer.Ordinal);

    // The first row to name a transponder keeps it. A later row naming it for a
    // different team, or for a different solo rider, cannot be scored: a read of
    // that transponder would have to count for two entries at once.
    var owner = new Dictionary<string, (string Entry, string Who)>(StringComparer.OrdinalIgnoreCase);
    var teamOrder = new List<string>();
    var teamRows = new Dictionary<string, List<RiderDataImporter.RiderImportData>>(StringComparer.Ordinal);
    var solos = new List<RiderDataImporter.RiderImportData>();

    foreach (var row in list)
    {
      var transponder = row.TagID.Trim();
      var teamKey = KeyFor(row.Team);
      var isTeam = teamKeys.Contains(teamKey);
      var entry = isTeam ? teamKey : $"SOLO:{transponder.ToUpperInvariant()}|{RowPerson(row)}";

      if (owner.TryGetValue(transponder, out var existing))
      {
        if (existing.Entry != entry)
        {
          issues.Add(new TeamRosterIssue(TeamRosterSeverity.Error,
            $"Transponder {transponder} is on two rows: {existing.Who} and {Describe(row)}. " +
            "A transponder can belong to one team or one solo rider, not both."));
          continue;
        }

        // The same solo rider listed twice: nothing new to add.
        if (!isTeam) continue;
      }
      else
      {
        owner[transponder] = (entry, Describe(row));
      }

      if (!isTeam)
      {
        solos.Add(row);
        continue;
      }

      if (!teamRows.TryGetValue(teamKey, out var members))
      {
        teamRows[teamKey] = members = new List<RiderDataImporter.RiderImportData>();
        teamOrder.Add(teamKey);
      }
      members.Add(row);
    }

    var teams = teamOrder.Select(key => BuildTeam(key, teamRows[key], issues)).ToList();
    return new TeamRoster(teams, solos, issues);
  }

  private static TeamEntry BuildTeam(string key, List<RiderDataImporter.RiderImportData> rows,
    List<TeamRosterIssue> issues)
  {
    var name = NormaliseName(rows[0].Team);

    var members = rows
      .GroupBy(RowPerson)
      .Select(person =>
      {
        var first = person.First();
        return new TeamMember
        {
          RiderNumber = first.RiderNumber.Trim(),
          FirstName = first.FirstName.Trim(),
          LastName = first.LastName.Trim(),
          Category = first.Category.Trim(),
          ShowName = person.Select(r => r.ShowName).FirstOrDefault(v => v.HasValue),
          Transponders = person.Select(r => r.TagID.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
        };
      })
      .ToList();

    // The class the team is scored in: the one most of its members ride in, and
    // on a tie the first row's, so the choice is predictable from the list.
    var classes = members
      .Select(m => m.Category)
      .Where(c => c.Length > 0)
      .ToList();
    var category = classes
      .GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
      .OrderByDescending(g => g.Count())
      .ThenBy(g => classes.FindIndex(c => string.Equals(c, g.Key, StringComparison.OrdinalIgnoreCase)))
      .Select(g => g.First())
      .FirstOrDefault() ?? "";

    if (classes.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
      issues.Add(new TeamRosterIssue(TeamRosterSeverity.Warning,
        $"{name} has riders in more than one class ({string.Join(", ", classes.Distinct(StringComparer.OrdinalIgnoreCase))}). " +
        $"The team is scored in {category}."));

    if (members.Count > LargeTeamSize)
      issues.Add(new TeamRosterIssue(TeamRosterSeverity.Warning,
        $"{name} has {members.Count} riders. If that is a club rather than a team, " +
        "this rider list is not meant for a team event."));

    var groups = TransponderGroup.Of(members);
    if (groups.Count > 1 && groups.Any(g => g.IsShared))
      issues.Add(new TeamRosterIssue(TeamRosterSeverity.Warning,
        $"{name}: {string.Join(" and ", groups.Where(g => g.IsShared).Select(g => g.Label))} share a transponder " +
        "while the others have their own. Laps on the shared one cannot be split per rider."));

    foreach (var number in members.Where(m => m.RiderNumber.Length > 0)
               .GroupBy(m => m.RiderNumber)
               .Where(g => g.Count() > 1))
      issues.Add(new TeamRosterIssue(TeamRosterSeverity.Warning,
        $"{name}: number {number.Key} is on rows with different names ({string.Join(", ", number.Select(m => m.Name))})."));

    return new TeamEntry { Key = key, Name = name, Category = category, Members = members };
  }

  /// <summary>
  /// This roster with the teams already on the loop folded in.
  ///
  /// A team that is racing keeps every transponder it has, even when a rider
  /// list imported mid-race says otherwise, so a re-import can never move a
  /// member's reads away from the laps they have ridden. A team the list does
  /// not name at all - renamed, or no list loaded after a restart - is kept from
  /// what was stored.
  /// </summary>
  public TeamRoster WithLiveEntries(IEnumerable<RiderInfo> liveEntries)
  {
    var live = liveEntries.Where(r => r.IsTeam).ToList();
    if (live.Count == 0) return this;

    var issues = Issues.ToList();
    var teams = Teams.ToList();

    foreach (var entry in live)
    {
      var index = teams.FindIndex(t => t.Key == entry.TagID);
      if (index < 0)
      {
        teams.Add(new TeamEntry
        {
          Key = entry.TagID,
          Name = entry.FirstName,
          Category = entry.Category,
          Members = entry.Members!
        });
        continue;
      }

      var merged = teams[index].Members.ToList();
      foreach (var liveMember in entry.Members!)
      {
        var at = merged.FindIndex(m => m.PersonKey == liveMember.PersonKey);
        if (at < 0)
        {
          merged.Add(liveMember);
          continue;
        }

        foreach (var transponder in liveMember.Transponders)
          merged[at] = merged[at].WithTransponder(transponder);
      }

      var listed = teams[index];
      teams[index] = new TeamEntry { Key = listed.Key, Name = listed.Name, Category = listed.Category, Members = merged };
    }

    // Transponders of racing teams win over the list: take them away from any
    // other team or solo row that now claims them.
    var liveTransponders = live
      .SelectMany(e => e.Members!.SelectMany(m => m.Transponders).Select(t => (Transponder: t, Key: e.TagID)))
      .GroupBy(x => x.Transponder, StringComparer.OrdinalIgnoreCase)
      .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < teams.Count; i++)
    {
      var team = teams[i];
      var kept = team.Members
        .Select(m => new TeamMember
        {
          RiderNumber = m.RiderNumber,
          FirstName = m.FirstName,
          LastName = m.LastName,
          Category = m.Category,
          ShowName = m.ShowName,
          Transponders = m.Transponders
            .Where(t => !liveTransponders.TryGetValue(t, out var owner) || owner == team.Key)
            .ToList()
        })
        .ToList();

      var lost = team.Members.SelectMany(m => m.Transponders).Count() - kept.SelectMany(m => m.Transponders).Count();
      if (lost == 0) continue;

      issues.Add(new TeamRosterIssue(TeamRosterSeverity.Warning,
        $"{team.Name}: {lost} transponder(s) on the rider list already score for another team that is racing, and stay with it."));
      teams[i] = new TeamEntry
      {
        Key = team.Key,
        Name = team.Name,
        Category = team.Category,
        Members = kept.Where(m => m.Transponders.Count > 0).ToList()
      };
    }

    var solos = Solos.Where(s => !liveTransponders.ContainsKey(s.TagID.Trim())).ToList();

    return new TeamRoster(teams, solos, issues);
  }

  private static string RowPerson(RiderDataImporter.RiderImportData row) =>
    PersonKey(row.RiderNumber, $"{row.FirstName} {row.LastName}");

  private static string Describe(RiderDataImporter.RiderImportData row)
  {
    var name = $"{row.FirstName} {row.LastName}".Trim();
    var who = row.RiderNumber.Trim().Length > 0
      ? (name.Length > 0 ? $"#{row.RiderNumber.Trim()} {name}" : $"#{row.RiderNumber.Trim()}")
      : (name.Length > 0 ? name : "a row with no name");
    var team = NormaliseName(row.Team);
    return team.Length > 0 ? $"{who} ({team})" : who;
  }
}
