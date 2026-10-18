namespace CrossMgrInterface;

/// <summary>One thing the outbox can do.</summary>
public enum OutboxWork { SaveSheets, PublishSession, SaveOverall, PublishOverall }

/// <summary>What to do next, for which session or overall, and at which revision - recorded once it is done.</summary>
public sealed record OutboxItem(OutboxWork Work, int Id, int Revision, string? Stamp = null)
{
  /// <summary>Parked items are remembered by this, so a later correction gets its own try.</summary>
  public string Key => $"{Work}:{Id}:{Revision}:{Stamp}";
}

/// <summary>An overall, with what the outbox needs to know about it.</summary>
/// <param name="Complete">Every moto it is meant to have has finished - all its planned motos, or all it has.</param>
/// <param name="Stamp">Its motos and their revisions now; it is out of date when this differs from what was sent.</param>
public sealed record OverallCandidate(int OverallId, IReadOnlyList<DbRace> Motos, bool Complete, string Stamp,
  string? PublishedStamp, string? SavedStamp);

/// <summary>
/// What the outbox does next, one thing at a time.
///
/// Saving comes first: it needs no internet, and a sheet on the laptop is the
/// one copy that cannot be lost to a dead hotspot. Then sessions, oldest
/// first, so the website fills up in the order the day ran. An overall last
/// of all, and only once every one of its motos is on the website in its
/// latest form - the website links an overall to its motos, and an overall
/// sent ahead of a correction to one of them would add up the old laps.
///
/// A session is not published until it has been finished a couple of
/// minutes: the last stray read usually turns up in that time, and an
/// unidentified rider is better named before the page goes up than after.
/// </summary>
public static class OutboxPolicy
{
  public static readonly TimeSpan Settle = TimeSpan.FromMinutes(2);

  public static OutboxItem? Next(IReadOnlyList<DbRace> races, IReadOnlyList<OverallCandidate> overalls, DateTime now,
    IReadOnlySet<string> parked, bool canPublish)
  {
    foreach (var race in races.OrderBy(r => r.StartTime))
    {
      if (race is not { IsFinished: true, AutoSaveSheets: true } || (race.SheetsRevision ?? -1) >= race.Revision) continue;
      var item = new OutboxItem(OutboxWork.SaveSheets, race.Id, race.Revision);
      if (!parked.Contains(item.Key)) return item;
    }

    foreach (var overall in overalls.Where(o => o.Complete && o.Motos.Any(m => m.AutoSaveSheets == true)))
    {
      if (overall.Stamp == overall.SavedStamp) continue;
      if (overall.Motos.Any(m => m.AutoSaveSheets == true && (m.SheetsRevision ?? -1) < m.Revision)) continue;
      var item = new OutboxItem(OutboxWork.SaveOverall, overall.OverallId, 0, overall.Stamp);
      if (!parked.Contains(item.Key)) return item;
    }

    if (!canPublish) return null;

    foreach (var race in races.OrderBy(r => r.StartTime))
    {
      if (!WaitingToPublish(race)) continue;
      if ((race.EndTime ?? race.LastSavedAt ?? race.StartTime) + Settle > now) continue;
      var item = new OutboxItem(OutboxWork.PublishSession, race.Id, race.Revision);
      if (!parked.Contains(item.Key)) return item;
    }

    foreach (var overall in overalls.Where(o => o.Complete && o.Motos.Any(m => m.AutoPublish == true)))
    {
      if (overall.Stamp == overall.PublishedStamp) continue;
      if (overall.Motos.Any(m => m.PublishedAt == null || (m.PublishedRevision ?? -1) < m.Revision)) continue;
      var item = new OutboxItem(OutboxWork.PublishOverall, overall.OverallId, 0, overall.Stamp);
      if (!parked.Contains(item.Key)) return item;
    }

    return null;
  }

  public static bool WaitingToPublish(DbRace race) =>
    race is { IsFinished: true, AutoPublish: true } && (race.PublishedRevision ?? -1) < race.Revision;

  /// <summary>An overall's motos and their revisions, and its points: what it was sent or saved as.</summary>
  public static string StampOf(DbOverall overall, IReadOnlyList<DbRace> motos) =>
    string.Join(",", motos.Select(m => $"{m.Id}:{m.Revision}")) + "|" +
    string.Join(",", OverallRules.FromOverall(overall).Points) + "|" + overall.Name;

  /// <summary>Waits a little longer each time the website cannot be reached, never more than five minutes.</summary>
  public static TimeSpan Backoff(int failures) =>
    TimeSpan.FromSeconds(Math.Min(300, 30 * Math.Pow(2, Math.Max(0, failures - 1))));
}
