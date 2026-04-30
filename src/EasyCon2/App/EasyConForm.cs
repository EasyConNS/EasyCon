using AvaloniaEdit.Folding;
using AvaloniaEdit.Highlighting;
using EasyCon.Core;
using EasyCon.Core.Config;
using EasyCon.Script.Asm;
using EasyCon.WinInput;
using EasyCon2.Avalonia.Core.Editor;
using EasyCon2.Avalonia.Core.VPad;
using EasyCon2.Forms;
using EasyCon2.Forms.win32;
using EasyCon2.Helper;
using EasyCon2.Models;
using EasyCon2.Services;
using EasyCon2.Theme;
using EasyCon2.Views;
using EasyDevice;
using EasyScript;
using GamepadApi;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using AvaColor = Avalonia.Media.Color;
using AvaColors = Avalonia.Media.Colors;
using Resources = EasyCon2.UI.Common.Properties.Resources;

namespace EasyCon2.App
{
    public partial class EasyConForm : Form, IOutputAdapter, IControllerAdapter
    {
        private readonly string VER = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        // Services
        private readonly ScriptService _scriptService = new();
        private readonly DeviceService _deviceService = new();
        private readonly CaptureService _captureService = new();
        private readonly ConfigService _configService = new();
        private readonly AppState _state = new();

        // Editor
        private ScriptEditorControl scriptEditor;
        private VPadService? _vpadService;
        private GamepadManager? _gamepadManager;
        private GamepadMappingConfig _gamepadMappingConfig = new();
        private FoldingManager? _foldingManager;
        private CustomFoldingStrategy? _foldingStrategy;

        // Timer for UI updates
        private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 200 };

        // EasyConForm-specific
        private QqAssist ws = new();
        private AlertDispatcher _alertDispatcher;

        const string FirmwarePath = @"Firmware\";

        private readonly string defaultName = "未命名脚本";
        private string fileName => scriptEditor.FileName == null ? defaultName : Path.GetFileName(scriptEditor.FileName);

        private string curILPath => scriptEditor.FileName == null ? "" : Path.Combine(Path.GetDirectoryName(scriptEditor.FileName), "ImgLabel");

        private readonly List<ToolStripMenuItem> captureTypes = [];

        public AvaColor CurrentLight => AvaColors.White;
        bool IControllerAdapter.IsRunning() => _scriptService.IsRunning;

