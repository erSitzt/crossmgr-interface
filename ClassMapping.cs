using System.Text;

namespace CrossMgrInterface;

/// <summary>What matching a plan's classes to a rider list came to.</summary>
/// <param name="Dropped">Sessions left with no class at all, and so taken out: no classes would mean every rider.</param>
public sealed record ClassMappingResult(DayPlan Plan, IReadOnlyList<string> Warnings, IReadOnlyList<string> Dropped);

/// <summary>
/// Puts a plan's classes onto a rider list's: a preset's Group A becomes MX1
/// and MX2, a club's "Jugend" from last year's list becomes this year's
/// "MX Jugend".
///
/// Across the whole plan at once, so the motos of one overall can never end
/// up with different classes. Names and overall groups that were made from
/// the classes ("Moto 1 - Group A") are made again from the new ones ("Moto 1
/// - MX1 / MX2"); a name somebody typed is left as typed. The order chosen
/// for a class's replacements is kept - in a session started in waves it is
/// the order the classes leave the gate.
///
/// Pure, so every case can be held to in a test.
/// </summary>
public static class ClassMapping
{
  /// <summary>The plan's classes that are not on the rider list, in the order the plan first uses them.</summary>
  public static IReadOnlyList<string> Unmatched(DayPlan plan, IReadOnlyCollection<string> listClasses) =>
    plan.Sessions
      .Where(s => !s.TeamEvent)
      .SelectMany(s => s.Classes)
      .Where(c => Find(listClasses, c) == null)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();

  /// <param name="map">For a class of the plan: the rider-list classes it becomes, in order. Empty leaves it out.</param>
  public static ClassMappingResult Apply(DayPlan plan, IReadOnlyDictionary<string, IReadOnlyList<string>> map,
    IReadOnlyCollection<string> listClasses)
  {
    var copy = PlanStore.ImportJson(PlanStore.ExportJson(plan))!;
    var warnings = new List<string>();
    var dropped = new List<string>();
    var groupsBefore = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    foreach (var session in copy.Sessions.ToList())
    {
      if (session.TeamEvent || session.Classes.Count == 0) continue;

      var oldClasses = session.Classes.ToList();
      var oldSuffix = DayPlanCode.ClassesSuffix(oldClasses);
      var oldGroup = session.OverallGroup;

      var newClasses = oldClasses
        .SelectMany(c => Lookup(map, c) is { } replacements ? replacements : new[] { Find(listClasses, c) ?? c })
        .Select(c => Find(listClasses, c) ?? c)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

      if (newClasses.Count == 0)
      {
        copy.Sessions.Remove(session);
        dropped.Add(session.Name.Length > 0 ? session.Name : $"The session at {session.Time:hh\\:mm}");
        continue;
      }

      var newSuffix = DayPlanCode.ClassesSuffix(newClasses);
      session.Classes = newClasses;

      if (oldGroup != null && oldGroup == oldSuffix) session.OverallGroup = newSuffix;
      if (session.OverallGroup != null && oldGroup != null)
        groupsBefore.TryAdd(oldGroup, session.OverallGroup);

      if (session.Name.EndsWith(" - " + oldSuffix, StringComparison.Ordinal))
        session.Name = session.Name[..^oldSuffix.Length] + newSuffix;
      else if (oldGroup != null && session.OverallGroup != oldGroup &&
               session.Name.EndsWith(" - " + oldGroup, StringComparison.Ordinal))
        session.Name = session.Name[..^oldGroup.Length] + session.OverallGroup;

      if (session.Start == PlannedStartMode.Waves && newClasses.Count < 2)
        warnings.Add($"{session.Name} starts in waves but now has only one class.");
    }

    // Two overalls that become one would add up motos that were meant apart.
    foreach (var merged in groupsBefore.GroupBy(p => p.Value, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
      warnings.Add($"The overalls {string.Join(" and ", merged.Select(p => p.Key))} would both become {merged.Key}.");

    var used = copy.Sessions.SelectMany(s => s.Classes).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var anyClasses = copy.Sessions.Any(s => s.Classes.Count > 0);
    var unused = listClasses.Where(c => !used.Contains(c)).ToList();
    if (anyClasses && unused.Count > 0 && !copy.Sessions.Any(s => s.Classes.Count == 0 && !s.TeamEvent))
      warnings.Add($"No session counts {string.Join(", ", unused)}.");

    return new ClassMappingResult(copy, warnings, dropped);
  }

  /// <summary>The rider list's own spelling of a class, if it has it: case, spaces and accents typed differently still match.</summary>
  public static string? Find(IReadOnlyCollection<string> listClasses, string name)
  {
    var key = Key(name);
    return listClasses.FirstOrDefault(c => Key(c) == key);
  }

  private static IReadOnlyList<string>? Lookup(IReadOnlyDictionary<string, IReadOnlyList<string>> map, string name)
  {
    var key = Key(name);
    foreach (var (from, to) in map)
      if (Key(from) == key) return to;
    return null;
  }

  private static string Key(string name) => name.Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant();
}
