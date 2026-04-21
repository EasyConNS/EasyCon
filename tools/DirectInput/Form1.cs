namespace DirectInput
{
    public partial class Form1 : Form
    {
        private XboxControllerMonitor _controllerMonitor;
        private Dictionary<string, ButtonDisplay> _buttonDisplays;
        private System.Timers.Timer _updateTimer;

        public Form1()
        {
            InitializeComponent();

            InitializeComponent1();
            InitializeButtonDisplays();
            InitializeControllerMonitor();
            SetupUpdateTimer();
        }

        private void InitializeButtonDisplays()
        {
            _buttonDisplays = new Dictionary<string, ButtonDisplay>();

            // 创建按钮显示控件
            CreateButtonDisplay("A", 150, 100);
            CreateButtonDisplay("B", 200, 70);
            CreateButtonDisplay("X", 100, 70);
            CreateButtonDisplay("Y", 150, 40);

            CreateButtonDisplay("DPadUp", 50, 150);
            CreateButtonDisplay("DPadDown", 50, 210);
            CreateButtonDisplay("DPadLeft", 20, 180);
            CreateButtonDisplay("DPadRight", 80, 180);

            CreateButtonDisplay("LeftShoulder", 50, 50);
            CreateButtonDisplay("RightShoulder", 250, 50);

            CreateButtonDisplay("LeftThumb", 100, 250);
            CreateButtonDisplay("RightThumb", 200, 250);

            CreateButtonDisplay("Start", 220, 180);
            CreateButtonDisplay("Back", 80, 180);

            // 触发器显示
            CreateTriggerDisplay("LeftTrigger", 50, 300);
            CreateTriggerDisplay("RightTrigger", 250, 300);

            // 摇杆显示
            CreateThumbstickDisplay("LeftThumbstick", 120, 350);
            CreateThumbstickDisplay("RightThumbstick", 180, 350);
        }

        private void CreateButtonDisplay(string buttonName, int x, int y)
        {
            var panel = new Panel
            {
                Size = new Size(40, 40),
                Location = new Point(x, y),
                BackColor = Color.LightGray,
                BorderStyle = BorderStyle.FixedSingle
            };

            var label = new Label
            {
                Text = buttonName,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Arial", 8)
            };

            panel.Controls.Add(label);
            Controls.Add(panel);

            _buttonDisplays[buttonName] = new ButtonDisplay
            {
                Panel = panel,
                Label = label
            };
        }

        private void CreateTriggerDisplay(string triggerName, int x, int y)
        {
            var panel = new Panel
            {
                Size = new Size(60, 20),
                Location = new Point(x, y),
                BackColor = Color.LightGray,
                BorderStyle = BorderStyle.FixedSingle
            };

            var progressBar = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Minimum = 0,
                Maximum = 255,
                Style = ProgressBarStyle.Continuous
            };

            var label = new Label
            {
                Text = triggerName,
                Location = new Point(0, -15),
                Size = new Size(60, 15),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Arial", 7)
            };

            panel.Controls.Add(progressBar);
            panel.Controls.Add(label);
            Controls.Add(panel);

            _buttonDisplays[triggerName] = new ButtonDisplay
            {
                Panel = panel,
                ProgressBar = progressBar
            };
        }

        private void CreateThumbstickDisplay(string thumbstickName, int x, int y)
        {
            var panel = new Panel
            {
                Size = new Size(60, 60),
                Location = new Point(x, y),
                BackColor = Color.LightGray,
                BorderStyle = BorderStyle.FixedSingle
            };

            var dot = new Panel
            {
                Size = new Size(10, 10),
                Location = new Point(25, 25),
                BackColor = Color.Red
            };

            var label = new Label
            {
                Text = thumbstickName,
                Location = new Point(0, -15),
                Size = new Size(60, 15),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Arial", 7)
            };

            panel.Controls.Add(dot);
            panel.Controls.Add(label);
            Controls.Add(panel);

            _buttonDisplays[thumbstickName] = new ButtonDisplay
            {
                Panel = panel,
                Dot = dot
            };
        }

        private void InitializeControllerMonitor()
        {
            _controllerMonitor = new XboxControllerMonitor();

            if (!_controllerMonitor.IsConnected())
            {
                MessageBox.Show("未检测到Xbox手柄！请连接手柄后重试。", "警告",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            _controllerMonitor.ButtonStateChanged += ControllerMonitor_ButtonStateChanged;
            _controllerMonitor.StartMonitoring();
        }

        private void ControllerMonitor_ButtonStateChanged(object sender, ButtonStateChangedEventArgs e)
        {
            // 在UI线程上更新显示
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateButtonDisplay(e)));
            }
            else
            {
                UpdateButtonDisplay(e);
            }
        }

        private void UpdateButtonDisplay(ButtonStateChangedEventArgs e)
        {
            if (_buttonDisplays.ContainsKey(e.Button))
            {
                var display = _buttonDisplays[e.Button];

                if (e.Button.Contains("Trigger"))
                {
                    // 更新触发器进度条
                    if (display.ProgressBar != null)
                    {
                        display.ProgressBar.Value = e.Value;
                        display.Panel.BackColor = e.Value > 30 ? Color.LightGreen : Color.LightGray;
                    }
                }
                else if (e.Button.Contains("Thumbstick"))
                {
                    // 更新摇杆位置
                    if (display.Dot != null)
                    {
                        // 将摇杆值(-32768 到 32767)映射到面板内位置
                        int xPos = (e.XValue / 6553) + 25; // 简化映射
                        int yPos = (e.YValue / 6553) + 25;

                        xPos = Math.Max(0, Math.Min(50, xPos));
                        yPos = Math.Max(0, Math.Min(50, yPos));

                        display.Dot.Location = new Point(xPos, yPos);
                        display.Panel.BackColor = (Math.Abs(e.XValue) > 8000 || Math.Abs(e.YValue) > 8000)
                            ? Color.LightGreen : Color.LightGray;
                    }
                }
                else
                {
                    // 更新按钮颜色
                    display.Panel.BackColor = e.IsPressed ? Color.LightGreen : Color.LightGray;
                }

                // 在状态栏显示最后按下的按钮
                statusLabel.Text = $"{e.Button}: {(e.IsPressed ? "按下" : "释放")} - {e.Timestamp:HH:mm:ss.fff}";
            }
        }

        private void SetupUpdateTimer()
        {
            _updateTimer = new();
            _updateTimer.Interval = 1000; // 每秒更新一次连接状态
            _updateTimer.Elapsed += UpdateTimer_Tick;
            _updateTimer.Start();
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            Invoke(delegate
            {
                connectionStatusLabel.Text = _controllerMonitor.IsConnected()
    ? "手柄已连接"
    : "手柄未连接";
                connectionStatusLabel.ForeColor = _controllerMonitor.IsConnected()
                    ? Color.Green
                    : Color.Red;
            });
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _controllerMonitor?.StopMonitoring();
            _updateTimer?.Stop();
        }

        // 窗体设计器生成的代码
        private Label statusLabel;
        private Label connectionStatusLabel;

        private void InitializeComponent1()
        {
            this.statusLabel = new Label();
            this.connectionStatusLabel = new Label();

            // 窗体设置
            this.Text = "Xbox手柄状态监控器";
            this.Size = new Size(400, 500);
            this.StartPosition = FormStartPosition.CenterScreen;

            // 状态标签
            this.statusLabel.Location = new Point(10, 420);
            this.statusLabel.Size = new Size(380, 20);
            this.statusLabel.Text = "等待输入...";

            // 连接状态标签
            this.connectionStatusLabel.Location = new Point(10, 450);
            this.connectionStatusLabel.Size = new Size(380, 20);
            this.connectionStatusLabel.Font = new Font("Arial", 10, FontStyle.Bold);

            this.Controls.Add(this.statusLabel);
            this.Controls.Add(this.connectionStatusLabel);

            this.FormClosing += MainForm_FormClosing;
        }

        private class ButtonDisplay
        {
            public Panel Panel { get; set; }
            public Label Label { get; set; }
            public ProgressBar ProgressBar { get; set; }
            public Panel Dot { get; set; }
        }
    }
}