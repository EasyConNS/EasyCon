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

namespace EasyCon
{
    public partial class CaptureVideo : Form
    {
        bool isMouseDown = false;
        Point mouseOffset;
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

        private void CaptureVideo_MouseDown(object sender, MouseEventArgs e)
        {
            Debug.WriteLine("mouse down12");
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

        private void CaptureVideo_MouseUp(object sender, MouseEventArgs e)
        {
            Debug.WriteLine("123");
            if (e.Button == MouseButtons.Left)
            {
                isMouseDown = false;
                Debug.WriteLine("mouse up");
            }
        }

        private void CaptureVideo_MouseMove(object sender, MouseEventArgs e)
        {
            Debug.WriteLine("44");
            if (isMouseDown)
            {
                Point mouse = Control.MousePosition;
                mouse.Offset(mouseOffset.X, mouseOffset.Y);
                Debug.WriteLine("mouse move" + mouseOffset.X + " " + mouseOffset.Y);
                this.Location = mouse;
            }
        }

        private void CaptureVideo_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            this.Close();
        }

        private void CaptureVideo_MouseWheel(object sender, MouseEventArgs e)
        {
            //this.Close();
        }
    }
}
