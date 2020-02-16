using AForge.Video;
using PokemonTycoon.Graphic;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PokemonTycoon
{
    public partial class MainForm : Form
    {
        internal class TabSampling : TabModule
        {
            MainForm form;

            const int GraphicSearchResultdMaximum = 50;

            Point _cursorLoc;
            public Bitmap SampleImage { get; private set; }

            public TabSampling(MainForm form)
            {
                this.form = form;
            }

            public override void Activate()
            {
                form.pictureBoxGraphic.BackgroundImage = ImageRes.PictureBoxBackground;
                LoadImageList();
                LoadCapturedShadows();
            }

            void PictureBoxShow(Image image)
            {
                if (image == null)
                    return;
                form.pictureBoxGraphic.Image = image;
                if (image.Width > form.pictureBoxGraphic.ClientSize.Width || image.Height > form.pictureBoxGraphic.ClientSize.Height)
                    form.pictureBoxGraphic.SizeMode = PictureBoxSizeMode.Zoom;
                else
                    form.pictureBoxGraphic.SizeMode = PictureBoxSizeMode.CenterImage;
            }

            void PictureBoxShow(Color color)
            {
                var img = new Bitmap(form.pictureBoxGraphic.ClientSize.Width, form.pictureBoxGraphic.ClientSize.Height);
                using (var g = Graphics.FromImage(img))
                    g.Clear(color);
                form.pictureBoxGraphic.Image = img;
                form.pictureBoxGraphic.SizeMode = PictureBoxSizeMode.Normal;
            }

            string GenerateCode(object x, object y, Color color)
            {
                return $"VideoCapture.Match({x}, {y}, Color.FromArgb({color.R}, {color.G}, {color.B}), DefaultColorCap)";
            }

            string GenerateCode(object x, object y, string imagename)
            {
                return $"VideoCapture.Match({x}, {y}, ImageRes.Get(\"{imagename}\"), DefaultImageCap, VideoCapture.LineSampler(3))";
            }

            public Bitmap LoadImage(Bitmap image)
            {
                SampleImage = image;
                PictureBoxShow(image);
                return image;
            }

            public Bitmap LoadImage(string name)
            {
                return LoadImage(ImageRes.Get(name));
            }

            public void SaveImage()
            {
                var name = form.comboBoxGraphicFilename.Text;
                SampleImage.Save(ImageRes.GetImagePath(name));
                LoadImageList();
                form.textBoxGraphicCode.Text = GenerateCode(form.textBoxGraphicX.Text, form.textBoxGraphicY.Text, name);
            }

            public void SaveShadow()
            {
                var str = form.comboBoxGraphicFilename.Text;
                var m = Regex.Match(str, @"^(\d+)([A-Za-z]?)$");
                if (!m.Success)
                {
                    MessageBox.Show("名称格式错误！");
                    return;
                }
                int id = int.Parse(m.Groups[1].Value);
                char v = m.Groups[2].Value.Length > 0 ? m.Groups[2].Value[0] : '\0';
                Shadow shadow;
                if (form.comboBoxGraphicCapturedShadows.SelectedItem != null)
                    shadow = new Shadow(id, v, (form.comboBoxGraphicCapturedShadows.SelectedItem as CapturedShadow).Image);
                else
                    shadow = new Shadow(id, v, GetImage(Shadow.X, Shadow.Y, Shadow.W, Shadow.H));
                bool overwrite = false;
                if (Shadow.NameExist(shadow))
                {
                    var dr = MessageBox.Show("剪影已存在，是否覆盖？", "", MessageBoxButtons.YesNoCancel);
                    if (dr == DialogResult.Yes)
                        overwrite = true;
                    else if(dr == DialogResult.Cancel)
                        return;
                }
                shadow = Shadow.Save(id, v, shadow, overwrite);

                SampleImage = shadow.Image;

                // update controls
                form.checkBoxGraphicSampling.Checked = false;
                PictureBoxShow(shadow.Image);
                form.textBoxGraphicX.Text = Shadow.X.ToString();
                form.textBoxGraphicY.Text = Shadow.Y.ToString();
                form.textBoxGraphicWidth.Text = Shadow.W.ToString();
                form.textBoxGraphicHeight.Text = Shadow.H.ToString();
                form.textBoxGraphicColorR.Text = string.Empty;
                form.textBoxGraphicColorG.Text = string.Empty;
                form.textBoxGraphicColorB.Text = string.Empty;
                form.textBoxGraphicWebColor.Text = string.Empty;
                form.textBoxGraphicSearchResult.Text = $"剪影({shadow.ToString()})";
                form.textBoxGraphicCode.Text = string.Empty;
                LoadCapturedShadows();
            }

            public void LoadImageList()
            {
                form.comboBoxGraphicFilename.Items.Clear();
                foreach (var fi in ImageRes.GetImages())
                    form.comboBoxGraphicFilename.Items.Add(fi.Name.Substring(0, fi.Name.Length - fi.Extension.Length));
            }

            public Color GetColor(int x, int y)
            {
                var color = VideoCapture.GetColor(x, y);
                SampleImage = null;

                // update controls
                form.checkBoxGraphicSampling.Checked = false;
                PictureBoxShow(color);
                form.textBoxGraphicX.Text = x.ToString();
                form.textBoxGraphicY.Text = y.ToString();
                form.textBoxGraphicWidth.Text = string.Empty;
                form.textBoxGraphicHeight.Text = string.Empty;
                form.textBoxGraphicColorR.Text = color.R.ToString();
                form.textBoxGraphicColorG.Text = color.G.ToString();
                form.textBoxGraphicColorB.Text = color.B.ToString();
                form.textBoxGraphicWebColor.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                form.textBoxGraphicCode.Text = GenerateCode(x, y, color);

                return color;
            }

            public Color GetColor(Point loc)
            {
                return GetColor(loc.X, loc.Y);
            }

            public Bitmap GetImage(int x, int y, int w, int h)
            {
                var img = VideoCapture.GetImage(x, y, w, h);
                SampleImage = img;

                // update controls
                form.checkBoxGraphicSampling.Checked = false;
                PictureBoxShow(img);
                form.textBoxGraphicX.Text = x.ToString();
                form.textBoxGraphicY.Text = y.ToString();
                form.textBoxGraphicWidth.Text = w.ToString();
                form.textBoxGraphicHeight.Text = h.ToString();
                form.textBoxGraphicColorR.Text = string.Empty;
                form.textBoxGraphicColorG.Text = string.Empty;
                form.textBoxGraphicColorB.Text = string.Empty;
                form.textBoxGraphicWebColor.Text = string.Empty;
                form.textBoxGraphicCode.Text = GenerateCode(x, y, form.comboBoxGraphicFilename.Text);

                return img;
            }

            public Bitmap GetImage(Point loc1, Point loc2)
            {
                return GetImage(
                    Math.Min(loc1.X, loc2.X),
                    Math.Min(loc1.Y, loc2.Y),
                    Math.Abs(loc1.X - loc2.X),
                    Math.Abs(loc1.Y - loc2.Y));
            }

            public int SearchColor(int x, int y, int w, int h, Color color, double cap = 1)
            {
                var list = VideoCapture.SearchColor(x, y, w, h, color, cap);

                // update controls
                PictureBoxShow(color);
                form.textBoxGraphicX.Text = x.ToString();
                form.textBoxGraphicY.Text = y.ToString();
                form.textBoxGraphicWidth.Text = w.ToString();
                form.textBoxGraphicHeight.Text = h.ToString();
                form.textBoxGraphicColorR.Text = color.R.ToString();
                form.textBoxGraphicColorG.Text = color.G.ToString();
                form.textBoxGraphicColorB.Text = color.B.ToString();
                form.textBoxGraphicWebColor.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                StringBuilder builder = new StringBuilder();
                builder.AppendLine($"共{list.Count}个结果");
                for (int i = 0; i < list.Count && i < GraphicSearchResultdMaximum; i++)
                    builder.AppendLine($"({list[i].X},{list[i].Y})");
                if (list.Count > GraphicSearchResultdMaximum)
                    builder.AppendLine($"...");
                form.textBoxGraphicSearchResult.Text = builder.ToString();
                form.textBoxGraphicCode.Text = string.Join(Environment.NewLine,
                    list.Select(loc => GenerateCode(loc.X, loc.Y, color)));

                return list.Count;
            }

            public int SearchColor(int r, int g, int b, double cap = 1)
            {
                return SearchColor(0, 0, 1920, 1080, Color.FromArgb(r, g, b), cap);
            }

            public int SearchImage(int x, int y, int w, int h, Bitmap image, double cap = 1)
            {
                var list = VideoCapture.SearchImage(x, y, w, h, image, cap, VideoCapture.LineSampler(3), VideoCapture.DefaultSampler);

                // update controls
                StringBuilder builder = new StringBuilder();
                builder.AppendLine($"共{list.Count}个结果");
                for (int i = 0; i < list.Count && i < GraphicSearchResultdMaximum; i++)
                    builder.AppendLine($"({list[i].X},{list[i].Y})");
                if (list.Count > GraphicSearchResultdMaximum)
                    builder.AppendLine($"...");
                form.textBoxGraphicSearchResult.Text = builder.ToString();
                form.textBoxGraphicCode.Text = string.Join(Environment.NewLine,
                    list.Select(loc => GenerateCode(loc.X, loc.Y, form.comboBoxGraphicFilename.Text)));

                return list.Count;
            }

            public int SearchImage(Bitmap image, double cap = 1)
            {
                return SearchImage(0, 0, 1920, 1080, image, cap);
            }

            public int SearchImage(double cap = 1)
            {
                return SearchImage(SampleImage, cap);
            }

            Point GetCursorLoc()
            {
                var p = Cursor.Position;
                var p0 = Screen.FromPoint(p).Bounds.Location;
                return new Point(p.X - p0.X, p.Y - p0.Y);
            }

            public void SamplePoint()
            {
                GetColor(GetCursorLoc());
                SystemSounds.Beep.Play();
            }

            public void SampleImageDown()
            {
                _cursorLoc = GetCursorLoc();
            }

            public void SampleImageUp()
            {
                var p0 = _cursorLoc;
                var p1 = GetCursorLoc();
                if (p0.X == p1.X || p0.Y == p1.Y)
                    GetColor(p1);
                else
                    GetImage(p0, p1);
                SystemSounds.Beep.Play();
            }

            public void SetRect(int x, int y, int w, int h, bool get)
            {
                form.textBoxGraphicX.Text = x.ToString();
                form.textBoxGraphicY.Text = y.ToString();
                form.textBoxGraphicWidth.Text = w.ToString();
                form.textBoxGraphicHeight.Text = h.ToString();
                if (get)
                    GetImage(x, y, w, h);
            }

            class CapturedShadow
            {
                public string Name;
                public Bitmap Image;

                public override string ToString()
                {
                    return Name;
                }
            }

            public void LoadCapturedShadows()
            {
                var pairs = Shadow.GetCapturedShadows();
                var shadows = new List<CapturedShadow>();
                for (int i = 0; i < pairs.Length; i++)
                {
                    var shadow = new CapturedShadow();
                    shadow.Name = $"{i + 1}_{pairs[i].Key}";
                    shadow.Image = pairs[i].Value;
                    shadows.Add(shadow);
                }
                form.comboBoxGraphicCapturedShadows.Items.Clear();
                form.comboBoxGraphicCapturedShadows.Items.AddRange(shadows.ToArray());
            }

            public void SelectCapturedShadows()
            {
                LoadImage((form.comboBoxGraphicCapturedShadows.SelectedItem as CapturedShadow).Image);
            }
        }
    }
}
