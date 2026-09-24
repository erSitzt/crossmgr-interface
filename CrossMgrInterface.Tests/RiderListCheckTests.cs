using Xunit;

namespace CrossMgrInterface.Tests;

public class RiderListCheckTests
{
  private static RiderDataImporter.RiderImportData Row(string tag, string number, string name,
    string category = "MX1", string team = "")
  {
    var parts = name.Split(' ', 2);
    return new RiderDataImporter.RiderImportData
    {
      TagID = tag,
      RiderNumber = number,
      FirstName = parts[0],
      LastName = parts.Length > 1 ? parts[1] : "",
      Category = category,
      Team = team
    };
  }

  private static List<RiderListProblem> Check(bool teamEvent, params RiderDataImporter.RiderImportData[] rows) =>
    RiderListCheck.Run(rows, teamEvent).ToList();

  [Fact]
  public void A_clean_list_has_no_problems()
  {
    var problems = Check(false,
      Row("A1", "11", "Anna Berg"),
      Row("B2", "12", "Ben Fischer", "MX2"));

    Assert.Empty(problems);
  }

  [Fact]
  public void A_number_on_two_people_is_marked_on_both()
  {
    var problems = Check(false,
      Row("A1", "11", "Anna Berg"),
      Row("B2", "11", "Ben Fischer"));

    Assert.Equal(new[] { 0, 1 }, problems.Select(p => p.Row).OrderBy(r => r));
    Assert.All(problems, p => Assert.Equal(RiderListSeverity.Warning, p.Severity));
    Assert.Contains("#11 Ben Fischer", problems.First(p => p.Row == 0).Message);
  }

  [Fact]
  public void The_same_rider_with_a_spare_transponder_is_not_a_duplicate()
  {
    var problems = Check(false,
      Row("A1", "11", "Anna Berg"),
      Row("A2", "11", "Anna  Berg"));

    Assert.Empty(problems);
  }

  [Fact]
  public void A_transponder_on_two_people_is_an_error()
  {
    var problems = Check(false,
      Row("A1", "11", "Anna Berg"),
      Row("a1", "12", "Ben Fischer"));

    Assert.Equal(2, problems.Count);
    Assert.All(problems, p => Assert.Equal(RiderListSeverity.Error, p.Severity));
  }

  [Fact]
  public void Team_mates_sharing_a_transponder_are_left_to_the_team_roster()
  {
    var rows = new[]
    {
      Row("T1", "21", "Carla Hoff", team: "MSC Adler"),
      Row("T1", "22", "David Kern", team: "MSC Adler")
    };

    Assert.Empty(Check(true, rows));
    Assert.Equal(2, Check(false, rows).Count);
  }

  [Fact]
  public void A_missing_class_counts_only_when_the_list_has_classes()
  {
    var withClasses = Check(false, Row("A1", "11", "Anna Berg"), Row("B2", "12", "Ben Fischer", ""));
    Assert.Equal("No class", Assert.Single(withClasses).Message);

    Assert.Empty(Check(false, Row("A1", "11", "Anna Berg", ""), Row("B2", "12", "Ben Fischer", "")));
  }

  [Fact]
  public void Missing_number_name_and_transponder_are_marked()
  {
    var problems = Check(false, new RiderDataImporter.RiderImportData { Category = "MX1" });

    Assert.Contains(problems, p => p.Severity == RiderListSeverity.Error && p.Message.StartsWith("No transponder"));
    Assert.Contains(problems, p => p.Message == "No start number");
    Assert.Contains(problems, p => p.Message == "No name");
  }

  [Fact]
  public void The_summary_counts_riders_classes_and_riders_to_look_at()
  {
    var rows = new[] { Row("A1", "11", "Anna Berg"), Row("B2", "11", "Ben Fischer", "MX2"), Row("C3", "13", "Cem Aydin") };
    var problems = RiderListCheck.Run(rows, false);

    Assert.Equal("3 riders in 2 classes - 2 riders need a look", RiderListCheck.Summary(rows, problems));
  }

  // ---- Saving -------------------------------------------------------------------

  [Fact]
  public void A_saved_list_reads_back_as_it_was()
  {
    var folder = Path.Combine(Path.GetTempPath(), "crossmgr-riderlist-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
      var rows = new[]
      {
        new RiderDataImporter.RiderImportData
        {
          TagID = "10000001", RiderNumber = "007", FirstName = "Anna", LastName = "Berg",
          Team = "MSC Adler", Category = "MX1", Machine = "KTM", ShowName = false
        },
        new RiderDataImporter.RiderImportData { TagID = "E2003412", RiderNumber = "12", FirstName = "Ben" }
      };

      var path = Path.Combine(folder, "riders-edited.xlsx");
      RiderDataImporter.SaveToExcel(rows, path);

      var importer = new RiderDataImporter();
      var result = importer.ImportFromExcelDetailed(path);

      Assert.Equal(2, result.ImportedCount);
      Assert.Empty(result.Skipped);

      var anna = importer.Rows[0];
      Assert.Equal(("10000001", "007", "Anna", "Berg", "MSC Adler", "MX1", "KTM", (bool?)false),
        (anna.TagID, anna.RiderNumber, anna.FirstName, anna.LastName, anna.Team, anna.Category, anna.Machine, anna.ShowName));

      var ben = importer.Rows[1];
      Assert.Equal(("E2003412", "12", "Ben", "", (bool?)null), (ben.TagID, ben.RiderNumber, ben.FirstName, ben.LastName, ben.ShowName));
    }
    finally
    {
      Directory.Delete(folder, recursive: true);
    }
  }

