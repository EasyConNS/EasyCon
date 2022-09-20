using EasyCon2.Helper;
using EasyCon2.Properties;
using ECDevice;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Security.Cryptography;
using LibAmiibo.Data;
using LibAmiibo.Data.Figurine;

namespace EasyCon2.Forms
{
    public partial class ControllerConfig : Form
    {
        ColorDialog colorDialog = new ColorDialog();
        internal NintendoSwitch NS;
        static readonly string AmiiboDir = Application.StartupPath + "Amiibo\\";
        List<AmiiboInfo> amiibos;
        AmiiboInfo amiibo;

        public ControllerConfig(NintendoSwitch gamepad)
        {
            NS = gamepad;
            InitializeComponent();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            byte mode = 0;

            if(radioButton1.Checked)
                mode = 3;
            if(radioButton2.Checked)
                mode = 2;
            if(radioButton3.Checked)
                mode = 1;

            if (!NS.IsConnected())
                return;
            if (NS.ChangeControllerMode(mode))
            {
                SystemSounds.Beep.Play();
                MessageBox.Show("手柄模式修改成功，请重启手柄后查看效果");
            }
            else
            {
                SystemSounds.Hand.Play();
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            byte[] color = new byte[12];
            color[0] = label2.BackColor.R;
            color[1] = label2.BackColor.G;
            color[2] = label2.BackColor.B;
            color[3] = label3.BackColor.R;
            color[4] = label3.BackColor.G;
            color[5] = label3.BackColor.B;
            color[6] = label4.BackColor.R;
            color[7] = label4.BackColor.G;
            color[8] = label4.BackColor.B;
            color[9] = label5.BackColor.R;
            color[10] = label5.BackColor.G;
            color[11] = label5.BackColor.B;

            if (!NS.IsConnected())
                return;
            if (NS.ChangeControllerColor(color))
            {
                SystemSounds.Beep.Play();
                MessageBox.Show("手柄颜色修改成功，请重启手柄后查看效果");
            }
            else
            {
                SystemSounds.Hand.Play();
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            Stream dataStream;

            if (!NS.IsConnected())
                return;
            Debug.WriteLine(AmiiboDir + comboBox2.SelectedItem);
            // first need generate amiibo bin
            if (comboBox2.SelectedIndex < amiibos.Count)
            {
                dataStream = new MemoryStream(CreateAmiibo(amiibo.head+amiibo.tail, amiibo.name));
            }
            else
            {
                dataStream = new FileStream(AmiiboDir + comboBox2.SelectedItem, FileMode.Open);
                if(dataStream.Length != 540)
                {
                    MessageBox.Show("Amiibo文件长度不正确");
                    return;
                }
            }
            var br = new BinaryReader(dataStream, Encoding.UTF8);

            var data = br.ReadBytes(540);

            if (comboBox1.SelectedIndex >= 10)
                return;

            if (NS.SaveAmiibo((byte)comboBox1.SelectedIndex, data))
            {
                SystemSounds.Beep.Play();
                MessageBox.Show("amiibo存储成功");
            }
            else
            {
                SystemSounds.Hand.Play();
            }
            br.Close();
        }

        private void comboBox2_DropDown(object sender, EventArgs e)
        {
            if (!Directory.Exists(AmiiboDir))
            {
                Directory.CreateDirectory(AmiiboDir);
                MessageBox.Show("Amiibo文件不存在，请将自定义bin文件放在Amiibo目录下");
                return;
            }
            comboBox2.Items.Clear();
            foreach (var am in amiibos)
            {
                comboBox2.Items.Add(am.name);
                //Debug.WriteLine(am.ToString());
            }

            // refresh amiibo ,add to list
            DirectoryInfo directoryInfo = new DirectoryInfo(AmiiboDir);
            FileInfo[] files = directoryInfo.GetFiles();//"*.png"
            foreach (FileInfo file in files)
            {
                if (file.Extension == ".bin")
                    comboBox2.Items.Add(file.Name);
            }
        }

        public static void ColorToHSV(Color color, out double hue, out double saturation, out double value)
        {
            int max = Math.Max(color.R, Math.Max(color.G, color.B));
            int min = Math.Min(color.R, Math.Min(color.G, color.B));

            hue = color.GetHue();
            saturation = (max == 0) ? 0 : 1d - (1d * min / max);
            value = max / 255d;
        }

        public static Color ColorFromHSV(double hue, double saturation, double value)
        {
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            double f = hue / 60 - Math.Floor(hue / 60);

            value = value * 255;
            int v = Convert.ToInt32(value);
            int p = Convert.ToInt32(value * (1 - saturation));
            int q = Convert.ToInt32(value * (1 - f * saturation));
            int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

            if (hi == 0)
                return Color.FromArgb(255, v, t, p);
            else if (hi == 1)
                return Color.FromArgb(255, q, v, p);
            else if (hi == 2)
                return Color.FromArgb(255, p, v, t);
            else if (hi == 3)
                return Color.FromArgb(255, p, q, v);
            else if (hi == 4)
                return Color.FromArgb(255, t, p, v);
            else
                return Color.FromArgb(255, v, p, q);
        }

        private void label2_Click(object sender, EventArgs e)
        {
            double[] hsv = {0,0,0};
            if (colorDialog.ShowDialog() == DialogResult.OK)
            {
                Debug.Print(colorDialog.Color.ToString());
                
                ColorToHSV(colorDialog.Color, out hsv[0], out hsv[1], out hsv[2]);
                Color rc = ColorFromHSV( (hsv[0]+180)%360,  hsv[1],  hsv[2]);
                if (rc.ToArgb() == Color.Black.ToArgb())
                    rc = Color.White;
                else if (rc.ToArgb() == Color.White.ToArgb())
                    rc = Color.Black;

                Debug.WriteLine(hsv[0].ToString()+" "+hsv[1].ToString()+" "+hsv[2].ToString());
                label2.BackColor = colorDialog.Color;
                label2.ForeColor = rc;
            }
        }

        private void label4_Click(object sender, EventArgs e)
        {
            double[] hsv = { 0, 0, 0 };
            if (colorDialog.ShowDialog() == DialogResult.OK)
            {
                Debug.Print(colorDialog.Color.ToString());

                ColorToHSV(colorDialog.Color, out hsv[0], out hsv[1], out hsv[2]);
                Color rc = ColorFromHSV((hsv[0] + 180) % 360, hsv[1], hsv[2]);
                if (rc.ToArgb() == Color.Black.ToArgb())
                    rc = Color.White;
                else if (rc.ToArgb() == Color.White.ToArgb())
                    rc = Color.Black;

                Debug.WriteLine(hsv[0].ToString() + " " + hsv[1].ToString() + " " + hsv[2].ToString());
                label4.BackColor = colorDialog.Color;
                label4.ForeColor = rc;
            }
        }

        private void label3_Click(object sender, EventArgs e)
        {
            double[] hsv = { 0, 0, 0 };
            if (colorDialog.ShowDialog() == DialogResult.OK)
            {
                Debug.Print(colorDialog.Color.ToString());

                ColorToHSV(colorDialog.Color, out hsv[0], out hsv[1], out hsv[2]);
                Color rc = ColorFromHSV((hsv[0] + 180) % 360, hsv[1], hsv[2]);
                if (rc.ToArgb() == Color.Black.ToArgb())
                    rc = Color.White;
                else if (rc.ToArgb() == Color.White.ToArgb())
                    rc = Color.Black;

                Debug.WriteLine(hsv[0].ToString() + " " + hsv[1].ToString() + " " + hsv[2].ToString());
                label3.BackColor = colorDialog.Color;
                label3.ForeColor = rc;
            }
        }

        private void label5_Click(object sender, EventArgs e)
        {
            double[] hsv = { 0, 0, 0 };
            if (colorDialog.ShowDialog() == DialogResult.OK)
            {
                Debug.Print(colorDialog.Color.ToString());

                ColorToHSV(colorDialog.Color, out hsv[0], out hsv[1], out hsv[2]);
                Color rc = ColorFromHSV((hsv[0] + 180) % 360, hsv[1], hsv[2]);
                if (rc.ToArgb() == Color.Black.ToArgb())
                    rc = Color.White;
                else if (rc.ToArgb() == Color.White.ToArgb())
                    rc = Color.Black;

                Debug.WriteLine(hsv[0].ToString() + " " + hsv[1].ToString() + " " + hsv[2].ToString());
                label5.BackColor = colorDialog.Color;
                label5.ForeColor = rc;
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            byte index = (byte)comboBox3.SelectedIndex;
            if (!NS.IsConnected())
                return;
            if (NS.ChangeAmiiboIndex(index))
            {
                SystemSounds.Beep.Play();
                MessageBox.Show("当前Amiibo切换成功");
            }
            else
            {
                SystemSounds.Hand.Play();
            }
        }

        private void ControllerConfig_Load(object sender, EventArgs e)
        {
            // load amiibo
            string str = System.Text.Encoding.UTF8.GetString(Resources.Amiibo);
            //Debug.WriteLine(str);
            amiibos = JsonSerializer.Deserialize<List<AmiiboInfo>>(str);
        }

        private void comboBox2_SelectionChangeCommitted(object sender, EventArgs e)
        {
            // show image
            if(comboBox2.SelectedIndex < amiibos.Count)
            {
                amiibo = amiibos[comboBox2.SelectedIndex];
            }
            else
            {
                amiibo = null;
                fileStream = new FileStream(AmiiboDir + comboBox2.SelectedItem, FileMode.Open);
                BinaryReader br = new BinaryReader(fileStream, Encoding.UTF8);
                string head = br[84].ToString() + br[85].ToString() + br[86].ToString() + br[87].ToString();
                string tail = br[88].ToString() + br[89].ToString() + br[90].ToString() + br[91].ToString();
                Debug.WriteLine(head);
                Debug.WriteLine(tail);
                foreach (Amiibo am in amiibos)
                {
                    if(am.head == head && am.tail == tail)
                    {
                        amiibo = am;
                        break;
                    }
                }
            }
            if (amiibo != null)
            {
                string imageName = amiibo.image.Split('/').Last();
                imageName = imageName.Replace("png", "jpg");
                Debug.WriteLine(imageName);
                pictureBox1.Image = Image.FromFile(AmiiboDir + "AmiiboImages\\" + imageName);
            }
        }
        private static byte[] CreateAmiibo(string id, string nick = "ca1e", string miiNick = "云浅雪")
        {
            var bytes = new byte[552];
            // Set BCC, Internal, Static Lock, and CC
            Array.Copy(new byte[] { 0x65, 0x48, 0x0F, 0xE0, 0xF1, 0x10, 0xFF, 0xEE }, bytes, 8);
            bytes[0x28] = 0xA5;
            // Set Dynamic Lock, and RFUI
            Array.Copy(new byte[] { 0x01, 0x00, 0x0F, 0xBD }, 0, bytes, 0x208, 4);
            // Set CFG0
            Array.Copy(new byte[] { 0x00, 0x00, 0x00, 0x04 }, 0, bytes, 0x20C, 4);
            // Set CFG1
            Array.Copy(new byte[] { 0x5F, 0x00, 0x00, 0x00 }, 0, bytes, 0x210, 4);
            // Set Keygen Salt
            RandomNumberGenerator.Create().GetBytes(new Span<byte>(bytes, 0x1E8, 0x20));
            var amiiboData = AmiiboTag.FromInternalTag(new ArraySegment<byte>(bytes));
            // into the soul
            amiiboData.Amiibo = Amiibo.FromStatueId(id);
            amiiboData.AmiiboSettings.AmiiboUserData.AmiiboNickname = nick;
            amiiboData.AmiiboSettings.AmiiboUserData.OwnerMii.MiiNickname = miiNick;
            amiiboData.RandomizeUID();
            return amiiboData.EncryptWithKeys();
        }
    }
}
