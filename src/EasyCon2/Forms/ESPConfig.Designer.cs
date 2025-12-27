namespace EasyCon2.Forms
{
    partial class ESPConfig
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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ESPConfig));
            this.proButton = new System.Windows.Forms.RadioButton();
            this.jcrButton = new System.Windows.Forms.RadioButton();
            this.jclButton = new System.Windows.Forms.RadioButton();
            this.bodyLabel = new System.Windows.Forms.Label();
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.buttonLabel = new System.Windows.Forms.Label();
            this.gripLLabel = new System.Windows.Forms.Label();
            this.gripRlabel = new System.Windows.Forms.Label();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.setModeButton = new System.Windows.Forms.Button();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.setColorButton = new System.Windows.Forms.Button();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.label2 = new System.Windows.Forms.Label();
            this.amiiboIndexNum = new System.Windows.Forms.NumericUpDown();
            this.nickBox = new System.Windows.Forms.TextBox();
            this.usernameBox = new System.Windows.Forms.TextBox();
            this.label11 = new System.Windows.Forms.Label();
            this.label10 = new System.Windows.Forms.Label();
            this.label9 = new System.Windows.Forms.Label();
            this.selectGameBox = new System.Windows.Forms.ComboBox();
            this.label8 = new System.Windows.Forms.Label();
            this.amiiboView = new System.Windows.Forms.PictureBox();
            this.changeAmiiboButton = new System.Windows.Forms.Button();
            this.saveAmiiboButton = new System.Windows.Forms.Button();
            this.label6 = new System.Windows.Forms.Label();
            this.selectAmiiboBox = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.saveIndexBox = new System.Windows.Forms.ComboBox();
            this.groupBox1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.groupBox3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.amiiboIndexNum)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.amiiboView)).BeginInit();
            this.SuspendLayout();
            // 
            // proButton
            // 
            this.proButton.AutoSize = true;
            this.proButton.Checked = true;
            this.proButton.Location = new System.Drawing.Point(31, 24);
            this.proButton.Name = "proButton";
            this.proButton.Size = new System.Drawing.Size(46, 21);
            this.proButton.TabIndex = 0;
            this.proButton.TabStop = true;
            this.proButton.Text = "Pro";
            this.proButton.UseVisualStyleBackColor = true;
            // 
            // jcrButton
            // 
            this.jcrButton.AutoSize = true;
            this.jcrButton.Location = new System.Drawing.Point(31, 51);
            this.jcrButton.Name = "jcrButton";
            this.jcrButton.Size = new System.Drawing.Size(81, 21);
            this.jcrButton.TabIndex = 1;
            this.jcrButton.Text = "JoyCon-R";
            this.jcrButton.UseVisualStyleBackColor = true;
            // 
            // jclButton
            // 
            this.jclButton.AutoSize = true;
            this.jclButton.Location = new System.Drawing.Point(31, 78);
            this.jclButton.Name = "jclButton";
            this.jclButton.Size = new System.Drawing.Size(79, 21);
            this.jclButton.TabIndex = 2;
            this.jclButton.Text = "JoyCon-L";
            this.jclButton.UseVisualStyleBackColor = true;
            // 
            // bodyLabel
            // 
            this.bodyLabel.AutoSize = true;
            this.bodyLabel.BackColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.bodyLabel.ForeColor = System.Drawing.SystemColors.ControlLightLight;
            this.bodyLabel.Location = new System.Drawing.Point(40, 26);
            this.bodyLabel.Name = "bodyLabel";
            this.bodyLabel.Size = new System.Drawing.Size(56, 17);
            this.bodyLabel.TabIndex = 4;
            this.bodyLabel.Text = "机身颜色";
            this.bodyLabel.Click += new System.EventHandler(this.body_Click);
            // 
            // contextMenuStrip1
            // 
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            this.contextMenuStrip1.Size = new System.Drawing.Size(61, 4);
            // 
            // buttonLabel
            // 
            this.buttonLabel.AutoSize = true;
            this.buttonLabel.BackColor = System.Drawing.SystemColors.ButtonHighlight;
            this.buttonLabel.ForeColor = System.Drawing.SystemColors.ControlText;
            this.buttonLabel.Location = new System.Drawing.Point(40, 76);
            this.buttonLabel.Name = "buttonLabel";
            this.buttonLabel.Size = new System.Drawing.Size(56, 17);
            this.buttonLabel.TabIndex = 6;
            this.buttonLabel.Text = "按钮颜色";
            this.buttonLabel.Click += new System.EventHandler(this.button_Click);
            // 
            // gripLLabel
            // 
            this.gripLLabel.AutoSize = true;
            this.gripLLabel.BackColor = System.Drawing.SystemColors.Highlight;
            this.gripLLabel.Location = new System.Drawing.Point(121, 26);
            this.gripLLabel.Name = "gripLLabel";
            this.gripLLabel.Size = new System.Drawing.Size(136, 17);
            this.gripLLabel.TabIndex = 7;
            this.gripLLabel.Text = "左手柄颜色（Pro独有）";
            this.gripLLabel.Click += new System.EventHandler(this.gripl_Click);
            // 
            // gripRlabel
            // 
            this.gripRlabel.AutoSize = true;
            this.gripRlabel.BackColor = System.Drawing.Color.Yellow;
            this.gripRlabel.ForeColor = System.Drawing.SystemColors.ControlText;
            this.gripRlabel.Location = new System.Drawing.Point(122, 76);
            this.gripRlabel.Name = "gripRlabel";
            this.gripRlabel.Size = new System.Drawing.Size(136, 17);
            this.gripRlabel.TabIndex = 8;
            this.gripRlabel.Text = "右手柄颜色（Pro独有）";
            this.gripRlabel.Click += new System.EventHandler(this.gripr_Click);
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.setModeButton);
            this.groupBox1.Controls.Add(this.jcrButton);
            this.groupBox1.Controls.Add(this.proButton);
            this.groupBox1.Controls.Add(this.jclButton);
            this.groupBox1.Location = new System.Drawing.Point(12, 12);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(176, 138);
            this.groupBox1.TabIndex = 9;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "手柄模式";
            // 
            // setModeButton
            // 
            this.setModeButton.Location = new System.Drawing.Point(31, 105);
            this.setModeButton.Name = "setModeButton";
            this.setModeButton.Size = new System.Drawing.Size(75, 23);
            this.setModeButton.TabIndex = 3;
            this.setModeButton.Text = "修改模式";
            this.setModeButton.UseVisualStyleBackColor = true;
            this.setModeButton.Click += new System.EventHandler(this.setMode_Click);
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.setColorButton);
            this.groupBox2.Controls.Add(this.buttonLabel);
            this.groupBox2.Controls.Add(this.bodyLabel);
            this.groupBox2.Controls.Add(this.gripRlabel);
            this.groupBox2.Controls.Add(this.gripLLabel);
            this.groupBox2.Location = new System.Drawing.Point(194, 12);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(352, 138);
            this.groupBox2.TabIndex = 10;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "颜色";
            // 
            // setColorButton
            // 
            this.setColorButton.Location = new System.Drawing.Point(79, 103);
            this.setColorButton.Name = "setColorButton";
            this.setColorButton.Size = new System.Drawing.Size(102, 23);
            this.setColorButton.TabIndex = 9;
            this.setColorButton.Text = "修改颜色";
            this.setColorButton.UseVisualStyleBackColor = true;
            this.setColorButton.Click += new System.EventHandler(this.setColor_Click);
            // 
            // groupBox3
            // 
            this.groupBox3.Controls.Add(this.label2);
            this.groupBox3.Controls.Add(this.amiiboIndexNum);
            this.groupBox3.Controls.Add(this.nickBox);
            this.groupBox3.Controls.Add(this.usernameBox);
            this.groupBox3.Controls.Add(this.label11);
            this.groupBox3.Controls.Add(this.label10);
            this.groupBox3.Controls.Add(this.label9);
            this.groupBox3.Controls.Add(this.selectGameBox);
            this.groupBox3.Controls.Add(this.label8);
            this.groupBox3.Controls.Add(this.amiiboView);
            this.groupBox3.Controls.Add(this.changeAmiiboButton);
            this.groupBox3.Controls.Add(this.saveAmiiboButton);
            this.groupBox3.Controls.Add(this.label6);
            this.groupBox3.Controls.Add(this.selectAmiiboBox);
            this.groupBox3.Controls.Add(this.label1);
            this.groupBox3.Controls.Add(this.saveIndexBox);
            this.groupBox3.Location = new System.Drawing.Point(12, 156);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(534, 336);
            this.groupBox3.TabIndex = 11;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "Amiibo";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(19, 295);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(56, 17);
            this.label2.TabIndex = 17;
            this.label2.Text = "当前序号";
            // 
            // amiiboIndexNum
            // 
            this.amiiboIndexNum.Location = new System.Drawing.Point(81, 293);
            this.amiiboIndexNum.Maximum = new decimal(new int[] {
            19,
            0,
            0,
            0});
            this.amiiboIndexNum.Name = "amiiboIndexNum";
            this.amiiboIndexNum.Size = new System.Drawing.Size(45, 23);
            this.amiiboIndexNum.TabIndex = 16;
            // 
            // nickBox
            // 
            this.nickBox.Location = new System.Drawing.Point(81, 160);
            this.nickBox.Name = "nickBox";
            this.nickBox.Size = new System.Drawing.Size(100, 23);
            this.nickBox.TabIndex = 15;
            // 
            // usernameBox
            // 
            this.usernameBox.Location = new System.Drawing.Point(81, 131);
            this.usernameBox.Name = "usernameBox";
            this.usernameBox.Size = new System.Drawing.Size(100, 23);
            this.usernameBox.TabIndex = 14;
            this.usernameBox.Text = "EasyCon";
            // 
            // label11
            // 
            this.label11.AutoSize = true;
            this.label11.Location = new System.Drawing.Point(43, 163);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(32, 17);
            this.label11.TabIndex = 13;
            this.label11.Text = "卡名";
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.Location = new System.Drawing.Point(31, 134);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(44, 17);
            this.label10.TabIndex = 12;
            this.label10.Text = "用户名";
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Location = new System.Drawing.Point(43, 70);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(32, 17);
            this.label9.TabIndex = 11;
            this.label9.Text = "游戏";
            // 
            // selectGameBox
            // 
            this.selectGameBox.FormattingEnabled = true;
            this.selectGameBox.Location = new System.Drawing.Point(81, 66);
            this.selectGameBox.Name = "selectGameBox";
            this.selectGameBox.Size = new System.Drawing.Size(158, 25);
            this.selectGameBox.TabIndex = 10;
            this.selectGameBox.Text = "选择游戏";
            this.selectGameBox.SelectionChangeCommitted += new System.EventHandler(this.game_SelectionChangeCommitted);
            // 
            // label8
            // 
            this.label8.Location = new System.Drawing.Point(19, 218);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(220, 38);
            this.label8.TabIndex = 9;
            this.label8.Text = "默认Amiibo是自动生成，自定义通过copy bin文件到Amiibo文件夹内";
            // 
            // amiiboView
            // 
            this.amiiboView.Location = new System.Drawing.Point(261, 22);
            this.amiiboView.Name = "amiiboView";
            this.amiiboView.Size = new System.Drawing.Size(258, 305);
            this.amiiboView.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.amiiboView.TabIndex = 8;
            this.amiiboView.TabStop = false;
            // 
            // changeAmiiboButton
            // 
            this.changeAmiiboButton.Location = new System.Drawing.Point(137, 292);
            this.changeAmiiboButton.Name = "changeAmiiboButton";
            this.changeAmiiboButton.Size = new System.Drawing.Size(102, 23);
            this.changeAmiiboButton.TabIndex = 7;
            this.changeAmiiboButton.Text = "激活";
            this.changeAmiiboButton.UseVisualStyleBackColor = true;
            this.changeAmiiboButton.Click += new System.EventHandler(this.changeAmiibo_Click);
            // 
            // saveAmiiboButton
            // 
            this.saveAmiiboButton.Location = new System.Drawing.Point(19, 189);
            this.saveAmiiboButton.Name = "saveAmiiboButton";
            this.saveAmiiboButton.Size = new System.Drawing.Size(220, 23);
            this.saveAmiiboButton.TabIndex = 4;
            this.saveAmiiboButton.Text = "保存Amiibo";
            this.saveAmiiboButton.UseVisualStyleBackColor = true;
            this.saveAmiiboButton.Click += new System.EventHandler(this.saveAmiibo_Click);
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(19, 102);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(56, 17);
            this.label6.TabIndex = 3;
            this.label6.Text = "存储内容";
            // 
            // selectAmiiboBox
            // 
            this.selectAmiiboBox.FormattingEnabled = true;
            this.selectAmiiboBox.Location = new System.Drawing.Point(81, 97);
            this.selectAmiiboBox.Name = "selectAmiiboBox";
            this.selectAmiiboBox.Size = new System.Drawing.Size(158, 25);
            this.selectAmiiboBox.TabIndex = 2;
            this.selectAmiiboBox.Text = "选择Amiibo";
            this.selectAmiiboBox.DropDown += new System.EventHandler(this.amiibo_DropDown);
            this.selectAmiiboBox.SelectionChangeCommitted += new System.EventHandler(this.amiibo_SelectionChangeCommitted);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(19, 37);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(56, 17);
            this.label1.TabIndex = 1;
            this.label1.Text = "存储序号";
            // 
            // saveIndexBox
            // 
            this.saveIndexBox.FormattingEnabled = true;
            this.saveIndexBox.Items.AddRange(new object[] {
            "0",
            "1",
            "2",
            "3",
            "4",
            "5",
            "6",
            "7",
            "8",
            "9",
            "10",
            "11",
            "12",
            "13",
            "14",
            "15",
            "16",
            "17",
            "18",
            "19"});
            this.saveIndexBox.Location = new System.Drawing.Point(81, 34);
            this.saveIndexBox.Name = "saveIndexBox";
            this.saveIndexBox.Size = new System.Drawing.Size(100, 25);
            this.saveIndexBox.TabIndex = 0;
            this.saveIndexBox.Text = "选择序号";
            // 
            // ControllerConfig
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(556, 494);
            this.Controls.Add(this.groupBox3);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.groupBox1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "ControllerConfig";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "手柄设置";
            this.Load += new System.EventHandler(this.ControllerConfig_Load);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.groupBox3.ResumeLayout(false);
            this.groupBox3.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.amiiboIndexNum)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.amiiboView)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private RadioButton proButton;
        private RadioButton jcrButton;
        private RadioButton jclButton;
        private Label bodyLabel;
        private ContextMenuStrip contextMenuStrip1;
        private Label buttonLabel;
        private Label gripLLabel;
        private Label gripRlabel;
        private GroupBox groupBox1;
        private GroupBox groupBox2;
        private Button setModeButton;
        private Button setColorButton;
        private GroupBox groupBox3;
        private Button saveAmiiboButton;
        private Label label6;
        private ComboBox selectAmiiboBox;
        private Label label1;
        private ComboBox saveIndexBox;
        private Button changeAmiiboButton;
        private PictureBox amiiboView;
        private Label label8;
        private Label label9;
        private ComboBox selectGameBox;
        private TextBox nickBox;
        private TextBox usernameBox;
        private Label label11;
        private Label label10;
        private NumericUpDown amiiboIndexNum;
        private Label label2;
    }
}