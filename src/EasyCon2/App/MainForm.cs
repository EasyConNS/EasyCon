using AvaloniaEdit.Folding;
using AvaloniaEdit.Highlighting;
using EasyCon.Core;
using EasyCon.Core.Config;
using EasyCon.Script.Asm;
using EasyCon.WinInput;
using EasyCon2.Avalonia.Core.Editor;
using EasyCon2.Avalonia.Core.VPad;
using EasyCon2.Forms;
using EasyCon2.Helper;
using EasyCon2.Models;
using EasyCon2.Services;
using EasyCon2.Views;
using EasyDevice;
using EasyScript;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using AvaColor = Avalonia.Media.Color;
using AvaColors = Avalonia.Media.Colors;
using Resources = EasyCon2.UI.Common.Properties.Resources;

namespace EasyCon2.App;

public partial class MainForm : Form, IOutputAdapter, IControllerAdapter
{
    private readonly string _version = Assembly.GetEntryAssembly()?
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion ?? "";

    // Services
    private readonly ScriptService _scriptService = new();
    private readonly DeviceService _deviceService = new();
    private readonly CaptureService _captureService = new();
    private readonly ConfigService _configService = new();

    // State
    private readonly AppState _state = new();

    // Editor
    private ScriptEditorControl _textEditor;
    private FoldingManager? _foldingManager;
    private CustomFoldingStrategy? _foldingStrategy;
    private VPadService? _vpadService;

