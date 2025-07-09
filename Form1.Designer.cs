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

  private void InitializeComponent()
  {
    this.listBoxMessages = new System.Windows.Forms.ListBox();
    this.labelStatus = new System.Windows.Forms.Label();
    this.buttonStart = new System.Windows.Forms.Button();
    this.buttonStop = new System.Windows.Forms.Button();
    this.textBoxPort = new System.Windows.Forms.TextBox();
    this.labelPort = new System.Windows.Forms.Label();
    this.buttonClear = new System.Windows.Forms.Button();
    this.labelConnections = new System.Windows.Forms.Label();
    this.SuspendLayout();
    // 
    // listBoxMessages
    // 
    this.listBoxMessages.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
    | System.Windows.Forms.AnchorStyles.Left)
    | System.Windows.Forms.AnchorStyles.Right)));
    this.listBoxMessages.Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
    this.listBoxMessages.HorizontalScrollbar = true;
    this.listBoxMessages.ItemHeight = 14;
    this.listBoxMessages.Location = new System.Drawing.Point(12, 80);
    this.listBoxMessages.Name = "listBoxMessages";
    this.listBoxMessages.Size = new System.Drawing.Size(776, 358);
    this.listBoxMessages.TabIndex = 0;
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
    this.buttonClear.Location = new System.Drawing.Point(713, 12);
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
    // Form1
    // 
    this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
    this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
    this.ClientSize = new System.Drawing.Size(800, 450);
    this.Controls.Add(this.labelConnections);
    this.Controls.Add(this.buttonClear);
    this.Controls.Add(this.labelPort);
    this.Controls.Add(this.textBoxPort);
    this.Controls.Add(this.buttonStop);
    this.Controls.Add(this.buttonStart);
    this.Controls.Add(this.labelStatus);
    this.Controls.Add(this.listBoxMessages);
    this.MinimumSize = new System.Drawing.Size(500, 300);
    this.Name = "Form1";
    this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
    this.Text = "CrossMgr RFID Interface";
    this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
    this.ResumeLayout(false);
    this.PerformLayout();
  }

  #endregion
}
