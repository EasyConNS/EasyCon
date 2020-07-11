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
            monitorScale = (e.Delta>0)?1.1:0.90909;

            Debug.WriteLine(monitorScale.ToString()+" "+ newSize.ToString());

            if (monitorHorOrVerZoom==1)
            {
                newSize.Height = (int)(newSize.Height * monitorScale);
            }
            else if(monitorHorOrVerZoom==2)
            {
                newSize.Width = (int)(newSize.Width * monitorScale);
            }
            else
            {
                newSize.Width = (int)(newSize.Width*monitorScale);
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
    }
}
