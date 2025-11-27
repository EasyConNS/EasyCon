namespace EasyVPad
{
    partial class VPadForm
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
            SuspendLayout();
            // 
            // VPadForm
            // 
            AutoScaleMode = AutoScaleMode.None;
            BackgroundImage = EasyVPad.Properties.Resources.JoyCon;
            ClientSize = new Size(100, 100);
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.None;
            Name = "VPadForm";
            ShowInTaskbar = false;
            TopMost = true;
            Load += FormController_Load;
            MouseDown += FormController_MouseDown;
            MouseMove += FormController_MouseMove;
            MouseUp += FormController_MouseUp;
            ResumeLayout(false);

        }

        #endregion
    }
}