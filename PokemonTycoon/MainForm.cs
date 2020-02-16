using PTDevice;
using PTController;
using PokemonTycoon.Graphic;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PokemonTycoon
{
    public partial class MainForm : Form
    {
        NintendoSwitch NS = NintendoSwitch.GetInstance();
        internal TabScripting tabScripting;
        internal TabSampling tabSampling;
        internal TabMonitor tabMonitor;
        internal TabSettings tabSettings;
        internal FormController formController;
        internal FormProjection formProjection;

        Dictionary<TabPage, TabModule> _tabModule = new Dictionary<TabPage, TabModule>();

        class ControllerAdapter : IControllerAdapter
        {
            MainForm form;

            public ControllerAdapter(MainForm form)
            {
                this.form = form;
            }

            public Color CurrentLight => form.tabScripting.CurrentLight;

            public bool IsRunning()
            {
                return form.tabScripting.IsRunning();
            }
        }

        public MainForm()
        {
            InitializeComponent();

            // create tab modules
            _tabModule[tabPageScripting] = tabScripting = new TabScripting(this);
            _tabModule[tabPageSampling] = tabSampling = new TabSampling(this);
            _tabModule[tabPageMonitor] = tabMonitor = new TabMonitor(this);
            _tabModule[tabPageSettings] = tabSettings = new TabSettings(this);

            formController = new FormController(new ControllerAdapter(this));
            formProjection = new FormProjection(this);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Utils.WM_APP_Activate)
            {
                WindowState = FormWindowState.Normal;
                Activate();
            }
            base.WndProc(ref m);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // initialize modules
            NS.Connect("COM5");
            CaptureScreen(1);

            // initialize tab modules
            _tabModule.Values.ToList().ForEach(m => m.Init());

            // register keys
            for (int i = 0; i < 4; i++)
            {
                var _i = i;
                RegisterKeyEvent(Keys.F1 + i, false, null, null, () => Test(_i + 1));
            }
            Func<bool> sampleCond = () => checkBoxGraphicSampling.Checked;
            RegisterKeyEvent(Keys.LMenu, false, tabPageSampling, sampleCond, null, sampleCond, tabSampling.SamplePoint);
            RegisterKeyEvent(Keys.RMenu, false, tabPageSampling, sampleCond, null, sampleCond, tabSampling.SamplePoint);
            RegisterKeyEvent(Keys.LControlKey, false, tabPageSampling, sampleCond, tabSampling.SampleImageDown, sampleCond, tabSampling.SampleImageUp);
            RegisterKeyEvent(Keys.RControlKey, false, tabPageSampling, sampleCond, tabSampling.SampleImageDown, sampleCond, tabSampling.SampleImageUp);
            formController.RegisterKey(Keys.Y, NintendoSwitch.Button.A);
            formController.RegisterKey(Keys.U, NintendoSwitch.Button.B);
            formController.RegisterKey(Keys.I, NintendoSwitch.Button.X);
            formController.RegisterKey(Keys.H, NintendoSwitch.Button.Y);
            formController.RegisterKey(Keys.T, NintendoSwitch.Button.R);
            formController.RegisterKey(Keys.G, NintendoSwitch.Button.L);
            formController.RegisterKey(Keys.R, NintendoSwitch.Button.ZR);
            formController.RegisterKey(Keys.F, NintendoSwitch.Button.ZL);
            formController.RegisterKey(Keys.J, NintendoSwitch.Button.MINUS);
            formController.RegisterKey(Keys.K, NintendoSwitch.Button.PLUS);
            formController.RegisterKey(Keys.Q, NintendoSwitch.Button.LCLICK);
            formController.RegisterKey(Keys.E, NintendoSwitch.Button.RCLICK);
            formController.RegisterKey(Keys.Z, NintendoSwitch.Button.CAPTURE);
            formController.RegisterKey(Keys.C, NintendoSwitch.Button.HOME);
            formController.RegisterKey(Keys.NumPad1, NintendoSwitch.HAT.BOTTOM_LEFT);
            formController.RegisterKey(Keys.NumPad2, NintendoSwitch.HAT.BOTTOM);
            formController.RegisterKey(Keys.NumPad3, NintendoSwitch.HAT.BOTTOM_RIGHT);
            formController.RegisterKey(Keys.NumPad4, NintendoSwitch.HAT.LEFT);
            formController.RegisterKey(Keys.NumPad5, NintendoSwitch.HAT.CENTER);
            formController.RegisterKey(Keys.NumPad6, NintendoSwitch.HAT.RIGHT);
            formController.RegisterKey(Keys.NumPad7, NintendoSwitch.HAT.TOP_LEFT);
            formController.RegisterKey(Keys.NumPad8, NintendoSwitch.HAT.TOP);
            formController.RegisterKey(Keys.NumPad9, NintendoSwitch.HAT.TOP_RIGHT);
            formController.RegisterKey(Keys.W, () => NS.LeftDirection(NintendoSwitch.DirectionKey.Up, true), () => NS.LeftDirection(NintendoSwitch.DirectionKey.Up, false));
            formController.RegisterKey(Keys.S, () => NS.LeftDirection(NintendoSwitch.DirectionKey.Down, true), () => NS.LeftDirection(NintendoSwitch.DirectionKey.Down, false));
            formController.RegisterKey(Keys.A, () => NS.LeftDirection(NintendoSwitch.DirectionKey.Left, true), () => NS.LeftDirection(NintendoSwitch.DirectionKey.Left, false));
            formController.RegisterKey(Keys.D, () => NS.LeftDirection(NintendoSwitch.DirectionKey.Right, true), () => NS.LeftDirection(NintendoSwitch.DirectionKey.Right, false));
            formController.RegisterKey(Keys.Up, () => NS.RightDirection(NintendoSwitch.DirectionKey.Up, true), () => NS.RightDirection(NintendoSwitch.DirectionKey.Up, false));
            formController.RegisterKey(Keys.Down, () => NS.RightDirection(NintendoSwitch.DirectionKey.Down, true), () => NS.RightDirection(NintendoSwitch.DirectionKey.Down, false));
            formController.RegisterKey(Keys.Left, () => NS.RightDirection(NintendoSwitch.DirectionKey.Left, true), () => NS.RightDirection(NintendoSwitch.DirectionKey.Left, false));
            formController.RegisterKey(Keys.Right, () => NS.RightDirection(NintendoSwitch.DirectionKey.Right, true), () => NS.RightDirection(NintendoSwitch.DirectionKey.Right, false));
            formController.RegisterKey(Keys.X, () => tabScripting.Try(tabScripting.Turbo));

            ActivateTab();

            // UI updating timer
            Thread t = new Thread(UpdateUI);
            t.IsBackground = true;
            t.Start();

            formController.ControllerEnabledLevel = 1;
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            _tabModule.Values.ToList().ForEach(m => m.Close());
            VideoCapture.VideoSource?.SignalToStop();
        }

        private bool Test(int fi = 1)
        {
            if (fi == 1)
            {
            }
            else if (fi == 2)
            {

            }
            else if (fi == 3)
            {
            }
            else if (fi == 4)
            {
            }
            return true;
        }

        public void RegisterKeyEvent(Keys key, bool global, TabPage tab, Func<bool> keydown, Func<bool> keyup = null)
        {
            if (keydown == null)
                keydown = () => true;
            if (keyup == null)
                keyup = () => true;
            Func<Func<bool>, Func<bool>> wrapper = f =>
            {
                return () =>
                {
                    if (!global && !ContainsFocus)
                        return false;
                    if (tab != null && tabControl1.SelectedTab != tab)
                        return false;
                    return f.Invoke();
                };
            };
            LowLevelKeyboard.GetInstance().RegisterKeyEvent((int)key, wrapper(keydown), wrapper(keyup));
        }

        public void RegisterKeyEvent(Keys key, bool global, TabPage tab, Func<bool> keydownCondition, Action keydownAction, Func<bool> keyupCondition, Action keyupAction = null)
        {
            Func<Func<bool>, Action, Func<bool>> wrapper = (con, f) =>
            {
                return () =>
                {
                    if (con?.Invoke() == false)
                        return false;
                    f?.Invoke();
                    return true;
                };
            };
            RegisterKeyEvent(key, global, tab, wrapper(keydownCondition, keydownAction), wrapper(keyupCondition, keyupAction));
        }

        void ActivateTab()
        {
            var tab = tabControl1.SelectedTab;
            if (_tabModule.ContainsKey(tab))
                _tabModule[tab].Activate();
        }

        void UpdateUI()
        {
            try
            {
                while (true)
                {
                    tabScripting.UpdateUI();
                    Thread.Sleep(27);
                }
            }
            catch
            { }
        }

        public void CaptureCamera(int? index = null)
        {
            VideoCapture.CaptureCamera(index);
            videoSourcePlayerMonitor.VideoSource = VideoCapture.VideoSource;
            buttonCaptureCamera.Text = $"采集USB[{VideoCapture.CameraIndex}]";
            buttonCaptureScreen.Text = $"采集屏幕";
        }

        public void CaptureScreen(int? index = null)
        {
            VideoCapture.CaptureScreen(index);
            videoSourcePlayerMonitor.VideoSource = VideoCapture.VideoSource;
            buttonCaptureCamera.Text = $"采集USB";
            buttonCaptureScreen.Text = $"采集屏幕[{VideoCapture.ScreenIndex}]";
        }

        private void buttonGraphicGetColor_Click(object sender, EventArgs e)
        {
            int x, y;
            if (!int.TryParse(textBoxGraphicX.Text, out x) || !int.TryParse(textBoxGraphicY.Text, out y))
            {
                MessageBox.Show("格式错误！");
                return;
            }
            tabSampling.GetColor(x, y);
        }

        private void buttonGraphicGetIImage_Click(object sender, EventArgs e)
        {
            int x, y, w, h;
            if (!int.TryParse(textBoxGraphicX.Text, out x) || !int.TryParse(textBoxGraphicY.Text, out y) || !int.TryParse(textBoxGraphicWidth.Text, out w) || !int.TryParse(textBoxGraphicHeight.Text, out h))
            {
                MessageBox.Show("格式错误！");
                return;
            }
            tabSampling.GetImage(x, y, w, h);
        }

        private void buttonGraphicSearchColor_Click(object sender, EventArgs e)
        {
            int r, g, b;
            double cap;
            if (!int.TryParse(textBoxGraphicColorR.Text, out r) || !int.TryParse(textBoxGraphicColorG.Text, out g) || !int.TryParse(textBoxGraphicColorB.Text, out b) || !double.TryParse(textBoxGraphicCap.Text, out cap))
            {
                MessageBox.Show("格式错误！");
                return;
            }
            tabSampling.SearchColor(r, g, b, cap);
        }

        private void buttonGraphicSearchImage_Click(object sender, EventArgs e)
        {
            double cap;
            if (!double.TryParse(textBoxGraphicCap.Text, out cap))
            {
                MessageBox.Show("格式错误！");
                return;
            }
            if (tabSampling.SampleImage == null)
            {
                MessageBox.Show("需要图片作为目标！");
                return;
            }
            tabSampling.SearchImage(cap);
        }

        private void buttonGraphicFreeze_Click(object sender, EventArgs e)
        {
            VideoCapture.Freeze();
        }

        private void buttonGraphicUnfreeze_Click(object sender, EventArgs e)
        {
            VideoCapture.Unfreeze();
        }

        private void buttonGraphicGetFiles_Click(object sender, EventArgs e)
        {
            tabSampling.LoadImageList();
        }

        private void buttonGraphicOpenImage_Click(object sender, EventArgs e)
        {
            try
            {
                tabSampling.LoadImage(comboBoxGraphicFilename.Text);
            }
            catch (FileNotFoundException)
            {
                MessageBox.Show("文件不存在！");
            }
            catch (OutOfMemoryException)
            {
                MessageBox.Show("读取失败！");
            }
        }

        private void buttonGraphicSaveImage_Click(object sender, EventArgs e)
        {
            if (tabSampling.SampleImage == null)
            {
                MessageBox.Show("图片不存在！");
                return;
            }
            var path = ImageRes.GetImagePath(comboBoxGraphicFilename.Text);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path) && MessageBox.Show("文件已存在，是否覆盖？", "", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;
            tabSampling.SaveImage();
        }

        private void buttonGraphicSaveShadow_Click(object sender, EventArgs e)
        {
            var path = ImageRes.GetImagePath(comboBoxGraphicFilename.Text);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path) && MessageBox.Show("文件已存在，是否覆盖？", "", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;
            tabSampling.SaveShadow();
        }

        private void buttonScriptTest_Click(object sender, EventArgs e)
        {
            tabScripting.Test();
        }

        private void richTextBoxScriptingMessage_KeyDown(object sender, KeyEventArgs e)
        {
            var box = richTextBoxMessage;
            if (
                box.GetLineFromCharIndex(box.SelectionStart) == 0 && e.KeyData == Keys.Up ||
                box.GetLineFromCharIndex(box.SelectionStart) == box.GetLineFromCharIndex(box.TextLength) && e.KeyData == Keys.Down ||
                box.SelectionStart == box.TextLength && e.KeyData == Keys.Right ||
                box.SelectionStart == 0 && e.KeyData == Keys.Left)
                e.Handled = true;
        }

        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            ActivateTab();
        }

        private void buttonCaptureCamera_Click(object sender, EventArgs e)
        {
            CaptureCamera();
        }

        private void buttonCaptureScreen_Click(object sender, EventArgs e)
        {
            CaptureScreen();
        }

        private void buttonScriptEggRun_Click(object sender, EventArgs e)
        {
            tabScripting.EggHatch();
        }

        private void buttonScriptEggSavePreset_Click(object sender, EventArgs e)
        {
            tabScripting.SavePreset();
        }

        private void buttonScriptEggDeletePreset_Click(object sender, EventArgs e)
        {
            tabScripting.DeletePreset();
        }

        private void comboBoxScriptEggPreset_SelectedIndexChanged(object sender, EventArgs e)
        {
            tabScripting.UsePreset(comboBoxScriptEggPreset.SelectedItem as string);
        }

        private void buttonScriptReleaseRun_Click(object sender, EventArgs e)
        {
            tabScripting.Release();
        }

        private void buttonScriptRaidRunSearch_Click(object sender, EventArgs e)
        {
            tabScripting.RaidSearch();
        }

        private void buttonScriptRaidRunWatt_Click(object sender, EventArgs e)
        {
            tabScripting.RaidWatt();
        }

        private void buttonGraphicPickAbility_Click(object sender, EventArgs e)
        {
            tabSampling.SetRect(1500, 618, 160, 30, true);
        }

        private void numericUpDownScriptRaidDays_ValueChanged(object sender, EventArgs e)
        {
            tabScripting.UpdateRaidEndDate();
        }

        private void buttonScriptRaidRunLeap_Click(object sender, EventArgs e)
        {
            tabScripting.RaidLeap();
        }

        private void buttonScriptRaidRunSkip_Click(object sender, EventArgs e)
        {
            tabScripting.RaidSkip();
        }

        private void buttonScriptRaidRunSkipFast_Click(object sender, EventArgs e)
        {
            tabScripting.RaidSkipFast();
        }

        private void buttonScriptRaidRunRestartNG_Click(object sender, EventArgs e)
        {
            tabScripting.RaidNG();
        }

        private void buttonScriptRaidRunRestart_Click(object sender, EventArgs e)
        {
            tabScripting.RaidRestartLeap();
        }

        private void buttonScriptRaidRunBattle_Click(object sender, EventArgs e)
        {
            tabScripting.RaidAutoBattle();
        }

        private void buttonScriptRaidTargetRemove_Click(object sender, EventArgs e)
        {
            tabScripting.RaidTargetRemove();
        }

        private void buttonScriptRaidTargetClear_Click(object sender, EventArgs e)
        {
            tabScripting.RaidTargetClear();
        }

        private void comboBoxScriptRaidTarget_SelectedIndexChanged(object sender, EventArgs e)
        {
            tabScripting.RaidTargetAdd();
        }

        private void comboBoxGraphicCapturedShadows_SelectedIndexChanged(object sender, EventArgs e)
        {
            tabSampling.SelectCapturedShadows();
        }

        private void buttonScriptTowerRun_Click(object sender, EventArgs e)
        {
            tabScripting.TowerRush();
        }

        private void buttonScriptTurbo_Click(object sender, EventArgs e)
        {
            tabScripting.Turbo();
        }

        private void buttonScriptFossilRun_Click(object sender, EventArgs e)
        {
            tabScripting.Fossil();
        }

        private void buttonScriptDiggingRun_Click(object sender, EventArgs e)
        {
            tabScripting.Digging();
        }

        private void textBoxScriptRaidStartDate_TextChanged(object sender, EventArgs e)
        {
            tabScripting.UpdateRaidEndDate();
        }

        private void buttonScriptRaidRunCreate_Click(object sender, EventArgs e)
        {
            tabScripting.RaidCreate();
        }

        private void buttonShowController_Click(object sender, EventArgs e)
        {
            formController.ControllerEnabledLevel = 2;
        }

        private void buttonShowProjection_Click(object sender, EventArgs e)
        {
            formProjection.Show();
        }

        private void buttonScriptStop_Click(object sender, EventArgs e)
        {
            tabScripting.Stop();
        }

        private void buttonScriptRaidRunGetRareDen_Click(object sender, EventArgs e)
        {
            tabScripting.RaidGetRareDen();
        }
    }
}
