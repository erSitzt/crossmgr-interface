namespace CrossMgrInterface;

/// <summary>
/// The qualifying half of Form1: assembling the field the gate pick order is
/// derived from, rendering the Qualifying tab, and printing the sheet.
///
/// Its own partial file for the same reason as the others - Form1.cs is very
/// large and the designer rewrites Form1.Designer.cs wholesale.
/// </summary>
public partial class Form1
{
  private QualifyingView _qualifyingView = null!;
  private TabPage tabPageQualifying = null!;
  private QualifyingReportGenerator _qualifyingReportGenerator = null!;

  /// <summary>Class shown on the Qualifying tab. Independent of the riders grid
  /// filter: during qualifying you want one class on the sheet while the grid
  /// still shows the whole field.</summary>
  private string _qualifyingClassFilter = "All Classes";

  /// <summary>
  /// Everyone entered in the session, which is more than everyone the timing
  /// loop has seen. Shared by the gate pick order and the transponder check -
  /// the second one is all about the riders the loop has NOT seen.
  ///
  /// A RiderInfo is only created on a rider's first crossing, so a rider on the
  /// imported roster who never went out has no record at all. Without the union
  /// below they would simply be missing from the sheet rather than listed last
  /// as having set no time, which is the one thing the operator most needs to
  /// see before handing the list out.
  /// </summary>
  private List<RiderInfo> BuildSessionField()
  {
    List<RiderInfo> field;

    lock (ridersLock)
    {
      field = riders.Values
        .Where(r => !ignoredTags.Contains(r.TagID))
        .Select(CloneRiderForDisplay)
        .ToList();
    }

    var seen = field.Select(r => r.TagID).ToHashSet(StringComparer.OrdinalIgnoreCase);

    foreach (var entry in _riderDataImporter.GetAllRiderData().Values)
    {
      if (string.IsNullOrWhiteSpace(entry.TagID)) continue;
      if (seen.Contains(entry.TagID) || ignoredTags.Contains(entry.TagID)) continue;

      // No laps and no IsDNS: having made zero crossings is itself what marks
      // them as never having gone out, and IsDNS is reserved for an operator
      // ruling that someone actively withdrew.
      field.Add(new RiderInfo
      {
        TagID = entry.TagID,
        RiderNumber = entry.RiderNumber,
        FirstName = entry.FirstName,
        LastName = entry.LastName,
        Team = entry.Team,
        Category = entry.Category,
        Machine = entry.Machine
      });
    }

    return field;
  }

  /// <summary>Builds the Qualifying tab and wires its events to Form1.</summary>
  private void InitializeQualifyingView()
  {
    _qualifyingView = new QualifyingView();
    tabPageQualifying = _qualifyingView.CreateQualifyingTab();
    _qualifyingReportGenerator = new QualifyingReportGenerator();

    _qualifyingView.PrintRequested += (_, _) => ShowQualifyingReport();
    _qualifyingView.RiderActivated += (_, tagId) => OpenLapCorrection(tagId);
    _qualifyingView.ClassFilterChanged += (_, className) =>
    {
      _qualifyingClassFilter = className;
      _refresh.RenderNow(RaceViewKind.Qualifying);
    };
  }

  /// <summary>Repaints the gate pick order from the current field.</summary>
  private void RenderQualifying()
  {
    var field = BuildSessionField();

    _qualifyingView.SetClasses(QualifyingClasses(field), _qualifyingClassFilter);

    var shown = _qualifyingClassFilter == "All Classes"
      ? field
      : field.Where(r => string.Equals(r.Category, _qualifyingClassFilter, StringComparison.OrdinalIgnoreCase)).ToList();

    var ranking = QualifyingRanking.Rank(shown);
    _qualifyingView.SetRows(BuildQualifyingRows(ranking), DescribeQualifyingField(ranking));
  }

