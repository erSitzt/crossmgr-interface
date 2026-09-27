namespace CrossMgrInterface;

/// <summary>
/// Overall results: the motos of a day added up into one result, per class.
///
/// Everything here reads the motos as stored, the way printing a past session
/// does, so the overall always agrees with the moto sheets - including a lap
/// fixed after the event. See <see cref="OverallScorer"/> for the scoring.
/// </summary>
public partial class Form1
{
  /// <summary>
  /// The overall the wizard said the next race counts towards: an existing
  /// one, or a new one. Held until the clock starts, because that is when the
  /// race is recorded; null for a race scored alone.
  /// </summary>
  private (int? Id, bool New)? _overallChoice;

  /// <summary>The overalls a new race can join as its next moto: those begun today.</summary>
  private IReadOnlyList<OverallChoice> TodaysOveralls()
  {
    try
    {
      return _raceDb.ListOveralls()
        .Where(o => o.CreatedAt.Date == DateTime.Today)
        .Select(o => new OverallChoice(o.Id, o.Name, o.RaceIds.Count))
        .ToList();
    }
    catch (Exception)
    {
      // Not being offered an overall is no reason not to set up a race.
      return Array.Empty<OverallChoice>();
    }
  }

  /// <summary>Puts the race that just started into the overall the wizard chose.</summary>
  private void JoinChosenOverall(int raceId)
  {
    if (_overallChoice is not { } choice) return;
    _overallChoice = null;

    try
    {
      var overall = choice.New
        ? _raceDb.CreateOverall(OverallNameFor(raceName, DateTime.Now), Array.Empty<int>())
        : _raceDb.GetOverall(choice.Id ?? 0);
      if (overall == null) return;

      _raceDb.AddRaceToOverall(overall.Id, raceId);
      AddDiagnostic($"Race counts towards the overall '{overall.Name}'.");
    }
    catch (Exception ex)
    {
      // The race is timed either way; the motos can still be put together
      // afterwards from Past sessions.
      AddDiagnostic($"Could not add the race to its overall: {ex.Message}");
    }
  }

