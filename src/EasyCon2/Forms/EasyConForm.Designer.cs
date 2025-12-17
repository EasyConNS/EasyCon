namespace EasyCon2.Forms
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
            menuStrip1 = new MenuStrip();
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
            脚本ToolStripMenuItem = new ToolStripMenuItem();
            编译ToolStripMenuItem = new ToolStripMenuItem();
            执行ToolStripMenuItem = new ToolStripMenuItem();
            搜图ToolStripMenuItem = new ToolStripMenuItem();
            采集卡类型ToolStripMenuItem = new ToolStripMenuItem();
            打开搜图ToolStripMenuItem = new ToolStripMenuItem();
            搜图说明ToolStripMenuItem = new ToolStripMenuItem();
            设置环境变量ToolStripMenuItem = new ToolStripMenuItem();
            设置ToolStripMenuItem = new ToolStripMenuItem();
            推送设置ToolStripMenuItem = new ToolStripMenuItem();
            显示调试信息ToolStripMenuItem = new ToolStripMenuItem();
            串口延迟ToolStripMenuItem = new ToolStripMenuItem();
            烧录自动运行ToolStripMenuItem = new ToolStripMenuItem();
            频道远程ToolStripMenuItem = new ToolStripMenuItem();
            显示折叠ToolStripMenuItem = new ToolStripMenuItem();
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
            groupBox1 = new GroupBox();
            buttonCLS = new Button();
            labelTimer = new Label();
            richTextBoxMessage = new RichTextBox();
            buttonScriptRunStop = new Button();
            groupBox6 = new GroupBox();
            buttonRecordPause = new Button();
            buttonRecord = new Button();
            buttonFlashClear = new Button();
            buttonFlash = new Button();
            buttonRemoteStop = new Button();
            buttonRemoteStart = new Button();
            groupBox2 = new GroupBox();
            buttonGenerateFirmware = new Button();
            textBoxFirmware = new TextBox();
            comboBoxBoardType = new ComboBox();
            buttonShowController = new Button();
            groupBoxScript = new GroupBox();
            findPanel1 = new FindPanel();
            elementHost1 = new System.Windows.Forms.Integration.ElementHost();
            groupBox3 = new GroupBox();
            ComPort = new ComboBox();
            labelSerialStatus = new Label();
            buttonSerialPortConnect = new Button();
            buttonSerialPortSearch = new Button();
            buttonControllerHelp = new Button();
            buttonKeyMapping = new Button();
            openFileDialog1 = new OpenFileDialog();
            saveFileDialog1 = new SaveFileDialog();
            groupBox4 = new GroupBox();
            toolStripStatusLabel1 = new ToolStripStatusLabel();
            statusStrip1 = new StatusStrip();
            textBoxScriptHelp = new TextBox();
            menuStrip1.SuspendLayout();
            groupBox1.SuspendLayout();
            groupBox6.SuspendLayout();
            groupBox2.SuspendLayout();
            groupBoxScript.SuspendLayout();
            groupBox3.SuspendLayout();
            groupBox4.SuspendLayout();
            statusStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // menuStrip1
            // 
            menuStrip1.Font = new Font("微软雅黑", 9F);
            menuStrip1.ImageScalingSize = new Size(20, 20);
            menuStrip1.Items.AddRange(new ToolStripItem[] { 文件ToolStripMenuItem, 編輯ToolStripMenuItem, 脚本ToolStripMenuItem, 搜图ToolStripMenuItem, 设置ToolStripMenuItem, 蓝牙ToolStripMenuItem, eSP32ToolStripMenuItem, 画图ToolStripMenuItem, 帮助ToolStripMenuItem });
            menuStrip1.Location = new Point(1, 1);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Padding = new Padding(7, 2, 0, 2);
            menuStrip1.RenderMode = ToolStripRenderMode.Professional;
            menuStrip1.Size = new Size(1121, 28);
            menuStrip1.TabIndex = 0;
            menuStrip1.Text = "menuStrip1";
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
            編輯ToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { 查找替換ToolStripMenuItem, 查找下一个ToolStripMenuItem });
            編輯ToolStripMenuItem.Name = "編輯ToolStripMenuItem";
            編輯ToolStripMenuItem.Size = new Size(53, 24);
            編輯ToolStripMenuItem.Text = "編輯";
            // 
            // 查找替換ToolStripMenuItem
            // 
            查找替換ToolStripMenuItem.Name = "查找替換ToolStripMenuItem";
            查找替換ToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.F;
            查找替換ToolStripMenuItem.Size = new Size(206, 26);
            查找替換ToolStripMenuItem.Text = "查找替換";
            查找替換ToolStripMenuItem.Click += 查找替換ToolStripMenuItem_Click;
            // 
            // 查找下一个ToolStripMenuItem
            // 
            查找下一个ToolStripMenuItem.Name = "查找下一个ToolStripMenuItem";
            查找下一个ToolStripMenuItem.ShortcutKeys = Keys.F3;
            查找下一个ToolStripMenuItem.Size = new Size(206, 26);
            查找下一个ToolStripMenuItem.Text = "查找下一个";
            查找下一个ToolStripMenuItem.Click += 查找下一个ToolStripMenuItem_Click;
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
            编译ToolStripMenuItem.Size = new Size(178, 26);
            编译ToolStripMenuItem.Text = "编译";
            编译ToolStripMenuItem.Click += 编译ToolStripMenuItem_Click;
            // 
            // 执行ToolStripMenuItem
            // 
            执行ToolStripMenuItem.Name = "执行ToolStripMenuItem";
            执行ToolStripMenuItem.ShortcutKeys = Keys.F5;
            执行ToolStripMenuItem.Size = new Size(178, 26);
            执行ToolStripMenuItem.Text = "执行";
            执行ToolStripMenuItem.Click += 执行ToolStripMenuItem_Click;
            // 
            // 搜图ToolStripMenuItem
            // 
            搜图ToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { 采集卡类型ToolStripMenuItem, 打开搜图ToolStripMenuItem, 搜图说明ToolStripMenuItem, 设置环境变量ToolStripMenuItem });
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
            // 打开搜图ToolStripMenuItem
            // 
            打开搜图ToolStripMenuItem.Name = "打开搜图ToolStripMenuItem";
            打开搜图ToolStripMenuItem.Size = new Size(182, 26);
            打开搜图ToolStripMenuItem.Text = "打开搜图";
            打开搜图ToolStripMenuItem.MouseHover += 打开搜图ToolStripMenuItem_MouseHover;
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
            设置ToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { 推送设置ToolStripMenuItem, 显示调试信息ToolStripMenuItem, 串口延迟ToolStripMenuItem, 烧录自动运行ToolStripMenuItem, 频道远程ToolStripMenuItem, 显示折叠ToolStripMenuItem });
            设置ToolStripMenuItem.Name = "设置ToolStripMenuItem";
            设置ToolStripMenuItem.Size = new Size(53, 24);
            设置ToolStripMenuItem.Text = "设置";
            // 
            // 推送设置ToolStripMenuItem
            // 
            推送设置ToolStripMenuItem.Name = "推送设置ToolStripMenuItem";
            推送设置ToolStripMenuItem.Size = new Size(224, 26);
            推送设置ToolStripMenuItem.Text = "推送设置";
            推送设置ToolStripMenuItem.Click += 推送设置ToolStripMenuItem_Click;
            // 
            // 显示调试信息ToolStripMenuItem
            // 
            显示调试信息ToolStripMenuItem.Name = "显示调试信息ToolStripMenuItem";
            显示调试信息ToolStripMenuItem.Size = new Size(224, 26);
            显示调试信息ToolStripMenuItem.Text = "显示调试信息";
            显示调试信息ToolStripMenuItem.Click += 显示调试信息ToolStripMenuItem_Click;
            // 
            // 串口延迟ToolStripMenuItem
            // 
            串口延迟ToolStripMenuItem.Name = "串口延迟ToolStripMenuItem";
            串口延迟ToolStripMenuItem.Size = new Size(224, 26);
            串口延迟ToolStripMenuItem.Text = "串口打开延迟";
            串口延迟ToolStripMenuItem.Click += openDelayToolStripMenuItem_Click;
            // 
            // 烧录自动运行ToolStripMenuItem
            // 
            烧录自动运行ToolStripMenuItem.Checked = true;
            烧录自动运行ToolStripMenuItem.CheckState = CheckState.Checked;
            烧录自动运行ToolStripMenuItem.Name = "烧录自动运行ToolStripMenuItem";
            烧录自动运行ToolStripMenuItem.Size = new Size(224, 26);
            烧录自动运行ToolStripMenuItem.Text = "烧录自动运行";
            烧录自动运行ToolStripMenuItem.Click += 脚本自动运行ToolStripMenuItem_Click;
            // 
            // 频道远程ToolStripMenuItem
            // 
            频道远程ToolStripMenuItem.Name = "频道远程ToolStripMenuItem";
            频道远程ToolStripMenuItem.Size = new Size(224, 26);
            频道远程ToolStripMenuItem.Text = "频道远程控制启动";
            频道远程ToolStripMenuItem.Click += 频道远程ToolStripMenuItem_Click;
            // 
            // 显示折叠ToolStripMenuItem
            // 
            显示折叠ToolStripMenuItem.Checked = true;
            显示折叠ToolStripMenuItem.CheckState = CheckState.Checked;
            显示折叠ToolStripMenuItem.Name = "显示折叠ToolStripMenuItem";
            显示折叠ToolStripMenuItem.Size = new Size(224, 26);
            显示折叠ToolStripMenuItem.Text = "显示折叠";
            显示折叠ToolStripMenuItem.Click += 显示折叠ToolStripMenuItem_Click;
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
            // groupBox1
            // 
            groupBox1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            groupBox1.Controls.Add(buttonCLS);
            groupBox1.Controls.Add(labelTimer);
            groupBox1.Controls.Add(richTextBoxMessage);
            groupBox1.Controls.Add(buttonScriptRunStop);
            groupBox1.Location = new Point(8, 30);
            groupBox1.Margin = new Padding(3, 4, 3, 4);
            groupBox1.Name = "groupBox1";
            groupBox1.Padding = new Padding(3, 4, 3, 4);
            groupBox1.Size = new Size(312, 453);
            groupBox1.TabIndex = 1;
            groupBox1.TabStop = false;
            groupBox1.Text = "日志";
            // 
            // buttonCLS
            // 
            buttonCLS.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            buttonCLS.Location = new Point(125, 400);
            buttonCLS.Name = "buttonCLS";
            buttonCLS.Size = new Size(46, 46);
            buttonCLS.TabIndex = 36;
            buttonCLS.Text = "清屏";
            buttonCLS.UseVisualStyleBackColor = true;
            buttonCLS.Click += buttonCLS_Click;
            // 
            // labelTimer
            // 
            labelTimer.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            labelTimer.BackColor = Color.Black;
            labelTimer.BorderStyle = BorderStyle.Fixed3D;
            labelTimer.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            labelTimer.ForeColor = Color.White;
            labelTimer.Location = new Point(6, 400);
            labelTimer.Name = "labelTimer";
            labelTimer.Size = new Size(119, 46);
            labelTimer.TabIndex = 35;
            labelTimer.Text = "00:00:00";
            labelTimer.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // richTextBoxMessage
            // 
            richTextBoxMessage.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            richTextBoxMessage.BackColor = Color.FromArgb(64, 64, 64);
            richTextBoxMessage.BorderStyle = BorderStyle.FixedSingle;
            richTextBoxMessage.ForeColor = Color.White;
            richTextBoxMessage.Location = new Point(6, 14);
            richTextBoxMessage.Name = "richTextBoxMessage";
            richTextBoxMessage.ReadOnly = true;
            richTextBoxMessage.Size = new Size(302, 383);
            richTextBoxMessage.TabIndex = 34;
            richTextBoxMessage.Text = "";
            richTextBoxMessage.WordWrap = false;
            // 
            // buttonScriptRunStop
            // 
            buttonScriptRunStop.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            buttonScriptRunStop.Location = new Point(171, 400);
            buttonScriptRunStop.Name = "buttonScriptRunStop";
            buttonScriptRunStop.Size = new Size(136, 46);
            buttonScriptRunStop.TabIndex = 4;
            buttonScriptRunStop.Text = "运行";
            buttonScriptRunStop.UseVisualStyleBackColor = true;
            buttonScriptRunStop.Click += buttonScriptRunStop_Click;
            // 
            // groupBox6
            // 
            groupBox6.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            groupBox6.Controls.Add(buttonRecordPause);
            groupBox6.Controls.Add(buttonRecord);
            groupBox6.Controls.Add(buttonFlashClear);
            groupBox6.Controls.Add(buttonFlash);
            groupBox6.Controls.Add(buttonRemoteStop);
            groupBox6.Controls.Add(buttonRemoteStart);
            groupBox6.Location = new Point(804, 30);
            groupBox6.Name = "groupBox6";
            groupBox6.Size = new Size(312, 118);
            groupBox6.TabIndex = 5;
            groupBox6.TabStop = false;
            groupBox6.Text = "选项";
            // 
            // buttonRecordPause
            // 
            buttonRecordPause.Enabled = false;
            buttonRecordPause.Location = new Point(210, 67);
            buttonRecordPause.Name = "buttonRecordPause";
            buttonRecordPause.Size = new Size(96, 42);
            buttonRecordPause.TabIndex = 6;
            buttonRecordPause.Text = "暂停录制";
            buttonRecordPause.UseVisualStyleBackColor = true;
            buttonRecordPause.Click += buttonRecordPause_Click;
            // 
            // buttonRecord
            // 
            buttonRecord.Location = new Point(210, 17);
            buttonRecord.Name = "buttonRecord";
            buttonRecord.Size = new Size(96, 47);
            buttonRecord.TabIndex = 5;
            buttonRecord.Text = "录制脚本";
            buttonRecord.UseVisualStyleBackColor = true;
            buttonRecord.Click += buttonRecord_Click;
            // 
            // buttonFlashClear
            // 
            buttonFlashClear.Location = new Point(6, 67);
            buttonFlashClear.Name = "buttonFlashClear";
            buttonFlashClear.Size = new Size(96, 42);
            buttonFlashClear.TabIndex = 4;
            buttonFlashClear.Text = "清除烧录";
            buttonFlashClear.UseVisualStyleBackColor = true;
            buttonFlashClear.Click += buttonFlashClear_Click;
            // 
            // buttonFlash
            // 
            buttonFlash.Location = new Point(6, 19);
            buttonFlash.Name = "buttonFlash";
            buttonFlash.Size = new Size(96, 47);
            buttonFlash.TabIndex = 1;
            buttonFlash.Text = "编译烧录";
            buttonFlash.UseVisualStyleBackColor = true;
            buttonFlash.Click += buttonFlash_Click;
            // 
            // buttonRemoteStop
            // 
            buttonRemoteStop.Location = new Point(108, 67);
            buttonRemoteStop.Name = "buttonRemoteStop";
            buttonRemoteStop.Size = new Size(96, 42);
            buttonRemoteStop.TabIndex = 3;
            buttonRemoteStop.Text = "远程停止";
            buttonRemoteStop.UseVisualStyleBackColor = true;
            buttonRemoteStop.Click += buttonRemoteStop_Click;
            // 
            // buttonRemoteStart
            // 
            buttonRemoteStart.Location = new Point(108, 17);
            buttonRemoteStart.Name = "buttonRemoteStart";
            buttonRemoteStart.Size = new Size(96, 47);
            buttonRemoteStart.TabIndex = 2;
            buttonRemoteStart.Text = "远程运行";
            buttonRemoteStart.UseVisualStyleBackColor = true;
            buttonRemoteStart.Click += buttonRemoteStart_Click;
            // 
            // groupBox2
            // 
            groupBox2.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            groupBox2.Controls.Add(buttonGenerateFirmware);
            groupBox2.Controls.Add(textBoxFirmware);
            groupBox2.Controls.Add(comboBoxBoardType);
            groupBox2.Location = new Point(804, 147);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(312, 94);
            groupBox2.TabIndex = 5;
            groupBox2.TabStop = false;
            groupBox2.Text = "固件";
            // 
            // buttonGenerateFirmware
            // 
            buttonGenerateFirmware.Location = new Point(192, 21);
            buttonGenerateFirmware.Name = "buttonGenerateFirmware";
            buttonGenerateFirmware.Size = new Size(114, 52);
            buttonGenerateFirmware.TabIndex = 0;
            buttonGenerateFirmware.Text = "生成固件";
            buttonGenerateFirmware.UseVisualStyleBackColor = true;
            buttonGenerateFirmware.Click += buttonGenerateFirmware_Click;
            // 
            // textBoxFirmware
            // 
            textBoxFirmware.Location = new Point(19, 55);
            textBoxFirmware.Name = "textBoxFirmware";
            textBoxFirmware.ReadOnly = true;
            textBoxFirmware.Size = new Size(149, 26);
            textBoxFirmware.TabIndex = 5;
            textBoxFirmware.WordWrap = false;
            // 
            // comboBoxBoardType
            // 
            comboBoxBoardType.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxBoardType.FormattingEnabled = true;
            comboBoxBoardType.Location = new Point(19, 21);
            comboBoxBoardType.Name = "comboBoxBoardType";
            comboBoxBoardType.Size = new Size(149, 28);
            comboBoxBoardType.TabIndex = 5;
            comboBoxBoardType.SelectedIndexChanged += comboBoxBoardType_SelectedIndexChanged;
            // 
            // buttonShowController
            // 
            buttonShowController.Location = new Point(6, 16);
            buttonShowController.Name = "buttonShowController";
            buttonShowController.Size = new Size(112, 40);
            buttonShowController.TabIndex = 3;
            buttonShowController.Text = "虚拟手柄";
            buttonShowController.UseVisualStyleBackColor = true;
            buttonShowController.Click += buttonShowController_Click;
            // 
            // groupBoxScript
            // 
            groupBoxScript.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            groupBoxScript.Controls.Add(findPanel1);
            groupBoxScript.Controls.Add(elementHost1);
            groupBoxScript.Location = new Point(327, 30);
            groupBoxScript.Margin = new Padding(3, 4, 3, 4);
            groupBoxScript.Name = "groupBoxScript";
            groupBoxScript.Padding = new Padding(3, 4, 3, 4);
            groupBoxScript.Size = new Size(471, 615);
            groupBoxScript.TabIndex = 2;
            groupBoxScript.TabStop = false;
            groupBoxScript.Text = "未命名脚本";
            // 
            // findPanel1
            // 
            findPanel1.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            findPanel1.BorderStyle = BorderStyle.FixedSingle;
            findPanel1.Location = new Point(245, 14);
            findPanel1.Name = "findPanel1";
            findPanel1.Replaced = null;
            findPanel1.Size = new Size(211, 114);
            findPanel1.TabIndex = 1;
            findPanel1.Target = null;
            findPanel1.Visible = false;
            // 
            // elementHost1
            // 
            elementHost1.Dock = DockStyle.Fill;
            elementHost1.Location = new Point(3, 23);
            elementHost1.Name = "elementHost1";
            elementHost1.Size = new Size(465, 588);
            elementHost1.TabIndex = 0;
            elementHost1.Text = "elementHost1";
            // 
            // groupBox3
            // 
            groupBox3.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            groupBox3.Controls.Add(ComPort);
            groupBox3.Controls.Add(labelSerialStatus);
            groupBox3.Controls.Add(buttonSerialPortConnect);
            groupBox3.Controls.Add(buttonSerialPortSearch);
            groupBox3.Location = new Point(8, 482);
            groupBox3.Name = "groupBox3";
            groupBox3.Size = new Size(312, 95);
            groupBox3.TabIndex = 4;
            groupBox3.TabStop = false;
            groupBox3.Text = "连接";
            // 
            // ComPort
            // 
            ComPort.FormattingEnabled = true;
            ComPort.Location = new Point(177, 20);
            ComPort.Name = "ComPort";
            ComPort.Size = new Size(114, 28);
            ComPort.TabIndex = 34;
            ComPort.Text = "下拉选择串口";
            ComPort.DropDown += ComPort_DropDown;
            // 
            // labelSerialStatus
            // 
            labelSerialStatus.BackColor = Color.DimGray;
            labelSerialStatus.BorderStyle = BorderStyle.FixedSingle;
            labelSerialStatus.ForeColor = SystemColors.ControlLight;
            labelSerialStatus.Location = new Point(21, 18);
            labelSerialStatus.Name = "labelSerialStatus";
            labelSerialStatus.Size = new Size(150, 26);
            labelSerialStatus.TabIndex = 31;
            labelSerialStatus.Text = "单片机未连接";
            labelSerialStatus.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // buttonSerialPortConnect
            // 
            buttonSerialPortConnect.Location = new Point(177, 51);
            buttonSerialPortConnect.Name = "buttonSerialPortConnect";
            buttonSerialPortConnect.Size = new Size(114, 32);
            buttonSerialPortConnect.TabIndex = 30;
            buttonSerialPortConnect.Text = "手动连接";
            buttonSerialPortConnect.UseVisualStyleBackColor = true;
            buttonSerialPortConnect.Click += buttonSerialPortConnect_Click;
            // 
            // buttonSerialPortSearch
            // 
            buttonSerialPortSearch.Location = new Point(21, 51);
            buttonSerialPortSearch.Name = "buttonSerialPortSearch";
            buttonSerialPortSearch.Size = new Size(150, 32);
            buttonSerialPortSearch.TabIndex = 29;
            buttonSerialPortSearch.Text = "自动连接(推荐)";
            buttonSerialPortSearch.UseVisualStyleBackColor = true;
            buttonSerialPortSearch.Click += buttonSerialPortSearch_Click;
            // 
            // buttonControllerHelp
            // 
            buttonControllerHelp.Location = new Point(127, 16);
            buttonControllerHelp.Name = "buttonControllerHelp";
            buttonControllerHelp.Size = new Size(65, 40);
            buttonControllerHelp.TabIndex = 33;
            buttonControllerHelp.Text = "帮助";
            buttonControllerHelp.UseVisualStyleBackColor = true;
            buttonControllerHelp.Click += buttonControllerHelp_Click;
            // 
            // buttonKeyMapping
            // 
            buttonKeyMapping.Location = new Point(202, 16);
            buttonKeyMapping.Name = "buttonKeyMapping";
            buttonKeyMapping.Size = new Size(89, 40);
            buttonKeyMapping.TabIndex = 4;
            buttonKeyMapping.Text = "按键映射";
            buttonKeyMapping.UseVisualStyleBackColor = true;
            buttonKeyMapping.Click += buttonKeyMapping_Click;
            // 
            // openFileDialog1
            // 
            openFileDialog1.Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
            openFileDialog1.RestoreDirectory = true;
            openFileDialog1.Title = "打开";
            // 
            // saveFileDialog1
            // 
            saveFileDialog1.Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
            saveFileDialog1.RestoreDirectory = true;
            saveFileDialog1.Title = "另存为";
            // 
            // groupBox4
            // 
            groupBox4.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            groupBox4.Controls.Add(buttonShowController);
            groupBox4.Controls.Add(buttonControllerHelp);
            groupBox4.Controls.Add(buttonKeyMapping);
            groupBox4.Location = new Point(8, 578);
            groupBox4.Name = "groupBox4";
            groupBox4.Size = new Size(312, 67);
            groupBox4.TabIndex = 34;
            groupBox4.TabStop = false;
            groupBox4.Text = "手柄";
            // 
            // toolStripStatusLabel1
            // 
            toolStripStatusLabel1.Name = "toolStripStatusLabel1";
            toolStripStatusLabel1.Size = new Size(0, 16);
            // 
            // statusStrip1
            // 
            statusStrip1.ImageScalingSize = new Size(20, 20);
            statusStrip1.Items.AddRange(new ToolStripItem[] { toolStripStatusLabel1 });
            statusStrip1.Location = new Point(1, 649);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(1121, 22);
            statusStrip1.TabIndex = 3;
            statusStrip1.Text = "statusStrip1";
            // 
            // textBoxScriptHelp
            // 
            textBoxScriptHelp.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            textBoxScriptHelp.Location = new Point(804, 240);
            textBoxScriptHelp.Multiline = true;
            textBoxScriptHelp.Name = "textBoxScriptHelp";
            textBoxScriptHelp.ReadOnly = true;
            textBoxScriptHelp.ScrollBars = ScrollBars.Vertical;
            textBoxScriptHelp.Size = new Size(312, 401);
            textBoxScriptHelp.TabIndex = 35;
            // 
            // EasyConForm
            // 
            AutoScaleMode = AutoScaleMode.Inherit;
            ClientSize = new Size(1123, 672);
            Controls.Add(textBoxScriptHelp);
            Controls.Add(groupBox4);
            Controls.Add(groupBox3);
            Controls.Add(groupBox6);
            Controls.Add(groupBox2);
            Controls.Add(statusStrip1);
            Controls.Add(groupBoxScript);
            Controls.Add(groupBox1);
            Controls.Add(menuStrip1);
            DoubleBuffered = true;
            Font = new Font("微软雅黑", 8.25F);
            Icon = (Icon)resources.GetObject("$this.Icon");
            KeyPreview = true;
            MainMenuStrip = menuStrip1;
            Margin = new Padding(3, 4, 3, 4);
            MaximizeBox = false;
            MinimumSize = new Size(800, 600);
            Name = "EasyConForm";
            Padding = new Padding(1);
            StartPosition = FormStartPosition.CenterScreen;
            Text = "EasyCon";
            FormClosing += EasyConForm_FormClosing;
            Load += EasyConForm_Load;
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            groupBox1.ResumeLayout(false);
            groupBox6.ResumeLayout(false);
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            groupBoxScript.ResumeLayout(false);
            groupBox3.ResumeLayout(false);
            groupBox4.ResumeLayout(false);
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
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
        private System.Windows.Forms.Button buttonShowController;
        private System.Windows.Forms.Button buttonScriptRunStop;
        private System.Windows.Forms.Label labelTimer;
        private System.Windows.Forms.RichTextBox richTextBoxMessage;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.Button buttonKeyMapping;
        private System.Windows.Forms.Button buttonSerialPortSearch;
        private System.Windows.Forms.Button buttonSerialPortConnect;
        private System.Windows.Forms.Label labelSerialStatus;
        private System.Windows.Forms.OpenFileDialog openFileDialog1;
        private System.Windows.Forms.SaveFileDialog saveFileDialog1;
        private System.Windows.Forms.Button buttonControllerHelp;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.Button buttonCLS;
        private System.Windows.Forms.Button buttonGenerateFirmware;
        private System.Windows.Forms.Button buttonFlash;
        private System.Windows.Forms.Button buttonRemoteStop;
        private System.Windows.Forms.Button buttonRemoteStart;
        private System.Windows.Forms.GroupBox groupBox6;
        private System.Windows.Forms.Button buttonFlashClear;
        private System.Windows.Forms.ToolStripMenuItem 项目源码ToolStripMenuItem;
        private System.Windows.Forms.ComboBox comboBoxBoardType;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.TextBox textBoxFirmware;
        private System.Windows.Forms.Button buttonRecord;
        private System.Windows.Forms.Button buttonRecordPause;
        private System.Windows.Forms.ToolStripMenuItem 搜图ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 打开搜图ToolStripMenuItem;
        private System.Windows.Forms.ComboBox ComPort;
        private System.Windows.Forms.GroupBox groupBox4;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel1;
        private System.Windows.Forms.StatusStrip statusStrip1;
        private System.Windows.Forms.ToolStripMenuItem 搜图说明ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 采集卡类型ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 设置ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 显示调试信息ToolStripMenuItem;
        private System.Windows.Forms.Integration.ElementHost elementHost1;
        private TextBox textBoxScriptHelp;
        private ToolStripMenuItem 推送设置ToolStripMenuItem;
        private ToolStripMenuItem 蓝牙ToolStripMenuItem;
        private ToolStripMenuItem 蓝牙设备驱动配置ToolStripMenuItem;
        private ToolStripMenuItem 频道远程ToolStripMenuItem;
        private ToolStripMenuItem eSP32ToolStripMenuItem;
        private ToolStripMenuItem 手柄设置ToolStripMenuItem;
        private ToolStripMenuItem 取消配对ToolStripMenuItem;
        private ToolStripMenuItem 画图ToolStripMenuItem;
        private ToolStripMenuItem 喷射ToolStripMenuItem;
        private ToolStripMenuItem 自由画板鼠标代替摇杆ToolStripMenuItem;
        private ToolStripMenuItem 串口延迟ToolStripMenuItem;
        private ToolStripMenuItem 设置环境变量ToolStripMenuItem;
        private ToolStripMenuItem 固件模式ToolStripMenuItem;
        private ToolStripMenuItem 联机模式ToolStripMenuItem;
        private ToolStripMenuItem 烧录模式ToolStripMenuItem;
        private ToolStripMenuItem 烧录自动运行ToolStripMenuItem;
        private ToolStripMenuItem 检查更新ToolStripMenuItem;
        private ToolStripMenuItem 編輯ToolStripMenuItem;
        private ToolStripMenuItem 查找替換ToolStripMenuItem;
        private ToolStripMenuItem 查找下一个ToolStripMenuItem;
        private FindPanel findPanel1;
        private ToolStripMenuItem 显示折叠ToolStripMenuItem;
    }
}

