namespace CrossMgrInterface;

/// <summary>
/// Team events: riders who share a team name take turns on track and are scored
/// as one entry. See <see cref="TeamRoster"/>.
///
/// The race engine is untouched by this, as it is by waves. A member's
/// transponder is resolved to the team's entry before a read is scored, and
/// everything after that - laps, standings, the flag, corrections, the database
/// - already works per entry. What this file adds is deciding which entry a
/// read belongs to, and remembering which member's transponder it was.
/// </summary>
public partial class Form1
{
  /// <summary>
  /// This session groups riders by team name. Setup, not session state: it is
  /// chosen before the clock starts and never changes while it runs.
  /// </summary>
  private bool teamEvent;

  /// <summary>
  /// Which transponder belongs to which team. Empty outside a team event.
  /// Replaced whole and never edited, so a reader holds a consistent roster
  /// even while it is being rebuilt.
  /// </summary>
  private TeamRoster _teams = TeamRoster.Empty;

  /// <summary>
  /// The last read of each transponder, counted or not, for
  /// <see cref="ReadDebounce"/>. Guarded by ridersLock.
  /// </summary>
  private readonly Dictionary<string, DateTime> lastReadByTransponder = new(StringComparer.OrdinalIgnoreCase);

  /// <summary>When each member was last said to be waiting near the loop. Guarded by ridersLock.</summary>
  private readonly Dictionary<string, DateTime> waitingMemberWarnedAt = new(StringComparer.OrdinalIgnoreCase);

  /// <summary>The field's median pace, for judging a team that has no pace of its own yet. The caller holds ridersLock.</summary>
  private TimeSpan? FieldPace() =>
    RaceProgress.MedianPace(riders.Values.Where(r => !ignoredTags.Contains(r.TagID)).ToList());

  /// <summary>
  /// Re-derives a team's two-on-track warnings after a crossing, and says so for
  /// any lap that has just been flagged. The caller holds ridersLock.
  /// </summary>
  private void DetectTwoOnTrack(RiderInfo team, List<(string, bool)> messagesToAdd)
  {
    var before = team.Laps
      .Where(l => l.IsSuspectedOverlap)
      .Select(l => l.LapNumber)
      .ToHashSet();

    TwoOnTrackDetector.Analyze(team, FieldPace());

    var newlyFlagged = team.Laps
      .Where(l => l.IsSuspectedOverlap && !before.Contains(l.LapNumber))
      .ToList();

    foreach (var lap in newlyFlagged)
    {
      var previous = team.Laps.FirstOrDefault(l => l.LapNumber == lap.LapNumber - 1);
      messagesToAdd.Add((
        $"👥 TWO ON TRACK?: {MemberLabel(team, lap.CrossedBy)} crossed {lap.LapTime?.TotalSeconds:F1}s after " +
        $"{MemberLabel(team, previous?.CrossedBy)} - one of those two laps is not a real lap. " +
        $"Right-click {team.Label} to fix it.",
        true));
    }

    if (newlyFlagged.Count > 0)
      RaiseNotice(NoticeLevel.Warning, $"Two riders on track? - {team.Label}");
  }

  private static string MemberLabel(RiderInfo team, string? transponder) =>
    team.MemberFor(transponder)?.Label ?? transponder ?? "an unknown rider";

  /// <summary>
  /// A team member whose transponder keeps being read without it counting is
  /// almost certainly waiting to take over within range of the loop. The
  /// debounce keeps those reads off the lap count, but the operator should get
  /// them to stand back: the next reads may not be so neatly spaced. The caller
  /// holds ridersLock.
  /// </summary>
  private void NoteWaitingMember(string entryKey, string transponder, DateTime at, List<(string, bool)> messagesToAdd)
  {
    var tuning = TeamEventSettings.Default;

    var recent = rejectedReads.Count(r =>
      r.TagID == entryKey &&
      string.Equals(r.CrossedBy, transponder, StringComparison.OrdinalIgnoreCase) &&
      r.CrossingTime <= at && at - r.CrossingTime <= tuning.WaitingWindow);

    if (recent < tuning.WaitingReads) return;
    if (waitingMemberWarnedAt.TryGetValue(transponder, out var warned) && at - warned < tuning.WaitingRewarn) return;
    waitingMemberWarnedAt[transponder] = at;

    var who = DescribeCrossing(entryKey, transponder);
    messagesToAdd.Add(($"👥 {who}: transponder keeps being read near the loop without crossing it. " +
                       "Riders waiting to take over must stay out of reader range.", true));
    RaiseNotice(NoticeLevel.Warning, $"{who} is waiting too close to the loop");
  }

