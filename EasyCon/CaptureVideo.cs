using AForge.Video;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using EasyCon.Graphic;
using Newtonsoft.Json;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;

namespace EasyCon
{
    public partial class CaptureVideo : Form
    {
        private enum MonitorMode
        {
            NoBorder = 0,
            Editor = 1,
        }

        bool isMouseDown = false;
        Point mouseOffset;
        double monitorScale = 1.1;
        int monitorHorOrVerZoom = 0;
        MonitorMode monitorMode = MonitorMode.Editor;
        private bool IsFirst = true;
        private float X;//当前窗体的宽度
        private float Y;//当前窗体的高度
        Point curResolution = new Point(1920, 1080);

        /// <summary>
        /// 将控件的宽，高，左边距，顶边距和字体大小暂存到tag属性中
        /// </summary>
        /// <param name="cons">递归控件中的控件</param>
        private void setTag(Control cons)
        {
            foreach (Control con in cons.Controls)
            {
                con.Tag = con.Width + ":" + con.Height + ":" + con.Left + ":" + con.Top + ":" + con.Font.Size;
                if (con.Controls.Count > 0)
                    setTag(con);
            }
        }

        //根据窗体大小调整控件大小
        private void setControls(float newx, float newy, Control cons)
        {
            //遍历窗体中的控件，重新设置控件的值
            foreach (Control con in cons.Controls)
            {

                string[] mytag = con.Tag.ToString().Split(new char[] { ':' });//获取控件的Tag属性值，并分割后存储字符串数组
                float a = System.Convert.ToSingle(mytag[0]) * newx;//根据窗体缩放比例确定控件的值，宽度
                con.Width = (int)a;//宽度
                a = System.Convert.ToSingle(mytag[1]) * newy;//高度
                con.Height = (int)(a);
                a = System.Convert.ToSingle(mytag[2]) * newx;//左边距离
                con.Left = (int)(a);
                a = System.Convert.ToSingle(mytag[3]) * newy;//上边缘距离
                con.Top = (int)(a);
                Single currentSize = System.Convert.ToSingle(mytag[4]) * newy;//字体大小
                con.Font = new Font(con.Font.Name, currentSize, con.Font.Style, con.Font.Unit);
                if (con.Controls.Count > 0)
                {
                    setControls(newx, newy, con);
                }
            }
        }

        public static List<ImgLabel> imgLabels = new List<ImgLabel>();
        public CaptureVideo()
        {
            InitializeComponent();
        }

        public CaptureVideo(int deviceId)
        {
            InitializeComponent();

            Debug.WriteLine(deviceId);
            VideoCapture.CaptureCamera(VideoSourcePlayerMonitor,deviceId);
        }