  private static List<QualifyingRowData> BuildQualifyingRows(IReadOnlyList<QualifyingEntry> ranking)
  {
    var rows = new List<QualifyingRowData>(ranking.Count);

    foreach (var entry in ranking)
    {
      var cells = new string[QualifyingRowData.ColumnCount];
      cells[QualifyingRowData.ColGatePick] = entry.GatePick.ToString();
      cells[QualifyingRowData.ColRiderNumber] = entry.Rider.RiderNumber;
      cells[QualifyingRowData.ColRiderName] = entry.Rider.Label;
      cells[QualifyingRowData.ColCategory] = entry.Rider.Category;
      cells[QualifyingRowData.ColBestLap] = entry.Status == QualifyingStatus.Timed
        ? FormatLap(entry.BestLapTime)
        : "NO TIME";
      cells[QualifyingRowData.ColGap] = FormatDelta(entry.GapToPole, entry);
      cells[QualifyingRowData.ColInterval] = FormatDelta(entry.IntervalToAhead, entry);
      cells[QualifyingRowData.ColOnLap] = entry.BestLapNumber > 0 ? entry.BestLapNumber.ToString() : "-";
      cells[QualifyingRowData.ColLaps] = entry.TimedLaps.ToString();
      cells[QualifyingRowData.ColStatus] = DescribeQualifyingStatus(entry);

      var timed = entry.Status == QualifyingStatus.Timed;

      rows.Add(new QualifyingRowData
      {
        TagID = entry.Rider.TagID,
        Cells = cells,
        // Podium shading follows the riders grid, so the same three colours mean
        // the same three places everywhere in the application.
        RowBackColor = timed
          ? entry.GatePick switch
          {
            1 => Color.Gold,
            2 => Color.Gainsboro,
            3 => Color.FromArgb(233, 205, 175),
            _ => Color.Empty
          }
          : Color.WhiteSmoke,
        RowForeColor = timed ? Color.Empty : Color.Gray,
        NeedsCheck = entry.Rider.HasAnomalies,
        Tooltip = DescribeQualifyingTooltip(entry)
      });
    }

    return rows;
  }

  private static string FormatLap(TimeSpan? lap) =>
    lap.HasValue ? lap.Value.ToString(@"m\:ss\.fff") : "-";

  private static string FormatDelta(TimeSpan? delta, QualifyingEntry entry)
  {
    if (entry.Status != QualifyingStatus.Timed) return "";
    // Pole has nothing to be behind, which is a dash rather than +0.000.
    return delta.HasValue ? $"+{delta.Value.TotalSeconds:F3}" : "-";
  }

  /// <summary>
  /// Why a rider has no time, or why the one they have is in doubt.
  ///
  /// Says nothing about IsDNF on purpose. The flag's grace marks everyone who
  /// was not still circulating when the session ended, which is very nearly the
  /// whole field, so showing it would fill the column with a warning that means
  /// only "had already pulled in".
  /// </summary>
  private static string DescribeQualifyingStatus(QualifyingEntry entry)
  {
    if (entry.Status == QualifyingStatus.DidNotGoOut)
      return entry.Rider.IsDNS ? "did not start" : "did not go out";

    if (entry.Status == QualifyingStatus.NoTime)
      return "out-lap only";

    return entry.Rider.HasAnomalies ? "CHECK" : "";
  }

  private static string DescribeQualifyingTooltip(QualifyingEntry entry) => entry.Status switch
  {
    QualifyingStatus.Timed =>
      $"{entry.Rider.Label} - best lap {FormatLap(entry.BestLapTime)} on lap {entry.BestLapNumber}" +
      (entry.BestLapSetAt.HasValue ? $" at {entry.BestLapSetAt.Value:HH:mm:ss}" : ""),
    QualifyingStatus.NoTime =>
      $"{entry.Rider.Label} crossed the loop but never completed a timed lap, so they pick last.",
    _ =>
      $"{entry.Rider.Label} never crossed the timing loop."
  };

  private static string DescribeQualifyingField(IReadOnlyList<QualifyingEntry> ranking)
  {
    var timed = ranking.Count(e => e.Status == QualifyingStatus.Timed);
    var without = ranking.Count - timed;

    if (ranking.Count == 0) return "Nobody has gone out yet.";

    return without == 0
      ? $"{timed} riders, all with a time."
      : $"{timed} riders with a time, {without} without - they pick last.";
  }

