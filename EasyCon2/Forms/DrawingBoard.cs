using ECDevice;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EasyCon2.Forms
{
    public partial class DrawingBoard : Form
    {
        Image image;
        internal NintendoSwitch NS;
        Task DrawTask;
        CancellationTokenSource source;
        Point Pos;
        List<List<Line>> Rows;

        ECKey a = ECKeyUtil.Button(SwitchButton.A);
        ECKey left = ECKeyUtil.HAT(SwitchHAT.LEFT);
        ECKey right = ECKeyUtil.HAT(SwitchHAT.RIGHT);
        ECKey up = ECKeyUtil.HAT(SwitchHAT.TOP);
        ECKey down = ECKeyUtil.HAT(SwitchHAT.BOTTOM);
        int duration = 50;
        int wait = 100;

        private void MoveTo(int x, int y)
        {
            int x_start = Pos.X;
            int x_end = x;
            int y_start = Pos.Y;
            int y_end = y;
            // move x
            if (Pos.X >x)
            {
                x_start = x;
                x_end = Pos.X;
            }

            for (int i = x_start; i < x_end; i++)
            {
                if (Pos.X < x)
                {
                    NS.Press(down, duration);
                    Thread.Sleep(wait);
                    NS.Up(down);
                }
                else
                {
                    NS.Press(up, duration);
                    Thread.Sleep(wait);
                    NS.Up(up);
                }
            }
            Thread.Sleep(wait);
            //if (Pos.X < x)
            //{
            //    NS.Down(down);
            //}
            //else if(Pos.X > x)
            //{
            //    NS.Down(up);
            //}
            //Thread.Sleep(29 * (x_end - x_start));

            //if (Pos.X < x)
            //{
            //    NS.Up(down);
            //    NS.Up(down);
            //    NS.Up(down);
            //}
            //else if (Pos.X > x)
            //{
            //    NS.Up(up);
            //    NS.Up(up);
            //    NS.Up(up);
            //}
            //Thread.Sleep(100);

            if (Pos.Y > y)
            {
                y_start = y;
                y_end = Pos.Y;
            }

            for (int i = y_start; i < y_end; i++)
            {
                if (Pos.Y < y)
                {
                    NS.Press(right, duration);
                    Thread.Sleep(wait);
                    NS.Up(right);
                }
                else
                {
                    NS.Press(left, duration);
                    Thread.Sleep(wait);
                    NS.Up(left);
                }
            }
            Thread.Sleep(wait);
            //// move y
            //if (Pos.Y < y)
            //{
            //    NS.Down(right);

            //}
            //else if (Pos.Y > y)
            //{
            //    NS.Down(left);
            //}
            //Thread.Sleep(29 * (y_end - y_start));

            //if (Pos.Y < y)
            //{
            //    NS.Up(right);
            //    NS.Up(right);
            //    NS.Up(right);
            //}
            //else if (Pos.Y > y)
            //{
            //    NS.Up(left);
            //    NS.Up(left);
            //    NS.Up(left);
            //}
            //Thread.Sleep(100);

            Pos.X = x;
            Pos.Y = y;
        }

        public DrawingBoard(NintendoSwitch gamepad)
        {
            NS = gamepad;
            InitializeComponent();
        }

        private void draw_task_opt()
        {
            Pos = new Point(0, 0);
            Rows = new List<List<Forms.Line>>();
            Bitmap pic = new Bitmap(image);
            wait = int.Parse(delayBox.Text);
            duration = int.Parse(durationBox.Text);

            int col = int.Parse(widthBox.Text);
            int row = int.Parse(heightBox.Text);
            bool reverse = reverseCheck.Checked;

            // search line
            for (int i = 0; i < row; i++)
            {
                bool line_find = false;
                List<Line> Lines = new List<Line>();
                for (int j = 0; j < col; j++)
                {
                    Color color = pic.GetPixel(j, i);
                    if(reverse)
                    {
                        if (color.R + color.G + color.B > 255 * 3 / 2 && line_find == false)
                        {
                            Line line = new Line();
                            line.start.X = i;
                            line.start.Y = j;
                            line.end.X = i;
                            Lines.Add(line);
                            line_find = true;
                        }
                        else if (color.R + color.G + color.B < 255 * 3 / 2 && line_find == true)
                        {
                            Lines.Last().end.Y = j - 1;
                            line_find = false;
                        }
                    }
                    else
                    {
                        if (color.R + color.G + color.B < 255 * 3 / 2 && line_find == false)
                        {
                            Line line = new Line();
                            line.start.X = i;
                            line.start.Y = j;
                            line.end.X = i;
                            Lines.Add(line);
                            line_find = true;
                        }
                        else if (color.R + color.G + color.B > 255 * 3 / 2 && line_find == true)
                        {
                            Lines.Last().end.Y = j - 1;
                            line_find = false;
                        }
                    }
                }

                if (Lines.Count > 0)
                {
                    if (Lines.Last().end.Y == 0)
                    {
                        Lines.Last().end.Y = col - 1;
                        line_find = false;
                    }
                    Rows.Add(Lines);
                }
                    
            }

            foreach (List<Line> Lines in Rows)
            {
                // search nearest line
                if (Math.Abs(Pos.Y-Lines[0].start.Y) > Math.Abs(Pos.Y - Lines.Last().end.Y))
                    Lines.Reverse();

                // draw line
                foreach (Line line in Lines)
                {
                    if (source.IsCancellationRequested)
                        return;
                    if (Math.Abs(Pos.Y - line.start.Y) < Math.Abs(Pos.Y - line.end.Y))
                        MoveTo(line.start.X, line.start.Y);
                    else
                        MoveTo(line.start.X, line.end.Y);
                    NS.Down(a);
                    Thread.Sleep(wait);
                    if (Math.Abs(Pos.Y - line.start.Y) < Math.Abs(Pos.Y - line.end.Y))
                        MoveTo(line.end.X, line.end.Y);
                    else
                        MoveTo(line.end.X, line.start.Y);
                    NS.Reset();
                    Thread.Sleep(wait);
                    //Thread.Sleep(wait);
                }
            }
            NS.Reset();
            Thread.Sleep(wait);
            startButton.Enabled = true;
        }

        // deprecated
        private void draw_task()
        {
            Bitmap pic = new Bitmap(image);
            wait = int.Parse(delayBox.Text);
            duration = int.Parse(durationBox.Text);

            int col = 320;//30 for test
            int row = 120;

            for (int i = 0; i < row; i++)
            {
                int direction = i % 2;
                for (int j = 0; j < col; j++)
                {
                    if (source.IsCancellationRequested)
                        return;
                        //# and convert 255 vals to 0 to match logic in Joystick.c and invertColormap option
                        if (direction == 0)
                    {
                        Color color = pic.GetPixel(j, i);
                        if (color.R + color.G + color.B < 255 * 3 / 2)
                        {
                            NS.Press(a, duration);
                            Thread.Sleep(wait);
                            NS.Reset();
                        }
                        NS.Press(right, duration);
                        Thread.Sleep(wait);
                        NS.Reset();
                    }

                    if (direction == 1)
                    {
                        Color color = pic.GetPixel(col - 1 - j, i);
                        if (color.R + color.G + color.B < 255 * 3 / 2)
                        {
                            NS.Press(a, duration);
                            Thread.Sleep(wait);
                            NS.Reset();
                        }
                        NS.Press(left, duration);
                        Thread.Sleep(wait);
                        NS.Reset();
                    }
                }
                NS.Press(down, duration);
                Thread.Sleep(wait);
            }
            NS.Reset();
            startButton.Enabled = true;
        }

        private void loadPicButton_Click(object sender, EventArgs e)
        {
            string filepath;
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Files|*.png;*.jpg";
            //openFileDialog.InitialDirectory = AppDomain.CurrentDomain.BaseDirectory;           
            openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                // 取得文件路径及文件名
                filepath = openFileDialog.FileName;
            }
            else
            {
                return;
            }
            image = Image.FromFile(filepath);
            if (image.Width != 320 && image.Height != 120)
            {
                MessageBox.Show("图片不是320*120大小，建议重新修改");
                return;
            }

            pictureBox1.Image = image;
        }

        private void startButton_Click(object sender, EventArgs e)
        {
            source = new CancellationTokenSource();
            //开启一个task执行任务
            DrawTask = new Task(() =>
            {
                draw_task_opt();
                //draw_task();
            });
            DrawTask.Start();
            startButton.Enabled = false;
        }

        private void stopButton_Click(object sender, EventArgs e)
        {
            source.Cancel();
            Thread.Sleep(1000);
            NS.Reset();
            startButton.Enabled = true;
            //DrawTask.Dispose();
        }

        private void evaluateButton_Click(object sender, EventArgs e)
        {
            Pos = new Point(0, 0);
            Rows = new List<List<Forms.Line>>();
            Bitmap pic = new Bitmap(image);
            wait = int.Parse(delayBox.Text);
            duration = int.Parse(durationBox.Text);

            int col = int.Parse(widthBox.Text);
            int row = int.Parse(heightBox.Text);
            bool reverse = reverseCheck.Checked;

            int point_num = 0;

            // search line
            for (int i = 0; i < row; i++)
            {
                bool line_find = false;
                List<Line> Lines = new List<Line>();
                for (int j = 0; j < col; j++)
                {
                    Color color = pic.GetPixel(j, i);
                    if (reverse)
                    {
                        if (color.R + color.G + color.B > 255 * 3 / 2 && line_find == false)
                        {
                            Line line = new Line();
                            line.start.X = i;
                            line.start.Y = j;
                            line.end.X = i;
                            Lines.Add(line);
                            line_find = true;
                        }
                        else if (color.R + color.G + color.B < 255 * 3 / 2 && line_find == true)
                        {
                            Lines.Last().end.Y = j - 1;
                            line_find = false;
                        }
                    }
                    else
                    {
                        if (color.R + color.G + color.B < 255 * 3 / 2 && line_find == false)
                        {
                            Line line = new Line();
                            line.start.X = i;
                            line.start.Y = j;
                            line.end.X = i;
                            Lines.Add(line);
                            line_find = true;
                        }
                        else if (color.R + color.G + color.B > 255 * 3 / 2 && line_find == true)
                        {
                            Lines.Last().end.Y = j - 1;
                            line_find = false;
                        }
                    }
                }

                if (Lines.Count > 0)
                {
                    if (Lines.Last().end.Y == 0)
                    {
                        Lines.Last().end.Y = col - 1;
                        line_find = false;
                    }
                    Rows.Add(Lines);
                }
            }

            foreach (List<Line> Lines in Rows)
            {
                // search nearest line
                if (Math.Abs(Pos.Y - Lines[0].start.Y) > Math.Abs(Pos.Y - Lines.Last().end.Y))
                    Lines.Reverse();

                // draw line
                foreach (Line line in Lines)
                {
                    if (Math.Abs(Pos.Y - line.start.Y) < Math.Abs(Pos.Y - line.end.Y))
                    {
                        point_num += Math.Abs(Pos.Y - line.start.Y);
                        Pos.Y = line.start.Y;
                        Pos.X = line.start.X;
                    }
                    else
                    {
                        point_num += Math.Abs(Pos.Y - line.end.Y);
                        Pos.Y = line.end.Y;
                        Pos.X = line.end.X;
                    }

                    if (Math.Abs(Pos.Y - line.start.Y) < Math.Abs(Pos.Y - line.end.Y))
                    {
                        point_num += Math.Abs(Pos.Y - line.end.Y);
                        Pos.Y = line.end.Y;
                        Pos.X = line.end.X;
                    }
                    else
                    {
                        point_num += Math.Abs(Pos.Y - line.start.Y);
                        Pos.Y = line.start.Y;
                        Pos.X = line.start.X;
                    }
                }
            }
            evaluateLabel.Text = "耗时：" + (point_num * wait / 1000.0 / 60.0).ToString() + "mins";
        }
    }
    public class Line
    {
        public Point start;
        public Point end;
    }
}
