namespace EasyCon
{
    partial class EasyConForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(EasyConForm));
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.文件ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.新建ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.打开ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.保存ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.另存为ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.关闭ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.退出ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.脚本ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.编译ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.执行ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.帮助ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.使用方法ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.联机模式ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.烧录模式ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.固件模式ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.下载AtmelFlipToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.显示调试信息ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.关于ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.项目源码ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.CpuOptToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.CaptureDevToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.SelectDeviceToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.buttonCLS = new System.Windows.Forms.Button();
            this.labelTimer = new System.Windows.Forms.Label();
            this.richTextBoxMessage = new System.Windows.Forms.RichTextBox();
            this.buttonScriptRunStop = new System.Windows.Forms.Button();
            this.buttonShowController = new System.Windows.Forms.Button();
            this.groupBoxScript = new System.Windows.Forms.GroupBox();
            this.textBoxScriptHelp = new System.Windows.Forms.TextBox();
            this.textBoxScript = new System.Windows.Forms.TextBox();
            this.statusStrip1 = new System.Windows.Forms.StatusStrip();
            this.toolStripProgressBar1 = new System.Windows.Forms.ToolStripProgressBar();
            this.toolStripStatusLabel1 = new System.Windows.Forms.ToolStripStatusLabel();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.ComPort = new System.Windows.Forms.ComboBox();
            this.buttonControllerHelp = new System.Windows.Forms.Button();
            this.labelSerialStatus = new System.Windows.Forms.Label();
            this.buttonSerialPortConnect = new System.Windows.Forms.Button();
            this.buttonSerialPortSearch = new System.Windows.Forms.Button();
            this.label18 = new System.Windows.Forms.Label();
            this.buttonKeyMapping = new System.Windows.Forms.Button();
            this.openFileDialog1 = new System.Windows.Forms.OpenFileDialog();
            this.saveFileDialog1 = new System.Windows.Forms.SaveFileDialog();
            this.groupBox6 = new System.Windows.Forms.GroupBox();
            this.buttonRecordPause = new System.Windows.Forms.Button();
            this.buttonRecord = new System.Windows.Forms.Button();
            this.buttonFlashClear = new System.Windows.Forms.Button();
            this.buttonFlash = new System.Windows.Forms.Button();
            this.buttonRemoteStop = new System.Windows.Forms.Button();
            this.buttonRemoteStart = new System.Windows.Forms.Button();
            this.groupBox5 = new System.Windows.Forms.GroupBox();
            this.buttonGenerateFirmware = new System.Windows.Forms.Button();
            this.comboBoxBoardType = new System.Windows.Forms.ComboBox();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.textBoxFirmware = new System.Windows.Forms.TextBox();
            this.menuStrip1.SuspendLayout();
            this.groupBox1.SuspendLayout();
            this.groupBoxScript.SuspendLayout();
            this.statusStrip1.SuspendLayout();
            this.groupBox3.SuspendLayout();
            this.groupBox6.SuspendLayout();
            this.groupBox5.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.SuspendLayout();
            // 
            // menuStrip1
            // 
            this.menuStrip1.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.文件ToolStripMenuItem,
            this.脚本ToolStripMenuItem,
            this.帮助ToolStripMenuItem,
            this.CaptureDevToolStripMenuItem});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Padding = new System.Windows.Forms.Padding(7, 2, 0, 2);
            this.menuStrip1.Size = new System.Drawing.Size(1119, 25);
            this.menuStrip1.TabIndex = 0;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // 文件ToolStripMenuItem
            // 
            this.文件ToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.新建ToolStripMenuItem,
            this.打开ToolStripMenuItem,
            this.保存ToolStripMenuItem,
            this.另存为ToolStripMenuItem,
            this.关闭ToolStripMenuItem,
            this.toolStripSeparator1,
            this.退出ToolStripMenuItem});
            this.文件ToolStripMenuItem.Name = "文件ToolStripMenuItem";
            this.文件ToolStripMenuItem.Size = new System.Drawing.Size(44, 21);
            this.文件ToolStripMenuItem.Text = "文件";
            // 
            // 新建ToolStripMenuItem
            // 
            this.新建ToolStripMenuItem.Name = "新建ToolStripMenuItem";
            this.新建ToolStripMenuItem.Size = new System.Drawing.Size(112, 22);
            this.新建ToolStripMenuItem.Text = "新建";
            this.新建ToolStripMenuItem.Click += new System.EventHandler(this.新建ToolStripMenuItem_Click);
            // 
            // 打开ToolStripMenuItem
            // 
            this.打开ToolStripMenuItem.Name = "打开ToolStripMenuItem";
            this.打开ToolStripMenuItem.Size = new System.Drawing.Size(112, 22);
            this.打开ToolStripMenuItem.Text = "打开";
            this.打开ToolStripMenuItem.Click += new System.EventHandler(this.打开ToolStripMenuItem_Click);
            // 
            // 保存ToolStripMenuItem
            // 
            this.保存ToolStripMenuItem.Name = "保存ToolStripMenuItem";
            this.保存ToolStripMenuItem.Size = new System.Drawing.Size(112, 22);
            this.保存ToolStripMenuItem.Text = "保存";
            this.保存ToolStripMenuItem.Click += new System.EventHandler(this.保存ToolStripMenuItem_Click);
            // 
            // 另存为ToolStripMenuItem
            // 
            this.另存为ToolStripMenuItem.Name = "另存为ToolStripMenuItem";
            this.另存为ToolStripMenuItem.Size = new System.Drawing.Size(112, 22);
            this.另存为ToolStripMenuItem.Text = "另存为";
            this.另存为ToolStripMenuItem.Click += new System.EventHandler(this.另存为ToolStripMenuItem_Click);
            // 
            // 关闭ToolStripMenuItem
            // 
            this.关闭ToolStripMenuItem.Name = "关闭ToolStripMenuItem";
            this.关闭ToolStripMenuItem.Size = new System.Drawing.Size(112, 22);
            this.关闭ToolStripMenuItem.Text = "关闭";
            this.关闭ToolStripMenuItem.Click += new System.EventHandler(this.关闭ToolStripMenuItem_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(109, 6);
            // 
            // 退出ToolStripMenuItem
            // 
            this.退出ToolStripMenuItem.Name = "退出ToolStripMenuItem";
            this.退出ToolStripMenuItem.Size = new System.Drawing.Size(112, 22);
            this.退出ToolStripMenuItem.Text = "退出";
            this.退出ToolStripMenuItem.Click += new System.EventHandler(this.退出ToolStripMenuItem_Click);
            // 
            // 脚本ToolStripMenuItem
            // 
            this.脚本ToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.编译ToolStripMenuItem,
            this.执行ToolStripMenuItem});
            this.脚本ToolStripMenuItem.Name = "脚本ToolStripMenuItem";
            this.脚本ToolStripMenuItem.Size = new System.Drawing.Size(44, 21);
            this.脚本ToolStripMenuItem.Text = "脚本";
            // 
            // 编译ToolStripMenuItem
            // 
            this.编译ToolStripMenuItem.Name = "编译ToolStripMenuItem";
            this.编译ToolStripMenuItem.Size = new System.Drawing.Size(100, 22);
            this.编译ToolStripMenuItem.Text = "编译";
            this.编译ToolStripMenuItem.Click += new System.EventHandler(this.编译ToolStripMenuItem_Click);
            // 
            // 执行ToolStripMenuItem
            // 
            this.执行ToolStripMenuItem.Name = "执行ToolStripMenuItem";
            this.执行ToolStripMenuItem.Size = new System.Drawing.Size(100, 22);
            this.执行ToolStripMenuItem.Text = "执行";
            this.执行ToolStripMenuItem.Click += new System.EventHandler(this.执行ToolStripMenuItem_Click);
            // 
            // 帮助ToolStripMenuItem
            // 
            this.帮助ToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.使用方法ToolStripMenuItem,
            this.下载AtmelFlipToolStripMenuItem,
            this.显示调试信息ToolStripMenuItem,
            this.toolStripSeparator2,
            this.关于ToolStripMenuItem,
            this.项目源码ToolStripMenuItem,
            this.CpuOptToolStripMenuItem});
            this.帮助ToolStripMenuItem.Name = "帮助ToolStripMenuItem";
            this.帮助ToolStripMenuItem.Size = new System.Drawing.Size(44, 21);
            this.帮助ToolStripMenuItem.Text = "帮助";
            // 
            // 使用方法ToolStripMenuItem
            // 
            this.使用方法ToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.联机模式ToolStripMenuItem,
            this.烧录模式ToolStripMenuItem,
            this.固件模式ToolStripMenuItem});
            this.使用方法ToolStripMenuItem.Name = "使用方法ToolStripMenuItem";
            this.使用方法ToolStripMenuItem.Size = new System.Drawing.Size(157, 22);
            this.使用方法ToolStripMenuItem.Text = "使用方法";
            // 
            // 联机模式ToolStripMenuItem
            // 
            this.联机模式ToolStripMenuItem.Name = "联机模式ToolStripMenuItem";
            this.联机模式ToolStripMenuItem.Size = new System.Drawing.Size(124, 22);
            this.联机模式ToolStripMenuItem.Tag = "";
            this.联机模式ToolStripMenuItem.Text = "联机模式";
            this.联机模式ToolStripMenuItem.Click += new System.EventHandler(this.联机模式ToolStripMenuItem_Click);
            // 
            // 烧录模式ToolStripMenuItem
            // 
            this.烧录模式ToolStripMenuItem.Name = "烧录模式ToolStripMenuItem";
            this.烧录模式ToolStripMenuItem.Size = new System.Drawing.Size(124, 22);
            this.烧录模式ToolStripMenuItem.Text = "烧录模式";
            this.烧录模式ToolStripMenuItem.Click += new System.EventHandler(this.烧录模式ToolStripMenuItem_Click);
            // 
            // 固件模式ToolStripMenuItem
            // 
            this.固件模式ToolStripMenuItem.Name = "固件模式ToolStripMenuItem";
            this.固件模式ToolStripMenuItem.Size = new System.Drawing.Size(124, 22);
            this.固件模式ToolStripMenuItem.Text = "固件模式";
            this.固件模式ToolStripMenuItem.Click += new System.EventHandler(this.固件模式ToolStripMenuItem_Click);
            // 
            // 下载AtmelFlipToolStripMenuItem
            // 
            this.下载AtmelFlipToolStripMenuItem.Name = "下载AtmelFlipToolStripMenuItem";
            this.下载AtmelFlipToolStripMenuItem.Size = new System.Drawing.Size(157, 22);
            this.下载AtmelFlipToolStripMenuItem.Text = "下载Atmel Flip";
            this.下载AtmelFlipToolStripMenuItem.Click += new System.EventHandler(this.下载AtmelFlipToolStripMenuItem_Click);
            // 
            // 显示调试信息ToolStripMenuItem
            // 
            this.显示调试信息ToolStripMenuItem.Name = "显示调试信息ToolStripMenuItem";
            this.显示调试信息ToolStripMenuItem.Size = new System.Drawing.Size(157, 22);
            this.显示调试信息ToolStripMenuItem.Text = "显示调试信息";
            this.显示调试信息ToolStripMenuItem.Click += new System.EventHandler(this.显示调试信息ToolStripMenuItem_Click);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(154, 6);
            // 
            // 关于ToolStripMenuItem
            // 
            this.关于ToolStripMenuItem.Name = "关于ToolStripMenuItem";
            this.关于ToolStripMenuItem.Size = new System.Drawing.Size(157, 22);
            this.关于ToolStripMenuItem.Text = "关于";
            this.关于ToolStripMenuItem.Click += new System.EventHandler(this.关于ToolStripMenuItem_Click);
            // 
            // 项目源码ToolStripMenuItem
            // 
            this.项目源码ToolStripMenuItem.Name = "项目源码ToolStripMenuItem";
            this.项目源码ToolStripMenuItem.Size = new System.Drawing.Size(157, 22);
            this.项目源码ToolStripMenuItem.Text = "项目源码";
            this.项目源码ToolStripMenuItem.Click += new System.EventHandler(this.项目源码ToolStripMenuItem_Click);
            // 
            // CpuOptToolStripMenuItem
            // 
            this.CpuOptToolStripMenuItem.Name = "CpuOptToolStripMenuItem";
            this.CpuOptToolStripMenuItem.Size = new System.Drawing.Size(157, 22);
            this.CpuOptToolStripMenuItem.Text = "CPU优化-关闭";
            this.CpuOptToolStripMenuItem.Click += new System.EventHandler(this.CpuOptToolStripMenuItem_Click);
            // 
            // CaptureDevToolStripMenuItem
            // 
            this.CaptureDevToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.SelectDeviceToolStripMenuItem});
            this.CaptureDevToolStripMenuItem.Name = "CaptureDevToolStripMenuItem";
            this.CaptureDevToolStripMenuItem.Size = new System.Drawing.Size(56, 21);
            this.CaptureDevToolStripMenuItem.Text = "采集卡";
            // 
            // SelectDeviceToolStripMenuItem
            // 
            this.SelectDeviceToolStripMenuItem.Name = "SelectDeviceToolStripMenuItem";
            this.SelectDeviceToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.SelectDeviceToolStripMenuItem.Text = "选择采集设备";
            this.SelectDeviceToolStripMenuItem.Click += new System.EventHandler(this.SelectDeviceToolStripMenuItem_Click);
            this.SelectDeviceToolStripMenuItem.MouseHover += new System.EventHandler(this.SelectDeviceToolStripMenuItem_MouseHover);
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.buttonCLS);
            this.groupBox1.Controls.Add(this.labelTimer);
            this.groupBox1.Controls.Add(this.richTextBoxMessage);
            this.groupBox1.Controls.Add(this.buttonScriptRunStop);
            this.groupBox1.Location = new System.Drawing.Point(14, 33);
            this.groupBox1.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Padding = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.groupBox1.Size = new System.Drawing.Size(312, 374);
            this.groupBox1.TabIndex = 1;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "联机模式";
            // 
            // buttonCLS
            // 
            this.buttonCLS.Location = new System.Drawing.Point(99, 332);
            this.buttonCLS.Name = "buttonCLS";
            this.buttonCLS.Size = new System.Drawing.Size(46, 36);
            this.buttonCLS.TabIndex = 36;
            this.buttonCLS.Text = "清屏";
            this.buttonCLS.UseVisualStyleBackColor = true;
            this.buttonCLS.Click += new System.EventHandler(this.buttonCLS_Click);
            // 
            // labelTimer
            // 
            this.labelTimer.BackColor = System.Drawing.Color.Black;
            this.labelTimer.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.labelTimer.Font = new System.Drawing.Font("Microsoft YaHei UI", 13F, System.Drawing.FontStyle.Bold);
            this.labelTimer.ForeColor = System.Drawing.Color.White;
            this.labelTimer.Location = new System.Drawing.Point(6, 332);
            this.labelTimer.Name = "labelTimer";
            this.labelTimer.Size = new System.Drawing.Size(92, 36);
            this.labelTimer.TabIndex = 35;
            this.labelTimer.Text = "00:00:00";
            this.labelTimer.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // richTextBoxMessage
            // 
            this.richTextBoxMessage.BackColor = System.Drawing.Color.Black;
            this.richTextBoxMessage.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.richTextBoxMessage.Cursor = System.Windows.Forms.Cursors.Arrow;
            this.richTextBoxMessage.ForeColor = System.Drawing.Color.White;
            this.richTextBoxMessage.Location = new System.Drawing.Point(6, 22);
            this.richTextBoxMessage.Name = "richTextBoxMessage";
            this.richTextBoxMessage.ReadOnly = true;
            this.richTextBoxMessage.Size = new System.Drawing.Size(302, 310);
            this.richTextBoxMessage.TabIndex = 34;
            this.richTextBoxMessage.Text = "";
            this.richTextBoxMessage.WordWrap = false;
            // 
            // buttonScriptRunStop
            // 
            this.buttonScriptRunStop.Location = new System.Drawing.Point(146, 332);
            this.buttonScriptRunStop.Name = "buttonScriptRunStop";
            this.buttonScriptRunStop.Size = new System.Drawing.Size(161, 36);
            this.buttonScriptRunStop.TabIndex = 4;
            this.buttonScriptRunStop.Text = "运行";
            this.buttonScriptRunStop.UseVisualStyleBackColor = true;
            this.buttonScriptRunStop.Click += new System.EventHandler(this.buttonScriptRunStop_Click);
            // 
            // buttonShowController
            // 
            this.buttonShowController.Location = new System.Drawing.Point(6, 79);
            this.buttonShowController.Name = "buttonShowController";
            this.buttonShowController.Size = new System.Drawing.Size(150, 40);
            this.buttonShowController.TabIndex = 3;
            this.buttonShowController.Text = "启用虚拟手柄";
            this.buttonShowController.UseVisualStyleBackColor = true;
            this.buttonShowController.Click += new System.EventHandler(this.buttonShowController_Click);
            // 
            // groupBoxScript
            // 
            this.groupBoxScript.Controls.Add(this.textBoxScriptHelp);
            this.groupBoxScript.Controls.Add(this.textBoxScript);
            this.groupBoxScript.Location = new System.Drawing.Point(332, 33);
            this.groupBoxScript.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.groupBoxScript.Name = "groupBoxScript";
            this.groupBoxScript.Padding = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.groupBoxScript.Size = new System.Drawing.Size(777, 702);
            this.groupBoxScript.TabIndex = 2;
            this.groupBoxScript.TabStop = false;
            this.groupBoxScript.Text = "脚本";
            // 
            // textBoxScriptHelp
            // 
            this.textBoxScriptHelp.Cursor = System.Windows.Forms.Cursors.Arrow;
            this.textBoxScriptHelp.Location = new System.Drawing.Point(434, 23);
            this.textBoxScriptHelp.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.textBoxScriptHelp.Multiline = true;
            this.textBoxScriptHelp.Name = "textBoxScriptHelp";
            this.textBoxScriptHelp.ReadOnly = true;
            this.textBoxScriptHelp.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBoxScriptHelp.Size = new System.Drawing.Size(336, 671);
            this.textBoxScriptHelp.TabIndex = 1;
            this.textBoxScriptHelp.Text = resources.GetString("textBoxScriptHelp.Text");
            // 
            // textBoxScript
            // 
            this.textBoxScript.AcceptsReturn = true;
            this.textBoxScript.AcceptsTab = true;
            this.textBoxScript.AllowDrop = true;
            this.textBoxScript.Location = new System.Drawing.Point(7, 23);
            this.textBoxScript.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.textBoxScript.Multiline = true;
            this.textBoxScript.Name = "textBoxScript";
            this.textBoxScript.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.textBoxScript.Size = new System.Drawing.Size(422, 671);
            this.textBoxScript.TabIndex = 0;
            this.textBoxScript.TextChanged += new System.EventHandler(this.textBoxScript_TextChanged);
            this.textBoxScript.DragDrop += new System.Windows.Forms.DragEventHandler(this.textBoxScript_DragDrop);
            this.textBoxScript.DragEnter += new System.Windows.Forms.DragEventHandler(this.textBoxScript_DragEnter);
            this.textBoxScript.PreviewKeyDown += new System.Windows.Forms.PreviewKeyDownEventHandler(this.textBoxScript_PreviewKeyDown);
            // 
            // statusStrip1
            // 
            this.statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripProgressBar1,
            this.toolStripStatusLabel1});
            this.statusStrip1.Location = new System.Drawing.Point(0, 739);
            this.statusStrip1.Name = "statusStrip1";
            this.statusStrip1.Size = new System.Drawing.Size(1119, 22);
            this.statusStrip1.TabIndex = 3;
            this.statusStrip1.Text = "statusStrip1";
            // 
            // toolStripProgressBar1
            // 
            this.toolStripProgressBar1.Name = "toolStripProgressBar1";
            this.toolStripProgressBar1.Size = new System.Drawing.Size(100, 16);
            // 
            // toolStripStatusLabel1
            // 
            this.toolStripStatusLabel1.Name = "toolStripStatusLabel1";
            this.toolStripStatusLabel1.Size = new System.Drawing.Size(0, 17);
            // 
            // groupBox3
            // 
            this.groupBox3.Controls.Add(this.ComPort);
            this.groupBox3.Controls.Add(this.buttonControllerHelp);
            this.groupBox3.Controls.Add(this.labelSerialStatus);
            this.groupBox3.Controls.Add(this.buttonSerialPortConnect);
            this.groupBox3.Controls.Add(this.buttonSerialPortSearch);
            this.groupBox3.Controls.Add(this.label18);
            this.groupBox3.Controls.Add(this.buttonKeyMapping);
            this.groupBox3.Controls.Add(this.buttonShowController);
            this.groupBox3.Location = new System.Drawing.Point(14, 609);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(312, 126);
            this.groupBox3.TabIndex = 4;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "连接";
            // 
            // ComPort
            // 
            this.ComPort.FormattingEnabled = true;
            this.ComPort.Location = new System.Drawing.Point(39, 20);
            this.ComPort.Name = "ComPort";
            this.ComPort.Size = new System.Drawing.Size(116, 24);
            this.ComPort.TabIndex = 34;
            this.ComPort.Text = "下拉选择串口";
            this.ComPort.DropDown += new System.EventHandler(this.ComPort_DropDown);
            // 
            // buttonControllerHelp
            // 
            this.buttonControllerHelp.Location = new System.Drawing.Point(157, 79);
            this.buttonControllerHelp.Name = "buttonControllerHelp";
            this.buttonControllerHelp.Size = new System.Drawing.Size(74, 40);
            this.buttonControllerHelp.TabIndex = 33;
            this.buttonControllerHelp.Text = "?";
            this.buttonControllerHelp.UseVisualStyleBackColor = true;
            this.buttonControllerHelp.Click += new System.EventHandler(this.buttonControllerHelp_Click);
            // 
            // labelSerialStatus
            // 
            this.labelSerialStatus.BackColor = System.Drawing.Color.DimGray;
            this.labelSerialStatus.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.labelSerialStatus.Location = new System.Drawing.Point(158, 21);
            this.labelSerialStatus.Name = "labelSerialStatus";
            this.labelSerialStatus.Size = new System.Drawing.Size(148, 22);
            this.labelSerialStatus.TabIndex = 31;
            this.labelSerialStatus.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // buttonSerialPortConnect
            // 
            this.buttonSerialPortConnect.Location = new System.Drawing.Point(6, 45);
            this.buttonSerialPortConnect.Name = "buttonSerialPortConnect";
            this.buttonSerialPortConnect.Size = new System.Drawing.Size(150, 32);
            this.buttonSerialPortConnect.TabIndex = 30;
            this.buttonSerialPortConnect.Text = "连接单片机(手动)";
            this.buttonSerialPortConnect.UseVisualStyleBackColor = true;
            this.buttonSerialPortConnect.Click += new System.EventHandler(this.buttonSerialPortConnect_Click);
            // 
            // buttonSerialPortSearch
            // 
            this.buttonSerialPortSearch.Location = new System.Drawing.Point(157, 45);
            this.buttonSerialPortSearch.Name = "buttonSerialPortSearch";
            this.buttonSerialPortSearch.Size = new System.Drawing.Size(150, 32);
            this.buttonSerialPortSearch.TabIndex = 29;
            this.buttonSerialPortSearch.Text = "自动搜索端口(推荐)";
            this.buttonSerialPortSearch.UseVisualStyleBackColor = true;
            this.buttonSerialPortSearch.Click += new System.EventHandler(this.buttonSerialPortSearch_Click);
            // 
            // label18
            // 
            this.label18.Location = new System.Drawing.Point(3, 21);
            this.label18.Margin = new System.Windows.Forms.Padding(0);
            this.label18.Name = "label18";
            this.label18.Size = new System.Drawing.Size(45, 18);
            this.label18.TabIndex = 28;
            this.label18.Text = "串口";
            this.label18.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // buttonKeyMapping
            // 
            this.buttonKeyMapping.Location = new System.Drawing.Point(232, 79);
            this.buttonKeyMapping.Name = "buttonKeyMapping";
            this.buttonKeyMapping.Size = new System.Drawing.Size(75, 40);
            this.buttonKeyMapping.TabIndex = 4;
            this.buttonKeyMapping.Text = "按键设置";
            this.buttonKeyMapping.UseVisualStyleBackColor = true;
            this.buttonKeyMapping.Click += new System.EventHandler(this.buttonKeyMapping_Click);
            // 
            // groupBox6
            // 
            this.groupBox6.Controls.Add(this.buttonRecordPause);
            this.groupBox6.Controls.Add(this.buttonRecord);
            this.groupBox6.Controls.Add(this.buttonFlashClear);
            this.groupBox6.Controls.Add(this.buttonFlash);
            this.groupBox6.Controls.Add(this.buttonRemoteStop);
            this.groupBox6.Controls.Add(this.buttonRemoteStart);
            this.groupBox6.Location = new System.Drawing.Point(165, 414);
            this.groupBox6.Name = "groupBox6";
            this.groupBox6.Size = new System.Drawing.Size(161, 189);
            this.groupBox6.TabIndex = 5;
            this.groupBox6.TabStop = false;
            this.groupBox6.Text = "烧录模式";
            // 
            // buttonRecordPause
            // 
            this.buttonRecordPause.Enabled = false;
            this.buttonRecordPause.Location = new System.Drawing.Point(81, 129);
            this.buttonRecordPause.Name = "buttonRecordPause";
            this.buttonRecordPause.Size = new System.Drawing.Size(74, 55);
            this.buttonRecordPause.TabIndex = 6;
            this.buttonRecordPause.Text = "暂停录制";
            this.buttonRecordPause.UseVisualStyleBackColor = true;
            this.buttonRecordPause.Click += new System.EventHandler(this.buttonRecordPause_Click);
            // 
            // buttonRecord
            // 
            this.buttonRecord.Location = new System.Drawing.Point(6, 129);
            this.buttonRecord.Name = "buttonRecord";
            this.buttonRecord.Size = new System.Drawing.Size(74, 55);
            this.buttonRecord.TabIndex = 5;
            this.buttonRecord.Text = "录制脚本";
            this.buttonRecord.UseVisualStyleBackColor = true;
            this.buttonRecord.Click += new System.EventHandler(this.buttonRecord_Click);
            // 
            // buttonFlashClear
            // 
            this.buttonFlashClear.Location = new System.Drawing.Point(81, 21);
            this.buttonFlashClear.Name = "buttonFlashClear";
            this.buttonFlashClear.Size = new System.Drawing.Size(74, 55);
            this.buttonFlashClear.TabIndex = 4;
            this.buttonFlashClear.Text = "清除烧录";
            this.buttonFlashClear.UseVisualStyleBackColor = true;
            this.buttonFlashClear.Click += new System.EventHandler(this.buttonFlashClear_Click);
            // 
            // buttonFlash
            // 
            this.buttonFlash.Location = new System.Drawing.Point(6, 21);
            this.buttonFlash.Name = "buttonFlash";
            this.buttonFlash.Size = new System.Drawing.Size(74, 55);
            this.buttonFlash.TabIndex = 1;
            this.buttonFlash.Text = "编译并烧录";
            this.buttonFlash.UseVisualStyleBackColor = true;
            this.buttonFlash.Click += new System.EventHandler(this.buttonFlash_Click);
            // 
            // buttonRemoteStop
            // 
            this.buttonRemoteStop.Location = new System.Drawing.Point(81, 77);
            this.buttonRemoteStop.Name = "buttonRemoteStop";
            this.buttonRemoteStop.Size = new System.Drawing.Size(74, 50);
            this.buttonRemoteStop.TabIndex = 3;
            this.buttonRemoteStop.Text = "远程停止";
            this.buttonRemoteStop.UseVisualStyleBackColor = true;
            this.buttonRemoteStop.Click += new System.EventHandler(this.buttonRemoteStop_Click);
            // 
            // buttonRemoteStart
            // 
            this.buttonRemoteStart.Location = new System.Drawing.Point(6, 77);
            this.buttonRemoteStart.Name = "buttonRemoteStart";
            this.buttonRemoteStart.Size = new System.Drawing.Size(74, 50);
            this.buttonRemoteStart.TabIndex = 2;
            this.buttonRemoteStart.Text = "远程运行";
            this.buttonRemoteStart.UseVisualStyleBackColor = true;
            this.buttonRemoteStart.Click += new System.EventHandler(this.buttonRemoteStart_Click);
            // 
            // groupBox5
            // 
            this.groupBox5.Controls.Add(this.buttonGenerateFirmware);
            this.groupBox5.Location = new System.Drawing.Point(14, 499);
            this.groupBox5.Name = "groupBox5";
            this.groupBox5.Size = new System.Drawing.Size(145, 104);
            this.groupBox5.TabIndex = 4;
            this.groupBox5.TabStop = false;
            this.groupBox5.Text = "固件模式";
            // 
            // buttonGenerateFirmware
            // 
            this.buttonGenerateFirmware.Location = new System.Drawing.Point(6, 21);
            this.buttonGenerateFirmware.Name = "buttonGenerateFirmware";
            this.buttonGenerateFirmware.Size = new System.Drawing.Size(134, 77);
            this.buttonGenerateFirmware.TabIndex = 0;
            this.buttonGenerateFirmware.Text = "生成固件";
            this.buttonGenerateFirmware.UseVisualStyleBackColor = true;
            this.buttonGenerateFirmware.Click += new System.EventHandler(this.buttonGenerateFirmware_Click);
            // 
            // comboBoxBoardType
            // 
            this.comboBoxBoardType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBoxBoardType.FormattingEnabled = true;
            this.comboBoxBoardType.Location = new System.Drawing.Point(6, 21);
            this.comboBoxBoardType.Name = "comboBoxBoardType";
            this.comboBoxBoardType.Size = new System.Drawing.Size(133, 24);
            this.comboBoxBoardType.TabIndex = 5;
            this.comboBoxBoardType.SelectedIndexChanged += new System.EventHandler(this.comboBoxBoardType_SelectedIndexChanged);
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.textBoxFirmware);
            this.groupBox2.Controls.Add(this.comboBoxBoardType);
            this.groupBox2.Location = new System.Drawing.Point(14, 414);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(145, 79);
            this.groupBox2.TabIndex = 5;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "单片机类型";
            // 
            // textBoxFirmware
            // 
            this.textBoxFirmware.Cursor = System.Windows.Forms.Cursors.Default;
            this.textBoxFirmware.Location = new System.Drawing.Point(6, 49);
            this.textBoxFirmware.Name = "textBoxFirmware";
            this.textBoxFirmware.ReadOnly = true;
            this.textBoxFirmware.Size = new System.Drawing.Size(133, 22);
            this.textBoxFirmware.TabIndex = 5;
            this.textBoxFirmware.WordWrap = false;
            // 
            // EasyConForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1119, 761);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.groupBox6);
            this.Controls.Add(this.groupBox5);
            this.Controls.Add(this.groupBox3);
            this.Controls.Add(this.statusStrip1);
            this.Controls.Add(this.groupBoxScript);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.menuStrip1);
            this.Font = new System.Drawing.Font("微软雅黑", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.KeyPreview = true;
            this.MainMenuStrip = this.menuStrip1;
            this.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.MaximizeBox = false;
            this.Name = "EasyConForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "伊机控 EasyCon v1.21";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.EasyConForm_FormClosing);
            this.Load += new System.EventHandler(this.EasyConForm_Load);
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.EasyConForm_KeyDown);
            this.Resize += new System.EventHandler(this.EasyConForm_Resize);
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.groupBox1.ResumeLayout(false);
            this.groupBoxScript.ResumeLayout(false);
            this.groupBoxScript.PerformLayout();
            this.statusStrip1.ResumeLayout(false);
            this.statusStrip1.PerformLayout();
            this.groupBox3.ResumeLayout(false);
            this.groupBox6.ResumeLayout(false);
            this.groupBox5.ResumeLayout(false);
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem 文件ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 脚本ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 帮助ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 新建ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 打开ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 保存ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 另存为ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 关闭ToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripMenuItem 退出ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 编译ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 执行ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 关于ToolStripMenuItem;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.GroupBox groupBoxScript;
        private System.Windows.Forms.TextBox textBoxScript;
        private System.Windows.Forms.TextBox textBoxScriptHelp;
        private System.Windows.Forms.Button buttonShowController;
        private System.Windows.Forms.StatusStrip statusStrip1;
        private System.Windows.Forms.ToolStripProgressBar toolStripProgressBar1;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel1;
        private System.Windows.Forms.Button buttonScriptRunStop;
        private System.Windows.Forms.Label labelTimer;
        private System.Windows.Forms.RichTextBox richTextBoxMessage;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.Button buttonKeyMapping;
        private System.Windows.Forms.Button buttonSerialPortSearch;
        private System.Windows.Forms.Label label18;
        private System.Windows.Forms.Button buttonSerialPortConnect;
        private System.Windows.Forms.Label labelSerialStatus;
        private System.Windows.Forms.OpenFileDialog openFileDialog1;
        private System.Windows.Forms.SaveFileDialog saveFileDialog1;
        private System.Windows.Forms.Button buttonControllerHelp;
        private System.Windows.Forms.ToolStripMenuItem 使用方法ToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.Button buttonCLS;
        private System.Windows.Forms.Button buttonGenerateFirmware;
        private System.Windows.Forms.Button buttonFlash;
        private System.Windows.Forms.Button buttonRemoteStop;
        private System.Windows.Forms.Button buttonRemoteStart;
        private System.Windows.Forms.GroupBox groupBox6;
        private System.Windows.Forms.Button buttonFlashClear;
        private System.Windows.Forms.GroupBox groupBox5;
        private System.Windows.Forms.ToolStripMenuItem 联机模式ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 烧录模式ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 固件模式ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 显示调试信息ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 项目源码ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 下载AtmelFlipToolStripMenuItem;
        private System.Windows.Forms.ComboBox comboBoxBoardType;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.TextBox textBoxFirmware;
        private System.Windows.Forms.Button buttonRecord;
        private System.Windows.Forms.Button buttonRecordPause;
        private System.Windows.Forms.ToolStripMenuItem CpuOptToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem CaptureDevToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem SelectDeviceToolStripMenuItem;
        private System.Windows.Forms.ComboBox ComPort;
    }
}