  /// <summary>
  /// Everything that follows from the session type, in one place.
  ///
  /// Called from the wizard and from crash recovery. The second is the one that
  /// is easy to forget: RestoreRaceState does not rebuild the tabs on its own,
  /// so without this a recovered qualifying session comes back with no
  /// Qualifying tab and race wording on the Race Day tile.
  /// </summary>
  private void ApplySessionTypeToUi()
  {
    // Two flags, not one: free practice is timed - so it gets the session
    // wording - but produces no gate pick order, so its results button must
    // keep saying Results... and keep opening the race report.
    _raceDayView.TimedSession = IsTimedSession;
    _raceDayView.GatePickOrder = IsQualifying;
    RebuildTabs();

    // Disabled rather than hidden: the Race Settings tab is absolute-positioned,
    // so hiding these would leave a hole in the middle of it. The DNF timeout
    // stays enabled - it still governs the grace after the flag.
    var extraLapsApply = !IsTimedSession;
    labelAdditionalLaps.Enabled = extraLapsApply;
    numericUpDownAdditionalLaps.Enabled = extraLapsApply;
    buttonSetAdditionalLaps.Enabled = extraLapsApply;

    UpdateRaceStartControls();

    // Deliberately does not persist. This runs during Form1_Load, before
    // raceDuration and additionalLapsAfterTimeExpiry have been read out of
    // their controls - saving here would write the field defaults over the
    // setup the operator had remembered. The wizard persists instead, once
    // everything has been applied.
  }

  /// <summary>
  /// Preview, print or save the gate pick order.
  ///
  /// Reached three ways - the Race menu, the button on the Qualifying tab, and
  /// the Race Day view's action button once it has been relabelled - all of
  /// which land here so there is one behaviour to get right.
  /// </summary>
  private void ShowQualifyingReport()
  {
    try
    {
      var field = BuildSessionField();

      DateTime? sessionStart;
      DateTime? sessionEnd;
      TimeSpan sessionDuration;
      bool sessionFinished;

      lock (ridersLock)
      {
        sessionStart = raceStartTime;
        sessionDuration = raceDuration;
        sessionFinished = raceFinished;
        sessionEnd = raceEndTime;
      }

      if (field.Count == 0)
      {
        MessageBox.Show(this,
          "Nobody has been out yet and no rider list has been imported, so there is " +
          "no gate pick order to print.",
          "Nothing to print", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
      }

      var defaultTitle = string.IsNullOrWhiteSpace(raceName)
        ? $"Gate Pick Order - {DateTime.Now:yyyy-MM-dd HH:mm}"
        : $"{raceName} - Gate Pick Order";

      using var options = new ReportOptionsDialog(defaultTitle);
      if (options.ShowDialog(this) != DialogResult.OK) return;

      var riders = field.ToDictionary(r => r.TagID, r => r);
      var title = options.RaceTitle;

      switch (options.SelectedAction)
      {
        case ReportAction.Preview:
          _qualifyingReportGenerator.ShowClassBasedPrintPreview(
            riders, title, sessionStart, sessionEnd, sessionDuration, sessionFinished);
          break;

        case ReportAction.Print:
          _qualifyingReportGenerator.PrintReport(
            riders, title, sessionStart, sessionEnd, sessionDuration, sessionFinished);
          break;

        case ReportAction.Export:
          _qualifyingReportGenerator.ExportToFile(
            riders, title, sessionStart, sessionEnd, sessionDuration, sessionFinished);
          break;
      }
    }
    catch (Exception ex)
    {
      ErrorDialog.Show(this, "The gate pick order could not be produced.",
        "Nothing has been changed. The lap data is unaffected, so you can try again.", ex);
    }
  }

  /// <summary>The classes present on the sheet, for the tab's filter.</summary>
  private List<string> QualifyingClasses(IEnumerable<RiderInfo> field) =>
    field.Where(r => !string.IsNullOrWhiteSpace(r.Category))
         .Select(r => r.Category)
         .Distinct(StringComparer.OrdinalIgnoreCase)
         .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
         .ToList();
}
