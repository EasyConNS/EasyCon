using Avalonia.Win32.Interoperability;
using EasyCon2.Forms;

namespace EasyCon2.App;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null)
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
        menuStrip = new MenuStrip();
        fileMenu = new ToolStripMenuItem();
        menuItemNew = new ToolStripMenuItem();
        menuItemOpen = new ToolStripMenuItem();
        menuItemSave = new ToolStripMenuItem();
        menuItemSaveAs = new ToolStripMenuItem();
        menuItemClose = new ToolStripMenuItem();
        toolStripSeparator1 = new ToolStripSeparator();
        menuItemExit = new ToolStripMenuItem();
        editMenu = new ToolStripMenuItem();
        menuItemFindReplace = new ToolStripMenuItem();
        menuItemFindNext = new ToolStripMenuItem();
        menuItemToggleComment = new ToolStripMenuItem();
        scriptMenu = new ToolStripMenuItem();
        formatMenuItem = new ToolStripMenuItem();
        runMenuItem = new ToolStripMenuItem();
        captureMenu = new ToolStripMenuItem();
        captureTypeMenu = new ToolStripMenuItem();
        setEnvVarMenuItem = new ToolStripMenuItem();
        captureHelpMenuItem = new ToolStripMenuItem();
        helpMenu = new ToolStripMenuItem();
        menuItemFirmwareMode = new ToolStripMenuItem();
        menuItemOnlineMode = new ToolStripMenuItem();
        menuItemFlashMode = new ToolStripMenuItem();
        menuItemScriptSyntax = new ToolStripMenuItem();
        toolStripSeparator2 = new ToolStripSeparator();
        menuItemAbout = new ToolStripMenuItem();
        mainSplit = new SplitContainer();
        contentPanel = new Panel();
        editorHost = new WinFormsAvaloniaControlHost();
        logPanel = new Panel();
        clsLogBtn = new Button();
        logTxtBox = new RichLogBox();
        burnPanel = new Panel();
        grpBurn = new GroupBox();
        btnRemoteStart = new Button();
        btnRemoteStop = new Button();
        btnFlash = new Button();
        btnFlashClear = new Button();
        grpFirmware = new GroupBox();
        comboBoardType = new ComboBox();
        btnGenFirmware = new Button();
        settingsPanel = new Panel();
        lblEditorSettings = new Label();
        chkAutoCompletion = new CheckBox();
        chkFolding = new CheckBox();
        chkDebugLog = new CheckBox();
        lblRunSettings = new Label();
        chkAutoRunAfterFlash = new CheckBox();
        chkHighResolutionTiming = new CheckBox();
        lblNotifySettings = new Label();
        btnAlertConfig = new Button();
        chkAutoSaveLog = new CheckBox();
        lblToolSettings = new Label();
        btnDrawingBoard = new Button();
        btnBluetoothSetting = new Button();
        btnESPConfig = new Button();
        btnUnpair = new Button();
        lblAbout = new Label();
        lblVersion = new Label();
        btnCheckUpdate = new Button();
        btnSource = new Button();
        scriptTitleLabel = new Label();
        sideBar = new Panel();
        btnPageLog = new Button();
        btnPageEditor = new Button();
        btnPageBurn = new Button();
        btnPageSettings = new Button();
        rightPanel = new FlowLayoutPanel();
        grpScriptRun = new GroupBox();
        runStopBtn = new Button();
        formatBtn = new Button();
        timerLabel = new Label();
        grpDevice = new GroupBox();
        comboComPort = new ComboBox();
        btnAutoConnect = new Button();
        btnManualConnect = new Button();
        grpVideoSource = new GroupBox();
        comboVideoSource = new ComboBox();
        btnCaptureToggle = new Button();
        btnOpenCaptureConsole = new Button();
        grpRecord = new GroupBox();
        btnRecord = new Button();
        btnRecordPause = new Button();
        grpController = new GroupBox();
        btnShowController = new Button();
        btnKeyMapping = new Button();
        statusStrip = new StatusStrip();
        toolStripStatusLabel1 = new ToolStripStatusLabel();
        toolStripStatusLabel2 = new ToolStripStatusLabel();
        labelSerialStatus = new ToolStripStatusLabel();
        toolStripStatusLabel3 = new ToolStripStatusLabel();
        labelCaptureStatus = new ToolStripStatusLabel();
        menuStrip.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)mainSplit).BeginInit();
        mainSplit.Panel1.SuspendLayout();
        mainSplit.Panel2.SuspendLayout();
        mainSplit.SuspendLayout();
        contentPanel.SuspendLayout();
        logPanel.SuspendLayout();
        burnPanel.SuspendLayout();
        grpBurn.SuspendLayout();
        grpFirmware.SuspendLayout();
        settingsPanel.SuspendLayout();
        sideBar.SuspendLayout();
        rightPanel.SuspendLayout();
        grpScriptRun.SuspendLayout();
        grpDevice.SuspendLayout();
        grpVideoSource.SuspendLayout();
        grpRecord.SuspendLayout();
        grpController.SuspendLayout();
        statusStrip.SuspendLayout();
        SuspendLayout();
        // 
        // menuStrip
        // 
        menuStrip.Font = new Font("微软雅黑", 9F);
        menuStrip.ImageScalingSize = new Size(20, 20);
        menuStrip.Items.AddRange(new ToolStripItem[] { fileMenu, editMenu, scriptMenu, captureMenu, helpMenu });
        menuStrip.Location = new Point(0, 0);
        menuStrip.Name = "menuStrip";
        menuStrip.Size = new Size(916, 28);
        menuStrip.TabIndex = 0;
        // 
        // fileMenu
        // 
        fileMenu.DropDownItems.AddRange(new ToolStripItem[] { menuItemNew, menuItemOpen, menuItemSave, menuItemSaveAs, menuItemClose, toolStripSeparator1, menuItemExit });
        fileMenu.Name = "fileMenu";
        fileMenu.Size = new Size(53, 24);
        fileMenu.Text = "文件";
        // 
        // menuItemNew
        // 
        menuItemNew.Name = "menuItemNew";
        menuItemNew.ShortcutKeys = Keys.Control | Keys.N;
        menuItemNew.Size = new Size(236, 26);
        menuItemNew.Text = "新建";
        menuItemNew.Click += menuItemNew_Click;
        // 
        // menuItemOpen
        // 
        menuItemOpen.Name = "menuItemOpen";
        menuItemOpen.ShortcutKeys = Keys.Control | Keys.O;
        menuItemOpen.Size = new Size(236, 26);
        menuItemOpen.Text = "打开";
        menuItemOpen.Click += menuItemOpen_Click;
        // 
        // menuItemSave
        // 
        menuItemSave.Name = "menuItemSave";
        menuItemSave.ShortcutKeys = Keys.Control | Keys.S;
        menuItemSave.Size = new Size(236, 26);
        menuItemSave.Text = "保存";
        menuItemSave.Click += menuItemSave_Click;
        // 
        // menuItemSaveAs
        // 
        menuItemSaveAs.Name = "menuItemSaveAs";
        menuItemSaveAs.ShortcutKeys = Keys.Control | Keys.Shift | Keys.S;
        menuItemSaveAs.Size = new Size(236, 26);
        menuItemSaveAs.Text = "另存为";
        menuItemSaveAs.Click += menuItemSaveAs_Click;
        // 
        // menuItemClose
        // 
        menuItemClose.Name = "menuItemClose";
        menuItemClose.ShortcutKeys = Keys.Control | Keys.W;
        menuItemClose.Size = new Size(236, 26);
        menuItemClose.Text = "关闭";
        menuItemClose.Click += menuItemClose_Click;
        // 
        // toolStripSeparator1
        // 
        toolStripSeparator1.Name = "toolStripSeparator1";
        toolStripSeparator1.Size = new Size(233, 6);
        // 
        // menuItemExit
        // 
        menuItemExit.Name = "menuItemExit";
        menuItemExit.ShortcutKeys = Keys.Control | Keys.Q;
        menuItemExit.Size = new Size(236, 26);
        menuItemExit.Text = "退出";
        menuItemExit.Click += menuItemExit_Click;
        // 
        // editMenu
        // 
        editMenu.DropDownItems.AddRange(new ToolStripItem[] { menuItemFindReplace, menuItemFindNext, menuItemToggleComment });
        editMenu.Name = "editMenu";
        editMenu.Size = new Size(53, 24);
        editMenu.Text = "编辑";
        // 
        // menuItemFindReplace
        // 
        menuItemFindReplace.Name = "menuItemFindReplace";
        menuItemFindReplace.ShortcutKeys = Keys.Control | Keys.F;
        menuItemFindReplace.Size = new Size(335, 26);
        menuItemFindReplace.Text = "查找替换";
        menuItemFindReplace.Click += menuItemFindReplace_Click;
        // 
        // menuItemFindNext
        // 
        menuItemFindNext.Name = "menuItemFindNext";
        menuItemFindNext.ShortcutKeys = Keys.F3;
        menuItemFindNext.Size = new Size(335, 26);
        menuItemFindNext.Text = "查找下一个";
        menuItemFindNext.Click += menuItemFindNext_Click;
        // 
        // menuItemToggleComment
        // 
        menuItemToggleComment.Name = "menuItemToggleComment";
        menuItemToggleComment.ShortcutKeys = Keys.Control | Keys.Oem2;
        menuItemToggleComment.Size = new Size(335, 26);
        menuItemToggleComment.Text = "注释/取消注释";
        menuItemToggleComment.Click += menuItemToggleComment_Click;
        // 
        // scriptMenu
        // 
        scriptMenu.DropDownItems.AddRange(new ToolStripItem[] { formatMenuItem, runMenuItem });
        scriptMenu.Name = "scriptMenu";
        scriptMenu.Size = new Size(53, 24);
        scriptMenu.Text = "脚本";
        scriptMenu.Visible = false;
        // 
        // formatMenuItem
        // 
        formatMenuItem.Name = "formatMenuItem";
        formatMenuItem.ShortcutKeys = Keys.Control | Keys.R;
        formatMenuItem.Size = new Size(193, 26);
        formatMenuItem.Text = "格式化";
        formatMenuItem.Click += formatBtn_Click;
        // 
        // runMenuItem
        // 
        runMenuItem.Name = "runMenuItem";
        runMenuItem.ShortcutKeys = Keys.F5;
        runMenuItem.Size = new Size(193, 26);
        runMenuItem.Text = "运行";
        runMenuItem.Click += runStopBtn_Click;
        // 
        // captureMenu
        // 
        captureMenu.DropDownItems.AddRange(new ToolStripItem[] { captureTypeMenu, setEnvVarMenuItem, captureHelpMenuItem });
        captureMenu.Name = "captureMenu";
        captureMenu.Size = new Size(53, 24);
        captureMenu.Text = "搜图";
        // 
        // captureTypeMenu
        // 
        captureTypeMenu.Name = "captureTypeMenu";
        captureTypeMenu.Size = new Size(182, 26);
        captureTypeMenu.Text = "采集卡类型";
        captureTypeMenu.DropDownOpening += captureTypeMenu_DropDownOpening;
        // 
        // setEnvVarMenuItem
        // 
        setEnvVarMenuItem.Name = "setEnvVarMenuItem";
        setEnvVarMenuItem.Size = new Size(182, 26);
        setEnvVarMenuItem.Text = "设置环境变量";
        setEnvVarMenuItem.Click += setEnvVarMenuItem_Click;
        // 
        // captureHelpMenuItem
        // 
        captureHelpMenuItem.Name = "captureHelpMenuItem";
        captureHelpMenuItem.Size = new Size(182, 26);
        captureHelpMenuItem.Text = "搜图说明";
        captureHelpMenuItem.Click += captureHelpMenuItem_Click;
        // 
        // helpMenu
        // 
        helpMenu.DropDownItems.AddRange(new ToolStripItem[] { menuItemFirmwareMode, menuItemOnlineMode, menuItemFlashMode, menuItemScriptSyntax, toolStripSeparator2, menuItemAbout });
        helpMenu.Name = "helpMenu";
        helpMenu.Size = new Size(53, 24);
        helpMenu.Text = "帮助";
        // 
        // menuItemFirmwareMode
        // 
        menuItemFirmwareMode.Name = "menuItemFirmwareMode";
        menuItemFirmwareMode.Size = new Size(152, 26);
        menuItemFirmwareMode.Text = "固件模式";
        menuItemFirmwareMode.Click += menuItemFirmwareMode_Click;
        // 
        // menuItemOnlineMode
        // 
        menuItemOnlineMode.Name = "menuItemOnlineMode";
        menuItemOnlineMode.Size = new Size(152, 26);
        menuItemOnlineMode.Text = "联机模式";
        menuItemOnlineMode.Click += menuItemOnlineMode_Click;
        // 
        // menuItemFlashMode
        // 
        menuItemFlashMode.Name = "menuItemFlashMode";
        menuItemFlashMode.Size = new Size(152, 26);
        menuItemFlashMode.Text = "烧录模式";
        menuItemFlashMode.Click += menuItemFlashMode_Click;
        // 
        // menuItemScriptSyntax
        // 
        menuItemScriptSyntax.Name = "menuItemScriptSyntax";
        menuItemScriptSyntax.Size = new Size(152, 26);
        menuItemScriptSyntax.Text = "脚本语法";
        menuItemScriptSyntax.Click += menuItemScriptSyntax_Click;
        // 
        // toolStripSeparator2
        // 
        toolStripSeparator2.Name = "toolStripSeparator2";
        toolStripSeparator2.Size = new Size(149, 6);
        // 
        // menuItemAbout
        // 
        menuItemAbout.Name = "menuItemAbout";
        menuItemAbout.Size = new Size(152, 26);
        menuItemAbout.Text = "关于";
        menuItemAbout.Click += menuItemAbout_Click;
        // 
        // mainSplit
        // 
        mainSplit.BackColor = Color.FromArgb(230, 229, 224);
        mainSplit.Dock = DockStyle.Fill;
        mainSplit.Location = new Point(0, 28);
        mainSplit.Name = "mainSplit";
        // 
        // mainSplit.Panel1
        // 
        mainSplit.Panel1.Controls.Add(contentPanel);
        mainSplit.Panel1.Controls.Add(sideBar);
        // 
        // mainSplit.Panel2
        // 
        mainSplit.Panel2.Controls.Add(rightPanel);
        mainSplit.Panel2MinSize = 240;
        mainSplit.Size = new Size(916, 681);
        mainSplit.SplitterDistance = 644;
        mainSplit.SplitterWidth = 6;
        mainSplit.TabIndex = 0;
        // 
        // contentPanel
        // 
        contentPanel.BackColor = Color.FromArgb(242, 241, 237);
        contentPanel.Controls.Add(editorHost);
        contentPanel.Controls.Add(logPanel);
        contentPanel.Controls.Add(burnPanel);
        contentPanel.Controls.Add(settingsPanel);
        contentPanel.Controls.Add(scriptTitleLabel);
        contentPanel.Dock = DockStyle.Fill;
        contentPanel.Location = new Point(40, 0);
        contentPanel.Name = "contentPanel";
        contentPanel.Size = new Size(604, 681);
        contentPanel.TabIndex = 0;
        // 
        // editorHost
        // 
        editorHost.Dock = DockStyle.Fill;
        editorHost.Font = new Font("Consolas", 9F);
        editorHost.Location = new Point(0, 24);
        editorHost.Name = "editorHost";
        editorHost.Size = new Size(566, 699);
        editorHost.TabIndex = 1;
        editorHost.Visible = false;
        // 
        // logPanel
        // 
        logPanel.BackColor = SystemColors.Control;
        logPanel.Controls.Add(clsLogBtn);
        logPanel.Controls.Add(logTxtBox);
        logPanel.Dock = DockStyle.Fill;
        logPanel.ForeColor = Color.White;
        logPanel.Location = new Point(0, 24);
        logPanel.Name = "logPanel";
        logPanel.Size = new Size(604, 657);
        logPanel.TabIndex = 4;
        // 
        // clsLogBtn
        // 
        clsLogBtn.AccessibleName = "清除日志输出";
        clsLogBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        clsLogBtn.BackColor = Color.Transparent;
        clsLogBtn.BackgroundImage = ResourceHelper.Clrlog;
        clsLogBtn.BackgroundImageLayout = ImageLayout.Stretch;
        clsLogBtn.FlatAppearance.BorderSize = 0;
        clsLogBtn.FlatStyle = FlatStyle.Flat;
        clsLogBtn.Location = new Point(564, 5);
        clsLogBtn.Name = "clsLogBtn";
        clsLogBtn.Size = new Size(30, 30);
        clsLogBtn.TabIndex = 39;
        clsLogBtn.UseVisualStyleBackColor = false;
        clsLogBtn.Click += clsLogBtn_Click;
        // 
        // logTxtBox
        // 
        logTxtBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        logTxtBox.BackColor = Color.FromArgb(64, 64, 64);
        logTxtBox.ForeColor = Color.White;
        logTxtBox.Location = new Point(6, 10);
        logTxtBox.Name = "logTxtBox";
        logTxtBox.Size = new Size(590, 644);
        logTxtBox.TabIndex = 0;
        logTxtBox.Text = "";
        // 
        // burnPanel
        // 
        burnPanel.BackColor = Color.FromArgb(242, 241, 237);
        burnPanel.Controls.Add(grpBurn);
        burnPanel.Controls.Add(grpFirmware);
        burnPanel.Dock = DockStyle.Fill;
        burnPanel.Location = new Point(0, 24);
        burnPanel.Name = "burnPanel";
        burnPanel.Size = new Size(604, 657);
        burnPanel.TabIndex = 5;
        burnPanel.Visible = false;
        // 
        // grpBurn
        // 
        grpBurn.Controls.Add(btnRemoteStart);
        grpBurn.Controls.Add(btnRemoteStop);
        grpBurn.Controls.Add(btnFlash);
        grpBurn.Controls.Add(btnFlashClear);
        grpBurn.ForeColor = Color.FromArgb(38, 37, 30);
        grpBurn.Location = new Point(20, 20);
        grpBurn.Name = "grpBurn";
        grpBurn.Size = new Size(300, 130);
        grpBurn.TabIndex = 0;
        grpBurn.TabStop = false;
        grpBurn.Text = "烧录";
        // 
        // btnRemoteStart
        // 
        btnRemoteStart.BackColor = Color.FromArgb(235, 234, 229);
        btnRemoteStart.FlatAppearance.BorderSize = 0;
        btnRemoteStart.FlatStyle = FlatStyle.Flat;
        btnRemoteStart.Font = new Font("微软雅黑", 9F);
        btnRemoteStart.ForeColor = Color.FromArgb(38, 37, 30);
        btnRemoteStart.Location = new Point(8, 22);
        btnRemoteStart.Name = "btnRemoteStart";
        btnRemoteStart.Size = new Size(130, 28);
        btnRemoteStart.TabIndex = 0;
        btnRemoteStart.Text = "远程运行";
        btnRemoteStart.UseVisualStyleBackColor = false;
        btnRemoteStart.Click += btnRemoteStart_Click;
        // 
        // btnRemoteStop
        // 
        btnRemoteStop.BackColor = Color.FromArgb(235, 234, 229);
        btnRemoteStop.FlatAppearance.BorderSize = 0;
        btnRemoteStop.FlatStyle = FlatStyle.Flat;
        btnRemoteStop.Font = new Font("微软雅黑", 9F);
        btnRemoteStop.ForeColor = Color.FromArgb(38, 37, 30);
        btnRemoteStop.Location = new Point(150, 22);
        btnRemoteStop.Name = "btnRemoteStop";
        btnRemoteStop.Size = new Size(130, 28);
        btnRemoteStop.TabIndex = 1;
        btnRemoteStop.Text = "远程停止";
        btnRemoteStop.UseVisualStyleBackColor = false;
        btnRemoteStop.Click += btnRemoteStop_Click;
        // 
        // btnFlash
        // 
        btnFlash.BackColor = Color.FromArgb(235, 234, 229);
        btnFlash.FlatAppearance.BorderSize = 0;
        btnFlash.FlatStyle = FlatStyle.Flat;
        btnFlash.Font = new Font("微软雅黑", 9F);
        btnFlash.ForeColor = Color.FromArgb(38, 37, 30);
        btnFlash.Location = new Point(8, 54);
        btnFlash.Name = "btnFlash";
        btnFlash.Size = new Size(272, 30);
        btnFlash.TabIndex = 2;
        btnFlash.Text = "编译烧录";
        btnFlash.UseVisualStyleBackColor = false;
        btnFlash.Click += btnFlash_Click;
        // 
        // btnFlashClear
        // 
        btnFlashClear.BackColor = Color.FromArgb(235, 234, 229);
        btnFlashClear.FlatAppearance.BorderSize = 0;
        btnFlashClear.FlatStyle = FlatStyle.Flat;
        btnFlashClear.Font = new Font("微软雅黑", 9F);
        btnFlashClear.ForeColor = Color.FromArgb(38, 37, 30);
        btnFlashClear.Location = new Point(8, 90);
        btnFlashClear.Name = "btnFlashClear";
        btnFlashClear.Size = new Size(130, 28);
        btnFlashClear.TabIndex = 3;
        btnFlashClear.Text = "清除烧录";
        btnFlashClear.UseVisualStyleBackColor = false;
        btnFlashClear.Click += btnFlashClear_Click;
        // 
        // grpFirmware
        // 
        grpFirmware.Controls.Add(comboBoardType);
        grpFirmware.Controls.Add(btnGenFirmware);
        grpFirmware.ForeColor = Color.FromArgb(38, 37, 30);
        grpFirmware.Location = new Point(20, 160);
        grpFirmware.Name = "grpFirmware";
        grpFirmware.Size = new Size(300, 90);
        grpFirmware.TabIndex = 1;
        grpFirmware.TabStop = false;
        grpFirmware.Text = "固件";
        // 
        // comboBoardType
        // 
        comboBoardType.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBoardType.Location = new Point(8, 22);
        comboBoardType.Name = "comboBoardType";
        comboBoardType.Size = new Size(272, 28);
        comboBoardType.TabIndex = 0;
        // 
        // btnGenFirmware
        // 
        btnGenFirmware.BackColor = Color.FromArgb(235, 234, 229);
        btnGenFirmware.FlatAppearance.BorderSize = 0;
        btnGenFirmware.FlatStyle = FlatStyle.Flat;
        btnGenFirmware.Font = new Font("微软雅黑", 9F);
        btnGenFirmware.ForeColor = Color.FromArgb(38, 37, 30);
        btnGenFirmware.Location = new Point(8, 54);
        btnGenFirmware.Name = "btnGenFirmware";
        btnGenFirmware.Size = new Size(130, 28);
        btnGenFirmware.TabIndex = 1;
        btnGenFirmware.Text = "生成固件";
        btnGenFirmware.UseVisualStyleBackColor = false;
        btnGenFirmware.Click += btnGenFirmware_Click;
        // 
        // settingsPanel
        // 
        settingsPanel.BackColor = Color.FromArgb(242, 241, 237);
        settingsPanel.Controls.Add(lblEditorSettings);
        settingsPanel.Controls.Add(chkAutoCompletion);
        settingsPanel.Controls.Add(chkFolding);
        settingsPanel.Controls.Add(chkDebugLog);
        settingsPanel.Controls.Add(lblRunSettings);
        settingsPanel.Controls.Add(chkAutoRunAfterFlash);
        settingsPanel.Controls.Add(chkHighResolutionTiming);
        settingsPanel.Controls.Add(lblNotifySettings);
        settingsPanel.Controls.Add(btnAlertConfig);
        settingsPanel.Controls.Add(chkAutoSaveLog);
        settingsPanel.Controls.Add(lblToolSettings);
        settingsPanel.Controls.Add(btnDrawingBoard);
        settingsPanel.Controls.Add(btnBluetoothSetting);
        settingsPanel.Controls.Add(btnESPConfig);
        settingsPanel.Controls.Add(btnUnpair);
        settingsPanel.Controls.Add(lblAbout);
        settingsPanel.Controls.Add(lblVersion);
        settingsPanel.Controls.Add(btnCheckUpdate);
        settingsPanel.Controls.Add(btnSource);
        settingsPanel.Dock = DockStyle.Fill;
        settingsPanel.Location = new Point(0, 24);
        settingsPanel.Name = "settingsPanel";
        settingsPanel.Size = new Size(604, 657);
        settingsPanel.TabIndex = 0;
        settingsPanel.Visible = false;
        // 
        // lblEditorSettings
        // 
        lblEditorSettings.AutoSize = true;
        lblEditorSettings.Font = new Font("微软雅黑", 11F, FontStyle.Bold);
        lblEditorSettings.ForeColor = Color.FromArgb(38, 37, 30);
        lblEditorSettings.Location = new Point(19, 39);
        lblEditorSettings.Name = "lblEditorSettings";
        lblEditorSettings.Size = new Size(107, 26);
        lblEditorSettings.TabIndex = 0;
        lblEditorSettings.Text = "编辑器设置";
        // 
        // chkAutoCompletion
        // 
        chkAutoCompletion.AutoSize = true;
        chkAutoCompletion.Font = new Font("微软雅黑", 9F);
        chkAutoCompletion.ForeColor = Color.FromArgb(38, 37, 30);
        chkAutoCompletion.Location = new Point(19, 79);
        chkAutoCompletion.Name = "chkAutoCompletion";
        chkAutoCompletion.Size = new Size(121, 24);
        chkAutoCompletion.TabIndex = 1;
        chkAutoCompletion.Text = "代码自动补全";
        chkAutoCompletion.CheckedChanged += chkAutoCompletion_CheckedChanged;
        // 
        // chkFolding
        // 
        chkFolding.AutoSize = true;
        chkFolding.Font = new Font("微软雅黑", 9F);
        chkFolding.ForeColor = Color.FromArgb(38, 37, 30);
        chkFolding.Location = new Point(19, 109);
        chkFolding.Name = "chkFolding";
        chkFolding.Size = new Size(121, 24);
        chkFolding.TabIndex = 2;
        chkFolding.Text = "显示代码折叠";
        chkFolding.CheckedChanged += chkFolding_CheckedChanged;
        // 
        // chkDebugLog
        // 
        chkDebugLog.AutoSize = true;
        chkDebugLog.Font = new Font("微软雅黑", 9F);
        chkDebugLog.ForeColor = Color.FromArgb(38, 37, 30);
        chkDebugLog.Location = new Point(19, 139);
        chkDebugLog.Name = "chkDebugLog";
        chkDebugLog.Size = new Size(121, 24);
        chkDebugLog.TabIndex = 3;
        chkDebugLog.Text = "显示调试信息";
        chkDebugLog.CheckedChanged += chkDebugLog_CheckedChanged;
        // 
        // lblRunSettings
        // 
        lblRunSettings.AutoSize = true;
        lblRunSettings.Font = new Font("微软雅黑", 11F, FontStyle.Bold);
        lblRunSettings.ForeColor = Color.FromArgb(38, 37, 30);
        lblRunSettings.Location = new Point(199, 39);
        lblRunSettings.Name = "lblRunSettings";
        lblRunSettings.Size = new Size(88, 26);
        lblRunSettings.TabIndex = 4;
        lblRunSettings.Text = "运行设置";
        // 
        // chkAutoRunAfterFlash
        // 
        chkAutoRunAfterFlash.AutoSize = true;
        chkAutoRunAfterFlash.Font = new Font("微软雅黑", 9F);
        chkAutoRunAfterFlash.ForeColor = Color.FromArgb(38, 37, 30);
        chkAutoRunAfterFlash.Location = new Point(199, 79);
        chkAutoRunAfterFlash.Name = "chkAutoRunAfterFlash";
        chkAutoRunAfterFlash.Size = new Size(136, 24);
        chkAutoRunAfterFlash.TabIndex = 5;
        chkAutoRunAfterFlash.Text = "烧录后自动运行";
        chkAutoRunAfterFlash.CheckedChanged += chkAutoRunAfterFlash_CheckedChanged;
        //
        // chkHighResolutionTiming
        //
        chkHighResolutionTiming.AutoSize = true;
        chkHighResolutionTiming.Font = new Font("微软雅黑", 9F);
        chkHighResolutionTiming.ForeColor = Color.FromArgb(38, 37, 30);
        chkHighResolutionTiming.Location = new Point(199, 109);
        chkHighResolutionTiming.Name = "chkHighResolutionTiming";
        chkHighResolutionTiming.Size = new Size(100, 24);
        chkHighResolutionTiming.TabIndex = 5;
        chkHighResolutionTiming.Text = "高精度模式";
        chkHighResolutionTiming.CheckedChanged += chkHighResolutionTiming_CheckedChanged;
        // 
        // lblNotifySettings
        // 
        lblNotifySettings.AutoSize = true;
        lblNotifySettings.Font = new Font("微软雅黑", 11F, FontStyle.Bold);
        lblNotifySettings.ForeColor = Color.FromArgb(38, 37, 30);
        lblNotifySettings.Location = new Point(199, 149);
        lblNotifySettings.Name = "lblNotifySettings";
        lblNotifySettings.Size = new Size(88, 26);
        lblNotifySettings.TabIndex = 6;
        lblNotifySettings.Text = "通知设置";
        // 
        // btnAlertConfig
        // 
        btnAlertConfig.BackColor = Color.FromArgb(235, 234, 229);
        btnAlertConfig.FlatAppearance.BorderSize = 0;
        btnAlertConfig.FlatStyle = FlatStyle.Flat;
        btnAlertConfig.Font = new Font("微软雅黑", 9F);
        btnAlertConfig.ForeColor = Color.FromArgb(38, 37, 30);
        btnAlertConfig.Location = new Point(199, 189);
        btnAlertConfig.Name = "btnAlertConfig";
        btnAlertConfig.Size = new Size(85, 30);
        btnAlertConfig.TabIndex = 7;
        btnAlertConfig.Text = "推送配置";
        btnAlertConfig.UseVisualStyleBackColor = false;
        btnAlertConfig.Click += btnAlertConfig_Click;
        // 
        // chkAutoSaveLog
        // 
        chkAutoSaveLog.AutoSize = true;
        chkAutoSaveLog.Font = new Font("微软雅黑", 9F);
        chkAutoSaveLog.ForeColor = Color.FromArgb(38, 37, 30);
        chkAutoSaveLog.Location = new Point(289, 194);
        chkAutoSaveLog.Name = "chkAutoSaveLog";
        chkAutoSaveLog.Size = new Size(121, 24);
        chkAutoSaveLog.TabIndex = 8;
        chkAutoSaveLog.Text = "自动保存日志";
        chkAutoSaveLog.CheckedChanged += chkAutoSaveLog_CheckedChanged;
        // 
        // lblToolSettings
        // 
        lblToolSettings.AutoSize = true;
        lblToolSettings.Font = new Font("微软雅黑", 11F, FontStyle.Bold);
        lblToolSettings.ForeColor = Color.FromArgb(38, 37, 30);
        lblToolSettings.Location = new Point(19, 209);
        lblToolSettings.Name = "lblToolSettings";
        lblToolSettings.Size = new Size(50, 26);
        lblToolSettings.TabIndex = 9;
        lblToolSettings.Text = "工具";
        // 
        // btnDrawingBoard
        // 
        btnDrawingBoard.BackColor = Color.FromArgb(235, 234, 229);
        btnDrawingBoard.FlatAppearance.BorderSize = 0;
        btnDrawingBoard.FlatStyle = FlatStyle.Flat;
        btnDrawingBoard.Font = new Font("微软雅黑", 9F);
        btnDrawingBoard.ForeColor = Color.FromArgb(38, 37, 30);
        btnDrawingBoard.Location = new Point(19, 289);
        btnDrawingBoard.Name = "btnDrawingBoard";
        btnDrawingBoard.Size = new Size(85, 30);
        btnDrawingBoard.TabIndex = 10;
        btnDrawingBoard.Text = "画图工具";
        btnDrawingBoard.UseVisualStyleBackColor = false;
        btnDrawingBoard.Click += btnDrawingBoard_Click;
        // 
        // btnBluetoothSetting
        // 
        btnBluetoothSetting.BackColor = Color.FromArgb(235, 234, 229);
        btnBluetoothSetting.FlatAppearance.BorderSize = 0;
        btnBluetoothSetting.FlatStyle = FlatStyle.Flat;
        btnBluetoothSetting.Font = new Font("微软雅黑", 9F);
        btnBluetoothSetting.ForeColor = Color.FromArgb(38, 37, 30);
        btnBluetoothSetting.Location = new Point(199, 308);
        btnBluetoothSetting.Name = "btnBluetoothSetting";
        btnBluetoothSetting.Size = new Size(85, 30);
        btnBluetoothSetting.TabIndex = 11;
        btnBluetoothSetting.Text = "蓝牙设置";
        btnBluetoothSetting.UseVisualStyleBackColor = false;
        btnBluetoothSetting.Visible = false;
        btnBluetoothSetting.Click += btnBluetoothSetting_Click;
        // 
        // btnESPConfig
        // 
        btnESPConfig.BackColor = Color.FromArgb(235, 234, 229);
        btnESPConfig.FlatAppearance.BorderSize = 0;
        btnESPConfig.FlatStyle = FlatStyle.Flat;
        btnESPConfig.Font = new Font("微软雅黑", 9F);
        btnESPConfig.ForeColor = Color.FromArgb(38, 37, 30);
        btnESPConfig.Location = new Point(19, 249);
        btnESPConfig.Name = "btnESPConfig";
        btnESPConfig.Size = new Size(100, 30);
        btnESPConfig.TabIndex = 12;
        btnESPConfig.Text = "ESP32设置";
        btnESPConfig.UseVisualStyleBackColor = false;
        btnESPConfig.Click += btnESPConfig_Click;
        // 
        // btnUnpair
        // 
        btnUnpair.BackColor = Color.FromArgb(235, 234, 229);
        btnUnpair.FlatAppearance.BorderSize = 0;
        btnUnpair.FlatStyle = FlatStyle.Flat;
        btnUnpair.Font = new Font("微软雅黑", 9F);
        btnUnpair.ForeColor = Color.FromArgb(38, 37, 30);
        btnUnpair.Location = new Point(125, 249);
        btnUnpair.Name = "btnUnpair";
        btnUnpair.Size = new Size(100, 30);
        btnUnpair.TabIndex = 17;
        btnUnpair.Text = "取消蓝牙配对";
        btnUnpair.UseVisualStyleBackColor = false;
        btnUnpair.Click += btnUnpair_Click;
        // 
        // lblAbout
        // 
        lblAbout.AutoSize = true;
        lblAbout.Font = new Font("微软雅黑", 11F, FontStyle.Bold);
        lblAbout.ForeColor = Color.FromArgb(38, 37, 30);
        lblAbout.Location = new Point(19, 330);
        lblAbout.Name = "lblAbout";
        lblAbout.Size = new Size(50, 26);
        lblAbout.TabIndex = 13;
        lblAbout.Text = "关于";
        // 
        // lblVersion
        // 
        lblVersion.AutoSize = true;
        lblVersion.Font = new Font("微软雅黑", 9F);
        lblVersion.ForeColor = Color.FromArgb(140, 139, 132);
        lblVersion.Location = new Point(19, 370);
        lblVersion.Name = "lblVersion";
        lblVersion.Size = new Size(59, 20);
        lblVersion.TabIndex = 14;
        lblVersion.Text = "版本: --";
        // 
        // btnCheckUpdate
        // 
        btnCheckUpdate.BackColor = Color.FromArgb(235, 234, 229);
        btnCheckUpdate.FlatAppearance.BorderSize = 0;
        btnCheckUpdate.FlatStyle = FlatStyle.Flat;
        btnCheckUpdate.Font = new Font("微软雅黑", 9F);
        btnCheckUpdate.ForeColor = Color.FromArgb(38, 37, 30);
        btnCheckUpdate.Location = new Point(19, 400);
        btnCheckUpdate.Name = "btnCheckUpdate";
        btnCheckUpdate.Size = new Size(85, 30);
        btnCheckUpdate.TabIndex = 15;
        btnCheckUpdate.Text = "检查更新";
        btnCheckUpdate.UseVisualStyleBackColor = false;
        btnCheckUpdate.Click += menuItemCheckUpdate_Click;
        // 
        // btnSource
        // 
        btnSource.BackColor = Color.FromArgb(235, 234, 229);
        btnSource.FlatAppearance.BorderSize = 0;
        btnSource.FlatStyle = FlatStyle.Flat;
        btnSource.Font = new Font("微软雅黑", 9F);
        btnSource.ForeColor = Color.FromArgb(38, 37, 30);
        btnSource.Location = new Point(109, 400);
        btnSource.Name = "btnSource";
        btnSource.Size = new Size(85, 30);
        btnSource.TabIndex = 16;
        btnSource.Text = "项目源码";
        btnSource.UseVisualStyleBackColor = false;
        btnSource.Click += menuItemSource_Click;
        // 
        // scriptTitleLabel
        // 
        scriptTitleLabel.BackColor = Color.FromArgb(230, 229, 224);
        scriptTitleLabel.Dock = DockStyle.Top;
        scriptTitleLabel.Font = new Font("微软雅黑", 9F);
        scriptTitleLabel.ForeColor = Color.FromArgb(38, 37, 30);
        scriptTitleLabel.Location = new Point(0, 0);
        scriptTitleLabel.Name = "scriptTitleLabel";
        scriptTitleLabel.Padding = new Padding(4, 0, 0, 0);
        scriptTitleLabel.Size = new Size(604, 24);
        scriptTitleLabel.TabIndex = 3;
        scriptTitleLabel.Text = "未命名脚本";
        scriptTitleLabel.TextAlign = ContentAlignment.MiddleLeft;
        scriptTitleLabel.Visible = false;
        //
        // sideBar
        // 
        sideBar.BackColor = Color.FromArgb(230, 229, 224);
        sideBar.Controls.Add(btnPageLog);
        sideBar.Controls.Add(btnPageEditor);
        sideBar.Controls.Add(btnPageBurn);
        sideBar.Controls.Add(btnPageSettings);
        sideBar.Dock = DockStyle.Left;
        sideBar.Location = new Point(0, 0);
        sideBar.Name = "sideBar";
        sideBar.Size = new Size(40, 681);
        sideBar.TabIndex = 1;
        // 
        // btnPageLog
        // 
        btnPageLog.BackColor = Color.FromArgb(235, 234, 229);
        btnPageLog.FlatAppearance.BorderSize = 0;
        btnPageLog.FlatStyle = FlatStyle.Flat;
        btnPageLog.Font = new Font("Segoe UI Emoji", 14F);
        btnPageLog.ForeColor = Color.FromArgb(38, 37, 30);
        btnPageLog.Location = new Point(2, 10);
        btnPageLog.Name = "btnPageLog";
        btnPageLog.Size = new Size(36, 36);
        btnPageLog.TabIndex = 0;
        btnPageLog.Text = "📄";
        btnPageLog.UseVisualStyleBackColor = false;
        btnPageLog.Click += btnPageLog_Click;
        // 
        // btnPageEditor
        // 
        btnPageEditor.BackColor = Color.FromArgb(230, 229, 224);
        btnPageEditor.FlatAppearance.BorderSize = 0;
        btnPageEditor.FlatStyle = FlatStyle.Flat;
        btnPageEditor.Font = new Font("Segoe UI Emoji", 14F);
        btnPageEditor.ForeColor = Color.FromArgb(38, 37, 30);
        btnPageEditor.Location = new Point(2, 50);
        btnPageEditor.Name = "btnPageEditor";
        btnPageEditor.Size = new Size(36, 36);
        btnPageEditor.TabIndex = 1;
        btnPageEditor.Text = "📝";
        btnPageEditor.UseVisualStyleBackColor = false;
        btnPageEditor.Click += btnPageEditor_Click;
        // 
        // btnPageBurn
        // 
        btnPageBurn.BackColor = Color.FromArgb(230, 229, 224);
        btnPageBurn.FlatAppearance.BorderSize = 0;
        btnPageBurn.FlatStyle = FlatStyle.Flat;
        btnPageBurn.Font = new Font("Segoe UI Emoji", 14F);
        btnPageBurn.ForeColor = Color.FromArgb(38, 37, 30);
        btnPageBurn.Location = new Point(2, 90);
        btnPageBurn.Name = "btnPageBurn";
        btnPageBurn.Size = new Size(36, 36);
        btnPageBurn.TabIndex = 2;
        btnPageBurn.Text = "🔥";
        btnPageBurn.UseVisualStyleBackColor = false;
        btnPageBurn.Click += btnPageBurn_Click;
        // 
        // btnPageSettings
        // 
        btnPageSettings.BackColor = Color.FromArgb(230, 229, 224);
        btnPageSettings.FlatAppearance.BorderSize = 0;
        btnPageSettings.FlatStyle = FlatStyle.Flat;
        btnPageSettings.Font = new Font("Segoe UI Emoji", 14F);
        btnPageSettings.ForeColor = Color.FromArgb(38, 37, 30);
        btnPageSettings.Location = new Point(2, 130);
        btnPageSettings.Name = "btnPageSettings";
        btnPageSettings.Size = new Size(36, 36);
        btnPageSettings.TabIndex = 3;
        btnPageSettings.Text = "⚙";
        btnPageSettings.UseVisualStyleBackColor = false;
        btnPageSettings.Click += btnPageSettings_Click;
        // 
        // rightPanel
        // 
        rightPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        rightPanel.AutoScroll = true;
        rightPanel.BackColor = Color.FromArgb(242, 241, 237);
        rightPanel.Controls.Add(grpScriptRun);
        rightPanel.Controls.Add(grpDevice);
        rightPanel.Controls.Add(grpVideoSource);
        rightPanel.Controls.Add(grpRecord);
        rightPanel.Controls.Add(grpController);
        rightPanel.FlowDirection = FlowDirection.TopDown;
        rightPanel.Location = new Point(3, 10);
        rightPanel.Name = "rightPanel";
        rightPanel.Padding = new Padding(4);
        rightPanel.Size = new Size(241, 668);
        rightPanel.TabIndex = 0;
        rightPanel.WrapContents = false;
        // 
        // grpScriptRun
        // 
        grpScriptRun.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        grpScriptRun.Controls.Add(runStopBtn);
        grpScriptRun.Controls.Add(formatBtn);
        grpScriptRun.Controls.Add(timerLabel);
        grpScriptRun.ForeColor = Color.FromArgb(38, 37, 30);
        grpScriptRun.Location = new Point(14, 7);
        grpScriptRun.Margin = new Padding(10, 3, 10, 0);
        grpScriptRun.Name = "grpScriptRun";
        grpScriptRun.Size = new Size(228, 162);
        grpScriptRun.TabIndex = 0;
        grpScriptRun.TabStop = false;
        grpScriptRun.Text = "脚本运行";
        // 
        // runStopBtn
        // 
        runStopBtn.BackColor = Color.FromArgb(31, 138, 101);
        runStopBtn.FlatAppearance.BorderSize = 0;
        runStopBtn.FlatStyle = FlatStyle.Flat;
        runStopBtn.Font = new Font("微软雅黑", 9F);
        runStopBtn.ForeColor = Color.White;
        runStopBtn.Location = new Point(8, 22);
        runStopBtn.Name = "runStopBtn";
        runStopBtn.Size = new Size(206, 55);
        runStopBtn.TabIndex = 0;
        runStopBtn.Text = "运行脚本";
        runStopBtn.UseVisualStyleBackColor = false;
        runStopBtn.Click += runStopBtn_Click;
        // 
        // formatBtn
        // 
        formatBtn.BackColor = Color.FromArgb(235, 234, 229);
        formatBtn.FlatAppearance.BorderSize = 0;
        formatBtn.FlatStyle = FlatStyle.Flat;
        formatBtn.Font = new Font("微软雅黑", 9F);
        formatBtn.ForeColor = Color.FromArgb(38, 37, 30);
        formatBtn.Location = new Point(8, 85);
        formatBtn.Name = "formatBtn";
        formatBtn.Size = new Size(206, 30);
        formatBtn.TabIndex = 1;
        formatBtn.Text = "格式化";
        formatBtn.UseVisualStyleBackColor = false;
        formatBtn.Click += formatBtn_Click;
        // 
        // timerLabel
        // 
        timerLabel.Font = new Font("Consolas", 12F);
        timerLabel.ForeColor = Color.FromArgb(38, 37, 30);
        timerLabel.Location = new Point(8, 123);
        timerLabel.Name = "timerLabel";
        timerLabel.Size = new Size(206, 30);
        timerLabel.TabIndex = 2;
        timerLabel.Text = "00:00:00";
        timerLabel.TextAlign = ContentAlignment.MiddleCenter;
        // 
        // grpDevice
        // 
        grpDevice.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        grpDevice.Controls.Add(comboComPort);
        grpDevice.Controls.Add(btnAutoConnect);
        grpDevice.Controls.Add(btnManualConnect);
        grpDevice.ForeColor = Color.FromArgb(38, 37, 30);
        grpDevice.Location = new Point(14, 172);
        grpDevice.Margin = new Padding(10, 3, 10, 0);
        grpDevice.Name = "grpDevice";
        grpDevice.Size = new Size(228, 130);
        grpDevice.TabIndex = 1;
        grpDevice.TabStop = false;
        grpDevice.Text = "设备连接";
        // 
        // comboComPort
        // 
        comboComPort.Location = new Point(8, 22);
        comboComPort.Name = "comboComPort";
        comboComPort.Size = new Size(206, 28);
        comboComPort.TabIndex = 0;
        comboComPort.DropDown += comboComPort_DropDown;
        // 
        // btnAutoConnect
        // 
        btnAutoConnect.BackColor = Color.FromArgb(235, 234, 229);
        btnAutoConnect.FlatAppearance.BorderSize = 0;
        btnAutoConnect.FlatStyle = FlatStyle.Flat;
        btnAutoConnect.Font = new Font("微软雅黑", 9F);
        btnAutoConnect.ForeColor = Color.FromArgb(38, 37, 30);
        btnAutoConnect.Location = new Point(8, 54);
        btnAutoConnect.Name = "btnAutoConnect";
        btnAutoConnect.Size = new Size(206, 30);
        btnAutoConnect.TabIndex = 1;
        btnAutoConnect.Text = "自动连接";
        btnAutoConnect.UseVisualStyleBackColor = false;
        btnAutoConnect.Click += btnAutoConnect_Click;
        // 
        // btnManualConnect
        // 
        btnManualConnect.BackColor = Color.FromArgb(235, 234, 229);
        btnManualConnect.FlatAppearance.BorderSize = 0;
        btnManualConnect.FlatStyle = FlatStyle.Flat;
        btnManualConnect.Font = new Font("微软雅黑", 9F);
        btnManualConnect.ForeColor = Color.FromArgb(38, 37, 30);
        btnManualConnect.Location = new Point(8, 90);
        btnManualConnect.Name = "btnManualConnect";
        btnManualConnect.Size = new Size(206, 30);
        btnManualConnect.TabIndex = 2;
        btnManualConnect.Text = "手动连接";
        btnManualConnect.UseVisualStyleBackColor = false;
        btnManualConnect.Click += btnManualConnect_Click;
        // 
        // grpVideoSource
        // 
        grpVideoSource.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        grpVideoSource.Controls.Add(comboVideoSource);
        grpVideoSource.Controls.Add(btnCaptureToggle);
        grpVideoSource.Controls.Add(btnOpenCaptureConsole);
        grpVideoSource.ForeColor = Color.FromArgb(38, 37, 30);
        grpVideoSource.Location = new Point(14, 305);
        grpVideoSource.Margin = new Padding(10, 3, 10, 0);
        grpVideoSource.Name = "grpVideoSource";
        grpVideoSource.Size = new Size(228, 150);
        grpVideoSource.TabIndex = 2;
        grpVideoSource.TabStop = false;
        grpVideoSource.Text = "视频源";
        // 
        // comboVideoSource
        // 
        comboVideoSource.Location = new Point(8, 22);
        comboVideoSource.Name = "comboVideoSource";
        comboVideoSource.Size = new Size(206, 28);
        comboVideoSource.TabIndex = 0;
        comboVideoSource.DropDown += comboVideoSource_DropDown;
        // 
        // btnCaptureToggle
        // 
        btnCaptureToggle.BackColor = Color.FromArgb(235, 234, 229);
        btnCaptureToggle.FlatAppearance.BorderSize = 0;
        btnCaptureToggle.FlatStyle = FlatStyle.Flat;
        btnCaptureToggle.Font = new Font("微软雅黑", 9F);
        btnCaptureToggle.ForeColor = Color.FromArgb(38, 37, 30);
        btnCaptureToggle.Location = new Point(8, 54);
        btnCaptureToggle.Name = "btnCaptureToggle";
        btnCaptureToggle.Size = new Size(206, 30);
        btnCaptureToggle.TabIndex = 1;
        btnCaptureToggle.Text = "连接视频源";
        btnCaptureToggle.UseVisualStyleBackColor = false;
        btnCaptureToggle.Click += btnCaptureToggle_Click;
        // 
        // btnOpenCaptureConsole
        // 
        btnOpenCaptureConsole.BackColor = Color.FromArgb(235, 234, 229);
        btnOpenCaptureConsole.FlatAppearance.BorderSize = 0;
        btnOpenCaptureConsole.FlatStyle = FlatStyle.Flat;
        btnOpenCaptureConsole.Font = new Font("微软雅黑", 9F);
        btnOpenCaptureConsole.ForeColor = Color.FromArgb(38, 37, 30);
        btnOpenCaptureConsole.Location = new Point(8, 90);
        btnOpenCaptureConsole.Name = "btnOpenCaptureConsole";
        btnOpenCaptureConsole.Size = new Size(206, 30);
        btnOpenCaptureConsole.TabIndex = 2;
        btnOpenCaptureConsole.Text = "搜图控制台";
        btnOpenCaptureConsole.UseVisualStyleBackColor = false;
        btnOpenCaptureConsole.Click += btnOpenCaptureConsole_Click;
        // 
        // grpRecord
        // 
        grpRecord.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        grpRecord.Controls.Add(btnRecord);
        grpRecord.Controls.Add(btnRecordPause);
        grpRecord.ForeColor = Color.FromArgb(38, 37, 30);
        grpRecord.Location = new Point(14, 458);
        grpRecord.Margin = new Padding(10, 3, 10, 0);
        grpRecord.Name = "grpRecord";
        grpRecord.Size = new Size(228, 90);
        grpRecord.TabIndex = 3;
        grpRecord.TabStop = false;
        grpRecord.Text = "录制";
        // 
        // btnRecord
        // 
        btnRecord.BackColor = Color.FromArgb(235, 234, 229);
        btnRecord.FlatAppearance.BorderSize = 0;
        btnRecord.FlatStyle = FlatStyle.Flat;
        btnRecord.Font = new Font("微软雅黑", 9F);
        btnRecord.ForeColor = Color.FromArgb(38, 37, 30);
        btnRecord.Location = new Point(8, 22);
        btnRecord.Name = "btnRecord";
        btnRecord.Size = new Size(206, 28);
        btnRecord.TabIndex = 0;
        btnRecord.Text = "录制脚本";
        btnRecord.UseVisualStyleBackColor = false;
        btnRecord.Click += btnRecord_Click;
        // 
        // btnRecordPause
        // 
        btnRecordPause.BackColor = Color.FromArgb(235, 234, 229);
        btnRecordPause.Enabled = false;
        btnRecordPause.FlatAppearance.BorderSize = 0;
        btnRecordPause.FlatStyle = FlatStyle.Flat;
        btnRecordPause.Font = new Font("微软雅黑", 9F);
        btnRecordPause.ForeColor = Color.FromArgb(38, 37, 30);
        btnRecordPause.Location = new Point(8, 54);
        btnRecordPause.Name = "btnRecordPause";
        btnRecordPause.Size = new Size(206, 28);
        btnRecordPause.TabIndex = 1;
        btnRecordPause.Text = "暂停录制";
        btnRecordPause.UseVisualStyleBackColor = false;
        btnRecordPause.Click += btnRecordPause_Click;
        // 
        // grpController
        // 
        grpController.Controls.Add(btnShowController);
        grpController.Controls.Add(btnKeyMapping);
        grpController.ForeColor = Color.FromArgb(38, 37, 30);
        grpController.Location = new Point(14, 551);
        grpController.Margin = new Padding(10, 3, 10, 0);
        grpController.Name = "grpController";
        grpController.Size = new Size(228, 90);
        grpController.TabIndex = 4;
        grpController.TabStop = false;
        grpController.Text = "手柄";
        // 
        // btnShowController
        // 
        btnShowController.BackColor = Color.FromArgb(235, 234, 229);
        btnShowController.FlatAppearance.BorderSize = 0;
        btnShowController.FlatStyle = FlatStyle.Flat;
        btnShowController.Font = new Font("微软雅黑", 9F);
        btnShowController.ForeColor = Color.FromArgb(38, 37, 30);
        btnShowController.Location = new Point(8, 22);
        btnShowController.Name = "btnShowController";
        btnShowController.Size = new Size(206, 28);
        btnShowController.TabIndex = 0;
        btnShowController.Text = "虚拟手柄";
        btnShowController.UseVisualStyleBackColor = false;
        btnShowController.Click += btnShowController_Click;
        // 
        // btnKeyMapping
        // 
        btnKeyMapping.BackColor = Color.FromArgb(235, 234, 229);
        btnKeyMapping.FlatAppearance.BorderSize = 0;
        btnKeyMapping.FlatStyle = FlatStyle.Flat;
        btnKeyMapping.Font = new Font("微软雅黑", 9F);
        btnKeyMapping.ForeColor = Color.FromArgb(38, 37, 30);
        btnKeyMapping.Location = new Point(8, 54);
        btnKeyMapping.Name = "btnKeyMapping";
        btnKeyMapping.Size = new Size(206, 28);
        btnKeyMapping.TabIndex = 1;
        btnKeyMapping.Text = "按键映射";
        btnKeyMapping.UseVisualStyleBackColor = false;
        btnKeyMapping.Click += btnKeyMapping_Click;
        // 
        // statusStrip
        // 
        statusStrip.BackColor = Color.FromArgb(230, 229, 224);
        statusStrip.ImageScalingSize = new Size(20, 20);
        statusStrip.Items.AddRange(new ToolStripItem[] { toolStripStatusLabel1, toolStripStatusLabel2, labelSerialStatus, toolStripStatusLabel3, labelCaptureStatus });
        statusStrip.Location = new Point(0, 709);
        statusStrip.Name = "statusStrip";
        statusStrip.Size = new Size(916, 26);
        statusStrip.TabIndex = 1;
        // 
        // toolStripStatusLabel1
        // 
        toolStripStatusLabel1.AutoSize = false;
        toolStripStatusLabel1.ForeColor = Color.FromArgb(38, 37, 30);
        toolStripStatusLabel1.Name = "toolStripStatusLabel1";
        toolStripStatusLabel1.Size = new Size(300, 20);
        // 
        // toolStripStatusLabel2
        // 
        toolStripStatusLabel2.ForeColor = Color.FromArgb(140, 139, 132);
        toolStripStatusLabel2.Name = "toolStripStatusLabel2";
        toolStripStatusLabel2.Size = new Size(21, 20);
        toolStripStatusLabel2.Text = " | ";
        // 
        // labelSerialStatus
        // 
        labelSerialStatus.ForeColor = Color.FromArgb(140, 139, 132);
        labelSerialStatus.Name = "labelSerialStatus";
        labelSerialStatus.Size = new Size(99, 20);
        labelSerialStatus.Text = "单片机未连接";
        // 
        // toolStripStatusLabel3
        // 
        toolStripStatusLabel3.ForeColor = Color.FromArgb(140, 139, 132);
        toolStripStatusLabel3.Name = "toolStripStatusLabel3";
        toolStripStatusLabel3.Size = new Size(21, 20);
        toolStripStatusLabel3.Text = " | ";
        // 
        // labelCaptureStatus
        // 
        labelCaptureStatus.ForeColor = Color.FromArgb(140, 139, 132);
        labelCaptureStatus.Name = "labelCaptureStatus";
        labelCaptureStatus.Size = new Size(99, 20);
        labelCaptureStatus.Text = "采集卡未连接";
        // 
        // MainForm
        // 
        AutoScaleDimensions = new SizeF(9F, 20F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(242, 241, 237);
        ClientSize = new Size(916, 735);
        Controls.Add(mainSplit);
        Controls.Add(menuStrip);
        Controls.Add(statusStrip);
        Font = new Font("微软雅黑", 9F);
        ForeColor = Color.FromArgb(38, 37, 30);
        Icon = (Icon)resources.GetObject("$this.Icon");
        MainMenuStrip = menuStrip;
        Name = "MainForm";
        Text = "伊机控 EasyCon";
        FormClosing += MainForm_FormClosing;
        menuStrip.ResumeLayout(false);
        menuStrip.PerformLayout();
        mainSplit.Panel1.ResumeLayout(false);
        mainSplit.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)mainSplit).EndInit();
        mainSplit.ResumeLayout(false);
        contentPanel.ResumeLayout(false);
        logPanel.ResumeLayout(false);
        burnPanel.ResumeLayout(false);
        grpBurn.ResumeLayout(false);
        grpFirmware.ResumeLayout(false);
        settingsPanel.ResumeLayout(false);
        settingsPanel.PerformLayout();
        sideBar.ResumeLayout(false);
        rightPanel.ResumeLayout(false);
        grpScriptRun.ResumeLayout(false);
        grpDevice.ResumeLayout(false);
        grpVideoSource.ResumeLayout(false);
        grpRecord.ResumeLayout(false);
        grpController.ResumeLayout(false);
        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    // Top-level
    private MenuStrip menuStrip;
    private SplitContainer mainSplit;
    private Panel sideBar;
    private Panel contentPanel;
    private Panel burnPanel;
    private Panel logPanel;
    private FlowLayoutPanel rightPanel;

    // Menu items
    private ToolStripMenuItem fileMenu;
    private ToolStripMenuItem menuItemNew;
    private ToolStripMenuItem menuItemOpen;
    private ToolStripMenuItem menuItemSave;
    private ToolStripMenuItem menuItemSaveAs;
    private ToolStripMenuItem menuItemClose;
    private ToolStripSeparator toolStripSeparator1;
    private ToolStripMenuItem menuItemExit;
    private ToolStripMenuItem editMenu;
    private ToolStripMenuItem menuItemFindReplace;
    private ToolStripMenuItem menuItemFindNext;
    private ToolStripMenuItem menuItemToggleComment;
    private ToolStripMenuItem scriptMenu;
    private ToolStripMenuItem formatMenuItem;
    private ToolStripMenuItem runMenuItem;
    private ToolStripMenuItem captureMenu;
    private ToolStripMenuItem captureTypeMenu;
    private ToolStripMenuItem setEnvVarMenuItem;
    private ToolStripMenuItem captureHelpMenuItem;
    private ToolStripMenuItem helpMenu;
    private ToolStripMenuItem menuItemFirmwareMode;
    private ToolStripMenuItem menuItemOnlineMode;
    private ToolStripMenuItem menuItemFlashMode;
    private ToolStripMenuItem menuItemScriptSyntax;
    private ToolStripSeparator toolStripSeparator2;
    private ToolStripMenuItem menuItemAbout;

    // Editor
    private WinFormsAvaloniaControlHost editorHost;
    private Label scriptTitleLabel;

    // Sidebar
    private Button btnPageEditor;
    private Button btnPageLog;
    private Button btnPageBurn;
    private Button btnPageSettings;

    // Right panel groups
    private GroupBox grpScriptRun;
    private Button runStopBtn;
    private Button formatBtn;
    private Label timerLabel;

    private GroupBox grpDevice;
    private ComboBox comboComPort;
    private Button btnAutoConnect;
    private Button btnManualConnect;

    private GroupBox grpVideoSource;
    private ComboBox comboVideoSource;
    private Button btnCaptureToggle;
    private Button btnOpenCaptureConsole;

    private GroupBox grpBurn;
    private Button btnRemoteStart;
    private Button btnRemoteStop;
    private Button btnFlash;
    private Button btnFlashClear;

    private GroupBox grpFirmware;
    private ComboBox comboBoardType;
    private Button btnGenFirmware;

    private GroupBox grpRecord;
    private Button btnRecord;
    private Button btnRecordPause;

    private GroupBox grpController;
    private Button btnShowController;
    private Button btnKeyMapping;

    // Log
    private RichLogBox logTxtBox;
    private Button clsLogBtn;

    // Status
    private StatusStrip statusStrip;
    private ToolStripStatusLabel toolStripStatusLabel1;
    private ToolStripStatusLabel toolStripStatusLabel2;
    private ToolStripStatusLabel labelSerialStatus;
    private ToolStripStatusLabel toolStripStatusLabel3;
    private ToolStripStatusLabel labelCaptureStatus;

    // Settings panel
    private Panel settingsPanel;
    private Label lblEditorSettings;
    private CheckBox chkAutoCompletion;
    private CheckBox chkFolding;
    private CheckBox chkDebugLog;
    private Label lblRunSettings;
    private CheckBox chkAutoRunAfterFlash;
    private Label lblNotifySettings;
    private Button btnAlertConfig;
    private CheckBox chkAutoSaveLog;
    private CheckBox chkHighResolutionTiming;
    private Label lblToolSettings;
    private Button btnDrawingBoard;
    private Button btnBluetoothSetting;
    private Button btnESPConfig;
    private Button btnUnpair;
    private Label lblAbout;
    private Label lblVersion;
    private Button btnCheckUpdate;
    private Button btnSource;
}
