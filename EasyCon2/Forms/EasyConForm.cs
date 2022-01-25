using EasyCon2.Capture;
using EasyCon2.Global;
using EasyCon2.Properties;
using EasyCon2.Script;
using EasyCon2.Script.Assembly;
using EasyCon2.Script.Parsing;
using ECDevice;
using ECDevice.Exts;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using PTController;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace EasyCon2.Forms
{
    public partial class EasyConForm : Form, IOutputAdapter, ICGamePad
    {
        private readonly TextEditor textBoxScript = new();
        internal NintendoSwitch NS = NintendoSwitch.GetInstance();
        internal FormController formController;
        internal FormKeyMapping formKeyMapping;
        internal CaptureVideoForm captureVideo;

        const string ConfigPath = @"config.json";
        const string ScriptPath = @"Script\";
        const string FirmwarePath = @"Firmware\";

        private VControllerConfig _config;
        private string _currentFile = null;

        private readonly Scripter _program = new();
        private bool scriptCompiling = false;
        private bool scriptRunning = false;
        private Thread thd;

        private readonly Queue<Tuple<RichTextBox, object, Color?>> _messages = new();
        private DateTime _startTime = DateTime.MinValue;
        private TimeSpan _lastRunningTime = TimeSpan.Zero;
        private bool _msgNewLine = true;
        private bool _msgFirstLine = true;
        private bool _fileEdited = false;

        private readonly List<ToolStripMenuItem> captureTypes = new();

        public EasyConForm()
        {
            InitializeComponent();

            formController = new FormController(new ControllerAdapter());
            formKeyMapping = new FormKeyMapping();

            LoadConfig();
            InitEditor();
#if DEBUG
            蓝牙ToolStripMenuItem.Visible = true;
#endif
        }

        private void InitEditor()
        {
            textBoxScript.ShowLineNumbers = true;
            IHighlightingDefinition syntaxHighlighting = HighlightingLoader.Load(XmlReader.Create(new MemoryStream(Encoding.UTF8.GetBytes(Resources.NX))), HighlightingManager.Instance);
            textBoxScript.SyntaxHighlighting = syntaxHighlighting;
            textBoxScript.DragEnter += new System.Windows.DragEventHandler(this.textBoxScript_DragEnter);
            textBoxScript.Drop += new System.Windows.DragEventHandler(this.textBoxScript_DragDrop);
            textBoxScript.TextChanged += new EventHandler(this.textBoxScript_TextChanged);

            elementHost1.Child = textBoxScript;
        }

        private void EasyConForm_Load(object sender, EventArgs e)
        {
            textBoxScriptHelp.Text = Resources.scriptdoc;
            comboBoxBoardType.Items.AddRange(Board.SupportedBoards);
            comboBoxBoardType.SelectedIndex = 0;
            RegisterKeys();

            // UI updating timer
            Task.Run(() => { UpdateUI(); });

            // serial debug
            NS.BytesSent += (port, bytes) =>
            {
                if (显示调试信息ToolStripMenuItem.Checked)
                    Print($"{port} >> " + string.Join(" ", bytes.Select(b => b.ToString("X2"))), null, true);
            };
            NS.BytesReceived += (port, bytes) =>
            {
                if (显示调试信息ToolStripMenuItem.Checked)
                    Print($"{port} << " + string.Join(" ", bytes.Select(b => b.ToString("X2"))), null, true);
            };

            InitCaptureTypes();

            // resize
            Xvalue = this.Width;
            Yvalue = this.Height;
            setTag(this);
        }

        public float Xvalue;
        public float Yvalue;

        private void setTag(Control cons)
        {
            foreach (Control con in cons.Controls)
            {
                con.Tag = con.Width + ":" + con.Height + ":" + con.Left + ":" + con.Top + ":" + con.Font.Size;
                if (con.Controls.Count > 0)
                    setTag(con);
            }
        }

        private void InitCaptureTypes()
        {
            // add capture types
            var types = OpenCVCapture.GetCaptureTypes();

            采集卡类型ToolStripMenuItem.DropDownItems.Clear();
            foreach (var name in types.Keys)
            {
                var item = new ToolStripMenuItem();
                采集卡类型ToolStripMenuItem.DropDownItems.Add(item);
                item.Checked = false;
                item.CheckState = CheckState.Unchecked;
                item.Name = "toolStripMenuItem2";
                item.Size = new Size(180, 22);
                item.Text = name;
                item.Click += new EventHandler(this.DeviceTypeItem_Click);
                item.Tag = types[name];
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
                        // run/stop button
                        if (scriptRunning)
                            buttonScriptRunStop.Text = "终止";
                        else
                            buttonScriptRunStop.Text = "运行";
                        // update record script to text
                        if (NS.recordState == RecordState.RECORD_START)
                        {
                            buttonRecord.Text = "停止录制";
                            textBoxScript.Text = NS.GetRecordScript();
                            this.textBoxScript.ScrollToHome();
                        }

                        // serial port status
                        var status = NS.ConnectionStatus;
                        if (status == Status.Connected)
                        {
                            labelSerialStatus.Text = "已连接(稳定模式)";
                            labelSerialStatus.ForeColor = Color.Lime;
                        }
                        else if (status == Status.ConnectedUnsafe)
                        {
                            labelSerialStatus.Text = "已连接(单向模式)";
                            labelSerialStatus.ForeColor = Color.Yellow;
                        }
                        else
                        {
                            labelSerialStatus.Text = "单片机未连接";
                            labelSerialStatus.ForeColor = Color.White;
                        }

                        // timer
                        TimeSpan time;
                        if (_startTime == DateTime.MinValue)
                            time = _lastRunningTime;
                        else
                            time = DateTime.Now - _startTime;
                        labelTimer.Text = time.ToString(@"hh\:mm\:ss");
                        labelTimer.ForeColor = scriptRunning ? Color.Lime : Color.White;

                        // message
                        var boxes = new HashSet<RichTextBox>();
                        while (_messages.Count > 0)
                        {
                            Tuple<RichTextBox, object, Color?> tuple;
                            lock (_messages)
                            {
                                tuple = _messages.Dequeue();
                            }
                            var box = tuple.Item1;
                            var message = tuple.Item2;
                            var color = tuple.Item3;
                            if (!boxes.Contains(box))
                            {
                                box.SuspendLayout();
                                boxes.Add(box);
                            }
                            if (message == null)
                            {
                                // cls
                                box.Text = string.Empty;
                                continue;
                            }
                            while (box.TextLength >= 1000000)
                            {
                                box.Select(0, box.GetFirstCharIndexFromLine(10));
                                box.ReadOnly = false;
                                box.SelectedText = string.Empty;
                                box.ReadOnly = true;
                            }
                            box.SelectionStart = box.TextLength;
                            box.SelectionLength = 0;
                            box.SelectionColor = color ?? box.ForeColor;
                            box.AppendText(message.ToString());
                        }
                        foreach (var box in boxes)
                        {
                            box.ScrollToCaret();
                            box.ResumeLayout();
                            box.Invalidate();
                        }

                        // script edited
                        var strse = _fileEdited ? "脚本(已编辑)" : "脚本";
                        if (groupBoxScript.Text != strse)
                            groupBoxScript.Text = strse;
                    });
                    Thread.Sleep(25);
                }
            }
            catch(Exception)
            {
                Debug.WriteLine("UI Task error occured!");
            }
        }

        private void LoadConfig()
        {
            try
            {
                _config = JsonSerializer.Deserialize<VControllerConfig>(File.ReadAllText(ConfigPath));
            }
            catch (Exception ex)
            {
                if (ex is not FileNotFoundException)
                    MessageBox.Show("读取设置文件失败！");
                _config = new VControllerConfig();
                _config.SetDefault();
            }
        }

        private void SaveConfig()
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(_config));
        }

        private string GetFirmwareName(string corename)
        {
            var dir = new DirectoryInfo(FirmwarePath);
            if (!dir.Exists)
                return null;
            var max = 0;
            string filename = null;
            foreach (var fi in dir.GetFiles())
            {
                var m = Regex.Match(fi.Name, $@"^{corename} v(\d+)\.hex$", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    var ver = int.Parse(m.Groups[1].Value);
                    if (ver > max)
                    {
                        max = ver;
                        filename = fi.Name;
                    }
                }
            }
            return filename;
        }

        private Board GetSelectedBoard()
        {
            return comboBoxBoardType.SelectedItem as Board;
        }

        private void RegisterKeys()
        {
            formController.UnregisterAllKeys();

            formController.RegisterKey(_config.KeyMapping.A, ECKeyUtil.Button(SwitchButton.A));
            formController.RegisterKey(_config.KeyMapping.B, ECKeyUtil.Button(SwitchButton.B));
            formController.RegisterKey(_config.KeyMapping.X, ECKeyUtil.Button(SwitchButton.X));
            formController.RegisterKey(_config.KeyMapping.Y, ECKeyUtil.Button(SwitchButton.Y));
            formController.RegisterKey(_config.KeyMapping.L, ECKeyUtil.Button(SwitchButton.L));
            formController.RegisterKey(_config.KeyMapping.R, ECKeyUtil.Button(SwitchButton.R));
            formController.RegisterKey(_config.KeyMapping.ZL, ECKeyUtil.Button(SwitchButton.ZL));
            formController.RegisterKey(_config.KeyMapping.ZR, ECKeyUtil.Button(SwitchButton.ZR));
            formController.RegisterKey(_config.KeyMapping.Plus, ECKeyUtil.Button(SwitchButton.PLUS));
            formController.RegisterKey(_config.KeyMapping.Minus, ECKeyUtil.Button(SwitchButton.MINUS));
            formController.RegisterKey(_config.KeyMapping.Capture, ECKeyUtil.Button(SwitchButton.CAPTURE));
            formController.RegisterKey(_config.KeyMapping.Home, ECKeyUtil.Button(SwitchButton.HOME));
            formController.RegisterKey(_config.KeyMapping.LClick, ECKeyUtil.Button(SwitchButton.LCLICK));
            formController.RegisterKey(_config.KeyMapping.RClick, ECKeyUtil.Button(SwitchButton.RCLICK));

            formController.RegisterKey(_config.KeyMapping.UpRight, ECKeyUtil.HAT(SwitchHAT.TOP_RIGHT));
            formController.RegisterKey(_config.KeyMapping.DownRight, ECKeyUtil.HAT(SwitchHAT.BOTTOM_RIGHT));
            formController.RegisterKey(_config.KeyMapping.UpLeft, ECKeyUtil.HAT(SwitchHAT.TOP_LEFT));
            formController.RegisterKey(_config.KeyMapping.DownLeft, ECKeyUtil.HAT(SwitchHAT.BOTTOM_LEFT));

            formController.RegisterKey(_config.KeyMapping.Up, () => NS.HatDirection(DirectionKey.Up, true), () => NS.HatDirection(DirectionKey.Up, false));
            formController.RegisterKey(_config.KeyMapping.Down, () => NS.HatDirection(DirectionKey.Down, true), () => NS.HatDirection(DirectionKey.Down, false));
            formController.RegisterKey(_config.KeyMapping.Left, () => NS.HatDirection(DirectionKey.Left, true), () => NS.HatDirection(DirectionKey.Left, false));
            formController.RegisterKey(_config.KeyMapping.Right, () => NS.HatDirection(DirectionKey.Right, true), () => NS.HatDirection(DirectionKey.Right, false));

            formController.RegisterKey(_config.KeyMapping.LSUp, () => NS.LeftDirection(DirectionKey.Up, true), () => NS.LeftDirection(DirectionKey.Up, false));
            formController.RegisterKey(_config.KeyMapping.LSDown, () => NS.LeftDirection(DirectionKey.Down, true), () => NS.LeftDirection(DirectionKey.Down, false));
            formController.RegisterKey(_config.KeyMapping.LSLeft, () => NS.LeftDirection(DirectionKey.Left, true), () => NS.LeftDirection(DirectionKey.Left, false));
            formController.RegisterKey(_config.KeyMapping.LSRight, () => NS.LeftDirection(DirectionKey.Right, true), () => NS.LeftDirection(DirectionKey.Right, false));
            formController.RegisterKey(_config.KeyMapping.RSUp, () => NS.RightDirection(DirectionKey.Up, true), () => NS.RightDirection(DirectionKey.Up, false));
            formController.RegisterKey(_config.KeyMapping.RSDown, () => NS.RightDirection(DirectionKey.Down, true), () => NS.RightDirection(DirectionKey.Down, false));
            formController.RegisterKey(_config.KeyMapping.RSLeft, () => NS.RightDirection(DirectionKey.Left, true), () => NS.RightDirection(DirectionKey.Left, false));
            formController.RegisterKey(_config.KeyMapping.RSRight, () => NS.RightDirection(DirectionKey.Right, true), () => NS.RightDirection(DirectionKey.Right, false));
        }

        #region 脚本功能接口
        private void Print(string message, Color? color, bool timestamp = true)
        {
            lock (_messages)
            {
                if (_msgNewLine)
                {
                    if (!_msgFirstLine)
                        _messages.Enqueue(new Tuple<RichTextBox, object, Color?>(richTextBoxMessage, Environment.NewLine, null));
                    _msgFirstLine = false;
                    if (timestamp)
                        _messages.Enqueue(new Tuple<RichTextBox, object, Color?>(richTextBoxMessage, DateTime.Now.ToString("[HH:mm:ss.fff] "), Color.Gray));
                }
                _messages.Enqueue(new Tuple<RichTextBox, object, Color?>(richTextBoxMessage, message, color));
                _msgNewLine = true;
            }
        }

        public void Print(string message, bool newline = true)
        {
            lock (_messages)
            {
                _msgNewLine = _msgNewLine && newline;
                Print(message, null);
            }
        }
        
        public void Alert(string message)
        {
            if(_config.AlertToken == "")
            {
                Print("推送Token不能为空");
                return;
            }
            var address = $"https://www.pushplus.plus/send/{_config.AlertToken}?content={message}&title=伊机控消息";
            using var client = new HttpClient();
            var result = client.GetAsync(address).Result.Content.ReadAsStringAsync().Result;
            Print(result);
        }

        void ICGamePad.ClickButtons(ECKey key, int duration)
        {
            NS.Press(key, duration);
        }

        void ICGamePad.PressButtons(ECKey key)
        {
            NS.Down(key);
        }

        void ICGamePad.ReleaseButtons(ECKey key)
        {
            NS.Up(key);
        }
        #endregion

        private void StatusShowLog(string str)
        {
            Invoke(delegate
            {
                toolStripStatusLabel1.Text = str;
            });
        }

        private void ScriptSelectLine(int index)
        {
            textBoxScript.ScrollToLine(index);
        }

        private bool ScriptCompile()
        {
            StatusShowLog("开始编译...");
            try
            {
                scriptCompiling = true;

                // 在这里根据图像处理窗口的情况，创建一个ExternalVariable的数组或List传给Parse函数
                // 每个ExternalVariable对应一个图像标签，name为名字，get为用来获取结果的函数，set暂时没有语句支持所以先省略                
                var externalVariables = new List<ExternalVariable>();
                foreach (var il in CaptureVideoForm.imgLabels)
                {
                    externalVariables.Add(new ExternalVariable(il.name, () => il.Search()));
                }

                _program.Parse(textBoxScript.Text, externalVariables);
                textBoxScript.Text = _program.ToCode();
                textBoxScript.Select(0, 0);
                return true;
            }
            catch (ParseException ex)
            {
                string str = $"{ex.Message}: 行{ex.Index + 1}";
                SystemSounds.Hand.Play();
                MessageBox.Show(str);
                StatusShowLog(str);
                ScriptSelectLine(ex.Index);
                return false;
            }
            finally
            {
                scriptCompiling = false;
            }
        }

        private void ScriptRun()
        {
            if (!ScriptCompile())
                return;
            if(_program.HasKeyAction)
            {
                if (!SerialCheckConnect())
                    return;
                if (!CheckFirmwareVersion())
                    return;
            }

            thd = new Thread(_RunScript);
            thd?.Start();
        }

        private void _RunScript()
        {
            scriptRunning = true;
            try
            {
                _startTime = DateTime.Now;
                _lastRunningTime = TimeSpan.Zero;
                Invoke(delegate
                {
                    formController.ControllerEnabled = false;
                    StatusShowLog("开始运行");
                });
                Print("-- 开始运行 --", Color.Lime);
                _program.Run(this, this);
                Print("-- 运行结束 --", Color.Lime);
                StatusShowLog("运行结束");
                SystemSounds.Beep.Play();
            }
            catch (ThreadInterruptedException)
            {
                Print("-- 运行终止 --", Color.Orange);
                StatusShowLog("运行终止");
                SystemSounds.Beep.Play();
            }
            catch (ScriptException ex)
            {
                Print(ex.Message, Color.OrangeRed);
                Print("-- 运行出错 --", Color.OrangeRed);
                StatusShowLog("运行出错");
                SystemSounds.Hand.Play();
            }
            catch (Exception exx)
            {
                Print(exx.Message, Color.OrangeRed);
                Print("-- 运行出错 --", Color.OrangeRed);
                StatusShowLog("运行出错");
                SystemSounds.Hand.Play();
                throw;
            }
            finally
            {
                NS.Reset();
                _lastRunningTime = DateTime.Now - _startTime;
                _startTime = DateTime.MinValue;
                scriptRunning = false;
            }
        }

        private void ScriptStop()
        {
            if (scriptRunning)
            {
                thd?.Interrupt();
                scriptRunning = false;
                StatusShowLog("运行被终止");
                SystemSounds.Beep.Play();
            }
        }

        private void ComPort_DropDown(object sender, EventArgs e)
        {
            StatusShowLog("尝试连接...");
            var ports = NintendoSwitch.GetPortNames();
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
            if (!ComPort.Text.Equals("下拉选择串口"))
                return SerialConnect(ComPort.Text);
            return SerialSearchConnect() != null;
        }

        private string SerialSearchConnect()
        {
            StatusShowLog("尝试连接...");
            var ports = NintendoSwitch.GetPortNames();
            foreach (var portName in ports)
            {
                var r = NS.TryConnect(portName, true);
                if (显示调试信息ToolStripMenuItem.Checked)
                    Print($"{portName} {r.GetName()}");
                if (r == NintendoSwitch.ConnectResult.Success)
                {
                    StatusShowLog("连接成功");
                    ComPort.Text = portName;
                    return portName;
                }
                // fix the internal thread cant quit safely,so wait 1s for next connect
                Thread.Sleep(1000);
            }
            StatusShowLog("连接失败");
            SystemSounds.Hand.Play();
            MessageBox.Show("找不到设备！请确认：\n1.已经为单片机烧好固件\n2.已经连好TTL线（详细使用教程见群946057081文档）\n3.以上两步操作正确的话，点击搜索时单片机上的TX灯会闪烁\n4.如果用的是CH340G，换一下帽子让3v3与S1相连（默认可能是5V与S1相连）\n5.以上步骤都完成后重启程序再试\n\n可用手动连接端口：" + string.Join("、", ports));
            return null;
        }

        private bool SerialConnect(string portName)
        {
            StatusShowLog("开始连接...");
            // try stable connection
            var r = NS.TryConnect(portName, true);
            if (显示调试信息ToolStripMenuItem.Checked)
                Print($"{portName} {r.GetName()}");
            if (r != NintendoSwitch.ConnectResult.Success)
            {
                // try unstable connection
                r = NS.TryConnect(portName, false);
                if (显示调试信息ToolStripMenuItem.Checked)
                    Print($"{portName} {r.GetName()}");
            }
            if (r == NintendoSwitch.ConnectResult.Success)
            {
                StatusShowLog("连接成功");
                ComPort.Text = portName;
                if (NS.ConnectionStatus == Status.ConnectedUnsafe)
                    MessageBox.Show("正在使用单向连接模式。这是一种应急方案，并不表示成功连接到单片机，有可能无法正常工作。请检查连线并尽量使用稳定模式。");
                return true;
            }
            else
            {
                StatusShowLog("连接失败");
                SystemSounds.Hand.Play();
                MessageBox.Show("连接失败！该端口不存在、无法使用或已被占用。请在设备管理器确认TTL所在串口正确识别，关闭其它占用USB的程序，并重启伊机控再试。");
                return false;
            }
        }

        private bool FileOpen()
        {
            if (!FileClose())
                return false;
            Directory.CreateDirectory(ScriptPath);
            openFileDialog1.Title = "打开";
            openFileDialog1.RestoreDirectory = true;
            openFileDialog1.InitialDirectory = Path.GetFullPath(ScriptPath);
            openFileDialog1.Filter = @"文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
            openFileDialog1.FileName = string.Empty;
            if (openFileDialog1.ShowDialog() != DialogResult.OK)
                return false;
            _currentFile = openFileDialog1.FileName;
            textBoxScript.Text = File.ReadAllText(_currentFile);
            _fileEdited = false;
            return true;
        }

        private bool FileSave(bool saveAs = false, bool close = false)
        {
            if (close && !FileClose())
                return false;
            if (saveAs || _currentFile == null)
            {
                Directory.CreateDirectory(ScriptPath);
                saveFileDialog1.Title = saveAs ? "另存为" : "保存";
                saveFileDialog1.RestoreDirectory = true;
                saveFileDialog1.InitialDirectory = Path.GetFullPath(ScriptPath);
                saveFileDialog1.Filter = @"文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
                saveFileDialog1.FileName = string.Empty;
                if (saveFileDialog1.ShowDialog() != DialogResult.OK)
                    return false;
                _currentFile = saveFileDialog1.FileName;
            }
            File.WriteAllText(_currentFile, textBoxScript.Text);
            _fileEdited = false;
            StatusShowLog("文件已保存");
            return true;
        }

        private bool FileClose()
        {
            if (_fileEdited)
            {
                var r = MessageBox.Show("文件已编辑，是否保存？", "", MessageBoxButtons.YesNoCancel);
                if (r == DialogResult.Cancel)
                    return false;
                else if (r == DialogResult.Yes)
                {
                    if (!FileSave(false, false))
                        return false;
                }
            }
            _currentFile = null;
            textBoxScript.Text = string.Empty;
            _fileEdited = false;
            StatusShowLog("文件已关闭");
            return true;
        }

        private bool GenerateFirmware()
        {
            if (!ScriptCompile())
                return false;
            try
            {
                StatusShowLog("开始生成固件...");
                var bytes = _program.Assemble(脚本自动运行ToolStripMenuItem.Checked);
                File.WriteAllBytes("temp.bin", bytes);
                string hexStr;
                var filename = GetFirmwareName(GetSelectedBoard().CoreName);
                if (filename == null)
                {
                    StatusShowLog("固件生成失败");
                    SystemSounds.Hand.Play();
                    MessageBox.Show("未找到固件！请确认程序Firmware目录下是否有对应固件文件！");
                    return false;
                }
                try
                {
                    hexStr = File.ReadAllText(FirmwarePath + filename);
                }
                catch (Exception ex)
                {
                    StatusShowLog("固件生成失败");
                    SystemSounds.Hand.Play();
                    MessageBox.Show($"固件读取失败！{ex.ToString()}");
                    return false;
                }
                hexStr = Assembler.WriteHex(hexStr, bytes, GetSelectedBoard());
                string name = Path.GetFileNameWithoutExtension(_currentFile);
                filename = filename.Replace(".", $"+Script.");
                File.WriteAllText(filename, hexStr);
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
                ScriptSelectLine(ex.Index);
                return false;
            }
            catch (ParseException ex)
            {
                StatusShowLog("固件生成失败");
                SystemSounds.Hand.Play();
                MessageBox.Show("固件生成失败！" + ex.Message);
                ScriptSelectLine(ex.Index);
                return false;
            }
        }

        private bool FlashPrepare()
        {
            if (!SerialCheckConnect())
                return false;
            if (NS.ConnectionStatus == Status.ConnectedUnsafe)
            {
                MessageBox.Show("需要稳定模式才能烧录");
                return false;
            }
            return true;
        }

        private bool CheckFirmwareVersion()
        {
            if (!SerialCheckConnect())
                return false;
            if (NS.ConnectionStatus == Status.ConnectedUnsafe)
                return true;
            int ver = NS.GetVersion();
            if (ver < GetSelectedBoard().Version)
            {
                StatusShowLog("需要更新固件");
                SystemSounds.Hand.Play();
                MessageBox.Show("固件版本不符，请重新刷入" + FirmwarePath);
                return false;
            }
            return true;
        }

        private void ShowControllerHelp()
        {
            MessageBox.Show("鼠标左键：启用/禁用\n鼠标右键：拖动移动位置，右键点击重置初始位置\n鼠标中键：禁用并隐藏\n\n（注意：在有脚本远程运行的情况下无法使用）", "关于虚拟手柄");
        }

        private void buttonShowController_Click(object sender, EventArgs e)
        {
            if (!SerialCheckConnect())
                return;
            formController.ControllerEnabled = true;
            if (_config.ShowControllerHelp)
            {
                ShowControllerHelp();
                _config.ShowControllerHelp = false;
                SaveConfig();
            }
        }

        private void 编译ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (ScriptCompile())
            {
                StatusShowLog("编译成功");
                SystemSounds.Beep.Play();
                MessageBox.Show($"编译成功！");
            }
        }

        private void 执行ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!scriptRunning)
                ScriptRun();
            else
                MessageBox.Show("已经在运行了！");
        }

        private void textBoxScript_TextChanged(object sender, EventArgs e)
        {
            if (scriptCompiling)
                return;
            if (scriptRunning)
                return;
            _fileEdited = true;
            _program.Reset();
        }

        private void buttonScriptRunStop_Click(object sender, EventArgs e)
        {
            if (!scriptRunning)
            {
                ScriptRun();
            } 
            else
                ScriptStop();
        }

        private void buttonScriptStop_Click(object sender, EventArgs e)
        {
            ScriptStop();
        }

        private void buttonSerialPortSearch_Click(object sender, EventArgs e)
        {
            if (SerialSearchConnect() != null)
                SystemSounds.Beep.Play();
        }

        private void buttonSerialPortConnect_Click(object sender, EventArgs e)
        {
            if (SerialConnect(ComPort.Text))
                SystemSounds.Beep.Play();
        }

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
            captureVideo?.Close();
        }

        private void buttonKeyMapping_Click(object sender, EventArgs e)
        {
            formKeyMapping.KeyMapping = _config.KeyMapping;
            if (formKeyMapping.ShowDialog() == DialogResult.OK)
            {
                _config.KeyMapping = formKeyMapping.KeyMapping;
                SaveConfig();
                RegisterKeys();
            }
        }

        private void buttonControllerHelp_Click(object sender, EventArgs e)
        {
            ShowControllerHelp();
        }

        private void buttonCLS_Click(object sender, EventArgs e)
        {
            lock (_messages)
            {
                _messages.Enqueue(new Tuple<RichTextBox, object, Color?>(richTextBoxMessage, null, null));
                _msgFirstLine = true;
                _msgNewLine = true;
            }
        }

        private void buttonGenerateFirmware_Click(object sender, EventArgs e)
        {
            GenerateFirmware();
        }

        private void buttonFlash_Click(object sender, EventArgs e)
        {
            if (!FlashPrepare())
            {
                return;
            }

            if (!CheckFirmwareVersion())
            {
                return;
            }

            if (!ScriptCompile())
            {
                return;
            }

            try
            {
                StatusShowLog("开始烧录...");
                var bytes = _program.Assemble(脚本自动运行ToolStripMenuItem.Checked);
                File.WriteAllBytes("temp.bin", bytes);
                if (bytes.Length > GetSelectedBoard().DataSize)
                {
                    StatusShowLog("烧录失败");
                    SystemSounds.Hand.Play();
                    MessageBox.Show("烧录失败！长度超出限制");
                    return;
                }
                if (!NS.Flash(bytes))
                {
                    StatusShowLog("烧录失败");
                    SystemSounds.Hand.Play();
                    MessageBox.Show("烧录失败！请检查设备连接后重试");
                    return;
                }
                StatusShowLog("烧录完毕");
                SystemSounds.Beep.Play();
                MessageBox.Show($"烧录完毕！已使用存储空间({bytes.Length}/{GetSelectedBoard().DataSize})");
            }
            catch (AssembleException ex)
            {
                StatusShowLog("烧录失败");
                SystemSounds.Hand.Play();
                MessageBox.Show("烧录失败！" + ex.Message);
                ScriptSelectLine(ex.Index);
                return;
            }
            catch (ParseException ex)
            {
                StatusShowLog("烧录失败");
                SystemSounds.Hand.Play();
                MessageBox.Show("烧录失败！" + ex.Message);
                ScriptSelectLine(ex.Index);
                return;
            }
        }

        private void buttonRemoteStart_Click(object sender, EventArgs e)
        {
            if (!SerialCheckConnect())
                return;
            if (NS.RemoteStart() || NS.ConnectionStatus == Status.ConnectedUnsafe)
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
            if (NS.RemoteStop() || NS.ConnectionStatus == Status.ConnectedUnsafe)
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
            if (!FlashPrepare())
                StatusShowLog("还未准备好烧录");

            if (!NS.Flash(Assembler.EmptyAsm))
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

        private void comboBoxBoardType_SelectedIndexChanged(object sender, EventArgs e)
        {
            textBoxFirmware.Text = $"{(comboBoxBoardType.SelectedItem as Board)?.CoreName}.hex";
        }

        private void buttonRecord_Click(object sender, EventArgs e)
        {
            // if record.state = start
            if (NS.recordState == RecordState.RECORD_STOP)
            {
                if (!SerialCheckConnect())
                    return;
                formController.ControllerEnabled = true;
                buttonRecord.Text = "停止录制";
                buttonRecordPause.Enabled = true;
                textBoxScript.IsEnabled = false;
                NS.StartRecord();
            }
            else
            {
                Debug.Write("stop");
                buttonRecord.Text = "录制脚本";
                buttonRecordPause.Enabled = false;
                textBoxScript.IsEnabled = true;
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
                if (Path.GetExtension(path[0]) != ".txt") return;
                var _script = File.ReadAllText(path[0]);
                textBoxScript.Text = _script;
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

        private void CaptureDeviceItem_Click(object sender, EventArgs e)
        {
            // tag = device id
            if (captureVideo?.deviceId == (int)(((ToolStripMenuItem)sender).Tag))
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
        }

        private void 打开搜图ToolStripMenuItem_MouseHover(object sender, EventArgs e)
        {
            var openDevice = (ToolStripMenuItem)sender;
            var devs = OpenCVCapture.GetCaptureCamera();

            openDevice.DropDownItems.Clear();
            int tag = 0;

            foreach (var d in devs)
            {
                var item = new ToolStripMenuItem();
                openDevice.DropDownItems.Add(item);
                item.Checked = false;
                item.CheckState = CheckState.Unchecked;
                item.Name = "menuItem" + d;
                item.Size = new Size(180, 22);
                item.Text = d;
                item.Click += new EventHandler(CaptureDeviceItem_Click);
                item.Tag = tag;
                tag++;
            }
        }

        private void 搜图说明ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("默认采集卡类型选择any，会自动选择合适的采集卡\n- 常见采集卡类型是DSHOW，MSMF，DC1394等\n- 如果出现黑屏、颜色不正确等情况，请切换其他采集卡类型，然后重新打开\n- 详细使用教程见群946057081文档", "采集卡");
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
            MessageBox.Show("- 使用电脑控制单片机的模式\n- 可视化运行，一键切换脚本（即将实装）\n- 无需反复刷固件\n- 支持超长脚本\n- 可使用虚拟手柄，用键盘玩游戏\n\n详细使用教程见群946057081文档", "联机模式");
        }

        private void 烧录模式ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("- 连线烧录后脱机运行的模式\n- 独立挂机，即插即用\n- 一键烧录，可控运行\n- 无需反复刷固件\n- 支持极限效率脚本\n\n详细使用教程见群946057081文档", "烧录模式");
        }

        private void 固件模式ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("- 生成固件后手动刷入单片机的模式\n- 独立挂机，即插即用\n- 支持极限效率脚本\n- 不需要任何额外配件\n\n详细使用教程见群946057081文档", "固件模式");
        }

        private void 关于ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show(@"详细使用教程见群946057081文档

Copyright © 2020. 铃落(Nukieberry)
Copyright © 2021. 云浅雪
Copyright © 2022. 卡尔(ca1e)", "关于");
        }

        private void 项目源码ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://github.com/nukieberry/PokemonTycoon") { UseShellExecute = true });
        }

        private void 脚本自动运行ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var menu = (ToolStripMenuItem)sender;
            menu.Checked = !menu.Checked;
        }

        private void 推送设置ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var form = new ConfigForm(_config);
            if(form.ShowDialog() == DialogResult.OK)
            {
                _config.AlertToken = form.TokenString;
                SaveConfig();
            }
        }

        private void 设备驱动配置ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var btform = new win32.BTDeviceForm();
            btform.ShowDialog();
            btform.Dispose();
        }
        #endregion

        private void EasyConForm_Resize(object sender, EventArgs e)
        {
            float newx = (this.Width) / Xvalue;
            float newy = this.Height / Yvalue;
            setControls(newx, newy, this);
        }

        private void setControls(float newx, float newy, Control cons)
        {
            foreach (Control con in cons.Controls)
            {
                string[] mytag = con.Tag.ToString().Split(new char[] { ':' });
                float a = Convert.ToSingle(mytag[0]) * newx;
                con.Width = (int)a;
                a = Convert.ToSingle(mytag[1]) * newy;
                con.Height = (int)(a);
                a = Convert.ToSingle(mytag[2]) * newx;
                con.Left = (int)(a);
                a = Convert.ToSingle(mytag[3]) * newy;
                con.Top = (int)(a);
                Single currentSize = Convert.ToSingle(mytag[4]) * newy;

                //改变字体大小
                con.Font = new Font(con.Font.Name, currentSize, con.Font.Style, con.Font.Unit);

                if (con.Controls.Count > 0)
                {
                    try
                    {
                        setControls(newx, newy, con);
                    }
                    catch
                    { }
                }
            }
        }
    }
}
