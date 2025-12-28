using EasyCon2.Properties;
using EasyDevice;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using LibAmiibo.Data;
using LibAmiibo.Data.Figurine;
using EasyCon2.Models;
using EasyCapture;

namespace EasyCon2.Forms
{
    public partial class ESPConfig : Form
    {
        ColorDialog colorDialog = new();
        internal NintendoSwitch NS;
        static readonly string AmiiboDir = Application.StartupPath + "Amiibo\\";
        List<AmiiboInfo> amiibos;
        Dictionary<string,List<AmiiboInfo>> amiibosDict;
        AmiiboInfo amiibo;

        public ESPConfig(NintendoSwitch gamepad)
        {
            NS = gamepad;
            InitializeComponent();
        }

        private void setMode_Click(object sender, EventArgs e)
        {
            byte mode = 0;

            if(proButton.Checked)
                mode = 3;
            if(jcrButton.Checked)
                mode = 2;
            if(jclButton.Checked)
                mode = 1;

            if (!NS.IsConnected())
            {
                MessageBox.Show("串口未连接");
                return;
            }
                
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

        private void setColor_Click(object sender, EventArgs e)
        {
            byte[] color =
            [
                bodyLabel.BackColor.R,
                bodyLabel.BackColor.G,
                bodyLabel.BackColor.B,
                buttonLabel.BackColor.R,
                buttonLabel.BackColor.G,
                buttonLabel.BackColor.B,
                gripLLabel.BackColor.R,
                gripLLabel.BackColor.G,
                gripLLabel.BackColor.B,
                gripRlabel.BackColor.R,
                gripRlabel.BackColor.G,
                gripRlabel.BackColor.B,
            ];
            if (!NS.IsConnected())
            {
                MessageBox.Show("串口未连接");
                return;
            }

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

        private void saveAmiibo_Click(object sender, EventArgs e)
        {
            Stream dataStream;

            if (!NS.IsConnected())
            {
                MessageBox.Show("串口未连接");
                return;
            }

            //Debug.WriteLine(AmiiboDir + selectAmiiboBox.SelectedItem);

            if(saveIndexBox.SelectedIndex >=20)
            {
                MessageBox.Show("请选择存储位置");
                return;
            }

            // first need generate amiibo bin
            if (selectGameBox.SelectedItem.ToString() != "自定义")
            {
                if (usernameBox.Text.Length > 20 || nickBox.Text.Length > 20)
                {
                    MessageBox.Show("用户名或者卡名过长");
                    return;
                }
                dataStream = new MemoryStream(CreateAmiibo(amiibo.head+amiibo.tail,nickBox.Text,usernameBox.Text));
            }
            else
            {
                dataStream = new FileStream(AmiiboDir + selectAmiiboBox.SelectedItem, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (dataStream.Length != 540)
                {
                    MessageBox.Show("Amiibo文件长度不正确");
                    return;
                }
            }
            var br = new BinaryReader(dataStream, Encoding.UTF8);
            var data = br.ReadBytes(540);

            // test
            //FileStream fs = new FileStream(AmiiboDir + "temp.bin", FileMode.Create);
            //BinaryWriter bw = new BinaryWriter(fs);
            //bw.Write(data);
            //bw.Close();
            //fs.Close();

            if (saveIndexBox.SelectedIndex >= 20)
                return;

            if (NS.SaveAmiibo((byte)saveIndexBox.SelectedIndex, data))
            {
                SystemSounds.Beep.Play();
                MessageBox.Show("amiibo存储成功");
            }
            else
            {
                SystemSounds.Hand.Play();
            }
            br.Close();
            dataStream.Close();
        }

        private void amiibo_DropDown(object sender, EventArgs e)
        {
            if (!Directory.Exists(AmiiboDir))
            {
                Directory.CreateDirectory(AmiiboDir);
                MessageBox.Show("Amiibo文件不存在，请将自定义bin文件放在Amiibo目录下");
                return;
            }
            //foreach (var am in amiibos)
            //{
            //    comboBox2.Items.Add(am.name);
            //    //Debug.WriteLine(am.ToString());
            //}

            // refresh amiibo ,add to list
            if(selectGameBox.SelectedItem?.ToString() == "自定义")
            {
                selectAmiiboBox.Items.Clear();
                DirectoryInfo directoryInfo = new DirectoryInfo(AmiiboDir);
                FileInfo[] files = directoryInfo.GetFiles();//"*.png"
                foreach (FileInfo file in files)
                {
                    if (file.Extension == ".bin")
                    {
                        selectAmiiboBox.Items.Add(file.Name);
                    }
                }
            }
        }

        private void Color_Changed(object sender, EventArgs e)
        {
            Label ctrl = (Label)sender;
            double[] hsv = {0,0,0};
            if (colorDialog.ShowDialog() == DialogResult.OK)
            {
                Debug.Print(colorDialog.Color.ToString());

                HSVColorExt.ColorToHSV(colorDialog.Color, out hsv[0], out hsv[1], out hsv[2]);
                Color rc = HSVColorExt.ColorFromHSV( (hsv[0]+180)%360,  hsv[1],  hsv[2]);
                if (rc.ToArgb() == Color.Black.ToArgb())
                    rc = Color.White;
                else if (rc.ToArgb() == Color.White.ToArgb())
                    rc = Color.Black;

                Debug.WriteLine(hsv[0].ToString()+" "+hsv[1].ToString()+" "+hsv[2].ToString());
                ctrl.BackColor = colorDialog.Color;
                ctrl.ForeColor = rc;
            }
        }

        private void changeAmiibo_Click(object sender, EventArgs e)
        {
            byte index = (byte)amiiboIndexNum.Value;
            if(index >= 20)
            {
                MessageBox.Show("当前序号不存在");
                return;
            }

            if (!NS.IsConnected())
            {
                MessageBox.Show("串口未连接");
                return;
            }
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
            amiibosDict = new Dictionary<string, List<AmiiboInfo>>();
            // load amiibo
            string str = System.Text.Encoding.UTF8.GetString(Resources.Amiibo);
            //Debug.WriteLine(str);
            amiibos = JsonSerializer.Deserialize<List<AmiiboInfo>>(str);
            foreach(var am in amiibos)
            {
                if(!amiibosDict.ContainsKey(am.gameSeries))
                {
                    amiibosDict[am.gameSeries] = new List<AmiiboInfo>();
                    selectGameBox.Items.Add(am.gameSeries);
                }
                amiibosDict[am.gameSeries].Add(am);
            }

            amiibosDict["自定义"] = new List<AmiiboInfo>();
            selectGameBox.Items.Add("自定义");
        }

        private void amiibo_SelectionChangeCommitted(object sender, EventArgs e)
        {
            FileStream fileStream;
            // show image
            if (selectGameBox.SelectedItem.ToString()!="自定义")
            {
                amiibo = amiibosDict[selectGameBox.SelectedItem.ToString()][selectAmiiboBox.SelectedIndex];
            }
            else
            {
                amiibo = null;
                fileStream = new FileStream(AmiiboDir + selectAmiiboBox.SelectedItem, FileMode.Open);
                BinaryReader br = new BinaryReader(fileStream, Encoding.UTF8);
                var data = br.ReadBytes(540);
                string head = data[84].ToString("X2") + data[85].ToString("X2") + data[86].ToString("X2") + data[87].ToString("X2");
                string tail = data[88].ToString("X2") + data[89].ToString("X2") + data[90].ToString("X2") + data[91].ToString("X2");
                Debug.WriteLine(head);
                Debug.WriteLine(tail);
                head = head.ToLower();
                tail = tail.ToLower();
                foreach (AmiiboInfo am in amiibos)
                {
                    if(am.head == head && am.tail == tail)
                    {
                        amiibo = am;
                        break;
                    }
                }
                fileStream.Close();
                br.Close();
            }
            if (amiibo != null)
            {
                string imageName = amiibo.image.Split('/').Last();
                imageName = imageName.Replace("png", "jpg");
                Debug.WriteLine(imageName);
                if (!File.Exists(AmiiboDir + "AmiiboImages\\" + imageName)) return;
                amiiboView.Image = Image.FromFile(AmiiboDir + "AmiiboImages\\" + imageName);
                nickBox.Text = amiibo.name.Replace(" ","");
            }
        }
        private static byte[] CreateAmiibo(string id, string nick = "cale", string owner = "EasyCon")
        {
            var bytes = new byte[552];
            Array.Copy(Resources.tmp, bytes, 532);
            // Set Keygen Salt
            RandomNumberGenerator.Create().GetBytes(new Span<byte>(bytes, 0x1E8, 0x20));
            var amiiboData = AmiiboTag.FromInternalTag(new ArraySegment<byte>(bytes));
            // into the soul
            amiiboData.Amiibo = Amiibo.FromStatueId(id);
            amiiboData.AmiiboSettings.AmiiboUserData.AmiiboNickname = nick;
            amiiboData.AmiiboSettings.AmiiboUserData.OwnerMii.MiiNickname = owner;
            amiiboData.AmiiboSettings.AmiiboUserData.OwnerMii.CalcCRC();
            amiiboData.RandomizeUID();
            return amiiboData.EncryptWithKeys();
        }

        private void game_SelectionChangeCommitted(object sender, EventArgs e)
        {
            selectAmiiboBox.Items.Clear();
            foreach(var am in amiibosDict[selectGameBox.SelectedItem.ToString()])
            {
                selectAmiiboBox.Items.Add(am.name);
            }
        }

    }
}
