namespace ILViewer
{
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
        private void InitializeComponent()
        {
            this.targetPicBox = new System.Windows.Forms.PictureBox();
            this.openBtn = new System.Windows.Forms.Button();
            this.ILpathBox = new System.Windows.Forms.TextBox();
            this.targRangX = new System.Windows.Forms.TextBox();
            this.targRangY = new System.Windows.Forms.TextBox();
            this.targRangW = new System.Windows.Forms.TextBox();
            this.targRangH = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.searchRangH = new System.Windows.Forms.TextBox();
            this.searchRangW = new System.Windows.Forms.TextBox();
            this.searchRangY = new System.Windows.Forms.TextBox();
            this.searchRangX = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.label8 = new System.Windows.Forms.Label();
            this.label9 = new System.Windows.Forms.Label();
            this.label10 = new System.Windows.Forms.Label();
            this.nameBox = new System.Windows.Forms.TextBox();
            this.matchDegreeBox = new System.Windows.Forms.TextBox();
            this.comboBox1 = new System.Windows.Forms.ComboBox();
            this.label11 = new System.Windows.Forms.Label();
            this.label12 = new System.Windows.Forms.Label();
            this.label13 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.targetPicBox)).BeginInit();
            this.SuspendLayout();
            // 
            // targetPicBox
            // 
            this.targetPicBox.Location = new System.Drawing.Point(24, 63);
            this.targetPicBox.Name = "targetPicBox";
            this.targetPicBox.Size = new System.Drawing.Size(88, 61);
            this.targetPicBox.TabIndex = 0;
            this.targetPicBox.TabStop = false;
            // 
            // openBtn
            // 
            this.openBtn.Location = new System.Drawing.Point(418, 12);
            this.openBtn.Name = "openBtn";
            this.openBtn.Size = new System.Drawing.Size(38, 23);
            this.openBtn.TabIndex = 1;
            this.openBtn.Text = "...";
            this.openBtn.UseVisualStyleBackColor = true;
            this.openBtn.Click += new System.EventHandler(this.openBtn_Click);
            // 
            // ILpathBox
            // 
            this.ILpathBox.Location = new System.Drawing.Point(24, 12);
            this.ILpathBox.Name = "ILpathBox";
            this.ILpathBox.Size = new System.Drawing.Size(388, 23);
            this.ILpathBox.TabIndex = 2;
            this.ILpathBox.Text = "D:\\repositories\\EasyCon2\\EasyCon\\bin\\Debug\\ImgLabel\\到湖边了.IL";
            // 
            // targRangX
            // 
            this.targRangX.Location = new System.Drawing.Point(50, 177);
            this.targRangX.Name = "targRangX";
            this.targRangX.Size = new System.Drawing.Size(70, 23);
            this.targRangX.TabIndex = 3;
            // 
            // targRangY
            // 
            this.targRangY.Location = new System.Drawing.Point(155, 177);
            this.targRangY.Name = "targRangY";
            this.targRangY.Size = new System.Drawing.Size(70, 23);
            this.targRangY.TabIndex = 4;
            // 
            // targRangW
            // 
            this.targRangW.Location = new System.Drawing.Point(50, 216);
            this.targRangW.Name = "targRangW";
            this.targRangW.Size = new System.Drawing.Size(70, 23);
            this.targRangW.TabIndex = 5;
            // 
            // targRangH
            // 
            this.targRangH.Location = new System.Drawing.Point(155, 216);
            this.targRangH.Name = "targRangH";
            this.targRangH.Size = new System.Drawing.Size(70, 23);
            this.targRangH.TabIndex = 6;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(115, 157);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(56, 17);
            this.label1.TabIndex = 7;
            this.label1.Text = "目标位置";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(369, 157);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(56, 17);
            this.label2.TabIndex = 12;
            this.label2.Text = "搜索范围";
            // 
            // searchRangH
            // 
            this.searchRangH.Location = new System.Drawing.Point(409, 216);
            this.searchRangH.Name = "searchRangH";
            this.searchRangH.Size = new System.Drawing.Size(70, 23);
            this.searchRangH.TabIndex = 11;
            // 
            // searchRangW
            // 
            this.searchRangW.Location = new System.Drawing.Point(304, 216);
            this.searchRangW.Name = "searchRangW";
            this.searchRangW.Size = new System.Drawing.Size(70, 23);
            this.searchRangW.TabIndex = 10;
            // 
            // searchRangY
            // 
            this.searchRangY.Location = new System.Drawing.Point(409, 177);
            this.searchRangY.Name = "searchRangY";
            this.searchRangY.Size = new System.Drawing.Size(70, 23);
            this.searchRangY.TabIndex = 9;
            // 
            // searchRangX
            // 
            this.searchRangX.Location = new System.Drawing.Point(304, 177);
            this.searchRangX.Name = "searchRangX";
            this.searchRangX.Size = new System.Drawing.Size(70, 23);
            this.searchRangX.TabIndex = 8;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(21, 183);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(19, 17);
            this.label3.TabIndex = 13;
            this.label3.Text = "X:";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(131, 183);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(18, 17);
            this.label4.TabIndex = 14;
            this.label4.Text = "Y:";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(279, 180);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(19, 17);
            this.label5.TabIndex = 15;
            this.label5.Text = "X:";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(385, 180);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(18, 17);
            this.label6.TabIndex = 16;
            this.label6.Text = "Y:";
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(21, 219);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(23, 17);
            this.label7.TabIndex = 17;
            this.label7.Text = "宽:";
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(275, 219);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(23, 17);
            this.label8.TabIndex = 18;
            this.label8.Text = "宽:";
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Location = new System.Drawing.Point(131, 219);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(23, 17);
            this.label9.TabIndex = 19;
            this.label9.Text = "高:";
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.Location = new System.Drawing.Point(385, 219);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(23, 17);
            this.label10.TabIndex = 20;
            this.label10.Text = "高:";
            // 
            // nameBox
            // 
            this.nameBox.Location = new System.Drawing.Point(181, 63);
            this.nameBox.Name = "nameBox";
            this.nameBox.Size = new System.Drawing.Size(100, 23);
            this.nameBox.TabIndex = 21;
            // 
            // matchDegreeBox
            // 
            this.matchDegreeBox.Location = new System.Drawing.Point(181, 97);
            this.matchDegreeBox.Name = "matchDegreeBox";
            this.matchDegreeBox.Size = new System.Drawing.Size(100, 23);
            this.matchDegreeBox.TabIndex = 22;
            // 
            // comboBox1
            // 
            this.comboBox1.FormattingEnabled = true;
            this.comboBox1.Location = new System.Drawing.Point(352, 99);
            this.comboBox1.Name = "comboBox1";
            this.comboBox1.Size = new System.Drawing.Size(123, 25);
            this.comboBox1.TabIndex = 23;
            // 
            // label11
            // 
            this.label11.AutoSize = true;
            this.label11.Location = new System.Drawing.Point(118, 66);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(59, 17);
            this.label11.TabIndex = 25;
            this.label11.Text = "搜图标签:";
            // 
            // label12
            // 
            this.label12.AutoSize = true;
            this.label12.Location = new System.Drawing.Point(130, 100);
            this.label12.Name = "label12";
            this.label12.Size = new System.Drawing.Size(47, 17);
            this.label12.TabIndex = 26;
            this.label12.Text = "匹配度:";
            // 
            // label13
            // 
            this.label13.AutoSize = true;
            this.label13.Location = new System.Drawing.Point(290, 103);
            this.label13.Name = "label13";
            this.label13.Size = new System.Drawing.Size(59, 17);
            this.label13.TabIndex = 27;
            this.label13.Text = "搜索方法:";
            // 
            // Form1
            // 
            this.AllowDrop = true;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(503, 271);
            this.Controls.Add(this.label13);
            this.Controls.Add(this.label12);
            this.Controls.Add(this.label11);
            this.Controls.Add(this.comboBox1);
            this.Controls.Add(this.matchDegreeBox);
            this.Controls.Add(this.nameBox);
            this.Controls.Add(this.label10);
            this.Controls.Add(this.label9);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.searchRangH);
            this.Controls.Add(this.searchRangW);
            this.Controls.Add(this.searchRangY);
            this.Controls.Add(this.searchRangX);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.targRangH);
            this.Controls.Add(this.targRangW);
            this.Controls.Add(this.targRangY);
            this.Controls.Add(this.targRangX);
            this.Controls.Add(this.ILpathBox);
            this.Controls.Add(this.openBtn);
            this.Controls.Add(this.targetPicBox);
            this.MaximizeBox = false;
            this.Name = "Form1";
            this.Text = "ECIL Viewer";
            this.DragDrop += new System.Windows.Forms.DragEventHandler(this.Form1_DragDrop);
            this.DragEnter += new System.Windows.Forms.DragEventHandler(this.Form1_DragEnter);
            ((System.ComponentModel.ISupportInitialize)(this.targetPicBox)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.PictureBox targetPicBox;
        private System.Windows.Forms.Button openBtn;
        private System.Windows.Forms.TextBox ILpathBox;
        private System.Windows.Forms.TextBox targRangX;
        private System.Windows.Forms.TextBox targRangY;
        private System.Windows.Forms.TextBox targRangW;
        private System.Windows.Forms.TextBox targRangH;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox searchRangH;
        private System.Windows.Forms.TextBox searchRangW;
        private System.Windows.Forms.TextBox searchRangY;
        private System.Windows.Forms.TextBox searchRangX;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.Label label10;
        private System.Windows.Forms.TextBox nameBox;
        private System.Windows.Forms.TextBox matchDegreeBox;
        private System.Windows.Forms.ComboBox comboBox1;
        private System.Windows.Forms.Label label11;
        private System.Windows.Forms.Label label12;
        private System.Windows.Forms.Label label13;
    }
}
