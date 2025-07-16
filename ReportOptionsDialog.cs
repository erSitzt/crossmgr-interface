namespace CrossMgrInterface;

/// <summary>
/// Action to perform with the race report
/// </summary>
public enum ReportAction
{
  Preview,
  Print,
  Export
}

/// <summary>
/// Dialog for selecting race report options
/// </summary>
public partial class ReportOptionsDialog : Form
{
  public string RaceTitle { get; private set; } = "";
  public ReportAction SelectedAction { get; private set; } = ReportAction.Preview;

  private TextBox textBoxRaceTitle = null!;
  private RadioButton radioPreview = null!;
  private RadioButton radioPrint = null!;
  private RadioButton radioExport = null!;
  private Button buttonOK = null!;
  private Button buttonCancel = null!;

  public ReportOptionsDialog()
  {
    InitializeComponent();

    // Set default race title with current date/time
    textBoxRaceTitle.Text = $"Race Results - {DateTime.Now:yyyy-MM-dd HH:mm}";

    // Default to preview
    radioPreview.Checked = true;
  }

  private void InitializeComponent()
  {
    this.textBoxRaceTitle = new TextBox();
    this.radioPreview = new RadioButton();
    this.radioPrint = new RadioButton();
    this.radioExport = new RadioButton();
    this.buttonOK = new Button();
    this.buttonCancel = new Button();

    this.SuspendLayout();

    // Form properties
    this.Text = "Generate Race Report";
    this.Size = new Size(480, 320); // Increased size for better visibility
    this.StartPosition = FormStartPosition.CenterParent;
    this.FormBorderStyle = FormBorderStyle.FixedDialog;
    this.MaximizeBox = false;
    this.MinimizeBox = false;

    // Race Title Label
    var labelTitle = new Label();
    labelTitle.Text = "Race Title:";
    labelTitle.Location = new Point(20, 20);
    labelTitle.Size = new Size(100, 23);
    this.Controls.Add(labelTitle);    // Race Title TextBox
    this.textBoxRaceTitle.Location = new Point(20, 45);
    this.textBoxRaceTitle.Size = new Size(420, 23); // Wider text box
    this.textBoxRaceTitle.TabIndex = 0;
    this.Controls.Add(this.textBoxRaceTitle);

    // Action Group
    var groupAction = new GroupBox();
    groupAction.Text = "Action";
    groupAction.Location = new Point(20, 85);
    groupAction.Size = new Size(420, 120); // Larger group box
    this.Controls.Add(groupAction);

    // Preview Radio Button
    this.radioPreview.Text = "Preview (Print Preview)";
    this.radioPreview.Location = new Point(15, 25);
    this.radioPreview.Size = new Size(380, 20); // Wider radio buttons
    this.radioPreview.TabIndex = 1;
    this.radioPreview.UseVisualStyleBackColor = true;
    groupAction.Controls.Add(this.radioPreview);    // Print Radio Button
    this.radioPrint.Text = "Print (Send to Printer)";
    this.radioPrint.Location = new Point(15, 50); // Moved down slightly
    this.radioPrint.Size = new Size(380, 20); // Wider
    this.radioPrint.TabIndex = 2;
    this.radioPrint.UseVisualStyleBackColor = true;
    groupAction.Controls.Add(this.radioPrint);

    // Export Radio Button
    this.radioExport.Text = "Export to File (Save as Text)";
    this.radioExport.Location = new Point(15, 75); // Moved down slightly
    this.radioExport.Size = new Size(380, 20); // Wider
    this.radioExport.TabIndex = 3;
    this.radioExport.UseVisualStyleBackColor = true;
    groupAction.Controls.Add(this.radioExport);// OK Button
    this.buttonOK.Text = "OK";
    this.buttonOK.Location = new Point(280, 270); // Moved down for larger dialog
    this.buttonOK.Size = new Size(75, 23);
    this.buttonOK.TabIndex = 4;
    this.buttonOK.UseVisualStyleBackColor = true;
    this.buttonOK.DialogResult = DialogResult.OK;
    this.buttonOK.Click += ButtonOK_Click;
    this.Controls.Add(this.buttonOK);

    // Cancel Button
    this.buttonCancel.Text = "Cancel";
    this.buttonCancel.Location = new Point(365, 270); // Moved down and right for larger dialog
    this.buttonCancel.Size = new Size(75, 23);
    this.buttonCancel.TabIndex = 5;
    this.buttonCancel.UseVisualStyleBackColor = true;
    this.buttonCancel.DialogResult = DialogResult.Cancel;
    this.Controls.Add(this.buttonCancel);

    // Set accept and cancel buttons
    this.AcceptButton = this.buttonOK;
    this.CancelButton = this.buttonCancel;

    this.ResumeLayout(false);
  }

  private void ButtonOK_Click(object? sender, EventArgs e)
  {
    // Validate and save values
    RaceTitle = textBoxRaceTitle.Text.Trim();

    if (string.IsNullOrEmpty(RaceTitle))
    {
      MessageBox.Show("Please enter a race title.", "Validation Error",
        MessageBoxButtons.OK, MessageBoxIcon.Warning);
      textBoxRaceTitle.Focus();
      return;
    }

    // Determine selected action
    if (radioPreview.Checked)
      SelectedAction = ReportAction.Preview;
    else if (radioPrint.Checked)
      SelectedAction = ReportAction.Print;
    else if (radioExport.Checked)
      SelectedAction = ReportAction.Export;

    // Close dialog with OK result
    this.DialogResult = DialogResult.OK;
    this.Close();
  }
}