  [Fact]
  public void Replacing_the_rows_rebuilds_the_lookup_and_forgets_skipped_rows()
  {
    var importer = new RiderDataImporter();
    importer.ReplaceRows(new[] { Row("A1", "11", "Anna Berg"), Row("A1", "11", "Anna Berg"), Row("", "13", "No Tag") });

    Assert.Equal(2, importer.Rows.Count);
    Assert.Equal(1, importer.Count);
    Assert.Equal("Anna", importer.GetRiderData("a1")!.FirstName);
    Assert.Empty(importer.LastSkipped);
  }

  [Theory]
  [InlineData(@"C:\Club\riders.xlsx", @"C:\Club\riders-edited.xlsx")]
  [InlineData(@"C:\Club\riders.csv", @"C:\Club\riders-edited.xlsx")]
  [InlineData(@"C:\Club\riders-edited.xlsx", @"C:\Club\riders-edited.xlsx")]
  [InlineData(null, @"D:\Fallback\riders-edited.xlsx")]
  [InlineData("riders.xlsx", @"D:\Fallback\riders-edited.xlsx")]
  public void An_edited_list_is_saved_beside_its_original(string? source, string expected)
  {
    Assert.Equal(expected, RiderDataImporter.EditedCopyPath(source, @"D:\Fallback"));
  }
}

public class RiderListDialogTests
{
  private static RiderDataImporter.RiderImportData Row(string tag, string number, string first, string category) =>
    new() { TagID = tag, RiderNumber = number, FirstName = first, LastName = "Test", Category = category };

  /// <summary>Runs <paramref name="body"/> on a thread a window can live on.</summary>
  private static void OnUiThread(Action body)
  {
    Exception? failure = null;
    var thread = new Thread(() =>
    {
      try { body(); }
      catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (failure != null) throw new Xunit.Sdk.XunitException(failure.ToString());
  }

  /// <summary>Types <paramref name="text"/> into a cell and commits it, as the operator would.</summary>
  private static void Type(DataGridView grid, int row, string column, string text)
  {
    grid.CurrentCell = grid.Rows[row].Cells[column];
    Assert.True(grid.BeginEdit(false));
    grid.EditingControl!.Text = text;
    grid.NotifyCurrentCellDirty(true);
    Assert.True(grid.EndEdit());
  }

  [Fact]
  public void Fixing_a_rider_under_Needs_a_look_keeps_them_in_view_and_saves_the_change()
  {
    OnUiThread(() =>
    {
      IReadOnlyList<RiderDataImporter.RiderImportData>? saved = null;
      var rows = new[] { Row("A1", "11", "Anna", "MX1"), Row("B2", "12", "Ben", "") };

      using var dialog = new RiderListDialog(new RiderListSource(rows, Array.Empty<(int, string)>()), false,
        save: r => { saved = r; return @"C:\Club\riders-edited.xlsx"; });
      dialog.Show();

      dialog.ClassFilter.SelectedIndex = 1; // Needs a look
      Assert.False(dialog.Grid.Rows[0].Visible);
      Assert.True(dialog.Grid.Rows[1].Visible);

      // A new class refills the class filter, from inside the grid's commit.
      Type(dialog.Grid, 1, "Class", "MX3");

      Assert.True(dialog.Grid.Rows[1].Visible);
      Assert.Equal("", dialog.Grid.Rows[1].Cells["Problem"].Value);
      Assert.Contains("MX3", dialog.ClassFilter.Items.Cast<object>().Select(i => i.ToString()));
      Assert.True(dialog.HasUnsavedChanges);

      TryFind(dialog, "Save")!.PerformClick();

      Assert.NotNull(saved);
      Assert.Equal("MX3", saved![1].Category);
      Assert.Equal("", rows[1].Category); // the list in use was not touched
      Assert.False(dialog.HasUnsavedChanges);
      dialog.Close();
    });
  }

  [Fact]
  public void A_scanned_transponder_goes_on_the_selected_rider_unless_someone_has_it()
  {
    OnUiThread(() =>
    {
      var rows = new[] { Row("A1", "11", "Anna", "MX1"), Row("B2", "12", "Ben", "MX1") };
      using var dialog = new RiderListDialog(new RiderListSource(rows, Array.Empty<(int, string)>()), false);
      dialog.Show();

      dialog.SelectRow(1);
      dialog.ToggleScan();
      Assert.True(dialog.IsScanning);

      dialog.OfferRead("A1");
      Assert.False(dialog.IsScanning);
      Assert.Equal("B2", dialog.Grid.Rows[1].Cells["Transponder"].Value);

      dialog.ToggleScan();
      dialog.OfferRead("C3");
      Assert.Equal("C3", dialog.Grid.Rows[1].Cells["Transponder"].Value);
      Assert.True(dialog.HasUnsavedChanges);
      dialog.Close();
    });
  }

  private static Button? TryFind(Control root, string text)
  {
    foreach (Control child in root.Controls)
    {
      if (child is Button b && b.Text == text) return b;
      if (TryFind(child, text) is { } found) return found;
    }
    return null;
  }
}
