namespace PokemonTycoon
{
    partial class Form1
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
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPageScripting = new System.Windows.Forms.TabPage();
            this.richTextBoxScriptingMessage = new System.Windows.Forms.RichTextBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.labelTestLight = new System.Windows.Forms.Label();
            this.buttonScriptTest = new System.Windows.Forms.Button();
            this.tabPageSampling = new System.Windows.Forms.TabPage();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.buttonGraphicSaveShadow = new System.Windows.Forms.Button();
            this.textBoxGraphicCap = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.checkBoxGraphicSampling = new System.Windows.Forms.CheckBox();
            this.buttonGraphicGetFiles = new System.Windows.Forms.Button();
            this.buttonGraphicOpenImage = new System.Windows.Forms.Button();
            this.comboBoxGraphicFilename = new System.Windows.Forms.ComboBox();
            this.buttonGraphicSaveImage = new System.Windows.Forms.Button();
            this.label8 = new System.Windows.Forms.Label();
            this.textBoxGraphicCode = new System.Windows.Forms.TextBox();
            this.textBoxGraphicSearchResult = new System.Windows.Forms.TextBox();
            this.buttonGraphicRelease = new System.Windows.Forms.Button();
            this.buttonGraphicCapture = new System.Windows.Forms.Button();
            this.buttonGraphicGetIImage = new System.Windows.Forms.Button();
            this.buttonGraphicGetColor = new System.Windows.Forms.Button();
            this.buttonGraphicSearchImage = new System.Windows.Forms.Button();
            this.buttonGraphicSearchColor = new System.Windows.Forms.Button();
            this.textBoxGraphicWebColor = new System.Windows.Forms.TextBox();
            this.label7 = new System.Windows.Forms.Label();
            this.textBoxGraphicColorB = new System.Windows.Forms.TextBox();
            this.textBoxGraphicColorG = new System.Windows.Forms.TextBox();
            this.textBoxGraphicColorR = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.textBoxGraphicHeight = new System.Windows.Forms.TextBox();
            this.label5 = new System.Windows.Forms.Label();
            this.textBoxGraphicWidth = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.textBoxGraphicY = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.textBoxGraphicX = new System.Windows.Forms.TextBox();
            this.pictureBoxGraphic = new System.Windows.Forms.PictureBox();
            this.tabPageSettings = new System.Windows.Forms.TabPage();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.buttonSettingsScreenPreview = new System.Windows.Forms.Button();
            this.buttonSettingsScreen = new System.Windows.Forms.Button();
            this.pictureBoxSettingsScreenPreview = new System.Windows.Forms.PictureBox();
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.系统ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.richTextBoxScriptingSummary = new System.Windows.Forms.RichTextBox();
            this.tabControl1.SuspendLayout();
            this.tabPageScripting.SuspendLayout();
            this.groupBox1.SuspendLayout();
            this.tabPageSampling.SuspendLayout();
            this.groupBox2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxGraphic)).BeginInit();
            this.tabPageSettings.SuspendLayout();
            this.groupBox3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxSettingsScreenPreview)).BeginInit();
            this.menuStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // tabControl1
            // 
            this.tabControl1.Controls.Add(this.tabPageScripting);
            this.tabControl1.Controls.Add(this.tabPageSampling);
            this.tabControl1.Controls.Add(this.tabPageSettings);
            this.tabControl1.ItemSize = new System.Drawing.Size(100, 24);
            this.tabControl1.Location = new System.Drawing.Point(0, 30);
            this.tabControl1.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.Padding = new System.Drawing.Point(3, 3);
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(1384, 730);
            this.tabControl1.SizeMode = System.Windows.Forms.TabSizeMode.Fixed;
            this.tabControl1.TabIndex = 0;
            this.tabControl1.SelectedIndexChanged += new System.EventHandler(this.tabControl1_SelectedIndexChanged);
            // 
            // tabPageScripting
            // 
            this.tabPageScripting.BackColor = System.Drawing.SystemColors.Control;
            this.tabPageScripting.Controls.Add(this.richTextBoxScriptingSummary);
            this.tabPageScripting.Controls.Add(this.richTextBoxScriptingMessage);
            this.tabPageScripting.Controls.Add(this.groupBox1);
            this.tabPageScripting.Location = new System.Drawing.Point(4, 28);
            this.tabPageScripting.Name = "tabPageScripting";
            this.tabPageScripting.Size = new System.Drawing.Size(1376, 698);
            this.tabPageScripting.TabIndex = 0;
            this.tabPageScripting.Text = "执行";
            // 
            // richTextBoxScriptingMessage
            // 
            this.richTextBoxScriptingMessage.BackColor = System.Drawing.Color.Black;
            this.richTextBoxScriptingMessage.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.richTextBoxScriptingMessage.ForeColor = System.Drawing.Color.White;
            this.richTextBoxScriptingMessage.Location = new System.Drawing.Point(888, 3);
            this.richTextBoxScriptingMessage.Name = "richTextBoxScriptingMessage";
            this.richTextBoxScriptingMessage.ReadOnly = true;
            this.richTextBoxScriptingMessage.Size = new System.Drawing.Size(485, 604);
            this.richTextBoxScriptingMessage.TabIndex = 26;
            this.richTextBoxScriptingMessage.Text = "";
            this.richTextBoxScriptingMessage.WordWrap = false;
            this.richTextBoxScriptingMessage.KeyDown += new System.Windows.Forms.KeyEventHandler(this.richTextBoxScriptingMessage_KeyDown);
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.labelTestLight);
            this.groupBox1.Controls.Add(this.buttonScriptTest);
            this.groupBox1.Location = new System.Drawing.Point(6, 7);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(185, 68);
            this.groupBox1.TabIndex = 0;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "调试";
            // 
            // labelTestLight
            // 
            this.labelTestLight.BackColor = System.Drawing.Color.White;
            this.labelTestLight.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.labelTestLight.Location = new System.Drawing.Point(98, 21);
            this.labelTestLight.Name = "labelTestLight";
            this.labelTestLight.Size = new System.Drawing.Size(80, 40);
            this.labelTestLight.TabIndex = 27;
            this.labelTestLight.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // buttonScriptTest
            // 
            this.buttonScriptTest.Location = new System.Drawing.Point(6, 20);
            this.buttonScriptTest.Name = "buttonScriptTest";
            this.buttonScriptTest.Size = new System.Drawing.Size(85, 42);
            this.buttonScriptTest.TabIndex = 27;
            this.buttonScriptTest.Text = "Test";
            this.buttonScriptTest.UseVisualStyleBackColor = true;
            this.buttonScriptTest.Click += new System.EventHandler(this.buttonScriptTest_Click);
            // 
            // tabPageSampling
            // 
            this.tabPageSampling.BackColor = System.Drawing.SystemColors.Control;
            this.tabPageSampling.Controls.Add(this.groupBox2);
            this.tabPageSampling.Location = new System.Drawing.Point(4, 28);
            this.tabPageSampling.Name = "tabPageSampling";
            this.tabPageSampling.Size = new System.Drawing.Size(1376, 698);
            this.tabPageSampling.TabIndex = 1;
            this.tabPageSampling.Text = "采样";
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.buttonGraphicSaveShadow);
            this.groupBox2.Controls.Add(this.textBoxGraphicCap);
            this.groupBox2.Controls.Add(this.label1);
            this.groupBox2.Controls.Add(this.checkBoxGraphicSampling);
            this.groupBox2.Controls.Add(this.buttonGraphicGetFiles);
            this.groupBox2.Controls.Add(this.buttonGraphicOpenImage);
            this.groupBox2.Controls.Add(this.comboBoxGraphicFilename);
            this.groupBox2.Controls.Add(this.buttonGraphicSaveImage);
            this.groupBox2.Controls.Add(this.label8);
            this.groupBox2.Controls.Add(this.textBoxGraphicCode);
            this.groupBox2.Controls.Add(this.textBoxGraphicSearchResult);
            this.groupBox2.Controls.Add(this.buttonGraphicRelease);
            this.groupBox2.Controls.Add(this.buttonGraphicCapture);
            this.groupBox2.Controls.Add(this.buttonGraphicGetIImage);
            this.groupBox2.Controls.Add(this.buttonGraphicGetColor);
            this.groupBox2.Controls.Add(this.buttonGraphicSearchImage);
            this.groupBox2.Controls.Add(this.buttonGraphicSearchColor);
            this.groupBox2.Controls.Add(this.textBoxGraphicWebColor);
            this.groupBox2.Controls.Add(this.label7);
            this.groupBox2.Controls.Add(this.textBoxGraphicColorB);
            this.groupBox2.Controls.Add(this.textBoxGraphicColorG);
            this.groupBox2.Controls.Add(this.textBoxGraphicColorR);
            this.groupBox2.Controls.Add(this.label6);
            this.groupBox2.Controls.Add(this.label4);
            this.groupBox2.Controls.Add(this.textBoxGraphicHeight);
            this.groupBox2.Controls.Add(this.label5);
            this.groupBox2.Controls.Add(this.textBoxGraphicWidth);
            this.groupBox2.Controls.Add(this.label3);
            this.groupBox2.Controls.Add(this.textBoxGraphicY);
            this.groupBox2.Controls.Add(this.label2);
            this.groupBox2.Controls.Add(this.textBoxGraphicX);
            this.groupBox2.Controls.Add(this.pictureBoxGraphic);
            this.groupBox2.Location = new System.Drawing.Point(8, 7);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(535, 683);
            this.groupBox2.TabIndex = 1;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "图像";
            // 
            // buttonGraphicSaveShadow
            // 
            this.buttonGraphicSaveShadow.Location = new System.Drawing.Point(126, 191);
            this.buttonGraphicSaveShadow.Name = "buttonGraphicSaveShadow";
            this.buttonGraphicSaveShadow.Size = new System.Drawing.Size(40, 23);
            this.buttonGraphicSaveShadow.TabIndex = 34;
            this.buttonGraphicSaveShadow.Text = "剪影";
            this.buttonGraphicSaveShadow.UseVisualStyleBackColor = true;
            this.buttonGraphicSaveShadow.Click += new System.EventHandler(this.buttonGraphicSaveShadow_Click);
            // 
            // textBoxGraphicCap
            // 
            this.textBoxGraphicCap.BackColor = System.Drawing.Color.White;
            this.textBoxGraphicCap.Location = new System.Drawing.Point(203, 192);
            this.textBoxGraphicCap.Name = "textBoxGraphicCap";
            this.textBoxGraphicCap.Size = new System.Drawing.Size(23, 21);
            this.textBoxGraphicCap.TabIndex = 33;
            this.textBoxGraphicCap.Text = "1";
            this.textBoxGraphicCap.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // label1
            // 
            this.label1.Location = new System.Drawing.Point(172, 193);
            this.label1.Margin = new System.Windows.Forms.Padding(0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(29, 18);
            this.label1.TabIndex = 32;
            this.label1.Text = "cap";
            this.label1.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // checkBoxGraphicSampling
            // 
            this.checkBoxGraphicSampling.Appearance = System.Windows.Forms.Appearance.Button;
            this.checkBoxGraphicSampling.Location = new System.Drawing.Point(9, 24);
            this.checkBoxGraphicSampling.Name = "checkBoxGraphicSampling";
            this.checkBoxGraphicSampling.Size = new System.Drawing.Size(217, 26);
            this.checkBoxGraphicSampling.TabIndex = 31;
            this.checkBoxGraphicSampling.Text = "按Alt取色，按住Ctrl截取区域";
            this.checkBoxGraphicSampling.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.checkBoxGraphicSampling.UseVisualStyleBackColor = true;
            // 
            // buttonGraphicGetFiles
            // 
            this.buttonGraphicGetFiles.Location = new System.Drawing.Point(3, 191);
            this.buttonGraphicGetFiles.Name = "buttonGraphicGetFiles";
            this.buttonGraphicGetFiles.Size = new System.Drawing.Size(40, 23);
            this.buttonGraphicGetFiles.TabIndex = 30;
            this.buttonGraphicGetFiles.Text = "刷新";
            this.buttonGraphicGetFiles.UseVisualStyleBackColor = true;
            this.buttonGraphicGetFiles.Click += new System.EventHandler(this.buttonGraphicGetFiles_Click);
            // 
            // buttonGraphicOpenImage
            // 
            this.buttonGraphicOpenImage.Location = new System.Drawing.Point(44, 191);
            this.buttonGraphicOpenImage.Name = "buttonGraphicOpenImage";
            this.buttonGraphicOpenImage.Size = new System.Drawing.Size(40, 23);
            this.buttonGraphicOpenImage.TabIndex = 29;
            this.buttonGraphicOpenImage.Text = "打开";
            this.buttonGraphicOpenImage.UseVisualStyleBackColor = true;
            this.buttonGraphicOpenImage.Click += new System.EventHandler(this.buttonGraphicOpenImage_Click);
            // 
            // comboBoxGraphicFilename
            // 
            this.comboBoxGraphicFilename.FormattingEnabled = true;
            this.comboBoxGraphicFilename.Location = new System.Drawing.Point(52, 163);
            this.comboBoxGraphicFilename.Name = "comboBoxGraphicFilename";
            this.comboBoxGraphicFilename.Size = new System.Drawing.Size(174, 24);
            this.comboBoxGraphicFilename.TabIndex = 28;
            // 
            // buttonGraphicSaveImage
            // 
            this.buttonGraphicSaveImage.Location = new System.Drawing.Point(85, 191);
            this.buttonGraphicSaveImage.Name = "buttonGraphicSaveImage";
            this.buttonGraphicSaveImage.Size = new System.Drawing.Size(40, 23);
            this.buttonGraphicSaveImage.TabIndex = 27;
            this.buttonGraphicSaveImage.Text = "保存";
            this.buttonGraphicSaveImage.UseVisualStyleBackColor = true;
            this.buttonGraphicSaveImage.Click += new System.EventHandler(this.buttonGraphicSaveImage_Click);
            // 
            // label8
            // 
            this.label8.Location = new System.Drawing.Point(6, 167);
            this.label8.Margin = new System.Windows.Forms.Padding(0);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(43, 18);
            this.label8.TabIndex = 25;
            this.label8.Text = "文件名";
            this.label8.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // textBoxGraphicCode
            // 
            this.textBoxGraphicCode.BackColor = System.Drawing.Color.WhiteSmoke;
            this.textBoxGraphicCode.Location = new System.Drawing.Point(163, 313);
            this.textBoxGraphicCode.Multiline = true;
            this.textBoxGraphicCode.Name = "textBoxGraphicCode";
            this.textBoxGraphicCode.ReadOnly = true;
            this.textBoxGraphicCode.Size = new System.Drawing.Size(369, 366);
            this.textBoxGraphicCode.TabIndex = 24;
            // 
            // textBoxGraphicSearchResult
            // 
            this.textBoxGraphicSearchResult.BackColor = System.Drawing.Color.WhiteSmoke;
            this.textBoxGraphicSearchResult.Location = new System.Drawing.Point(3, 313);
            this.textBoxGraphicSearchResult.Multiline = true;
            this.textBoxGraphicSearchResult.Name = "textBoxGraphicSearchResult";
            this.textBoxGraphicSearchResult.ReadOnly = true;
            this.textBoxGraphicSearchResult.Size = new System.Drawing.Size(157, 366);
            this.textBoxGraphicSearchResult.TabIndex = 23;
            // 
            // buttonGraphicRelease
            // 
            this.buttonGraphicRelease.Location = new System.Drawing.Point(117, 281);
            this.buttonGraphicRelease.Name = "buttonGraphicRelease";
            this.buttonGraphicRelease.Size = new System.Drawing.Size(113, 30);
            this.buttonGraphicRelease.TabIndex = 22;
            this.buttonGraphicRelease.Text = "释放缓冲";
            this.buttonGraphicRelease.UseVisualStyleBackColor = true;
            this.buttonGraphicRelease.Click += new System.EventHandler(this.buttonGraphicRelease_Click);
            // 
            // buttonGraphicCapture
            // 
            this.buttonGraphicCapture.Location = new System.Drawing.Point(3, 281);
            this.buttonGraphicCapture.Name = "buttonGraphicCapture";
            this.buttonGraphicCapture.Size = new System.Drawing.Size(113, 30);
            this.buttonGraphicCapture.TabIndex = 21;
            this.buttonGraphicCapture.Text = "启动缓冲";
            this.buttonGraphicCapture.UseVisualStyleBackColor = true;
            this.buttonGraphicCapture.Click += new System.EventHandler(this.buttonGraphicCapture_Click);
            // 
            // buttonGraphicGetIImage
            // 
            this.buttonGraphicGetIImage.Location = new System.Drawing.Point(117, 219);
            this.buttonGraphicGetIImage.Name = "buttonGraphicGetIImage";
            this.buttonGraphicGetIImage.Size = new System.Drawing.Size(113, 30);
            this.buttonGraphicGetIImage.TabIndex = 20;
            this.buttonGraphicGetIImage.Text = "按坐标提取图像";
            this.buttonGraphicGetIImage.UseVisualStyleBackColor = true;
            this.buttonGraphicGetIImage.Click += new System.EventHandler(this.buttonGraphicGetIImage_Click);
            // 
            // buttonGraphicGetColor
            // 
            this.buttonGraphicGetColor.Location = new System.Drawing.Point(3, 219);
            this.buttonGraphicGetColor.Name = "buttonGraphicGetColor";
            this.buttonGraphicGetColor.Size = new System.Drawing.Size(113, 30);
            this.buttonGraphicGetColor.TabIndex = 19;
            this.buttonGraphicGetColor.Text = "按坐标提取颜色";
            this.buttonGraphicGetColor.UseVisualStyleBackColor = true;
            this.buttonGraphicGetColor.Click += new System.EventHandler(this.buttonGraphicGetColor_Click);
            // 
            // buttonGraphicSearchImage
            // 
            this.buttonGraphicSearchImage.Location = new System.Drawing.Point(117, 250);
            this.buttonGraphicSearchImage.Name = "buttonGraphicSearchImage";
            this.buttonGraphicSearchImage.Size = new System.Drawing.Size(113, 30);
            this.buttonGraphicSearchImage.TabIndex = 18;
            this.buttonGraphicSearchImage.Text = "查找图像";
            this.buttonGraphicSearchImage.UseVisualStyleBackColor = true;
            this.buttonGraphicSearchImage.Click += new System.EventHandler(this.buttonGraphicSearchImage_Click);
            // 
            // buttonGraphicSearchColor
            // 
            this.buttonGraphicSearchColor.Location = new System.Drawing.Point(3, 250);
            this.buttonGraphicSearchColor.Name = "buttonGraphicSearchColor";
            this.buttonGraphicSearchColor.Size = new System.Drawing.Size(113, 30);
            this.buttonGraphicSearchColor.TabIndex = 17;
            this.buttonGraphicSearchColor.Text = "查找颜色";
            this.buttonGraphicSearchColor.UseVisualStyleBackColor = true;
            this.buttonGraphicSearchColor.Click += new System.EventHandler(this.buttonGraphicSearchColor_Click);
            // 
            // textBoxGraphicWebColor
            // 
            this.textBoxGraphicWebColor.BackColor = System.Drawing.Color.WhiteSmoke;
            this.textBoxGraphicWebColor.Location = new System.Drawing.Point(89, 135);
            this.textBoxGraphicWebColor.Name = "textBoxGraphicWebColor";
            this.textBoxGraphicWebColor.ReadOnly = true;
            this.textBoxGraphicWebColor.Size = new System.Drawing.Size(116, 21);
            this.textBoxGraphicWebColor.TabIndex = 16;
            this.textBoxGraphicWebColor.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // label7
            // 
            this.label7.Location = new System.Drawing.Point(19, 137);
            this.label7.Margin = new System.Windows.Forms.Padding(0);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(67, 18);
            this.label7.TabIndex = 15;
            this.label7.Text = "Web颜色";
            this.label7.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // textBoxGraphicColorB
            // 
            this.textBoxGraphicColorB.BackColor = System.Drawing.Color.White;
            this.textBoxGraphicColorB.Location = new System.Drawing.Point(167, 111);
            this.textBoxGraphicColorB.Name = "textBoxGraphicColorB";
            this.textBoxGraphicColorB.Size = new System.Drawing.Size(38, 21);
            this.textBoxGraphicColorB.TabIndex = 14;
            this.textBoxGraphicColorB.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // textBoxGraphicColorG
            // 
            this.textBoxGraphicColorG.BackColor = System.Drawing.Color.White;
            this.textBoxGraphicColorG.Location = new System.Drawing.Point(128, 111);
            this.textBoxGraphicColorG.Name = "textBoxGraphicColorG";
            this.textBoxGraphicColorG.Size = new System.Drawing.Size(38, 21);
            this.textBoxGraphicColorG.TabIndex = 13;
            this.textBoxGraphicColorG.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // textBoxGraphicColorR
            // 
            this.textBoxGraphicColorR.BackColor = System.Drawing.Color.White;
            this.textBoxGraphicColorR.Location = new System.Drawing.Point(89, 111);
            this.textBoxGraphicColorR.Name = "textBoxGraphicColorR";
            this.textBoxGraphicColorR.Size = new System.Drawing.Size(38, 21);
            this.textBoxGraphicColorR.TabIndex = 12;
            this.textBoxGraphicColorR.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // label6
            // 
            this.label6.Location = new System.Drawing.Point(19, 113);
            this.label6.Margin = new System.Windows.Forms.Padding(0);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(67, 18);
            this.label6.TabIndex = 11;
            this.label6.Text = "颜色(RGB)";
            this.label6.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // label4
            // 
            this.label4.Location = new System.Drawing.Point(111, 83);
            this.label4.Margin = new System.Windows.Forms.Padding(0);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(55, 18);
            this.label4.TabIndex = 10;
            this.label4.Text = "Height =";
            this.label4.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // textBoxGraphicHeight
            // 
            this.textBoxGraphicHeight.BackColor = System.Drawing.Color.White;
            this.textBoxGraphicHeight.Location = new System.Drawing.Point(172, 81);
            this.textBoxGraphicHeight.Name = "textBoxGraphicHeight";
            this.textBoxGraphicHeight.Size = new System.Drawing.Size(38, 21);
            this.textBoxGraphicHeight.TabIndex = 9;
            this.textBoxGraphicHeight.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // label5
            // 
            this.label5.Location = new System.Drawing.Point(6, 84);
            this.label5.Margin = new System.Windows.Forms.Padding(0);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(55, 18);
            this.label5.TabIndex = 8;
            this.label5.Text = "Width =";
            this.label5.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // textBoxGraphicWidth
            // 
            this.textBoxGraphicWidth.BackColor = System.Drawing.Color.White;
            this.textBoxGraphicWidth.Location = new System.Drawing.Point(67, 81);
            this.textBoxGraphicWidth.Name = "textBoxGraphicWidth";
            this.textBoxGraphicWidth.Size = new System.Drawing.Size(38, 21);
            this.textBoxGraphicWidth.TabIndex = 7;
            this.textBoxGraphicWidth.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // label3
            // 
            this.label3.Location = new System.Drawing.Point(111, 58);
            this.label3.Margin = new System.Windows.Forms.Padding(0);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(55, 18);
            this.label3.TabIndex = 6;
            this.label3.Text = "Y =";
            this.label3.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // textBoxGraphicY
            // 
            this.textBoxGraphicY.BackColor = System.Drawing.Color.White;
            this.textBoxGraphicY.Location = new System.Drawing.Point(172, 56);
            this.textBoxGraphicY.Name = "textBoxGraphicY";
            this.textBoxGraphicY.Size = new System.Drawing.Size(38, 21);
            this.textBoxGraphicY.TabIndex = 5;
            this.textBoxGraphicY.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // label2
            // 
            this.label2.Location = new System.Drawing.Point(6, 59);
            this.label2.Margin = new System.Windows.Forms.Padding(0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(55, 18);
            this.label2.TabIndex = 4;
            this.label2.Text = "X =";
            this.label2.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // textBoxGraphicX
            // 
            this.textBoxGraphicX.BackColor = System.Drawing.Color.White;
            this.textBoxGraphicX.Location = new System.Drawing.Point(67, 56);
            this.textBoxGraphicX.Name = "textBoxGraphicX";
            this.textBoxGraphicX.Size = new System.Drawing.Size(38, 21);
            this.textBoxGraphicX.TabIndex = 3;
            this.textBoxGraphicX.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // pictureBoxGraphic
            // 
            this.pictureBoxGraphic.BackColor = System.Drawing.Color.White;
            this.pictureBoxGraphic.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pictureBoxGraphic.Location = new System.Drawing.Point(232, 10);
            this.pictureBoxGraphic.Name = "pictureBoxGraphic";
            this.pictureBoxGraphic.Size = new System.Drawing.Size(300, 300);
            this.pictureBoxGraphic.TabIndex = 1;
            this.pictureBoxGraphic.TabStop = false;
            // 
            // tabPageSettings
            // 
            this.tabPageSettings.BackColor = System.Drawing.SystemColors.Control;
            this.tabPageSettings.Controls.Add(this.groupBox3);
            this.tabPageSettings.Location = new System.Drawing.Point(4, 28);
            this.tabPageSettings.Name = "tabPageSettings";
            this.tabPageSettings.Size = new System.Drawing.Size(1376, 698);
            this.tabPageSettings.TabIndex = 2;
            this.tabPageSettings.Text = "设置";
            // 
            // groupBox3
            // 
            this.groupBox3.Controls.Add(this.buttonSettingsScreenPreview);
            this.groupBox3.Controls.Add(this.buttonSettingsScreen);
            this.groupBox3.Controls.Add(this.pictureBoxSettingsScreenPreview);
            this.groupBox3.Location = new System.Drawing.Point(8, 3);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(204, 164);
            this.groupBox3.TabIndex = 0;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "屏幕";
            // 
            // buttonSettingsScreenPreview
            // 
            this.buttonSettingsScreenPreview.Location = new System.Drawing.Point(133, 134);
            this.buttonSettingsScreenPreview.Name = "buttonSettingsScreenPreview";
            this.buttonSettingsScreenPreview.Size = new System.Drawing.Size(65, 23);
            this.buttonSettingsScreenPreview.TabIndex = 2;
            this.buttonSettingsScreenPreview.Text = "预览";
            this.buttonSettingsScreenPreview.UseVisualStyleBackColor = true;
            this.buttonSettingsScreenPreview.Click += new System.EventHandler(this.buttonSettingsScreenPreview_Click);
            // 
            // buttonSettingsScreen
            // 
            this.buttonSettingsScreen.Location = new System.Drawing.Point(6, 134);
            this.buttonSettingsScreen.Name = "buttonSettingsScreen";
            this.buttonSettingsScreen.Size = new System.Drawing.Size(121, 23);
            this.buttonSettingsScreen.TabIndex = 1;
            this.buttonSettingsScreen.UseVisualStyleBackColor = true;
            this.buttonSettingsScreen.Click += new System.EventHandler(this.buttonSettingsScreen_Click);
            // 
            // pictureBoxSettingsScreenPreview
            // 
            this.pictureBoxSettingsScreenPreview.Location = new System.Drawing.Point(6, 20);
            this.pictureBoxSettingsScreenPreview.Name = "pictureBoxSettingsScreenPreview";
            this.pictureBoxSettingsScreenPreview.Size = new System.Drawing.Size(192, 108);
            this.pictureBoxSettingsScreenPreview.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureBoxSettingsScreenPreview.TabIndex = 1;
            this.pictureBoxSettingsScreenPreview.TabStop = false;
            // 
            // menuStrip1
            // 
            this.menuStrip1.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.系统ToolStripMenuItem});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Padding = new System.Windows.Forms.Padding(7, 2, 0, 2);
            this.menuStrip1.Size = new System.Drawing.Size(1384, 25);
            this.menuStrip1.TabIndex = 1;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // 系统ToolStripMenuItem
            // 
            this.系统ToolStripMenuItem.Name = "系统ToolStripMenuItem";
            this.系统ToolStripMenuItem.Size = new System.Drawing.Size(44, 21);
            this.系统ToolStripMenuItem.Text = "系统";
            // 
            // richTextBoxScriptingSummary
            // 
            this.richTextBoxScriptingSummary.BackColor = System.Drawing.Color.Black;
            this.richTextBoxScriptingSummary.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.richTextBoxScriptingSummary.ForeColor = System.Drawing.Color.White;
            this.richTextBoxScriptingSummary.Location = new System.Drawing.Point(888, 608);
            this.richTextBoxScriptingSummary.Name = "richTextBoxScriptingSummary";
            this.richTextBoxScriptingSummary.ReadOnly = true;
            this.richTextBoxScriptingSummary.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.None;
            this.richTextBoxScriptingSummary.Size = new System.Drawing.Size(485, 87);
            this.richTextBoxScriptingSummary.TabIndex = 27;
            this.richTextBoxScriptingSummary.Text = "";
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1384, 761);
            this.Controls.Add(this.tabControl1);
            this.Controls.Add(this.menuStrip1);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 8F);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.KeyPreview = true;
            this.MainMenuStrip = this.menuStrip1;
            this.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.MaximizeBox = false;
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "宝可小管家";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.Form1_KeyDown);
            this.KeyUp += new System.Windows.Forms.KeyEventHandler(this.Form1_KeyUp);
            this.tabControl1.ResumeLayout(false);
            this.tabPageScripting.ResumeLayout(false);
            this.groupBox1.ResumeLayout(false);
            this.tabPageSampling.ResumeLayout(false);
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxGraphic)).EndInit();
            this.tabPageSettings.ResumeLayout(false);
            this.groupBox3.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxSettingsScreenPreview)).EndInit();
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPageScripting;
        private System.Windows.Forms.TabPage tabPageSampling;
        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem 系统ToolStripMenuItem;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.PictureBox pictureBoxGraphic;
        private System.Windows.Forms.TextBox textBoxGraphicX;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox textBoxGraphicY;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox textBoxGraphicHeight;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.TextBox textBoxGraphicWidth;
        private System.Windows.Forms.TextBox textBoxGraphicWebColor;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.TextBox textBoxGraphicColorB;
        private System.Windows.Forms.TextBox textBoxGraphicColorG;
        private System.Windows.Forms.TextBox textBoxGraphicColorR;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Button buttonGraphicRelease;
        private System.Windows.Forms.Button buttonGraphicCapture;
        private System.Windows.Forms.Button buttonGraphicGetIImage;
        private System.Windows.Forms.Button buttonGraphicGetColor;
        private System.Windows.Forms.Button buttonGraphicSearchImage;
        private System.Windows.Forms.Button buttonGraphicSearchColor;
        private System.Windows.Forms.TextBox textBoxGraphicSearchResult;
        private System.Windows.Forms.TextBox textBoxGraphicCode;
        private System.Windows.Forms.Button buttonGraphicSaveImage;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Button buttonGraphicGetFiles;
        private System.Windows.Forms.Button buttonGraphicOpenImage;
        private System.Windows.Forms.ComboBox comboBoxGraphicFilename;
        private System.Windows.Forms.CheckBox checkBoxGraphicSampling;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.RichTextBox richTextBoxScriptingMessage;
        private System.Windows.Forms.Button buttonScriptTest;
        private System.Windows.Forms.Label labelTestLight;
        private System.Windows.Forms.TextBox textBoxGraphicCap;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TabPage tabPageSettings;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.Button buttonSettingsScreenPreview;
        private System.Windows.Forms.Button buttonSettingsScreen;
        private System.Windows.Forms.PictureBox pictureBoxSettingsScreenPreview;
        private System.Windows.Forms.Button buttonGraphicSaveShadow;
        private System.Windows.Forms.RichTextBox richTextBoxScriptingSummary;
    }
}

