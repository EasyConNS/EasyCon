﻿using ECDevice;
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

        public DrawingBoard(NintendoSwitch gamepad)
        {
            NS = gamepad;
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
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
            if(image.Width != 320 && image.Height != 120)
            {
                MessageBox.Show("图片不是320*120大小，建议重新修改");
                return;
            }

            pictureBox1.Image = image;
        }

        static int Keycode(SwitchButton key)
        {
            int n = (int)key;
            int k = -1;
            while (n != 0)
            {
                n >>= 1;
                k++;
            }
            return k;
        }

        private void draw_task()
        {
            ECKey a = ECKeyUtil.Button(SwitchButton.A);
            ECKey left = ECKeyUtil.HAT(SwitchHAT.LEFT);
            ECKey right = ECKeyUtil.HAT(SwitchHAT.RIGHT);
            ECKey up = ECKeyUtil.HAT(SwitchHAT.TOP);
            ECKey down = ECKeyUtil.HAT(SwitchHAT.BOTTOM);

            Bitmap pic = new Bitmap(image);
            //Color color = b.GetPixel(e.X, e.Y);
            int wait = int.Parse(textBox1.Text);
            int duration = 60;
            //NS.Press(a,25);

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

        }

        private void button2_Click(object sender, EventArgs e)
        {
            //DrawTask = Task.Run(draw_task);
            source = new CancellationTokenSource();
            //开启一个task执行任务
            DrawTask = new Task(() =>
            {
                draw_task();
            });
            DrawTask.Start();
            button2.Enabled = false;
        }

        private void button3_Click(object sender, EventArgs e)
        {
            source.Cancel();
            Thread.Sleep(100);
            NS.Reset();
            button2.Enabled = true;
            DrawTask.Dispose();
        }
    }
}
