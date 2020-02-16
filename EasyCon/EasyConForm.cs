using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO.Ports;
using PTDevice;
using System.Diagnostics;
using PTController;
using EasyCon.Script;
using System.Text.RegularExpressions;
using System.Threading;
using System.Media;
using System.IO;
using Newtonsoft.Json;
using PTDevice.Arduino;

namespace EasyCon
{
    public partial class EasyConForm : Form, IOutputAdapter
    {
        internal NintendoSwitch NS = NintendoSwitch.GetInstance();
        internal FormController formController;
        internal FormKeyMapping formKeyMapping;

        const string ConfigPath = @"config.json";
        Config _config;
        bool _eventdisabled = false;
        Thread _thread;
        Script.Script _program;
        DateTime _startTime = DateTime.MinValue;
        TimeSpan _lastRunningTime = TimeSpan.Zero;
        Queue<Tuple<RichTextBox, object, Color?>> _messages = new Queue<Tuple<RichTextBox, object, Color?>>();

        const string ScriptPath = @"Script\";
        const string FirmwarePath = @"Arduino.hex";
        string _currentFile = null;
        bool _fileEdited = false;

        public EasyConForm()
        {
            InitializeComponent();
            Icon = Properties.Resources.favicon;

            formController = new FormController(new ControllerAdapter());
            formKeyMapping = new FormKeyMapping();
            LoadConfig();
        }

