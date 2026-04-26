using Avalonia.Win32.Interoperability;
using EasyCon2.Forms;

namespace EasyCon2.App
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
            menuStrip = new MenuStrip();
            文件ToolStripMenuItem = new ToolStripMenuItem();
            新建ToolStripMenuItem = new ToolStripMenuItem();
            打开ToolStripMenuItem = new ToolStripMenuItem();
            保存ToolStripMenuItem = new ToolStripMenuItem();
            另存为ToolStripMenuItem = new ToolStripMenuItem();
            关闭ToolStripMenuItem = new ToolStripMenuItem();
            toolStripSeparator1 = new ToolStripSeparator();
            退出ToolStripMenuItem = new ToolStripMenuItem();
            編輯ToolStripMenuItem = new ToolStripMenuItem();
            查找替換ToolStripMenuItem = new ToolStripMenuItem();
            查找下一个ToolStripMenuItem = new ToolStripMenuItem();
            注释取消注释ToolStripMenuItem = new ToolStripMenuItem();
            脚本ToolStripMenuItem = new ToolStripMenuItem();
            编译ToolStripMenuItem = new ToolStripMenuItem();
            执行ToolStripMenuItem = new ToolStripMenuItem();
            搜图ToolStripMenuItem = new ToolStripMenuItem();
            采集卡类型ToolStripMenuItem = new ToolStripMenuItem();
            搜图说明ToolStripMenuItem = new ToolStripMenuItem();
            设置环境变量ToolStripMenuItem = new ToolStripMenuItem();
            设置ToolStripMenuItem = new ToolStripMenuItem();
            推送设置ToolStripMenuItem = new ToolStripMenuItem();
            显示调试信息ToolStripMenuItem = new ToolStripMenuItem();
            烧录自动运行ToolStripMenuItem = new ToolStripMenuItem();
            显示折叠ToolStripMenuItem = new ToolStripMenuItem();
            代码自动补全ToolStripMenuItem = new ToolStripMenuItem();
            深色模式ToolStripMenuItem = new ToolStripMenuItem();
            高精度模式ToolStripMenuItem = new ToolStripMenuItem();
            蓝牙ToolStripMenuItem = new ToolStripMenuItem();
            蓝牙设备驱动配置ToolStripMenuItem = new ToolStripMenuItem();
            eSP32ToolStripMenuItem = new ToolStripMenuItem();
            手柄设置ToolStripMenuItem = new ToolStripMenuItem();
            取消配对ToolStripMenuItem = new ToolStripMenuItem();
            画图ToolStripMenuItem = new ToolStripMenuItem();
            喷射ToolStripMenuItem = new ToolStripMenuItem();
            自由画板鼠标代替摇杆ToolStripMenuItem = new ToolStripMenuItem();
            帮助ToolStripMenuItem = new ToolStripMenuItem();
            固件模式ToolStripMenuItem = new ToolStripMenuItem();
            联机模式ToolStripMenuItem = new ToolStripMenuItem();
            烧录模式ToolStripMenuItem = new ToolStripMenuItem();
            toolStripSeparator2 = new ToolStripSeparator();
            检查更新ToolStripMenuItem = new ToolStripMenuItem();
            项目源码ToolStripMenuItem = new ToolStripMenuItem();
            关于ToolStripMenuItem = new ToolStripMenuItem();
            buttonScriptHelp = new Button();
            labelTimer = new Label();
            logTxtBox = new RichLogBox();
            buttonRecordPause = new Button();
            buttonRecord = new Button();
            buttonFlashClear = new Button();
            buttonFlash = new Button();
            buttonRemoteStop = new Button();
            buttonRemoteStart = new Button();
            grpFirmware = new GroupBox();
            genFwButton = new Button();
            comboBoxBoardType = new ComboBox();
            buttonShowController = new Button();
            editorHost = new WinFormsAvaloniaControlHost();
            ComPort = new ComboBox();
            buttonSerialPortConnect = new Button();
            buttonSerialPortSearch = new Button();
            buttonControllerHelp = new Button();
            buttonKeyMapping = new Button();
            comboInputMode = new ComboBox();
            openFileDialog = new OpenFileDialog();
            saveFileDialog = new SaveFileDialog();
            toolStripStatusLabel1 = new ToolStripStatusLabel();
            statusStrip = new StatusStrip();
            toolStripStatusLabel2 = new ToolStripStatusLabel();
            labelSerialStatus = new ToolStripStatusLabel();
            toolStripStatusLabel3 = new ToolStripStatusLabel();
            labelCaptureStatus = new ToolStripStatusLabel();
            optPanel = new FlowLayoutPanel();
            logPanel = new Panel();
            logTitleLabel = new Label();
            clsLogBtn = new Button();
            runStopBtn = new Button();
            scriptContainer = new Panel();
            scriptTitleLabel = new Label();
            bottomPanel = new TableLayoutPanel();
            lblTitleSerial = new Label();
            tblSerialPort = new TableLayoutPanel();
            lblTitleVideo = new Label();
            tblVideoSource = new TableLayoutPanel();
            comboVideoSource = new ComboBox();
            btnCaptureToggle = new Button();
            btnOpenCaptureConsole = new Button();
            lblTitleController = new Label();
            tblController = new TableLayoutPanel();
            menuStrip.SuspendLayout();
            grpFirmware.SuspendLayout();
            statusStrip.SuspendLayout();
            optPanel.SuspendLayout();
            logPanel.SuspendLayout();
            scriptContainer.SuspendLayout();
            bottomPanel.SuspendLayout();
            tblSerialPort.SuspendLayout();
            tblVideoSource.SuspendLayout();
            tblController.SuspendLayout();
            SuspendLayout();
            // 
            // menuStrip
            // 
            menuStrip.Font = new Font("微软雅黑", 9F);
            menuStrip.ImageScalingSize = new Size(20, 20);
            menuStrip.Items.AddRange(new ToolStripItem[] { 文件ToolStripMenuItem, 編輯ToolStripMenuItem, 脚本ToolStripMenuItem, 搜图ToolStripMenuItem, 设置ToolStripMenuItem, 蓝牙ToolStripMenuItem, eSP32ToolStripMenuItem, 画图ToolStripMenuItem, 帮助ToolStripMenuItem });
            menuStrip.Location = new Point(1, 1);
            menuStrip.Name = "menuStrip";
            menuStrip.Padding = new Padding(7, 2, 0, 2);
            menuStrip.RenderMode = ToolStripRenderMode.Professional;
            menuStrip.Size = new Size(1138, 28);
            menuStrip.TabIndex = 0;
            menuStrip.Text = "menuStrip1";
            // 
            // 文件ToolStripMenuItem
            // 
            文件ToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { 新建ToolStripMenuItem, 打开ToolStripMenuItem, 保存ToolStripMenuItem, 另存为ToolStripMenuItem, 关闭ToolStripMenuItem, toolStripSeparator1, 退出ToolStripMenuItem });
            文件ToolStripMenuItem.Name = "文件ToolStripMenuItem";
            文件ToolStripMenuItem.Size = new Size(53, 24);
            文件ToolStripMenuItem.Text = "文件";
            // 
            // 新建ToolStripMenuItem
            // 
            新建ToolStripMenuItem.Name = "新建ToolStripMenuItem";
            新建ToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.N;
            新建ToolStripMenuItem.Size = new Size(236, 26);
            新建ToolStripMenuItem.Text = "新建";
            新建ToolStripMenuItem.Click += 新建ToolStripMenuItem_Click;
            // 
            // 打开ToolStripMenuItem
            // 
            打开ToolStripMenuItem.Name = "打开ToolStripMenuItem";
            打开ToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.O;
            打开ToolStripMenuItem.Size = new Size(236, 26);
            打开ToolStripMenuItem.Text = "打开";
            打开ToolStripMenuItem.Click += 打开ToolStripMenuItem_Click;
            // 
            // 保存ToolStripMenuItem
            // 
            保存ToolStripMenuItem.Name = "保存ToolStripMenuItem";
            保存ToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.S;
            保存ToolStripMenuItem.Size = new Size(236, 26);
            保存ToolStripMenuItem.Text = "保存";
            保存ToolStripMenuItem.Click += 保存ToolStripMenuItem_Click;
            // 
            // 另存为ToolStripMenuItem
            // 
            另存为ToolStripMenuItem.Name = "另存为ToolStripMenuItem";
            另存为ToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.Shift | Keys.S;
            另存为ToolStripMenuItem.Size = new Size(236, 26);
            另存为ToolStripMenuItem.Text = "另存为";
            另存为ToolStripMenuItem.Click += 另存为ToolStripMenuItem_Click;
            // 
            // 关闭ToolStripMenuItem
            // 
            关闭ToolStripMenuItem.Name = "关闭ToolStripMenuItem";
            关闭ToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.W;
            关闭ToolStripMenuItem.Size = new Size(236, 26);
            关闭ToolStripMenuItem.Text = "关闭";
            关闭ToolStripMenuItem.Click += 关闭ToolStripMenuItem_Click;
            // 
            // toolStripSeparator1
            // 
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new Size(233, 6);
            // 
            // 退出ToolStripMenuItem
            // 
            退出ToolStripMenuItem.Name = "退出ToolStripMenuItem";
            退出ToolStripMenuItem.ShortcutKeyDisplayString = "";
            退出ToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.Q;
            退出ToolStripMenuItem.Size = new Size(236, 26);
            退出ToolStripMenuItem.Text = "退出";
            退出ToolStripMenuItem.Click += 退出ToolStripMenuItem_Click;
            // 
            // 編輯ToolStripMenuItem
            // 
            編輯ToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { 查找替換ToolStripMenuItem, 查找下一个ToolStripMenuItem, 注释取消注释ToolStripMenuItem });
            編輯ToolStripMenuItem.Name = "編輯ToolStripMenuItem";
            編輯ToolStripMenuItem.Size = new Size(53, 24);
            編輯ToolStripMenuItem.Text = "编辑";
            編輯ToolStripMenuItem.Visible = false;
            // 
            // 查找替換ToolStripMenuItem
            // 
            查找替換ToolStripMenuItem.Name = "查找替換ToolStripMenuItem";
            查找替換ToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.F;
            查找替換ToolStripMenuItem.Size = new Size(240, 26);
            查找替換ToolStripMenuItem.Text = "查找替換";
            查找替換ToolStripMenuItem.Click += 查找替換ToolStripMenuItem_Click;
            // 
            // 查找下一个ToolStripMenuItem
            // 
            查找下一个ToolStripMenuItem.Name = "查找下一个ToolStripMenuItem";
            查找下一个ToolStripMenuItem.ShortcutKeys = Keys.F3;
            查找下一个ToolStripMenuItem.Size = new Size(240, 26);
            查找下一个ToolStripMenuItem.Text = "查找下一个";
            查找下一个ToolStripMenuItem.Click += 查找下一个ToolStripMenuItem_Click;
            // 
            // 注释取消注释ToolStripMenuItem
            // 
            注释取消注释ToolStripMenuItem.Name = "注释取消注释ToolStripMenuItem";
            注释取消注释ToolStripMenuItem.ShortcutKeyDisplayString = "Ctrl+/";
            注释取消注释ToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.Oem2;
            注释取消注释ToolStripMenuItem.Size = new Size(240, 26);
            注释取消注释ToolStripMenuItem.Text = "注释/取消注释";
            注释取消注释ToolStripMenuItem.Click += 注释取消注释ToolStripMenuItem_Click;
            // 
            // 脚本ToolStripMenuItem
            // 
            脚本ToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { 编译ToolStripMenuItem, 执行ToolStripMenuItem });
            脚本ToolStripMenuItem.Name = "脚本ToolStripMenuItem";
            脚本ToolStripMenuItem.Size = new Size(53, 24);
            脚本ToolStripMenuItem.Text = "脚本";
            // 
            // 编译ToolStripMenuItem
            // 
            编译ToolStripMenuItem.Name = "编译ToolStripMenuItem";
            编译ToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.R;
            编译ToolStripMenuItem.Size = new Size(193, 26);
            编译ToolStripMenuItem.Text = "格式化";
            编译ToolStripMenuItem.Click += compileButton_Click;
            // 
            // 执行ToolStripMenuItem
            // 
            执行ToolStripMenuItem.Name = "执行ToolStripMenuItem";
            执行ToolStripMenuItem.ShortcutKeys = Keys.F5;
            执行ToolStripMenuItem.Size = new Size(193, 26);
            执行ToolStripMenuItem.Text = "运行";
            执行ToolStripMenuItem.Click += 执行ToolStripMenuItem_Click;
            // 
            // 搜图ToolStripMenuItem
            // 
            搜图ToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { 采集卡类型ToolStripMenuItem, 搜图说明ToolStripMenuItem, 设置环境变量ToolStripMenuItem });
            搜图ToolStripMenuItem.Name = "搜图ToolStripMenuItem";
            搜图ToolStripMenuItem.Size = new Size(53, 24);
            搜图ToolStripMenuItem.Text = "搜图";
            // 
            // 采集卡类型ToolStripMenuItem
            // 
            采集卡类型ToolStripMenuItem.Name = "采集卡类型ToolStripMenuItem";
            采集卡类型ToolStripMenuItem.Size = new Size(182, 26);
            采集卡类型ToolStripMenuItem.Text = "采集卡类型";
            // 
            // 搜图说明ToolStripMenuItem
            // 
            搜图说明ToolStripMenuItem.Name = "搜图说明ToolStripMenuItem";
            搜图说明ToolStripMenuItem.Size = new Size(182, 26);
            搜图说明ToolStripMenuItem.Text = "搜图说明";
            搜图说明ToolStripMenuItem.Click += 搜图说明ToolStripMenuItem_Click;
            // 
            // 设置环境变量ToolStripMenuItem
            // 
            设置环境变量ToolStripMenuItem.Name = "设置环境变量ToolStripMenuItem";
            设置环境变量ToolStripMenuItem.Size = new Size(182, 26);
            设置环境变量ToolStripMenuItem.Text = "设置环境变量";
            设置环境变量ToolStripMenuItem.Click += 设置环境变量ToolStripMenuItem_Click;
            // 
            // 设置ToolStripMenuItem
            // 
            设置ToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { 推送设置ToolStripMenuItem, 显示调试信息ToolStripMenuItem, 烧录自动运行ToolStripMenuItem, 显示折叠ToolStripMenuItem, 代码自动补全ToolStripMenuItem, 深色模式ToolStripMenuItem, 高精度模式ToolStripMenuItem });
            设置ToolStripMenuItem.Name = "设置ToolStripMenuItem";
            设置ToolStripMenuItem.Size = new Size(53, 24);
            设置ToolStripMenuItem.Text = "设置";
            // 
            // 推送设置ToolStripMenuItem
            // 
            推送设置ToolStripMenuItem.Name = "推送设置ToolStripMenuItem";
            推送设置ToolStripMenuItem.Size = new Size(182, 26);
            推送设置ToolStripMenuItem.Text = "推送设置";
            推送设置ToolStripMenuItem.Click += 推送设置ToolStripMenuItem_Click;
            // 
            // 显示调试信息ToolStripMenuItem
            // 
            显示调试信息ToolStripMenuItem.Name = "显示调试信息ToolStripMenuItem";
            显示调试信息ToolStripMenuItem.Size = new Size(182, 26);
            显示调试信息ToolStripMenuItem.Text = "显示调试信息";
            显示调试信息ToolStripMenuItem.Click += 显示调试信息ToolStripMenuItem_Click;
            // 
            // 烧录自动运行ToolStripMenuItem
            // 
            烧录自动运行ToolStripMenuItem.Checked = true;
            烧录自动运行ToolStripMenuItem.CheckState = CheckState.Checked;
            烧录自动运行ToolStripMenuItem.Name = "烧录自动运行ToolStripMenuItem";
            烧录自动运行ToolStripMenuItem.Size = new Size(182, 26);
            烧录自动运行ToolStripMenuItem.Text = "烧录自动运行";
            烧录自动运行ToolStripMenuItem.Click += 脚本自动运行ToolStripMenuItem_Click;
            // 
            // 显示折叠ToolStripMenuItem
            // 
            显示折叠ToolStripMenuItem.Checked = true;
            显示折叠ToolStripMenuItem.CheckState = CheckState.Checked;
            显示折叠ToolStripMenuItem.Name = "显示折叠ToolStripMenuItem";
            显示折叠ToolStripMenuItem.Size = new Size(182, 26);
            显示折叠ToolStripMenuItem.Text = "显示折叠";
            显示折叠ToolStripMenuItem.Click += 显示折叠ToolStripMenuItem_Click;
            // 
            // 代码自动补全ToolStripMenuItem
            // 
            代码自动补全ToolStripMenuItem.Checked = true;
            代码自动补全ToolStripMenuItem.CheckOnClick = true;
            代码自动补全ToolStripMenuItem.CheckState = CheckState.Checked;
            代码自动补全ToolStripMenuItem.Name = "代码自动补全ToolStripMenuItem";
            代码自动补全ToolStripMenuItem.Size = new Size(182, 26);
            代码自动补全ToolStripMenuItem.Text = "代码自动补全";
            代码自动补全ToolStripMenuItem.Click += 代码自动补全ToolStripMenuItem_Click;
            // 
            // 深色模式ToolStripMenuItem
            // 
            深色模式ToolStripMenuItem.CheckOnClick = true;
            深色模式ToolStripMenuItem.Name = "深色模式ToolStripMenuItem";
            深色模式ToolStripMenuItem.Size = new Size(182, 26);
            深色模式ToolStripMenuItem.Text = "深色模式";
            深色模式ToolStripMenuItem.Click += 深色模式ToolStripMenuItem_Click;
            // 
            // 高精度模式ToolStripMenuItem
            // 
            高精度模式ToolStripMenuItem.CheckOnClick = true;
            高精度模式ToolStripMenuItem.Name = "高精度模式ToolStripMenuItem";
            高精度模式ToolStripMenuItem.Size = new Size(182, 26);
            高精度模式ToolStripMenuItem.Text = "高精度模式";
            高精度模式ToolStripMenuItem.Click += 高精度模式ToolStripMenuItem_Click;
            // 
            // 蓝牙ToolStripMenuItem
            // 
            蓝牙ToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { 蓝牙设备驱动配置ToolStripMenuItem });
            蓝牙ToolStripMenuItem.Name = "蓝牙ToolStripMenuItem";
            蓝牙ToolStripMenuItem.Size = new Size(53, 24);
            蓝牙ToolStripMenuItem.Text = "蓝牙";
            蓝牙ToolStripMenuItem.Visible = false;
            // 
            // 蓝牙设备驱动配置ToolStripMenuItem
            // 
            蓝牙设备驱动配置ToolStripMenuItem.Name = "蓝牙设备驱动配置ToolStripMenuItem";
            蓝牙设备驱动配置ToolStripMenuItem.Size = new Size(182, 26);
            蓝牙设备驱动配置ToolStripMenuItem.Text = "设备驱动配置";
            蓝牙设备驱动配置ToolStripMenuItem.Click += 设备驱动配置ToolStripMenuItem_Click;
            // 
            // eSP32ToolStripMenuItem
            // 
            eSP32ToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { 手柄设置ToolStripMenuItem, 取消配对ToolStripMenuItem });
            eSP32ToolStripMenuItem.Name = "eSP32ToolStripMenuItem";
            eSP32ToolStripMenuItem.Size = new Size(67, 24);
            eSP32ToolStripMenuItem.Text = "ESP32";
            // 
            // 手柄设置ToolStripMenuItem
            // 
            手柄设置ToolStripMenuItem.Name = "手柄设置ToolStripMenuItem";
            手柄设置ToolStripMenuItem.Size = new Size(152, 26);
            手柄设置ToolStripMenuItem.Text = "手柄设置";
            手柄设置ToolStripMenuItem.Click += 手柄设置ToolStripMenuItem_Click;
            // 
            // 取消配对ToolStripMenuItem
            // 
            取消配对ToolStripMenuItem.Name = "取消配对ToolStripMenuItem";
            取消配对ToolStripMenuItem.Size = new Size(152, 26);
            取消配对ToolStripMenuItem.Text = "取消配对";
            取消配对ToolStripMenuItem.Click += 取消配对ToolStripMenuItem_Click;
            // 
            // 画图ToolStripMenuItem
            // 
            画图ToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { 喷射ToolStripMenuItem, 自由画板鼠标代替摇杆ToolStripMenuItem });
            画图ToolStripMenuItem.Name = "画图ToolStripMenuItem";
            画图ToolStripMenuItem.Size = new Size(53, 24);
            画图ToolStripMenuItem.Text = "画图";
            // 
            // 喷射ToolStripMenuItem
            // 
            喷射ToolStripMenuItem.Name = "喷射ToolStripMenuItem";
            喷射ToolStripMenuItem.Size = new Size(272, 26);
            喷射ToolStripMenuItem.Text = "喷射战士";
            喷射ToolStripMenuItem.Click += 喷射ToolStripMenuItem_Click;
            // 
            // 自由画板鼠标代替摇杆ToolStripMenuItem
            // 
            自由画板鼠标代替摇杆ToolStripMenuItem.Name = "自由画板鼠标代替摇杆ToolStripMenuItem";
            自由画板鼠标代替摇杆ToolStripMenuItem.Size = new Size(272, 26);
            自由画板鼠标代替摇杆ToolStripMenuItem.Text = "自由画板（鼠标代替摇杆）";
            自由画板鼠标代替摇杆ToolStripMenuItem.Click += 自由画板鼠标代替摇杆ToolStripMenuItem_Click;
            // 
            // 帮助ToolStripMenuItem
            // 
            帮助ToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { 固件模式ToolStripMenuItem, 联机模式ToolStripMenuItem, 烧录模式ToolStripMenuItem, toolStripSeparator2, 检查更新ToolStripMenuItem, 项目源码ToolStripMenuItem, 关于ToolStripMenuItem });
            帮助ToolStripMenuItem.Name = "帮助ToolStripMenuItem";
            帮助ToolStripMenuItem.Size = new Size(53, 24);
            帮助ToolStripMenuItem.Text = "帮助";
            // 
            // 固件模式ToolStripMenuItem
            // 
            固件模式ToolStripMenuItem.Name = "固件模式ToolStripMenuItem";
            固件模式ToolStripMenuItem.Size = new Size(152, 26);
            固件模式ToolStripMenuItem.Text = "固件模式";
            固件模式ToolStripMenuItem.Click += 固件模式ToolStripMenuItem_Click;
            // 
            // 联机模式ToolStripMenuItem
            // 
            联机模式ToolStripMenuItem.Name = "联机模式ToolStripMenuItem";
            联机模式ToolStripMenuItem.Size = new Size(152, 26);
            联机模式ToolStripMenuItem.Text = "联机模式";
            联机模式ToolStripMenuItem.Click += 联机模式ToolStripMenuItem_Click;
            // 
            // 烧录模式ToolStripMenuItem
            // 
            烧录模式ToolStripMenuItem.Name = "烧录模式ToolStripMenuItem";
            烧录模式ToolStripMenuItem.Size = new Size(152, 26);
            烧录模式ToolStripMenuItem.Text = "烧录模式";
            烧录模式ToolStripMenuItem.Click += 烧录模式ToolStripMenuItem_Click;
            // 
            // toolStripSeparator2
            // 
            toolStripSeparator2.Name = "toolStripSeparator2";
            toolStripSeparator2.Size = new Size(149, 6);
            // 
            // 检查更新ToolStripMenuItem
            // 
            检查更新ToolStripMenuItem.Name = "检查更新ToolStripMenuItem";
            检查更新ToolStripMenuItem.Size = new Size(152, 26);
            检查更新ToolStripMenuItem.Text = "检查更新";
            检查更新ToolStripMenuItem.Click += 检查更新ToolStripMenuItem_Click;
            // 
            // 项目源码ToolStripMenuItem
            // 
            项目源码ToolStripMenuItem.Name = "项目源码ToolStripMenuItem";
            项目源码ToolStripMenuItem.Size = new Size(152, 26);
            项目源码ToolStripMenuItem.Text = "项目源码";
            项目源码ToolStripMenuItem.Click += 项目源码ToolStripMenuItem_Click;
            // 
            // 关于ToolStripMenuItem
            // 
            关于ToolStripMenuItem.Name = "关于ToolStripMenuItem";
            关于ToolStripMenuItem.Size = new Size(152, 26);
            关于ToolStripMenuItem.Text = "关于";
            关于ToolStripMenuItem.Click += 关于ToolStripMenuItem_Click;
            // 
            // buttonScriptHelp
            // 
            buttonScriptHelp.AccessibleName = "脚本语法帮助";
            buttonScriptHelp.FlatAppearance.BorderSize = 1;
            buttonScriptHelp.FlatStyle = FlatStyle.Flat;
            buttonScriptHelp.Font = new Font("微软雅黑", 9F);
            buttonScriptHelp.Location = new Point(3, 303);
            buttonScriptHelp.Name = "buttonScriptHelp";
            buttonScriptHelp.Size = new Size(113, 40);
            buttonScriptHelp.TabIndex = 6;
            buttonScriptHelp.Text = "脚本语法";
            buttonScriptHelp.UseVisualStyleBackColor = true;
            buttonScriptHelp.Click += 脚本语法ToolStripMenuItem_Click;
            // 
            // labelTimer
            // 
            labelTimer.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            labelTimer.BackColor = Color.FromArgb(45, 44, 38);
            labelTimer.BorderStyle = BorderStyle.Fixed3D;
            labelTimer.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            labelTimer.ForeColor = Color.White;
            labelTimer.Location = new Point(3, 470);
            labelTimer.Name = "labelTimer";
            labelTimer.Size = new Size(238, 68);
            labelTimer.TabIndex = 35;
            labelTimer.Text = "00:00:00";
            labelTimer.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // logTxtBox
            // 
            logTxtBox.AccessibleName = "输出窗口";
            logTxtBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            logTxtBox.BackColor = Color.FromArgb(45, 44, 38);
            logTxtBox.BorderStyle = BorderStyle.FixedSingle;
            logTxtBox.ForeColor = Color.White;
            logTxtBox.Location = new Point(3, 24);
            logTxtBox.Name = "logTxtBox";
            logTxtBox.ReadOnly = true;
            logTxtBox.Size = new Size(474, 446);
            logTxtBox.TabIndex = 34;
            logTxtBox.Text = "";
            logTxtBox.WordWrap = false;
            // 
            // buttonRecordPause
            // 
            buttonRecordPause.AccessibleName = "暂停录制脚本";
            buttonRecordPause.Dock = DockStyle.Fill;
            buttonRecordPause.Enabled = false;
            buttonRecordPause.FlatAppearance.BorderSize = 0;
            buttonRecordPause.FlatStyle = FlatStyle.Flat;
            buttonRecordPause.Font = new Font("微软雅黑", 9F);
            buttonRecordPause.Location = new Point(189, 91);
            buttonRecordPause.Name = "buttonRecordPause";
            buttonRecordPause.Size = new Size(174, 41);
            buttonRecordPause.TabIndex = 6;
            buttonRecordPause.Text = "暂停录制";
            buttonRecordPause.UseVisualStyleBackColor = true;
            buttonRecordPause.Click += buttonRecordPause_Click;
            // 
            // buttonRecord
            // 
            buttonRecord.AccessibleName = "录制脚本";
            buttonRecord.Dock = DockStyle.Fill;
            buttonRecord.FlatAppearance.BorderSize = 0;
            buttonRecord.FlatStyle = FlatStyle.Flat;
            buttonRecord.Font = new Font("微软雅黑", 9F);
            buttonRecord.Location = new Point(9, 91);
            buttonRecord.Name = "buttonRecord";
            buttonRecord.Size = new Size(174, 41);
            buttonRecord.TabIndex = 5;
            buttonRecord.Text = "录制脚本";
            buttonRecord.UseVisualStyleBackColor = true;
            buttonRecord.Click += buttonRecord_Click;
            // 
            // buttonFlashClear
            // 
            buttonFlashClear.AccessibleName = "清除固件已有脚本";
            buttonFlashClear.FlatAppearance.BorderSize = 0;
            buttonFlashClear.FlatStyle = FlatStyle.Flat;
            buttonFlashClear.Font = new Font("微软雅黑", 9F);
            buttonFlashClear.Location = new Point(3, 141);
            buttonFlashClear.Name = "buttonFlashClear";
            buttonFlashClear.Size = new Size(102, 40);
            buttonFlashClear.TabIndex = 4;
            buttonFlashClear.Text = "清除烧录";
            buttonFlashClear.UseVisualStyleBackColor = true;
            buttonFlashClear.Click += buttonFlashClear_Click;
            // 
            // buttonFlash
            // 
            buttonFlash.AccessibleName = "编译固件并烧录";
            buttonFlash.FlatAppearance.BorderSize = 0;
            buttonFlash.FlatStyle = FlatStyle.Flat;
            buttonFlash.Font = new Font("微软雅黑", 9F);
            buttonFlash.Location = new Point(3, 95);
            buttonFlash.Name = "buttonFlash";
            buttonFlash.Size = new Size(102, 40);
            buttonFlash.TabIndex = 1;
            buttonFlash.Text = "编译烧录";
            buttonFlash.UseVisualStyleBackColor = true;
            buttonFlash.Click += buttonFlash_Click;
            // 
            // buttonRemoteStop
            // 
            buttonRemoteStop.AccessibleName = "停止运行烧录脚本";
            buttonRemoteStop.FlatAppearance.BorderSize = 0;
            buttonRemoteStop.FlatStyle = FlatStyle.Flat;
            buttonRemoteStop.Font = new Font("微软雅黑", 9F);
            buttonRemoteStop.Location = new Point(3, 49);
            buttonRemoteStop.Name = "buttonRemoteStop";
            buttonRemoteStop.Size = new Size(102, 40);
            buttonRemoteStop.TabIndex = 3;
            buttonRemoteStop.Text = "远程停止";
            buttonRemoteStop.UseVisualStyleBackColor = true;
            buttonRemoteStop.Click += buttonRemoteStop_Click;
            // 
            // buttonRemoteStart
            // 
            buttonRemoteStart.AccessibleName = "运行烧录脚本";
            buttonRemoteStart.FlatAppearance.BorderSize = 0;
            buttonRemoteStart.FlatStyle = FlatStyle.Flat;
            buttonRemoteStart.Font = new Font("微软雅黑", 9F);
            buttonRemoteStart.Location = new Point(3, 3);
            buttonRemoteStart.Name = "buttonRemoteStart";
            buttonRemoteStart.Size = new Size(102, 40);
            buttonRemoteStart.TabIndex = 2;
            buttonRemoteStart.Text = "远程运行";
            buttonRemoteStart.UseVisualStyleBackColor = true;
            buttonRemoteStart.Click += buttonRemoteStart_Click;
            // 
            // grpFirmware
            // 
            grpFirmware.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            grpFirmware.Controls.Add(genFwButton);
            grpFirmware.Controls.Add(comboBoxBoardType);
            grpFirmware.Location = new Point(3, 187);
            grpFirmware.Name = "grpFirmware";
            grpFirmware.Size = new Size(113, 110);
            grpFirmware.TabIndex = 5;
            grpFirmware.TabStop = false;
            grpFirmware.Text = "固件";
            // 
            // genFwButton
            // 
            genFwButton.AccessibleName = "生成固件";
            genFwButton.FlatAppearance.BorderSize = 0;
            genFwButton.FlatStyle = FlatStyle.Flat;
            genFwButton.Font = new Font("微软雅黑", 9F);
            genFwButton.Location = new Point(8, 57);
            genFwButton.Name = "genFwButton";
            genFwButton.Size = new Size(94, 34);
            genFwButton.TabIndex = 0;
            genFwButton.Text = "生成";
            genFwButton.UseVisualStyleBackColor = true;
            genFwButton.Click += buttonGenerateFirmware_Click;
            // 
            // comboBoxBoardType
            // 
            comboBoxBoardType.AccessibleName = "固件选择生成用";
            comboBoxBoardType.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxBoardType.FormattingEnabled = true;
            comboBoxBoardType.Location = new Point(8, 23);
            comboBoxBoardType.Name = "comboBoxBoardType";
            comboBoxBoardType.Size = new Size(94, 28);
            comboBoxBoardType.TabIndex = 5;
            // 
            // buttonShowController
            // 
            buttonShowController.AccessibleName = "打开虚拟手柄";
            buttonShowController.Dock = DockStyle.Fill;
            buttonShowController.FlatAppearance.BorderSize = 0;
            buttonShowController.FlatStyle = FlatStyle.Flat;
            buttonShowController.Font = new Font("微软雅黑", 9F);
            buttonShowController.Location = new Point(189, 9);
            buttonShowController.Name = "buttonShowController";
            buttonShowController.Size = new Size(174, 30);
            buttonShowController.TabIndex = 3;
            buttonShowController.Text = "连接";
            buttonShowController.UseVisualStyleBackColor = true;
            buttonShowController.Click += buttonShowController_Click;
            // 
            // editorHost
            // 
            editorHost.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            editorHost.Font = new Font("Consolas", 9F);
            editorHost.Location = new Point(0, 23);
            editorHost.Name = "editorHost";
            editorHost.Size = new Size(511, 515);
            editorHost.TabIndex = 0;
            editorHost.Text = "elementHost1";
            // 
            // ComPort
            // 
            ComPort.AccessibleName = "选择串口";
            ComPort.Dock = DockStyle.Fill;
            ComPort.FormattingEnabled = true;
            ComPort.Location = new Point(9, 73);
            ComPort.Name = "ComPort";
            ComPort.Size = new Size(173, 28);
            ComPort.TabIndex = 34;
            ComPort.Text = "下拉选择串口";
            ComPort.DropDown += ComPort_DropDown;
            // 
            // buttonSerialPortConnect
            // 
            buttonSerialPortConnect.AccessibleName = "手动连接串口";
            buttonSerialPortConnect.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            buttonSerialPortConnect.FlatAppearance.BorderSize = 0;
            buttonSerialPortConnect.FlatStyle = FlatStyle.Flat;
            buttonSerialPortConnect.Font = new Font("微软雅黑", 9F);
            buttonSerialPortConnect.Location = new Point(188, 73);
            buttonSerialPortConnect.Name = "buttonSerialPortConnect";
            buttonSerialPortConnect.Size = new Size(174, 28);
            buttonSerialPortConnect.TabIndex = 30;
            buttonSerialPortConnect.Text = "手动连接";
            buttonSerialPortConnect.UseVisualStyleBackColor = true;
            buttonSerialPortConnect.Click += buttonSerialPortConnect_Click;
            // 
            // buttonSerialPortSearch
            // 
            buttonSerialPortSearch.AccessibleName = "自动连接串口";
            tblSerialPort.SetColumnSpan(buttonSerialPortSearch, 2);
            buttonSerialPortSearch.Dock = DockStyle.Fill;
            buttonSerialPortSearch.FlatAppearance.BorderSize = 0;
            buttonSerialPortSearch.FlatStyle = FlatStyle.Flat;
            buttonSerialPortSearch.Font = new Font("微软雅黑", 9F);
            buttonSerialPortSearch.Location = new Point(9, 9);
            buttonSerialPortSearch.Name = "buttonSerialPortSearch";
            buttonSerialPortSearch.Size = new Size(353, 58);
            buttonSerialPortSearch.TabIndex = 29;
            buttonSerialPortSearch.Text = "自动连接(推荐)";
            buttonSerialPortSearch.UseVisualStyleBackColor = true;
            buttonSerialPortSearch.Click += buttonSerialPortSearch_Click;
            // 
            // buttonControllerHelp
            // 
            buttonControllerHelp.AccessibleName = "虚拟手柄帮助";
            buttonControllerHelp.Dock = DockStyle.Fill;
            buttonControllerHelp.FlatAppearance.BorderSize = 0;
            buttonControllerHelp.FlatStyle = FlatStyle.Flat;
            buttonControllerHelp.Font = new Font("微软雅黑", 9F);
            buttonControllerHelp.Location = new Point(189, 45);
            buttonControllerHelp.Name = "buttonControllerHelp";
            buttonControllerHelp.Size = new Size(174, 40);
            buttonControllerHelp.TabIndex = 33;
            buttonControllerHelp.Text = "帮助";
            buttonControllerHelp.UseVisualStyleBackColor = true;
            buttonControllerHelp.Click += buttonControllerHelp_Click;
            // 
            // buttonKeyMapping
            // 
            buttonKeyMapping.AccessibleName = "手柄映射配置";
            buttonKeyMapping.Dock = DockStyle.Fill;
            buttonKeyMapping.FlatAppearance.BorderSize = 0;
            buttonKeyMapping.FlatStyle = FlatStyle.Flat;
            buttonKeyMapping.Font = new Font("微软雅黑", 9F);
            buttonKeyMapping.Location = new Point(9, 45);
            buttonKeyMapping.Name = "buttonKeyMapping";
            buttonKeyMapping.Size = new Size(174, 40);
            buttonKeyMapping.TabIndex = 4;
            buttonKeyMapping.Text = "按键映射";
            buttonKeyMapping.UseVisualStyleBackColor = true;
            buttonKeyMapping.Click += buttonKeyMapping_Click;
            // 
            // comboInputMode
            // 
            comboInputMode.AccessibleName = "输入模式";
            comboInputMode.Dock = DockStyle.Fill;
            comboInputMode.DropDownStyle = ComboBoxStyle.DropDownList;
            comboInputMode.Location = new Point(9, 9);
            comboInputMode.Name = "comboInputMode";
            comboInputMode.Size = new Size(174, 28);
            comboInputMode.TabIndex = 7;
            comboInputMode.SelectedIndexChanged += comboInputMode_SelectedIndexChanged;
            // 
            // openFileDialog
            // 
            openFileDialog.Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
            openFileDialog.RestoreDirectory = true;
            openFileDialog.Title = "打开";
            // 
            // saveFileDialog
            // 
            saveFileDialog.Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
            saveFileDialog.RestoreDirectory = true;
            saveFileDialog.Title = "另存为";
            // 
            // toolStripStatusLabel1
            // 
            toolStripStatusLabel1.AutoSize = false;
            toolStripStatusLabel1.Name = "toolStripStatusLabel1";
            toolStripStatusLabel1.Size = new Size(200, 20);
            toolStripStatusLabel1.Text = "LOG";
            // 
            // statusStrip
            // 
            statusStrip.ImageScalingSize = new Size(20, 20);
            statusStrip.Items.AddRange(new ToolStripItem[] { toolStripStatusLabel1, toolStripStatusLabel2, labelSerialStatus, toolStripStatusLabel3, labelCaptureStatus });
            statusStrip.Location = new Point(1, 744);
            statusStrip.Name = "statusStrip";
            statusStrip.Size = new Size(1138, 26);
            statusStrip.TabIndex = 3;
            statusStrip.Text = "statusStrip1";
            // 
            // toolStripStatusLabel2
            // 
            toolStripStatusLabel2.AutoSize = false;
            toolStripStatusLabel2.Name = "toolStripStatusLabel2";
            toolStripStatusLabel2.Size = new Size(100, 20);
            // 
            // labelSerialStatus
            // 
            labelSerialStatus.AccessibleName = "单片机连接状态";
            labelSerialStatus.AutoSize = false;
            labelSerialStatus.BackColor = Color.FromArgb(235, 234, 229);
            labelSerialStatus.ForeColor = Color.FromArgb(140, 139, 132);
            labelSerialStatus.Name = "labelSerialStatus";
            labelSerialStatus.Size = new Size(120, 20);
            labelSerialStatus.Text = "单片机未连接";
            // 
            // toolStripStatusLabel3
            // 
            toolStripStatusLabel3.AutoSize = false;
            toolStripStatusLabel3.Name = "toolStripStatusLabel3";
            toolStripStatusLabel3.Size = new Size(20, 20);
            // 
            // labelCaptureStatus
            // 
            labelCaptureStatus.AccessibleName = "采集卡连接状态";
            labelCaptureStatus.AutoSize = false;
            labelCaptureStatus.BackColor = Color.FromArgb(235, 234, 229);
            labelCaptureStatus.ForeColor = Color.FromArgb(140, 139, 132);
            labelCaptureStatus.Name = "labelCaptureStatus";
            labelCaptureStatus.Size = new Size(99, 20);
            labelCaptureStatus.Text = "采集卡未连接";
            // 
            // optPanel
            // 
            optPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            optPanel.Controls.Add(buttonRemoteStart);
            optPanel.Controls.Add(buttonRemoteStop);
            optPanel.Controls.Add(buttonFlash);
            optPanel.Controls.Add(buttonFlashClear);
            optPanel.Controls.Add(grpFirmware);
            optPanel.Controls.Add(buttonScriptHelp);
            optPanel.Location = new Point(1016, 155);
            optPanel.Name = "optPanel";
            optPanel.Size = new Size(122, 418);
            optPanel.TabIndex = 35;
            // 
            // logPanel
            // 
            logPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            logPanel.Controls.Add(logTitleLabel);
            logPanel.Controls.Add(clsLogBtn);
            logPanel.Controls.Add(runStopBtn);
            logPanel.Controls.Add(logTxtBox);
            logPanel.Controls.Add(labelTimer);
            logPanel.Location = new Point(7, 32);
            logPanel.Name = "logPanel";
            logPanel.Size = new Size(480, 541);
            logPanel.TabIndex = 37;
            // 
            // logTitleLabel
            // 
            logTitleLabel.AutoSize = true;
            logTitleLabel.Location = new Point(3, 3);
            logTitleLabel.Name = "logTitleLabel";
            logTitleLabel.Size = new Size(37, 20);
            logTitleLabel.TabIndex = 38;
            logTitleLabel.Text = "输出";
            // 
            // clsLogBtn
            // 
            clsLogBtn.AccessibleName = "清除日志输出";
            clsLogBtn.BackColor = Color.Transparent;
            clsLogBtn.BackgroundImage = (Image)resources.GetObject("clsLogBtn.BackgroundImage");
            clsLogBtn.BackgroundImageLayout = ImageLayout.Stretch;
            clsLogBtn.FlatAppearance.BorderSize = 0;
            clsLogBtn.FlatStyle = FlatStyle.Flat;
            clsLogBtn.Location = new Point(454, 3);
            clsLogBtn.Name = "clsLogBtn";
            clsLogBtn.Size = new Size(20, 20);
            clsLogBtn.TabIndex = 38;
            clsLogBtn.UseVisualStyleBackColor = false;
            clsLogBtn.Click += clsLogBtn_Click;
            clsLogBtn.MouseHover += clsLogBtn_MouseHover;
            // 
            // runStopBtn
            // 
            runStopBtn.AccessibleName = "运行或停止脚本脚本";
            runStopBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            runStopBtn.BackColor = Color.FromArgb(31, 138, 101);
            runStopBtn.BackgroundImageLayout = ImageLayout.Stretch;
            runStopBtn.FlatAppearance.BorderSize = 0;
            runStopBtn.FlatStyle = FlatStyle.Flat;
            runStopBtn.Font = new Font("微软雅黑", 12F, FontStyle.Bold);
            runStopBtn.ForeColor = Color.White;
            runStopBtn.Location = new Point(247, 470);
            runStopBtn.Name = "runStopBtn";
            runStopBtn.Size = new Size(230, 68);
            runStopBtn.TabIndex = 37;
            runStopBtn.Text = "运行脚本";
            runStopBtn.UseVisualStyleBackColor = false;
            runStopBtn.Click += buttonScriptRunStop_Click;
            // 
            // scriptContainer
            // 
            scriptContainer.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            scriptContainer.Controls.Add(scriptTitleLabel);
            scriptContainer.Controls.Add(editorHost);
            scriptContainer.Location = new Point(496, 32);
            scriptContainer.Name = "scriptContainer";
            scriptContainer.Size = new Size(514, 541);
            scriptContainer.TabIndex = 38;
            // 
            // scriptTitleLabel
            // 
            scriptTitleLabel.AutoSize = true;
            scriptTitleLabel.Location = new Point(-3, 2);
            scriptTitleLabel.Name = "scriptTitleLabel";
            scriptTitleLabel.Size = new Size(79, 20);
            scriptTitleLabel.TabIndex = 0;
            scriptTitleLabel.Text = "未命名脚本";
            // 
            // bottomPanel
            // 
            bottomPanel.ColumnCount = 3;
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33333F));
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33333F));
            bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33334F));
            bottomPanel.Controls.Add(lblTitleSerial, 0, 0);
            bottomPanel.Controls.Add(tblSerialPort, 0, 1);
            bottomPanel.Controls.Add(lblTitleVideo, 1, 0);
            bottomPanel.Controls.Add(tblVideoSource, 1, 1);
            bottomPanel.Controls.Add(lblTitleController, 2, 0);
            bottomPanel.Controls.Add(tblController, 2, 1);
            bottomPanel.Dock = DockStyle.Bottom;
            bottomPanel.Location = new Point(1, 576);
            bottomPanel.Name = "bottomPanel";
            bottomPanel.RowCount = 2;
            bottomPanel.RowStyles.Add(new RowStyle());
            bottomPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            bottomPanel.Size = new Size(1138, 168);
            bottomPanel.TabIndex = 39;
            // 
            // lblTitleSerial
            // 
            lblTitleSerial.AutoSize = true;
            lblTitleSerial.Dock = DockStyle.Fill;
            lblTitleSerial.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
            lblTitleSerial.Location = new Point(3, 0);
            lblTitleSerial.Name = "lblTitleSerial";
            lblTitleSerial.Size = new Size(373, 19);
            lblTitleSerial.TabIndex = 0;
            lblTitleSerial.Text = "串口连接";
            // 
            // tblSerialPort
            // 
            tblSerialPort.BackColor = Color.FromArgb(230, 229, 224);
            tblSerialPort.ColumnCount = 2;
            tblSerialPort.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tblSerialPort.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tblSerialPort.Controls.Add(buttonSerialPortSearch, 0, 0);
            tblSerialPort.Controls.Add(ComPort, 0, 1);
            tblSerialPort.Controls.Add(buttonSerialPortConnect, 1, 1);
            tblSerialPort.Dock = DockStyle.Fill;
            tblSerialPort.Location = new Point(4, 23);
            tblSerialPort.Margin = new Padding(4);
            tblSerialPort.Name = "tblSerialPort";
            tblSerialPort.Padding = new Padding(6);
            tblSerialPort.RowCount = 2;
            tblSerialPort.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tblSerialPort.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tblSerialPort.Size = new Size(371, 141);
            tblSerialPort.TabIndex = 0;
            // 
            // lblTitleVideo
            // 
            lblTitleVideo.AutoSize = true;
            lblTitleVideo.Dock = DockStyle.Fill;
            lblTitleVideo.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
            lblTitleVideo.Location = new Point(382, 0);
            lblTitleVideo.Name = "lblTitleVideo";
            lblTitleVideo.Size = new Size(373, 19);
            lblTitleVideo.TabIndex = 0;
            lblTitleVideo.Text = "视频源";
            // 
            // tblVideoSource
            // 
            tblVideoSource.BackColor = Color.FromArgb(230, 229, 224);
            tblVideoSource.ColumnCount = 2;
            tblVideoSource.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tblVideoSource.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tblVideoSource.Controls.Add(comboVideoSource, 0, 0);
            tblVideoSource.Controls.Add(btnCaptureToggle, 0, 1);
            tblVideoSource.Controls.Add(btnOpenCaptureConsole, 1, 1);
            tblVideoSource.Dock = DockStyle.Fill;
            tblVideoSource.Location = new Point(383, 23);
            tblVideoSource.Margin = new Padding(4);
            tblVideoSource.Name = "tblVideoSource";
            tblVideoSource.Padding = new Padding(6);
            tblVideoSource.RowCount = 2;
            tblVideoSource.RowStyles.Add(new RowStyle());
            tblVideoSource.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tblVideoSource.Size = new Size(371, 141);
            tblVideoSource.TabIndex = 0;
            // 
            // comboVideoSource
            // 
            comboVideoSource.AccessibleName = "视频源选择";
            tblVideoSource.SetColumnSpan(comboVideoSource, 2);
            comboVideoSource.Dock = DockStyle.Fill;
            comboVideoSource.DropDownStyle = ComboBoxStyle.DropDownList;
            comboVideoSource.FormattingEnabled = true;
            comboVideoSource.Location = new Point(9, 9);
            comboVideoSource.Name = "comboVideoSource";
            comboVideoSource.Size = new Size(353, 28);
            comboVideoSource.TabIndex = 0;
            comboVideoSource.DropDown += comboVideoSource_DropDown;
            // 
            // btnCaptureToggle
            // 
            btnCaptureToggle.AccessibleName = "连接视频源";
            btnCaptureToggle.Dock = DockStyle.Fill;
            btnCaptureToggle.FlatAppearance.BorderSize = 0;
            btnCaptureToggle.FlatStyle = FlatStyle.Flat;
            btnCaptureToggle.Font = new Font("微软雅黑", 9F);
            btnCaptureToggle.Location = new Point(9, 43);
            btnCaptureToggle.Name = "btnCaptureToggle";
            btnCaptureToggle.Size = new Size(173, 89);
            btnCaptureToggle.TabIndex = 1;
            btnCaptureToggle.Text = "连接视频源";
            btnCaptureToggle.UseVisualStyleBackColor = true;
            btnCaptureToggle.Click += btnCaptureToggle_Click;
            // 
            // btnOpenCaptureConsole
            // 
            btnOpenCaptureConsole.AccessibleName = "搜图控制台";
            btnOpenCaptureConsole.Dock = DockStyle.Fill;
            btnOpenCaptureConsole.FlatAppearance.BorderSize = 0;
            btnOpenCaptureConsole.FlatStyle = FlatStyle.Flat;
            btnOpenCaptureConsole.Font = new Font("微软雅黑", 9F);
            btnOpenCaptureConsole.Location = new Point(188, 43);
            btnOpenCaptureConsole.Name = "btnOpenCaptureConsole";
            btnOpenCaptureConsole.Size = new Size(174, 89);
            btnOpenCaptureConsole.TabIndex = 2;
            btnOpenCaptureConsole.Text = "搜图控制台";
            btnOpenCaptureConsole.UseVisualStyleBackColor = true;
            btnOpenCaptureConsole.Click += btnOpenCaptureConsole_Click;
            // 
            // lblTitleController
            // 
            lblTitleController.AutoSize = true;
            lblTitleController.Dock = DockStyle.Fill;
            lblTitleController.Font = new Font("微软雅黑", 9F, FontStyle.Bold);
            lblTitleController.Location = new Point(761, 0);
            lblTitleController.Name = "lblTitleController";
            lblTitleController.Size = new Size(374, 19);
            lblTitleController.TabIndex = 0;
            lblTitleController.Text = "虚拟手柄";
            // 
            // tblController
            // 
            tblController.BackColor = Color.FromArgb(230, 229, 224);
            tblController.ColumnCount = 2;
            tblController.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tblController.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tblController.Controls.Add(comboInputMode, 0, 0);
            tblController.Controls.Add(buttonShowController, 1, 0);
            tblController.Controls.Add(buttonKeyMapping, 0, 1);
            tblController.Controls.Add(buttonControllerHelp, 1, 1);
            tblController.Controls.Add(buttonRecord, 0, 2);
            tblController.Controls.Add(buttonRecordPause, 1, 2);
            tblController.Dock = DockStyle.Fill;
            tblController.Location = new Point(762, 23);
            tblController.Margin = new Padding(4);
            tblController.Name = "tblController";
            tblController.Padding = new Padding(6);
            tblController.RowCount = 3;
            tblController.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            tblController.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tblController.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tblController.Size = new Size(372, 141);
            tblController.TabIndex = 0;
            // 
            // EasyConForm
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(242, 241, 237);
            ClientSize = new Size(1140, 771);
            Controls.Add(bottomPanel);
            Controls.Add(scriptContainer);
            Controls.Add(optPanel);
            Controls.Add(logPanel);
            Controls.Add(statusStrip);
            Controls.Add(menuStrip);
            DoubleBuffered = true;
            Font = new Font("微软雅黑", 8.25F);
            Icon = (Icon)resources.GetObject("$this.Icon");
            KeyPreview = true;
            MainMenuStrip = menuStrip;
            Margin = new Padding(3, 4, 3, 4);
            MinimumSize = new Size(800, 500);
            Name = "EasyConForm";
            Padding = new Padding(1);
            StartPosition = FormStartPosition.CenterScreen;
            Text = "EasyCon";
            FormClosing += EasyConForm_FormClosing;
            Load += EasyConForm_Load;
            menuStrip.ResumeLayout(false);
            menuStrip.PerformLayout();
            grpFirmware.ResumeLayout(false);
            statusStrip.ResumeLayout(false);
            statusStrip.PerformLayout();
            optPanel.ResumeLayout(false);
            logPanel.ResumeLayout(false);
            logPanel.PerformLayout();
            scriptContainer.ResumeLayout(false);
            scriptContainer.PerformLayout();
            bottomPanel.ResumeLayout(false);
            bottomPanel.PerformLayout();
            tblSerialPort.ResumeLayout(false);
            tblVideoSource.ResumeLayout(false);
            tblController.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.MenuStrip menuStrip;
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
        private System.Windows.Forms.Button buttonShowController;
        private System.Windows.Forms.Label labelTimer;
        private RichLogBox logTxtBox;
        private System.Windows.Forms.Button buttonKeyMapping;
        private System.Windows.Forms.Button buttonSerialPortSearch;
        private System.Windows.Forms.Button buttonSerialPortConnect;
        private System.Windows.Forms.OpenFileDialog openFileDialog;
        private System.Windows.Forms.SaveFileDialog saveFileDialog;
        private System.Windows.Forms.Button buttonControllerHelp;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.Button genFwButton;
        private System.Windows.Forms.Button buttonFlash;
        private System.Windows.Forms.Button buttonRemoteStop;
        private System.Windows.Forms.Button buttonRemoteStart;
        private System.Windows.Forms.Button buttonFlashClear;
        private System.Windows.Forms.Button buttonScriptHelp;
        private System.Windows.Forms.ToolStripMenuItem 项目源码ToolStripMenuItem;
        private System.Windows.Forms.ComboBox comboBoxBoardType;
        private System.Windows.Forms.GroupBox grpFirmware;
        private System.Windows.Forms.Button buttonRecord;
        private System.Windows.Forms.Button buttonRecordPause;
        private System.Windows.Forms.ToolStripMenuItem 搜图ToolStripMenuItem;
        private System.Windows.Forms.ComboBox ComPort;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel1;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripMenuItem 搜图说明ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 采集卡类型ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 设置ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 显示调试信息ToolStripMenuItem;
        private WinFormsAvaloniaControlHost editorHost;
        private ToolStripMenuItem 推送设置ToolStripMenuItem;
        private ToolStripMenuItem 代码自动补全ToolStripMenuItem;
        private ToolStripMenuItem 深色模式ToolStripMenuItem;
        private ToolStripMenuItem 高精度模式ToolStripMenuItem;
        private ToolStripMenuItem 蓝牙ToolStripMenuItem;
        private ToolStripMenuItem 蓝牙设备驱动配置ToolStripMenuItem;
        private ToolStripMenuItem eSP32ToolStripMenuItem;
        private ToolStripMenuItem 手柄设置ToolStripMenuItem;
        private ToolStripMenuItem 取消配对ToolStripMenuItem;
        private ToolStripMenuItem 画图ToolStripMenuItem;
        private ToolStripMenuItem 喷射ToolStripMenuItem;
        private ToolStripMenuItem 自由画板鼠标代替摇杆ToolStripMenuItem;
        private ToolStripMenuItem 设置环境变量ToolStripMenuItem;
        private ToolStripMenuItem 固件模式ToolStripMenuItem;
        private ToolStripMenuItem 联机模式ToolStripMenuItem;
        private ToolStripMenuItem 烧录模式ToolStripMenuItem;
        private ToolStripMenuItem 烧录自动运行ToolStripMenuItem;
        private ToolStripMenuItem 检查更新ToolStripMenuItem;
        private ToolStripMenuItem 編輯ToolStripMenuItem;
        private ToolStripMenuItem 查找替換ToolStripMenuItem;
        private ToolStripMenuItem 查找下一个ToolStripMenuItem;
        private ToolStripMenuItem 显示折叠ToolStripMenuItem;
        private ToolStripMenuItem 注释取消注释ToolStripMenuItem;
        private FlowLayoutPanel optPanel;
        private ToolStripStatusLabel labelSerialStatus;
        private ToolStripStatusLabel labelCaptureStatus;
        private ToolStripStatusLabel toolStripStatusLabel3;
        private Panel logPanel;
        private Button runStopBtn;
        private Button clsLogBtn;
        private Label logTitleLabel;
        private ToolStripStatusLabel toolStripStatusLabel2;
        private Panel scriptContainer;
        private Label scriptTitleLabel;
        private TableLayoutPanel bottomPanel;
        private TableLayoutPanel tblSerialPort;
        private TableLayoutPanel tblVideoSource;
        private TableLayoutPanel tblController;
        private Label lblTitleSerial;
        private Label lblTitleVideo;
        private Label lblTitleController;
        private ComboBox comboVideoSource;
        private Button btnCaptureToggle;
        private Button btnOpenCaptureConsole;
        private ComboBox comboInputMode;
    }
}
