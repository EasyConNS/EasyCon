namespace PTController
{
    partial class FormController
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
            this.SuspendLayout();
            // 
            // FormController
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.BackgroundImage = global::PTController.Properties.Resources.JoyCon;
            this.ClientSize = new System.Drawing.Size(100, 100);
            this.Cursor = System.Windows.Forms.Cursors.Hand;
            this.DoubleBuffered = true;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "FormController";
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.Load += new System.EventHandler(this.FormController_Load);
            this.MouseDown += new System.Windows.Forms.MouseEventHandler(this.FormController_MouseDown);
            this.MouseMove += new System.Windows.Forms.MouseEventHandler(this.FormController_MouseMove);
            this.MouseUp += new System.Windows.Forms.MouseEventHandler(this.FormController_MouseUp);
            this.ResumeLayout(false);

        }

        #endregion
    }
}