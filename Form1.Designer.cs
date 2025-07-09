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
    base.Dispose(disposing);
  }

  #region Windows Form Designer generated code

  /// <summary>
  ///  Required method for Designer support - do not modify
  ///  the contents of this method with the code editor.
  /// </summary>
  private System.Windows.Forms.ListBox listBoxMessages;
  private System.Windows.Forms.Label labelStatus;
  private System.Windows.Forms.Button buttonStart;
  private System.Windows.Forms.Button buttonStop;
  private System.Windows.Forms.TextBox textBoxPort;
  private System.Windows.Forms.Label labelPort;
  private System.Windows.Forms.Button buttonClear;
  private System.Windows.Forms.Label labelConnections;
  private System.Windows.Forms.Button buttonShowSummary;
  private System.Windows.Forms.Button buttonClearRiders;
  private System.Windows.Forms.Label labelRaceDuration;
  private System.Windows.Forms.NumericUpDown numericUpDownRaceDuration;
  private System.Windows.Forms.Button buttonSetDuration;
  private System.Windows.Forms.TabControl tabControl;
  private System.Windows.Forms.TabPage tabPageLive;
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

  private void InitializeComponent()
  {
    this.components = new System.ComponentModel.Container();
    this.labelStatus = new System.Windows.Forms.Label();
    this.buttonStart = new System.Windows.Forms.Button();
    this.buttonStop = new System.Windows.Forms.Button();
    this.textBoxPort = new System.Windows.Forms.TextBox();
    this.labelPort = new System.Windows.Forms.Label();
    this.buttonClear = new System.Windows.Forms.Button();
    this.labelConnections = new System.Windows.Forms.Label();
    this.buttonShowSummary = new System.Windows.Forms.Button();
    this.buttonClearRiders = new System.Windows.Forms.Button();
    this.labelRaceDuration = new System.Windows.Forms.Label();
    this.numericUpDownRaceDuration = new System.Windows.Forms.NumericUpDown();
    this.buttonSetDuration = new System.Windows.Forms.Button();
    this.tabControl = new System.Windows.Forms.TabControl();
    this.tabPageLive = new System.Windows.Forms.TabPage();
    this.listBoxMessages = new System.Windows.Forms.ListBox();
    this.tabPageRiders = new System.Windows.Forms.TabPage();
    this.dataGridViewRiders = new System.Windows.Forms.DataGridView();
    this.tabPageStats = new System.Windows.Forms.TabPage();
    this.labelLastTag = new System.Windows.Forms.Label();
    this.labelTotalLaps = new System.Windows.Forms.Label();
    this.labelTotalRiders = new System.Windows.Forms.Label();
    this.labelRaceTime = new System.Windows.Forms.Label();
    this.labelNextCrossing = new System.Windows.Forms.Label();
    this.labelRaceEndTime = new System.Windows.Forms.Label();
    this.labelTimeRemaining = new System.Windows.Forms.Label();
    this.labelPredictedLaps = new System.Windows.Forms.Label();
    this.timerUpdate = new System.Windows.Forms.Timer(this.components);
    this.labelTagFilter = new System.Windows.Forms.Label();
    this.textBoxTagFilter = new System.Windows.Forms.TextBox();
    this.buttonSetFilter = new System.Windows.Forms.Button();
    this.checkBoxFilterEnabled = new System.Windows.Forms.CheckBox();
    this.tabPageLapChart = new System.Windows.Forms.TabPage();
    this.panelLapChart = new System.Windows.Forms.Panel();
    this.tabPageRaceSettings = new System.Windows.Forms.TabPage();
    this.groupBoxRaceStart = new System.Windows.Forms.GroupBox();
    this.radioButtonStartOnFirstTag = new System.Windows.Forms.RadioButton();
    this.radioButtonStartManual = new System.Windows.Forms.RadioButton();
    this.buttonStartRace = new System.Windows.Forms.Button();
    this.labelRaceStatus = new System.Windows.Forms.Label();
    this.labelFilterEnabled = new System.Windows.Forms.Label();
    this.labelAdditionalLaps = new System.Windows.Forms.Label();
    this.numericUpDownAdditionalLaps = new System.Windows.Forms.NumericUpDown();
    this.buttonSetAdditionalLaps = new System.Windows.Forms.Button();
    this.tabControl.SuspendLayout();
    this.tabPageLive.SuspendLayout();
    this.tabPageRiders.SuspendLayout();
    ((System.ComponentModel.ISupportInitialize)(this.dataGridViewRiders)).BeginInit();
    this.tabPageStats.SuspendLayout();
    ((System.ComponentModel.ISupportInitialize)(this.numericUpDownRaceDuration)).BeginInit();
    ((System.ComponentModel.ISupportInitialize)(this.numericUpDownAdditionalLaps)).BeginInit();
    this.tabPageLapChart.SuspendLayout();
    this.tabPageRaceSettings.SuspendLayout();
    this.groupBoxRaceStart.SuspendLayout();
    this.SuspendLayout();
    // 
    // tabControl
    // 
    this.tabControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
    | System.Windows.Forms.AnchorStyles.Left)
    | System.Windows.Forms.AnchorStyles.Right)));
    this.tabControl.Controls.Add(this.tabPageLive);
    this.tabControl.Controls.Add(this.tabPageRiders);
    this.tabControl.Controls.Add(this.tabPageStats);
    this.tabControl.Controls.Add(this.tabPageLapChart);
    this.tabControl.Controls.Add(this.tabPageRaceSettings);
    this.tabControl.Location = new System.Drawing.Point(12, 50);
    this.tabControl.Name = "tabControl";
    this.tabControl.SelectedIndex = 0;
    this.tabControl.Size = new System.Drawing.Size(1160, 550);
    this.tabControl.TabIndex = 10;
    // 
    // tabPageLive
    // 
    this.tabPageLive.Controls.Add(this.listBoxMessages);
    this.tabPageLive.Location = new System.Drawing.Point(4, 24);
    this.tabPageLive.Name = "tabPageLive";
    this.tabPageLive.Padding = new System.Windows.Forms.Padding(3);
    this.tabPageLive.Size = new System.Drawing.Size(1152, 492);
    this.tabPageLive.TabIndex = 0;
    this.tabPageLive.Text = "Live Feed";
    this.tabPageLive.UseVisualStyleBackColor = true;
    // 
    // listBoxMessages
    // 
    this.listBoxMessages.Dock = System.Windows.Forms.DockStyle.Fill;
    this.listBoxMessages.Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
    this.listBoxMessages.HorizontalScrollbar = true;
    this.listBoxMessages.ItemHeight = 14;
    this.listBoxMessages.Location = new System.Drawing.Point(3, 3);
    this.listBoxMessages.Name = "listBoxMessages";
    this.listBoxMessages.Size = new System.Drawing.Size(1146, 486);
    this.listBoxMessages.TabIndex = 0;
    // 
    // tabPageRiders
    // 
    this.tabPageRiders.Controls.Add(this.dataGridViewRiders);
    this.tabPageRiders.Location = new System.Drawing.Point(4, 24);
    this.tabPageRiders.Name = "tabPageRiders";
    this.tabPageRiders.Padding = new System.Windows.Forms.Padding(3);
    this.tabPageRiders.Size = new System.Drawing.Size(1152, 492);
    this.tabPageRiders.TabIndex = 1;
    this.tabPageRiders.Text = "Riders";
    this.tabPageRiders.UseVisualStyleBackColor = true;
    // 
    // dataGridViewRiders
    // 
    this.dataGridViewRiders.AllowUserToAddRows = false;
    this.dataGridViewRiders.AllowUserToDeleteRows = false;
    this.dataGridViewRiders.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
    this.dataGridViewRiders.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
    this.dataGridViewRiders.Dock = System.Windows.Forms.DockStyle.Fill;
    this.dataGridViewRiders.Location = new System.Drawing.Point(3, 3);
    this.dataGridViewRiders.Name = "dataGridViewRiders";
    this.dataGridViewRiders.ReadOnly = true;
    this.dataGridViewRiders.RowHeadersVisible = false;
    this.dataGridViewRiders.RowTemplate.Height = 25;
    this.dataGridViewRiders.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
    this.dataGridViewRiders.Size = new System.Drawing.Size(1146, 486);
    this.dataGridViewRiders.TabIndex = 0;
    // 
    // tabPageStats
    // 
    this.tabPageStats.Controls.Add(this.labelPredictedLaps);
    this.tabPageStats.Controls.Add(this.labelTimeRemaining);
    this.tabPageStats.Controls.Add(this.labelRaceEndTime);
    this.tabPageStats.Controls.Add(this.labelNextCrossing);
    this.tabPageStats.Controls.Add(this.labelLastTag);
    this.tabPageStats.Controls.Add(this.labelTotalLaps);
    this.tabPageStats.Controls.Add(this.labelTotalRiders);
    this.tabPageStats.Controls.Add(this.labelRaceTime);
    this.tabPageStats.Location = new System.Drawing.Point(4, 24);
    this.tabPageStats.Name = "tabPageStats";
    this.tabPageStats.Size = new System.Drawing.Size(1152, 492);
    this.tabPageStats.TabIndex = 2;
    this.tabPageStats.Text = "Race Statistics";
    this.tabPageStats.UseVisualStyleBackColor = true;
    // 
    // tabPageLapChart
    // 
    this.tabPageLapChart.Controls.Add(this.panelLapChart);
    this.tabPageLapChart.Location = new System.Drawing.Point(4, 24);
    this.tabPageLapChart.Name = "tabPageLapChart";
    this.tabPageLapChart.Padding = new System.Windows.Forms.Padding(3);
    this.tabPageLapChart.Size = new System.Drawing.Size(1152, 492);
    this.tabPageLapChart.TabIndex = 3;
    this.tabPageLapChart.Text = "Lap Chart";
    this.tabPageLapChart.UseVisualStyleBackColor = true;
    // 
    // panelLapChart
    // 
    this.panelLapChart.AutoScroll = true;
    this.panelLapChart.BackColor = System.Drawing.Color.White;
    this.panelLapChart.Dock = System.Windows.Forms.DockStyle.Fill;
    this.panelLapChart.Location = new System.Drawing.Point(3, 3);
    this.panelLapChart.Name = "panelLapChart";
    this.panelLapChart.Size = new System.Drawing.Size(1146, 486);
    this.panelLapChart.TabIndex = 0;
    this.panelLapChart.Paint += new System.Windows.Forms.PaintEventHandler(this.panelLapChart_Paint);
    // 
    // tabPageRaceSettings
    // 
    this.tabPageRaceSettings.Controls.Add(this.labelAdditionalLaps);
    this.tabPageRaceSettings.Controls.Add(this.numericUpDownAdditionalLaps);
    this.tabPageRaceSettings.Controls.Add(this.buttonSetAdditionalLaps);
    this.tabPageRaceSettings.Controls.Add(this.groupBoxRaceStart);
    this.tabPageRaceSettings.Controls.Add(this.labelTagFilter);
    this.tabPageRaceSettings.Controls.Add(this.textBoxTagFilter);
    this.tabPageRaceSettings.Controls.Add(this.buttonSetFilter);
    this.tabPageRaceSettings.Controls.Add(this.checkBoxFilterEnabled);
    this.tabPageRaceSettings.Controls.Add(this.labelFilterEnabled);
    this.tabPageRaceSettings.Controls.Add(this.labelRaceDuration);
    this.tabPageRaceSettings.Controls.Add(this.numericUpDownRaceDuration);
    this.tabPageRaceSettings.Controls.Add(this.buttonSetDuration);
    this.tabPageRaceSettings.Location = new System.Drawing.Point(4, 24);
    this.tabPageRaceSettings.Name = "tabPageRaceSettings";
    this.tabPageRaceSettings.Padding = new System.Windows.Forms.Padding(3);
    this.tabPageRaceSettings.Size = new System.Drawing.Size(1152, 522);
    this.tabPageRaceSettings.TabIndex = 4;
    this.tabPageRaceSettings.Text = "Race Settings";
    this.tabPageRaceSettings.UseVisualStyleBackColor = true;
    // 
    // labelStatus
    // 
    this.labelStatus.AutoSize = true;
    this.labelStatus.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
    this.labelStatus.ForeColor = System.Drawing.Color.Red;
    this.labelStatus.Location = new System.Drawing.Point(12, 55);
    this.labelStatus.Name = "labelStatus";
    this.labelStatus.Size = new System.Drawing.Size(55, 15);
    this.labelStatus.TabIndex = 1;
    this.labelStatus.Text = "Stopped";
    // 
    // buttonStart
    // 
    this.buttonStart.Location = new System.Drawing.Point(140, 12);
    this.buttonStart.Name = "buttonStart";
    this.buttonStart.Size = new System.Drawing.Size(75, 23);
    this.buttonStart.TabIndex = 2;
    this.buttonStart.Text = "Start";
    this.buttonStart.UseVisualStyleBackColor = true;
    this.buttonStart.Click += new System.EventHandler(this.buttonStart_Click);
    // 
    // buttonStop
    // 
    this.buttonStop.Enabled = false;
    this.buttonStop.Location = new System.Drawing.Point(221, 12);
    this.buttonStop.Name = "buttonStop";
    this.buttonStop.Size = new System.Drawing.Size(75, 23);
    this.buttonStop.TabIndex = 3;
    this.buttonStop.Text = "Stop";
    this.buttonStop.UseVisualStyleBackColor = true;
    this.buttonStop.Click += new System.EventHandler(this.buttonStop_Click);
    // 
    // textBoxPort
    // 
    this.textBoxPort.Location = new System.Drawing.Point(47, 12);
    this.textBoxPort.Name = "textBoxPort";
    this.textBoxPort.Size = new System.Drawing.Size(87, 23);
    this.textBoxPort.TabIndex = 4;
    this.textBoxPort.Text = "53135";
    // 
    // labelPort
    // 
    this.labelPort.AutoSize = true;
    this.labelPort.Location = new System.Drawing.Point(12, 15);
    this.labelPort.Name = "labelPort";
    this.labelPort.Size = new System.Drawing.Size(29, 15);
    this.labelPort.TabIndex = 5;
    this.labelPort.Text = "Port:";
    // 
    // buttonClear
    // 
    this.buttonClear.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
    this.buttonClear.Location = new System.Drawing.Point(1097, 12);
    this.buttonClear.Name = "buttonClear";
    this.buttonClear.Size = new System.Drawing.Size(75, 23);
    this.buttonClear.TabIndex = 6;
    this.buttonClear.Text = "Clear";
    this.buttonClear.UseVisualStyleBackColor = true;
    this.buttonClear.Click += new System.EventHandler(this.buttonClear_Click);
    // 
    // labelConnections
    // 
    this.labelConnections.AutoSize = true;
    this.labelConnections.Location = new System.Drawing.Point(302, 15);
    this.labelConnections.Name = "labelConnections";
    this.labelConnections.Size = new System.Drawing.Size(79, 15);
    this.labelConnections.TabIndex = 7;
    this.labelConnections.Text = "Connections: 0";
    // 
    // buttonShowSummary
    // 
    this.buttonShowSummary.Location = new System.Drawing.Point(400, 12);
    this.buttonShowSummary.Name = "buttonShowSummary";
    this.buttonShowSummary.Size = new System.Drawing.Size(100, 23);
    this.buttonShowSummary.TabIndex = 8;
    this.buttonShowSummary.Text = "Show Summary";
    this.buttonShowSummary.UseVisualStyleBackColor = true;
    this.buttonShowSummary.Click += new System.EventHandler(this.buttonShowSummary_Click);
    // 
    // buttonClearRiders
    // 
    this.buttonClearRiders.Location = new System.Drawing.Point(510, 12);
    this.buttonClearRiders.Name = "buttonClearRiders";
    this.buttonClearRiders.Size = new System.Drawing.Size(90, 23);
    this.buttonClearRiders.TabIndex = 9;
    this.buttonClearRiders.Text = "Clear Riders";
    this.buttonClearRiders.UseVisualStyleBackColor = true;
    this.buttonClearRiders.Click += new System.EventHandler(this.buttonClearRiders_Click);
    // 
    // labelRaceDuration
    // 
    this.labelRaceDuration.AutoSize = true;
    this.labelRaceDuration.Location = new System.Drawing.Point(20, 20);
    this.labelRaceDuration.Name = "labelRaceDuration";
    this.labelRaceDuration.Size = new System.Drawing.Size(105, 15);
    this.labelRaceDuration.TabIndex = 10;
    this.labelRaceDuration.Text = "Race Duration (min):";
    // 
    // numericUpDownRaceDuration
    // 
    this.numericUpDownRaceDuration.Location = new System.Drawing.Point(130, 17);
    this.numericUpDownRaceDuration.Maximum = new decimal(new int[] {
    180,
    0,
    0,
    0});
    this.numericUpDownRaceDuration.Minimum = new decimal(new int[] {
    1,
    0,
    0,
    0});
    this.numericUpDownRaceDuration.Name = "numericUpDownRaceDuration";
    this.numericUpDownRaceDuration.Size = new System.Drawing.Size(60, 23);
    this.numericUpDownRaceDuration.TabIndex = 11;
    this.numericUpDownRaceDuration.Value = new decimal(new int[] {
    20,
    0,
    0,
    0});
    // 
    // buttonSetDuration
    // 
    this.buttonSetDuration.Location = new System.Drawing.Point(196, 17);
    this.buttonSetDuration.Name = "buttonSetDuration";
    this.buttonSetDuration.Size = new System.Drawing.Size(75, 23);
    this.buttonSetDuration.TabIndex = 12;
    this.buttonSetDuration.Text = "Set";
    this.buttonSetDuration.UseVisualStyleBackColor = true;
    this.buttonSetDuration.Click += new System.EventHandler(this.buttonSetDuration_Click);
    // 
    // labelRaceTime
    // 
    this.labelRaceTime.AutoSize = true;
    this.labelRaceTime.Font = new System.Drawing.Font("Microsoft Sans Serif", 14.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
    this.labelRaceTime.Location = new System.Drawing.Point(30, 30);
    this.labelRaceTime.Name = "labelRaceTime";
    this.labelRaceTime.Size = new System.Drawing.Size(165, 24);
    this.labelRaceTime.TabIndex = 0;
    this.labelRaceTime.Text = "Race Time: 00:00";
    // 
    // labelTotalRiders
    // 
    this.labelTotalRiders.AutoSize = true;
    this.labelTotalRiders.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
    this.labelTotalRiders.Location = new System.Drawing.Point(30, 80);
    this.labelTotalRiders.Name = "labelTotalRiders";
    this.labelTotalRiders.Size = new System.Drawing.Size(103, 20);
    this.labelTotalRiders.TabIndex = 1;
    this.labelTotalRiders.Text = "Total Riders: 0";
    // 
    // labelTotalLaps
    // 
    this.labelTotalLaps.AutoSize = true;
    this.labelTotalLaps.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
    this.labelTotalLaps.Location = new System.Drawing.Point(30, 110);
    this.labelTotalLaps.Name = "labelTotalLaps";
    this.labelTotalLaps.Size = new System.Drawing.Size(91, 20);
    this.labelTotalLaps.TabIndex = 2;
    this.labelTotalLaps.Text = "Total Laps: 0";
    // 
    // labelLastTag
    // 
    this.labelLastTag.AutoSize = true;
    this.labelLastTag.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
    this.labelLastTag.Location = new System.Drawing.Point(30, 140);
    this.labelLastTag.Name = "labelLastTag";
    this.labelLastTag.Size = new System.Drawing.Size(119, 20);
    this.labelLastTag.TabIndex = 3;
    this.labelLastTag.Text = "Last Tag: None";
    // 
    // labelNextCrossing
    // 
    this.labelNextCrossing.AutoSize = true;
    this.labelNextCrossing.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
    this.labelNextCrossing.Location = new System.Drawing.Point(30, 170);
    this.labelNextCrossing.Name = "labelNextCrossing";
    this.labelNextCrossing.Size = new System.Drawing.Size(180, 20);
    this.labelNextCrossing.TabIndex = 4;
    this.labelNextCrossing.Text = "Next Expected: Calculating...";
    // 
    // labelRaceEndTime
    // 
    this.labelRaceEndTime.AutoSize = true;
    this.labelRaceEndTime.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
    this.labelRaceEndTime.Location = new System.Drawing.Point(30, 200);
    this.labelRaceEndTime.Name = "labelRaceEndTime";
    this.labelRaceEndTime.Size = new System.Drawing.Size(140, 20);
    this.labelRaceEndTime.TabIndex = 5;
    this.labelRaceEndTime.Text = "Race End: Not Set";
    // 
    // labelTimeRemaining
    // 
    this.labelTimeRemaining.AutoSize = true;
    this.labelTimeRemaining.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
    this.labelTimeRemaining.ForeColor = System.Drawing.Color.DarkRed;
    this.labelTimeRemaining.Location = new System.Drawing.Point(30, 230);
    this.labelTimeRemaining.Name = "labelTimeRemaining";
    this.labelTimeRemaining.Size = new System.Drawing.Size(167, 20);
    this.labelTimeRemaining.TabIndex = 6;
    this.labelTimeRemaining.Text = "Time Remaining: N/A";
    // 
    // labelPredictedLaps
    // 
    this.labelPredictedLaps.AutoSize = true;
    this.labelPredictedLaps.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
    this.labelPredictedLaps.Location = new System.Drawing.Point(30, 260);
    this.labelPredictedLaps.Name = "labelPredictedLaps";
    this.labelPredictedLaps.Size = new System.Drawing.Size(200, 20);
    this.labelPredictedLaps.TabIndex = 7;
    this.labelPredictedLaps.Text = "Predicted Laps (Leader): N/A";
    // 
    // timerUpdate
    // 
    this.timerUpdate.Enabled = true;
    this.timerUpdate.Interval = 1000;
    this.timerUpdate.Tick += new System.EventHandler(this.timerUpdate_Tick);
    // 
    // labelTagFilter
    // 
    this.labelTagFilter.AutoSize = true;
    this.labelTagFilter.Location = new System.Drawing.Point(20, 60);
    this.labelTagFilter.Name = "labelTagFilter";
    this.labelTagFilter.Size = new System.Drawing.Size(64, 15);
    this.labelTagFilter.TabIndex = 13;
    this.labelTagFilter.Text = "Tag Filter:";
    // 
    // textBoxTagFilter
    // 
    this.textBoxTagFilter.Location = new System.Drawing.Point(90, 57);
    this.textBoxTagFilter.Name = "textBoxTagFilter";
    this.textBoxTagFilter.Size = new System.Drawing.Size(100, 23);
    this.textBoxTagFilter.TabIndex = 14;
    // 
    // buttonSetFilter
    // 
    this.buttonSetFilter.Location = new System.Drawing.Point(196, 57);
    this.buttonSetFilter.Name = "buttonSetFilter";
    this.buttonSetFilter.Size = new System.Drawing.Size(75, 23);
    this.buttonSetFilter.TabIndex = 15;
    this.buttonSetFilter.Text = "Set Filter";
    this.buttonSetFilter.UseVisualStyleBackColor = true;
    this.buttonSetFilter.Click += new System.EventHandler(this.buttonSetFilter_Click);
    // 
    // checkBoxFilterEnabled
    // 
    this.checkBoxFilterEnabled.AutoSize = true;
    this.checkBoxFilterEnabled.Location = new System.Drawing.Point(277, 61);
    this.checkBoxFilterEnabled.Name = "checkBoxFilterEnabled";
    this.checkBoxFilterEnabled.Size = new System.Drawing.Size(15, 14);
    this.checkBoxFilterEnabled.TabIndex = 16;
    this.checkBoxFilterEnabled.UseVisualStyleBackColor = true;
    this.checkBoxFilterEnabled.CheckedChanged += new System.EventHandler(this.checkBoxFilterEnabled_CheckedChanged);
    // 
    // groupBoxRaceStart
    // 
    this.groupBoxRaceStart.Controls.Add(this.radioButtonStartOnFirstTag);
    this.groupBoxRaceStart.Controls.Add(this.radioButtonStartManual);
    this.groupBoxRaceStart.Controls.Add(this.buttonStartRace);
    this.groupBoxRaceStart.Controls.Add(this.labelRaceStatus);
    this.groupBoxRaceStart.Location = new System.Drawing.Point(20, 100);
    this.groupBoxRaceStart.Name = "groupBoxRaceStart";
    this.groupBoxRaceStart.Size = new System.Drawing.Size(280, 100);
    this.groupBoxRaceStart.TabIndex = 17;
    this.groupBoxRaceStart.TabStop = false;
    this.groupBoxRaceStart.Text = "Race Start Mode";
    // 
    // radioButtonStartOnFirstTag
    // 
    this.radioButtonStartOnFirstTag.AutoSize = true;
    this.radioButtonStartOnFirstTag.Checked = true;
    this.radioButtonStartOnFirstTag.Location = new System.Drawing.Point(10, 22);
    this.radioButtonStartOnFirstTag.Name = "radioButtonStartOnFirstTag";
    this.radioButtonStartOnFirstTag.Size = new System.Drawing.Size(130, 19);
    this.radioButtonStartOnFirstTag.TabIndex = 0;
    this.radioButtonStartOnFirstTag.TabStop = true;
    this.radioButtonStartOnFirstTag.Text = "Start on first tag read";
    this.radioButtonStartOnFirstTag.UseVisualStyleBackColor = true;
    // 
    // radioButtonStartManual
    // 
    this.radioButtonStartManual.AutoSize = true;
    this.radioButtonStartManual.Location = new System.Drawing.Point(10, 47);
    this.radioButtonStartManual.Name = "radioButtonStartManual";
    this.radioButtonStartManual.Size = new System.Drawing.Size(93, 19);
    this.radioButtonStartManual.TabIndex = 1;
    this.radioButtonStartManual.Text = "Manual start";
    this.radioButtonStartManual.UseVisualStyleBackColor = true;
    // 
    // buttonStartRace
    // 
    this.buttonStartRace.Enabled = false;
    this.buttonStartRace.Location = new System.Drawing.Point(150, 45);
    this.buttonStartRace.Name = "buttonStartRace";
    this.buttonStartRace.Size = new System.Drawing.Size(75, 23);
    this.buttonStartRace.TabIndex = 2;
    this.buttonStartRace.Text = "Start Race";
    this.buttonStartRace.UseVisualStyleBackColor = true;
    this.buttonStartRace.Click += new System.EventHandler(this.buttonStartRace_Click);
    // 
    // labelRaceStatus
    // 
    this.labelRaceStatus.AutoSize = true;
    this.labelRaceStatus.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
    this.labelRaceStatus.ForeColor = System.Drawing.Color.DarkRed;
    this.labelRaceStatus.Location = new System.Drawing.Point(10, 75);
    this.labelRaceStatus.Name = "labelRaceStatus";
    this.labelRaceStatus.Size = new System.Drawing.Size(88, 15);
    this.labelRaceStatus.TabIndex = 3;
    this.labelRaceStatus.Text = "Race: Not Started";
    // 
    // labelFilterEnabled
    // 
    this.labelFilterEnabled.AutoSize = true;
    this.labelFilterEnabled.Location = new System.Drawing.Point(298, 61);
    this.labelFilterEnabled.Name = "labelFilterEnabled";
    this.labelFilterEnabled.Size = new System.Drawing.Size(51, 15);
    this.labelFilterEnabled.TabIndex = 18;
    this.labelFilterEnabled.Text = "Enabled";
    // 
    // labelAdditionalLaps
    // 
    this.labelAdditionalLaps.AutoSize = true;
    this.labelAdditionalLaps.Location = new System.Drawing.Point(20, 50);
    this.labelAdditionalLaps.Name = "labelAdditionalLaps";
    this.labelAdditionalLaps.Size = new System.Drawing.Size(149, 15);
    this.labelAdditionalLaps.TabIndex = 13;
    this.labelAdditionalLaps.Text = "Additional Laps After Time:";
    // 
    // numericUpDownAdditionalLaps
    // 
    this.numericUpDownAdditionalLaps.Location = new System.Drawing.Point(175, 48);
    this.numericUpDownAdditionalLaps.Maximum = new decimal(new int[] {
    10,
    0,
    0,
    0});
    this.numericUpDownAdditionalLaps.Minimum = new decimal(new int[] {
    1,
    0,
    0,
    0});
    this.numericUpDownAdditionalLaps.Name = "numericUpDownAdditionalLaps";
    this.numericUpDownAdditionalLaps.Size = new System.Drawing.Size(120, 23);
    this.numericUpDownAdditionalLaps.TabIndex = 14;
    this.numericUpDownAdditionalLaps.Value = new decimal(new int[] {
    1,
    0,
    0,
    0});
    // 
    // buttonSetAdditionalLaps
    // 
    this.buttonSetAdditionalLaps.Location = new System.Drawing.Point(301, 48);
    this.buttonSetAdditionalLaps.Name = "buttonSetAdditionalLaps";
    this.buttonSetAdditionalLaps.Size = new System.Drawing.Size(75, 23);
    this.buttonSetAdditionalLaps.TabIndex = 15;
    this.buttonSetAdditionalLaps.Text = "Set";
    this.buttonSetAdditionalLaps.UseVisualStyleBackColor = true;
    this.buttonSetAdditionalLaps.Click += new System.EventHandler(this.buttonSetAdditionalLaps_Click);
    // 
    // Form1
    // 
    this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
    this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
    this.ClientSize = new System.Drawing.Size(1184, 612);
    this.Controls.Add(this.tabControl);
    this.Controls.Add(this.buttonClearRiders);
    this.Controls.Add(this.buttonShowSummary);
    this.Controls.Add(this.labelConnections);
    this.Controls.Add(this.buttonClear);
    this.Controls.Add(this.labelPort);
    this.Controls.Add(this.textBoxPort);
    this.Controls.Add(this.buttonStop);
    this.Controls.Add(this.buttonStart);
    this.Controls.Add(this.labelStatus);
    this.MinimumSize = new System.Drawing.Size(800, 500);
    this.Name = "Form1";
    this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
    this.Text = "CrossMgr RFID Interface";
    this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
    this.tabControl.ResumeLayout(false);
    this.tabPageLive.ResumeLayout(false);
    this.tabPageRiders.ResumeLayout(false);
    ((System.ComponentModel.ISupportInitialize)(this.dataGridViewRiders)).EndInit();
    this.tabPageStats.ResumeLayout(false);
    this.tabPageStats.PerformLayout();
    ((System.ComponentModel.ISupportInitialize)(this.numericUpDownRaceDuration)).EndInit();
    ((System.ComponentModel.ISupportInitialize)(this.numericUpDownAdditionalLaps)).EndInit();
    this.tabPageLapChart.ResumeLayout(false);
    this.tabPageRaceSettings.ResumeLayout(false);
    this.tabPageRaceSettings.PerformLayout();
    this.groupBoxRaceStart.ResumeLayout(false);
    this.groupBoxRaceStart.PerformLayout();
    this.ResumeLayout(false);
    this.PerformLayout();
  }

  #endregion
}