        private void CaptureVideo_Load(object sender, EventArgs e)
        {
            X = this.Width;//获取窗体的宽度
            Y = this.Height;//获取窗体的高度
            setTag(this);//调用方法

            // get the search methord list
            List<ImgLabel.SearchMethod> searchMethods = ImgLabel.GetAllSearchMethod();
            foreach (var method in searchMethods)
            {
                searchMethodComBox.Items.Add(method.ToDescription());
            }

            // data binding
            textBox2.DataBindings.Add("Text", curImgLabel, "RangeX");
            textBox3.DataBindings.Add("Text", curImgLabel, "RangeY");
            textBox1.DataBindings.Add("Text", curImgLabel, "RangeWidth");
            textBox6.DataBindings.Add("Text", curImgLabel, "RangeHeight");
            textBox4.DataBindings.Add("Text", curImgLabel, "TargetX");
            textBox5.DataBindings.Add("Text", curImgLabel, "TargetY");
            textBox7.DataBindings.Add("Text", curImgLabel, "TargetWidth");
            textBox8.DataBindings.Add("Text", curImgLabel, "TargetHeight");
            textBox9.DataBindings.Add("Text", curImgLabel, "matchDegree");

            // load the imglabel
            curImgLabel.setSource(() => VideoCapture.GetImage());

            // if no imglabel, create one
            string parentDir = System.Windows.Forms.Application.StartupPath + "\\ImgLabel\\";
            if (!Directory.Exists(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            string[] file = Directory.GetFiles(parentDir, "*.IL");
            for (int i = 0; i < file.Length; i++)
            {
                ImgLabel temp = JsonConvert.DeserializeObject<ImgLabel>(File.ReadAllText(file[i]));
                temp.refresh(()=>VideoCapture.GetImage());
                imgLabels.Add(temp);
                imgLableList.Items.Add(temp.name);
            }

        }
        
        private void CaptureVideo_Resize(object sender, EventArgs e)
        {
            //如果是第一次运行，需要把下面的if语句取消注释，否则会没反应，其以后再运行或调试的时候，就把它注释即可
            if (IsFirst) { IsFirst = false; return; }
            float newx = (this.Width) / X; //窗体宽度缩放比例
            float newy = (this.Height) / Y;//窗体高度缩放比例
            setControls(newx, newy, this);//随窗体改变控件大小
        }

        private void CaptureVideo_FormClosed(object sender, FormClosedEventArgs e)
        {
            VideoCapture.Close();
        }

        private void VideoSourcePlayerMonitor_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (monitorMode == MonitorMode.NoBorder)
            {
                // change to editor
                this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
                this.VideoSourcePlayerMonitor.Dock = System.Windows.Forms.DockStyle.None;
                monitorMode = MonitorMode.Editor;
                this.Refresh();
            }
            else if (monitorMode == MonitorMode.Editor)
            {
                // change to noborder
                this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
                this.VideoSourcePlayerMonitor.Dock = System.Windows.Forms.DockStyle.Fill;
                monitorMode = MonitorMode.NoBorder;
                this.VideoSourcePlayerMonitor.BringToFront();
            }
        }

        private void VideoSourcePlayerMonitor_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            if (e.Control)
            {
                monitorHorOrVerZoom = 1;
            }
            else if (e.Shift)
            {
                monitorHorOrVerZoom = 2;
            }
        }

        private void VideoSourcePlayerMonitor_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Control control = sender as Control;
                int offsetX = -e.X;
                int offsetY = -e.Y;

