namespace EasyCon
{
    partial class FormSCombin
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
            this.buttonCancel = new System.Windows.Forms.Button();
            this.buttonOK = new System.Windows.Forms.Button();
            this.textBoxScript = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // buttonCancel
            // 
            this.buttonCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.buttonCancel.Location = new System.Drawing.Point(259, 280);
            this.buttonCancel.Name = "buttonCancel";
            this.buttonCancel.Size = new System.Drawing.Size(174, 32);
            this.buttonCancel.TabIndex = 43;
            this.buttonCancel.Text = "取消";
            this.buttonCancel.UseVisualStyleBackColor = true;
            this.buttonCancel.Click += new System.EventHandler(this.buttonCancel_Click);
            // 
            // buttonOK
            // 
            this.buttonOK.Location = new System.Drawing.Point(60, 280);
            this.buttonOK.Name = "buttonOK";
            this.buttonOK.Size = new System.Drawing.Size(174, 32);
            this.buttonOK.TabIndex = 42;
            this.buttonOK.Text = "确定";
            this.buttonOK.UseVisualStyleBackColor = true;
            this.buttonOK.Click += new System.EventHandler(this.buttonOK_Click);
            // 
            // textBoxScript
            // 
            this.textBoxScript.AcceptsReturn = true;
            this.textBoxScript.AcceptsTab = true;
            this.textBoxScript.AllowDrop = true;
            this.textBoxScript.Location = new System.Drawing.Point(12, 24);
            this.textBoxScript.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.textBoxScript.Multiline = true;
            this.textBoxScript.Name = "textBoxScript";
            this.textBoxScript.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.textBoxScript.Size = new System.Drawing.Size(476, 189);
            this.textBoxScript.TabIndex = 44;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 8);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(35, 12);
            this.label1.TabIndex = 45;
            this.label1.Text = "脚本2";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(21, 222);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(461, 48);
            this.label2.TabIndex = 46;
            this.label2.Text = "说明：\r\n    先在主界面写好脚本1，再在上方写好脚本2。\r\n    点击确定后将保存到程序目录下的temp.bin中并烧录到单片机。\r\n    未连接单片机时" +
    "会烧录失败无需关心。可自行将temp.bin烧录到单片机eeprom中。\r\n";
            // 
            // FormSCombin
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(501, 326);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.textBoxScript);
            this.Controls.Add(this.buttonCancel);
            this.Controls.Add(this.buttonOK);
            this.MaximizeBox = false;
            this.Name = "FormSCombin";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "合并脚本";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button buttonCancel;
        private System.Windows.Forms.Button buttonOK;
        private System.Windows.Forms.TextBox textBoxScript;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
    }
}