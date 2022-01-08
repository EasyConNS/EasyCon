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
            this.targetImg = new System.Windows.Forms.PictureBox();
            this.imgLabelNametxt = new System.Windows.Forms.TextBox();
            this.label8 = new System.Windows.Forms.Label();
            this.lowestMatch = new System.Windows.Forms.TextBox();
            this.label6 = new System.Windows.Forms.Label();
            this.label22 = new System.Windows.Forms.Label();
            this.label21 = new System.Windows.Forms.Label();
            this.targRangX = new System.Windows.Forms.TextBox();
            this.label16 = new System.Windows.Forms.Label();
            this.targRangY = new System.Windows.Forms.TextBox();
            this.label17 = new System.Windows.Forms.Label();
            this.targRangW = new System.Windows.Forms.TextBox();
            this.label19 = new System.Windows.Forms.Label();
            this.targRangH = new System.Windows.Forms.TextBox();
            this.label20 = new System.Windows.Forms.Label();
            this.searchRangX = new System.Windows.Forms.TextBox();
            this.label14 = new System.Windows.Forms.Label();
            this.searchRangY = new System.Windows.Forms.TextBox();
            this.label15 = new System.Windows.Forms.Label();
            this.searchRangW = new System.Windows.Forms.TextBox();
            this.label13 = new System.Windows.Forms.Label();
            this.searchRangH = new System.Windows.Forms.TextBox();
            this.label18 = new System.Windows.Forms.Label();
            this.label12 = new System.Windows.Forms.Label();
            this.searchMethodComBox = new System.Windows.Forms.ComboBox();
            this.imgLableList = new System.Windows.Forms.ListBox();
            this.SaveTagBtn = new System.Windows.Forms.Button();
            this.label23 = new System.Windows.Forms.Label();
            this.DynTestBtn = new System.Windows.Forms.Button();
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.searchResultImg = new System.Windows.Forms.PictureBox();
            this.CaptureVideoHelp = new System.Windows.Forms.TextBox();
            this.openCapBtn = new System.Windows.Forms.Button();
            this.ResolutionBtn = new System.Windows.Forms.Button();
            this.label2 = new System.Windows.Forms.Label();
            this.monitorVisChk = new System.Windows.Forms.CheckBox();
            this.VideoSourcePlayerMonitor = new EasyCon2.Forms.PaintControl();
            this.Snapshot = new EasyCon2.Forms.PaintControl();
            this.groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.targetImg)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.searchResultImg)).BeginInit();
            this.SuspendLayout();
            // 
            // reasultListBox
            // 
            this.reasultListBox.FormattingEnabled = true;
            this.reasultListBox.ItemHeight = 17;
            this.reasultListBox.Location = new System.Drawing.Point(607, 564);
            this.reasultListBox.Margin = new System.Windows.Forms.Padding(4);
            this.reasultListBox.Name = "reasultListBox";
            this.reasultListBox.Size = new System.Drawing.Size(139, 55);
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
            this.captureBtn.Location = new System.Drawing.Point(1, 490);
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
            this.label4.Text = "双击切换采集/编辑模式，滚轮缩放，ctrl+滚轮水平缩放，shift+滚轮垂直缩放";
            // 
            // rangeBtn
            // 
            this.rangeBtn.Location = new System.Drawing.Point(85, 490);
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
            this.searchTestBtn.Location = new System.Drawing.Point(300, 490);
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
            this.targetBtn.Location = new System.Drawing.Point(193, 490);
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
            this.groupBox1.Controls.Add(this.targetImg);
            this.groupBox1.Controls.Add(this.imgLabelNametxt);
            this.groupBox1.Controls.Add(this.label8);
            this.groupBox1.Controls.Add(this.lowestMatch);
            this.groupBox1.Controls.Add(this.label6);
            this.groupBox1.Controls.Add(this.label22);
            this.groupBox1.Controls.Add(this.label21);
            this.groupBox1.Controls.Add(this.targRangX);
            this.groupBox1.Controls.Add(this.label16);
            this.groupBox1.Controls.Add(this.targRangY);
            this.groupBox1.Controls.Add(this.label17);
            this.groupBox1.Controls.Add(this.targRangW);
            this.groupBox1.Controls.Add(this.label19);
            this.groupBox1.Controls.Add(this.targRangH);
            this.groupBox1.Controls.Add(this.label20);
            this.groupBox1.Controls.Add(this.searchRangX);
            this.groupBox1.Controls.Add(this.label14);
            this.groupBox1.Controls.Add(this.searchRangY);
            this.groupBox1.Controls.Add(this.label15);
            this.groupBox1.Controls.Add(this.searchRangW);
            this.groupBox1.Controls.Add(this.label13);
            this.groupBox1.Controls.Add(this.searchRangH);
            this.groupBox1.Controls.Add(this.label18);
            this.groupBox1.Controls.Add(this.label12);
            this.groupBox1.Controls.Add(this.searchMethodComBox);
            this.groupBox1.Location = new System.Drawing.Point(7, 531);
            this.groupBox1.Margin = new System.Windows.Forms.Padding(4);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Padding = new System.Windows.Forms.Padding(4);
            this.groupBox1.Size = new System.Drawing.Size(592, 214);
            this.groupBox1.TabIndex = 18;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "搜索参数";
            // 
            // targetImg
            // 
            this.targetImg.Location = new System.Drawing.Point(512, 135);
            this.targetImg.Margin = new System.Windows.Forms.Padding(4);
            this.targetImg.Name = "targetImg";
            this.targetImg.Size = new System.Drawing.Size(74, 68);
            this.targetImg.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.targetImg.TabIndex = 34;
            this.targetImg.TabStop = false;
            // 
            // imgLabelNametxt
            // 
            this.imgLabelNametxt.Location = new System.Drawing.Point(91, 23);
            this.imgLabelNametxt.Margin = new System.Windows.Forms.Padding(4);
            this.imgLabelNametxt.Name = "imgLabelNametxt";
            this.imgLabelNametxt.Size = new System.Drawing.Size(115, 23);
            this.imgLabelNametxt.TabIndex = 33;
            this.imgLabelNametxt.Text = "5号路蛋屋主人";
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(6, 30);
            this.label8.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(71, 17);
            this.label8.TabIndex = 32;
            this.label8.Text = "搜图标签名:";
            // 
            // lowestMatch
            // 
            this.lowestMatch.Location = new System.Drawing.Point(516, 26);
            this.lowestMatch.Margin = new System.Windows.Forms.Padding(4);
            this.lowestMatch.Name = "lowestMatch";
            this.lowestMatch.Size = new System.Drawing.Size(72, 23);
            this.lowestMatch.TabIndex = 31;
            this.lowestMatch.Text = "90.0";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(407, 30);
            this.label6.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(95, 17);
            this.label6.TabIndex = 30;
            this.label6.Text = "最低更新匹配度:";
            // 
            // label22
            // 
            this.label22.AutoSize = true;
            this.label22.Location = new System.Drawing.Point(370, 108);
            this.label22.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label22.Name = "label22";
            this.label22.Size = new System.Drawing.Size(56, 17);
            this.label22.TabIndex = 29;
            this.label22.Text = "搜索范围";
            // 
            // label21
            // 
            this.label21.AutoSize = true;
            this.label21.Location = new System.Drawing.Point(117, 108);
            this.label21.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label21.Name = "label21";
            this.label21.Size = new System.Drawing.Size(56, 17);
            this.label21.TabIndex = 28;
            this.label21.Text = "目标位置";
            // 
            // targRangX
            // 
            this.targRangX.Location = new System.Drawing.Point(48, 136);
            this.targRangX.Margin = new System.Windows.Forms.Padding(4);
            this.targRangX.Name = "targRangX";
            this.targRangX.Size = new System.Drawing.Size(80, 23);
            this.targRangX.TabIndex = 27;
            // 
            // label16
            // 
            this.label16.AutoSize = true;
            this.label16.Location = new System.Drawing.Point(19, 143);
            this.label16.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label16.Name = "label16";
            this.label16.Size = new System.Drawing.Size(19, 17);
            this.label16.TabIndex = 26;
            this.label16.Text = "X:";
            // 
            // targRangY
            // 
            this.targRangY.Location = new System.Drawing.Point(168, 136);
            this.targRangY.Margin = new System.Windows.Forms.Padding(4);
            this.targRangY.Name = "targRangY";
            this.targRangY.Size = new System.Drawing.Size(80, 23);
            this.targRangY.TabIndex = 25;
            // 
            // label17
            // 
            this.label17.AutoSize = true;
            this.label17.Location = new System.Drawing.Point(139, 143);
            this.label17.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label17.Name = "label17";
            this.label17.Size = new System.Drawing.Size(18, 17);
            this.label17.TabIndex = 24;
            this.label17.Text = "Y:";
            // 
            // targRangW
            // 
            this.targRangW.Location = new System.Drawing.Point(48, 175);
            this.targRangW.Margin = new System.Windows.Forms.Padding(4);
            this.targRangW.Name = "targRangW";
            this.targRangW.Size = new System.Drawing.Size(80, 23);
            this.targRangW.TabIndex = 23;
            // 
            // label19
            // 
            this.label19.AutoSize = true;
            this.label19.Location = new System.Drawing.Point(15, 182);
            this.label19.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label19.Name = "label19";
            this.label19.Size = new System.Drawing.Size(23, 17);
            this.label19.TabIndex = 22;
            this.label19.Text = "宽:";
            // 
            // targRangH
            // 
            this.targRangH.Location = new System.Drawing.Point(168, 175);
            this.targRangH.Margin = new System.Windows.Forms.Padding(4);
            this.targRangH.Name = "targRangH";
            this.targRangH.Size = new System.Drawing.Size(80, 23);
            this.targRangH.TabIndex = 21;
            // 
            // label20
            // 
            this.label20.AutoSize = true;
            this.label20.Location = new System.Drawing.Point(135, 182);
            this.label20.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label20.Name = "label20";
            this.label20.Size = new System.Drawing.Size(23, 17);
            this.label20.TabIndex = 20;
            this.label20.Text = "高:";
            // 
            // searchRangX
            // 
            this.searchRangX.Location = new System.Drawing.Point(299, 136);
            this.searchRangX.Margin = new System.Windows.Forms.Padding(4);
            this.searchRangX.Name = "searchRangX";
            this.searchRangX.Size = new System.Drawing.Size(80, 23);
            this.searchRangX.TabIndex = 19;
            // 
            // label14
            // 
            this.label14.AutoSize = true;
            this.label14.Location = new System.Drawing.Point(268, 143);
            this.label14.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label14.Name = "label14";
            this.label14.Size = new System.Drawing.Size(19, 17);
            this.label14.TabIndex = 18;
            this.label14.Text = "X:";
            // 
            // searchRangY
            // 
            this.searchRangY.Location = new System.Drawing.Point(419, 136);
            this.searchRangY.Margin = new System.Windows.Forms.Padding(4);
            this.searchRangY.Name = "searchRangY";
            this.searchRangY.Size = new System.Drawing.Size(80, 23);
            this.searchRangY.TabIndex = 17;
            // 
            // label15
            // 
            this.label15.AutoSize = true;
            this.label15.Location = new System.Drawing.Point(388, 143);
            this.label15.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label15.Name = "label15";
            this.label15.Size = new System.Drawing.Size(18, 17);
            this.label15.TabIndex = 16;
            this.label15.Text = "Y:";
            // 
            // searchRangW
            // 
            this.searchRangW.Location = new System.Drawing.Point(299, 175);
            this.searchRangW.Margin = new System.Windows.Forms.Padding(4);
            this.searchRangW.Name = "searchRangW";
            this.searchRangW.Size = new System.Drawing.Size(80, 23);
            this.searchRangW.TabIndex = 15;
            // 
            // label13
            // 
            this.label13.AutoSize = true;
            this.label13.Location = new System.Drawing.Point(266, 182);
            this.label13.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label13.Name = "label13";
            this.label13.Size = new System.Drawing.Size(23, 17);
            this.label13.TabIndex = 14;
            this.label13.Text = "宽:";
            // 
            // searchRangH
            // 
            this.searchRangH.Location = new System.Drawing.Point(419, 175);
            this.searchRangH.Margin = new System.Windows.Forms.Padding(4);
            this.searchRangH.Name = "searchRangH";
            this.searchRangH.Size = new System.Drawing.Size(80, 23);
            this.searchRangH.TabIndex = 13;
            // 
            // label18
            // 
            this.label18.AutoSize = true;
            this.label18.Location = new System.Drawing.Point(386, 182);
            this.label18.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label18.Name = "label18";
            this.label18.Size = new System.Drawing.Size(23, 17);
            this.label18.TabIndex = 12;
            this.label18.Text = "高:";
            // 
            // label12
            // 
            this.label12.AutoSize = true;
            this.label12.Location = new System.Drawing.Point(211, 30);
            this.label12.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label12.Name = "label12";
            this.label12.Size = new System.Drawing.Size(59, 17);
            this.label12.TabIndex = 1;
            this.label12.Text = "搜索方法:";
            // 
            // searchMethodComBox
            // 
            this.searchMethodComBox.FormattingEnabled = true;
            this.searchMethodComBox.Location = new System.Drawing.Point(281, 24);
            this.searchMethodComBox.Margin = new System.Windows.Forms.Padding(4);
            this.searchMethodComBox.Name = "searchMethodComBox";
            this.searchMethodComBox.Size = new System.Drawing.Size(121, 25);
            this.searchMethodComBox.TabIndex = 0;
            this.searchMethodComBox.Text = "选择搜索方法";
            // 
            // imgLableList
            // 
            this.imgLableList.FormattingEnabled = true;
            this.imgLableList.ItemHeight = 17;
            this.imgLableList.Location = new System.Drawing.Point(760, 413);
            this.imgLableList.Margin = new System.Windows.Forms.Padding(4);
            this.imgLableList.Name = "imgLableList";
            this.imgLableList.Size = new System.Drawing.Size(166, 327);
            this.imgLableList.TabIndex = 19;
            this.imgLableList.DoubleClick += new System.EventHandler(this.imgLableList_DoubleClick);
            // 
            // SaveTagBtn
            // 
            this.SaveTagBtn.Location = new System.Drawing.Point(506, 490);
            this.SaveTagBtn.Margin = new System.Windows.Forms.Padding(4);
            this.SaveTagBtn.Name = "SaveTagBtn";
            this.SaveTagBtn.Size = new System.Drawing.Size(79, 33);
            this.SaveTagBtn.TabIndex = 20;
            this.SaveTagBtn.Text = "保存标签";
            this.SaveTagBtn.UseVisualStyleBackColor = true;
            this.SaveTagBtn.Click += new System.EventHandler(this.SaveTagBtn_Click);
            // 
            // label23
            // 
            this.label23.AutoSize = true;
            this.label23.Location = new System.Drawing.Point(607, 543);
            this.label23.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label23.Name = "label23";
            this.label23.Size = new System.Drawing.Size(92, 17);
            this.label23.TabIndex = 21;
            this.label23.Text = "匹配度：耗时：";
            // 
            // DynTestBtn
            // 
            this.DynTestBtn.Location = new System.Drawing.Point(393, 490);
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
            // searchResultImg
            // 
            this.searchResultImg.Location = new System.Drawing.Point(607, 631);
            this.searchResultImg.Margin = new System.Windows.Forms.Padding(4);
            this.searchResultImg.Name = "searchResultImg";
            this.searchResultImg.Size = new System.Drawing.Size(140, 103);
            this.searchResultImg.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.searchResultImg.TabIndex = 24;
            this.searchResultImg.TabStop = false;
            // 
            // CaptureVideoHelp
            // 
            this.CaptureVideoHelp.Cursor = System.Windows.Forms.Cursors.Arrow;
            this.CaptureVideoHelp.Font = new System.Drawing.Font("宋体", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.CaptureVideoHelp.Location = new System.Drawing.Point(933, 413);
            this.CaptureVideoHelp.Margin = new System.Windows.Forms.Padding(4, 6, 4, 6);
            this.CaptureVideoHelp.Multiline = true;
            this.CaptureVideoHelp.Name = "CaptureVideoHelp";
            this.CaptureVideoHelp.ReadOnly = true;
            this.CaptureVideoHelp.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.CaptureVideoHelp.Size = new System.Drawing.Size(331, 332);
            this.CaptureVideoHelp.TabIndex = 25;
            this.CaptureVideoHelp.Text = resources.GetString("CaptureVideoHelp.Text");
            // 
            // openCapBtn
            // 
            this.openCapBtn.Location = new System.Drawing.Point(593, 490);
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
            this.ResolutionBtn.Size = new System.Drawing.Size(204, 33);
            this.ResolutionBtn.TabIndex = 27;
            this.ResolutionBtn.Text = "当前分辨率：1080P点击切换";
            this.ResolutionBtn.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.ResolutionBtn.UseVisualStyleBackColor = true;
            this.ResolutionBtn.Click += new System.EventHandler(this.button8_Click);
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
            this.Controls.Add(this.searchResultImg);
            this.Controls.Add(this.DynTestBtn);
            this.Controls.Add(this.label23);
            this.Controls.Add(this.SaveTagBtn);
            this.Controls.Add(this.imgLableList);
            this.Controls.Add(this.reasultListBox);
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
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.targetImg)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.searchResultImg)).EndInit();
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
        private System.Windows.Forms.TextBox targRangX;
        private System.Windows.Forms.Label label16;
        private System.Windows.Forms.TextBox targRangY;
        private System.Windows.Forms.Label label17;
        private System.Windows.Forms.TextBox targRangW;
        private System.Windows.Forms.Label label19;
        private System.Windows.Forms.TextBox targRangH;
        private System.Windows.Forms.Label label20;
        private System.Windows.Forms.TextBox searchRangX;
        private System.Windows.Forms.Label label14;
        private System.Windows.Forms.TextBox searchRangY;
        private System.Windows.Forms.Label label15;
        private System.Windows.Forms.TextBox searchRangW;
        private System.Windows.Forms.Label label13;
        private System.Windows.Forms.TextBox searchRangH;
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
    }
}