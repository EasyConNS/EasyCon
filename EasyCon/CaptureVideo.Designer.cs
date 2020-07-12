namespace EasyCon
{
    partial class CaptureVideo
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(CaptureVideo));
            this.VideoSourcePlayerMonitor = new AForge.Controls.VideoSourcePlayer();
            this.listBox1 = new System.Windows.Forms.ListBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.Snapshot = new System.Windows.Forms.PictureBox();
            this.button1 = new System.Windows.Forms.Button();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.button2 = new System.Windows.Forms.Button();
            this.button3 = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.Snapshot)).BeginInit();
            this.SuspendLayout();
            // 
            // VideoSourcePlayerMonitor
            // 
            this.VideoSourcePlayerMonitor.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.VideoSourcePlayerMonitor.Location = new System.Drawing.Point(648, 0);
            this.VideoSourcePlayerMonitor.Margin = new System.Windows.Forms.Padding(0);
            this.VideoSourcePlayerMonitor.Name = "VideoSourcePlayerMonitor";
            this.VideoSourcePlayerMonitor.Size = new System.Drawing.Size(447, 251);
            this.VideoSourcePlayerMonitor.TabIndex = 0;
            this.VideoSourcePlayerMonitor.Text = "videoSourcePlayer1";
            this.VideoSourcePlayerMonitor.VideoSource = null;
            this.VideoSourcePlayerMonitor.KeyDown += new System.Windows.Forms.KeyEventHandler(this.VideoSourcePlayerMonitor_KeyDown);
            this.VideoSourcePlayerMonitor.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.VideoSourcePlayerMonitor_MouseDoubleClick);
            this.VideoSourcePlayerMonitor.MouseDown += new System.Windows.Forms.MouseEventHandler(this.VideoSourcePlayerMonitor_MouseDown);
            this.VideoSourcePlayerMonitor.MouseMove += new System.Windows.Forms.MouseEventHandler(this.VideoSourcePlayerMonitor_MouseMove);
            this.VideoSourcePlayerMonitor.MouseUp += new System.Windows.Forms.MouseEventHandler(this.VideoSourcePlayerMonitor_MouseUp);
            this.VideoSourcePlayerMonitor.MouseWheel += new System.Windows.Forms.MouseEventHandler(this.VideoSourcePlayerMonitor_MouseWheel);
            // 
            // listBox1
            // 
            this.listBox1.FormattingEnabled = true;
            this.listBox1.ItemHeight = 12;
            this.listBox1.Location = new System.Drawing.Point(964, 362);
            this.listBox1.Name = "listBox1";
            this.listBox1.Size = new System.Drawing.Size(120, 280);
            this.listBox1.TabIndex = 1;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(997, 338);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(53, 12);
            this.label1.TabIndex = 2;
            this.label1.Text = "搜图标签";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(29, 396);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(449, 12);
            this.label2.TabIndex = 3;
            this.label2.Text = "第一步先截图，第二部用右键圈选搜索范围，然后点击确定搜索范围，最后搜索测试";
            // 
            // Snapshot
            // 
            this.Snapshot.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
            this.Snapshot.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.Snapshot.Location = new System.Drawing.Point(0, 0);
            this.Snapshot.Margin = new System.Windows.Forms.Padding(0);
            this.Snapshot.Name = "Snapshot";
            this.Snapshot.Size = new System.Drawing.Size(640, 360);
            this.Snapshot.TabIndex = 4;
            this.Snapshot.TabStop = false;
            this.Snapshot.Paint += new System.Windows.Forms.PaintEventHandler(this.Snapshot_Paint);
            this.Snapshot.MouseDown += new System.Windows.Forms.MouseEventHandler(this.Snapshot_MouseDown);
            this.Snapshot.MouseMove += new System.Windows.Forms.MouseEventHandler(this.Snapshot_MouseMove);
            this.Snapshot.MouseUp += new System.Windows.Forms.MouseEventHandler(this.Snapshot_MouseUp);
            this.Snapshot.MouseWheel += new System.Windows.Forms.MouseEventHandler(this.Snapshot_MouseWheel);
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(64, 423);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(75, 23);
            this.button1.TabIndex = 5;
            this.button1.Text = "截图";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button1_Click);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(48, 362);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(473, 12);
            this.label3.TabIndex = 6;
            this.label3.Text = "截图放大区域，按住左键平移（会自动放大到原始分辨率），按住右键移动圈选搜索区域";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(646, 274);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(449, 12);
            this.label4.TabIndex = 7;
            this.label4.Text = "双击切换采集模式/编辑模式，滚轮缩放，ctrl+滚轮水平缩放，shift+滚轮垂直缩放";
            // 
            // button2
            // 
            this.button2.Location = new System.Drawing.Point(145, 423);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(95, 23);
            this.button2.TabIndex = 8;
            this.button2.Text = "确定搜索范围";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += new System.EventHandler(this.button2_Click);
            // 
            // button3
            // 
            this.button3.Location = new System.Drawing.Point(246, 423);
            this.button3.Name = "button3";
            this.button3.Size = new System.Drawing.Size(95, 23);
            this.button3.TabIndex = 9;
            this.button3.Text = "搜索测试";
            this.button3.UseVisualStyleBackColor = true;
            this.button3.Click += new System.EventHandler(this.button3_Click);
            // 
            // CaptureVideo
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1096, 661);
            this.Controls.Add(this.button3);
            this.Controls.Add(this.button2);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.Snapshot);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.listBox1);
            this.Controls.Add(this.VideoSourcePlayerMonitor);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.Name = "CaptureVideo";
            this.Text = "CaptureVideo";
            this.Load += new System.EventHandler(this.CaptureVideo_Load);
            this.Resize += new System.EventHandler(this.CaptureVideo_Resize);
            ((System.ComponentModel.ISupportInitialize)(this.Snapshot)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private AForge.Controls.VideoSourcePlayer VideoSourcePlayerMonitor;
        private System.Windows.Forms.ListBox listBox1;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.PictureBox Snapshot;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.Button button3;
    }
}