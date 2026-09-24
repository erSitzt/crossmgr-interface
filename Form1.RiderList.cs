namespace CrossMgrInterface;

/// <summary>
/// The Rider list window: the imported list checked and corrected before the
/// first session, and saved as a copy the application uses from then on.
/// </summary>
public partial class Form1
{
  /// <summary>The open Rider list, so a read can reach it while it is scanning. Null when closed.</summary>
  private volatile RiderListDialog? _riderListDialog;

  private void ShowRiderList()
  {
    using var dialog = new RiderListDialog(
      CurrentRiderListSource(),
      teamEvent,
      SaveEditedRiderList,
      ImportRiderListAgain);

    _riderListDialog = dialog;
    try
    {
      dialog.ShowDialog(this);
    }
    finally
    {
      _riderListDialog = null;
    }
  }

  private RiderListSource CurrentRiderListSource() =>
    new(_riderDataImporter.Rows.ToList(), _riderDataImporter.LastSkipped);

  /// <summary>Called on the reader's thread for every read; hands it to the Rider list only while it is scanning.</summary>
  private void OfferReadToRiderList(string tagId)
  {
    if (_riderListDialog is not { IsScanning: true } dialog || dialog.IsDisposed) return;

    try
    {
      dialog.BeginInvoke(() => dialog.OfferRead(tagId));
    }
    catch (InvalidOperationException)
    {
      // Closed between the check and the call: nothing to fill in any more.
    }
  }

  /// <summary>
  /// Saves the edited list beside the one it came from and puts it in use, the
  /// way an import would. Returns where it went, or null when it could not be
  /// written.
  /// </summary>
  private string? SaveEditedRiderList(IReadOnlyList<RiderDataImporter.RiderImportData> rows)
  {
    var path = RiderDataImporter.EditedCopyPath(_settings.LastRiderListPath, AppPaths.RiderListsFolder);

    try
    {
      RiderDataImporter.SaveToExcel(rows, path);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      // Beside the original was not possible - a write-protected stick, or the
      // file open in Excel. The application's own folder always is.
      // A bare file name has no folder of its own, so this lands in the fallback.
      var fallback = RiderDataImporter.EditedCopyPath(Path.GetFileName(_settings.LastRiderListPath),
        AppPaths.RiderListsFolder);

      try
      {
        RiderDataImporter.SaveToExcel(rows, fallback);
        AddDiagnostic($"Could not save the rider list to {path} ({ex.Message}); saved to {fallback} instead");
        path = fallback;
      }
      catch (Exception inner)
      {
        MessageBox.Show(this,
          $"The rider list could not be saved.\n\n{inner.Message}",
          "Save rider list", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return null;
      }
    }

    var previous = _settings.LastRiderListPath;
    var changedTransponders = TranspondersRacingThatLeftTheList(rows);

    _riderDataImporter.ReplaceRows(rows);
    RebuildTeamRoster();
    ReportTeamRoster();
    ApplyImportedDataToExistingRiders(overwrite: true);
    PopulateClassFilter();
    RememberRiderList(path);

    AddMessage($"📋 Rider list saved to {Path.GetFileName(path)} ({rows.Count} riders)" +
               (previous != null && !string.Equals(previous, path, StringComparison.OrdinalIgnoreCase)
                 ? $" - {Path.GetFileName(previous)} is left as it was"
                 : ""));

    // Laps belong to the transponder that was read. Taking a transponder off the
    // list does not move them - that is what Identify is for.
    foreach (var (tag, label, laps) in changedTransponders)
    {
      AddMessage($"⚠️ {label} already has {laps} lap(s) on transponder {tag}, which is no longer on the list. " +
                 "To move those laps, right-click the rider in the Riders list and use Identify.");
    }

    _refresh.RenderNow(RaceViewKind.All);
    return path;
  }

  /// <summary>Riders with laps whose transponder is not on the new list any more.</summary>
  private List<(string Tag, string Label, int Laps)> TranspondersRacingThatLeftTheList(
    IReadOnlyList<RiderDataImporter.RiderImportData> rows)
  {
    var listed = rows.Select(r => r.TagID.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

    lock (ridersLock)
    {
      return riders.Values
        .Where(r => !r.IsTeam && r.TotalLaps > 0 && !ignoredTags.Contains(r.TagID))
        .Where(r => _riderDataImporter.HasRiderData(r.TagID) && !listed.Contains(r.TagID))
        .Select(r => (r.TagID, r.Label, r.TotalLaps))
        .ToList();
    }
  }

  /// <summary>Import riders..., from inside the Rider list. The new list, or null when the operator cancelled.</summary>
  private RiderListSource? ImportRiderListAgain() =>
    ImportRidersFromFile() ? CurrentRiderListSource() : null;

  /// <summary>
  /// Puts the list's number, name, team, class and machine on a rider already
  /// being timed. True when anything changed. The caller holds ridersLock.
  /// </summary>
  private bool OverwriteFromList(RiderInfo rider, RiderDataImporter.RiderImportData listed)
  {
    var changed = false;

    void Set(Func<string> get, Action<string> set, string value)
    {
      if (string.Equals(get(), value, StringComparison.Ordinal)) return;
      set(value);
      changed = true;
    }

    Set(() => rider.RiderNumber, v => rider.RiderNumber = v, listed.RiderNumber);
    Set(() => rider.FirstName, v => rider.FirstName = v, listed.FirstName);
    Set(() => rider.LastName, v => rider.LastName = v, listed.LastName);
    Set(() => rider.Team, v => rider.Team = v, listed.Team);
    Set(() => rider.Machine, v => rider.Machine = v, listed.Machine);

    if (!string.Equals(rider.Category, listed.Category, StringComparison.Ordinal))
    {
      // Same rule as Change class in Fix laps: a read from a class still at the
      // gate is thrown away, so moving a rider into one would stop their laps.
      if (waves != null && raceStarted && !waves.HasStarted(listed.Category))
      {
        AddMessage($"⚠️ {rider.Label} stays in {rider.Category}: {listed.Category} has not left the gate yet. " +
                   "Change the class again once it has started.");
      }
      else
      {
        rider.Category = listed.Category;
        changed = true;
      }
    }

    if (listed.ShowName.HasValue && rider.ShowName != listed.ShowName)
    {
      rider.ShowName = listed.ShowName;
      changed = true;
    }

    return changed;
  }
}
