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
    private Label labelTitle;
    private GroupBox groupAction;
    private Button buttonCancel = null!;

  public ReportOptionsDialog() : this(null) { }

  /// <summary>
  /// <paramref name="defaultTitle"/> is normally the race name from setup, so the
  /// results sheet is titled correctly without the operator retyping it.
  /// </summary>
  public ReportOptionsDialog(string? defaultTitle)
  {
    InitializeComponent();

    textBoxRaceTitle.Text = string.IsNullOrWhiteSpace(defaultTitle)
      ? $"Race Results - {DateTime.Now:yyyy-MM-dd HH:mm}"
      : defaultTitle;

    // Default to preview
    radioPreview.Checked = true;
  }

    private void InitializeComponent()
    {
        textBoxRaceTitle = new TextBox();
        radioPreview = new RadioButton();
        radioPrint = new RadioButton();
        radioExport = new RadioButton();
        buttonOK = new Button();
        buttonCancel = new Button();
        labelTitle = new Label();
        groupAction = new GroupBox();
        groupAction.SuspendLayout();
        SuspendLayout();
        // 
        // textBoxRaceTitle
        // 
        textBoxRaceTitle.Location = new Point(12, 35);
        textBoxRaceTitle.Name = "textBoxRaceTitle";
        textBoxRaceTitle.Size = new Size(470, 31);
        textBoxRaceTitle.TabIndex = 0;
        // 
        // radioPreview
        // 
        radioPreview.Location = new Point(15, 25);
        radioPreview.Name = "radioPreview";
        radioPreview.Size = new Size(380, 31);
        radioPreview.TabIndex = 1;
        radioPreview.Text = "Preview (Print Preview)";
        radioPreview.UseVisualStyleBackColor = true;
        // 
        // radioPrint
        // 
        radioPrint.Location = new Point(15, 62);
        radioPrint.Name = "radioPrint";
        radioPrint.Size = new Size(380, 31);
        radioPrint.TabIndex = 2;
        radioPrint.Text = "Print (Send to Printer)";
        radioPrint.UseVisualStyleBackColor = true;
        // 
        // radioExport
        // 
        radioExport.Location = new Point(15, 99);
        radioExport.Name = "radioExport";
        radioExport.Size = new Size(380, 31);
        radioExport.TabIndex = 3;
        radioExport.Text = "Export to File (Save as Text)";
        radioExport.UseVisualStyleBackColor = true;
        // 
        // buttonOK
        // 
        buttonOK.DialogResult = DialogResult.OK;
        buttonOK.Location = new Point(326, 294);
        buttonOK.Name = "buttonOK";
        buttonOK.Size = new Size(75, 40);
        buttonOK.TabIndex = 4;
        buttonOK.Text = "OK";
        buttonOK.UseVisualStyleBackColor = true;
        buttonOK.Click += ButtonOK_Click;
        // 
        // buttonCancel
        // 
        buttonCancel.DialogResult = DialogResult.Cancel;
        buttonCancel.Location = new Point(407, 294);
        buttonCancel.Name = "buttonCancel";
        buttonCancel.Size = new Size(75, 40);
        buttonCancel.TabIndex = 5;
        buttonCancel.Text = "Cancel";
        buttonCancel.UseVisualStyleBackColor = true;
        // 
        // labelTitle
        // 
        labelTitle.Location = new Point(12, 9);
        labelTitle.Name = "labelTitle";
        labelTitle.Size = new Size(100, 23);
        labelTitle.TabIndex = 0;
        labelTitle.Text = "Race Title:";
        // 
        // groupAction
        // 
        groupAction.Controls.Add(radioPreview);
        groupAction.Controls.Add(radioPrint);
        groupAction.Controls.Add(radioExport);
        groupAction.Location = new Point(12, 72);
        groupAction.Name = "groupAction";
        groupAction.Size = new Size(470, 216);
        groupAction.TabIndex = 1;
        groupAction.TabStop = false;
        groupAction.Text = "Action";
        // 
        // ReportOptionsDialog
        // 
        AcceptButton = buttonOK;
        CancelButton = buttonCancel;
        ClientSize = new Size(494, 346);
        Controls.Add(labelTitle);
        Controls.Add(textBoxRaceTitle);
        Controls.Add(groupAction);
        Controls.Add(buttonOK);
        Controls.Add(buttonCancel);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "ReportOptionsDialog";
        StartPosition = FormStartPosition.CenterParent;
        Text = "Generate Race Report";
        groupAction.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
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
