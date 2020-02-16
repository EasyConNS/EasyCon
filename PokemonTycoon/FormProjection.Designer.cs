namespace PokemonTycoon
{
    partial class FormProjection
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
            this.videoSourcePlayerProjection = new AForge.Controls.VideoSourcePlayer();
            this.SuspendLayout();
            // 
            // videoSourcePlayerProjection
            // 
            this.videoSourcePlayerProjection.Location = new System.Drawing.Point(0, 0);
            this.videoSourcePlayerProjection.Name = "videoSourcePlayerProjection";
            this.videoSourcePlayerProjection.Size = new System.Drawing.Size(75, 23);
            this.videoSourcePlayerProjection.TabIndex = 0;
            this.videoSourcePlayerProjection.Text = "videoSourcePlayer1";
            this.videoSourcePlayerProjection.VideoSource = null;
            // 
            // FormProjection
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.videoSourcePlayerProjection);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "FormProjection";
            this.Text = "FormProjection";
            this.Load += new System.EventHandler(this.FormProjection_Load);
            this.ResumeLayout(false);

        }

        #endregion

        private AForge.Controls.VideoSourcePlayer videoSourcePlayerProjection;
    }
}