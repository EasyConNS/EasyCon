using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using VideoCaptureCore;
using VideoCaptureCore.helper;

namespace CaptureUI
{
    public partial class Form1 : Form
    {
        private BasicSampleApplication sample;
        private ObservableCollection<Process> processes;
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            InitComposition();
            InitWindowList();
        }

        private void InitComposition()
        {
            sample = compositionHostControl1.GenApp();
        }

        private void InitWindowList()
        {
            var processesWithWindows = from p in Process.GetProcesses()
                                       where !string.IsNullOrWhiteSpace(p.MainWindowTitle) && WindowEnumerationHelper.IsWindowValidForCapture(p.MainWindowHandle)
                                       select p;
            processes = new ObservableCollection<Process>(processesWithWindows);
            comboBox1.DataSource = processes;
            comboBox1.ValueMember = "MainWindowHandle";
            comboBox1.DisplayMember = "MainWindowTitle";

            comboBox1.SelectedIndex = -1;
            StopCapture();
        }

        private void StartHwndCapture(IntPtr hwnd)
        {
            sample.StartCaptureFromItem(hwnd);
        }

        private void StopCapture()
        {
            sample.StopCapture();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            StopCapture();
            comboBox1.SelectedIndex = -1;
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            var comboBox = (ComboBox)sender;
            var process = (Process)comboBox.SelectedItem;

            if (process != null)
            {
                StopCapture();
                var hwnd = process.MainWindowHandle;
                try
                {
                    StartHwndCapture(hwnd);
                }
                catch (Exception)
                {
                    Debug.WriteLine($"Hwnd 0x{hwnd.ToInt32():X8} is not valid for capture!");
                    processes.Remove(process);
                    comboBox.SelectedIndex = -1;
                }
            }
        }

        private void comboBox1_DropDown(object sender, EventArgs e)
        {
            var processesWithWindows = from p in Process.GetProcesses()
                                       where !string.IsNullOrWhiteSpace(p.MainWindowTitle) && WindowEnumerationHelper.IsWindowValidForCapture(p.MainWindowHandle)
                                       select p;
            processes = new ObservableCollection<Process>(processesWithWindows);
            comboBox1.DataSource = processes;
        }

        private async void button2_ClickAsync(object sender, EventArgs e)
        {
            Stopwatch sw = new Stopwatch();
            cc(sw);
        }

        private async void cc(Stopwatch sw)
        {

            sw.Reset();
            sw.Start();
            var st = await sample.GetBitmapAsync();
            pictureBox1.Image = new System.Drawing.Bitmap(st);
            sw.Stop();
            label1.Text = $"耗时: {sw.ElapsedMilliseconds} 毫秒\n";
        }
    }
}
