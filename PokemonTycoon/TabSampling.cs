using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PokemonTycoon
{
    public partial class Form1 : Form
    {
        class TabSampling : TabModule
        {
            Form1 form;

            const int GraphicSearchResultdMaximum = 50;

            Point _cursorLoc;
            public Bitmap SampleImage { get; private set; }

            public TabSampling(Form1 form)
            {
                this.form = form;
            }

            public override void Activate()
            {
                form.pictureBoxGraphic.BackgroundImage = ImageRes.PictureBoxBackground;
                LoadImageList();
            }

            void PictureBoxShow(Image image)
            {
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
                return $"Monitor.Match({x}, {y}, Color.FromArgb({color.R}, {color.G}, {color.B}), DefaultColorCap)";
            }

            string GenerateCode(object x, object y, string imagename)
            {
                return $"Monitor.Match({x}, {y}, ImageRes.Get(\"{imagename}\"), DefaultImageCap)";
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
                Bitmap image = GetImage(Shadow.X, Shadow.Y, Shadow.W, Shadow.H);
                var shadow = Shadow.Save(id, v, image);

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
                form.textBoxGraphicSearchResult.Text = $"剪影保存完毕({shadow.Name})";
                form.textBoxGraphicCode.Text = string.Empty;
            }

            public void LoadImageList()
            {
                form.comboBoxGraphicFilename.Items.Clear();
                foreach (var fi in ImageRes.GetImages())
                    form.comboBoxGraphicFilename.Items.Add(fi.Name.Substring(0, fi.Name.Length - fi.Extension.Length));
            }

            public Color GetColor(int x, int y)
            {
                var color = Monitor.GetColor(x, y);
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
                var img = Monitor.GetImage(x, y, w, h);
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
                var list = Monitor.SearchColor(x, y, w, h, color, cap);

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
                var scn = Screen.AllScreens[Monitor.ScreenIndex];
                return SearchColor(scn.Bounds.X, scn.Bounds.Y, scn.Bounds.Width, scn.Bounds.Height, Color.FromArgb(r, g, b), cap);
            }

            public int SearchImage(int x, int y, int w, int h, Bitmap image, double cap = 1)
            {
                var list = Monitor.SearchImage(x, y, w, h, image, cap);

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
                var scn = Screen.AllScreens[Monitor.ScreenIndex];
                return SearchImage(scn.Bounds.X, scn.Bounds.Y, scn.Bounds.Width, scn.Bounds.Height, image, cap);
            }

            public int SearchImage(double cap = 1)
            {
                return SearchImage(SampleImage, cap);
            }

            public bool SamplePointDown()
            {
                return form.checkBoxGraphicSampling.Checked;
            }

            public bool SamplePointUp()
            {
                if (!form.checkBoxGraphicSampling.Checked)
                    return false;
                GetColor(Cursor.Position);
                return true;
            }

            public bool SampleImageDown()
            {
                if (!form.checkBoxGraphicSampling.Checked)
                    return false;
                _cursorLoc = Cursor.Position;
                return true;
            }

            public bool SampleImageUp()
            {
                if (!form.checkBoxGraphicSampling.Checked)
                    return false;
                GetImage(_cursorLoc, Cursor.Position);
                return true;
            }
        }
    }
}
