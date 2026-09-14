namespace CrossMgrInterface;

/// <summary>
/// Decides which scored entry a transponder read belongs to.
///
/// Usually the transponder is its own entry. It is not when the operator merged
/// a stray transponder onto a rider (an alias), or when it belongs to a member
/// of a team in a team event. Either way the transponder that was actually read
/// is kept as well, because for a team that is the only record of which member
/// crossed the line.
/// </summary>
public static class CrossingResolver
{
  /// <param name="transponder">The code the reader sent.</param>
  /// <param name="aliases">Stray transponder to the entry the operator merged it onto.</param>
  /// <param name="teams">The team event's roster; <see cref="TeamRoster.Empty"/> when there are no teams.</param>
  /// <param name="entryExists">Whether an entry is being scored under that key right now.</param>
  public static (string EntryKey, string CrossedBy) Resolve(string transponder,
    IReadOnlyDictionary<string, string> aliases, TeamRoster teams, Func<string, bool> entryExists)
  {
    // An alias is the operator's own decision, so it comes first. Aliases are
    // kept for the whole meeting, though, and one pointing at a team from an
    // earlier team event would create a nameless entry in an ordinary session:
    // follow it only while that team is part of this session.
    if (aliases.TryGetValue(transponder, out var target) &&
        (!TeamRoster.IsTeamKey(target) || teams.TeamFor(target) != null || entryExists(target)))
    {
      return (target, transponder);
    }

    return (teams.EntryKeyFor(transponder) ?? transponder, transponder);
  }
}
