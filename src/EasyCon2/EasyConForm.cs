using EasyCon.Script;
using EasyCon.Script.Assembly;
using EasyCon.Script.Runner;
using EasyCon2.Config;
using EasyCon2.Helper;
using EasyCon2.Properties;
using EasyDevice;
using EasyScript;
using EasyVPad;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Xml;

namespace EasyCon2.Forms
{
    public partial class EasyConForm : Form, IControllerAdapter, IOutputAdapter
    {
        private readonly Version VER = Assembly.GetExecutingAssembly().GetName().Version;
        private readonly TextEditor textEditor = new();
        private CodeCompletionController _completionController;
        internal readonly VPadForm virtController;

        public Color CurrentLight => Color.White;
        bool IControllerAdapter.IsRunning() => scriptRunning;

        private NintendoSwitch NS = new();
        private readonly Scripter _program = new();
        private CaptureVideoForm captureVideo = new();

        private QqAssist ws = new();

        private ConfigState _config;
        const string ConfigPath = @"config.json";
        const string ScriptPath = @"Script\";
        const string FirmwarePath = @"Firmware\";

        private readonly string defaultName = "未命名脚本";
        private string fileName => textEditor.Document.FileName == null ? defaultName : Path.GetFileName(textEditor.Document.FileName);

        private readonly List<ToolStripMenuItem> captureTypes = [];

        public EasyConForm()
        {
            InitializeComponent();

            virtController = new VPadForm(this, this.NS);

            LoadConfig();
            captureVideo.LoadImgLabels();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                findPanel1.Hide();
            }
            return base.ProcessCmdKey(ref msg, keyData);
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

        private FoldingManager _foldingManager;
        private CustomFoldingStrategy _foldingStrategy;

        private void EasyConForm_Load(object sender, EventArgs e)
        {
            this.Text = $"伊机控 EasyCon v{VER}  QQ群:946057081";
            comboBoxBoardType.Items.AddRange(Board.SupportedBoards);
            comboBoxBoardType.SelectedIndex = 0;
            RegisterKeys();
            InitEditor();
            InitEvent();

            // UI updating timer
            Task.Run(() => { UpdateUI(); });

            PyRunner.Init("D:\\AppData\\scoop\\apps\\python313\\current\\python313.dll");

            InitCaptureDevices();
            InitCaptureTypes();

            StatusShowLog($"已加载搜图标签：{captureVideo.LoadedLabels.Count()}");
        }

        private void EasyConForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = !FileClose();
            captureVideo?.Close();
        }

        private void InitEditor()
        {
            textEditor.ShowLineNumbers = true;
            var syntaxHighlighting = HighlightingLoader.Load(XmlReader.Create(new MemoryStream(Resources.ecp)), HighlightingManager.Instance);
            HighlightingManager.Instance.RegisterHighlighting("ECP", [".txt"], syntaxHighlighting);
            var luaHighlighting = HighlightingLoader.Load(XmlReader.Create(new MemoryStream(Resources.lua)), HighlightingManager.Instance);
            HighlightingManager.Instance.RegisterHighlighting("Lua", [".lua"], luaHighlighting);
            var pyHighlighting = HighlightingLoader.Load(XmlReader.Create(new MemoryStream(Resources.Python_Mode)), HighlightingManager.Instance);
            HighlightingManager.Instance.RegisterHighlighting("Python", [".py"], pyHighlighting);
            textEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("ECP");
            textEditor.DragEnter += new System.Windows.DragEventHandler(this.textBoxScript_DragEnter);
            textEditor.Drop += new System.Windows.DragEventHandler(this.textBoxScript_DragDrop);
            textEditor.TextChanged += new EventHandler(this.textBoxScript_TextChanged);

            var completionProvider = new EcpCompletionProvider(textEditor);
            completionProvider.GetImgLabel += () => captureVideo.LoadedLabels.Select(il => il.name);
            _completionController = new CodeCompletionController(textEditor, completionProvider);

            _foldingManager = FoldingManager.Install(textEditor.TextArea);
            _foldingStrategy = new CustomFoldingStrategy();
            _foldingStrategy.UpdateFoldings(_foldingManager, textEditor.Document);

            findPanel1.InitEditor(textEditor);

            editorHost.Child = textEditor;
        }

