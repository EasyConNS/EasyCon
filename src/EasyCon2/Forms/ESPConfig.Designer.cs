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
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ESPConfig));
            proButton = new RadioButton();
            jcrButton = new RadioButton();
            jclButton = new RadioButton();
            bodyLabel = new Label();
            contextMenuStrip1 = new ContextMenuStrip(components);
            buttonLabel = new Label();
            gripLLabel = new Label();
            gripRlabel = new Label();
            groupBox1 = new GroupBox();
            setModeButton = new Button();
            groupBox2 = new GroupBox();
            setColorButton = new Button();
            groupBox3 = new GroupBox();
            label2 = new Label();
            amiiboIndexNum = new NumericUpDown();
            nickBox = new TextBox();
            usernameBox = new TextBox();
            label11 = new Label();
            label10 = new Label();
            label9 = new Label();
            selectGameBox = new ComboBox();
            label8 = new Label();
            amiiboView = new PictureBox();
            changeAmiiboButton = new Button();
            saveAmiiboButton = new Button();
            label6 = new Label();
            selectAmiiboBox = new ComboBox();
            label1 = new Label();
            saveIndexBox = new ComboBox();
            groupBox1.SuspendLayout();
            groupBox2.SuspendLayout();
            groupBox3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)amiiboIndexNum).BeginInit();
            ((System.ComponentModel.ISupportInitialize)amiiboView).BeginInit();
            SuspendLayout();
            // 
            // proButton
            // 
            proButton.AutoSize = true;
            proButton.Checked = true;
            proButton.Location = new Point(24, 39);
            proButton.Margin = new Padding(4, 4, 4, 4);
            proButton.Name = "proButton";
            proButton.Size = new Size(55, 24);
            proButton.TabIndex = 0;
            proButton.TabStop = true;
            proButton.Text = "Pro";
            proButton.UseVisualStyleBackColor = true;
            // 
            // jcrButton
            // 
            jcrButton.AutoSize = true;
            jcrButton.Location = new Point(87, 39);
            jcrButton.Margin = new Padding(4, 4, 4, 4);
            jcrButton.Name = "jcrButton";
            jcrButton.Size = new Size(99, 24);
            jcrButton.TabIndex = 1;
            jcrButton.Text = "JoyCon-R";
            jcrButton.UseVisualStyleBackColor = true;
            // 
            // jclButton
            // 
            jclButton.AutoSize = true;
            jclButton.Location = new Point(193, 39);
            jclButton.Margin = new Padding(4, 4, 4, 4);
            jclButton.Name = "jclButton";
            jclButton.Size = new Size(97, 24);
            jclButton.TabIndex = 2;
            jclButton.Text = "JoyCon-L";
            jclButton.UseVisualStyleBackColor = true;
            // 
            // bodyLabel
            // 
            bodyLabel.AutoSize = true;
            bodyLabel.BackColor = SystemColors.ActiveCaptionText;
            bodyLabel.ForeColor = SystemColors.ControlLightLight;
            bodyLabel.Location = new Point(51, 31);
            bodyLabel.Margin = new Padding(4, 0, 4, 0);
            bodyLabel.Name = "bodyLabel";
            bodyLabel.Size = new Size(69, 20);
            bodyLabel.TabIndex = 4;
            bodyLabel.Text = "机身颜色";
            bodyLabel.Click += Color_Changed;
            // 
            // contextMenuStrip1
            // 
            contextMenuStrip1.ImageScalingSize = new Size(20, 20);
            contextMenuStrip1.Name = "contextMenuStrip1";
            contextMenuStrip1.Size = new Size(61, 4);
            // 
            // buttonLabel
            // 
            buttonLabel.AutoSize = true;
            buttonLabel.BackColor = SystemColors.ButtonHighlight;
            buttonLabel.ForeColor = SystemColors.ControlText;
            buttonLabel.Location = new Point(50, 68);
            buttonLabel.Margin = new Padding(4, 0, 4, 0);
            buttonLabel.Name = "buttonLabel";
            buttonLabel.Size = new Size(69, 20);
            buttonLabel.TabIndex = 6;
            buttonLabel.Text = "按钮颜色";
            buttonLabel.Click += Color_Changed;
            // 
            // gripLLabel
            // 
            gripLLabel.AutoSize = true;
            gripLLabel.BackColor = SystemColors.Highlight;
            gripLLabel.Location = new Point(156, 31);
            gripLLabel.Margin = new Padding(4, 0, 4, 0);
            gripLLabel.Name = "gripLLabel";
            gripLLabel.Size = new Size(169, 20);
            gripLLabel.TabIndex = 7;
            gripLLabel.Text = "左手柄颜色（Pro独有）";
            gripLLabel.Click += Color_Changed;
            // 
            // gripRlabel
            // 
            gripRlabel.AutoSize = true;
            gripRlabel.BackColor = Color.Yellow;
            gripRlabel.ForeColor = SystemColors.ControlText;
            gripRlabel.Location = new Point(156, 68);
            gripRlabel.Margin = new Padding(4, 0, 4, 0);
            gripRlabel.Name = "gripRlabel";
            gripRlabel.Size = new Size(169, 20);
            gripRlabel.TabIndex = 8;
            gripRlabel.Text = "右手柄颜色（Pro独有）";
            gripRlabel.Click += Color_Changed;
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(setModeButton);
            groupBox1.Controls.Add(jcrButton);
            groupBox1.Controls.Add(proButton);
            groupBox1.Controls.Add(jclButton);
            groupBox1.Location = new Point(15, 14);
            groupBox1.Margin = new Padding(4, 4, 4, 4);
            groupBox1.Name = "groupBox1";
            groupBox1.Padding = new Padding(4, 4, 4, 4);
            groupBox1.Size = new Size(306, 134);
            groupBox1.TabIndex = 9;
            groupBox1.TabStop = false;
            groupBox1.Text = "手柄模式";
            // 
            // setModeButton
            // 
            setModeButton.Location = new Point(202, 87);
            setModeButton.Margin = new Padding(4, 4, 4, 4);
            setModeButton.Name = "setModeButton";
            setModeButton.Size = new Size(96, 39);
            setModeButton.TabIndex = 3;
            setModeButton.Text = "修改模式";
            setModeButton.UseVisualStyleBackColor = true;
            setModeButton.Click += setMode_Click;
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(setColorButton);
            groupBox2.Controls.Add(buttonLabel);
            groupBox2.Controls.Add(bodyLabel);
            groupBox2.Controls.Add(gripRlabel);
            groupBox2.Controls.Add(gripLLabel);
            groupBox2.Location = new Point(342, 14);
            groupBox2.Margin = new Padding(4, 4, 4, 4);
            groupBox2.Name = "groupBox2";
            groupBox2.Padding = new Padding(4, 4, 4, 4);
            groupBox2.Size = new Size(351, 134);
            groupBox2.TabIndex = 10;
            groupBox2.TabStop = false;
            groupBox2.Text = "颜色";
            // 
            // setColorButton
            // 
            setColorButton.Location = new Point(210, 99);
            setColorButton.Margin = new Padding(4, 4, 4, 4);
            setColorButton.Name = "setColorButton";
            setColorButton.Size = new Size(115, 27);
            setColorButton.TabIndex = 9;
            setColorButton.Text = "修改颜色";
            setColorButton.UseVisualStyleBackColor = true;
            setColorButton.Click += setColor_Click;
            // 
            // groupBox3
            // 
            groupBox3.Controls.Add(label2);
            groupBox3.Controls.Add(amiiboIndexNum);
            groupBox3.Controls.Add(nickBox);
            groupBox3.Controls.Add(usernameBox);
            groupBox3.Controls.Add(label11);
            groupBox3.Controls.Add(label10);
            groupBox3.Controls.Add(label9);
            groupBox3.Controls.Add(selectGameBox);
            groupBox3.Controls.Add(label8);
            groupBox3.Controls.Add(amiiboView);
            groupBox3.Controls.Add(changeAmiiboButton);
            groupBox3.Controls.Add(saveAmiiboButton);
            groupBox3.Controls.Add(label6);
            groupBox3.Controls.Add(selectAmiiboBox);
            groupBox3.Controls.Add(label1);
            groupBox3.Controls.Add(saveIndexBox);
            groupBox3.Location = new Point(15, 156);
            groupBox3.Margin = new Padding(4, 4, 4, 4);
            groupBox3.Name = "groupBox3";
            groupBox3.Padding = new Padding(4, 4, 4, 4);
            groupBox3.Size = new Size(687, 423);
            groupBox3.TabIndex = 11;
            groupBox3.TabStop = false;
            groupBox3.Text = "Amiibo";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(24, 347);
            label2.Margin = new Padding(4, 0, 4, 0);
            label2.Name = "label2";
            label2.Size = new Size(69, 20);
            label2.TabIndex = 17;
            label2.Text = "当前序号";
            // 
            // amiiboIndexNum
            // 
            amiiboIndexNum.Location = new Point(104, 345);
            amiiboIndexNum.Margin = new Padding(4, 4, 4, 4);
            amiiboIndexNum.Maximum = new decimal(new int[] { 19, 0, 0, 0 });
            amiiboIndexNum.Name = "amiiboIndexNum";
            amiiboIndexNum.Size = new Size(58, 27);
            amiiboIndexNum.TabIndex = 16;
            // 
            // nickBox
            // 
            nickBox.Location = new Point(104, 188);
            nickBox.Margin = new Padding(4, 4, 4, 4);
            nickBox.Name = "nickBox";
            nickBox.Size = new Size(202, 27);
            nickBox.TabIndex = 15;
            // 
            // usernameBox
            // 
            usernameBox.Location = new Point(104, 154);
            usernameBox.Margin = new Padding(4, 4, 4, 4);
            usernameBox.Name = "usernameBox";
            usernameBox.Size = new Size(202, 27);
            usernameBox.TabIndex = 14;
            usernameBox.Text = "EasyCon";
            // 
            // label11
            // 
            label11.AutoSize = true;
            label11.Location = new Point(55, 192);
            label11.Margin = new Padding(4, 0, 4, 0);
            label11.Name = "label11";
            label11.Size = new Size(39, 20);
            label11.TabIndex = 13;
            label11.Text = "卡名";
            // 
            // label10
            // 
            label10.AutoSize = true;
            label10.Location = new Point(40, 158);
            label10.Margin = new Padding(4, 0, 4, 0);
            label10.Name = "label10";
            label10.Size = new Size(54, 20);
            label10.TabIndex = 12;
            label10.Text = "用户名";
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.Location = new Point(55, 82);
            label9.Margin = new Padding(4, 0, 4, 0);
            label9.Name = "label9";
            label9.Size = new Size(39, 20);
            label9.TabIndex = 11;
            label9.Text = "游戏";
            // 
            // selectGameBox
            // 
            selectGameBox.FormattingEnabled = true;
            selectGameBox.Location = new Point(104, 78);
            selectGameBox.Margin = new Padding(4, 4, 4, 4);
            selectGameBox.Name = "selectGameBox";
            selectGameBox.Size = new Size(202, 28);
            selectGameBox.TabIndex = 10;
            selectGameBox.Text = "选择游戏";
            selectGameBox.SelectionChangeCommitted += game_SelectionChangeCommitted;
            // 
            // label8
            // 
            label8.Location = new Point(24, 256);
            label8.Margin = new Padding(4, 0, 4, 0);
            label8.Name = "label8";
            label8.Size = new Size(283, 45);
            label8.TabIndex = 9;
            label8.Text = "默认Amiibo是自动生成，自定义通过copy bin文件到Amiibo文件夹内";
            // 
            // amiiboView
            // 
            amiiboView.Location = new Point(336, 26);
            amiiboView.Margin = new Padding(4, 4, 4, 4);
            amiiboView.Name = "amiiboView";
            amiiboView.Size = new Size(342, 386);
            amiiboView.SizeMode = PictureBoxSizeMode.StretchImage;
            amiiboView.TabIndex = 8;
            amiiboView.TabStop = false;
            // 
            // changeAmiiboButton
            // 
            changeAmiiboButton.Location = new Point(176, 344);
            changeAmiiboButton.Margin = new Padding(4, 4, 4, 4);
            changeAmiiboButton.Name = "changeAmiiboButton";
            changeAmiiboButton.Size = new Size(131, 27);
            changeAmiiboButton.TabIndex = 7;
            changeAmiiboButton.Text = "激活";
            changeAmiiboButton.UseVisualStyleBackColor = true;
            changeAmiiboButton.Click += changeAmiibo_Click;
            // 
            // saveAmiiboButton
            // 
            saveAmiiboButton.Location = new Point(24, 222);
            saveAmiiboButton.Margin = new Padding(4, 4, 4, 4);
            saveAmiiboButton.Name = "saveAmiiboButton";
            saveAmiiboButton.Size = new Size(283, 27);
            saveAmiiboButton.TabIndex = 4;
            saveAmiiboButton.Text = "保存Amiibo";
            saveAmiiboButton.UseVisualStyleBackColor = true;
            saveAmiiboButton.Click += saveAmiibo_Click;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(24, 120);
            label6.Margin = new Padding(4, 0, 4, 0);
            label6.Name = "label6";
            label6.Size = new Size(69, 20);
            label6.TabIndex = 3;
            label6.Text = "存储内容";
            // 
            // selectAmiiboBox
            // 
            selectAmiiboBox.FormattingEnabled = true;
            selectAmiiboBox.Location = new Point(104, 114);
            selectAmiiboBox.Margin = new Padding(4, 4, 4, 4);
            selectAmiiboBox.Name = "selectAmiiboBox";
            selectAmiiboBox.Size = new Size(202, 28);
            selectAmiiboBox.TabIndex = 2;
            selectAmiiboBox.Text = "选择Amiibo";
            selectAmiiboBox.DropDown += amiibo_DropDown;
            selectAmiiboBox.SelectionChangeCommitted += amiibo_SelectionChangeCommitted;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(24, 44);
            label1.Margin = new Padding(4, 0, 4, 0);
            label1.Name = "label1";
            label1.Size = new Size(69, 20);
            label1.TabIndex = 1;
            label1.Text = "存储序号";
            // 
            // saveIndexBox
            // 
            saveIndexBox.FormattingEnabled = true;
            saveIndexBox.Items.AddRange(new object[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15", "16", "17", "18", "19" });
            saveIndexBox.Location = new Point(104, 40);
            saveIndexBox.Margin = new Padding(4, 4, 4, 4);
            saveIndexBox.Name = "saveIndexBox";
            saveIndexBox.Size = new Size(202, 28);
            saveIndexBox.TabIndex = 0;
            saveIndexBox.Text = "选择序号";
            // 
            // ESPConfig
            // 
            AutoScaleDimensions = new SizeF(9F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(715, 581);
            Controls.Add(groupBox3);
            Controls.Add(groupBox2);
            Controls.Add(groupBox1);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Icon = (Icon)resources.GetObject("$this.Icon");
            Margin = new Padding(4, 4, 4, 4);
            Name = "ESPConfig";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "手柄设置";
            Load += ControllerConfig_Load;
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            groupBox3.ResumeLayout(false);
            groupBox3.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)amiiboIndexNum).EndInit();
            ((System.ComponentModel.ISupportInitialize)amiiboView).EndInit();
            ResumeLayout(false);

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