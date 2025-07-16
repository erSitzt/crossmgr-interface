namespace CrossMgrInterface;

partial class Form1
{
  /// <summary>
  ///  Required designer variable.
  /// </summary>
  private System.ComponentModel.IContainer components = null;

  /// <summary>
  ///  Clean up any resources being used.
  /// </summary>
  /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
  protected override void Dispose(bool disposing)
  {
    if (disposing && (components != null))
    {
      components.Dispose();
    }
    if (disposing)
    {
      _raceReportGenerator?.Dispose();
    }
    base.Dispose(disposing);
  }

  #region Windows Form Designer generated code

  /// <summary>
  ///  Required method for Designer support - do not modify
  ///  the contents of this method with the code editor.
  /// </summary>
  private System.Windows.Forms.ListBox listBoxMessages;
  private System.Windows.Forms.ListBox listBoxTagEvents;
  private System.Windows.Forms.Label labelStatus;
  private System.Windows.Forms.Button buttonStart;
  private System.Windows.Forms.Button buttonStop;
  private System.Windows.Forms.TextBox textBoxPort;
  private System.Windows.Forms.Label labelPort;
  private System.Windows.Forms.Button buttonClear;
  private System.Windows.Forms.Button buttonClearTagEvents;
  private System.Windows.Forms.Label labelConnections;
  private System.Windows.Forms.Button buttonShowSummary;
  private System.Windows.Forms.Button buttonClearRiders;
  private System.Windows.Forms.Button buttonGenerateReport;
  private System.Windows.Forms.Label labelRaceDuration;
  private System.Windows.Forms.NumericUpDown numericUpDownRaceDuration;
  private System.Windows.Forms.Button buttonSetDuration;
  private System.Windows.Forms.TabControl tabControl;
  private System.Windows.Forms.TabPage tabPageLive;
  private System.Windows.Forms.TabPage tabPageTagEvents;
  private System.Windows.Forms.TabPage tabPageRiders;
  private System.Windows.Forms.TabPage tabPageStats;
  private System.Windows.Forms.TabPage tabPageLapChart;
  private System.Windows.Forms.TabPage tabPageRaceSettings;
  private System.Windows.Forms.DataGridView dataGridViewRiders;
  private System.Windows.Forms.Label labelRaceTime;
  private System.Windows.Forms.Label labelTotalRiders;
  private System.Windows.Forms.Label labelTotalLaps;
  private System.Windows.Forms.Label labelLastTag;
  private System.Windows.Forms.Label labelNextCrossing;
  private System.Windows.Forms.Label labelRaceEndTime;
  private System.Windows.Forms.Label labelTimeRemaining;
  private System.Windows.Forms.Label labelPredictedLaps;
  private System.Windows.Forms.Timer timerUpdate;
  private System.Windows.Forms.Label labelTagFilter;
  private System.Windows.Forms.TextBox textBoxTagFilter;
  private System.Windows.Forms.Button buttonSetFilter;
  private System.Windows.Forms.CheckBox checkBoxFilterEnabled;
  private System.Windows.Forms.Panel panelLapChart;
  private System.Windows.Forms.GroupBox groupBoxRaceStart;
  private System.Windows.Forms.RadioButton radioButtonStartOnFirstTag;
  private System.Windows.Forms.RadioButton radioButtonStartManual;
  private System.Windows.Forms.Button buttonStartRace;
  private System.Windows.Forms.Label labelRaceStatus;
  private System.Windows.Forms.Label labelFilterEnabled;
  private System.Windows.Forms.Label labelAdditionalLaps;
  private System.Windows.Forms.NumericUpDown numericUpDownAdditionalLaps;
  private System.Windows.Forms.Button buttonSetAdditionalLaps;

  // Short lap detection controls
  private System.Windows.Forms.Label labelMinimumLapTime;
  private System.Windows.Forms.NumericUpDown numericUpDownMinimumLapTime;
  private System.Windows.Forms.CheckBox checkBoxShortLapDetection;
  private System.Windows.Forms.Button buttonSetShortLapSettings;