        private void InitEvent()
        {
            ScriptRunningChanged += (bool isTunning) =>
            {
                Invoke(delegate
                {
                    if (!isTunning)
                    {

                        runStopBtn.Text = "运行脚本";
                        runStopBtn.BackColor = Color.FromArgb(95, 46, 204, 113);

                    }
                    else
                    {
                        runStopBtn.Text = "终止脚本";
                        runStopBtn.BackColor = Color.FromArgb(95, 231, 76, 60);
                    }
                    runStopBtn.Enabled = true;
                    编译ToolStripMenuItem.Enabled = !isTunning;
                    执行ToolStripMenuItem.Enabled = !isTunning;

                    // timer label
                    labelTimer.ForeColor = isTunning ? Color.Lime : Color.White;
                });
            };

            // serial debug
            NS.StatusChanged += (stat) =>
            {
                // serial port status
                if (NS.IsConnected())
                {
                    labelSerialStatus.Text = "单片机已连接";
                    labelSerialStatus.ForeColor = Color.Lime;
                }
                else
                {
                    labelSerialStatus.Text = "单片机未连接";
                    labelSerialStatus.ForeColor = Color.White;
                }
            };
            NS.Log += (message) =>
            {
                if (显示调试信息ToolStripMenuItem.Checked)
                    logTxtBox.Print($"NS LOG >> {message}", null);
            };
            NS.BytesSent += (port, bytes) =>
            {
                if (显示调试信息ToolStripMenuItem.Checked)
                    logTxtBox.Print($"{port} >> {string.Join(" ", bytes.Select(b => b.ToString("X2")))}", null);
            };
            NS.BytesReceived += (port, bytes) =>
            {
                if (显示调试信息ToolStripMenuItem.Checked)
                    logTxtBox.Print($"{port} << {string.Join(" ", bytes.Select(b => b.ToString("X2")))}", null);
            };
        }

        private void CaptureDeviceItem_Click(object sender, EventArgs e)
        {
            // tag = device id
            if (captureVideo?.DeviceID == (int)(((ToolStripMenuItem)sender).Tag))
            {
                MessageBox.Show("相同采集卡已经打开了");
                return;
            }

            int dev_type = 0;
            foreach (var item in captureTypes)
            {
                if (item.Checked == true)
                    dev_type = (int)item.Tag;
            }

            captureVideo = new CaptureVideoForm((int)(((ToolStripMenuItem)sender).Tag), dev_type);
            captureVideo.Show();
            StatusShowLog($"已加载搜图标签：{captureVideo.LoadedLabels.Count()}");
        }

        private void 打开搜图ToolStripMenuItem_MouseHover(object sender, EventArgs e)
        {
            InitCaptureDevices();
        }

        private void InitCaptureDevices()
        {
            var devs = EasyCon.Core.ECCore.GetCaptureSources();

            打开搜图ToolStripMenuItem.DropDownItems.Clear();

            var enumerable = devs.Select((d, i) =>
            {
                var item = new ToolStripMenuItem
                {
                    Checked = false,
                    CheckState = CheckState.Unchecked,
                    Size = new Size(180, 22),
                    Name = "menuItem" + d ?? "?",
                    Text = d ?? "?"
                };
                item.Click += new EventHandler(CaptureDeviceItem_Click);
                item.Tag = i;
                return item;
            });

            打开搜图ToolStripMenuItem.DropDownItems.AddRange(enumerable.ToArray());
        }

