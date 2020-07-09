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
            this.VideoSourcePlayerMonitor = new AForge.Controls.VideoSourcePlayer();
            this.SuspendLayout();
            // 
            // VideoSourcePlayerMonitor
            // 
            this.VideoSourcePlayerMonitor.Dock = System.Windows.Forms.DockStyle.Fill;
            this.VideoSourcePlayerMonitor.Enabled = false;
            this.VideoSourcePlayerMonitor.Location = new System.Drawing.Point(0, 0);
            this.VideoSourcePlayerMonitor.Margin = new System.Windows.Forms.Padding(13);
            this.VideoSourcePlayerMonitor.Name = "VideoSourcePlayerMonitor";
            this.VideoSourcePlayerMonitor.Size = new System.Drawing.Size(800, 450);
            this.VideoSourcePlayerMonitor.TabIndex = 0;
            this.VideoSourcePlayerMonitor.Text = "videoSourcePlayer1";
            this.VideoSourcePlayerMonitor.VideoSource = null;
            // 
            // CaptureVideo
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.VideoSourcePlayerMonitor);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "CaptureVideo";
            this.Text = "CaptureVideo";
            this.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.CaptureVideo_MouseDoubleClick);
            this.MouseDown += new System.Windows.Forms.MouseEventHandler(this.CaptureVideo_MouseDown);
            this.MouseMove += new System.Windows.Forms.MouseEventHandler(this.CaptureVideo_MouseMove);
            this.MouseUp += new System.Windows.Forms.MouseEventHandler(this.CaptureVideo_MouseUp);
            this.MouseWheel += new System.Windows.Forms.MouseEventHandler(this.CaptureVideo_MouseWheel);
            this.ResumeLayout(false);

        }

        #endregion

        private AForge.Controls.VideoSourcePlayer VideoSourcePlayerMonitor;
    }
}