namespace EasyCon2.Forms
{
    partial class CaptureVideoForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(CaptureVideoForm));
            this.reasultListBox = new System.Windows.Forms.ListBox();
            this.label1 = new System.Windows.Forms.Label();
            this.captureBtn = new System.Windows.Forms.Button();
            this.label4 = new System.Windows.Forms.Label();
            this.rangeBtn = new System.Windows.Forms.Button();
            this.searchTestBtn = new System.Windows.Forms.Button();
            this.targetBtn = new System.Windows.Forms.Button();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.searchHNUD = new System.Windows.Forms.NumericUpDown();
            this.searchWNUD = new System.Windows.Forms.NumericUpDown();
            this.searchYNUD = new System.Windows.Forms.NumericUpDown();
            this.searchXNUD = new System.Windows.Forms.NumericUpDown();
            this.targetHNUD = new System.Windows.Forms.NumericUpDown();
            this.targetWNUD = new System.Windows.Forms.NumericUpDown();
            this.targetYNUD = new System.Windows.Forms.NumericUpDown();
            this.searchResultImg = new System.Windows.Forms.PictureBox();
            this.targetXNUD = new System.Windows.Forms.NumericUpDown();
            this.targetImg = new System.Windows.Forms.PictureBox();
            this.imgLabelNametxt = new System.Windows.Forms.TextBox();
            this.label8 = new System.Windows.Forms.Label();
            this.label23 = new System.Windows.Forms.Label();
            this.lowestMatch = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.label22 = new System.Windows.Forms.Label();
            this.label21 = new System.Windows.Forms.Label();
            this.label16 = new System.Windows.Forms.Label();
            this.label17 = new System.Windows.Forms.Label();
            this.label19 = new System.Windows.Forms.Label();
            this.label20 = new System.Windows.Forms.Label();
            this.label14 = new System.Windows.Forms.Label();
            this.label15 = new System.Windows.Forms.Label();
            this.label13 = new System.Windows.Forms.Label();
            this.label18 = new System.Windows.Forms.Label();
            this.label12 = new System.Windows.Forms.Label();
            this.searchMethodComBox = new System.Windows.Forms.ComboBox();
            this.imgLableList = new System.Windows.Forms.ListBox();
            this.SaveTagBtn = new System.Windows.Forms.Button();
            this.DynTestBtn = new System.Windows.Forms.Button();
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.CaptureVideoHelp = new System.Windows.Forms.TextBox();
            this.openCapBtn = new System.Windows.Forms.Button();
            this.ResolutionBtn = new System.Windows.Forms.Button();
            this.label2 = new System.Windows.Forms.Label();
            this.monitorVisChk = new System.Windows.Forms.CheckBox();
            this.VideoSourcePlayerMonitor = new EasyCon2.Forms.PaintControl();
            this.Snapshot = new EasyCon2.Forms.PaintControl();
            this.openFileDialog1 = new System.Windows.Forms.OpenFileDialog();
            this.groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.searchHNUD)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.searchWNUD)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.searchYNUD)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.searchXNUD)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.targetHNUD)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.targetWNUD)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.targetYNUD)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.searchResultImg)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.targetXNUD)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.targetImg)).BeginInit();
            this.SuspendLayout();
            // 
            // reasultListBox
            // 
            this.reasultListBox.FormattingEnabled = true;
            this.reasultListBox.ItemHeight = 17;
            this.reasultListBox.Location = new System.Drawing.Point(405, 22);
            this.reasultListBox.Margin = new System.Windows.Forms.Padding(4);
            this.reasultListBox.Name = "reasultListBox";
            this.reasultListBox.Size = new System.Drawing.Size(119, 55);
            this.reasultListBox.TabIndex = 1;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(756, 391);
            this.label1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(109, 17);
            this.label1.TabIndex = 2;
            this.label1.Text = "搜图标签-双击加载";
            // 
            // captureBtn
            // 
            this.captureBtn.Location = new System.Drawing.Point(35, 487);
            this.captureBtn.Margin = new System.Windows.Forms.Padding(4);
            this.captureBtn.Name = "captureBtn";
            this.captureBtn.Size = new System.Drawing.Size(77, 33);
            this.captureBtn.TabIndex = 5;
            this.captureBtn.Text = "截图";
            this.captureBtn.UseVisualStyleBackColor = true;
            this.captureBtn.Click += new System.EventHandler(this.captureBtn_Click);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(756, 364);
            this.label4.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(421, 17);
            this.label4.TabIndex = 7;
            this.label4.Text = "双击切换放大/编辑模式，滚轮缩放，ctrl+滚轮水平缩放，shift+滚轮垂直缩放";
            // 
            // rangeBtn
            // 
            this.rangeBtn.Location = new System.Drawing.Point(119, 487);
            this.rangeBtn.Margin = new System.Windows.Forms.Padding(4);
            this.rangeBtn.Name = "rangeBtn";
            this.rangeBtn.Size = new System.Drawing.Size(100, 33);
            this.rangeBtn.TabIndex = 8;
            this.rangeBtn.Text = "开始圈选(红)";
            this.rangeBtn.UseVisualStyleBackColor = true;
            this.rangeBtn.Click += new System.EventHandler(this.rangeBtn_Click);
            // 
            // searchTestBtn
            // 
            this.searchTestBtn.Location = new System.Drawing.Point(334, 487);
            this.searchTestBtn.Margin = new System.Windows.Forms.Padding(4);
            this.searchTestBtn.Name = "searchTestBtn";
            this.searchTestBtn.Size = new System.Drawing.Size(86, 33);
            this.searchTestBtn.TabIndex = 9;
            this.searchTestBtn.Text = "搜索测试";
            this.searchTestBtn.UseVisualStyleBackColor = true;
            this.searchTestBtn.Click += new System.EventHandler(this.searchTestBtn_Click);
            // 
            // targetBtn
            // 
            this.targetBtn.Location = new System.Drawing.Point(227, 487);
            this.targetBtn.Margin = new System.Windows.Forms.Padding(4);
            this.targetBtn.Name = "targetBtn";
            this.targetBtn.Size = new System.Drawing.Size(100, 33);
            this.targetBtn.TabIndex = 10;
            this.targetBtn.Text = "开始圈选(绿)";
            this.targetBtn.UseVisualStyleBackColor = true;
            this.targetBtn.Click += new System.EventHandler(this.targetBtn_Click);
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.searchHNUD);
            this.groupBox1.Controls.Add(this.searchWNUD);
            this.groupBox1.Controls.Add(this.searchYNUD);
            this.groupBox1.Controls.Add(this.searchXNUD);
            this.groupBox1.Controls.Add(this.targetHNUD);
            this.groupBox1.Controls.Add(this.targetWNUD);
            this.groupBox1.Controls.Add(this.targetYNUD);
            this.groupBox1.Controls.Add(this.searchResultImg);
            this.groupBox1.Controls.Add(this.targetXNUD);
            this.groupBox1.Controls.Add(this.targetImg);
            this.groupBox1.Controls.Add(this.imgLabelNametxt);
            this.groupBox1.Controls.Add(this.label8);
            this.groupBox1.Controls.Add(this.label23);
            this.groupBox1.Controls.Add(this.lowestMatch);
            this.groupBox1.Controls.Add(this.reasultListBox);
            this.groupBox1.Controls.Add(this.label6);
            this.groupBox1.Controls.Add(this.label22);
            this.groupBox1.Controls.Add(this.label21);
            this.groupBox1.Controls.Add(this.label16);
            this.groupBox1.Controls.Add(this.label17);
            this.groupBox1.Controls.Add(this.label19);
            this.groupBox1.Controls.Add(this.label20);
            this.groupBox1.Controls.Add(this.label14);
            this.groupBox1.Controls.Add(this.label15);
            this.groupBox1.Controls.Add(this.label13);
            this.groupBox1.Controls.Add(this.label18);
            this.groupBox1.Controls.Add(this.label12);
            this.groupBox1.Controls.Add(this.searchMethodComBox);
            this.groupBox1.Location = new System.Drawing.Point(35, 531);
            this.groupBox1.Margin = new System.Windows.Forms.Padding(4);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Padding = new System.Windows.Forms.Padding(4);
            this.groupBox1.Size = new System.Drawing.Size(669, 214);
            this.groupBox1.TabIndex = 18;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "搜索参数";
            // 
            // searchHNUD
            // 
            this.searchHNUD.Location = new System.Drawing.Point(341, 180);
            this.searchHNUD.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
            this.searchHNUD.Name = "searchHNUD";
            this.searchHNUD.Size = new System.Drawing.Size(57, 23);
            this.searchHNUD.TabIndex = 42;
            // 
            // searchWNUD
            // 
            this.searchWNUD.Location = new System.Drawing.Point(246, 179);
            this.searchWNUD.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
            this.searchWNUD.Name = "searchWNUD";
            this.searchWNUD.Size = new System.Drawing.Size(57, 23);
            this.searchWNUD.TabIndex = 41;
            // 
            // searchYNUD
            // 
            this.searchYNUD.Location = new System.Drawing.Point(341, 151);
            this.searchYNUD.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
            this.searchYNUD.Name = "searchYNUD";
            this.searchYNUD.Size = new System.Drawing.Size(57, 23);
            this.searchYNUD.TabIndex = 40;
            // 
            // searchXNUD
            // 
            this.searchXNUD.Location = new System.Drawing.Point(246, 150);
            this.searchXNUD.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
            this.searchXNUD.Name = "searchXNUD";
            this.searchXNUD.Size = new System.Drawing.Size(57, 23);
            this.searchXNUD.TabIndex = 39;
            // 
            // targetHNUD
            // 
            this.targetHNUD.Location = new System.Drawing.Point(138, 179);
            this.targetHNUD.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
            this.targetHNUD.Name = "targetHNUD";
            this.targetHNUD.Size = new System.Drawing.Size(57, 23);
            this.targetHNUD.TabIndex = 38;
            // 
            // targetWNUD
            // 
            this.targetWNUD.Location = new System.Drawing.Point(37, 179);
            this.targetWNUD.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
            this.targetWNUD.Name = "targetWNUD";
            this.targetWNUD.Size = new System.Drawing.Size(57, 23);
            this.targetWNUD.TabIndex = 37;
            // 
            // targetYNUD
            // 
            this.targetYNUD.Location = new System.Drawing.Point(138, 150);
            this.targetYNUD.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
            this.targetYNUD.Name = "targetYNUD";
            this.targetYNUD.Size = new System.Drawing.Size(57, 23);
            this.targetYNUD.TabIndex = 36;
            // 
            // searchResultImg
            // 
            this.searchResultImg.Location = new System.Drawing.Point(533, 89);
            this.searchResultImg.Margin = new System.Windows.Forms.Padding(4);
            this.searchResultImg.Name = "searchResultImg";
            this.searchResultImg.Size = new System.Drawing.Size(120, 120);
            this.searchResultImg.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.searchResultImg.TabIndex = 24;
            this.searchResultImg.TabStop = false;
            // 
            // targetXNUD
            // 
            this.targetXNUD.Location = new System.Drawing.Point(37, 150);
            this.targetXNUD.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
            this.targetXNUD.Name = "targetXNUD";
            this.targetXNUD.Size = new System.Drawing.Size(57, 23);
            this.targetXNUD.TabIndex = 35;
            // 
            // targetImg
            // 
            this.targetImg.Location = new System.Drawing.Point(405, 89);
            this.targetImg.Margin = new System.Windows.Forms.Padding(4);
            this.targetImg.Name = "targetImg";
            this.targetImg.Size = new System.Drawing.Size(120, 120);
            this.targetImg.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.targetImg.TabIndex = 34;
            this.targetImg.TabStop = false;
            this.targetImg.DoubleClick += new System.EventHandler(this.targetImg_DoubleClick);
            // 
            // imgLabelNametxt
            // 
            this.imgLabelNametxt.Location = new System.Drawing.Point(91, 23);
            this.imgLabelNametxt.Margin = new System.Windows.Forms.Padding(4);
            this.imgLabelNametxt.Name = "imgLabelNametxt";
            this.imgLabelNametxt.Size = new System.Drawing.Size(133, 23);
            this.imgLabelNametxt.TabIndex = 33;
            this.imgLabelNametxt.Text = "5号路蛋屋主人";
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(30, 25);
            this.label8.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(47, 17);
            this.label8.TabIndex = 32;
            this.label8.Text = "标签名:";
            // 
            // label23
            // 
            this.label23.Location = new System.Drawing.Point(533, 24);
            this.label23.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label23.Name = "label23";
            this.label23.Size = new System.Drawing.Size(120, 53);
            this.label23.TabIndex = 21;
            this.label23.Text = "匹配度：100%耗时：100毫秒最大匹配度100%";
            // 
            // lowestMatch
            // 
            this.lowestMatch.Location = new System.Drawing.Point(112, 86);
            this.lowestMatch.Margin = new System.Windows.Forms.Padding(4);
            this.lowestMatch.Name = "lowestMatch";
            this.lowestMatch.Size = new System.Drawing.Size(112, 23);
            this.lowestMatch.TabIndex = 31;
            this.lowestMatch.Text = "90.0";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(6, 89);
            this.label6.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(95, 17);
            this.label6.TabIndex = 30;
            this.label6.Text = "最低更新匹配度:";
            // 
            // label22
            // 
            this.label22.AutoSize = true;
            this.label22.Location = new System.Drawing.Point(216, 126);
            this.label22.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label22.Name = "label22";
            this.label22.Size = new System.Drawing.Size(59, 17);
            this.label22.TabIndex = 29;
            this.label22.Text = "搜索范围:";
            // 
            // label21
            // 
            this.label21.AutoSize = true;
            this.label21.Location = new System.Drawing.Point(11, 126);
            this.label21.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label21.Name = "label21";
            this.label21.Size = new System.Drawing.Size(59, 17);
            this.label21.TabIndex = 28;
            this.label21.Text = "目标位置:";
            // 
            // label16
            // 
            this.label16.AutoSize = true;
            this.label16.Location = new System.Drawing.Point(11, 153);
            this.label16.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label16.Name = "label16";
            this.label16.Size = new System.Drawing.Size(19, 17);
            this.label16.TabIndex = 26;
            this.label16.Text = "X:";
            // 
            // label17
            // 
            this.label17.AutoSize = true;
            this.label17.Location = new System.Drawing.Point(113, 153);
            this.label17.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label17.Name = "label17";
            this.label17.Size = new System.Drawing.Size(18, 17);
            this.label17.TabIndex = 24;
            this.label17.Text = "Y:";
            // 
            // label19
            // 
            this.label19.AutoSize = true;
            this.label19.Location = new System.Drawing.Point(7, 182);
            this.label19.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label19.Name = "label19";
            this.label19.Size = new System.Drawing.Size(23, 17);
            this.label19.TabIndex = 22;
            this.label19.Text = "宽:";
            // 
            // label20
            // 
            this.label20.AutoSize = true;
            this.label20.Location = new System.Drawing.Point(108, 182);
            this.label20.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label20.Name = "label20";
            this.label20.Size = new System.Drawing.Size(23, 17);
            this.label20.TabIndex = 20;
            this.label20.Text = "高:";
            // 
            // label14
            // 
            this.label14.AutoSize = true;
            this.label14.Location = new System.Drawing.Point(220, 153);
            this.label14.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label14.Name = "label14";
            this.label14.Size = new System.Drawing.Size(19, 17);
            this.label14.TabIndex = 18;
            this.label14.Text = "X:";
            // 
            // label15
            // 
            this.label15.AutoSize = true;
            this.label15.Location = new System.Drawing.Point(316, 154);
            this.label15.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label15.Name = "label15";
            this.label15.Size = new System.Drawing.Size(18, 17);
            this.label15.TabIndex = 16;
            this.label15.Text = "Y:";
            // 
            // label13
            // 
            this.label13.AutoSize = true;
            this.label13.Location = new System.Drawing.Point(216, 182);
            this.label13.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label13.Name = "label13";
            this.label13.Size = new System.Drawing.Size(23, 17);
            this.label13.TabIndex = 14;
            this.label13.Text = "宽:";
            // 
            // label18
            // 
            this.label18.AutoSize = true;
            this.label18.Location = new System.Drawing.Point(316, 183);
            this.label18.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label18.Name = "label18";
            this.label18.Size = new System.Drawing.Size(23, 17);
            this.label18.TabIndex = 12;
            this.label18.Text = "高:";
            // 
            // label12
            // 
            this.label12.AutoSize = true;
            this.label12.Location = new System.Drawing.Point(18, 58);
            this.label12.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label12.Name = "label12";
            this.label12.Size = new System.Drawing.Size(59, 17);
            this.label12.TabIndex = 1;
            this.label12.Text = "搜索方法:";
            // 
            // searchMethodComBox
            // 
            this.searchMethodComBox.FormattingEnabled = true;
            this.searchMethodComBox.Location = new System.Drawing.Point(91, 54);
            this.searchMethodComBox.Margin = new System.Windows.Forms.Padding(4);
            this.searchMethodComBox.Name = "searchMethodComBox";
            this.searchMethodComBox.Size = new System.Drawing.Size(133, 25);
            this.searchMethodComBox.TabIndex = 0;
            this.searchMethodComBox.Text = "选择搜索方法";
            // 
            // imgLableList
            // 
            this.imgLableList.FormattingEnabled = true;
            this.imgLableList.ItemHeight = 17;
            this.imgLableList.Location = new System.Drawing.Point(759, 418);
            this.imgLableList.Margin = new System.Windows.Forms.Padding(4);
            this.imgLableList.Name = "imgLableList";
            this.imgLableList.Size = new System.Drawing.Size(166, 327);
            this.imgLableList.TabIndex = 19;
            this.imgLableList.DoubleClick += new System.EventHandler(this.imgLableList_DoubleClick);
            // 
            // SaveTagBtn
            // 
            this.SaveTagBtn.Location = new System.Drawing.Point(540, 487);
            this.SaveTagBtn.Margin = new System.Windows.Forms.Padding(4);
            this.SaveTagBtn.Name = "SaveTagBtn";
            this.SaveTagBtn.Size = new System.Drawing.Size(79, 33);
            this.SaveTagBtn.TabIndex = 20;
            this.SaveTagBtn.Text = "保存标签";
            this.SaveTagBtn.UseVisualStyleBackColor = true;
            this.SaveTagBtn.Click += new System.EventHandler(this.SaveTagBtn_Click);
            // 
            // DynTestBtn
            // 
            this.DynTestBtn.Location = new System.Drawing.Point(427, 487);
            this.DynTestBtn.Margin = new System.Windows.Forms.Padding(4);
            this.DynTestBtn.Name = "DynTestBtn";
            this.DynTestBtn.Size = new System.Drawing.Size(112, 33);
            this.DynTestBtn.TabIndex = 22;
            this.DynTestBtn.Text = "动态测试";
            this.DynTestBtn.UseVisualStyleBackColor = true;
            this.DynTestBtn.Click += new System.EventHandler(this.DynTestBtn_Click);
            // 
            // timer1
            // 
            this.timer1.Tick += new System.EventHandler(this.timer1_Tick);
            // 
            // CaptureVideoHelp
            // 
            this.CaptureVideoHelp.Cursor = System.Windows.Forms.Cursors.Arrow;
            this.CaptureVideoHelp.Font = new System.Drawing.Font("宋体", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.CaptureVideoHelp.Location = new System.Drawing.Point(933, 418);
            this.CaptureVideoHelp.Margin = new System.Windows.Forms.Padding(4, 6, 4, 6);
            this.CaptureVideoHelp.Multiline = true;
            this.CaptureVideoHelp.Name = "CaptureVideoHelp";
            this.CaptureVideoHelp.ReadOnly = true;
            this.CaptureVideoHelp.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.CaptureVideoHelp.Size = new System.Drawing.Size(331, 327);
            this.CaptureVideoHelp.TabIndex = 25;
            // 
            // openCapBtn
            // 
            this.openCapBtn.Location = new System.Drawing.Point(627, 487);
            this.openCapBtn.Margin = new System.Windows.Forms.Padding(4);
            this.openCapBtn.Name = "openCapBtn";
            this.openCapBtn.Size = new System.Drawing.Size(77, 33);
            this.openCapBtn.TabIndex = 26;
            this.openCapBtn.Text = "打开截图";
            this.openCapBtn.UseVisualStyleBackColor = true;
            this.openCapBtn.Click += new System.EventHandler(this.openCapBtn_Click);
            // 
            // ResolutionBtn
            // 
            this.ResolutionBtn.Location = new System.Drawing.Point(755, 327);
            this.ResolutionBtn.Margin = new System.Windows.Forms.Padding(4);
            this.ResolutionBtn.Name = "ResolutionBtn";
            this.ResolutionBtn.Size = new System.Drawing.Size(171, 33);
            this.ResolutionBtn.TabIndex = 27;
            this.ResolutionBtn.Text = "当前分辨率：1080P点击切换";
            this.ResolutionBtn.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.ResolutionBtn.UseVisualStyleBackColor = true;
            this.ResolutionBtn.Click += new System.EventHandler(this.ResolutionBtn_Click);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(219, 459);
            this.label2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(224, 17);
            this.label2.TabIndex = 30;
            this.label2.Text = "左键按住移动，滚轮缩放，右键按住圈选";
            // 
            // monitorVisChk
            // 
            this.monitorVisChk.AutoSize = true;
            this.monitorVisChk.Checked = true;
            this.monitorVisChk.CheckState = System.Windows.Forms.CheckState.Checked;
            this.monitorVisChk.Location = new System.Drawing.Point(1187, 327);
            this.monitorVisChk.Margin = new System.Windows.Forms.Padding(4);
            this.monitorVisChk.Name = "monitorVisChk";
            this.monitorVisChk.Size = new System.Drawing.Size(87, 21);
            this.monitorVisChk.TabIndex = 32;
            this.monitorVisChk.Text = "监视器显示";
            this.monitorVisChk.UseVisualStyleBackColor = true;
            this.monitorVisChk.CheckedChanged += new System.EventHandler(this.monitorVisChk_CheckedChanged);
            // 
            // VideoSourcePlayerMonitor
            // 
            this.VideoSourcePlayerMonitor.BackColor = System.Drawing.SystemColors.ButtonShadow;
            this.VideoSourcePlayerMonitor.Location = new System.Drawing.Point(756, 1);
            this.VideoSourcePlayerMonitor.Margin = new System.Windows.Forms.Padding(4);
            this.VideoSourcePlayerMonitor.Name = "VideoSourcePlayerMonitor";
            this.VideoSourcePlayerMonitor.Size = new System.Drawing.Size(518, 318);
            this.VideoSourcePlayerMonitor.TabIndex = 33;
            this.VideoSourcePlayerMonitor.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.VideoSourcePlayerMonitor_MouseDoubleClick);
            this.VideoSourcePlayerMonitor.MouseDown += new System.Windows.Forms.MouseEventHandler(this.VideoSourcePlayerMonitor_MouseDown);
            this.VideoSourcePlayerMonitor.MouseMove += new System.Windows.Forms.MouseEventHandler(this.VideoSourcePlayerMonitor_MouseMove);
            this.VideoSourcePlayerMonitor.MouseUp += new System.Windows.Forms.MouseEventHandler(this.VideoSourcePlayerMonitor_MouseUp);
            this.VideoSourcePlayerMonitor.MouseWheel += new System.Windows.Forms.MouseEventHandler(this.VideoSourcePlayerMonitor_MouseWheel);
            this.VideoSourcePlayerMonitor.PreviewKeyDown += new System.Windows.Forms.PreviewKeyDownEventHandler(this.VideoSourcePlayerMonitor_PreviewKeyDown);
            // 
            // Snapshot
            // 
            this.Snapshot.BackColor = System.Drawing.SystemColors.ControlDark;
            this.Snapshot.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
            this.Snapshot.Location = new System.Drawing.Point(1, 1);
            this.Snapshot.Margin = new System.Windows.Forms.Padding(4);
            this.Snapshot.Name = "Snapshot";
            this.Snapshot.Size = new System.Drawing.Size(746, 448);
            this.Snapshot.TabIndex = 34;
            this.Snapshot.MouseDown += new System.Windows.Forms.MouseEventHandler(this.Snapshot_MouseDown);
            this.Snapshot.MouseMove += new System.Windows.Forms.MouseEventHandler(this.Snapshot_MouseMove);
            this.Snapshot.MouseUp += new System.Windows.Forms.MouseEventHandler(this.Snapshot_MouseUp);
            this.Snapshot.MouseWheel += new System.Windows.Forms.MouseEventHandler(this.Snapshot_MouseWheel);
            // 
            // openFileDialog1
            // 
            this.openFileDialog1.DefaultExt = "png";
            this.openFileDialog1.FileName = "target";
            this.openFileDialog1.Filter = "图片文件(*.jpg,*.gif,*.bmp,*.png)|*.jpg;*.gif;*.bmp;*.png";
            // 
            // CaptureVideoForm
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Inherit;
            this.ClientSize = new System.Drawing.Size(1279, 747);
            this.Controls.Add(this.Snapshot);
            this.Controls.Add(this.VideoSourcePlayerMonitor);
            this.Controls.Add(this.monitorVisChk);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.ResolutionBtn);
            this.Controls.Add(this.openCapBtn);
            this.Controls.Add(this.CaptureVideoHelp);
            this.Controls.Add(this.DynTestBtn);
            this.Controls.Add(this.SaveTagBtn);
            this.Controls.Add(this.imgLableList);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.targetBtn);
            this.Controls.Add(this.searchTestBtn);
            this.Controls.Add(this.rangeBtn);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.captureBtn);
            this.Controls.Add(this.label1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MaximizeBox = false;
            this.Name = "CaptureVideoForm";
            this.Padding = new System.Windows.Forms.Padding(1);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "搜图控制台";
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.CaptureVideo_FormClosed);
            this.Load += new System.EventHandler(this.CaptureVideo_Load);
            this.Resize += new System.EventHandler(this.CaptureVideoForm_Resize);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.searchHNUD)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.searchWNUD)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.searchYNUD)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.searchXNUD)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.targetHNUD)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.targetWNUD)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.targetYNUD)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.searchResultImg)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.targetXNUD)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.targetImg)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.ListBox reasultListBox;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button captureBtn;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Button rangeBtn;
        private System.Windows.Forms.Button searchTestBtn;
        private System.Windows.Forms.Button targetBtn;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.ListBox imgLableList;
        private System.Windows.Forms.Button SaveTagBtn;
        private System.Windows.Forms.Label label22;
        private System.Windows.Forms.Label label21;
        private System.Windows.Forms.Label label16;
        private System.Windows.Forms.Label label17;
        private System.Windows.Forms.Label label19;
        private System.Windows.Forms.Label label20;
        private System.Windows.Forms.Label label14;
        private System.Windows.Forms.Label label15;
        private System.Windows.Forms.Label label13;
        private System.Windows.Forms.Label label18;
        private System.Windows.Forms.Label label12;
        private System.Windows.Forms.ComboBox searchMethodComBox;
        private System.Windows.Forms.Label label23;
        private System.Windows.Forms.Button DynTestBtn;
        private System.Windows.Forms.TextBox lowestMatch;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.TextBox imgLabelNametxt;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Timer timer1;
        private System.Windows.Forms.PictureBox searchResultImg;
        private System.Windows.Forms.PictureBox targetImg;
        private System.Windows.Forms.TextBox CaptureVideoHelp;
        private System.Windows.Forms.Button openCapBtn;
        private System.Windows.Forms.Button ResolutionBtn;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.CheckBox monitorVisChk;
        private PaintControl VideoSourcePlayerMonitor;
        private PaintControl Snapshot;
        private NumericUpDown targetXNUD;
        private NumericUpDown targetHNUD;
        private NumericUpDown targetWNUD;
        private NumericUpDown targetYNUD;
        private NumericUpDown searchHNUD;
        private NumericUpDown searchWNUD;
        private NumericUpDown searchYNUD;
        private NumericUpDown searchXNUD;
        private OpenFileDialog openFileDialog1;
    }
}