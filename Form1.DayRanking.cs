namespace CrossMgrInterface;

/// <summary>
/// Master of all classes: every rider of every race of a day in one list,
/// ranked on lap times. Reads the races as stored, as the overall does, so the
/// list always agrees with each race's sheet. See <see cref="DayRankingScorer"/>.
/// </summary>
public partial class Form1
{
  internal const string DayRankingDefaultName = "Master of all classes";

  /// <summary>
  /// Opens the list of <paramref name="day"/> - by default today, or the last
  /// day races were run when there were none today.
  /// </summary>
  private void ShowDayRanking(IWin32Window owner, DateTime? day)
  {
    try
    {
      var date = day ?? LastRaceDay();
      if (date == null || _raceDb.RacesOfDay(date.Value).Count == 0)
      {
        MessageBox.Show(owner,
          "No race has finished on that day yet. The list fills as the day's races finish.",
          "Master of all classes", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
      }

      var ranking = _raceDb.EnsureDayRanking(date.Value, DayRankingDefaultName, _settings.AfterSession.AllClassesMeasure);
      using var dialog = new DayRankingDialog(new DayRankingHost(this), ranking);
      dialog.ShowDialog(owner);
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(owner, "The list of all classes could not be shown.",
        "The races themselves are unaffected.", ex);
    }
  }

  private DateTime? LastRaceDay()
  {
    if (_raceDb.RacesOfDay(DateTime.Today).Count > 0) return DateTime.Today;
    return _raceDb.GetAllRaces()
      .Where(r => r.SessionType == SessionType.Race && r.IsFinished)
      .Select(r => (DateTime?)r.StartTime.Date)
      .Max();
  }

  private DayRankingResult ScoreDayRanking(DbDayRanking ranking)
  {
    var races = _raceDb.RacesOfDay(ranking.Day);
    var result = DayRankingScorer.Score(ranking.Name, races.Select(LoadMoto).ToList(), ranking.Measure,
      final: !DayHasRacesToRun(ranking.Day));

    // A lap is only the same lap on the same circuit.
    var circuits = races.Select(r => r.TrackId).Where(t => !string.IsNullOrEmpty(t)).Distinct().Count();
    if (circuits > 1)
      result.Notes.Add("These races were run on different circuits, so their lap times do not compare.");
    return result;
  }

  /// <summary>True while the day's plan still has races to run: the list is provisional until then.</summary>
  private bool DayHasRacesToRun(DateTime day)
  {
    if (_activePlan is not { } plan || plan.Date.Date != day.Date) return false;
    var runs = PlanRuns();
    return plan.Sessions.Any(s => s.SessionType == SessionType.Race && !s.TeamEvent && !s.Skipped &&
                                  !(runs.TryGetValue(s.Id, out var run) && run.Finished));
  }

  private void PrintDayRanking(IWin32Window owner, DbDayRanking ranking, DayRankingResult result)
  {
    using var options = new ReportOptionsDialog(ranking.Name);
    if (options.ShowDialog(owner) != DialogResult.OK) return;

    using var generator = new DayRankingReportGenerator();
    generator.Run(owner, result, options.RaceTitle, options.SelectedAction);
  }

  /// <summary>
  /// Sends the list to the website. Its races have to be there first - each
  /// rider's best lap links to the race it was set in - so any that are not
  /// are offered first, as Publish... on an overall does.
  /// </summary>
  private void PublishDayRanking(IWin32Window owner, DbDayRanking ranking)
  {
    try
    {
      var races = _raceDb.RacesOfDay(ranking.Day);
      var missing = races.Where(r => r.PublishedAt == null).ToList();
      if (missing.Count > 0)
      {
        var answer = MessageBox.Show(owner,
          "The list links to each race's own page, so these have to be published first:\n\n" +
          string.Join("\n", missing.Select(r => $"  {RaceLabel(r)}")) +
          "\n\nPublish them now?",
          "Publish Master of all classes", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;

        foreach (var race in missing)
          if (!PublishSession(race)) return;
      }

      var payload = BuildDayRankingPayload(ranking, races);
      var request = new PublishRequest
      {
        DayRanking = payload,
        SiteName = PublishSiteName,
        Riders = payload.Entries.Count,
        PublishedBefore = ranking.PublishedAt,
        Demo = IsDemo
      };

      var publisher = HttpResultsPublisher.FromSettings(_settings, AddDiagnostic);
      using var dialog = new PublishResultsDialog(request, publisher);
      dialog.ShowDialog(owner);

      if (dialog.WantsSettings)
      {
        ShowPublishSettings();
        if (PublishingAvailable) PublishDayRanking(owner, ranking);
        return;
      }

      if (!dialog.Published) return;

      _raceDb.MarkDayRankingPublished(ranking.Id, DateTime.Now, dialog.PublishedUrl ?? "");
      _raceDb.SetDayRankingStamps(ranking.Id, published: OutboxPolicy.StampOf(ranking, races));
      ranking.PublishedAt = DateTime.Now;
      AddMessage($"🌐 '{ranking.Name}' published to {PublishSiteName}");
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(owner, "The list of all classes could not be published.",
        "Nothing has been changed here, and the races are unaffected.", ex);
    }
  }

  /// <summary>The list as the website is told it. Shared by Publish... and the outbox.</summary>
  private PublishedDayRanking BuildDayRankingPayload(DbDayRanking ranking, IReadOnlyList<DbRace> races)
  {
    var result = ScoreDayRanking(ranking);
    // Team races are scored as left out, so they are not among the sessions sent.
    var counted = races.Where(r => r.TeamEvent != true).ToList();
    return PublishPayloadBuilder.BuildDayRanking(result, ranking.PublicId,
      counted.Select(r => _raceDb.EnsurePublicId(r.Id) ?? "").ToList(),
      _settings.PublishNamesByDefault, _settings.HiddenNameStyle, demo: IsDemo);
  }

  private sealed class DayRankingHost : IDayRankingHost
  {
    private readonly Form1 _form;
    public DayRankingHost(Form1 form) => _form = form;

    public DayRankingResult Score(DbDayRanking ranking) => _form.ScoreDayRanking(ranking);
    public void Save(DbDayRanking ranking) => _form._raceDb.UpdateDayRanking(ranking);
    public void PrintResults(IWin32Window owner, DbDayRanking ranking, DayRankingResult result) =>
      _form.PrintDayRanking(owner, ranking, result);
    public bool PublishingAvailable => _form.PublishingAvailable;
    public void Publish(IWin32Window owner, DbDayRanking ranking) => _form.PublishDayRanking(owner, ranking);
  }
}