  private void InitializeComponent()
  {
    components = new System.ComponentModel.Container();
    labelStatus = new Label();
    buttonStart = new Button();
    buttonStop = new Button();
    textBoxPort = new TextBox();
    labelPort = new Label();
    buttonClear = new Button();
    labelConnections = new Label();
    buttonShowSummary = new Button();
    buttonClearRiders = new Button();
    buttonGenerateReport = new Button();
    labelRaceDuration = new Label();
    numericUpDownRaceDuration = new NumericUpDown();
    buttonSetDuration = new Button();
    tabControl = new TabControl();
    tabPageLive = new TabPage();
    listBoxMessages = new ListBox();
    tabPageTagEvents = new TabPage();
    listBoxTagEvents = new ListBox();
    tabPageRiders = new TabPage();
    dataGridViewRiders = new DataGridView();
    tabPageStats = new TabPage();
    labelPredictedLaps = new Label();
    labelTimeRemaining = new Label();
    labelRaceEndTime = new Label();
    labelNextCrossing = new Label();
    labelLastTag = new Label();
    labelTotalLaps = new Label();
    labelTotalRiders = new Label();
    labelRaceTime = new Label();
    tabPageLapChart = new TabPage();
    panelLapChart = new Panel();
    tabPageRaceSettings = new TabPage();
    labelAdditionalLaps = new Label();
    numericUpDownAdditionalLaps = new NumericUpDown();
    buttonSetAdditionalLaps = new Button();
    labelMinimumLapTime = new Label();
    numericUpDownMinimumLapTime = new NumericUpDown();
    checkBoxShortLapDetection = new CheckBox();
    buttonSetShortLapSettings = new Button();
    groupBoxRaceStart = new GroupBox();
    radioButtonStartOnFirstTag = new RadioButton();
    radioButtonStartManual = new RadioButton();
    buttonStartRace = new Button();
    labelRaceStatus = new Label();
    labelTagFilter = new Label();
    textBoxTagFilter = new TextBox();
    buttonSetFilter = new Button();
    checkBoxFilterEnabled = new CheckBox();
    labelFilterEnabled = new Label();
    timerUpdate = new System.Windows.Forms.Timer(components);
    ((System.ComponentModel.ISupportInitialize)numericUpDownRaceDuration).BeginInit();
    tabControl.SuspendLayout();
    tabPageLive.SuspendLayout();
    tabPageTagEvents.SuspendLayout();
    tabPageRiders.SuspendLayout();
    ((System.ComponentModel.ISupportInitialize)dataGridViewRiders).BeginInit();
    tabPageStats.SuspendLayout();
    tabPageLapChart.SuspendLayout();
    tabPageRaceSettings.SuspendLayout();
    ((System.ComponentModel.ISupportInitialize)numericUpDownAdditionalLaps).BeginInit();
    ((System.ComponentModel.ISupportInitialize)numericUpDownMinimumLapTime).BeginInit();
    groupBoxRaceStart.SuspendLayout();
    SuspendLayout();
    // 
    // labelStatus
    // 
    labelStatus.AutoSize = true;
    labelStatus.Font = new Font("Microsoft Sans Serif", 9F, FontStyle.Bold);
    labelStatus.ForeColor = Color.Red;
    labelStatus.Location = new Point(17, 92);
    labelStatus.Margin = new Padding(4, 0, 4, 0);
    labelStatus.Name = "labelStatus";
    labelStatus.Size = new Size(84, 22);
    labelStatus.TabIndex = 1;
    labelStatus.Text = "Stopped";
    // 
    // buttonStart
    // 
    buttonStart.Location = new Point(200, 20);
    buttonStart.Margin = new Padding(4, 5, 4, 5);
    buttonStart.Name = "buttonStart";
    buttonStart.Size = new Size(107, 38);
    buttonStart.TabIndex = 2;
    buttonStart.Text = "Start";
    buttonStart.UseVisualStyleBackColor = true;
    buttonStart.Click += buttonStart_Click;
    // 
    // buttonStop
    // 
    buttonStop.Enabled = false;
    buttonStop.Location = new Point(316, 20);
    buttonStop.Margin = new Padding(4, 5, 4, 5);
    buttonStop.Name = "buttonStop";
    buttonStop.Size = new Size(107, 38);
    buttonStop.TabIndex = 3;
    buttonStop.Text = "Stop";
    buttonStop.UseVisualStyleBackColor = true;
    buttonStop.Click += buttonStop_Click;
    // 
    // textBoxPort
    // 
    textBoxPort.Location = new Point(69, 25);
    textBoxPort.Margin = new Padding(4, 5, 4, 5);
    textBoxPort.Name = "textBoxPort";
    textBoxPort.Size = new Size(123, 31);
    textBoxPort.TabIndex = 4;
    textBoxPort.Text = "53135";
    // 
    // labelPort
    // 
    labelPort.AutoSize = true;
    labelPort.Location = new Point(17, 28);
    labelPort.Margin = new Padding(4, 0, 4, 0);
    labelPort.Name = "labelPort";
    labelPort.Size = new Size(48, 25);
    labelPort.TabIndex = 5;
    labelPort.Text = "Port:";
    // 
    // buttonClear
    // 
    buttonClear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
    buttonClear.Location = new Point(1567, 20);
    buttonClear.Margin = new Padding(4, 5, 4, 5);
    buttonClear.Name = "buttonClear";
    buttonClear.Size = new Size(107, 38);
    buttonClear.TabIndex = 6;
    buttonClear.Text = "Clear";
    buttonClear.UseVisualStyleBackColor = true;
    buttonClear.Click += buttonClear_Click;
    // 
    // labelConnections
    // 
    labelConnections.AutoSize = true;
    labelConnections.Location = new Point(431, 27);
    labelConnections.Margin = new Padding(4, 0, 4, 0);
    labelConnections.Name = "labelConnections";
    labelConnections.Size = new Size(129, 25);
    labelConnections.TabIndex = 7;
    labelConnections.Text = "Connections: 0";
    // 
    // buttonShowSummary
    // 
    buttonShowSummary.Location = new Point(571, 20);
    buttonShowSummary.Margin = new Padding(4, 5, 4, 5);
    buttonShowSummary.Name = "buttonShowSummary";
    buttonShowSummary.Size = new Size(143, 38);
    buttonShowSummary.TabIndex = 8;
    buttonShowSummary.Text = "Show Summary";
    buttonShowSummary.UseVisualStyleBackColor = true;
    buttonShowSummary.Click += buttonShowSummary_Click;
    // 
    // buttonClearRiders
    // 
    buttonClearRiders.Location = new Point(729, 20);
    buttonClearRiders.Margin = new Padding(4, 5, 4, 5);
    buttonClearRiders.Name = "buttonClearRiders";
    buttonClearRiders.Size = new Size(129, 38);
    buttonClearRiders.TabIndex = 9;
    buttonClearRiders.Text = "Clear Riders";
    buttonClearRiders.UseVisualStyleBackColor = true;
    buttonClearRiders.Click += buttonClearRiders_Click;
    // 
    // buttonGenerateReport
    // 
    buttonGenerateReport.Location = new Point(871, 20);
    buttonGenerateReport.Margin = new Padding(4, 5, 4, 5);
    buttonGenerateReport.Name = "buttonGenerateReport";
    buttonGenerateReport.Size = new Size(150, 38);
    buttonGenerateReport.TabIndex = 10;
    buttonGenerateReport.Text = "Generate Report";
    buttonGenerateReport.UseVisualStyleBackColor = true;
    buttonGenerateReport.Click += buttonGenerateReport_Click;
    // 
    // labelRaceDuration
    // 
    labelRaceDuration.AutoSize = true;
    labelRaceDuration.Location = new Point(29, 33);
    labelRaceDuration.Margin = new Padding(4, 0, 4, 0);
    labelRaceDuration.Name = "labelRaceDuration";
    labelRaceDuration.Size = new Size(172, 25);
    labelRaceDuration.TabIndex = 10;
    labelRaceDuration.Text = "Race Duration (min):";
    // 
    // numericUpDownRaceDuration
    // 
    numericUpDownRaceDuration.Location = new Point(295, 31);
    numericUpDownRaceDuration.Margin = new Padding(4, 5, 4, 5);
    numericUpDownRaceDuration.Maximum = new decimal(new int[] { 180, 0, 0, 0 });
    numericUpDownRaceDuration.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
    numericUpDownRaceDuration.Name = "numericUpDownRaceDuration";
    numericUpDownRaceDuration.Size = new Size(171, 31);
    numericUpDownRaceDuration.TabIndex = 11;
    numericUpDownRaceDuration.Value = new decimal(new int[] { 20, 0, 0, 0 });
    // 
    // buttonSetDuration
    // 
    buttonSetDuration.Location = new Point(474, 26);
    buttonSetDuration.Margin = new Padding(4, 5, 4, 5);
    buttonSetDuration.Name = "buttonSetDuration";
    buttonSetDuration.Size = new Size(107, 38);
    buttonSetDuration.TabIndex = 12;
    buttonSetDuration.Text = "Set";
    buttonSetDuration.UseVisualStyleBackColor = true;
    buttonSetDuration.Click += buttonSetDuration_Click;
    // 
    // tabControl
    // 
    tabControl.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
    tabControl.Controls.Add(tabPageLive);
    tabControl.Controls.Add(tabPageTagEvents);
    tabControl.Controls.Add(tabPageRiders);
    tabControl.Controls.Add(tabPageStats);
    tabControl.Controls.Add(tabPageLapChart);
    tabControl.Controls.Add(tabPageRaceSettings);
    tabControl.Location = new Point(17, 83);
    tabControl.Margin = new Padding(4, 5, 4, 5);
    tabControl.Name = "tabControl";
    tabControl.SelectedIndex = 0;
    tabControl.Size = new Size(1657, 917);
    tabControl.TabIndex = 10;
    // 
    // tabPageLive
    // 
    tabPageLive.Controls.Add(listBoxMessages);
    tabPageLive.Location = new Point(4, 34);
    tabPageLive.Margin = new Padding(4, 5, 4, 5);
    tabPageLive.Name = "tabPageLive";
    tabPageLive.Padding = new Padding(4, 5, 4, 5);
    tabPageLive.Size = new Size(1649, 879);
    tabPageLive.TabIndex = 0;
    tabPageLive.Text = "Race Events";
    tabPageLive.UseVisualStyleBackColor = true;
    // 
    // listBoxMessages
    // 
    listBoxMessages.Dock = DockStyle.Fill;
    listBoxMessages.Font = new Font("Consolas", 9F);
    listBoxMessages.HorizontalScrollbar = true;
    listBoxMessages.Location = new Point(4, 5);
    listBoxMessages.Margin = new Padding(4, 5, 4, 5);
    listBoxMessages.Name = "listBoxMessages";
    listBoxMessages.Size = new Size(1641, 869);
    listBoxMessages.TabIndex = 0;
    // 
    // tabPageTagEvents
    // 
    tabPageTagEvents.Controls.Add(listBoxTagEvents);
    tabPageTagEvents.Location = new Point(4, 34);
    tabPageTagEvents.Margin = new Padding(4, 5, 4, 5);
    tabPageTagEvents.Name = "tabPageTagEvents";
    tabPageTagEvents.Padding = new Padding(4, 5, 4, 5);
    tabPageTagEvents.Size = new Size(1649, 879);
    tabPageTagEvents.TabIndex = 1;
    tabPageTagEvents.Text = "Tag Events";
    tabPageTagEvents.UseVisualStyleBackColor = true;
    // 
    // listBoxTagEvents
    // 
    listBoxTagEvents.Dock = DockStyle.Fill;
    listBoxTagEvents.Font = new Font("Consolas", 9F);
    listBoxTagEvents.HorizontalScrollbar = true;
    listBoxTagEvents.Location = new Point(4, 5);
    listBoxTagEvents.Margin = new Padding(4, 5, 4, 5);
    listBoxTagEvents.Name = "listBoxTagEvents";
    listBoxTagEvents.Size = new Size(1641, 869);
    listBoxTagEvents.TabIndex = 0;
    // 
    // tabPageRiders
    // 
    tabPageRiders.Controls.Add(dataGridViewRiders);
    tabPageRiders.Location = new Point(4, 34);
    tabPageRiders.Margin = new Padding(4, 5, 4, 5);
    tabPageRiders.Name = "tabPageRiders";
    tabPageRiders.Padding = new Padding(4, 5, 4, 5);
    tabPageRiders.Size = new Size(1649, 879);
    tabPageRiders.TabIndex = 2;
    tabPageRiders.Text = "Riders";
    tabPageRiders.UseVisualStyleBackColor = true;
    // 
    // dataGridViewRiders
    // 
    dataGridViewRiders.AllowUserToAddRows = false;
    dataGridViewRiders.AllowUserToDeleteRows = false;
    dataGridViewRiders.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
    dataGridViewRiders.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
    dataGridViewRiders.Dock = DockStyle.Fill;
    dataGridViewRiders.Location = new Point(4, 5);
    dataGridViewRiders.Margin = new Padding(4, 5, 4, 5);
    dataGridViewRiders.Name = "dataGridViewRiders";
    dataGridViewRiders.ReadOnly = true;
    dataGridViewRiders.RowHeadersVisible = false;
    dataGridViewRiders.RowHeadersWidth = 62;
    dataGridViewRiders.RowTemplate.Height = 25;
    dataGridViewRiders.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
    dataGridViewRiders.Size = new Size(1641, 869);
    dataGridViewRiders.TabIndex = 0;
    // 
    // tabPageStats
    // 
    tabPageStats.Controls.Add(labelPredictedLaps);
    tabPageStats.Controls.Add(labelTimeRemaining);
    tabPageStats.Controls.Add(labelRaceEndTime);
    tabPageStats.Controls.Add(labelNextCrossing);
    tabPageStats.Controls.Add(labelLastTag);
    tabPageStats.Controls.Add(labelTotalLaps);
    tabPageStats.Controls.Add(labelTotalRiders);
    tabPageStats.Controls.Add(labelRaceTime);
    tabPageStats.Location = new Point(4, 34);
    tabPageStats.Margin = new Padding(4, 5, 4, 5);
    tabPageStats.Name = "tabPageStats";
    tabPageStats.Size = new Size(1649, 879);
    tabPageStats.TabIndex = 3;
    tabPageStats.Text = "Race Statistics";
    tabPageStats.UseVisualStyleBackColor = true;
    // 
    // labelPredictedLaps
    // 
    labelPredictedLaps.AutoSize = true;
    labelPredictedLaps.Font = new Font("Microsoft Sans Serif", 12F);
    labelPredictedLaps.Location = new Point(43, 433);
    labelPredictedLaps.Margin = new Padding(4, 0, 4, 0);
    labelPredictedLaps.Name = "labelPredictedLaps";
    labelPredictedLaps.Size = new Size(325, 29);
    labelPredictedLaps.TabIndex = 7;
    labelPredictedLaps.Text = "Predicted Laps (Leader): N/A";
    // 
    // labelTimeRemaining
    // 
    labelTimeRemaining.AutoSize = true;
    labelTimeRemaining.Font = new Font("Microsoft Sans Serif", 12F, FontStyle.Bold);
    labelTimeRemaining.ForeColor = Color.DarkRed;
    labelTimeRemaining.Location = new Point(43, 383);
    labelTimeRemaining.Margin = new Padding(4, 0, 4, 0);
    labelTimeRemaining.Name = "labelTimeRemaining";
    labelTimeRemaining.Size = new Size(262, 29);
    labelTimeRemaining.TabIndex = 6;
    labelTimeRemaining.Text = "Time Remaining: N/A";
    // 
    // labelRaceEndTime
    // 
    labelRaceEndTime.AutoSize = true;
    labelRaceEndTime.Font = new Font("Microsoft Sans Serif", 12F);
    labelRaceEndTime.Location = new Point(43, 333);
    labelRaceEndTime.Margin = new Padding(4, 0, 4, 0);
    labelRaceEndTime.Name = "labelRaceEndTime";
    labelRaceEndTime.Size = new Size(210, 29);
    labelRaceEndTime.TabIndex = 5;
    labelRaceEndTime.Text = "Race End: Not Set";
    // 
    // labelNextCrossing
    // 
    labelNextCrossing.AutoSize = true;
    labelNextCrossing.Font = new Font("Microsoft Sans Serif", 12F);
    labelNextCrossing.Location = new Point(43, 283);
    labelNextCrossing.Margin = new Padding(4, 0, 4, 0);
    labelNextCrossing.Name = "labelNextCrossing";
    labelNextCrossing.Size = new Size(318, 29);
    labelNextCrossing.TabIndex = 4;
    labelNextCrossing.Text = "Next Expected: Calculating...";
    // 
    // labelLastTag
    // 
    labelLastTag.AutoSize = true;
    labelLastTag.Font = new Font("Microsoft Sans Serif", 12F);
    labelLastTag.Location = new Point(43, 233);
    labelLastTag.Margin = new Padding(4, 0, 4, 0);
    labelLastTag.Name = "labelLastTag";
    labelLastTag.Size = new Size(177, 29);
    labelLastTag.TabIndex = 3;
    labelLastTag.Text = "Last Tag: None";
    // 
    // labelTotalLaps
    // 
    labelTotalLaps.AutoSize = true;
    labelTotalLaps.Font = new Font("Microsoft Sans Serif", 12F);
    labelTotalLaps.Location = new Point(43, 183);
    labelTotalLaps.Margin = new Padding(4, 0, 4, 0);
    labelTotalLaps.Name = "labelTotalLaps";
    labelTotalLaps.Size = new Size(151, 29);
    labelTotalLaps.TabIndex = 2;
    labelTotalLaps.Text = "Total Laps: 0";
    // 
    // labelTotalRiders
    // 
    labelTotalRiders.AutoSize = true;
    labelTotalRiders.Font = new Font("Microsoft Sans Serif", 12F);
    labelTotalRiders.Location = new Point(43, 133);
    labelTotalRiders.Margin = new Padding(4, 0, 4, 0);
    labelTotalRiders.Name = "labelTotalRiders";
    labelTotalRiders.Size = new Size(170, 29);
    labelTotalRiders.TabIndex = 1;
    labelTotalRiders.Text = "Total Riders: 0";
    // 
    // labelRaceTime
    // 
    labelRaceTime.AutoSize = true;
    labelRaceTime.Font = new Font("Microsoft Sans Serif", 14.25F, FontStyle.Bold);
    labelRaceTime.Location = new Point(43, 50);
    labelRaceTime.Margin = new Padding(4, 0, 4, 0);
    labelRaceTime.Name = "labelRaceTime";
    labelRaceTime.Size = new Size(261, 33);
    labelRaceTime.TabIndex = 0;
    labelRaceTime.Text = "Race Time: 00:00";
    // 
    // tabPageLapChart
    // 
    tabPageLapChart.Controls.Add(panelLapChart);
    tabPageLapChart.Location = new Point(4, 34);
    tabPageLapChart.Margin = new Padding(4, 5, 4, 5);
    tabPageLapChart.Name = "tabPageLapChart";
    tabPageLapChart.Padding = new Padding(4, 5, 4, 5);
    tabPageLapChart.Size = new Size(1649, 879);
    tabPageLapChart.TabIndex = 4;
    tabPageLapChart.Text = "Lap Chart";
    tabPageLapChart.UseVisualStyleBackColor = true;
    // 
    // panelLapChart
    // 
    panelLapChart.AutoScroll = true;
    panelLapChart.BackColor = Color.White;
    panelLapChart.Dock = DockStyle.Fill;
    panelLapChart.Location = new Point(4, 5);
    panelLapChart.Margin = new Padding(4, 5, 4, 5);
    panelLapChart.Name = "panelLapChart";
    panelLapChart.Size = new Size(1641, 869);
    panelLapChart.TabIndex = 0;
    panelLapChart.Paint += panelLapChart_Paint;
    // 
    // tabPageRaceSettings
    // 
    tabPageRaceSettings.Controls.Add(buttonSetShortLapSettings);
    tabPageRaceSettings.Controls.Add(checkBoxShortLapDetection);
    tabPageRaceSettings.Controls.Add(numericUpDownMinimumLapTime);
    tabPageRaceSettings.Controls.Add(labelMinimumLapTime);
    tabPageRaceSettings.Controls.Add(labelAdditionalLaps);
    tabPageRaceSettings.Controls.Add(numericUpDownAdditionalLaps);
    tabPageRaceSettings.Controls.Add(buttonSetAdditionalLaps);
    tabPageRaceSettings.Controls.Add(groupBoxRaceStart);
    tabPageRaceSettings.Controls.Add(labelTagFilter);
    tabPageRaceSettings.Controls.Add(textBoxTagFilter);
    tabPageRaceSettings.Controls.Add(buttonSetFilter);
    tabPageRaceSettings.Controls.Add(checkBoxFilterEnabled);
    tabPageRaceSettings.Controls.Add(labelFilterEnabled);
    tabPageRaceSettings.Controls.Add(labelRaceDuration);
    tabPageRaceSettings.Controls.Add(numericUpDownRaceDuration);
    tabPageRaceSettings.Controls.Add(buttonSetDuration);
    tabPageRaceSettings.Location = new Point(4, 34);
    tabPageRaceSettings.Margin = new Padding(4, 5, 4, 5);
    tabPageRaceSettings.Name = "tabPageRaceSettings";
    tabPageRaceSettings.Padding = new Padding(4, 5, 4, 5);
    tabPageRaceSettings.Size = new Size(1649, 879);
    tabPageRaceSettings.TabIndex = 5;
    tabPageRaceSettings.Text = "Race Settings";
    tabPageRaceSettings.UseVisualStyleBackColor = true;
    // 
    // labelAdditionalLaps
    // 
    labelAdditionalLaps.AutoSize = true;
    labelAdditionalLaps.Location = new Point(29, 74);
    labelAdditionalLaps.Margin = new Padding(4, 0, 4, 0);
    labelAdditionalLaps.Name = "labelAdditionalLaps";
    labelAdditionalLaps.Size = new Size(226, 25);
    labelAdditionalLaps.TabIndex = 13;
    labelAdditionalLaps.Text = "Additional Laps After Time:";
    // 
    // numericUpDownAdditionalLaps
    // 
    numericUpDownAdditionalLaps.Location = new Point(295, 72);
    numericUpDownAdditionalLaps.Margin = new Padding(4, 5, 4, 5);
    numericUpDownAdditionalLaps.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
    numericUpDownAdditionalLaps.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
    numericUpDownAdditionalLaps.Name = "numericUpDownAdditionalLaps";
    numericUpDownAdditionalLaps.Size = new Size(171, 31);
    numericUpDownAdditionalLaps.TabIndex = 14;
    numericUpDownAdditionalLaps.Value = new decimal(new int[] { 1, 0, 0, 0 });
    // 
    // buttonSetAdditionalLaps
    // 
    buttonSetAdditionalLaps.Location = new Point(474, 67);
    buttonSetAdditionalLaps.Margin = new Padding(4, 5, 4, 5);
    buttonSetAdditionalLaps.Name = "buttonSetAdditionalLaps";
    buttonSetAdditionalLaps.Size = new Size(107, 38);
    buttonSetAdditionalLaps.TabIndex = 15;
    buttonSetAdditionalLaps.Text = "Set";
    buttonSetAdditionalLaps.UseVisualStyleBackColor = true;
    buttonSetAdditionalLaps.Click += buttonSetAdditionalLaps_Click;
    // 
    // labelMinimumLapTime
    // 
    labelMinimumLapTime.AutoSize = true;
    labelMinimumLapTime.Location = new Point(29, 124);
    labelMinimumLapTime.Margin = new Padding(4, 0, 4, 0);
    labelMinimumLapTime.Name = "labelMinimumLapTime";
    labelMinimumLapTime.Size = new Size(201, 25);
    labelMinimumLapTime.TabIndex = 16;
    labelMinimumLapTime.Text = "Minimum Lap Time (sec):";
    // 
    // numericUpDownMinimumLapTime
    // 
    numericUpDownMinimumLapTime.Location = new Point(295, 122);
    numericUpDownMinimumLapTime.Margin = new Padding(4, 5, 4, 5);
    numericUpDownMinimumLapTime.Maximum = new decimal(new int[] { 300, 0, 0, 0 });
    numericUpDownMinimumLapTime.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
    numericUpDownMinimumLapTime.Name = "numericUpDownMinimumLapTime";
    numericUpDownMinimumLapTime.Size = new Size(171, 31);
    numericUpDownMinimumLapTime.TabIndex = 17;
    numericUpDownMinimumLapTime.Value = new decimal(new int[] { 10, 0, 0, 0 });
    // 
    // checkBoxShortLapDetection
    // 
    checkBoxShortLapDetection.AutoSize = true;
    checkBoxShortLapDetection.Checked = true;
    checkBoxShortLapDetection.CheckState = CheckState.Checked;
    checkBoxShortLapDetection.Location = new Point(474, 124);
    checkBoxShortLapDetection.Margin = new Padding(4, 5, 4, 5);
    checkBoxShortLapDetection.Name = "checkBoxShortLapDetection";
    checkBoxShortLapDetection.Size = new Size(152, 29);
    checkBoxShortLapDetection.TabIndex = 18;
    checkBoxShortLapDetection.Text = "Enable Detection";
    checkBoxShortLapDetection.UseVisualStyleBackColor = true;
    // 
    // buttonSetShortLapSettings
    // 
    buttonSetShortLapSettings.Location = new Point(634, 119);
    buttonSetShortLapSettings.Margin = new Padding(4, 5, 4, 5);
    buttonSetShortLapSettings.Name = "buttonSetShortLapSettings";
    buttonSetShortLapSettings.Size = new Size(107, 38);
    buttonSetShortLapSettings.TabIndex = 19;
    buttonSetShortLapSettings.Text = "Set";
    buttonSetShortLapSettings.UseVisualStyleBackColor = true;
    //buttonSetShortLapSettings.Click += buttonSetShortLapSettings_Click;
    // 
    // groupBoxRaceStart
    // 
    groupBoxRaceStart.Controls.Add(radioButtonStartOnFirstTag);
    groupBoxRaceStart.Controls.Add(radioButtonStartManual);
    groupBoxRaceStart.Controls.Add(buttonStartRace);
    groupBoxRaceStart.Controls.Add(labelRaceStatus);
    groupBoxRaceStart.Location = new Point(29, 167);
    groupBoxRaceStart.Margin = new Padding(4, 5, 4, 5);
    groupBoxRaceStart.Name = "groupBoxRaceStart";
    groupBoxRaceStart.Padding = new Padding(4, 5, 4, 5);
    groupBoxRaceStart.Size = new Size(400, 167);
    groupBoxRaceStart.TabIndex = 17;
    groupBoxRaceStart.TabStop = false;
    groupBoxRaceStart.Text = "Race Start Mode";
    // 
    // radioButtonStartOnFirstTag
    // 
    radioButtonStartOnFirstTag.AutoSize = true;
    radioButtonStartOnFirstTag.Checked = true;
    radioButtonStartOnFirstTag.Location = new Point(14, 37);
    radioButtonStartOnFirstTag.Margin = new Padding(4, 5, 4, 5);
    radioButtonStartOnFirstTag.Name = "radioButtonStartOnFirstTag";
    radioButtonStartOnFirstTag.Size = new Size(205, 29);
    radioButtonStartOnFirstTag.TabIndex = 0;
    radioButtonStartOnFirstTag.TabStop = true;
    radioButtonStartOnFirstTag.Text = "Start on first tag read";
    radioButtonStartOnFirstTag.UseVisualStyleBackColor = true;
    // 
    // radioButtonStartManual
    // 
    radioButtonStartManual.AutoSize = true;
    radioButtonStartManual.Location = new Point(14, 78);
    radioButtonStartManual.Margin = new Padding(4, 5, 4, 5);
    radioButtonStartManual.Name = "radioButtonStartManual";
    radioButtonStartManual.Size = new Size(135, 29);
    radioButtonStartManual.TabIndex = 1;
    radioButtonStartManual.Text = "Manual start";
    radioButtonStartManual.UseVisualStyleBackColor = true;
    // 
    // buttonStartRace
    // 
    buttonStartRace.Enabled = false;
    buttonStartRace.Location = new Point(214, 75);
    buttonStartRace.Margin = new Padding(4, 5, 4, 5);
    buttonStartRace.Name = "buttonStartRace";
    buttonStartRace.Size = new Size(107, 38);
    buttonStartRace.TabIndex = 2;
    buttonStartRace.Text = "Start Race";
    buttonStartRace.UseVisualStyleBackColor = true;
    buttonStartRace.Click += buttonStartRace_Click;
    // 
    // labelRaceStatus
    // 
    labelRaceStatus.AutoSize = true;
    labelRaceStatus.Font = new Font("Microsoft Sans Serif", 9F, FontStyle.Bold);
    labelRaceStatus.ForeColor = Color.DarkRed;
    labelRaceStatus.Location = new Point(14, 125);
    labelRaceStatus.Margin = new Padding(4, 0, 4, 0);
    labelRaceStatus.Name = "labelRaceStatus";
    labelRaceStatus.Size = new Size(170, 22);
    labelRaceStatus.TabIndex = 3;
    labelRaceStatus.Text = "Race: Not Started";
    // 
    // labelTagFilter
    // 
    labelTagFilter.AutoSize = true;
    labelTagFilter.Location = new Point(29, 117);
    labelTagFilter.Margin = new Padding(4, 0, 4, 0);
    labelTagFilter.Name = "labelTagFilter";
    labelTagFilter.Size = new Size(86, 25);
    labelTagFilter.TabIndex = 13;
    labelTagFilter.Text = "Tag Filter:";
    // 
    // textBoxTagFilter
    // 
    textBoxTagFilter.Location = new Point(295, 115);
    textBoxTagFilter.Margin = new Padding(4, 5, 4, 5);
    textBoxTagFilter.Name = "textBoxTagFilter";
    textBoxTagFilter.Size = new Size(171, 31);
    textBoxTagFilter.TabIndex = 14;
    // 
    // buttonSetFilter
    // 
    buttonSetFilter.Location = new Point(474, 111);
    buttonSetFilter.Margin = new Padding(4, 5, 4, 5);
    buttonSetFilter.Name = "buttonSetFilter";
    buttonSetFilter.Size = new Size(107, 38);
    buttonSetFilter.TabIndex = 15;
    buttonSetFilter.Text = "Set Filter";
    buttonSetFilter.UseVisualStyleBackColor = true;
    buttonSetFilter.Click += buttonSetFilter_Click;
    // 
    // checkBoxFilterEnabled
    // 
    checkBoxFilterEnabled.AutoSize = true;
    checkBoxFilterEnabled.Location = new Point(589, 121);
    checkBoxFilterEnabled.Margin = new Padding(4, 5, 4, 5);
    checkBoxFilterEnabled.Name = "checkBoxFilterEnabled";
    checkBoxFilterEnabled.Size = new Size(22, 21);
    checkBoxFilterEnabled.TabIndex = 16;
    checkBoxFilterEnabled.UseVisualStyleBackColor = true;
    checkBoxFilterEnabled.CheckedChanged += checkBoxFilterEnabled_CheckedChanged;
    // 
    // labelFilterEnabled
    // 
    labelFilterEnabled.AutoSize = true;
    labelFilterEnabled.Location = new Point(618, 118);
    labelFilterEnabled.Margin = new Padding(4, 0, 4, 0);
    labelFilterEnabled.Name = "labelFilterEnabled";
    labelFilterEnabled.Size = new Size(75, 25);
    labelFilterEnabled.TabIndex = 18;
    labelFilterEnabled.Text = "Enabled";
    // 
    // timerUpdate
    // 
    timerUpdate.Enabled = true;
    timerUpdate.Interval = 1000;
    timerUpdate.Tick += timerUpdate_Tick;
    // 
    // Form1
    // 
    AutoScaleDimensions = new SizeF(10F, 25F);
    AutoScaleMode = AutoScaleMode.Font;
    ClientSize = new Size(1691, 1020);
    Controls.Add(tabControl);
    Controls.Add(buttonGenerateReport);
    Controls.Add(buttonClearRiders);
    Controls.Add(buttonShowSummary);
    Controls.Add(labelConnections);
    Controls.Add(buttonClear);
    Controls.Add(labelPort);
    Controls.Add(textBoxPort);
    Controls.Add(buttonStop);
    Controls.Add(buttonStart);
    Controls.Add(labelStatus);
    Margin = new Padding(4, 5, 4, 5);
    MinimumSize = new Size(1133, 796);
    Name = "Form1";
    StartPosition = FormStartPosition.CenterScreen;
    Text = "CrossMgr RFID Interface";
    FormClosing += Form1_FormClosing;
    ((System.ComponentModel.ISupportInitialize)numericUpDownRaceDuration).EndInit();
    tabControl.ResumeLayout(false);
    tabPageLive.ResumeLayout(false);
    tabPageTagEvents.ResumeLayout(false);
    tabPageRiders.ResumeLayout(false);
    ((System.ComponentModel.ISupportInitialize)dataGridViewRiders).EndInit();
    tabPageStats.ResumeLayout(false);
    tabPageStats.PerformLayout();
    tabPageLapChart.ResumeLayout(false);
    tabPageRaceSettings.ResumeLayout(false);
    tabPageRaceSettings.PerformLayout();
    ((System.ComponentModel.ISupportInitialize)numericUpDownAdditionalLaps).EndInit();
    ((System.ComponentModel.ISupportInitialize)numericUpDownMinimumLapTime).EndInit();
    groupBoxRaceStart.ResumeLayout(false);
    groupBoxRaceStart.PerformLayout();
    ResumeLayout(false);
    PerformLayout();
  }

  #endregion
}