  /// <summary>
  /// "Moto 1 - MX2" makes "Overall - MX2", so two classes' overalls on one day
  /// are told apart; a name that is only "Moto 1" makes "Overall - 27.09.2026".
  /// </summary>
  internal static string OverallNameFor(string? motoName, DateTime day)
  {
    var rest = System.Text.RegularExpressions.Regex.Replace(motoName ?? "",
      @"^\s*(moto|lauf|heat|race)\s*\d+\s*[-–:]?\s*", "",
      System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
    return rest.Length > 0 ? $"Overall - {rest}" : $"Overall - {day:dd.MM.yyyy}";
  }
  /// <summary>
  /// Opens the overall a set of motos make up. Motos that already count
  /// towards one overall open it; otherwise, after asking, they become a new
  /// one, in the order they were run.
  /// </summary>
  private void ShowOverallFor(IWin32Window owner, IReadOnlyList<DbRace> races)
  {
    try
    {
      if (races.Any(r => r.SessionType != SessionType.Race))
      {
        MessageBox.Show(owner, "Only races count towards an overall. Practice and qualifying do not.",
          "Overall results", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
      }

      var existing = races.Select(r => _raceDb.OverallFor(r.Id)).ToList();

      // One overall between them, and nothing selected outside it: just open it.
      if (existing.All(o => o != null) && existing.Select(o => o!.Id).Distinct().Count() == 1)
      {
        ShowOverall(owner, existing[0]!);
        return;
      }

      if (races.Count < 2)
      {
        MessageBox.Show(owner, "Select at least two motos (Ctrl+click) to put them together into an overall.",
          "Overall results", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
      }

      var ordered = races.OrderBy(r => r.StartTime).ToList();
      var names = string.Join("\n", ordered.Select((r, i) => $"  Moto {i + 1}: {RaceLabel(r)}"));

      var moving = existing.Where(o => o != null).Select(o => o!.Name).Distinct().ToList();
      var warning = moving.Count == 0
        ? ""
        : $"\n\nSome of them count towards {string.Join(", ", moving.Select(n => $"'{n}'"))} now, " +
          "and will be taken out of it.";

      var answer = MessageBox.Show(owner,
        $"Score these motos together as one overall?\n\n{names}{warning}",
        "Overall results", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
      if (answer != DialogResult.OK) return;

      var overall = _raceDb.CreateOverall(DefaultOverallName(ordered[0]), Array.Empty<int>());
      foreach (var race in ordered) _raceDb.AddRaceToOverall(overall.Id, race.Id);
      AddMessage($"🏁 Overall '{overall.Name}' made from {ordered.Count} motos.");

      ShowOverall(owner, _raceDb.GetOverall(overall.Id) ?? overall);
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(owner, "The overall results could not be shown.",
        "The motos themselves are unaffected.", ex);
    }
  }

  private void ShowOverall(IWin32Window owner, DbOverall overall)
  {
    using var dialog = new OverallResultsDialog(new OverallHost(this), overall);
    dialog.ShowDialog(owner);
  }

  private static string DefaultOverallName(DbRace firstMoto) => OverallNameFor(firstMoto.Name, firstMoto.StartTime);

  private static string RaceLabel(DbRace race) =>
    string.IsNullOrWhiteSpace(race.Name) ? $"Race {race.StartTime:dd.MM.yyyy HH:mm}" : race.Name;

  /// <summary>
  /// A moto as the overall reads it: the same riders its printed sheet counts
  /// (see <see cref="PrintStoredSession"/>), under the rules it ran to.
  /// </summary>
  private OverallMoto LoadMoto(DbRace race)
  {
    var field = _raceDb.RestoreRiderData(race.Id);
    foreach (var tag in _raceDb.GetIgnoredTags(race.Id)) field.Remove(tag);

    return new OverallMoto(RaceLabel(race), field, RaceRules.FromRace(race),
      race.StartTime, race.EndTime ?? race.StartTime + race.Duration, race.IsFinished);
  }

  private List<DbRace> MotosOf(DbOverall overall) =>
    overall.RaceIds.Select(id => _raceDb.GetRace(id)).OfType<DbRace>().ToList();

  private OverallResult ScoreOverall(DbOverall overall) =>
    OverallScorer.Score(overall.Name, MotosOf(overall).Select(LoadMoto).ToList(), OverallRules.FromOverall(overall));

  private void PrintOverall(IWin32Window owner, DbOverall overall, OverallResult result)
  {
    using var options = new ReportOptionsDialog(overall.Name);
    if (options.ShowDialog(owner) != DialogResult.OK) return;

    using var generator = new OverallReportGenerator();
    generator.Run(owner, result, options.RaceTitle, options.SelectedAction);
  }

  /// <summary>
  /// Sends the overall to the website. Its motos have to be there first - the
  /// overall links to their pages - so any that are not are offered first, one
  /// after the other, through the ordinary publish dialog.
  /// </summary>
  private void PublishOverall(IWin32Window owner, DbOverall overall)
  {
    try
    {
      var motos = MotosOf(overall);

      var running = motos.Where(r => !r.IsFinished).ToList();
      if (running.Count > 0)
      {
        MessageBox.Show(owner,
          $"{string.Join(", ", running.Select(RaceLabel))} has not finished yet. " +
          "Publish the overall once every moto is over.",
          "Publish overall", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
      }

      var missing = motos.Where(r => r.PublishedAt == null).ToList();
      if (missing.Count > 0)
      {
        var answer = MessageBox.Show(owner,
          "The overall links to each moto's own page, so these have to be published first:\n\n" +
          string.Join("\n", missing.Select(r => $"  {RaceLabel(r)}")) +
          "\n\nPublish them now?",
          "Publish overall", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;

        foreach (var race in missing)
          if (!PublishSession(race)) return;
      }

      var result = ScoreOverall(overall);
      var payload = PublishPayloadBuilder.BuildOverall(result, overall.PublicId,
        motos.Select(r => _raceDb.EnsurePublicId(r.Id) ?? "").ToList(),
        _settings.PublishNamesByDefault, _settings.HiddenNameStyle);

      var request = new PublishRequest
      {
        Overall = payload,
        SiteName = PublishSiteName,
        Riders = payload.Classes.Sum(c => c.Entries.Count),
        PublishedBefore = overall.PublishedAt
      };

      var publisher = HttpResultsPublisher.FromSettings(_settings, AddDiagnostic);
      using var dialog = new PublishResultsDialog(request, publisher);
      dialog.ShowDialog(owner);

      if (dialog.WantsSettings)
      {
        ShowPublishSettings();
        if (PublishingAvailable) PublishOverall(owner, overall);
        return;
      }

      if (!dialog.Published) return;

      _raceDb.MarkOverallPublished(overall.Id, DateTime.Now, dialog.PublishedUrl ?? "");
      overall.PublishedAt = DateTime.Now;
      AddMessage($"🌐 Overall '{overall.Name}' published to {PublishSiteName}");
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(owner, "The overall could not be published.",
        "Nothing has been changed here, and the motos are unaffected.", ex);
    }
  }

  private bool DeleteOverall(IWin32Window owner, DbOverall overall)
  {
    var answer = MessageBox.Show(owner,
      $"Stop counting these motos together as '{overall.Name}'?\n\n" +
      "The motos and their results stay as they are; only the overall goes. " +
      "It can be put together again from Past sessions.",
      "Overall results", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
    if (answer != DialogResult.Yes) return false;

    _raceDb.DeleteOverall(overall.Id);
    AddMessage($"🗑️ Overall '{overall.Name}' removed. Its motos are unchanged.");
    return true;
  }

  /// <summary>What the Overall results window may ask of the form.</summary>
  private sealed class OverallHost : IOverallResultsHost
  {
    private readonly Form1 _form;
    public OverallHost(Form1 form) => _form = form;

    public OverallResult Score(DbOverall overall) => _form.ScoreOverall(overall);
    public void Save(DbOverall overall) => _form._raceDb.UpdateOverall(overall);
    public void PrintResults(IWin32Window owner, DbOverall overall, OverallResult result) =>
      _form.PrintOverall(owner, overall, result);
    public bool PublishingAvailable => _form.PublishingAvailable;
    public void Publish(IWin32Window owner, DbOverall overall) => _form.PublishOverall(owner, overall);
    public bool Delete(IWin32Window owner, DbOverall overall) => _form.DeleteOverall(owner, overall);
  }
}
