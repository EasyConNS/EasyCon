using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PokemonTycoon
{
    public partial class Form1 : Form
    {
        TabScripting tabScripting;
        TabSampling tabSampling;
        TabSettings tabSettings;

        Dictionary<Keys, bool> _keyDown = new Dictionary<Keys, bool>();
        Dictionary<Keys, Dictionary<TabPage, Tuple<Func<bool>, Func<bool>>>> _keyEvent = new Dictionary<Keys, Dictionary<TabPage, Tuple<Func<bool>, Func<bool>>>>();
        Dictionary<TabPage, TabModule> _tabModule = new Dictionary<TabPage, TabModule>();

        public Form1()
        {
            tabScripting = new TabScripting(this);
            tabSampling = new TabSampling(this);
            tabSettings = new TabSettings(this);
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // register keys
            RegisterKeyEvent(Keys.Menu, tabPageSampling, tabSampling.SamplePointDown, tabSampling.SamplePointUp);
            RegisterKeyEvent(Keys.ControlKey, tabPageSampling, tabSampling.SampleImageDown, tabSampling.SampleImageUp);

            // register tab modules
            _tabModule[tabPageScripting] = tabScripting;
            _tabModule[tabPageSampling] = tabSampling;
            _tabModule[tabPageSettings] = tabSettings;
            ActivateTab();
        }

        void RegisterKeyEvent(Keys key, TabPage tab, Func<bool> keydown, Func<bool> keyup = null)
        {
            if (keydown == null)
                keydown = () => true;
            if (keyup == null)
                keyup = () => true;
            if (!_keyEvent.ContainsKey(key))
                _keyEvent[key] = new Dictionary<TabPage, Tuple<Func<bool>, Func<bool>>>();
            _keyEvent[key][tab] = new Tuple<Func<bool>, Func<bool>>(keydown, keyup);
        }

        bool IsKeyDown(Keys key)
        {
            return _keyDown.ContainsKey(key) && _keyDown[key];
        }

        abstract class TabModule
        {
            public virtual void Activate()
            { }
        }

        void ActivateTab()
        {
            var tab = tabControl1.SelectedTab;
            if (_tabModule.ContainsKey(tab))
                _tabModule[tab].Activate();
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            var key = e.KeyCode;
            var tab = tabControl1.SelectedTab;
            if (IsKeyDown(key))
                return;
            _keyDown[key] = true;
            if (_keyEvent.ContainsKey(key) && _keyEvent[key].ContainsKey(tab))
                e.Handled = _keyEvent[key][tab].Item1.Invoke();
        }

        private void Form1_KeyUp(object sender, KeyEventArgs e)
        {
            var key = e.KeyCode;
            var tab = tabControl1.SelectedTab;
            if (!IsKeyDown(key))
                return;
            _keyDown[key] = false;
            if (_keyEvent.ContainsKey(key) && _keyEvent[key].ContainsKey(tab))
                e.Handled = _keyEvent[key][tab].Item2.Invoke();
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

        private void buttonGraphicCapture_Click(object sender, EventArgs e)
        {
            Monitor.Capture();
        }

        private void buttonGraphicRelease_Click(object sender, EventArgs e)
        {
            Monitor.Release();
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
            var box = richTextBoxScriptingMessage;
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

        private void buttonSettingsScreen_Click(object sender, EventArgs e)
        {
            tabSettings.ChangeScreen();
        }

        private void buttonSettingsScreenPreview_Click(object sender, EventArgs e)
        {
            tabSettings.PreviewScreen();
        }
    }
}
