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
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(CaptureVideoForm));
            reasultListBox = new ListBox();
            label1 = new Label();
            captureBtn = new Button();
            label4 = new Label();
            rangeBtn = new Button();
            searchTestBtn = new Button();
            targetBtn = new Button();
            groupBox1 = new GroupBox();
            searchHNUD = new NumericUpDown();
            searchWNUD = new NumericUpDown();
            searchYNUD = new NumericUpDown();
            searchXNUD = new NumericUpDown();
            targetHNUD = new NumericUpDown();
            targetWNUD = new NumericUpDown();
            targetYNUD = new NumericUpDown();
            searchResultImg = new PictureBox();
            targetXNUD = new NumericUpDown();
            targetImg = new PictureBox();
            imgLabelNametxt = new TextBox();
            label8 = new Label();
            label23 = new Label();
            lowestMatch = new TextBox();
            label6 = new Label();
            label22 = new Label();
            label21 = new Label();
            label16 = new Label();
            label17 = new Label();
            label19 = new Label();
            label20 = new Label();
            label14 = new Label();
            label15 = new Label();
            label13 = new Label();
            label18 = new Label();
            label12 = new Label();
            searchMethodComBox = new ComboBox();
            imgLableList = new ListBox();
            SaveTagBtn = new Button();
            DynTestBtn = new Button();
            searchTestTimer = new System.Windows.Forms.Timer(components);
            CaptureVideoHelp = new TextBox();
            openCapBtn = new Button();
            ResolutionBtn = new Button();
            label2 = new Label();
            monitorVisChk = new CheckBox();
            VideoMonitor = new PaintControl();
            Snapshot = new PaintControl();
            openFileDialog1 = new OpenFileDialog();
            monitorTimer = new System.Windows.Forms.Timer(components);
            groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)searchHNUD).BeginInit();
            ((System.ComponentModel.ISupportInitialize)searchWNUD).BeginInit();
            ((System.ComponentModel.ISupportInitialize)searchYNUD).BeginInit();
            ((System.ComponentModel.ISupportInitialize)searchXNUD).BeginInit();
            ((System.ComponentModel.ISupportInitialize)targetHNUD).BeginInit();
            ((System.ComponentModel.ISupportInitialize)targetWNUD).BeginInit();
            ((System.ComponentModel.ISupportInitialize)targetYNUD).BeginInit();
            ((System.ComponentModel.ISupportInitialize)searchResultImg).BeginInit();
            ((System.ComponentModel.ISupportInitialize)targetXNUD).BeginInit();
            ((System.ComponentModel.ISupportInitialize)targetImg).BeginInit();
            SuspendLayout();
            // 
            // reasultListBox
            // 
            reasultListBox.FormattingEnabled = true;
            reasultListBox.Location = new Point(405, 22);
            reasultListBox.Margin = new Padding(4);
            reasultListBox.Name = "reasultListBox";
            reasultListBox.Size = new Size(119, 44);
            reasultListBox.TabIndex = 1;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(756, 391);
            label1.Margin = new Padding(4, 0, 4, 0);
            label1.Name = "label1";
            label1.Size = new Size(135, 20);
            label1.TabIndex = 2;
            label1.Text = "搜图标签-双击加载";
            // 
            // captureBtn
            // 
            captureBtn.Location = new Point(35, 487);
            captureBtn.Margin = new Padding(4);
            captureBtn.Name = "captureBtn";
            captureBtn.Size = new Size(77, 33);
            captureBtn.TabIndex = 5;
            captureBtn.Text = "截图";
            captureBtn.UseVisualStyleBackColor = true;
            captureBtn.Click += captureBtn_Click;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(756, 364);
            label4.Margin = new Padding(4, 0, 4, 0);
            label4.Name = "label4";
            label4.Size = new Size(527, 20);
            label4.TabIndex = 7;
            label4.Text = "双击切换放大/编辑模式，滚轮缩放，ctrl+滚轮水平缩放，shift+滚轮垂直缩放";
            // 
            // rangeBtn
            // 
            rangeBtn.Location = new Point(119, 487);
            rangeBtn.Margin = new Padding(4);
            rangeBtn.Name = "rangeBtn";
            rangeBtn.Size = new Size(100, 33);
            rangeBtn.TabIndex = 8;
            rangeBtn.Text = "开始圈选(红)";
            rangeBtn.UseVisualStyleBackColor = true;
            rangeBtn.Click += rangeBtn_Click;
            // 
            // searchTestBtn
            // 
            searchTestBtn.Location = new Point(334, 487);
            searchTestBtn.Margin = new Padding(4);
            searchTestBtn.Name = "searchTestBtn";
            searchTestBtn.Size = new Size(86, 33);
            searchTestBtn.TabIndex = 9;
            searchTestBtn.Text = "搜索测试";
            searchTestBtn.UseVisualStyleBackColor = true;
            searchTestBtn.Click += searchTestBtn_Click;
            // 
            // targetBtn
            // 
            targetBtn.Location = new Point(227, 487);
            targetBtn.Margin = new Padding(4);
            targetBtn.Name = "targetBtn";
            targetBtn.Size = new Size(100, 33);
            targetBtn.TabIndex = 10;
            targetBtn.Text = "开始圈选(绿)";
            targetBtn.UseVisualStyleBackColor = true;
            targetBtn.Click += targetBtn_Click;
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(searchHNUD);
            groupBox1.Controls.Add(searchWNUD);
            groupBox1.Controls.Add(searchYNUD);
            groupBox1.Controls.Add(searchXNUD);
            groupBox1.Controls.Add(targetHNUD);
            groupBox1.Controls.Add(targetWNUD);
            groupBox1.Controls.Add(targetYNUD);
            groupBox1.Controls.Add(searchResultImg);
            groupBox1.Controls.Add(targetXNUD);
            groupBox1.Controls.Add(targetImg);
            groupBox1.Controls.Add(imgLabelNametxt);
            groupBox1.Controls.Add(label8);
            groupBox1.Controls.Add(label23);
            groupBox1.Controls.Add(lowestMatch);
            groupBox1.Controls.Add(reasultListBox);
            groupBox1.Controls.Add(label6);
            groupBox1.Controls.Add(label22);
            groupBox1.Controls.Add(label21);
            groupBox1.Controls.Add(label16);
            groupBox1.Controls.Add(label17);
            groupBox1.Controls.Add(label19);
            groupBox1.Controls.Add(label20);
            groupBox1.Controls.Add(label14);
            groupBox1.Controls.Add(label15);
            groupBox1.Controls.Add(label13);
            groupBox1.Controls.Add(label18);
            groupBox1.Controls.Add(label12);
            groupBox1.Controls.Add(searchMethodComBox);
            groupBox1.Location = new Point(35, 531);
            groupBox1.Margin = new Padding(4);
            groupBox1.Name = "groupBox1";
            groupBox1.Padding = new Padding(4);
            groupBox1.Size = new Size(669, 214);
            groupBox1.TabIndex = 18;
            groupBox1.TabStop = false;
            groupBox1.Text = "搜索参数";
            // 
            // searchHNUD
            // 
            searchHNUD.Location = new Point(341, 180);
            searchHNUD.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
            searchHNUD.Name = "searchHNUD";
            searchHNUD.Size = new Size(57, 27);
            searchHNUD.TabIndex = 42;
            // 
            // searchWNUD
            // 
            searchWNUD.Location = new Point(246, 179);
            searchWNUD.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
            searchWNUD.Name = "searchWNUD";
            searchWNUD.Size = new Size(57, 27);
            searchWNUD.TabIndex = 41;
            // 
            // searchYNUD
            // 
            searchYNUD.Location = new Point(341, 151);
            searchYNUD.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
            searchYNUD.Name = "searchYNUD";
            searchYNUD.Size = new Size(57, 27);
            searchYNUD.TabIndex = 40;
            // 
            // searchXNUD
            // 
            searchXNUD.Location = new Point(246, 150);
            searchXNUD.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
            searchXNUD.Name = "searchXNUD";
            searchXNUD.Size = new Size(57, 27);
            searchXNUD.TabIndex = 39;
            // 
            // targetHNUD
            // 
            targetHNUD.Location = new Point(138, 179);
            targetHNUD.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
            targetHNUD.Name = "targetHNUD";
            targetHNUD.Size = new Size(57, 27);
            targetHNUD.TabIndex = 38;
            // 
            // targetWNUD
            // 
            targetWNUD.Location = new Point(37, 179);
            targetWNUD.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
            targetWNUD.Name = "targetWNUD";
            targetWNUD.Size = new Size(57, 27);
            targetWNUD.TabIndex = 37;
            // 
            // targetYNUD
            // 
            targetYNUD.Location = new Point(138, 150);
            targetYNUD.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
            targetYNUD.Name = "targetYNUD";
            targetYNUD.Size = new Size(57, 27);
            targetYNUD.TabIndex = 36;
            // 
            // searchResultImg
            // 
            searchResultImg.Location = new Point(533, 89);
            searchResultImg.Margin = new Padding(4);
            searchResultImg.Name = "searchResultImg";
            searchResultImg.Size = new Size(120, 120);
            searchResultImg.SizeMode = PictureBoxSizeMode.Zoom;
            searchResultImg.TabIndex = 24;
            searchResultImg.TabStop = false;
            // 
            // targetXNUD
            // 
            targetXNUD.Location = new Point(37, 150);
            targetXNUD.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
            targetXNUD.Name = "targetXNUD";
            targetXNUD.Size = new Size(57, 27);
            targetXNUD.TabIndex = 35;
            // 
            // targetImg
            // 
            targetImg.Location = new Point(405, 89);
            targetImg.Margin = new Padding(4);
            targetImg.Name = "targetImg";
            targetImg.Size = new Size(120, 120);
            targetImg.SizeMode = PictureBoxSizeMode.Zoom;
            targetImg.TabIndex = 34;
            targetImg.TabStop = false;
            targetImg.DoubleClick += targetImg_DoubleClick;
            // 
            // imgLabelNametxt
            // 
            imgLabelNametxt.Location = new Point(91, 23);
            imgLabelNametxt.Margin = new Padding(4);
            imgLabelNametxt.Name = "imgLabelNametxt";
            imgLabelNametxt.Size = new Size(133, 27);
            imgLabelNametxt.TabIndex = 33;
            imgLabelNametxt.Text = "5号路蛋屋主人";
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.Location = new Point(30, 25);
            label8.Margin = new Padding(4, 0, 4, 0);
            label8.Name = "label8";
            label8.Size = new Size(58, 20);
            label8.TabIndex = 32;
            label8.Text = "标签名:";
            // 
            // label23
            // 
            label23.Location = new Point(533, 24);
            label23.Margin = new Padding(4, 0, 4, 0);
            label23.Name = "label23";
            label23.Size = new Size(120, 53);
            label23.TabIndex = 21;
            label23.Text = "匹配度：100%耗时：100毫秒最大匹配度100%";
            // 
            // lowestMatch
            // 
            lowestMatch.Location = new Point(112, 86);
            lowestMatch.Margin = new Padding(4);
            lowestMatch.Name = "lowestMatch";
            lowestMatch.Size = new Size(112, 27);
            lowestMatch.TabIndex = 31;
            lowestMatch.Text = "90.0";
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(6, 89);
            label6.Margin = new Padding(4, 0, 4, 0);
            label6.Name = "label6";
            label6.Size = new Size(118, 20);
            label6.TabIndex = 30;
            label6.Text = "最低更新匹配度:";
            // 
            // label22
            // 
            label22.AutoSize = true;
            label22.Location = new Point(216, 126);
            label22.Margin = new Padding(4, 0, 4, 0);
            label22.Name = "label22";
            label22.Size = new Size(73, 20);
            label22.TabIndex = 29;
            label22.Text = "搜索范围:";
            // 
            // label21
            // 
            label21.AutoSize = true;
            label21.Location = new Point(11, 126);
            label21.Margin = new Padding(4, 0, 4, 0);
            label21.Name = "label21";
            label21.Size = new Size(73, 20);
            label21.TabIndex = 28;
            label21.Text = "目标位置:";
            // 
            // label16
            // 
            label16.AutoSize = true;
            label16.Location = new Point(11, 153);
            label16.Margin = new Padding(4, 0, 4, 0);
            label16.Name = "label16";
            label16.Size = new Size(23, 20);
            label16.TabIndex = 26;
            label16.Text = "X:";
            // 
            // label17
            // 
            label17.AutoSize = true;
            label17.Location = new Point(113, 153);
            label17.Margin = new Padding(4, 0, 4, 0);
            label17.Name = "label17";
            label17.Size = new Size(22, 20);
            label17.TabIndex = 24;
            label17.Text = "Y:";
            // 
            // label19
            // 
            label19.AutoSize = true;
            label19.Location = new Point(7, 182);
            label19.Margin = new Padding(4, 0, 4, 0);
            label19.Name = "label19";
            label19.Size = new Size(28, 20);
            label19.TabIndex = 22;
            label19.Text = "宽:";
            // 
            // label20
            // 
            label20.AutoSize = true;
            label20.Location = new Point(108, 182);
            label20.Margin = new Padding(4, 0, 4, 0);
            label20.Name = "label20";
            label20.Size = new Size(28, 20);
            label20.TabIndex = 20;
            label20.Text = "高:";
            // 
            // label14
            // 
            label14.AutoSize = true;
            label14.Location = new Point(220, 153);
            label14.Margin = new Padding(4, 0, 4, 0);
            label14.Name = "label14";
            label14.Size = new Size(23, 20);
            label14.TabIndex = 18;
            label14.Text = "X:";
            // 
            // label15
            // 
            label15.AutoSize = true;
            label15.Location = new Point(316, 154);
            label15.Margin = new Padding(4, 0, 4, 0);
            label15.Name = "label15";
            label15.Size = new Size(22, 20);
            label15.TabIndex = 16;
            label15.Text = "Y:";
            // 
            // label13
            // 
            label13.AutoSize = true;
            label13.Location = new Point(216, 182);
            label13.Margin = new Padding(4, 0, 4, 0);
            label13.Name = "label13";
            label13.Size = new Size(28, 20);
            label13.TabIndex = 14;
            label13.Text = "宽:";
            // 
            // label18
            // 
            label18.AutoSize = true;
            label18.Location = new Point(316, 183);
            label18.Margin = new Padding(4, 0, 4, 0);
            label18.Name = "label18";
            label18.Size = new Size(28, 20);
            label18.TabIndex = 12;
            label18.Text = "高:";
            // 
            // label12
            // 
            label12.AutoSize = true;
            label12.Location = new Point(18, 58);
            label12.Margin = new Padding(4, 0, 4, 0);
            label12.Name = "label12";
            label12.Size = new Size(73, 20);
            label12.TabIndex = 1;
            label12.Text = "搜索方法:";
            // 
            // searchMethodComBox
            // 
            searchMethodComBox.FormattingEnabled = true;
            searchMethodComBox.Location = new Point(91, 54);
            searchMethodComBox.Margin = new Padding(4);
            searchMethodComBox.Name = "searchMethodComBox";
            searchMethodComBox.Size = new Size(133, 28);
            searchMethodComBox.TabIndex = 0;
            searchMethodComBox.Text = "选择搜索方法";
            // 
            // imgLableList
            // 
            imgLableList.FormattingEnabled = true;
            imgLableList.Location = new Point(759, 418);
            imgLableList.Margin = new Padding(4);
            imgLableList.Name = "imgLableList";
            imgLableList.Size = new Size(166, 324);
            imgLableList.TabIndex = 19;
            imgLableList.DoubleClick += imgLableList_DoubleClick;
            // 
            // SaveTagBtn
            // 
            SaveTagBtn.Location = new Point(540, 487);
            SaveTagBtn.Margin = new Padding(4);
            SaveTagBtn.Name = "SaveTagBtn";
            SaveTagBtn.Size = new Size(79, 33);
            SaveTagBtn.TabIndex = 20;
            SaveTagBtn.Text = "保存标签";
            SaveTagBtn.UseVisualStyleBackColor = true;
            SaveTagBtn.Click += SaveTagBtn_Click;
            // 
            // DynTestBtn
            // 
            DynTestBtn.Location = new Point(427, 487);
            DynTestBtn.Margin = new Padding(4);
            DynTestBtn.Name = "DynTestBtn";
            DynTestBtn.Size = new Size(112, 33);
            DynTestBtn.TabIndex = 22;
            DynTestBtn.Text = "动态测试";
            DynTestBtn.UseVisualStyleBackColor = true;
            DynTestBtn.Click += DynTestBtn_Click;
            // 
            // searchTestTimer
            // 
            searchTestTimer.Tick += timer1_Tick;
            // 
            // CaptureVideoHelp
            // 
            CaptureVideoHelp.Font = new Font("宋体", 9F);
            CaptureVideoHelp.Location = new Point(933, 418);
            CaptureVideoHelp.Margin = new Padding(4, 6, 4, 6);
            CaptureVideoHelp.Multiline = true;
            CaptureVideoHelp.Name = "CaptureVideoHelp";
            CaptureVideoHelp.ReadOnly = true;
            CaptureVideoHelp.ScrollBars = ScrollBars.Vertical;
            CaptureVideoHelp.Size = new Size(331, 327);
            CaptureVideoHelp.TabIndex = 25;
            // 
            // openCapBtn
            // 
            openCapBtn.Location = new Point(627, 487);
            openCapBtn.Margin = new Padding(4);
            openCapBtn.Name = "openCapBtn";
            openCapBtn.Size = new Size(77, 33);
            openCapBtn.TabIndex = 26;
            openCapBtn.Text = "打开截图";
            openCapBtn.UseVisualStyleBackColor = true;
            openCapBtn.Click += openCapBtn_Click;
            // 
            // ResolutionBtn
            // 
            ResolutionBtn.Location = new Point(755, 327);
            ResolutionBtn.Margin = new Padding(4);
            ResolutionBtn.Name = "ResolutionBtn";
            ResolutionBtn.Size = new Size(171, 33);
            ResolutionBtn.TabIndex = 27;
            ResolutionBtn.Text = "当前分辨率：1080P点击切换";
            ResolutionBtn.TextAlign = ContentAlignment.MiddleLeft;
            ResolutionBtn.UseVisualStyleBackColor = true;
            ResolutionBtn.Click += ResolutionBtn_Click;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(219, 459);
            label2.Margin = new Padding(4, 0, 4, 0);
            label2.Name = "label2";
            label2.Size = new Size(279, 20);
            label2.TabIndex = 30;
            label2.Text = "左键按住移动，滚轮缩放，右键按住圈选";
            // 
            // monitorVisChk
            // 
            monitorVisChk.AutoSize = true;
            monitorVisChk.Checked = true;
            monitorVisChk.CheckState = CheckState.Checked;
            monitorVisChk.Location = new Point(1187, 327);
            monitorVisChk.Margin = new Padding(4);
            monitorVisChk.Name = "monitorVisChk";
            monitorVisChk.Size = new Size(106, 24);
            monitorVisChk.TabIndex = 32;
            monitorVisChk.Text = "监视器显示";
            monitorVisChk.UseVisualStyleBackColor = true;
            monitorVisChk.CheckedChanged += monitorVisChk_CheckedChanged;
            // 
            // VideoMonitor
            // 
            VideoMonitor.BackColor = SystemColors.ButtonShadow;
            VideoMonitor.Location = new Point(756, 1);
            VideoMonitor.Margin = new Padding(4);
            VideoMonitor.Name = "VideoMonitor";
            VideoMonitor.Size = new Size(518, 318);
            VideoMonitor.TabIndex = 33;
            VideoMonitor.MouseDoubleClick += VideoSourcePlayerMonitor_MouseDoubleClick;
            VideoMonitor.MouseDown += VideoSourcePlayerMonitor_MouseDown;
            VideoMonitor.MouseMove += VideoSourcePlayerMonitor_MouseMove;
            VideoMonitor.MouseUp += VideoSourcePlayerMonitor_MouseUp;
            VideoMonitor.MouseWheel += VideoSourcePlayerMonitor_MouseWheel;
            VideoMonitor.PreviewKeyDown += VideoSourcePlayerMonitor_PreviewKeyDown;
            // 
            // Snapshot
            // 
            Snapshot.BackColor = SystemColors.ControlDark;
            Snapshot.BackgroundImageLayout = ImageLayout.Center;
            Snapshot.Location = new Point(1, 1);
            Snapshot.Margin = new Padding(4);
            Snapshot.Name = "Snapshot";
            Snapshot.Size = new Size(746, 448);
            Snapshot.TabIndex = 34;
            Snapshot.MouseDown += Snapshot_MouseDown;
            Snapshot.MouseMove += Snapshot_MouseMove;
            Snapshot.MouseUp += Snapshot_MouseUp;
            Snapshot.MouseWheel += Snapshot_MouseWheel;
            // 
            // openFileDialog1
            // 
            openFileDialog1.DefaultExt = "png";
            openFileDialog1.FileName = "target";
            openFileDialog1.Filter = "图片文件(*.jpg,*.gif,*.bmp,*.png)|*.jpg;*.gif;*.bmp;*.png";
            // 
            // monitorTimer
            // 
            monitorTimer.Enabled = true;
            monitorTimer.Interval = 16;
            monitorTimer.Tick += monitorTimer_Tick;
            // 
            // CaptureVideoForm
            // 
            AutoScaleMode = AutoScaleMode.Inherit;
            ClientSize = new Size(1279, 747);
            Controls.Add(Snapshot);
            Controls.Add(VideoMonitor);
            Controls.Add(monitorVisChk);
            Controls.Add(label2);
            Controls.Add(ResolutionBtn);
            Controls.Add(openCapBtn);
            Controls.Add(CaptureVideoHelp);
            Controls.Add(DynTestBtn);
            Controls.Add(SaveTagBtn);
            Controls.Add(imgLableList);
            Controls.Add(groupBox1);
            Controls.Add(targetBtn);
            Controls.Add(searchTestBtn);
            Controls.Add(rangeBtn);
            Controls.Add(label4);
            Controls.Add(captureBtn);
            Controls.Add(label1);
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            Icon = (Icon)resources.GetObject("$this.Icon");
            Margin = new Padding(4);
            MaximizeBox = false;
            Name = "CaptureVideoForm";
            Padding = new Padding(1);
            StartPosition = FormStartPosition.CenterScreen;
            Text = "搜图控制台";
            FormClosed += CaptureVideo_FormClosed;
            Load += CaptureVideo_Load;
            Resize += CaptureVideoForm_Resize;
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)searchHNUD).EndInit();
            ((System.ComponentModel.ISupportInitialize)searchWNUD).EndInit();
            ((System.ComponentModel.ISupportInitialize)searchYNUD).EndInit();
            ((System.ComponentModel.ISupportInitialize)searchXNUD).EndInit();
            ((System.ComponentModel.ISupportInitialize)targetHNUD).EndInit();
            ((System.ComponentModel.ISupportInitialize)targetWNUD).EndInit();
            ((System.ComponentModel.ISupportInitialize)targetYNUD).EndInit();
            ((System.ComponentModel.ISupportInitialize)searchResultImg).EndInit();
            ((System.ComponentModel.ISupportInitialize)targetXNUD).EndInit();
            ((System.ComponentModel.ISupportInitialize)targetImg).EndInit();
            ResumeLayout(false);
            PerformLayout();

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
        private System.Windows.Forms.Timer searchTestTimer;
        private System.Windows.Forms.PictureBox searchResultImg;
        private System.Windows.Forms.PictureBox targetImg;
        private System.Windows.Forms.TextBox CaptureVideoHelp;
        private System.Windows.Forms.Button openCapBtn;
        private System.Windows.Forms.Button ResolutionBtn;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.CheckBox monitorVisChk;
        private PaintControl VideoMonitor;
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
        private System.Windows.Forms.Timer monitorTimer;
    }
}