        private void EasyConForm_Load(object sender, EventArgs e)
        {
            //var names = SerialPort.GetPortNames();
            //foreach (var s in names)
            //    Debug.WriteLine(s);
            //var uno = new PTDevice.Arduino.ArduinoSerial("COM5");
            //uno.Start();
            //uno.BytesReceived += bytes => Debug.WriteLine("> " + string.Join(" ", bytes.Select(b => b.ToString("X2"))));
            //uno.Write(0xA5);
            //uno.Write(0x82);

            RegisterKeys();

            // UI updating timer
            Thread t = new Thread(UpdateUI);
            t.IsBackground = true;
            t.Start();

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

#if DEBUG
            // pack release folder
            DirectoryInfo dir = new DirectoryInfo(@"..\Release");
            if (dir.Exists)
            {
                foreach (var fi in dir.GetFiles())
                    if (!fi.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) && !fi.Extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) && !fi.Extension.Equals(".hex", StringComparison.OrdinalIgnoreCase))
                        fi.Delete();
                var hex0 = @"F:\Users\Nukieberry\Documents\GitHub\SerialCon\Joystick.hex";
                var hex1 = Path.Combine(dir.FullName, FirmwarePath);
                if (File.Exists(hex0) && !File.Exists(hex1))
                    File.Copy(hex0, hex1);
                dir.MoveTo(Path.Combine(dir.Parent.FullName, "EasyCon"));
            }
            // enable debug print
            显示调试信息ToolStripMenuItem.Checked = true;
#endif
        }

        void Test(int key)
        {
            NS.Test(key);
            if (key == 8)
            {
                NS.Flash(new byte[] { 0xFF, 0xFF });
            }
        }

        void UpdateUI()
        {
            try
            {
                while (true)
                {
                    Invoke((Action)delegate
                    {
                        // run/stop button
                        if (_thread != null)
                            buttonScriptRunStop.Text = "终止";
                        else
                            buttonScriptRunStop.Text = "运行";

                        // serial port status
                        var status = NS.GetConnectionStatus();
                        if (status == PTDevice.Arduino.ArduinoSerial.Status.Connected)
                        {
                            labelSerialStatus.Text = "已连接(稳定模式)";
                            labelSerialStatus.ForeColor = Color.Lime;
                        }
                        else if (status == PTDevice.Arduino.ArduinoSerial.Status.ConnectedUnsafe)
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
                        labelTimer.ForeColor = _thread == null ? Color.White : Color.Lime;

                        // message
                        HashSet<RichTextBox> boxes = new HashSet<RichTextBox>();
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
                    Thread.Sleep(27);
                }
            }
            catch
            { }
        }

        void LoadConfig()
        {
            try
            {
                _config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(ConfigPath));
            }
            catch (Exception ex)
            {
                if (!(ex is FileNotFoundException))
                    MessageBox.Show("读取设置文件失败！");
                _config = new Config();
                _config.SetDefault();
            }
        }

        void SaveConfig()
        {
            File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(_config));
        }

        void RegisterKeys()
        {
            formController.UnregisterAllKeys();

            formController.RegisterKey(_config.KeyMapping.A, NintendoSwitch.Button.A);
            formController.RegisterKey(_config.KeyMapping.B, NintendoSwitch.Button.B);
            formController.RegisterKey(_config.KeyMapping.X, NintendoSwitch.Button.X);
            formController.RegisterKey(_config.KeyMapping.Y, NintendoSwitch.Button.Y);
            formController.RegisterKey(_config.KeyMapping.L, NintendoSwitch.Button.L);
            formController.RegisterKey(_config.KeyMapping.R, NintendoSwitch.Button.R);
            formController.RegisterKey(_config.KeyMapping.ZL, NintendoSwitch.Button.ZL);
            formController.RegisterKey(_config.KeyMapping.ZR, NintendoSwitch.Button.ZR);
            formController.RegisterKey(_config.KeyMapping.Plus, NintendoSwitch.Button.PLUS);
            formController.RegisterKey(_config.KeyMapping.Minus, NintendoSwitch.Button.MINUS);
            formController.RegisterKey(_config.KeyMapping.Capture, NintendoSwitch.Button.CAPTURE);
            formController.RegisterKey(_config.KeyMapping.Home, NintendoSwitch.Button.HOME);
            formController.RegisterKey(_config.KeyMapping.LClick, NintendoSwitch.Button.LCLICK);
            formController.RegisterKey(_config.KeyMapping.RClick, NintendoSwitch.Button.RCLICK);
            formController.RegisterKey(_config.KeyMapping.Up, NintendoSwitch.HAT.TOP);
            formController.RegisterKey(_config.KeyMapping.Down, NintendoSwitch.HAT.BOTTOM);
            formController.RegisterKey(_config.KeyMapping.Left, NintendoSwitch.HAT.LEFT);
            formController.RegisterKey(_config.KeyMapping.Right, NintendoSwitch.HAT.RIGHT);
            formController.RegisterKey(_config.KeyMapping.LSUp, () => NS.LeftDirection(NintendoSwitch.DirectionKey.Up, true), () => NS.LeftDirection(NintendoSwitch.DirectionKey.Up, false));
            formController.RegisterKey(_config.KeyMapping.LSDown, () => NS.LeftDirection(NintendoSwitch.DirectionKey.Down, true), () => NS.LeftDirection(NintendoSwitch.DirectionKey.Down, false));
            formController.RegisterKey(_config.KeyMapping.LSLeft, () => NS.LeftDirection(NintendoSwitch.DirectionKey.Left, true), () => NS.LeftDirection(NintendoSwitch.DirectionKey.Left, false));
            formController.RegisterKey(_config.KeyMapping.LSRight, () => NS.LeftDirection(NintendoSwitch.DirectionKey.Right, true), () => NS.LeftDirection(NintendoSwitch.DirectionKey.Right, false));
            formController.RegisterKey(_config.KeyMapping.RSUp, () => NS.RightDirection(NintendoSwitch.DirectionKey.Up, true), () => NS.RightDirection(NintendoSwitch.DirectionKey.Up, false));
            formController.RegisterKey(_config.KeyMapping.RSDown, () => NS.RightDirection(NintendoSwitch.DirectionKey.Down, true), () => NS.RightDirection(NintendoSwitch.DirectionKey.Down, false));
            formController.RegisterKey(_config.KeyMapping.RSLeft, () => NS.RightDirection(NintendoSwitch.DirectionKey.Left, true), () => NS.RightDirection(NintendoSwitch.DirectionKey.Left, false));
            formController.RegisterKey(_config.KeyMapping.RSRight, () => NS.RightDirection(NintendoSwitch.DirectionKey.Right, true), () => NS.RightDirection(NintendoSwitch.DirectionKey.Right, false));
        }

        public void Print(string message, Color? color, bool timestamp = true)
        {
            lock (_messages)
            {
                if (timestamp)
                    _messages.Enqueue(new Tuple<RichTextBox, object, Color?>(richTextBoxMessage, DateTime.Now.ToString("[HH:mm:ss.fff] "), Color.Gray));
                _messages.Enqueue(new Tuple<RichTextBox, object, Color?>(richTextBoxMessage, message, color));
                _messages.Enqueue(new Tuple<RichTextBox, object, Color?>(richTextBoxMessage, Environment.NewLine, null));
            }
        }

        public void Print(string message)
        {
            Print(message, null);
        }

        public void CLS()
        {
            lock (_messages)
                _messages.Enqueue(new Tuple<RichTextBox, object, Color?>(richTextBoxMessage, null, null));
        }

        public void StatusShowProgress(double d)
        {
            Invoke((Action)delegate
            {
                toolStripProgressBar1.Value = (int)(100 * d.Clamp(0, 1));
            });
        }

        public void StatusShowLog(string str)
        {
            Invoke((Action)delegate
            {
                toolStripStatusLabel1.Text = str;
            });
        }

        void ScriptSelectLine(int index)
        {
            if (index < 0)
                return;
            int position = textBoxScript.GetFirstCharIndexFromLine(index);
            if (position < 0)
            {
                // lineNumber is too big
                textBoxScript.Select(textBoxScript.Text.Length, 0);
            }
            else
            {
                int lineEnd = textBoxScript.Text.IndexOf(Environment.NewLine, position);
                if (lineEnd < 0)
                    lineEnd = textBoxScript.Text.Length;
                textBoxScript.Select(position, lineEnd - position);
            }
        }

        public bool ScriptCompile()
        {
            StatusShowLog("开始编译...");
            try
            {
                _eventdisabled = true;
                _program = new Script.Script();
                _program.Compile(textBoxScript.Text);
                textBoxScript.Text = _program.ToCode();
                textBoxScript.Select(0, 0);
                return true;
            }
            catch (CompileException ex)
            {
                _program = null;
                string str = $"{ex.Message}: 行{ex.Index + 1}";
                SystemSounds.Hand.Play();
                MessageBox.Show(str);
                StatusShowLog(str);
                ScriptSelectLine(ex.Index);
                return false;
            }
            finally
            {
                _eventdisabled = false;
            }
        }

        public void ScriptRun()
        {
            if (_program == null && !ScriptCompile())
                return;
            if (!SerialCheckConnect())
                return;
            if (!CheckFirmwareVersion())
                return;
            _thread = new Thread(_Run);
            _thread.IsBackground = true;
            _thread.Start();
        }

        void _Run()
        {
            try
            {
                _startTime = DateTime.Now;
                _lastRunningTime = TimeSpan.Zero;
                Invoke((Action)delegate
                {
                    textBoxScript.ReadOnly = true;
                    StatusShowLog("开始运行");
                });
                Print("-- 开始运行 --", Color.Lime);
                _program.Run(this);
                Print("-- 运行结束 --", Color.Lime);
                StatusShowLog("运行结束");
                SystemSounds.Beep.Play();
            }
            catch (ThreadAbortException)
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
            catch (Exception)
            {
                Print("-- 运行出错 --", Color.OrangeRed);
                StatusShowLog("运行出错");
                SystemSounds.Hand.Play();
            }
            finally
            {
                Invoke((Action)delegate
                {
                    textBoxScript.ReadOnly = false;
                });
                _lastRunningTime = DateTime.Now - _startTime;
                _startTime = DateTime.MinValue;
            }
            if (_thread == Thread.CurrentThread)
                _thread = null;
        }

        public void ScriptStop()
        {
            if (_thread != null)
            {
                _thread?.Abort();
                _thread = null;
                StatusShowLog("运行被终止");
                SystemSounds.Beep.Play();
            }
        }

        public bool SerialCheckConnect()
        {
            if (NS.IsConnected())
                return true;
            if (textBoxSerialPort.Text != "")
                return SerialConnect(textBoxSerialPort.Text);
            return SerialSearchConnect() != null;
        }

        public string SerialSearchConnect()
        {
            StatusShowLog("尝试连接...");
            var ports = SerialPort.GetPortNames();
            foreach (var portName in ports)
            {
                var r = NS.TryConnect(portName, true);
                if (显示调试信息ToolStripMenuItem.Checked)
                    Print($"{portName} {r.GetName()}");
                if (r == NintendoSwitch.ConnectResult.Success)
                {
                    StatusShowLog("连接成功");
                    textBoxSerialPort.Text = portName;
                    return portName;
                }
            }
            StatusShowLog("连接失败");
            SystemSounds.Hand.Play();
            MessageBox.Show("找不到设备！请确认：\n1.已经为单片机烧好固件\n2.已经连好TTL线（RX接0，TX接1，GND接GND）\n3.如果用的是CH340G，换一下帽子让3v3与S1相连（默认可能是5V与S1相连）\n\n可用端口：" + string.Join("、", ports));
            return null;
        }

        public bool SerialConnect(string portName)
        {
            StatusShowLog("开始连接...");
            int n;
            if (int.TryParse(portName, out n))
                portName = "COM" + portName;
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
                textBoxSerialPort.Text = portName;
                if (NS.GetConnectionStatus() == ArduinoSerial.Status.ConnectedUnsafe)
                    MessageBox.Show("正在使用单向连接模式。这是一种应急方案，并不表示成功连接到单片机，有可能无法正常工作。请检查连线并尽量使用稳定模式。");
                return true;
            }
            else
            {
                StatusShowLog("连接失败");
                SystemSounds.Hand.Play();
                MessageBox.Show("连接失败！该端口不存在、无法使用或已被占用");
                return false;
            }
        }

        public bool FileOpen()
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

        public bool FileSave(bool saveAs = false, bool close = false)
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

        public bool FileClose()
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
            textBoxScript.Text = string.Empty;
            _fileEdited = false;
            StatusShowLog("文件已关闭");
            return true;
        }

        public bool FileCreate()
        {
            var b = FileClose();
            StatusShowLog("新建完毕");
            return b;
        }

        public bool GenerateFirmware()
        {
            if (_program == null && !ScriptCompile())
                return false;
            try
            {
                StatusShowLog("开始生成固件...");
                var bytes = _program.Assemble();
#if DEBUG
                File.WriteAllBytes("1.bin", bytes);
#endif
                string hexStr;
                try
                {
                    hexStr = File.ReadAllText(FirmwarePath);
                }
                catch (Exception)
                {
                    StatusShowLog("固件生成失败");
                    SystemSounds.Hand.Play();
                    MessageBox.Show("固件读取失败！");
                    return false;
                }
                hexStr = Assembler.WriteHex(hexStr, bytes);
                string name = Path.GetFileNameWithoutExtension(_currentFile);
                var path = FirmwarePath.Replace(".", $"+Script.");
                File.WriteAllText(path, hexStr);
                StatusShowLog("固件生成完毕");
                SystemSounds.Beep.Play();
                MessageBox.Show("固件生成完毕！已保存为" + Path.GetFileName(path));
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
            catch (CompileException ex)
            {
                StatusShowLog("固件生成失败");
                SystemSounds.Hand.Play();
                MessageBox.Show("固件生成失败！" + ex.Message);
                ScriptSelectLine(ex.Index);
                return false;
            }
        }

        bool FlashPrepare()
        {
            if (!SerialCheckConnect())
                return false;
            if (NS.GetConnectionStatus() == ArduinoSerial.Status.ConnectedUnsafe)
            {
                MessageBox.Show("需要稳定模式才能烧录");
                return false;
            }
            return true;
        }

        public bool FlashProgram()
        {
            if (!FlashPrepare())
                return false;
            if (!CheckFirmwareVersion())
                return false;
            if (_program == null && !ScriptCompile())
                return false;
            try
            {
                StatusShowLog("开始烧录...");
                var bytes = _program.Assemble();
#if DEBUG
                File.WriteAllBytes("1.bin", bytes);
#endif
                if (!NS.Flash(bytes))
                {
                    StatusShowLog("烧录失败");
                    SystemSounds.Hand.Play();
                    MessageBox.Show("烧录失败！请检查设备连接后重试");
                    return false;
                }
            }
            catch (AssembleException ex)
            {
                StatusShowLog("烧录失败");
                SystemSounds.Hand.Play();
                MessageBox.Show("烧录失败！" + ex.Message);
                ScriptSelectLine(ex.Index);
                return false;
            }
            catch (CompileException ex)
            {
                StatusShowLog("烧录失败");
                SystemSounds.Hand.Play();
                MessageBox.Show("烧录失败！" + ex.Message);
                ScriptSelectLine(ex.Index);
                return false;
            }
            StatusShowLog("烧录完毕");
            SystemSounds.Beep.Play();
            MessageBox.Show("烧录完毕");
            return true;
        }

        bool CheckFirmwareVersion()
        {
            if (!SerialCheckConnect())
                return false;
            if (NS.GetConnectionStatus() == ArduinoSerial.Status.ConnectedUnsafe)
                return true;
            int ver = NS.GetVersion();
            if (ver < Assembler.FirmwareVersion)
            {
                StatusShowLog("需要更新固件");
                SystemSounds.Hand.Play();
                MessageBox.Show("固件版本不符，请重新刷入" + FirmwarePath);
                return false;
            }
            return true;
        }

        public bool FlashClear()
        {
            if (!FlashPrepare())
                return false;
            if (!NS.Flash(Assembler.EmptyAsm))
            {
                StatusShowLog("烧录失败");
                SystemSounds.Hand.Play();
                MessageBox.Show("烧录失败！请检查设备连接后重试");
                return false;
            }
            StatusShowLog("清除完毕");
            SystemSounds.Beep.Play();
            MessageBox.Show("清除完毕");
            return true;
        }

        void RemoteStart()
        {
            if (!SerialCheckConnect())
                return;
            if (NS.RemoteStart() || NS.GetConnectionStatus() == ArduinoSerial.Status.ConnectedUnsafe)
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

        void RemoteStop()
        {
            if (!SerialCheckConnect())
                return;
            if (NS.RemoteStop() || NS.GetConnectionStatus() == ArduinoSerial.Status.ConnectedUnsafe)
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

        void ShowControllerHelp()
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
            ScriptRun();
        }

        private void textBoxScript_TextChanged(object sender, EventArgs e)
        {
            if (_eventdisabled)
                return;
            _fileEdited = true;
            _program = null;
        }

        private void buttonScriptRunStop_Click(object sender, EventArgs e)
        {
            if (_thread == null)
                ScriptRun();
            else
                ScriptStop();
        }

        private void buttonScriptStop_Click(object sender, EventArgs e)
        {
            ScriptStop();
        }

        private void textBoxScript_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            if (textBoxScript.ReadOnly == false && e.KeyCode == Keys.Tab)
            {
            }
        }

        private void 关于ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("作者：铃落（Nukieberry）\n\nv1.01改动：\n- 新增调试信息显示\n- 修复了固件模式生成的固件用avr-programmer无法刷入的问题\n- 增加了一个CH340G常见问题的解决方法说明\n- 尝试修复一个导致自动搜索卡死的问题", "关于");
        }

        private void buttonSerialPortSearch_Click(object sender, EventArgs e)
        {
            if (SerialSearchConnect() != null)
                SystemSounds.Beep.Play();
        }

        private void buttonSerialPortConnect_Click(object sender, EventArgs e)
        {
            if (SerialConnect(textBoxSerialPort.Text))
                SystemSounds.Beep.Play();
        }

        private void 新建ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FileCreate();
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
            CLS();
        }

        private void EasyConForm_KeyDown(object sender, KeyEventArgs e)
        {
#if DEBUG
            if (e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F12)
                Test(e.KeyCode - Keys.F1 + 1);
#endif
        }

        private void buttonGenerateFirmware_Click(object sender, EventArgs e)
        {
            GenerateFirmware();
        }

        private void buttonFlash_Click(object sender, EventArgs e)
        {
            FlashProgram();
        }

        private void buttonRemoteStart_Click(object sender, EventArgs e)
        {
            RemoteStart();
        }

        private void buttonRemoteStop_Click(object sender, EventArgs e)
        {
            RemoteStop();
        }

        private void buttonFlashClear_Click(object sender, EventArgs e)
        {
            FlashClear();
        }

        private void 联机模式ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("- 使用电脑控制单片机的模式\n- 可视化运行，一键切换脚本（即将实装）\n- 无需反复刷固件\n- 支持超长脚本\n- 可使用虚拟手柄，用键盘玩游戏\n\n硬件准备：\n一个Arduino UNO R3官方版，外加杜邦线若干。\n一根USB Type-B线，连电脑用Flip把Arduino.hex固件烧进去，然后拔了连NS。\n一根USB-TTL线，USB端连电脑（TTL端连单片机，RX接0，TX接1，GND接GND）\n\n软件使用：\n1.菜单，文件，打开，选一个脚本\n2.点“运行”按钮", "联机模式");
        }

        private void 烧录模式ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("- 连线烧录后脱机运行的模式\n- 独立挂机，即插即用\n- 一键烧录，可控运行\n- 无需反复刷固件\n- 支持极限效率脚本\n\n硬件准备：\n一个Arduino UNO R3官方版，外加杜邦线若干。\n一根USB Type-B线，连电脑用Flip把Arduino.hex固件烧进去，然后拔了连NS。\n一根USB-TTL线，USB端连电脑，TTL端连单片机（RX接0，TX接1，GND接GND）\n\n软件使用：\n1.菜单，文件，打开，选一个脚本\n2.点“编译并烧录”按钮\n3.点“远程运行”来立即运行，或拔掉单片机，下次插上时会自动运行", "烧录模式");
        }

        private void 固件模式ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("- 生成固件后手动刷入单片机的模式\n- 独立挂机，即插即用\n- 支持极限效率脚本\n- 不需要任何额外配件\n\n硬件准备：\n一个Arduino UNO R3官方版，外加杜邦线若干。\n一根USB Type-B线。\n\n软件使用：\n1.菜单，文件，打开，选一个脚本\n2.点“生成固件”按钮\n3.单片机连电脑，用Flip把生成的Arduino+Script.hex固件刷进去，拔掉再连NS就会自动运行", "固件模式");
        }

        private void 显示调试信息ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            显示调试信息ToolStripMenuItem.Checked = !显示调试信息ToolStripMenuItem.Checked;
        }

        private void 项目源码ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("https://github.com/nukieberry/PokemonTycoon");
        }
    }
}
