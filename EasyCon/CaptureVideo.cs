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
                needUpdate = true;
                this.Refresh();
            }
            else if (monitorMode == MonitorMode.Editor)
            {
                // change to noborder
                this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
                this.VideoSourcePlayerMonitor.Dock = System.Windows.Forms.DockStyle.Fill;
                monitorMode = MonitorMode.NoBorder;
                this.VideoSourcePlayerMonitor.BringToFront();
                needUpdate = true;
                this.Refresh();
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

        private Graphics SnapshotGraphic;
        Bitmap bitmap;
        private void button1_Click(object sender, EventArgs e)
        {
            string path = System.Windows.Forms.Application.StartupPath + "\\Capture\\";
            //判断该路径下文件夹是否存在，不存在的情况下新建文件夹
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            bitmap = VideoSourcePlayerMonitor.GetCurrentVideoFrame();
            //VideoSourcePlayerMonitor
            bitmap.Save(path + DateTime.Now.Ticks.ToString() + ".bmp", System.Drawing.Imaging.ImageFormat.Bmp);
            // Snapshot.Image = bitmap;
            //Snapshot.SizeMode = PictureBoxSizeMode.StretchImage;

            //      this.SetStyle(ControlStyles.DoubleBuffer |
            //ControlStyles.UserPaint |
            //ControlStyles.AllPaintingInWmPaint,
            //true);
            //this.UpdateStyles();
            //this.Snapshot.set
            //Snapshot
            //bitmaps
            SnapshotGraphic = Snapshot.CreateGraphics();
            //SnapshotGraphic.dou
            bmpPos.X = 0;
            bmpPos.Y = 0;
            bmpScale.X = 1.0f;
            bmpScale.Y = 1.0f;
            SnapshotGraphic.DrawImage(bitmap, new Rectangle(0, 0, Snapshot.Width, Snapshot.Height), new Rectangle(bmpPos.X, bmpPos.Y, bitmap.Width, bitmap.Height), GraphicsUnit.Pixel);
            //bmpPos.X = Snapshot.Width / 2;
            //bmpPos.Y = Snapshot.Height / 2;
        }

        private Point mouseDownPoint = new Point();
        private Point mouseDownPoint1 = new Point();
        private Point mouseDownPoint2 = new Point();
        private bool isMove;
        private bool isMove1;
        private bool needUpdate = false;
        private void Snapshot_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                mouseDownPoint.X = e.X;   //记录鼠标左键按下时位置
                mouseDownPoint.Y = e.Y;
                //Debug.WriteLine("donw:"+e.X.ToString()+" "+e.Y.ToString());
                isMove = true;
                Snapshot.Focus();    //鼠标滚轮事件(缩放时)需要picturebox有焦点
            }
            else if (e.Button == MouseButtons.Right)
            {
                mouseDownPoint1.X = e.X;   //记录鼠标s左键按下时位置
                mouseDownPoint1.Y = e.Y;
                isMove1 = true;
                //Debug.WriteLine("donw:"+e.X.ToString()+" "+e.Y.ToString());
                Snapshot.Focus();    //鼠标滚轮事件(缩放时)需要picturebox有焦点
            }
        }

        private void Snapshot_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
            {
                isMove = false;
                isMove1 = false;
            }
        }

        private void Snapshot_MouseMove(object sender, MouseEventArgs e)
        {
            Snapshot.Focus();    //鼠标在picturebox上时才有焦点，此时可以缩放

            if (e.Button == MouseButtons.Left && isMove)
            {
                SnapshotTranslate.X = e.X - mouseDownPoint.X;
                SnapshotTranslate.Y = e.Y - mouseDownPoint.Y;
                mouseDownPoint.X = e.X;
                mouseDownPoint.Y = e.Y;
                needUpdate = true;
                Snapshot.Refresh();
            }
            else if (e.Button == MouseButtons.Right && isMove1)
            {
                mouseDownPoint2.X = e.X;
                mouseDownPoint2.Y = e.Y;
                needUpdate = true;
                Snapshot.Refresh();
            }
        }

        private void Snapshot_MouseWheel(object sender, MouseEventArgs e)
        {
            //Snapshot.Focus();    //鼠标在picturebox上时才有焦点，此时可以缩放
            ////var newSize = new Size(this.Size.Width, this.Size.Height);
            //bmpScale.X += (e.Delta > 0) ? -0.1f:0.1f;
            //bmpScale.Y += (e.Delta > 0) ? -0.1f:0.1f;

            //bmpScale.X = (float)Math.Max(bmpScale.X, 0.1);
            //bmpScale.Y = (float)Math.Max(bmpScale.Y, 0.1);

            ////Debug.WriteLine("bmpscale:" + bmpScale.ToString());
            //Snapshot.Refresh();
            //needUpdate = true;
        }

        private Point SnapshotTranslate = new Point();
        private Point bmpPos = new Point();
        private PointF bmpScale = new Point();
        private void Snapshot_Paint(object sender, PaintEventArgs e)
        {
            if (needUpdate)
            {
                if (bitmap == null)
                    return;

                //Debug.WriteLine("paint:"+mouseDownPoint.ToString());
                SnapshotGraphic = e.Graphics;//Snapshot.CreateGraphics();s
                bmpPos.X -= (int)(SnapshotTranslate.X);
                bmpPos.Y -= (int)(SnapshotTranslate.Y);
                SnapshotTranslate.X = 0;
                SnapshotTranslate.Y = 0;
                needUpdate = false;
                Debug.WriteLine("wh:" + mouseDownPoint1.X.ToString() + " " + mouseDownPoint1.Y.ToString());
                SnapshotGraphic.DrawImage(bitmap, new Rectangle(0, 0, Snapshot.Width, Snapshot.Height), new Rectangle(bmpPos.X, bmpPos.Y, (int)(Snapshot.Width), (int)(Snapshot.Height)), GraphicsUnit.Pixel);
                SnapshotGraphic.DrawRectangle(new Pen(Color.Red, 5), new Rectangle(mouseDownPoint1.X, mouseDownPoint1.Y, mouseDownPoint2.X - mouseDownPoint1.X, mouseDownPoint2.Y - mouseDownPoint1.Y));
                //SnapshotGraphic.DrawImage(bitmap, new Rectangle(0, 0, Snapshot.Width, Snapshot.Height), new Rectangle(bmpPos.X , bmpPos.Y, (int)(bitmap.Width*bmpScale.X), (int)(bitmap.Height * bmpScale.Y)), GraphicsUnit.Pixel);
            }
        }

        private Bitmap searchPic;
        private void button2_Click(object sender, EventArgs e)
        {
            if (bitmap == null)
                return;

            if ((mouseDownPoint2.X - mouseDownPoint1.X) <= 0 || (mouseDownPoint2.Y - mouseDownPoint1.Y)<=0)
                return;

            Rectangle searchRange;
            string path = System.Windows.Forms.Application.StartupPath + "\\SearchPic\\";
            //判断该路径下文件夹是否存在，不存在的情况下新建文件夹
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            //Debug.WriteLine("x:" + (bmpPos.X + mouseDownPoint1.X).ToString());
            //Debug.WriteLine("y:" + (bmpPos.Y + mouseDownPoint1.Y).ToString());
            searchRange = new Rectangle(bmpPos.X + mouseDownPoint1.X, bmpPos.Y + mouseDownPoint1.Y, mouseDownPoint2.X - mouseDownPoint1.X, mouseDownPoint2.Y - mouseDownPoint1.Y);
            searchPic = bitmap.Clone(searchRange, bitmap.PixelFormat);
            searchPic.Save(path + DateTime.Now.Ticks.ToString() + "_search.bmp", System.Drawing.Imaging.ImageFormat.Bmp);
        }

        private void button3_Click(object sender, EventArgs e)
        {
            if (bitmap == null || searchPic == null)
                return;

            DateTime cur = DateTime.Now;
            var list = VideoCapture.SearchImage(0, 0, bitmap.Width, bitmap.Height, searchPic, 0.9, VideoCapture.LineSampler(3), VideoCapture.DefaultSampler);
            DateTime end = DateTime.Now;
            for (int i = 0; i < list.Count; i++)
                Debug.WriteLine($"({list[i].X},{list[i].Y})");

            if(list.Count>0)
            {
                MessageBox.Show("找到目标图片,用时间:"+(end-cur).ToString());
            }
        }
    }
}