        private void InitCaptureTypes()
        {
            // add capture types
            var types = EasyCon.Core.ECCore.GetCaptureTypes();

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
                if (name == _config.CaptureType)
                    item.Checked = true;
                captureTypes.Add(item);
            }
        }

        private void UpdateUI()
        {
            try
            {
                while (true)
                {
                    Invoke(delegate
                    {
                        // update record script to text
                        if (NS.recordState == RecordState.RECORD_START)
                        {
                            buttonRecord.Text = "停止录制";
                            textEditor.Text = NS.GetRecordScript();
                            this.textEditor.ScrollToHome();
                        }
                        if (captureVideo.IsOpened)
                        {
                            labelCaptureStatus.Text = "采集卡已连接";
                            labelCaptureStatus.ForeColor = Color.Lime;
                        }
                        else
                        {
                            labelCaptureStatus.Text = "采集卡未连接";
                            labelCaptureStatus.ForeColor = Color.White;
                        }

                        // timer
                        if (_startTime != DateTime.MinValue)
                        {
                            var time = DateTime.Now - _startTime;
                            labelTimer.Text = time.ToString(@"hh\:mm\:ss");
                        }
                    });
                    Thread.Sleep(25);
                }
            }
            catch (Exception)
            {
                Debug.WriteLine("UI Task error occured!");
            }
        }

        private void LoadConfig()
        {
            try
            {
                _config = JsonSerializer.Deserialize<ConfigState>(File.ReadAllText(ConfigPath));
            }
            catch (Exception ex)
            {
                if (ex is not FileNotFoundException)
                    MessageBox.Show("读取设置文件失败！");
                _config = new();
                _config.SetDefault();
            }


            频道远程ToolStripMenuItem.Checked = _config?.ChannelControl ?? false;
            if (频道远程ToolStripMenuItem.Checked)
            {
                StartWebSocket();
            }

#if DEBUG
            蓝牙ToolStripMenuItem.Visible = true;
#endif
        }

        private void SaveConfig()
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(_config));
        }

        private void RegisterKeys()
        {
            virtController.RegisterAllKeys(_config.KeyMapping);
        }

        private void StatusShowLog(string str)
        {
            Invoke(delegate
            {
                toolStripStatusLabel1.Text = str;
            });
        }

        private void ScriptSelectLine(int index)
        {
            textEditor.ScrollToLine(index);
        }

        private Board GetSelectedBoard()
        {
            return comboBoxBoardType.SelectedItem as Board;
        }

        public void Print(string message, bool newline = true) =>
            logTxtBox.Print(message, newline);

        public void Alert(string message)
        {
            Task.Run(() =>
            {
                try
                {
                    var result = new EasyCon.Core.PushPlusClient(_config.AlertToken).SendMessage(message).Result;
                    Print(result);

                    if (_config.ChannelToken != "")
                    {
                        ws?.SendNotify(message);
                    }
                    else
                    {
                        Print("推送失败: 请配置推送Token");
                    }
                }
                catch (Exception e)
                {
                    Print($"推送失败:{e.Message}");
                }
            });
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

        private void ComPort_DropDown(object sender, EventArgs e)
        {
            var ports = ECDevice.GetPortNames();
            ComPort.Items.Clear();
            foreach (var portName in ports)
            {
                ComPort.Items.Add(portName);
                Debug.WriteLine(portName);
            }
        }

        private bool SerialCheckConnect()
        {
            if (NS.IsConnected())
                return true;
            var port = ComPort.Text.Equals("下拉选择串口") ? "" : ComPort.Text;
            SerialSearchConnect(port);
            return NS.IsConnected();
        }

        private async void SerialSearchConnect(string port = "")
        {
            EnableConnBtn(false);
            StatusShowLog("尝试连接...");

            var ports = port == "" ? ECDevice.GetPortNames() : [port];

            await Task.Run(() =>
            {
                foreach (var portName in ports)
                {
                    var r = NS.TryConnect(portName);
                    if (显示调试信息ToolStripMenuItem.Checked)
                        Print($"{portName} {r.GetName()}");
                    if (r == NintendoSwitch.ConnectResult.Success)
                    {
                        StatusShowLog("连接成功");
                        SystemSounds.Beep.Play();
                        Invoke(() =>
                        {

                            ComPort.Text = portName;
                        });
                        break;
                    }
                    // fix the internal thread cant quit safely,so wait 1s for next connect
                    Thread.Sleep(1000);
                }
            });

            if (!NS.IsConnected())
            {
                StatusShowLog("连接失败");
                SystemSounds.Hand.Play();
                if (port != "")

                    MessageBox.Show("连接失败！该端口不存在、无法使用或已被占用。请在设备管理器确认TTL所在串口正确识别，关闭其它占用USB的程序，并重启伊机控再试。");
                else
                    MessageBox.Show("找不到设备！请确认：\n1.已经为单片机烧好固件\n2.已经连好TTL线（详细使用教程见群946057081文档）\n3.以上两步操作正确的话，点击搜索时单片机上的TX灯会闪烁\n4.如果用的是CH340G，换一下帽子让3v3与S1相连（默认可能是5V与S1相连）\n5.以上步骤都完成后重启程序再试\n\n可用手动连接端口：" + string.Join("、", ports));
            }

            EnableConnBtn();
        }
        #region 文件操作
        private bool FileOpen(string path = "")
        {
            Directory.CreateDirectory(ScriptPath);
            openFileDialog.Title = "打开";
            openFileDialog.RestoreDirectory = true;
            openFileDialog.InitialDirectory = Path.GetFullPath(ScriptPath);
            openFileDialog.Filter = @"文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
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

            textEditor.Load(_currentFile);
            textEditor.Document.FileName = _currentFile;
            var hightligDefin = Path.GetExtension(_currentFile) switch
            {
                ".cs" => "C#",
                ".py" => "Python",
                ".lua" => "Lua",
                _ => "ECP",
            };
            textEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(hightligDefin);
            scriptTitleLabel.Text = textEditor.IsModified ? $"{fileName}(已编辑)" : fileName;
            return true;
        }

        private bool FileSave(bool saveAs = false)
        {
            Directory.CreateDirectory(ScriptPath);
            saveFileDialog.Title = saveAs ? "另存为" : "保存";
            saveFileDialog.RestoreDirectory = true;
            saveFileDialog.InitialDirectory = Path.GetFullPath(ScriptPath);
            saveFileDialog.Filter = @"文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
            saveFileDialog.FileName = string.Empty;

            if (saveAs || textEditor.Document.FileName == null)
            {
                if (saveFileDialog.ShowDialog() != DialogResult.OK)
                    return false;
                textEditor.Document.FileName = saveFileDialog.FileName;

            }
            textEditor.Save(textEditor.Document.FileName);
            StatusShowLog("文件已保存");
            return true;
        }

        private bool FileClose()
        {
            if (textEditor.IsModified)
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
            textEditor.Document.FileName = null;
            textEditor.Clear();
            textEditor.IsModified = false;
            scriptTitleLabel.Text = textEditor.IsModified ? $"{fileName}(已编辑)" : fileName;
            StatusShowLog("文件已关闭");
            return true;
        }
        #endregion
        private void ShowControllerHelp()
        {
            new HelpTxtDialog(@"鼠标左键：启用/禁用
鼠标右键：拖动移动位置，右键点击重置初始位置
鼠标中键：禁用并隐藏

（注意：在有脚本远程运行的情况下无法使用）", "关于虚拟手柄").Show();
        }

        private void buttonShowController_Click(object sender, EventArgs e)
        {
            if (!SerialCheckConnect())
                return;
            virtController.ControllerEnabled = true;
            if (_config.ShowControllerHelp)
            {
                ShowControllerHelp();
                _config.ShowControllerHelp = false;
                SaveConfig();
            }
        }

        private async void compileButton_Click(object sender, EventArgs e)
        {
            if (await ScriptCompile())
            {
                StatusShowLog("编译成功");
                SystemSounds.Beep.Play();
                MessageBox.Show("编译成功！", "编译结果");
            }
        }

        private void 执行ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!scriptRunning)
            {
                ScriptRun();
            }
        }

        private void textBoxScript_TextChanged(object sender, EventArgs e)
        {
            // script edited
            scriptTitleLabel.Text = textEditor.IsModified ? $"{fileName}(已编辑)" : fileName;

            if (显示折叠ToolStripMenuItem.Checked)
                _foldingStrategy.UpdateFoldings(_foldingManager, textEditor.Document);
            ScriptReset();
        }

        private void buttonScriptRunStop_Click(object sender, EventArgs e)
        {
            runStopBtn.Enabled = false;
            if (!scriptRunning)
            {
                ScriptRun();
            }
            else
            {
                ScriptStop();
            }
            runStopBtn.Enabled = true;
        }

        private void EnableConnBtn(bool show = true)
        {
            buttonSerialPortSearch.Enabled = show;
            buttonSerialPortConnect.Enabled = show;
        }

        private void buttonSerialPortSearch_Click(object sender, EventArgs e)
        {
            SerialSearchConnect();
        }

        private void buttonSerialPortConnect_Click(object sender, EventArgs e)
        {
            SerialSearchConnect(ComPort.Text);
        }

        private void buttonKeyMapping_Click(object sender, EventArgs e)
        {
            using (var formKeyMapping = new FormKeyMapping(_config.KeyMapping))
            {
                if (formKeyMapping.ShowDialog() == DialogResult.OK)
                {
                    _config.KeyMapping = formKeyMapping.KeyMapping;
                    SaveConfig();
                    RegisterKeys();
                }
            }
        }

        private void buttonControllerHelp_Click(object sender, EventArgs e)
        {
            ShowControllerHelp();
        }

        private async void buttonGenerateFirmware_Click(object sender, EventArgs e)
        {
            genFwButton.Enabled = false;
            await GenerateFirmware(GetSelectedBoard());
            genFwButton.Enabled = true;
        }

        private async void buttonFlash_Click(object sender, EventArgs e)
        {
            if (!SerialCheckConnect())
            {
                return;
            }

            if (!CheckFwVersion())
            {
                return;
            }

            if (!await ScriptCompile())
            {
                return;
            }
            ScriptFlash(GetSelectedBoard().DataSize);
        }

        private void buttonRemoteStart_Click(object sender, EventArgs e)
        {
            if (!SerialCheckConnect())
                return;
            if (NS.RemoteStart())
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
            if (!SerialCheckConnect())
                return;
            if (NS.RemoteStop())
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
            if (!SerialCheckConnect())
            {
                StatusShowLog("还未准备好烧录");
                SystemSounds.Hand.Play();
                return;
            }

            if (!NS.Flash(HexWriter.EmptyAsm))
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
            // if record.state = start
            if (NS.recordState == RecordState.RECORD_STOP)
            {
                if (!SerialCheckConnect())
                    return;
                virtController.ControllerEnabled = true;
                buttonRecord.Text = "停止录制";
                buttonRecordPause.Enabled = true;
                textEditor.IsEnabled = false;
                NS.StartRecord();
            }
            else
            {
                Debug.Write("stop");
                buttonRecord.Text = "录制脚本";
                buttonRecordPause.Enabled = false;
                textEditor.IsEnabled = true;
                NS.StopRecord();
            }
        }

        private void buttonRecordPause_Click(object sender, EventArgs e)
        {
            // pause the record
            if (NS.recordState == RecordState.RECORD_START)
            {
                NS.PauseRecord();
            }
        }

        private void textBoxScript_DragDrop(object sender, System.Windows.DragEventArgs e)
        {
            try
            {
                var path = (string[])e.Data.GetData(DataFormats.FileDrop, false);
                if (!FileClose())
                    return;
                FileOpen(path[0]);
            }
            catch
            {
                MessageBox.Show("打开失败了，原因未知", "打开脚本");
            }
        }

        private void textBoxScript_DragEnter(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = System.Windows.DragDropEffects.All;
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }
        }

        #region  EasyCon菜单功能
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

        private void 查找替換ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (textEditor.SelectedText.Length > 0)
                findPanel1.Target = textEditor.SelectedText;
            findPanel1.Show();
        }

        private void 查找下一个ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (textEditor.SelectedText.Length > 0)
                findPanel1.Target = textEditor.SelectedText;
            var index = findPanel1.Find();

            if (index == -1)
            {
                MessageBox.Show("到底了");
                return;
            }

            textEditor.Select(index, findPanel1.Target.Length);
            textEditor.ScrollToLine(textEditor.Document.GetLineByOffset(index).LineNumber);
        }

        private void DeviceTypeItem_Click(object sender, EventArgs e)
        {
            // tag = device id
            //(ToolStripMenuItem)sender = true;
            foreach (var item in captureTypes)
            {
                item.Checked = false;
            }
            ToolStripMenuItem selected = (ToolStripMenuItem)sender;
            selected.Checked = true;
            _config.CaptureType = selected.Text;
            Debug.WriteLine(selected.Text);
            SaveConfig();
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
        }

        private void CPU优化设置ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var menu = (ToolStripMenuItem)sender;
            menu.Checked = !menu.Checked;
            var rlt = NS.SetCpuOpt(menu.Checked);
            StatusShowLog($"CPU优化已{(rlt ? "开启" : "关闭")}");
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
            var form = new ConfigForm(_config);
            if (form.ShowDialog() == DialogResult.OK)
            {
                _config = form.Config;
                SaveConfig();
            }
        }

        private void 设备驱动配置ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using var btform = new win32.BTDeviceForm();
            btform.ShowDialog();
        }

        private void 频道远程ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var menu = (ToolStripMenuItem)sender;
            menu.Checked = !menu.Checked;
            if (menu.Checked)
            {
                menu.Checked = StartWebSocket();
            }
            else
            {
                WSRun = false;
            }
            _config.ChannelControl = menu.Checked;
            SaveConfig();
        }

        private void 手柄设置ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using var controllerConfig = new ESPConfig(NS);
            controllerConfig.ShowDialog();
        }

        private void 取消配对ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!SerialCheckConnect())
                return;
            if (NS.UnPair())
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
            var board = new DrawingBoard(NS);
            board.Show();
        }

        private void openDelayToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var menu = (ToolStripMenuItem)sender;
            menu.Checked = !menu.Checked;
            var rlt = NS.SetOpenDelay(menu.Checked);
            StatusShowLog($"串口打开延迟已{(rlt ? "开启" : "关闭")}");
        }

        private void 自由画板鼠标代替摇杆ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Mouse mouse = new(NS);
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
                    // Fetch latest release information.
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(5);

                    var data = await client.GetStringAsync("https://gitee.com/api/v5/repos/EasyConNS/EasyCon/tags");
                    var ver = JsonSerializer.Deserialize<VerInfo[]>(data);
                    if (ver == null)
                        return;

                    // Check if already up-to-date.
                    var info = new VersionParser(ver, VER);
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
                _foldingStrategy.UpdateFoldings(_foldingManager, textEditor.Document);
            else
                _foldingManager.Clear();
        }

        private void 注释取消注释ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // 获取选择范围
            int startOffset = textEditor.SelectionStart;
            int endOffset = startOffset + textEditor.SelectionLength;

            // 获取开始和结束行
            var startLine = textEditor.Document.GetLineByOffset(startOffset);
            var endLine = textEditor.Document.GetLineByOffset(endOffset);
            Debug.WriteLine($"选择范围：{startLine.LineNumber}-{endLine.LineNumber}");


            var docomment = false;
            for (int lineNum = endLine.LineNumber; lineNum >= startLine.LineNumber; lineNum--)
            {
                var line = textEditor.Document.GetLineByNumber(lineNum);
                if (Scripter.CanComment(textEditor.Document.GetText(line)))
                {
                    docomment = true;
                    break;
                }
            }

            using (textEditor.Document.RunUpdate())
            {
                for (int lineNum = endLine.LineNumber; lineNum >= startLine.LineNumber; lineNum--)
                {
                    var line = textEditor.Document.GetLineByNumber(lineNum);
                    var text = textEditor.Document.GetText(line);
                    text = Scripter.ToggleComment(text, docomment);
                    textEditor.Document.Replace(line, text);
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
        #endregion

        private void clsLogBtn_MouseHover(object sender, EventArgs e)
        {
            // 创建the ToolTip 
            ToolTip toolTip1 = new ToolTip();

            // 设置显示样式
            toolTip1.AutoPopDelay = 5000;//提示信息的可见时间
            toolTip1.InitialDelay = 300;//事件触发多久后出现提示
            toolTip1.ReshowDelay = 300;//指针从一个控件移向另一个控件时，经过多久才会显示下一个提示框
            toolTip1.ShowAlways = true;//是否显示提示框

            //  设置伴随的对象.
            toolTip1.SetToolTip(this.clsLogBtn, "全部清除");
        }
    }
}
