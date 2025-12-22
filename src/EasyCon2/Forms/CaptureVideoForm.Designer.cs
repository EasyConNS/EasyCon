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
            resultListBox = new ListBox();
            imgreadmelbl = new Label();
            captureBtn = new Button();
            readmelbl = new Label();
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
            imgnamelabel = new Label();
            matchRltlabel = new Label();
            searchlabel = new Label();
            targetlabel = new Label();
            xlbl = new Label();
            ylbl = new Label();
            wlbl = new Label();
            heightlbl = new Label();
            sxlbl = new Label();
            sylbl = new Label();
            swlbl = new Label();
            sheightlbl = new Label();
            srchmethodlabel = new Label();
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
            // resultListBox
            // 
            resultListBox.FormattingEnabled = true;
            resultListBox.Location = new Point(405, 22);
            resultListBox.Margin = new Padding(4);
            resultListBox.Name = "resultListBox";
            resultListBox.Size = new Size(119, 44);
            resultListBox.TabIndex = 1;
            // 
            // imgreadmelbl
            // 
            imgreadmelbl.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            imgreadmelbl.AutoSize = true;
            imgreadmelbl.Location = new Point(760, 374);
            imgreadmelbl.Margin = new Padding(4, 0, 4, 0);
            imgreadmelbl.Name = "imgreadmelbl";
            imgreadmelbl.Size = new Size(135, 20);
            imgreadmelbl.TabIndex = 2;
            imgreadmelbl.Text = "搜图标签-双击加载";
            // 
            // captureBtn
            // 
            captureBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            captureBtn.Location = new Point(18, 420);
            captureBtn.Margin = new Padding(4);
            captureBtn.Name = "captureBtn";
            captureBtn.Size = new Size(77, 33);
            captureBtn.TabIndex = 5;
            captureBtn.Text = "截图";
            captureBtn.UseVisualStyleBackColor = true;
            captureBtn.Click += captureBtn_Click;
            // 
            // readmelbl
            // 
            readmelbl.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            readmelbl.AutoSize = true;
            readmelbl.Location = new Point(937, 361);
            readmelbl.Margin = new Padding(4, 0, 4, 0);
            readmelbl.Name = "readmelbl";
            readmelbl.Size = new Size(240, 20);
            readmelbl.TabIndex = 7;
            readmelbl.Text = "双击切换放大/编辑模式，滚轮缩放";
            // 
            // rangeBtn
            // 
            rangeBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            rangeBtn.Location = new Point(102, 420);
            rangeBtn.Margin = new Padding(4);
            rangeBtn.Name = "rangeBtn";
            rangeBtn.Size = new Size(100, 33);
            rangeBtn.TabIndex = 6;
            rangeBtn.Text = "开始圈选(红)";
            rangeBtn.UseVisualStyleBackColor = true;
            rangeBtn.Click += rangeBtn_Click;
            // 
            // searchTestBtn
            // 
            searchTestBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            searchTestBtn.Location = new Point(317, 420);
            searchTestBtn.Margin = new Padding(4);
            searchTestBtn.Name = "searchTestBtn";
            searchTestBtn.Size = new Size(86, 33);
            searchTestBtn.TabIndex = 8;
            searchTestBtn.Text = "搜索测试";
            searchTestBtn.UseVisualStyleBackColor = true;
            searchTestBtn.Click += searchTestBtn_Click;
            // 
            // targetBtn
            // 
            targetBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            targetBtn.Location = new Point(210, 420);
            targetBtn.Margin = new Padding(4);
            targetBtn.Name = "targetBtn";
            targetBtn.Size = new Size(100, 33);
            targetBtn.TabIndex = 7;
            targetBtn.Text = "开始圈选(绿)";
            targetBtn.UseVisualStyleBackColor = true;
            targetBtn.Click += targetBtn_Click;
            // 
            // groupBox1
            // 
            groupBox1.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
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
            groupBox1.Controls.Add(imgnamelabel);
            groupBox1.Controls.Add(matchRltlabel);
            groupBox1.Controls.Add(resultListBox);
            groupBox1.Controls.Add(searchlabel);
            groupBox1.Controls.Add(targetlabel);
            groupBox1.Controls.Add(xlbl);
            groupBox1.Controls.Add(ylbl);
            groupBox1.Controls.Add(wlbl);
            groupBox1.Controls.Add(heightlbl);
            groupBox1.Controls.Add(sxlbl);
            groupBox1.Controls.Add(sylbl);
            groupBox1.Controls.Add(swlbl);
            groupBox1.Controls.Add(sheightlbl);
            groupBox1.Controls.Add(srchmethodlabel);
            groupBox1.Controls.Add(searchMethodComBox);
            groupBox1.Location = new Point(18, 464);
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
            imgLabelNametxt.Location = new Point(97, 31);
            imgLabelNametxt.Margin = new Padding(4);
            imgLabelNametxt.Name = "imgLabelNametxt";
            imgLabelNametxt.Size = new Size(133, 27);
            imgLabelNametxt.TabIndex = 12;
            imgLabelNametxt.Text = "5号路蛋屋主人";
            // 
            // imgnamelabel
            // 
            imgnamelabel.AutoSize = true;
            imgnamelabel.Location = new Point(36, 33);
            imgnamelabel.Margin = new Padding(4, 0, 4, 0);
            imgnamelabel.Name = "imgnamelabel";
            imgnamelabel.Size = new Size(58, 20);
            imgnamelabel.TabIndex = 32;
            imgnamelabel.Text = "标签名:";
            // 
            // matchRltlabel
            // 
            matchRltlabel.Location = new Point(533, 22);
            matchRltlabel.Margin = new Padding(4, 0, 4, 0);
            matchRltlabel.Name = "matchRltlabel";
            matchRltlabel.Size = new Size(120, 63);
            matchRltlabel.TabIndex = 21;
            matchRltlabel.Text = "匹配度：100%\r\n耗时：100毫秒\r\n最大匹配度100%";
            // 
            // searchlabel
            // 
            searchlabel.AutoSize = true;
            searchlabel.Location = new Point(216, 126);
            searchlabel.Margin = new Padding(4, 0, 4, 0);
            searchlabel.Name = "searchlabel";
            searchlabel.Size = new Size(73, 20);
            searchlabel.TabIndex = 29;
            searchlabel.Text = "搜索范围:";
            // 
            // targetlabel
            // 
            targetlabel.AutoSize = true;
            targetlabel.Location = new Point(11, 126);
            targetlabel.Margin = new Padding(4, 0, 4, 0);
            targetlabel.Name = "targetlabel";
            targetlabel.Size = new Size(73, 20);
            targetlabel.TabIndex = 28;
            targetlabel.Text = "目标位置:";
            // 
            // xlbl
            // 
            xlbl.AutoSize = true;
            xlbl.Location = new Point(11, 153);
            xlbl.Margin = new Padding(4, 0, 4, 0);
            xlbl.Name = "xlbl";
            xlbl.Size = new Size(23, 20);
            xlbl.TabIndex = 26;
            xlbl.Text = "X:";
            // 
            // ylbl
            // 
            ylbl.AutoSize = true;
            ylbl.Location = new Point(113, 153);
            ylbl.Margin = new Padding(4, 0, 4, 0);
            ylbl.Name = "ylbl";
            ylbl.Size = new Size(22, 20);
            ylbl.TabIndex = 24;
            ylbl.Text = "Y:";
            // 
            // wlbl
            // 
            wlbl.AutoSize = true;
            wlbl.Location = new Point(7, 182);
            wlbl.Margin = new Padding(4, 0, 4, 0);
            wlbl.Name = "wlbl";
            wlbl.Size = new Size(28, 20);
            wlbl.TabIndex = 22;
            wlbl.Text = "宽:";
            // 
            // heightlbl
            // 
            heightlbl.AutoSize = true;
            heightlbl.Location = new Point(108, 182);
            heightlbl.Margin = new Padding(4, 0, 4, 0);
            heightlbl.Name = "heightlbl";
            heightlbl.Size = new Size(28, 20);
            heightlbl.TabIndex = 20;
            heightlbl.Text = "高:";
            // 
            // sxlbl
            // 
            sxlbl.AutoSize = true;
            sxlbl.Location = new Point(220, 153);
            sxlbl.Margin = new Padding(4, 0, 4, 0);
            sxlbl.Name = "sxlbl";
            sxlbl.Size = new Size(23, 20);
            sxlbl.TabIndex = 18;
            sxlbl.Text = "X:";
            // 
            // sylbl
            // 
            sylbl.AutoSize = true;
            sylbl.Location = new Point(316, 154);
            sylbl.Margin = new Padding(4, 0, 4, 0);
            sylbl.Name = "sylbl";
            sylbl.Size = new Size(22, 20);
            sylbl.TabIndex = 16;
            sylbl.Text = "Y:";
            // 
            // swlbl
            // 
            swlbl.AutoSize = true;
            swlbl.Location = new Point(216, 182);
            swlbl.Margin = new Padding(4, 0, 4, 0);
            swlbl.Name = "swlbl";
            swlbl.Size = new Size(28, 20);
            swlbl.TabIndex = 14;
            swlbl.Text = "宽:";
            // 
            // sheightlbl
            // 
            sheightlbl.AutoSize = true;
            sheightlbl.Location = new Point(316, 183);
            sheightlbl.Margin = new Padding(4, 0, 4, 0);
            sheightlbl.Name = "sheightlbl";
            sheightlbl.Size = new Size(28, 20);
            sheightlbl.TabIndex = 12;
            sheightlbl.Text = "高:";
            // 
            // srchmethodlabel
            // 
            srchmethodlabel.AutoSize = true;
            srchmethodlabel.Location = new Point(24, 76);
            srchmethodlabel.Margin = new Padding(4, 0, 4, 0);
            srchmethodlabel.Name = "srchmethodlabel";
            srchmethodlabel.Size = new Size(73, 20);
            srchmethodlabel.TabIndex = 1;
            srchmethodlabel.Text = "搜索方法:";
            // 
            // searchMethodComBox
            // 
            searchMethodComBox.DropDownStyle = ComboBoxStyle.DropDownList;
            searchMethodComBox.FormattingEnabled = true;
            searchMethodComBox.Location = new Point(97, 72);
            searchMethodComBox.Margin = new Padding(4);
            searchMethodComBox.Name = "searchMethodComBox";
            searchMethodComBox.Size = new Size(133, 28);
            searchMethodComBox.TabIndex = 13;
            // 
            // imgLableList
            // 
            imgLableList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            imgLableList.FormattingEnabled = true;
            imgLableList.Location = new Point(763, 399);
            imgLableList.Margin = new Padding(4);
            imgLableList.Name = "imgLableList";
            imgLableList.Size = new Size(166, 264);
            imgLableList.TabIndex = 19;
            imgLableList.DoubleClick += imgLableList_DoubleClick;
            // 
            // SaveTagBtn
            // 
            SaveTagBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            SaveTagBtn.Location = new Point(523, 420);
            SaveTagBtn.Margin = new Padding(4);
            SaveTagBtn.Name = "SaveTagBtn";
            SaveTagBtn.Size = new Size(79, 33);
            SaveTagBtn.TabIndex = 10;
            SaveTagBtn.Text = "保存标签";
            SaveTagBtn.UseVisualStyleBackColor = true;
            SaveTagBtn.Click += SaveTagBtn_Click;
            // 
            // DynTestBtn
            // 
            DynTestBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            DynTestBtn.Location = new Point(410, 420);
            DynTestBtn.Margin = new Padding(4);
            DynTestBtn.Name = "DynTestBtn";
            DynTestBtn.Size = new Size(112, 33);
            DynTestBtn.TabIndex = 9;
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
            CaptureVideoHelp.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            CaptureVideoHelp.Font = new Font("宋体", 9F);
            CaptureVideoHelp.Location = new Point(937, 399);
            CaptureVideoHelp.Margin = new Padding(4, 6, 4, 6);
            CaptureVideoHelp.Multiline = true;
            CaptureVideoHelp.Name = "CaptureVideoHelp";
            CaptureVideoHelp.ReadOnly = true;
            CaptureVideoHelp.ScrollBars = ScrollBars.Vertical;
            CaptureVideoHelp.Size = new Size(331, 280);
            CaptureVideoHelp.TabIndex = 20;
            // 
            // openCapBtn
            // 
            openCapBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            openCapBtn.Location = new Point(610, 420);
            openCapBtn.Margin = new Padding(4);
            openCapBtn.Name = "openCapBtn";
            openCapBtn.Size = new Size(77, 33);
            openCapBtn.TabIndex = 11;
            openCapBtn.Text = "打开截图";
            openCapBtn.UseVisualStyleBackColor = true;
            openCapBtn.Click += openCapBtn_Click;
            // 
            // ResolutionBtn
            // 
            ResolutionBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            ResolutionBtn.Location = new Point(759, 327);
            ResolutionBtn.Margin = new Padding(4);
            ResolutionBtn.Name = "ResolutionBtn";
            ResolutionBtn.Size = new Size(221, 33);
            ResolutionBtn.TabIndex = 3;
            ResolutionBtn.Text = "当前分辨率：1080P点击切换";
            ResolutionBtn.UseVisualStyleBackColor = true;
            ResolutionBtn.Click += ResolutionBtn_Click;
            // 
            // label2
            // 
            label2.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            label2.AutoSize = true;
            label2.Location = new Point(202, 391);
            label2.Margin = new Padding(4, 0, 4, 0);
            label2.Name = "label2";
            label2.Size = new Size(279, 20);
            label2.TabIndex = 30;
            label2.Text = "左键按住移动，滚轮缩放，右键按住圈选";
            // 
            // monitorVisChk
            // 
            monitorVisChk.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            monitorVisChk.AutoSize = true;
            monitorVisChk.Checked = true;
            monitorVisChk.CheckState = CheckState.Checked;
            monitorVisChk.Location = new Point(1184, 328);
            monitorVisChk.Margin = new Padding(4);
            monitorVisChk.Name = "monitorVisChk";
            monitorVisChk.Size = new Size(106, 24);
            monitorVisChk.TabIndex = 4;
            monitorVisChk.Text = "监视器显示";
            monitorVisChk.UseVisualStyleBackColor = true;
            monitorVisChk.CheckedChanged += monitorVisChk_CheckedChanged;
            // 
            // VideoMonitor
            // 
            VideoMonitor.BackColor = SystemColors.ButtonShadow;
            VideoMonitor.Location = new Point(760, 1);
            VideoMonitor.Margin = new Padding(4);
            VideoMonitor.Name = "VideoMonitor";
            VideoMonitor.Size = new Size(518, 318);
            VideoMonitor.TabIndex = 2;
            VideoMonitor.MouseDoubleClick += VideoSourcePlayerMonitor_MouseDoubleClick;
            VideoMonitor.MouseDown += VideoSourcePlayerMonitor_MouseDown;
            VideoMonitor.MouseMove += VideoSourcePlayerMonitor_MouseMove;
            VideoMonitor.MouseUp += VideoSourcePlayerMonitor_MouseUp;
            VideoMonitor.MouseWheel += VideoSourcePlayerMonitor_MouseWheel;
            // 
            // Snapshot
            // 
            Snapshot.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Snapshot.BackColor = SystemColors.ControlDark;
            Snapshot.BackgroundImageLayout = ImageLayout.Center;
            Snapshot.Location = new Point(1, 1);
            Snapshot.Margin = new Padding(4);
            Snapshot.Name = "Snapshot";
            Snapshot.Size = new Size(740, 370);
            Snapshot.TabIndex = 1;
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
            openFileDialog1.RestoreDirectory = true;
            openFileDialog1.Title = "打开";
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
            ClientSize = new Size(1283, 681);
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
            Controls.Add(readmelbl);
            Controls.Add(captureBtn);
            Controls.Add(imgreadmelbl);
            DoubleBuffered = true;
            Icon = (Icon)resources.GetObject("$this.Icon");
            Margin = new Padding(4);
            MaximizeBox = false;
            Name = "CaptureVideoForm";
            Padding = new Padding(1);
            StartPosition = FormStartPosition.CenterScreen;
            Text = "搜图控制台";
            FormClosed += CaptureVideo_FormClosed;
            Load += CaptureVideo_Load;
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
        private System.Windows.Forms.ListBox resultListBox;
        private System.Windows.Forms.Label imgreadmelbl;
        private System.Windows.Forms.Button captureBtn;
        private System.Windows.Forms.Label readmelbl;
        private System.Windows.Forms.Button rangeBtn;
        private System.Windows.Forms.Button searchTestBtn;
        private System.Windows.Forms.Button targetBtn;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.ListBox imgLableList;
        private System.Windows.Forms.Button SaveTagBtn;
        private System.Windows.Forms.Label searchlabel;
        private System.Windows.Forms.Label targetlabel;
        private System.Windows.Forms.Label xlbl;
        private System.Windows.Forms.Label ylbl;
        private System.Windows.Forms.Label wlbl;
        private System.Windows.Forms.Label heightlbl;
        private System.Windows.Forms.Label sxlbl;
        private System.Windows.Forms.Label sylbl;
        private System.Windows.Forms.Label swlbl;
        private System.Windows.Forms.Label sheightlbl;
        private System.Windows.Forms.Label srchmethodlabel;
        private System.Windows.Forms.ComboBox searchMethodComBox;
        private System.Windows.Forms.Label matchRltlabel;
        private System.Windows.Forms.Button DynTestBtn;
        private System.Windows.Forms.TextBox imgLabelNametxt;
        private System.Windows.Forms.Label imgnamelabel;
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