  /// <summary>
  /// Works out the teams again from the rider list and from the teams already
  /// racing. Call after anything that changes the flag, the list or the field.
  /// </summary>
  private void RebuildTeamRoster()
  {
    lock (ridersLock)
    {
      _teams = teamEvent
        ? TeamRoster.Build(_riderDataImporter.Rows).WithLiveEntries(riders.Values)
        : TeamRoster.Empty;
    }
  }

  /// <summary>The entry a read counts for, and the transponder that was read. The caller holds ridersLock.</summary>
  private (string EntryKey, string CrossedBy) ResolveCrossing(string transponder) =>
    CrossingResolver.Resolve(transponder, tagAliases, _teams, riders.ContainsKey);

  /// <summary>"#14 Ben Fischer (MSC Adler)" for a team member's transponder; null for any other.</summary>
  private string? DescribeTeamMember(string transponder)
  {
    var teams = _teams;
    var member = teams.MemberFor(transponder);
    if (member == null) return null;

    var team = teams.TeamFor(teams.EntryKeyFor(transponder)!);
    return team == null ? member.Label : $"{member.Label} ({team.Name})";
  }

  /// <summary>
  /// Who crossed, for a log line: the member and their team when a team member's
  /// transponder was read, the entry's label otherwise.
  /// </summary>
  private string DescribeCrossing(string entryKey, string crossedBy) =>
    (crossedBy != entryKey ? DescribeTeamMember(crossedBy) : null) ?? GetRiderDisplayText(entryKey);

  /// <summary>Writes what the rider list makes as a team event to the log, and warns about anything that cannot be scored.</summary>
  private void ReportTeamRoster()
  {
    if (!teamEvent) return;

    var teams = _teams;
    AddMessage($"👥 Team event: {teams.Summary}");

    foreach (var issue in teams.Issues)
      AddMessage(issue.Severity == TeamRosterSeverity.Error ? $"❌ {issue.Message}" : $"⚠️ {issue.Message}");

    if (teams.HasErrors)
      RaiseNotice(NoticeLevel.Warning, "A transponder is on two entries in the rider list - see Race Events");
  }

  /// <summary>
  /// A rider list imported from the menu while a team event is set up: check it
  /// still is one. Grouping a list by its team column is only right for a list
  /// made for a team event - an ordinary one holds club names there.
  ///
  /// Before the clock starts only. Once racing, entries are already scored by
  /// team and switching the flag would split them.
  /// </summary>
  private void ConfirmTeamEventForNewRiderList()
  {
    if (!teamEvent || raceStarted) return;

    var roster = TeamRoster.Build(_riderDataImporter.Rows);
    var answer = MessageBox.Show(this,
      "This session is set up as a team event: riders who share a team name score as one team.\n\n" +
      $"This rider list makes {roster.Summary}.\n\nIs this still a team event?",
      "Team event", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

    if (answer == DialogResult.Yes) return;

    teamEvent = false;
    RememberRaceSetup();
    ApplyTeamEventToUi();
    AddMessage("👥 Team event switched off - every rider is scored on their own transponder.");
  }

  /// <summary>
  /// What a team event changes on screen: who is on track beside each team, and
  /// the members where the club would be. Called from ApplySessionTypeToUi, so it
  /// follows the wizard, crash recovery and startup alike.
  /// </summary>
  private void ApplyTeamEventToUi()
  {
    if (dataGridViewRiders.Columns["OnTrack"] is { } onTrack) onTrack.Visible = teamEvent;

    if (dataGridViewRiders.Columns["Team"] is { } team)
    {
      team.HeaderText = teamEvent ? "Riders" : "Team";
      team.Width = teamEvent ? 240 : 120;
    }

    // "11/14" does not fit where "127" did.
    if (dataGridViewRiders.Columns["RiderNumber"] is { } number) number.Width = teamEvent ? 80 : 60;

    if (_raceDayView != null) _raceDayView.ShowOnTrack = teamEvent;
    _refresh?.Invalidate(RaceViewKind.All);
  }
}