        public EasyConForm()
        {
            InitializeComponent();
            _configService.Load();
            ThemeManager.Init(_configService.Config.DarkMode);
            _alertDispatcher = new AlertDispatcher(ConfigManager.LoadAlert());
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (editorHost.ContainsFocus)
                return false;
            return base.ProcessDialogKey(keyData);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x0219:
                    switch (m.WParam)
                    {
                        case 0x8000:
                            Debug.WriteLine("dev arrived");
                            break;
                        case 0x8004:
                            Debug.WriteLine("dev removed");
                            break;
                    }
                    break;
            }
            base.WndProc(ref m);
        }

        #region Initialization

        private void EasyConForm_Load(object sender, EventArgs e)
        {
            string displayVersion = VER;
            int plusIndex = VER?.IndexOf('+') ?? -1;
            if (plusIndex > 0)
            {
                displayVersion = VER.Substring(0, plusIndex);
            }
            this.Text = $"伊机控 EasyCon v{displayVersion}  QQ群:946057081";

            comboBoxBoardType.Items.AddRange(Board.SupportedBoards);
            comboBoxBoardType.SelectedIndex = 0;
            InitTheme();
            ApplyTheme();
            InitEditor();
            InitServices();
            InitTimer();

            ThemeManager.ThemeChanged += isDark =>
            {
                Post(() =>
                {
                    ApplyTheme();
                    scriptEditor.IsDarkTheme = isDark;
                });
            };

            comboInputMode.Items.Add("键盘");
            comboInputMode.SelectedIndex = 0;
            InitGamepadManager();

            // 初始化菜单项状态
            代码自动补全ToolStripMenuItem.Checked = _configService.Config.EnableAutoCompletion;
            深色模式ToolStripMenuItem.Checked = _configService.Config.DarkMode;
            高精度模式ToolStripMenuItem.Checked = _configService.Config.HighResolutionTiming;

#if DEBUG
            蓝牙ToolStripMenuItem.Visible = true;
#endif

            InitCaptureDevices();
            InitCaptureTypes();
        }

        private void InitEditor()
        {
            scriptEditor = ScriptEditorHost.CreateControl();
            scriptEditor.EditorTextChanged += textBoxScript_TextChanged;
            scriptEditor.FileDropped += (path) =>
            {
                try
                {
                    if (!FileClose()) return;
                    FileOpen(path);
                }
                catch
                {
                    MessageBox.Show("打开失败了，原因未知", "打开脚本");
                }
            };

            _foldingManager = FoldingManager.Install(scriptEditor.TextArea);
            _foldingStrategy = new CustomFoldingStrategy();
            _foldingStrategy.UpdateFoldings(_foldingManager, scriptEditor.TextDocument);

            scriptEditor.SetImgLabelProvider(() => _captureService.LoadedLabels.Select(il => il.name));
            scriptEditor.EnableAutoCompletion = _configService.Config.EnableAutoCompletion;
            scriptEditor.SetFontSize(_configService.Config.EditorFontSize);
            scriptEditor.FontSizeChanged += size =>
            {
                _configService.Config.EditorFontSize = size;
                _configService.Save();
            };

            editorHost.Content = scriptEditor;
        }

        private void InitServices()
        {
            _deviceService.DebugLogEnabled = 显示调试信息ToolStripMenuItem.Checked;

            _scriptService.RunningStateChanged += running =>
                Post(() =>
                {
                    _state.ScriptRunning = running;
                    if (!running) _state.ScriptStartTime = DateTime.MinValue;
                    runStopBtn.Text = running ? "终止脚本" : "运行脚本";
                    runStopBtn.BackColor = running ? ThemeManager.Error : ThemeManager.Success;
                    runStopBtn.Enabled = true;
                    编译ToolStripMenuItem.Enabled = !running;
                    执行ToolStripMenuItem.Enabled = !running;
                    labelTimer.ForeColor = running ? ThemeManager.Success : ThemeManager.WhiteOrLight;
                });

            _scriptService.LogOutput += (msg, color) =>
                Post(() =>
                {
                    logTxtBox.Print(msg, color);
                    if (msg == "-- 运行结束 --") SystemSounds.Beep.Play();
                    else if (msg == "-- 运行终止 --") SystemSounds.Beep.Play();
                    else if (msg == "-- 运行出错 --") SystemSounds.Hand.Play();
                });

            _scriptService.StatusChanged += msg =>
                Post(() => toolStripStatusLabel1.Text = msg);

            _deviceService.ConnectionStateChanged += connected =>
                Post(() =>
                {
                    labelSerialStatus.Text = connected ? "单片机已连接" : "单片机未连接";
                    labelSerialStatus.ForeColor = connected ? ThemeManager.Success : ThemeManager.TextPrimary;
                });

            _deviceService.Log += msg =>
                Post(() => logTxtBox.Print(msg, null));

            _deviceService.StatusChanged += msg =>
                Post(() => toolStripStatusLabel1.Text = msg);

            _captureService.ConnectionStateChanged += connected =>
                Post(() =>
                {
                    labelCaptureStatus.Text = connected ? "采集卡已连接" : "采集卡未连接";
                    labelCaptureStatus.ForeColor = connected ? ThemeManager.Success : ThemeManager.TextPrimary;
                    btnCaptureToggle.Text = connected ? "断开视频源" : "连接视频源";
                });

            _captureService.StatusChanged += msg =>
                Post(() => toolStripStatusLabel1.Text = msg);
        }

        private void InitTimer()
        {
            _uiTimer.Tick += (_, _) =>
            {
                // Script title
                scriptTitleLabel.Text = scriptEditor.IsModified ? $"{fileName}(已编辑)" : fileName;

                // Timer display
                if (_state.ScriptRunning)
                    labelTimer.Text = _state.TimerText;

                // Recording real-time update
                if (_deviceService.RecordState == RecordState.RECORD_START)
                {
                    buttonRecord.Text = "停止录制";
                    scriptEditor.Text = _deviceService.GetRecordScript();
                    scriptEditor.ScrollToHome();
                }
            };
            _uiTimer.Start();
        }

        #endregion

        #region UI Helpers

        private void Post(Action action)
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(action);
        }

        private void StatusShowLog(string str)
        {
            Post(() => toolStripStatusLabel1.Text = str);
        }

        private void ScriptSelectLine(int index)
        {
            scriptEditor.ScrollToLine(index);
        }

        private Board GetSelectedBoard()
        {
            return comboBoxBoardType.SelectedItem as Board;
        }

        private void EnableConnBtn(bool show = true)
        {
            buttonSerialPortSearch.Enabled = show;
            buttonSerialPortConnect.Enabled = show;
        }

        #endregion

        #region IOutputAdapter / IControllerAdapter

        public async void Print(string message, bool newline = true) =>
            logTxtBox.Print(message, newline);

        public void Alert(string message)
        {
            Task.Run(async () =>
            {
                try
                {
                    _alertDispatcher.OnResult += (_, result) => Print(result);
                    await _alertDispatcher.DispatchAsync(message);
                }
                catch (Exception e)
                {
                    Print($"推送失败:{e.Message}");
                }
            });
        }

        #endregion

        #region Capture

        private void InitCaptureDevices()
        {
            var devs = CaptureService.GetVideoSources();
            comboVideoSource.Items.Clear();
            foreach (var d in devs)
            {
                comboVideoSource.Items.Add(d);
            }
            if (comboVideoSource.Items.Count > 0)
                comboVideoSource.SelectedIndex = 0;
        }

        private void InitCaptureTypes()
        {
            var types = CaptureService.GetCaptureTypes();
            采集卡类型ToolStripMenuItem.DropDownItems.Clear();
            foreach (var (name, value) in types)
            {
                var item = new ToolStripMenuItem();
                采集卡类型ToolStripMenuItem.DropDownItems.Add(item);
                item.Checked = false;
                item.CheckState = CheckState.Unchecked;
                item.Name = $"tsmCapType{name}";
                item.Size = new Size(180, 22);
                item.Text = name;
                item.Click += new EventHandler(this.DeviceTypeItem_Click);
                item.Tag = value;
                if (name == _configService.Config.CaptureType)
                    item.Checked = true;
                captureTypes.Add(item);
            }
        }

        private void comboVideoSource_DropDown(object sender, EventArgs e)
        {
            InitCaptureDevices();
        }

        private void btnCaptureToggle_Click(object sender, EventArgs e)
        {
            if (_captureService.IsConnected)
            {
                _captureService.Disconnect();
                return;
            }

            if (comboVideoSource.SelectedItem == null)
            {
                MessageBox.Show("请先选择视频源");
                return;
            }

            var source = ((string name, int index))comboVideoSource.SelectedItem;
            int captureType = 0;
            foreach (var item in captureTypes)
            {
                if (item.Checked)
                    captureType = (int)item.Tag;
            }

            _captureService.Connect(source.index, captureType, curILPath);
        }

        private void btnOpenCaptureConsole_Click(object sender, EventArgs e)
        {
            _captureService.ShowCaptureConsole();
        }

        #endregion

        #region Serial Connection

        private void ComPort_DropDown(object sender, EventArgs e)
        {
            var ports = _deviceService.GetPortNames();
            ComPort.Items.Clear();
            foreach (var portName in ports)
            {
                ComPort.Items.Add(portName);
                Debug.WriteLine(portName);
            }
        }

        private async void buttonSerialPortSearch_Click(object sender, EventArgs e)
        {
            EnableConnBtn(false);
            var (success, connectedPort) = await _deviceService.AutoConnectAsync();
            if (success)
            {
                ComPort.Text = connectedPort;
            }
            else
            {
                MessageBox.Show(@"找不到设备！请确认：
1.已经为单片机烧好固件
2.已经连好TTL线（详细使用教程见群946057081文档）
3.以上两步操作正确的话，点击搜索时单片机上的TX灯会闪烁
4.如果用的是CH340G，换一下帽子让3v3与S1相连（默认可能是5V与S1相连）
5.以上步骤都完成后重启程序再试", "连接失败");
            }
            EnableConnBtn();
        }

        private async void buttonSerialPortConnect_Click(object sender, EventArgs e)
        {
            EnableConnBtn(false);
            var success = await _deviceService.ManualConnectAsync(ComPort.Text);
            if (!success)
            {
                MessageBox.Show($@"连接失败！端口 {ComPort.Text} 不存在、无法使用或已被占用。
请在设备管理器确认 TTL 所在串口正确识别。关闭其他占用USB的程序，并重启软件再试。", "连接失败");
            }
            EnableConnBtn();
        }

        #endregion

        #region File Operations

        private bool FileOpen(string path = "")
        {
            openFileDialog.Title = "打开";
            openFileDialog.RestoreDirectory = true;
            openFileDialog.Filter = "脚本文件 (*.txt，*.ecs)|*.txt;*.ecs|所有文件(*.*)|*.*";
            openFileDialog.FileName = string.Empty;

            string _currentFile = path;
            if (path == "")
            {
                if (openFileDialog.ShowDialog() != DialogResult.OK)
                    return false;
                if (!FileClose())
                    return false;
                _currentFile = openFileDialog.FileName;
            }

            scriptEditor.Load(_currentFile);
            scriptEditor.IsModified = false;
            scriptEditor.FileName = _currentFile;
            scriptEditor.SyntaxHighlighting = EcsHighlightingLoader.GetByExtension(Path.GetExtension(_currentFile));
            StatusShowLog("文件已打开");
            _captureService.LoadImgLabels(curILPath);
            return true;
        }

        private bool FileSave(bool saveAs = false)
        {
            saveFileDialog.Title = saveAs ? "另存为" : "保存";
            saveFileDialog.RestoreDirectory = true;
            saveFileDialog.Filter = "脚本文件 (*.txt，*.ecs)|*.txt;*.ecs|所有文件(*.*)|*.*";
            saveFileDialog.FileName = "未命名脚本.txt";

            if (saveAs || scriptEditor.FileName == null)
            {
                if (saveFileDialog.ShowDialog() != DialogResult.OK)
                    return false;
                scriptEditor.FileName = saveFileDialog.FileName;
            }
            scriptEditor.Save(scriptEditor.FileName);
            scriptEditor.IsModified = false;
            StatusShowLog("文件已保存");
            return true;
        }

        private bool FileClose()
        {
            if (scriptEditor.IsModified)
            {
                var r = MessageBox.Show("文件已编辑，是否保存？", "", MessageBoxButtons.YesNoCancel);
                if (r == DialogResult.Cancel)
                    return false;
                else if (r == DialogResult.Yes)
                {
                    if (!FileSave())
                        return false;
                }
            }
            scriptEditor.FileName = null;
            scriptEditor.Clear();
            scriptEditor.IsModified = false;
            StatusShowLog("文件已关闭");
            return true;
        }
        #endregion

        #region Controller

        private void ShowControllerHelp()
        {
            new HelpTxtDialog(@"鼠标左键：启用/禁用
鼠标右键：拖动移动位置，右键点击重置初始位置
鼠标中键：禁用并隐藏

（注意：在有脚本远程运行的情况下无法使用）", "关于虚拟手柄").Show();
        }

        private void comboInputMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            var idx = comboInputMode.SelectedIndex;
            buttonKeyMapping.Enabled = idx == 0;
            buttonRecord.Enabled = idx == 0;
        }

        private void InitGamepadManager()
        {
            if (_gamepadManager != null) return;
            _gamepadManager = new GamepadManager();
            _gamepadManager.DeviceConnected += _ => RefreshInputModeList();
            _gamepadManager.DeviceDisconnected += _ => RefreshInputModeList();
            _gamepadManager.Start();
        }

        private void RefreshInputModeList()
        {
            if (!IsHandleCreated) return;
            BeginInvoke(() =>
            {
                var selected = comboInputMode.SelectedIndex;
                comboInputMode.Items.Clear();
                comboInputMode.Items.Add("键盘");
                if (_gamepadManager != null)
                {
                    foreach (var d in _gamepadManager.GetConnectedDevices())
                        comboInputMode.Items.Add($"手柄 {d.Index}");
                }
                if (selected >= 0 && selected < comboInputMode.Items.Count)
                    comboInputMode.SelectedIndex = selected;
                else
                    comboInputMode.SelectedIndex = 0;
            });
        }

        private void buttonShowController_Click(object sender, EventArgs e)
        {
            if (!_deviceService.IsConnected)
            {
                MessageBox.Show("请先连接设备");
                return;
            }

            _vpadService ??= new VPadService(_deviceService.Device, this);

            if (_vpadService.IsActive)
            {
                buttonShowController.Text = "连接";
                _vpadService.HideOverlay();
                return;
            }

            var idx = comboInputMode.SelectedIndex;
            if (idx == 0)
            {
                _vpadService.SwitchInput(new KeyboardInputBinder(_deviceService.Device, _configService.KeyMapping));
            }
            else if (idx > 0 && _gamepadManager != null)
            {
                var gIdx = idx - 1;
                var binder = new GamepadInputBinder(_gamepadManager, gIdx, _deviceService.Device, _gamepadMappingConfig);
                _vpadService.SwitchInput(binder);
            }
            buttonShowController.Text = "断开";
            _vpadService.Show();

            // Register Escape key to hide overlay
            _vpadService.RegisterEscapeKey(
                () =>
                {
                    if (_vpadService is not { IsActive: true }) return false;

                    buttonShowController.Text = "连接";
                    _vpadService.HideOverlay();
                    return true;
                },
                () => false);

            if (_configService.Config.ShowControllerHelp)
            {
                ShowControllerHelp();
                _configService.Config.ShowControllerHelp = false;
                _configService.Save();
            }
        }

        private void buttonKeyMapping_Click(object sender, EventArgs e)
        {
            using var formKeyMapping = new FormKeyMapping(_configService.KeyMapping);
            if (formKeyMapping.ShowDialog() == DialogResult.OK)
            {
                _configService.UpdateKeyMapping(formKeyMapping.KeyMapping);
                RegisterKeys();
            }
        }

        private void buttonControllerHelp_Click(object sender, EventArgs e)
        {
            ShowControllerHelp();
        }

        private void RegisterKeys()
        {
            _vpadService?.UpdateKeyMapping(_configService.KeyMapping);
            _vpadService?.RegisterEscapeKey(
                () =>
                {
                    if (_vpadService is not { IsActive: true }) return false;

                    buttonShowController.Text = "连接";
                    _vpadService.HideOverlay();
                    return true;
                },
                () => false);
        }

        #endregion

        #region Script Operations

        private bool ScriptCompile()
        {
            var externalGetters = _captureService.BuildExternalGetters();
            var (success, errorLine, error) = _scriptService.Compile(
                scriptEditor.Text, scriptEditor.FileName, externalGetters);

            if (!success)
            {
                if (errorLine != null)
                    MessageBox.Show($"{errorLine}：{error}", "脚本编译出错");
                else
                    MessageBox.Show(error, "编译出错");
                return false;
            }

            StatusShowLog("编译完成");
            return true;
        }

        private void ScriptRun()
        {
            var externalGetters = _captureService.BuildExternalGetters();
            var (success, errorLine, error) = _scriptService.Compile(
                scriptEditor.Text, scriptEditor.FileName, externalGetters);

            if (!success)
            {
                if (errorLine != null)
                    MessageBox.Show($"{errorLine}：{error}", "脚本编译出错");
                else
                    MessageBox.Show(error, "编译出错");
                return;
            }

            if (_scriptService.HasKeyAction)
            {
                if (!_deviceService.IsConnected)
                {
                    MessageBox.Show("需要连接单片机才能运行脚本", "执行脚本");
                    return;
                }
                if (!CheckFwVersion())
                    return;
                if (!_deviceService.RemoteStop())
                {
                    MessageBox.Show("需要先停止烧录脚本运行，请点击<远程停止>按钮");
                    return;
                }
            }

            _vpadService?.Deactivate();
            _state.ScriptStartTime = DateTime.Now;
            _scriptService.Run(this, new GamePadAdapter(_deviceService.Device, _configService.Config.HighResolutionTiming));
        }

        private bool CheckFwVersion()
        {
            if (_deviceService.GetVersion() < Board.Version)
            {
                StatusShowLog("需要更新固件");
                SystemSounds.Hand.Play();
                MessageBox.Show("固件版本不符，请重新刷入" + FirmwarePath);
                return false;
            }
            return true;
        }

        private byte[]? ScriptBuild()
        {
            return _scriptService.Assemble(烧录自动运行ToolStripMenuItem.Checked);
        }

        private bool GenerateFirmware(Board board)
        {
            if (!ScriptCompile())
                return false;
            try
            {
                StatusShowLog("开始生成固件...");
                var bytes = ScriptBuild();
                if (bytes == null)
                {
                    StatusShowLog("固件生成失败");
                    SystemSounds.Hand.Play();
                    MessageBox.Show("编译失败！");
                    return false;
                }
                var filename = board.GenerateFirmware(bytes);
                StatusShowLog("固件生成完毕");
                SystemSounds.Beep.Play();
                MessageBox.Show("固件生成完毕！已保存为" + Path.GetFileName(filename));
                return true;
            }
            catch (AssembleException ex)
            {
                StatusShowLog("固件生成失败");
                SystemSounds.Hand.Play();
                MessageBox.Show(ex.Message);
            }
            catch (Exception ex)
            {
                StatusShowLog("固件生成失败");
                SystemSounds.Hand.Play();
                MessageBox.Show("固件生成失败！" + ex.Message);
            }
            return false;
        }

        private bool ScriptFlash(int maxSize = 0)
        {
            try
            {
                StatusShowLog("开始烧录...");
                var bytes = ScriptBuild();
                if (bytes == null)
                {
                    StatusShowLog("烧录失败");
                    SystemSounds.Hand.Play();
                    MessageBox.Show("编译失败！");
                    return false;
                }
                if (bytes.Length > maxSize)
                    throw new Exception("长度超出限制");
                if (_deviceService.Flash(bytes))
                {
                    StatusShowLog("烧录完毕");
                    SystemSounds.Beep.Play();
                    MessageBox.Show($"烧录完毕！已使用存储空间({bytes.Length}/{maxSize})");
                    return true;
                }
                throw new Exception("请检查设备连接后重试");
            }
            catch (AssembleException ex)
            {
                StatusShowLog("烧录失败");
                SystemSounds.Hand.Play();
                MessageBox.Show("编译失败！" + ex.Message);
            }
            catch (Exception ex)
            {
                StatusShowLog("烧录失败");
                SystemSounds.Hand.Play();
                MessageBox.Show("烧录失败！" + ex.Message);
            }
            return false;
        }

        #endregion

        #region Script Controls

        private void compileButton_Click(object sender, EventArgs e)
        {
            var externalGetters = _captureService.BuildExternalGetters();
            var (success, formattedCode, errorLine, error) = _scriptService.Format(
                scriptEditor.Text, scriptEditor.FileName, externalGetters);

            if (success)
            {
                SystemSounds.Beep.Play();
                scriptEditor.Text = formattedCode;
                scriptEditor.Select(0, 0);
            }
            else
            {
                SystemSounds.Hand.Play();
                StatusShowLog("编译失败");
                if (errorLine != null)
                    MessageBox.Show($"{errorLine}：{error}", "脚本编译出错");
                else
                    MessageBox.Show(error, "编译出错");
            }
        }

        private void 执行ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!_scriptService.IsRunning)
            {
                ScriptRun();
            }
        }

        private void textBoxScript_TextChanged(object? sender, EventArgs e)
        {
            if (显示折叠ToolStripMenuItem.Checked && _foldingStrategy != null && _foldingManager != null)
                _foldingStrategy.UpdateFoldings(_foldingManager, scriptEditor.TextDocument);
            _scriptService.Reset();
        }

        private void buttonScriptRunStop_Click(object sender, EventArgs e)
        {
            runStopBtn.Enabled = false;
            if (!_scriptService.IsRunning)
            {
                if (scriptEditor.FileName != null && scriptEditor.IsModified)
                {
                    MessageBox.Show("您还没有保存脚本，请先保存后再运行");
                }
                else
                {
                    ScriptRun();
                }
            }
            else
            {
                _scriptService.Stop();
            }
            runStopBtn.Enabled = true;
        }

        private async void buttonGenerateFirmware_Click(object sender, EventArgs e)
        {
            genFwButton.Enabled = false;
            GenerateFirmware(GetSelectedBoard());
            genFwButton.Enabled = true;
        }

        private void buttonFlash_Click(object sender, EventArgs e)
        {
            if (!_deviceService.IsConnected)
                return;

            if (!CheckFwVersion())
                return;

            if (!ScriptCompile())
                return;

            ScriptFlash(GetSelectedBoard().DataSize);
        }

        private void buttonRemoteStart_Click(object sender, EventArgs e)
        {
            if (!_deviceService.IsConnected)
                return;
            if (_deviceService.RemoteStart())
            {
                SystemSounds.Beep.Play();
                StatusShowLog("远程运行已开始");
            }
            else
            {
                SystemSounds.Hand.Play();
                StatusShowLog("远程运行失败");
            }
        }

        private void buttonRemoteStop_Click(object sender, EventArgs e)
        {
            if (!_deviceService.IsConnected)
                return;
            if (_deviceService.RemoteStop())
            {
                SystemSounds.Beep.Play();
                StatusShowLog("远程运行已停止");
            }
            else
            {
                SystemSounds.Hand.Play();
                StatusShowLog("远程停止失败");
            }
        }

        private void buttonFlashClear_Click(object sender, EventArgs e)
        {
            if (!_deviceService.IsConnected)
            {
                StatusShowLog("还未准备好烧录");
                SystemSounds.Hand.Play();
                return;
            }

            if (!_deviceService.Flash(HexWriter.EmptyAsm))
            {
                StatusShowLog("烧录失败");
                SystemSounds.Hand.Play();
                MessageBox.Show("烧录失败！请检查设备连接后重试");
            }
            else
            {
                StatusShowLog("清除完毕");
                SystemSounds.Beep.Play();
                MessageBox.Show("清除完毕");
            }
        }

        private void buttonRecord_Click(object sender, EventArgs e)
        {
            if (_deviceService.RecordState == RecordState.RECORD_STOP)
            {
                if (!_deviceService.IsConnected)
                    return;
                if (_vpadService != null)
                {
                    RegisterKeys();
                    _vpadService.Show();
                }
                buttonRecord.Text = "停止录制";
                buttonRecordPause.Enabled = true;
                scriptEditor.IsReadOnly = true;
                _deviceService.StartRecord();
            }
            else
            {
                Debug.Write("stop");
                buttonRecord.Text = "录制脚本";
                buttonRecordPause.Enabled = false;
                scriptEditor.IsReadOnly = false;
                _deviceService.StopRecord();
            }
        }

        private void buttonRecordPause_Click(object sender, EventArgs e)
        {
            if (_deviceService.RecordState == RecordState.RECORD_START)
            {
                _deviceService.PauseRecord();
            }
        }

        #endregion

        #region Menu Handlers

        private void 新建ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FileClose();
            StatusShowLog("新建完毕");
        }

        private void 打开ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FileOpen();
        }

        private void 保存ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FileSave();
        }

        private void 另存为ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FileSave(true);
        }

        private void 关闭ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FileClose();
        }

        private void 退出ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void EasyConForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = !FileClose();
            _captureService.Disconnect();
        }

        private void 查找替換ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            scriptEditor.OpenSearchPanel();
        }

        private void 查找下一个ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // SearchPanel 内部处理 F3 快捷键
        }

        private void DeviceTypeItem_Click(object sender, EventArgs e)
        {
            foreach (var item in captureTypes)
                item.Checked = false;
            ToolStripMenuItem selected = (ToolStripMenuItem)sender;
            selected.Checked = true;
            _configService.Config.CaptureType = selected.Text;
            Debug.WriteLine(selected.Text);
            _configService.Save();
        }

        private void 搜图说明ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show(@"默认采集卡类型选择any，会自动选择合适的采集卡
- 常见采集卡类型是DSHOW，MSMF，DC1394等
- obs30+版本已支持内置虚拟摄像头，无需安装额外插件
- 如果出现黑屏、颜色不正确等情况，请切换其他采集卡类型，然后重新打开
- 如果遇到搜图卡顿问题可尝试点击一次<设置环境变量>菜单
- 详细使用教程见群946057081文档", "采集卡");
        }

        private void 显示调试信息ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            显示调试信息ToolStripMenuItem.Checked = !显示调试信息ToolStripMenuItem.Checked;
            _deviceService.DebugLogEnabled = 显示调试信息ToolStripMenuItem.Checked;
        }

        private void 联机模式ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show(@"- 使用电脑控制单片机的模式
- 可视化运行，一键切换脚本（即将实装）
- 无需反复刷固件
- 支持超长脚本
- 可使用虚拟手柄，用键盘玩游戏

详细使用教程见群946057081文档", "联机模式");
        }

        private void 烧录模式ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show(@"- 连线烧录后脱机运行的模式
- 独立挂机，即插即用
- 一键烧录，可控运行
- 无需反复刷固件
- 支持极限效率脚本

详细使用教程见群946057081文档", "烧录模式");
        }

        private void 固件模式ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show(@"- 生成固件后手动刷入单片机的模式
- 独立挂机，即插即用
- 支持极限效率脚本
- 不需要任何额外配件

详细使用教程见群946057081文档", "固件模式");
        }

        private void 关于ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show($@"伊机控 v{VER}  QQ群:946057081

Copyright © 2020. 铃落(Nukieberry)
Copyright © 2021. elmagnifico
Copyright © 2025. 卡尔(ca1e)", "关于");
        }

        private void 项目源码ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://github.com/EasyConNS/EasyCon") { UseShellExecute = true });
        }

        private void 脚本自动运行ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var menu = (ToolStripMenuItem)sender;
            menu.Checked = !menu.Checked;
        }

        private void 推送设置ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using var form = new AlertConfigForm(RefreshAlert);
            form.ShowDialog(this);
        }

        public void RefreshAlert()
        {
            _alertDispatcher = new AlertDispatcher(ConfigManager.LoadAlert());
        }

        private void 代码自动补全ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var menu = (ToolStripMenuItem)sender;
            _configService.Config.EnableAutoCompletion = menu.Checked;
            scriptEditor.EnableAutoCompletion = menu.Checked;
            _configService.Save();
        }

        private void 设备驱动配置ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using var btform = new BTDeviceForm();
            btform.ShowDialog();
        }

        private void 手柄设置ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using var controllerConfig = new ESPConfig(_deviceService.Device);
            controllerConfig.ShowDialog();
        }

        private void 取消配对ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!_deviceService.IsConnected)
                return;
            if (_deviceService.UnPair())
            {
                SystemSounds.Beep.Play();
                StatusShowLog("取消配对成功");
            }
            else
            {
                SystemSounds.Hand.Play();
                StatusShowLog("取消配对失败");
            }
        }

        private void 喷射ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var board = new DrawingBoard(_deviceService.Device);
            board.Show();
        }

        private void 自由画板鼠标代替摇杆ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Mouse mouse = new(_deviceService.Device);
            mouse.Show();
        }

        private void 设置环境变量ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Environment.SetEnvironmentVariable("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS", "0");
            var oc = Environment.GetEnvironmentVariable("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS");
            StatusShowLog($"环境变量设置成功：{oc}");
        }

        private void 检查更新ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Task.Run(async () =>
            {
                try
                {
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(5);

                    var data = await client.GetStringAsync("https://gitee.com/api/v5/repos/EasyConNS/EasyCon/tags");
                    var ver = JsonSerializer.Deserialize<VerInfo[]>(data);
                    if (ver == null)
                        return;

                    var info = new VersionParser(ver, Assembly.GetExecutingAssembly().GetName().Version);
                    var msg = info.IsNewVersion ? $"发现新版本{info.NewVer}，快去群文件里看看吧" : "暂时没有发现新版本";
                    Invoke(() =>
                    {
                        MessageBox.Show(msg);
                    });
                }
                catch (Exception e)
                {
                    Debug.WriteLine($"update failed:{e.Message}");
                }
            });
        }

        private void 显示折叠ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var menu = (ToolStripMenuItem)sender;
            menu.Checked = !menu.Checked;
            if (menu.Checked)
                _foldingStrategy?.UpdateFoldings(_foldingManager!, scriptEditor.TextDocument);
            else
                _foldingManager?.Clear();
        }

        private void 注释取消注释ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            int startOffset = scriptEditor.SelectionStart;
            int endOffset = startOffset + scriptEditor.SelectionLength;

            var doc = scriptEditor.TextDocument;
            var startLine = doc.GetLineByOffset(startOffset);
            var endLine = doc.GetLineByOffset(endOffset);

            var docomment = false;
            for (int lineNum = endLine.LineNumber; lineNum >= startLine.LineNumber; lineNum--)
            {
                var line = doc.GetLineByNumber(lineNum);
                if (Scripter.CanComment(doc.GetText(line)))
                {
                    docomment = true;
                    break;
                }
            }

            using (doc.RunUpdate())
            {
                for (int lineNum = endLine.LineNumber; lineNum >= startLine.LineNumber; lineNum--)
                {
                    var line = doc.GetLineByNumber(lineNum);
                    var text = doc.GetText(line);
                    text = Scripter.ToggleComment(text, docomment);
                    doc.Replace(line, text);
                }
            }
        }

        private void 脚本语法ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            new HelpTxtDialog(Resources.scriptdoc).Show();
        }

        private void clsLogBtn_Click(object sender, EventArgs e)
        {
            logTxtBox.ClearLog();
        }

        bool WSRun = false;
        private bool StartWebSocket()
        {
            try
            {
                ws.Connect();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"websocket connect failed:{ex.Message}");
                return false;
            }
            Task task = Task.Run(() =>
            {
                WSRun = true;
                while (WSRun)
                {
                    try
                    {
                        if (!ws.IsConnect) ws.Connect();
                        Thread.Sleep(1000);
                    }
                    catch
                    {
                        ws.Connect();
                        Debug.WriteLine("ws lost connection,retry");
                    }
                }
            });
            return true;
        }

        #endregion

        #region Theme

        private void InitTheme()
        {
            var colors = ThemeManager.IsDark
                ? (ProfessionalColorTable)new DarkMenuColors()
                : new WarmMenuColors();
            var renderer = new ToolStripProfessionalRenderer(colors);
            renderer.RoundedEdges = false;
            menuStrip.Renderer = renderer;
            statusStrip.Renderer = renderer;
        }

        private void ApplyTheme()
        {
            SuspendLayout();

            BackColor = ThemeManager.PageBackground;
            ForeColor = ThemeManager.TextPrimary;

            // Always-dark surfaces
            logTxtBox.BackColor = ThemeManager.DarkSurfaceBackground;
            logTxtBox.ForeColor = ThemeManager.DarkSurfaceText;
            labelTimer.BackColor = ThemeManager.DarkSurfaceBackground;

            // Status labels
            labelSerialStatus.BackColor = ThemeManager.SurfaceBackground;
            labelCaptureStatus.BackColor = ThemeManager.SurfaceBackground;

            // Table layout panels
            tblSerialPort.BackColor = ThemeManager.SurfaceBackground;
            tblVideoSource.BackColor = ThemeManager.SurfaceBackground;
            tblController.BackColor = ThemeManager.SurfaceBackground;

            // Run button
            runStopBtn.BackColor = ThemeManager.Success;
            runStopBtn.ForeColor = ThemeManager.WhiteOrLight;

            // ComboBoxes
            var comboBg = ThemeManager.IsDark ? ThemeManager.SurfaceBackground : SystemColors.Window;
            var comboFg = ThemeManager.IsDark ? ThemeManager.TextPrimary : SystemColors.WindowText;
            ComPort.BackColor = comboBg;
            ComPort.ForeColor = comboFg;
            comboVideoSource.BackColor = comboBg;
            comboVideoSource.ForeColor = comboFg;
            comboInputMode.BackColor = comboBg;
            comboInputMode.ForeColor = comboFg;
            comboBoxBoardType.BackColor = comboBg;
            comboBoxBoardType.ForeColor = comboFg;

            InitTheme();

            // Set ForeColor on all menu/status items
            var menuTextColor = ThemeManager.IsDark ? ThemeManager.TextPrimary : SystemColors.ControlText;
            SetItemsForeColor(menuStrip.Items, menuTextColor);
            SetItemsForeColor(statusStrip.Items, menuTextColor);

            ResumeLayout();
        }

        private static void SetItemsForeColor(ToolStripItemCollection items, Color color)
        {
            foreach (ToolStripItem item in items)
            {
                item.ForeColor = color;
                if (item is ToolStripMenuItem menuItem)
                    SetItemsForeColor(menuItem.DropDownItems, color);
            }
        }

        private void 深色模式ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var menu = (ToolStripMenuItem)sender;
            _configService.Config.DarkMode = menu.Checked;
            _configService.Save();
            ThemeManager.Toggle(menu.Checked);
        }

        private void 高精度模式ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var menu = (ToolStripMenuItem)sender;
            _configService.Config.HighResolutionTiming = menu.Checked;
            _configService.Save();
        }

        private class WarmMenuColors : ProfessionalColorTable
        {
            public override Color MenuStripGradientBegin => Color.FromArgb(242, 241, 237);
            public override Color MenuStripGradientEnd => Color.FromArgb(242, 241, 237);
            public override Color MenuItemSelected => Color.FromArgb(235, 234, 229);
            public override Color MenuItemBorder => Color.FromArgb(225, 224, 219);
            public override Color MenuItemSelectedGradientBegin => Color.FromArgb(235, 234, 229);
            public override Color MenuItemSelectedGradientEnd => Color.FromArgb(235, 234, 229);
            public override Color MenuItemPressedGradientBegin => Color.FromArgb(230, 229, 224);
            public override Color MenuItemPressedGradientEnd => Color.FromArgb(230, 229, 224);
            public override Color MenuBorder => Color.FromArgb(225, 224, 219);
            public override Color ToolStripDropDownBackground => Color.FromArgb(242, 241, 237);
            public override Color ImageMarginGradientBegin => Color.FromArgb(237, 236, 232);
            public override Color ImageMarginGradientMiddle => Color.FromArgb(237, 236, 232);
            public override Color ImageMarginGradientEnd => Color.FromArgb(237, 236, 232);
            public override Color SeparatorDark => Color.FromArgb(210, 209, 204);
            public override Color SeparatorLight => Color.FromArgb(235, 234, 229);
            public override Color StatusStripGradientBegin => Color.FromArgb(230, 229, 224);
            public override Color StatusStripGradientEnd => Color.FromArgb(230, 229, 224);
        }

        #endregion

        private void clsLogBtn_MouseHover(object sender, EventArgs e)
        {
            ToolTip toolTip1 = new ToolTip();
            toolTip1.AutoPopDelay = 5000;
            toolTip1.InitialDelay = 300;
            toolTip1.ReshowDelay = 300;
            toolTip1.ShowAlways = true;
            toolTip1.SetToolTip(this.clsLogBtn, "全部清除");
        }
    }
}