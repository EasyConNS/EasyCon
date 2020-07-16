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
        public CaptureVideo()
        {
            InitializeComponent();
        }

        public CaptureVideo(IVideoSource source)
        {
            InitializeComponent();
            VideoSourcePlayerMonitor.VideoSource = source;
        }

        public void setVideoSource(IVideoSource source)
        {
            VideoSourcePlayerMonitor.VideoSource = source;
        }

        private void VideoSourcePlayerMonitor_MouseWheel(object sender, MouseEventArgs e)
        {
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

        private void CaptureVideo_Load(object sender, EventArgs e)
        {
            X = this.Width;//获取窗体的宽度
            Y = this.Height;//获取窗体的高度
            setTag(this);//调用方法
        }

        private void CaptureVideo_Resize(object sender, EventArgs e)
        {
            //如果是第一次运行，需要把下面的if语句取消注释，否则会没反应，其以后再运行或调试的时候，就把它注释即可
            if (IsFirst) { IsFirst = false; return; }
            float newx = (this.Width) / X; //窗体宽度缩放比例
            float newy = (this.Height) / Y;//窗体高度缩放比例
            setControls(newx, newy, this);//随窗体改变控件大小
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

        private void VideoSourcePlayerMonitor_KeyDown(object sender, KeyEventArgs e)
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
                //判断是窗体还是控件，从而改进鼠标相对于窗体的位置
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

        private enum SnapshotMode
        {
            NoAction,
            FirstZoom,
            SecondRangeSelect,
            ThridObjSelect
        }

        private Graphics SnapshotGraphic;
        private Bitmap snapshot;
        private Bitmap searchObjImg;
        private Bitmap searchRangeImg;
        private Point SnapshotTranslateMDP = new Point();
        private Point SnapshotRangeMDP = new Point();
        private Point SnapshotRangeMMP = new Point();
        private bool SnapshotTranslateMove;
        private bool SnapshotRangeMove;
        private SnapshotMode snapshotMode = SnapshotMode.NoAction;
        private Point SnapshotTranslate = new Point();
        private Point SnapshotPos = new Point();

        private Rectangle SnapshotRangeR = new Rectangle();
        private Rectangle SnapshotSearchObjR = new Rectangle();

        private void button1_Click(object sender, EventArgs e)
        {
            string path = System.Windows.Forms.Application.StartupPath + "\\Capture\\";
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            snapshot = VideoSourcePlayerMonitor.GetCurrentVideoFrame();
            snapshot.Save(path + DateTime.Now.Ticks.ToString() + ".bmp", System.Drawing.Imaging.ImageFormat.Bmp);

            SnapshotGraphic = Snapshot.CreateGraphics();
            SnapshotPos.X = 0;
            SnapshotPos.Y = 0;
            snapshotMode = SnapshotMode.FirstZoom;
            SnapshotGraphic.DrawImage(snapshot, new Rectangle(0, 0, Snapshot.Width, Snapshot.Height), new Rectangle(SnapshotPos.X, SnapshotPos.Y, snapshot.Width, snapshot.Height), GraphicsUnit.Pixel);

            // save the last range
            //SnapshotRangeR.X = 0;
            //SnapshotRangeR.Y = 0;
            //SnapshotRangeR.Width = 0;
            //SnapshotRangeR.Height = 0;

            //SnapshotSearchObjR.X = 0;
            //SnapshotSearchObjR.Y = 0;
            //SnapshotSearchObjR.Width = 0;
            //SnapshotSearchObjR.Height = 0;
        }

        private void Snapshot_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                SnapshotTranslateMDP.X = e.X;
                SnapshotTranslateMDP.Y = e.Y;
                //Debug.WriteLine("donw:"+e.X.ToString()+" "+e.Y.ToString());
                SnapshotTranslateMove = true;
                if(snapshotMode == SnapshotMode.FirstZoom)
                {
                    //Debug.WriteLine("donw:" + e.X.ToString() + " " + e.Y.ToString());
                    SnapshotPos.X = (int)((float)e.X / Snapshot.Width * snapshot.Width - e.X);
                    SnapshotPos.Y = (int)((float)e.Y / Snapshot.Height * snapshot.Height- e.Y);
                    Snapshot.Refresh();
                }

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
            if (e.Button == MouseButtons.Left )
            {
                SnapshotTranslateMove = false;
                snapshotMode = SnapshotMode.SecondRangeSelect;
            } 
            else if (e.Button == MouseButtons.Right)
            {
                SnapshotRangeMove = false;
                if (snapshotMode == SnapshotMode.SecondRangeSelect)
                {
                    snapshotMode = SnapshotMode.ThridObjSelect;
                }
                else if (snapshotMode == SnapshotMode.ThridObjSelect)
                {
                    snapshotMode = SnapshotMode.SecondRangeSelect;
                }
                else
                {
                    snapshotMode = SnapshotMode.SecondRangeSelect;
                }
            }
        }

        private void Snapshot_MouseMove(object sender, MouseEventArgs e)
        {
            //Snapshot.Focus();    //鼠标在picturebox上时才有焦点，此时可以缩放

            if (e.Button == MouseButtons.Left && SnapshotTranslateMove)
            {
                Snapshot.Focus();    //鼠标在picturebox上时才有焦点，此时可以缩放
                SnapshotTranslate.X = e.X - SnapshotTranslateMDP.X;
                SnapshotTranslate.Y = e.Y - SnapshotTranslateMDP.Y;
                SnapshotTranslateMDP.X = e.X;
                SnapshotTranslateMDP.Y = e.Y;
                Snapshot.Refresh();
            }
            else if (e.Button == MouseButtons.Right && SnapshotRangeMove)
            {
                Snapshot.Focus();    //鼠标在picturebox上时才有焦点，此时可以缩放
                SnapshotRangeMMP.X = e.X;
                SnapshotRangeMMP.Y = e.Y;
                Snapshot.Refresh();
            }
        }


        private void Snapshot_Paint(object sender, PaintEventArgs e)
        {

            if (snapshot == null)
                return;

            //Debug.WriteLine("paint:"+SnapshotTranslateMDP.ToString());
            SnapshotGraphic = e.Graphics;//Snapshot.CreateGraphics();s

            if (snapshotMode == SnapshotMode.FirstZoom)
            {
                //Debug.WriteLine("paint:" + SnapshotPos.X.ToString() + " " + SnapshotPos.Y.ToString());
                snapshotMode = SnapshotMode.NoAction;
            }
            else
            {
                SnapshotPos.X -= (int)(SnapshotTranslate.X);
                SnapshotPos.Y -= (int)(SnapshotTranslate.Y);
                SnapshotTranslate.X = 0;
                SnapshotTranslate.Y = 0;
            }
            //Debug.WriteLine("wh:" + SnapshotRangeMDP.X.ToString() + " " + SnapshotRangeMDP.Y.ToString());

            // draw snapshot
            SnapshotGraphic.DrawImage(snapshot, new Rectangle(0, 0, Snapshot.Width, Snapshot.Height), new Rectangle(SnapshotPos.X, SnapshotPos.Y, (int)(Snapshot.Width), (int)(Snapshot.Height)), GraphicsUnit.Pixel);

            if(snapshotMode == SnapshotMode.SecondRangeSelect && SnapshotRangeMove)
            {
                SnapshotRangeR.X = SnapshotRangeMDP.X;
                SnapshotRangeR.Y = SnapshotRangeMDP.Y;
                SnapshotRangeR.Width = SnapshotRangeMMP.X - SnapshotRangeMDP.X;
                SnapshotRangeR.Height = SnapshotRangeMMP.Y - SnapshotRangeMDP.Y;
            }else if (snapshotMode == SnapshotMode.ThridObjSelect && SnapshotRangeMove)
            {
                SnapshotSearchObjR.X = SnapshotRangeMDP.X;
                SnapshotSearchObjR.Y = SnapshotRangeMDP.Y;
                SnapshotSearchObjR.Width = SnapshotRangeMMP.X - SnapshotRangeMDP.X;
                SnapshotSearchObjR.Height = SnapshotRangeMMP.Y - SnapshotRangeMDP.Y;
            }

            // range rectangle
            SnapshotGraphic.DrawRectangle(new Pen(Color.Red, 3), SnapshotRangeR);

            // obj rectangle
            SnapshotGraphic.DrawRectangle(new Pen(Color.SpringGreen, 2), SnapshotSearchObjR);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (snapshot == null)
                return;

            if (SnapshotRangeR.Width <= 0 || SnapshotRangeR.Height <= 0)
                return;

            string path = System.Windows.Forms.Application.StartupPath + "\\SearchRange\\";
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            Rectangle range = new Rectangle(SnapshotRangeR.X + SnapshotPos.X, SnapshotRangeR.Y + SnapshotPos.Y, SnapshotRangeR.Width, SnapshotRangeR.Height);

            searchRangeImg = snapshot.Clone(range, snapshot.PixelFormat);
            searchRangeImg.Save(path + DateTime.Now.Ticks.ToString() + "_search_range.bmp", System.Drawing.Imaging.ImageFormat.Bmp);
        }

        private void button3_Click(object sender, EventArgs e)
        {
            if (snapshot == null)
                return;

            if(searchRangeImg == null)
            {
                button2_Click(null, null);
            }

            if (searchObjImg == null)
            {
                button4_Click(null, null);
            }

            if (searchObjImg == null || searchRangeImg == null)
                return;

            DateTime cur = DateTime.Now;
            Stopwatch sw = new Stopwatch();

            sw.Reset();
            sw.Start(); //计时开始
            ImgLabel imgLabel = new ImgLabel(searchRangeImg, searchObjImg, SnapshotRangeR, ImgLabel.SearchMethod.StrictMatchRND);
            var list = imgLabel.search();
            sw.Stop();   //计时结束
            Debug.WriteLine("sf:" + sw.ElapsedMilliseconds + "毫秒");

            for (int i = 0; i < list.Count; i++)
                Debug.WriteLine($"({list[i].X},{list[i].Y})");

            if (list.Count > 0)
            {
                MessageBox.Show("找到目标图片,用时间:" + sw.ElapsedMilliseconds + "毫秒");
            }else
            {
                MessageBox.Show("找不到目标图片");
            }
            searchObjImg = null;
            searchRangeImg = null;
        }

        private void CaptureVideo_FormClosed(object sender, FormClosedEventArgs e)
        {
            VideoCapture.VideoSource?.SignalToStop();
        }

        private void button4_Click(object sender, EventArgs e)
        {
            if (snapshot == null)
                return;

            if (SnapshotSearchObjR.Width <= 0 || SnapshotSearchObjR.Height <= 0)
                return;

            string path = System.Windows.Forms.Application.StartupPath + "\\SearchObj\\";
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            Rectangle range = new Rectangle(SnapshotSearchObjR.X + SnapshotPos.X, SnapshotSearchObjR.Y + SnapshotPos.Y, SnapshotSearchObjR.Width, SnapshotSearchObjR.Height);

            searchObjImg = snapshot.Clone(range, snapshot.PixelFormat);
            searchObjImg.Save(path + DateTime.Now.Ticks.ToString() + "_search_obj.bmp", System.Drawing.Imaging.ImageFormat.Bmp);
        }
    }
}
