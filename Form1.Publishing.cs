namespace CrossMgrInterface;

/// <summary>
/// Sending a finished session to the club's results website.
///
/// Every entry point - the Race menu, the Race Day screen and Past sessions -
/// comes through here, so what is published and what is said about it is
/// decided once.
/// </summary>
public partial class Form1
{
  private bool? _publishingAvailableCache;

  /// <summary>
  /// True once the club has entered both a website and a key. Cached: the
  /// key is a DPAPI decrypt off disk, and the Race Day heartbeat asks every
  /// second. Cleared when the settings window closes.
  /// </summary>
  private bool PublishingAvailable =>
    _publishingAvailableCache ??= !IsDemo && HttpResultsPublisher.FromSettings(_settings).IsConfigured;

  private string PublishSiteName
  {
    get
    {
      var url = _settings.ResultsSiteUrl ?? HttpResultsPublisher.DefaultUrl;
      try { return new Uri(url).Host; } catch (Exception) { return url; }
    }
  }

  /// <summary>Opens the settings, and saves whatever the operator decided.</summary>
  private void ShowPublishSettings()
  {
    using var dialog = new PublishSettingsDialog(_settings.ResultsSiteUrl, _settings.LiveSiteUrl,
      PublishCredentials.HasKey(), PublishCredentials.Hint(),
      _settings.PublishNamesByDefault, _settings.HiddenNameStyle);
    if (dialog.ShowDialog(this) != DialogResult.OK) return;

    _settings.ResultsSiteUrl = string.IsNullOrWhiteSpace(dialog.SiteUrl) ? null : dialog.SiteUrl;
    _settings.LiveSiteUrl = string.IsNullOrWhiteSpace(dialog.LiveSiteUrl) ? null : dialog.LiveSiteUrl;
    _settings.PublishNamesByDefault = dialog.PublishNamesByDefault;
    _settings.HiddenNameStyle = dialog.HiddenNameStyle;
    _settings.Save();

    // Both caches answer from the key file, which may just have changed.
    _publishingAvailableCache = null;
    InvalidateLiveConfiguration();
    // A live session whose address or key just changed must not carry on
    // with the old one. Off; the operator switches it on again if wanted.
    if (_liveOn) SetLiveTiming(false, silent: true);

    if (dialog.ForgetKey)
    {
      PublishCredentials.Forget();
      AddDiagnostic("Results website: the saved key was removed.");
    }
    else if (dialog.NewKey != null && !PublishCredentials.Save(dialog.NewKey))
    {
      ErrorDialog.Show(this, "The key could not be saved.",
        "The results website key could not be written to this computer. " +
        "Publishing will ask for it again.");
    }

    UpdateCommandStates();
    RenderRaceDay();
  }

  /// <summary>Publishes the session that is on screen now.</summary>
  private void PublishCurrentSession()
  {
    if (currentRaceId is not { } raceId)
    {
      MessageBox.Show(this, "There is no session to publish yet.",
        "Publish results", MessageBoxButtons.OK, MessageBoxIcon.Information);
      return;
    }

    var race = _raceDb.GetRace(raceId);
    if (race != null) PublishSession(race);
  }

  /// <summary>
  /// Publishes a stored session, restoring it exactly as printing one does -
  /// including which riders count - so the website and the sheet agree.
  /// </summary>
  private void PublishSession(DbRace race)
  {
    try
    {
      var request = BuildPublishRequest(race);
      if (request == null) return;

      var publisher = HttpResultsPublisher.FromSettings(_settings, AddDiagnostic);

      using var dialog = new PublishResultsDialog(request, publisher);
      var result = dialog.ShowDialog(this);

      if (dialog.WantsSettings)
      {
        ShowPublishSettings();
        // Straight back to publishing: the operator opened settings in order
        // to finish what they were doing.
        if (PublishingAvailable) PublishSession(race);
        return;
      }

      if (!dialog.Published) return;

      var url = dialog.PublishedUrl ?? "";
      _raceDb.MarkPublished(race.Id, DateTime.Now, url);
      AddMessage($"🌐 Results published to {PublishSiteName}");
      RaiseNotice(NoticeLevel.Info, $"Results published to {PublishSiteName}.");
      RenderRaceDay();
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(this, "The results could not be published.",
        "Nothing has been changed here, and the stored session is unaffected.", ex);
    }
  }

  /// <summary>
  /// Gathers everything the dialog needs, or says why the session cannot be
  /// published. Returns null only when there is nothing worth showing a dialog
  /// about.
  /// </summary>
  private PublishRequest? BuildPublishRequest(DbRace race)
  {
    var gatePick = race.SessionType == SessionType.TimedQualifying;

    // The same rule the printed sheet applies: riders who never went out
    // belong on a gate pick order and nowhere else.
    var field = _raceDb.RestoreRiderData(race.Id, includeRosterOnly: gatePick);
    foreach (var tag in _raceDb.GetIgnoredTags(race.Id))
      field.Remove(tag);

    if (field.Count == 0)
    {
      MessageBox.Show(this,
        "Nothing was recorded in that session, so there is nothing to publish.",
        "Nothing to publish", MessageBoxButtons.OK, MessageBoxIcon.Information);
      return null;
    }

    var rules = RaceRules.FromRace(race);

    var report = new RaceReportGenerator().PrepareReportData(
      field,
      race.StartTime,
      race.EndTime ?? race.StartTime + race.Duration,
      race.Duration,
      race.IsFinished,
      string.IsNullOrWhiteSpace(race.Name) ? $"Session {race.StartTime:yyyy-MM-dd HH:mm}" : race.Name,
      race.FinalLapsStartTime,
      race.IsFinished ? race.EndTime : null,
      race.AdditionalLaps ?? 0,
      rules);

    var track = string.IsNullOrEmpty(race.TrackId) ? null : _trackStore.Find(race.TrackId);

    var session = PublishPayloadBuilder.Build(new PublishInputs
    {
      Report = report,
      // Assigns one if this session predates the field, so an old race can
      // still be published.
      PublicId = _raceDb.EnsurePublicId(race.Id) ?? Guid.NewGuid().ToString("N"),
      SessionType = race.SessionType,
      Track = track,
      GatePick = gatePick ? QualifyingRanking.Rank(field.Values) : null,
      Field = field,
      PublishNamesByDefault = _settings.PublishNamesByDefault,
      HiddenNameStyle = _settings.HiddenNameStyle
    });

    return new PublishRequest
    {
      Session = session,
      SiteName = PublishSiteName,
      Riders = session.Entries.Count,
      Laps = session.Entries.Sum(e => e.LapTimes.Count),
      CircuitName = track?.Name,
      PublishedBefore = race.PublishedAt,
      Refusal = race.IsFinished
        ? null
        : "This session is still running. Publish it once the flag is out."
    };
  }
}