                if (!(control is System.Windows.Forms.Form))
                {
                    offsetX = offsetX - control.Left;
                    offsetY = offsetY - control.Top;
                }
                mouseOffset = new Point(offsetX, offsetY);
                isMouseDown = true;
                Debug.WriteLine("mouse down" + offsetX + " " + offsetY);
            }
        }

        private void VideoSourcePlayerMonitor_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isMouseDown = false;
                Debug.WriteLine("mouse up");
            }
        }

        private void VideoSourcePlayerMonitor_MouseMove(object sender, MouseEventArgs e)
        {
            if (isMouseDown)
            {
                Point mouse = Control.MousePosition;
                mouse.Offset(mouseOffset.X, mouseOffset.Y);
                Debug.WriteLine("mouse move" + mouseOffset.X + " " + mouseOffset.Y);
                this.Location = mouse;
            }
        }

        private void VideoSourcePlayerMonitor_MouseWheel(object sender, MouseEventArgs e)
        {
            if (monitorMode == MonitorMode.Editor)
                return;

            var newSize = new Size(this.Size.Width, this.Size.Height);
            monitorScale = (e.Delta > 0) ? 1.1 : 0.90909;

            Debug.WriteLine(monitorScale.ToString() + " " + newSize.ToString());

            if (monitorHorOrVerZoom == 1)
            {
                newSize.Height = (int)(newSize.Height * monitorScale);
            }
            else if (monitorHorOrVerZoom == 2)
            {
                newSize.Width = (int)(newSize.Width * monitorScale);
            }
            else
            {
                newSize.Width = (int)(newSize.Width * monitorScale);
                newSize.Height = (int)(newSize.Height * monitorScale);
            }

            Debug.WriteLine("new size:" + newSize.ToString());
            monitorHorOrVerZoom = 0;
            this.Size = newSize;
        }

        private enum SnapshotMode
        {
            NoAction,
            FirstZoom,
            SecondRangeSelect,
            ThridObjSelect,
            Refresh
        }

        private Graphics SnapshotGraphic;
        static private Bitmap snapshot;
        private Point SnapshotLMDP = new Point();
        private Point SnapshotLMMD = new Point();
        private bool SnapshotLMMing;

        private Point SnapshotRangeMDP = new Point();
        private Point SnapshotRangeMMP = new Point();
        private bool SnapshotRangeMove;

        private SnapshotMode snapshotMode = SnapshotMode.NoAction;
        private Point SnapshotPos = new Point(0, 0);
        private Rectangle SnapshotRangeR = new Rectangle(0, 0, 0, 0);
        private Rectangle SnapshotSearchObjR = new Rectangle(0, 0, 0, 0);
        private ImgLabel curImgLabel = new ImgLabel();
        private PointF snapshotScale;
        static private Bitmap ss;
        private void Snapshot_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                SnapshotLMDP.X = e.X;
                SnapshotLMDP.Y = e.Y;
                //Debug.WriteLine("donw:"+e.X.ToString()+" "+e.Y.ToString());
                SnapshotLMMing = true;
                Snapshot.Focus();
            }
            else if (e.Button == MouseButtons.Right)
            {
                SnapshotRangeMDP.X = e.X;
                SnapshotRangeMDP.Y = e.Y;
                SnapshotRangeMove = true;
                //Debug.WriteLine("donw:"+e.X.ToString()+" "+e.Y.ToString());
                Snapshot.Focus();
            }
        }

        private void Snapshot_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                SnapshotLMMing = false;
            }
            else if (e.Button == MouseButtons.Right)
            {
                SnapshotRangeMove = false;
            }
        }

        private void Snapshot_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && SnapshotLMMing)
            {
                Snapshot.Focus();
                SnapshotLMMD.X = e.X - SnapshotLMDP.X;
                SnapshotLMMD.Y = e.Y - SnapshotLMDP.Y;
                SnapshotLMDP.X = e.X;
                SnapshotLMDP.Y = e.Y;
                Snapshot.Refresh();
            }
            else if (e.Button == MouseButtons.Right && SnapshotRangeMove)
            {
                Snapshot.Focus();
                SnapshotRangeMMP.X = e.X;
                SnapshotRangeMMP.Y = e.Y;
                Snapshot.Refresh();
            }
        }

        private void Snapshot_MouseWheel(object sender, MouseEventArgs e)
        {
            Snapshot.Focus();    //鼠标在picturebox上时才有焦点，此时可以缩放
            //var newSize = new Size(this.Size.Width, this.Size.Height);
            snapshotScale.X += (e.Delta > 0) ? -0.1f : 0.1f;
            snapshotScale.Y += (e.Delta > 0) ? -0.1f : 0.1f;

            // limit the scale
            snapshotScale.X = (float)Math.Min(Math.Max(snapshotScale.X, 0.5), 3.0);
            snapshotScale.Y = (float)Math.Min(Math.Max(snapshotScale.Y, 0.5), 3.0);

            Debug.WriteLine("bmpscale:" + snapshotScale.ToString());
            Snapshot.Refresh();
        }

        private void Snapshot_Paint(object sender, PaintEventArgs e)
        {

            if (snapshot == null)
                return;

            SnapshotGraphic = e.Graphics;

            Rectangle delta = new Rectangle();
            delta.X = SnapshotPos.X - (int)((SnapshotLMMD.X) * snapshotScale.X);
            delta.Y = SnapshotPos.Y - (int)((SnapshotLMMD.Y) * snapshotScale.Y);
            delta.Width = (int)(Snapshot.Width * snapshotScale.X);
            delta.Height = (int)(Snapshot.Height * snapshotScale.Y);
            SnapshotLMMD.X = 0;
            SnapshotLMMD.Y = 0;

            if (snapshotMode != SnapshotMode.NoAction)
            {
                Graphics g = Graphics.FromImage(snapshot);
                g.Clear(Color.FromArgb(240, 240, 240));
                g.DrawImage(ss, new Rectangle(ss.Width, ss.Height, ss.Width, ss.Height), new Rectangle(0, 0, ss.Width, ss.Height), GraphicsUnit.Pixel);

                if (snapshotMode == SnapshotMode.SecondRangeSelect)
                {
                    // cal the range start pos in bitmap
                    SnapshotRangeR.X = SnapshotPos.X + (int)((SnapshotRangeMDP.X) * snapshotScale.X);
                    SnapshotRangeR.Y = SnapshotPos.Y + (int)((SnapshotRangeMDP.Y) * snapshotScale.Y);
                    SnapshotRangeR.Width = (int)((SnapshotRangeMMP.X - SnapshotRangeMDP.X) * snapshotScale.X);
                    SnapshotRangeR.Height = (int)((SnapshotRangeMMP.Y - SnapshotRangeMDP.Y) * snapshotScale.Y);
                    curImgLabel.RangeX = SnapshotRangeR.X + 2 - curResolution.X;
                    curImgLabel.RangeY = SnapshotRangeR.Y + 2 - curResolution.Y;
                    curImgLabel.RangeWidth = SnapshotRangeR.Width - 3;
                    curImgLabel.RangeHeight = SnapshotRangeR.Height - 3;
                }

                // range rectangle
                g.DrawRectangle(new Pen(Color.Red, 3), SnapshotRangeR);

                if (snapshotMode == SnapshotMode.ThridObjSelect)
                {
                    // cal the range start pos in bitmap
                    SnapshotSearchObjR.X = SnapshotPos.X + (int)((SnapshotRangeMDP.X) * snapshotScale.X);
                    SnapshotSearchObjR.Y = SnapshotPos.Y + (int)((SnapshotRangeMDP.Y) * snapshotScale.Y);
                    SnapshotSearchObjR.Width = (int)((SnapshotRangeMMP.X - SnapshotRangeMDP.X) * snapshotScale.X);
                    SnapshotSearchObjR.Height = (int)((SnapshotRangeMMP.Y - SnapshotRangeMDP.Y) * snapshotScale.Y);
                    curImgLabel.TargetX = SnapshotSearchObjR.X + 1 - curResolution.X;
                    curImgLabel.TargetY = SnapshotSearchObjR.Y + 1 - curResolution.Y;
                    curImgLabel.TargetWidth = SnapshotSearchObjR.Width - 2;
                    curImgLabel.TargetHeight = SnapshotSearchObjR.Height - 2;
                }

                // range rectangle
                g.DrawRectangle(new Pen(Color.SpringGreen, 2), SnapshotSearchObjR);
                g.Dispose();
            }

            SnapshotPos.X = delta.X;
            SnapshotPos.Y = delta.Y;

            // draw snapshot
            SnapshotGraphic.DrawImage(snapshot, new Rectangle(0, 0, Snapshot.Width, Snapshot.Height), delta, GraphicsUnit.Pixel);
        }

        double max_matchDegree = 0;
        private void searchImg_test()
        {
            Stopwatch sw = new Stopwatch();
            ImgLabel.SearchMethod method;
            if (searchMethodComBox.SelectedItem == null)
                method = ImgLabel.SearchMethod.SqDiffNormed;
            else
                method = EnumHelper.GetEnumFromString<ImgLabel.SearchMethod>(searchMethodComBox.SelectedItem.ToString());

            curImgLabel.searchMethod = method;
            //Debug.WriteLine(method);

            double matchDegree;
            sw.Reset();
            sw.Start();
            var list = curImgLabel.search(out matchDegree);
            sw.Stop();

            // load the result
            reasultListBox.Items.Clear();
            if (list.Count > 0)
            {

                for (int i = 0; i < list.Count; i++)
                {
                    reasultListBox.Items.Add(list[i].X.ToString() + "," + list[i].Y.ToString());

                    Bitmap result = curImgLabel.getResultImg(i);
                    Graphics g = searchResultImg.CreateGraphics();
                    g.Clear(Color.FromArgb(240, 240, 240));
                    g.DrawImage(result, new Rectangle(0, 0, searchResultImg.Width, searchResultImg.Height), new Rectangle(0, 0, result.Width, result.Height), GraphicsUnit.Pixel);
                    g.Dispose();
                }
                max_matchDegree = Math.Max(matchDegree, max_matchDegree);

                label23.Text = $"匹配度:{matchDegree:f1}%\n" + "耗时:" + sw.ElapsedMilliseconds + "毫秒\n" + $"最大匹配度:{max_matchDegree:f1}%";
            }
            else
            {
                label23.Text = "无法找到目标";
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            string path = System.Windows.Forms.Application.StartupPath + "\\Capture\\";
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            // get cur bmp
            ss?.Dispose();
            ss = VideoCapture.GetImage();
            ss.Save(path + DateTime.Now.Ticks.ToString() + ".bmp", System.Drawing.Imaging.ImageFormat.Bmp);

            // need a 9 times of the real pic for display
            snapshot?.Dispose();
            snapshot = new Bitmap(ss.Width * 3, ss.Height * 3, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            // draw it at center
            Graphics g = Graphics.FromImage(snapshot);
            g.Clear(Color.FromArgb(240, 240, 240));
            g.DrawImage(ss, new Rectangle(ss.Width, ss.Height, ss.Width, ss.Height), new Rectangle(0, 0, ss.Width, ss.Height), GraphicsUnit.Pixel);
            g.Dispose();

            // default settings
            SnapshotPos.X = ss.Width;
            SnapshotPos.Y = ss.Height;
            snapshotScale.X = ss.Width / Snapshot.Width;
            snapshotScale.Y = ss.Height / Snapshot.Height;

            // show it
            Snapshot.Refresh();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (snapshot == null)
                return;

            if (snapshotMode == SnapshotMode.SecondRangeSelect)
            {
                if (SnapshotRangeR.Width <= 0 || SnapshotRangeR.Height <= 0)
                {
                    MessageBox.Show("搜索范围太小，请重新圈选");
                    return;
                }

                snapshotMode = SnapshotMode.NoAction;
                button2.Text = "开始圈选(红)";
                button4.Enabled = true;
            }
            else
            {
                snapshotMode = SnapshotMode.SecondRangeSelect;
                button2.Text = "确定搜索范围";
                button4.Enabled = false;
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            max_matchDegree = 0;
            targetImg.Image?.Dispose();
            targetImg.Image = curImgLabel.getSearchImg();
            if (targetImg.Image != null)
                searchImg_test();
            else
                MessageBox.Show("没有搜索目标");
        }

        private void button4_Click(object sender, EventArgs e)
        {
            if (snapshot == null)
                return;

            if (snapshotMode == SnapshotMode.ThridObjSelect)
            {
                if (SnapshotRangeR.Width <= 0 || SnapshotRangeR.Height <= 0)
                {
                    MessageBox.Show("搜索目标太小，请重新圈选");
                    return;
                }

                Rectangle range = new Rectangle(SnapshotSearchObjR.X + 1, SnapshotSearchObjR.Y + 1, SnapshotSearchObjR.Width - 2, SnapshotSearchObjR.Height - 2);

                curImgLabel.setSearchImg(snapshot.Clone(range, snapshot.PixelFormat));
                targetImg.Image = curImgLabel.getSearchImg();

                snapshotMode = SnapshotMode.NoAction;
                button4.Text = "开始圈选(绿)";
                button2.Enabled = true;
            }
            else
            {
                snapshotMode = SnapshotMode.ThridObjSelect;
                button4.Text = "确定搜索目标";
                button2.Enabled = false;
            }

        }

        private void button5_Click(object sender, EventArgs e)
        {
            ImgLabel.SearchMethod method;
            if (searchMethodComBox.SelectedItem == null)
                method = ImgLabel.SearchMethod.SqDiffNormed;
            else
                method = EnumHelper.GetEnumFromString<ImgLabel.SearchMethod>(searchMethodComBox.SelectedItem.ToString());

            curImgLabel.searchMethod = method;
            curImgLabel.matchDegree = double.Parse(textBox9.Text);

            // save the imglabel to local
            for (int index = 0; index < imgLabels.Count; index++)
            {
                // if the name exist,just overwrite it
                if (imgLabels[index].name == textBox10.Text)
                {

                    imgLabels[index].copy(curImgLabel);
                    imgLabels[index].save();
                    return;
                }
            }

            // not find, add a new one
            ImgLabel newone = new ImgLabel(()=>VideoCapture.GetImage());
            curImgLabel.name = textBox10.Text;

            newone.copy(curImgLabel);
            newone.save();

            // add to list and ui
            imgLabels.Add(newone);
            imgLableList.Items.Add(newone.name);
        }

        private void button7_Click(object sender, EventArgs e)
        {
            if (button7.Text == "动态测试")
            {
                max_matchDegree = 0;
                targetImg.Image?.Dispose();
                targetImg.Image = curImgLabel.getSearchImg();
                if (targetImg.Image != null)
                    searchImg_test();
                else
                {
                    MessageBox.Show("没有搜索目标");
                    return;
                }

                // 60 fps
                timer1.Interval = (int)(1000.0 / 60.0);

                // disable some funcs
                button1.Enabled = false;
                button2.Enabled = false;
                button3.Enabled = false;
                button4.Enabled = false;

                timer1.Start();
                button7.Text = "动态测试ing";
            }
            else
            {
                timer1.Stop();
                button7.Text = "动态测试";

                button1.Enabled = true;
                button2.Enabled = true;
                button3.Enabled = true;
                button4.Enabled = true;
            }
        }

        private void imgLableList_DoubleClick(object sender, EventArgs e)
        {
            if (imgLableList.SelectedItem != null)
            {
                // load the click item
                foreach (var item in imgLabels)
                {
                    if (item.name == imgLableList.SelectedItem)
                    {
                        //Debug.WriteLine("find" + item.name);
                        curImgLabel.copy(item);
                        curImgLabel.refresh(()=>VideoCapture.GetImage());

                        // update ui
                        textBox10.Text = curImgLabel.name;
                        searchMethodComBox.SelectedItem = curImgLabel.searchMethod.ToDescription();
                        textBox9.Text = curImgLabel.matchDegree.ToString();
                        targetImg.Image = curImgLabel.getSearchImg();
                        if (targetImg.Image == null)
                            MessageBox.Show("没有搜索目标图片");

                        SnapshotRangeR.X = curImgLabel.RangeX + curResolution.X - 2;
                        SnapshotRangeR.Y = curImgLabel.RangeY + curResolution.Y - 2;
                        SnapshotRangeR.Width = curImgLabel.RangeWidth + 3;
                        SnapshotRangeR.Height = curImgLabel.RangeHeight + 3;

                        SnapshotSearchObjR.X = curImgLabel.TargetX + curResolution.X - 1;
                        SnapshotSearchObjR.Y = curImgLabel.TargetY + curResolution.Y - 1;
                        SnapshotSearchObjR.Width = curImgLabel.TargetWidth + 2;
                        SnapshotSearchObjR.Height = curImgLabel.TargetHeight + 2;

                        snapshotMode = SnapshotMode.Refresh;
                        Snapshot.Refresh();
                    }
                }
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            searchImg_test();
        }

        private void button6_Click(object sender, EventArgs e)
        {
            string path = System.Windows.Forms.Application.StartupPath + "\\Capture\\";
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            OpenFileDialog openFileDialog1 = new OpenFileDialog();
            openFileDialog1.Title = "打开";
            openFileDialog1.RestoreDirectory = true;
            openFileDialog1.InitialDirectory = Path.GetFullPath(path);
            openFileDialog1.Filter = @"文本文件 (*.bmp)|*.bmp|所有文件 (*.*)|*.*";
            openFileDialog1.FileName = string.Empty;
            if (openFileDialog1.ShowDialog() != DialogResult.OK)
                return;
            Debug.WriteLine(openFileDialog1.FileName);

            // get new snatshot pic
            // get cur bmp
            ss?.Dispose();
            ss = new Bitmap(openFileDialog1.FileName);

            // need a 9 times of the real pic for display
            snapshot?.Dispose();
            snapshot = new Bitmap(ss.Width * 3, ss.Height * 3, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            // draw it at center
            Graphics g = Graphics.FromImage(snapshot);
            g.Clear(Color.FromArgb(240, 240, 240));
            g.DrawImage(ss, new Rectangle(ss.Width, ss.Height, ss.Width, ss.Height), new Rectangle(0, 0, ss.Width, ss.Height), GraphicsUnit.Pixel);
            g.Dispose();

            // default settings
            SnapshotPos.X = ss.Width;
            SnapshotPos.Y = ss.Height;
            snapshotScale.X = ss.Width / Snapshot.Width;
            snapshotScale.Y = ss.Height / Snapshot.Height;

            // show it
            Snapshot.Refresh();
        }

        private void button8_Click(object sender, EventArgs e)
        {
            if (button8.Text == "当前分辨率：1080P")
            {
                // 720p
                curResolution = new Point(1280, 720);
                VideoCapture.SetResolution(curResolution);
                button8.Text = "当前分辨率：720P";
            }
            else if (button8.Text == "当前分辨率：720P")
            {
                // 480p
                curResolution = new Point(640, 480);
                VideoCapture.SetResolution(curResolution);
                button8.Text = "当前分辨率：480p";
            }
            else
            {
                // 1080p
                curResolution = new Point(1920, 1080);
                VideoCapture.SetResolution(curResolution);
                button8.Text = "当前分辨率：1080P";
            }
        }

        private void timer2_Tick(object sender, EventArgs e)
        {
            //VideoSourcePlayerMonitor.Image?.Dispose();
            //Bitmap _image = VideoCapture.GetImage();
            //Bitmap img = new Bitmap(VideoSourcePlayerMonitor.Width, VideoSourcePlayerMonitor.Height, PixelFormat.Format24bppRgb);
            //using (var g = Graphics.FromImage(img))
            //    g.DrawImage(_image, new Rectangle(0, 0, VideoSourcePlayerMonitor.Width, VideoSourcePlayerMonitor.Height), new Rectangle(0, 0, _image.Width, _image.Height), GraphicsUnit.Pixel);
            //_image.Dispose();
            //VideoSourcePlayerMonitor.Image = img;

            VideoSourcePlayerMonitor.Image?.Dispose();
            VideoSourcePlayerMonitor.Image = VideoCapture.GetImage();
        }

        //uint count = 0;
        private void VideoSourcePlayerMonitor_Paint(object sender, PaintEventArgs e)
        {
            //count++;
            //Debug.WriteLine("---" + (count / (VideoCapture.runTime.ElapsedMilliseconds / 1000.0)).ToString("#.00") + " fps");
            //if (VideoCapture.runTime.ElapsedMilliseconds > 20000)
            //{
            //    VideoCapture.runTime.Restart();
            //    count = 0;
            //}

            Graphics g = e.Graphics;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            Bitmap newframe = VideoCapture.GetImage();
            g.DrawImage(newframe, new Rectangle(0, 0, VideoSourcePlayerMonitor.Width, VideoSourcePlayerMonitor.Height), new Rectangle(0, 0, curResolution.X, curResolution.Y), GraphicsUnit.Pixel);
            newframe.Dispose();
        }
    }
}