    // Timer for UI updates
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 200 };

    // Theme colors (Cursor warm light)
    static readonly Color ColorSuccess = Color.FromArgb(31, 138, 101);   // #1f8a65
    static readonly Color ColorAccent = Color.FromArgb(245, 78, 0);      // #f54e00
    static readonly Color ColorError = Color.FromArgb(207, 45, 86);      // #cf2d56
    static readonly Color ColorFgDark = Color.FromArgb(38, 37, 30);      // #26251e
    static readonly Color ColorFgMuted = Color.FromArgb(140, 139, 132);  // rgba(38,37,30,0.55)
    static readonly Color ColorBgButton = Color.FromArgb(235, 234, 229); // #ebeae5
    static readonly Color ColorBgSurface = Color.FromArgb(230, 229, 224);// #e6e5e0

    public AvaColor CurrentLight => AvaColors.White;
    bool IControllerAdapter.IsRunning() => _scriptService.IsRunning;

    public MainForm()
    {
        InitializeComponent();
        _configService.Load();
        InitTheme();
        InitEditor();
        InitServices();
        InitTimer();
        rightPanel.ClientSizeChanged += RightPanel_Resize;
        Load += MainForm_Load;
    }

    #region Initialization

    private void InitEditor()
    {
        _textEditor = ScriptEditorHost.CreateControl();
        _textEditor.EditorTextChanged += TextEditor_TextChanged;
        _textEditor.FileDropped += (path) =>
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

        // Folding
        _foldingManager = FoldingManager.Install(_textEditor.TextArea);
        _foldingStrategy = new CustomFoldingStrategy();
        _foldingStrategy.UpdateFoldings(_foldingManager, _textEditor.TextDocument);

        // Completion
        _textEditor.SetImgLabelProvider(() => _captureService.LoadedLabels.Select(il => il.name));
        _textEditor.EnableAutoCompletion = _configService.Config.EnableAutoCompletion;
        _textEditor.SetFontSize(_configService.Config.EditorFontSize);
        _textEditor.FontSizeChanged += size =>
        {
            _configService.Config.EditorFontSize = size;
            _configService.Save();
        };

        // Host in Avalonia control host
        editorHost.Content = _textEditor;
    }

    private void InitServices()
    {
        // ScriptService events
        _scriptService.RunningStateChanged += running =>
            Post(() =>
            {
                _state.ScriptRunning = running;
                if (!running)
                    _state.ScriptStartTime = DateTime.MinValue;
                UpdateRunButton(running);
            });

        _scriptService.LogOutput += (msg, color) =>
            Post(() => logTxtBox.Print(msg, color));

        _scriptService.StatusChanged += msg =>
            Post(() => toolStripStatusLabel1.Text = msg);

        // DeviceService events
        _deviceService.ConnectionStateChanged += connected =>
            Post(() => UpdateDeviceStatus(connected));

        _deviceService.Log += msg =>
            Post(() => logTxtBox.Print(msg, null));

        _deviceService.StatusChanged += msg =>
            Post(() => toolStripStatusLabel1.Text = msg);

        // CaptureService events
        _captureService.ConnectionStateChanged += connected =>
            Post(() => UpdateCaptureStatus(connected));

        _captureService.StatusChanged += msg =>
            Post(() => toolStripStatusLabel1.Text = msg);
    }

    private void InitTimer()
    {
        _uiTimer.Tick += (_, _) =>
        {
            // Script title
            scriptTitleLabel.Text = _state.TitleText;

            // Timer display
            if (_state.ScriptRunning)
                timerLabel.Text = _state.TimerText;

            // Recording real-time update
            if (_deviceService.RecordState == RecordState.RECORD_START)
            {
                _textEditor.Text = _deviceService.GetRecordScript();
                _textEditor.ScrollToHome();
            }
        };
        _uiTimer.Start();
    }

    #endregion

    #region UI Update Helpers

    private void UpdateRunButton(bool running)
    {
        if (!IsHandleCreated) return;
        runStopBtn.Text = running ? "终止脚本" : "运行脚本";
        runStopBtn.BackColor = running ? ColorError : ColorSuccess;
        runStopBtn.ForeColor = Color.White;
        runStopBtn.Enabled = true;
        timerLabel.ForeColor = running ? ColorAccent : ColorFgDark;
    }

    private void UpdateDeviceStatus(bool connected)
    {
        if (!IsHandleCreated) return;
        labelSerialStatus.Text = connected ? "单片机已连接" : "单片机未连接";
        labelSerialStatus.ForeColor = connected ? ColorSuccess : ColorFgMuted;
    }

    private void UpdateCaptureStatus(bool connected)
    {
        if (!IsHandleCreated) return;
        labelCaptureStatus.Text = connected ? "采集卡已连接" : "采集卡未连接";
        labelCaptureStatus.ForeColor = connected ? ColorSuccess : ColorFgMuted;
        btnCaptureToggle.Text = connected ? "断开视频源" : "连接视频源";
    }

    private void StatusShow(string msg)
    {
        if (IsHandleCreated)
            toolStripStatusLabel1.Text = msg;
    }

    private void Post(Action action)
    {
        if (IsHandleCreated && !IsDisposed)
            base.BeginInvoke(action);
    }

    #endregion

    #region Sidebar Page Switching

    private void ResetPages()
    {
        burnPanel.Visible = false;
        settingsPanel.Visible = false;
        logPanel.Visible = false;
        editorHost.Visible = false;
        scriptTitleLabel.Visible = false;
        btnPageEditor.BackColor = ColorBgSurface;
        btnPageLog.BackColor = ColorBgSurface;
        btnPageBurn.BackColor = ColorBgSurface;
        btnPageSettings.BackColor = ColorBgSurface;
    }

    private void btnPageEditor_Click(object sender, EventArgs e)
    {
        ResetPages();
        editorHost.Visible = true;
        scriptTitleLabel.Visible = true;
        btnPageEditor.BackColor = ColorBgButton;
    }

    private void btnPageLog_Click(object sender, EventArgs e)
    {
        ResetPages();
        logPanel.Visible = true;
        btnPageLog.BackColor = ColorBgButton;
    }

    private void btnPageBurn_Click(object sender, EventArgs e)
    {
        ResetPages();
        burnPanel.Visible = true;
        btnPageBurn.BackColor = ColorBgButton;
    }

    private void btnPageSettings_Click(object sender, EventArgs e)
    {
        ResetPages();
        settingsPanel.Visible = true;
        btnPageSettings.BackColor = ColorBgButton;
    }

    #endregion

    #region Script Operations

    private async void runStopBtn_Click(object sender, EventArgs e)
    {
        runStopBtn.Enabled = false;

        if (!_scriptService.IsRunning)
        {
            // Check unsaved
            if (_state.CurrentFilePath != null && _textEditor.IsModified)
            {
                MessageBox.Show("您还没有保存脚本，请先保存后再运行");
                runStopBtn.Enabled = true;
                return;
            }

            // Compile first
            var externalGetters = _captureService.BuildExternalGetters();
            var (success, errorLine, error) = _scriptService.Compile(
                _textEditor.Text, _textEditor.TextDocument.FileName, externalGetters);

            if (!success)
            {
                if (errorLine != null)
                {
                    MessageBox.Show($"{errorLine}：{error}", "脚本编译出错");
                }
                else
                {
                    MessageBox.Show(error, "编译出错");
                }
                runStopBtn.Enabled = true;
                return;
            }

            // Check device requirement
            if (_scriptService.HasKeyAction && !_deviceService.IsConnected)
            {
                MessageBox.Show("需要连接单片机才能运行脚本");
                runStopBtn.Enabled = true;
                return;
            }

            if (_scriptService.HasKeyAction && !_deviceService.Device.RemoteStop())
            {
                MessageBox.Show("需要先停止烧录脚本运行，请点击<远程停止>按钮");
                runStopBtn.Enabled = true;
                return;
            }

            _state.ScriptStartTime = DateTime.Now;
            _state.ScriptRunning = true;
            _vpadService?.Deactivate();

            var pad = new GamePadAdapter(_deviceService.Device, _configService.Config.HighResolutionTiming);
            _scriptService.Run(this, pad);
        }
        else
        {
            _scriptService.Stop();
            _state.ScriptRunning = false;
            _state.ScriptStartTime = DateTime.MinValue;
            timerLabel.Text = "00:00:00";
            SystemSounds.Beep.Play();
        }
    }

    private async void formatBtn_Click(object sender, EventArgs e)
    {
        var externalGetters = _captureService.BuildExternalGetters();
        var (success, formatted, errorLine, error) = _scriptService.Format(
            _textEditor.Text, _textEditor.TextDocument.FileName, externalGetters);

        if (success)
        {
            _textEditor.Text = formatted;
            _textEditor.Select(0, 0);
            SystemSounds.Beep.Play();
        }
        else
        {
            SystemSounds.Hand.Play();
            StatusShow("格式化失败");
            if (errorLine != null)
                MessageBox.Show($"{errorLine}：{error}", "格式化出错");
            else
                MessageBox.Show(error, "格式化出错");
        }
    }

    #endregion

    #region Device Connection

    private void btnAutoConnect_Click(object sender, EventArgs e)
    {
        EnableConnBtn(false);
        StatusShow("尝试连接...");

        _ = Task.Run(async () =>
        {
            var (success, port) = await _deviceService.AutoConnectAsync();

            Post(() =>
            {
                if (success && port != null)
                {
                    comboComPort.Text = port;
                }
                else
                {
                    var ports = _deviceService.GetPortNames();
                    MessageBox.Show(
                        "找不到设备！请确认：\n" +
                        "1.已经为单片机烧好固件\n" +
                        "2.已经连好TTL线\n" +
                        "3.以上两步操作正确的话，点击搜索时单片机上的TX灯会闪烁\n\n" +
                        $"可用端口：{string.Join("、", ports)}");
                }
                EnableConnBtn();
            });
        });
    }

    private void btnManualConnect_Click(object sender, EventArgs e)
    {
        var port = comboComPort.Text;
        if (string.IsNullOrWhiteSpace(port))
        {
            MessageBox.Show("请先选择或输入串口");
            return;
        }

        EnableConnBtn(false);
        StatusShow("尝试连接...");

        _ = Task.Run(async () =>
        {
            var success = await _deviceService.ManualConnectAsync(port);
            Post(() =>
            {
                if (!success)
                    MessageBox.Show($@"连接失败！端口 {port} 不存在、无法使用或已被占用。
请在设备管理器确认 TTL 所在串口正确识别。关闭其他占用USB的程序，并重启软件再试。", "连接失败");
                EnableConnBtn();
            });
        });
    }

    private void comboComPort_DropDown(object sender, EventArgs e)
    {
        comboComPort.Items.Clear();
        comboComPort.Items.AddRange(_deviceService.GetPortNames());
    }

    private void EnableConnBtn(bool enabled = true)
    {
        btnAutoConnect.Enabled = enabled;
        btnManualConnect.Enabled = enabled;
    }

    #endregion

    #region Video Source

    private void comboVideoSource_DropDown(object sender, EventArgs e)
    {
        comboVideoSource.Items.Clear();
        foreach (var (name, index) in CaptureService.GetVideoSources())
        {
            comboVideoSource.Items.Add(new VideoSourceItem(name, index));
        }
    }

    private void btnCaptureToggle_Click(object sender, EventArgs e)
    {
        if (_captureService.IsConnected)
        {
            _captureService.Disconnect();
            return;
        }

        if (comboVideoSource.SelectedItem is not VideoSourceItem item)
        {
            MessageBox.Show("请先选择视频源");
            return;
        }

        // Determine capture type
        int captureType = 0;
        if (_configService.Config.CaptureType != "ANY")
        {
            var types = CaptureService.GetCaptureTypes();
            var match = types.FirstOrDefault(t => t.name == _configService.Config.CaptureType);
            captureType = match.value;
        }

        var imgLabelPath = _state.CurrentFilePath != null
            ? Path.Combine(Path.GetDirectoryName(_state.CurrentFilePath)!, "ImgLabel")
            : "";

        if (_captureService.Connect(item.Index, captureType, imgLabelPath))
        {
            SystemSounds.Beep.Play();
        }
    }

    private void btnOpenCaptureConsole_Click(object sender, EventArgs e)
    {
        _captureService.ShowCaptureConsole();
    }

    private record VideoSourceItem(string Name, int Index)
    {
        public override string ToString() => Name;
        public int Index { get; } = Index;
    }

    #endregion

    #region Burn / Flash

    private void btnRemoteStart_Click(object sender, EventArgs e)
    {
        if (!_deviceService.IsConnected)
        {
            MessageBox.Show("请先连接设备");
            return;
        }
        if (_deviceService.RemoteStart())
        {
            StatusShow("远程运行已开始");
            SystemSounds.Beep.Play();
        }
        else
        {
            StatusShow("远程运行失败");
            SystemSounds.Hand.Play();
        }
    }

    private void btnRemoteStop_Click(object sender, EventArgs e)
    {
        if (!_deviceService.IsConnected)
        {
            MessageBox.Show("请先连接设备");
            return;
        }
        if (_deviceService.RemoteStop())
        {
            StatusShow("远程停止成功");
            SystemSounds.Beep.Play();
        }
        else
        {
            StatusShow("远程停止失败");
            SystemSounds.Hand.Play();
        }
    }

    private void btnFlash_Click(object sender, EventArgs e)
    {
        if (!_deviceService.IsConnected)
        {
            MessageBox.Show("请先连接设备");
            return;
        }

        var board = comboBoardType.SelectedItem as Board;
        if (board == null)
        {
            MessageBox.Show("请先选择板型");
            return;
        }

        // Check firmware version
        var version = _deviceService.GetVersion();
        if (version != 0x45)
        {
            MessageBox.Show("单片机固件版本不匹配，请先更新固件");
            return;
        }

        // Compile
        var externalGetters = _captureService.BuildExternalGetters();
        var (success, errorLine, error) = _scriptService.Compile(
            _textEditor.Text, _textEditor.TextDocument.FileName, externalGetters);
        if (!success)
        {
            MessageBox.Show(errorLine != null ? $"{errorLine}：{error}" : error, "编译出错");
            return;
        }

        // Assemble
        var bytes = _scriptService.Assemble(true);
        if (bytes == null || bytes.Length == 0)
        {
            MessageBox.Show("编译结果为空，无法烧录");
            return;
        }

        if (bytes.Length > board.DataSize)
        {
            MessageBox.Show($"脚本编译后 {bytes.Length} 字节，超出 {board.DisplayName} 的 {board.DataSize} 字节限制");
            return;
        }

        // Flash
        if (_deviceService.Flash(bytes))
        {
            StatusShow("烧录成功");
            SystemSounds.Beep.Play();

            if (_configService.Config.AutoRunAfterFlash)
            {
                _deviceService.RemoteStart();
                StatusShow("烧录成功，已自动运行");
            }
        }
        else
        {
            StatusShow("烧录失败");
            SystemSounds.Hand.Play();
        }
    }

    private void btnFlashClear_Click(object sender, EventArgs e)
    {
        if (!_deviceService.IsConnected)
        {
            MessageBox.Show("请先连接设备");
            return;
        }

        if (_deviceService.Flash(HexWriter.EmptyAsm))
        {
            StatusShow("清除烧录成功");
            SystemSounds.Beep.Play();
        }
        else
        {
            StatusShow("清除烧录失败");
            SystemSounds.Hand.Play();
        }
    }

    #endregion

    #region Firmware Generation

    private void btnGenFirmware_Click(object sender, EventArgs e)
    {
        var board = comboBoardType.SelectedItem as Board;
        if (board == null)
        {
            MessageBox.Show("请先选择板型");
            return;
        }

        // Compile
        var externalGetters = _captureService.BuildExternalGetters();
        var (success, errorLine, error) = _scriptService.Compile(
            _textEditor.Text, _textEditor.TextDocument.FileName, externalGetters);
        if (!success)
        {
            MessageBox.Show(errorLine != null ? $"{errorLine}：{error}" : error, "编译出错");
            return;
        }

        // Assemble
        var bytes = _scriptService.Assemble(true);
        if (bytes == null || bytes.Length == 0)
        {
            MessageBox.Show("编译结果为空");
            return;
        }

        // Generate firmware
        btnGenFirmware.Enabled = false;
        try
        {
            var result = board.GenerateFirmware(bytes);
            MessageBox.Show($"固件已生成：{result}", "生成固件");
            SystemSounds.Beep.Play();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"生成固件失败：{ex.Message}");
        }
        finally
        {
            btnGenFirmware.Enabled = true;
        }
    }

    #endregion

    #region Script Recording

    private void btnRecord_Click(object sender, EventArgs e)
    {
        if (_deviceService.RecordState == RecordState.RECORD_STOP)
        {
            if (!_deviceService.IsConnected)
            {
                MessageBox.Show("请先连接设备");
                return;
            }

            if (_vpadService != null)
            {
                _vpadService.SwitchInput(new KeyboardInputBinder(_deviceService.Device, _configService.KeyMapping));
                _vpadService.Show();
            }
            else
            {
                MessageBox.Show("请先绑定虚拟手柄");
                return;
            }

            btnRecord.Text = "停止录制";
            btnRecordPause.Enabled = true;
            _textEditor.IsReadOnly = true;

            _deviceService.StartRecord();
            StatusShow("开始录制");
        }
        else
        {
            btnRecord.Text = "录制脚本";
            btnRecordPause.Enabled = false;
            _textEditor.IsReadOnly = false;

            _deviceService.StopRecord();
            StatusShow("录制完成");
        }
    }

    private void btnRecordPause_Click(object sender, EventArgs e)
    {
        if (_deviceService.RecordState == RecordState.RECORD_START)
        {
            _deviceService.PauseRecord();
            btnRecordPause.Text = "继续录制";
            StatusShow("录制已暂停");
        }
        else if (_deviceService.RecordState == RecordState.RECORD_PAUSE)
        {
            _deviceService.StartRecord();
            btnRecordPause.Text = "暂停录制";
            StatusShow("继续录制");
        }
    }

    #endregion

    #region Virtual Controller

    private void btnShowController_Click(object sender, EventArgs e)
    {
        if (!_deviceService.IsConnected)
        {
            MessageBox.Show("请先连接设备");
            return;
        }

        _vpadService ??= new VPadService(_deviceService.Device, this);
        _vpadService.SwitchInput(new KeyboardInputBinder(_deviceService.Device, _configService.KeyMapping));
        _vpadService.Show();
    }

    private void btnKeyMapping_Click(object sender, EventArgs e)
    {
        using var dlg = new FormKeyMapping(_configService.KeyMapping);
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            _configService.UpdateKeyMapping(dlg.KeyMapping);
            _vpadService?.UpdateKeyMapping(_configService.KeyMapping);
        }
    }

    #endregion

    #region File Operations

    private void menuItemNew_Click(object sender, EventArgs e)
    {
        FileClose();
        StatusShow("新建完毕");
    }

    private void menuItemOpen_Click(object sender, EventArgs e)
    {
        FileOpen();
    }

    private void menuItemSave_Click(object sender, EventArgs e)
    {
        FileSave();
    }

    private void menuItemSaveAs_Click(object sender, EventArgs e)
    {
        FileSave(asNew: true);
    }

    private void menuItemClose_Click(object sender, EventArgs e)
    {
        FileClose();
    }

    private void menuItemExit_Click(object sender, EventArgs e)
    {
        Close();
    }

    private bool FileOpen(string? path = null)
    {
        string filePath;
        if (path != null)
        {
            filePath = path;
        }
        else
        {
            using var dlg = new OpenFileDialog
            {
                Title = "打开",
                RestoreDirectory = true,
                Filter = "脚本文件 (*.txt，*.ecs)|*.txt;*.ecs|所有文件(*.*)|*.*",
                FileName = string.Empty
            };
            if (dlg.ShowDialog() != DialogResult.OK)
                return false;
            if (!FileClose())
                return false;
            filePath = dlg.FileName;
        }

        if (!FileClose())
            return false;

        _textEditor.Load(filePath);
        _textEditor.IsModified = false;
        _textEditor.FileName = filePath;
        _textEditor.SyntaxHighlighting = EcsHighlightingLoader.GetByExtension(Path.GetExtension(filePath));
        _state.CurrentFilePath = filePath;
        _state.IsModified = false;
        StatusShow("文件已打开");
        return true;
    }

    private bool FileSave(bool asNew = false)
    {
        if (asNew || _state.CurrentFilePath == null)
        {
            using var dlg = new SaveFileDialog
            {
                Title = asNew ? "另存为" : "保存",
                RestoreDirectory = true,
                Filter = "脚本文件 (*.txt，*.ecs)|*.txt;*.ecs|所有文件(*.*)|*.*",
                FileName = "未命名脚本.txt"
            };
            if (dlg.ShowDialog() != DialogResult.OK)
                return false;
            _state.CurrentFilePath = dlg.FileName;
            _textEditor.FileName = dlg.FileName;
        }

        _textEditor.Save(_state.CurrentFilePath);
        _textEditor.IsModified = false;
        _state.IsModified = false;
        StatusShow("文件已保存");
        return true;
    }

    private bool FileClose()
    {
        if (_textEditor.IsModified)
        {
            var r = MessageBox.Show("文件已编辑，是否保存？", "", MessageBoxButtons.YesNoCancel);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes && !FileSave()) return false;
        }
        _state.CurrentFilePath = null;
        _state.IsModified = false;
        _textEditor.FileName = null;
        _textEditor.Clear();
        _textEditor.IsModified = false;
        StatusShow("文件已关闭");
        return true;
    }

    private void TextEditor_TextChanged(object? sender, EventArgs e)
    {
        _state.IsModified = _textEditor.IsModified;
        if (_configService.Config.EnableAutoCompletion)
            _foldingStrategy?.UpdateFoldings(_foldingManager!, _textEditor.TextDocument);
        _scriptService.Reset();
    }

    #endregion

    #region Edit Operations

    private void menuItemFindReplace_Click(object sender, EventArgs e)
    {
        _textEditor.OpenSearchPanel();
    }

    private void menuItemFindNext_Click(object sender, EventArgs e)
    {
        // SearchPanel 内部处理 F3 快捷键
    }

    private void menuItemToggleComment_Click(object sender, EventArgs e)
    {
        int startOffset = _textEditor.SelectionStart;
        int endOffset = startOffset + _textEditor.SelectionLength;
        var doc = _textEditor.TextDocument;
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

    #endregion

    #region Settings

    private void chkAutoCompletion_CheckedChanged(object sender, EventArgs e)
    {
        _configService.Config.EnableAutoCompletion = chkAutoCompletion.Checked;
        _textEditor.EnableAutoCompletion = chkAutoCompletion.Checked;
        _configService.Save();
    }

    private void chkFolding_CheckedChanged(object sender, EventArgs e)
    {
        _configService.Config.ShowControllerHelp = chkFolding.Checked;
        _configService.Save();
        if (chkFolding.Checked)
            _foldingStrategy?.UpdateFoldings(_foldingManager!, _textEditor.TextDocument);
        else
            _foldingManager?.Clear();
    }

    private void chkDebugLog_CheckedChanged(object sender, EventArgs e)
    {
        _deviceService.DebugLogEnabled = chkDebugLog.Checked;
    }

    private void chkAutoRunAfterFlash_CheckedChanged(object sender, EventArgs e)
    {
        _configService.Config.AutoRunAfterFlash = chkAutoRunAfterFlash.Checked;
        _configService.Save();
    }

    private void chkHighResolutionTiming_CheckedChanged(object sender, EventArgs e)
    {
        _configService.Config.HighResolutionTiming = chkHighResolutionTiming.Checked;
        _configService.Save();
    }

    private void btnAlertConfig_Click(object sender, EventArgs e)
    {
        using var dlg = new AlertConfigForm();
        dlg.ShowDialog(this);
    }

    private void chkAutoSaveLog_CheckedChanged(object sender, EventArgs e)
    {
        _configService.Config.AutoSaveLog = chkAutoSaveLog.Checked;
        _configService.Save();
    }

    private void btnDrawingBoard_Click(object sender, EventArgs e)
    {
        var form = new DrawingBoard(_deviceService.Device);
        form.Show();
    }

    private void btnBluetoothSetting_Click(object sender, EventArgs e)
    {
        var form = new Forms.win32.BTDeviceForm();
        form.Show();
    }

    private void btnESPConfig_Click(object sender, EventArgs e)
    {
        var form = new ESPConfig(_deviceService.Device);
        form.Show();
    }

    private void btnUnpair_Click(object sender, EventArgs e)
    {
        if (!_deviceService.IsConnected)
            return;
        if (_deviceService.UnPair())
        {
            SystemSounds.Beep.Play();
            StatusShow("取消配对成功");
        }
        else
        {
            SystemSounds.Hand.Play();
            StatusShow("取消配对失败");
        }
    }

    #endregion

    #region Capture Menu (retained from old structure)

    private void captureTypeMenu_DropDownOpening(object sender, EventArgs e)
    {
        captureTypeMenu.DropDownItems.Clear();
        foreach (var (name, value) in CaptureService.GetCaptureTypes())
        {
            var item = new ToolStripMenuItem(name) { Tag = value };
            if (name == _configService.Config.CaptureType)
                item.Checked = true;
            item.Click += CaptureTypeItem_Click;
            captureTypeMenu.DropDownItems.Add(item);
        }
    }

    private void CaptureTypeItem_Click(object? sender, EventArgs e)
    {
        var selected = (ToolStripMenuItem)sender!;
        captureTypeMenu.DropDownItems.Cast<ToolStripMenuItem>()
            .ToList().ForEach(i => i.Checked = false);
        selected.Checked = true;
        _configService.Config.CaptureType = selected.Text;
        _configService.Save();
    }

    private void setEnvVarMenuItem_Click(object sender, EventArgs e)
    {
        Environment.SetEnvironmentVariable("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS", "0");
        var val = Environment.GetEnvironmentVariable("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS");
        StatusShow($"环境变量设置成功：{val}");
    }

    private void captureHelpMenuItem_Click(object sender, EventArgs e)
    {
        MessageBox.Show(@"默认采集卡类型选择any，会自动选择合适的采集卡
- 常见采集卡类型是DSHOW，MSMF，DC1394等
- obs30+版本已支持内置虚拟摄像头，无需安装额外插件
- 如果出现黑屏、颜色不正确等情况，请切换其他采集卡类型，然后重新打开
- 如果遇到搜图卡顿问题可尝试点击一次<设置环境变量>菜单
- 详细使用教程见群946057081文档", "采集卡");
    }

    #endregion

    #region Help Menu

    private void menuItemFirmwareMode_Click(object sender, EventArgs e)
    {
        MessageBox.Show(@"- 生成固件后手动刷入单片机的模式
- 独立挂机，即插即用
- 支持极限效率脚本
- 不需要任何额外配件

详细使用教程见群946057081文档", "固件模式");
    }

    private void menuItemOnlineMode_Click(object sender, EventArgs e)
    {
        MessageBox.Show(@"- 使用电脑控制单片机的模式
- 可视化运行，一键切换脚本（即将实装）
- 无需反复刷固件
- 支持超长脚本
- 可使用虚拟手柄，用键盘玩游戏

详细使用教程见群946057081文档", "联机模式");
    }

    private void menuItemFlashMode_Click(object sender, EventArgs e)
    {
        MessageBox.Show(@"- 连线烧录后脱机运行的模式
- 独立挂机，即插即用
- 一键烧录，可控运行
- 无需反复刷固件
- 支持极限效率脚本

详细使用教程见群946057081文档", "烧录模式");
    }

    private void menuItemScriptSyntax_Click(object sender, EventArgs e)
    {
        new HelpTxtDialog(Resources.scriptdoc).Show();
    }

    private void menuItemCheckUpdate_Click(object sender, EventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var data = await client.GetStringAsync("https://gitee.com/api/v5/repos/EasyConNS/EasyCon/tags");
                var ver = JsonSerializer.Deserialize<VerInfo[]>(data);
                if (ver == null) return;

                var info = new VersionParser(ver, Assembly.GetExecutingAssembly().GetName().Version);
                var msg = info.IsNewVersion
                    ? $"发现新版本{info.NewVer}，快去群文件里看看吧"
                    : "暂时没有发现新版本";
                Post(() => MessageBox.Show(msg));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"update check failed: {ex.Message}");
            }
        });
    }

    private void menuItemAbout_Click(object sender, EventArgs e)
    {
        string displayVer = _version;
        int plusIdx = _version?.IndexOf('+') ?? -1;
        if (plusIdx > 0) displayVer = _version!.Substring(0, plusIdx);

        MessageBox.Show(
            $"伊机控 v{displayVer}  QQ群:946057081\n\n" +
            "Copyright © 2020. 铃落(Nukieberry)\n" +
            "Copyright © 2021. elmagnifico\n" +
            "Copyright © 2025. 卡尔(ca1e)",
            "关于");
    }

    private void menuItemSource_Click(object sender, EventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/EasyConNS/EasyCon") { UseShellExecute = true });
    }

    #endregion

    #region IOutputAdapter

    public void Print(string message, bool newline = true) =>
        logTxtBox.Print(message, newline);

    public void Alert(string message)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var dispatcher = new AlertDispatcher(ConfigManager.LoadAlert());
                dispatcher.OnResult += (_, result) => Print(result);
                await dispatcher.DispatchAsync(message);
            }
            catch (Exception ex)
            {
                Print($"推送失败:{ex.Message}");
            }
        });
    }

    #endregion

    #region Form Lifecycle

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (editorHost.ContainsFocus)
            return false;
        return base.ProcessDialogKey(keyData);
    }

    private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        e.Cancel = !FileClose();
        _captureService.Disconnect();
        _uiTimer.Stop();
    }

    private void MainForm_Load(object? sender, EventArgs e)
    {
        Print("正在初始化伊机控...");

        // Initialize board type combo
        comboBoardType.Items.AddRange(Board.SupportedBoards);
        if (comboBoardType.Items.Count > 0)
            comboBoardType.SelectedIndex = 0;

        // Initialize settings from config
        chkAutoCompletion.Checked = _configService.Config.EnableAutoCompletion;
        chkFolding.Checked = _configService.Config.ShowControllerHelp;
        chkDebugLog.Checked = _deviceService.DebugLogEnabled;
        chkAutoRunAfterFlash.Checked = _configService.Config.AutoRunAfterFlash;
        chkHighResolutionTiming.Checked = _configService.Config.HighResolutionTiming;

        // Version label & title
        string displayVer = _version;
        int plusIdx = _version?.IndexOf('+') ?? -1;
        if (plusIdx > 0) displayVer = _version!.Substring(0, plusIdx);
        lblVersion.Text = $"版本: {displayVer}";
        Text = $"伊机控 EasyCon v{displayVer}  QQ群:946057081";
        Print("准备就绪，欢迎使用伊机控！");
        Print("将脚本文件直接拖入窗口打开，然后点击运行开始执行脚本");
    }

    private void RightPanel_Resize(object? sender, EventArgs e)
    {
        var rw = rightPanel.ClientSize.Width - rightPanel.Padding.Horizontal;
        var gw = rw - 20;
        if (gw < 100) return;
        foreach (GroupBox grp in rightPanel.Controls.OfType<GroupBox>())
        {
            grp.Width = gw;
            var innerWidth = gw - 16;
            foreach (Control inner in grp.Controls)
            {
                if (inner is not Label)
                    inner.Width = innerWidth;
            }
        }
    }

    private void clsLogBtn_Click(object sender, EventArgs e)
    {
        logTxtBox.ClearLog();
    }

    #endregion

    #region Theme

    private void InitTheme()
    {
        var renderer = new ToolStripProfessionalRenderer(new WarmMenuColors());
        renderer.RoundedEdges = false;
        menuStrip.Renderer = renderer;
        statusStrip.Renderer = renderer;
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
}