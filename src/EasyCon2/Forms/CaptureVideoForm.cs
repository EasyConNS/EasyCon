using EasyCapture;
using EasyCon2.Helper;
using EasyCon2.Properties;
using OpenCvSharp.Extensions;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO;

namespace EasyCon2.Forms
{
    public partial class CaptureVideoForm : Form, IDisposable
    {
        private enum MonitorMode
        {
            NoBorder = 0,
            Editor = 1,
        }

        private enum SnapshotMode
        {
            NoAction,
            FirstZoom,
            SecondRangeSelect,
            ThridObjSelect,
            Refresh
        }

        static readonly string CapDir = Application.StartupPath + "\\Capture\\";
        static readonly string ImgDir = Application.StartupPath + "\\ImgLabel\\";

        private int deviceId = -1;
        private readonly OpenCVCapture cvcap = new();

        private readonly object _lock = new();
        private ILManager ilManager = new();

        private Point _curResolution = new(1920, 1080);
        public Point CurResolution
        {
            get
            {
                return _curResolution;
            }
            set
            {
                if (_curResolution.X != value.X || _curResolution.Y != value.Y)
                {
                    _curResolution = value;
                    cvcap.SetProperties(value.X, value.Y);
                }
            }
        }

        private bool isMouseDown = false;
        private Point mouseOffset;
        private double monitorScale = 1.1;
        private MonitorMode monitorMode = MonitorMode.Editor;

        private Bitmap _image;
        private static Bitmap _snapshot;
        private static Bitmap ss;
        private Point SnapshotLMDP = new();
        private Point SnapshotLMMD = new();
        private bool SnapshotLMMing;

        private Point SnapshotRangeMDP = new();
        private Point SnapshotRangeMMP = new();
        private bool SnapshotRangeMove;

        private SnapshotMode snapshotMode = SnapshotMode.NoAction;
        private Point SnapshotPos = new(0, 0);
        private Rectangle SnapshotRangeR = new(0, 0, 0, 0);
        private Rectangle SnapshotSearchObjR = new(0, 0, 0, 0);
        private PointF snapshotScale;

        public int DeviceID => deviceId;
        public bool IsOpened => cvcap.IsOpened;

        public IEnumerable<ImgLabel> LoadedLabels => ilManager.Labels.Select(il =>
        {
            il.Current.GetFrame = GetImage;
            return il.Current;
        });

        public void LoadImgLabels() => ilManager.LoadImgLabels(ImgDir);

        public CaptureVideoForm()
        {
            InitializeComponent();
        }

        public CaptureVideoForm(int devId, int typeId)
        {
            InitializeComponent();

            deviceId = devId;
            Debug.WriteLine($"采集卡Id:{deviceId}");

            if (!cvcap.Open(devId, typeId))
            {
                cvcap.Release();
                MessageBox.Show("当前采集卡已经被其他程序打开，请先关闭后再尝试");
                Close();
            }
            cvcap.SetProperties(CurResolution.X, CurResolution.Y);

            BindingData();
        }

        private void CaptureVideo_Load(object sender, EventArgs e)
        {
            Directory.CreateDirectory(CapDir);
            Directory.CreateDirectory(ImgDir);

            ilManager.LoadImgLabels(ImgDir);

            VideoMonitor.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            VideoMonitor.PaintEventHandler += MonitorPaint;
            Snapshot.PaintEventHandler += SnapshotPaint;
        }

        private void BindingData()
        {
            searchMethodComBox.DataSource = EasyCon.Core.ECCore.GetSearchMethods().Select(e => new
            {
                Value = e,
                Description = e.ToDescription()
            }).ToList();
            searchMethodComBox.ValueMember = "Value";
            searchMethodComBox.DisplayMember = "Description";

            imgLabelNametxt.DataBindings.Add("Text", ilManager, "Name");
            searchMethodComBox.DataBindings.Add("SelectedValue", ilManager, "SearchMethod");
            searchXNUD.DataBindings.Add("Value", ilManager, "RangeX");
            searchYNUD.DataBindings.Add("Value", ilManager, "RangeY");
            searchWNUD.DataBindings.Add("Value", ilManager, "RangeWidth");
            searchHNUD.DataBindings.Add("Value", ilManager, "RangeHeight");
            targetXNUD.DataBindings.Add("Value", ilManager, "TargetX");
            targetYNUD.DataBindings.Add("Value", ilManager, "TargetY");
            targetWNUD.DataBindings.Add("Value", ilManager, "TargetWidth");
            targetHNUD.DataBindings.Add("Value", ilManager, "TargetHeight");

            imgLableList.DataSource = ilManager.Labels;
            imgLableList.DisplayMember = "Name";
        }

        private Bitmap GetImage()
        {
            if (!cvcap.IsOpened)
                throw new Exception("请先连接采集卡再执行搜图");
            lock (_lock)
            {
                if (_image == null)
                    return null;

                return _image.Clone(new Rectangle(0, 0, _image.Width, _image.Height), _image.PixelFormat);
            }
        }

        #region 窗口功能
        private void MonitorPaint(object sender, PaintEventArgs e)
        {
            try
            {
                using var newImg = GetImage();
                if (newImg == null) return;
                var g = e.Graphics;
                // Maximize performance
                g.CompositingMode = CompositingMode.SourceOver;
                g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                g.CompositingQuality = CompositingQuality.HighSpeed;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.SmoothingMode = SmoothingMode.None;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;

                g.DrawImage(newImg, new Rectangle(0, 0, VideoMonitor.Width, VideoMonitor.Height), new Rectangle(0, 0, CurResolution.X, CurResolution.Y), GraphicsUnit.Pixel);
            }
            catch
            {
                Debug.WriteLine("something wrong, beacause when closing but the render is paintting");
            }
        }

        private void Snapshot_DoubleClick(object sender, EventArgs e)
        {
            openFileDialog1.InitialDirectory = Path.GetFullPath(CapDir);
            if (openFileDialog1.ShowDialog() != DialogResult.OK)
                return;
            Debug.WriteLine(openFileDialog1.FileName);

            using var capture = new Bitmap(openFileDialog1.FileName);
            showCap(capture);
        }

        private void SnapshotPaint(object sender, PaintEventArgs e)
        {
            if (_snapshot == null)
                return;

            var delta = new Rectangle()
            {
                X = SnapshotPos.X - (int)((SnapshotLMMD.X) * snapshotScale.X),
                Y = SnapshotPos.Y - (int)((SnapshotLMMD.Y) * snapshotScale.Y),
                Width = (int)(Snapshot.Width * snapshotScale.X),
                Height = (int)(Snapshot.Height * snapshotScale.Y)
            };
            SnapshotLMMD.X = 0;
            SnapshotLMMD.Y = 0;

            if (snapshotMode != SnapshotMode.NoAction)
            {
                Graphics g = Graphics.FromImage(_snapshot);
                g.Clear(Color.FromArgb(240, 240, 240));
                g.DrawImage(ss, new Rectangle(ss.Width, ss.Height, ss.Width, ss.Height), new Rectangle(0, 0, ss.Width, ss.Height), GraphicsUnit.Pixel);

                if (snapshotMode == SnapshotMode.SecondRangeSelect)
                {
                    // cal the range start pos in bitmap
                    SnapshotRangeR.X = SnapshotPos.X + (int)((SnapshotRangeMDP.X) * snapshotScale.X);
                    SnapshotRangeR.Y = SnapshotPos.Y + (int)((SnapshotRangeMDP.Y) * snapshotScale.Y);
                    SnapshotRangeR.Width = (int)((SnapshotRangeMMP.X - SnapshotRangeMDP.X) * snapshotScale.X);
                    SnapshotRangeR.Height = (int)((SnapshotRangeMMP.Y - SnapshotRangeMDP.Y) * snapshotScale.Y);

                    ilManager.RangeX = SnapshotRangeR.X + 2 - CurResolution.X;
                    ilManager.RangeY = SnapshotRangeR.Y + 2 - CurResolution.Y;
                    ilManager.RangeWidth = SnapshotRangeR.Width - 3;
                    ilManager.RangeHeight = SnapshotRangeR.Height - 3;
                }

                // range rectangle
                g.DrawRectangle(new Pen(Color.Red, 3), SnapshotRangeR);

                if (snapshotMode == SnapshotMode.ThridObjSelect)
                {
                    // cal the range start pos in bitmap
                    SnapshotSearchObjR.X = SnapshotPos.X + (int)((SnapshotRangeMDP.X) * snapshotScale.X);
                    SnapshotSearchObjR.Y = SnapshotPos.Y + (int)((SnapshotRangeMDP.Y) * snapshotScale.Y);
                    SnapshotSearchObjR.Width = (int)((SnapshotRangeMMP.X - SnapshotRangeMDP.X) * snapshotScale.X);
                    SnapshotSearchObjR.Height = (int)((SnapshotRangeMMP.Y - SnapshotRangeMDP.Y) * snapshotScale.Y);

                    ilManager.TargetX = SnapshotSearchObjR.X + 1 - CurResolution.X;
                    ilManager.TargetY = SnapshotSearchObjR.Y + 1 - CurResolution.Y;
                    ilManager.TargetWidth = SnapshotSearchObjR.Width - 2;
                    ilManager.TargetHeight = SnapshotSearchObjR.Height - 2;
                }

                // range rectangle
                g.DrawRectangle(new Pen(Color.SpringGreen, 2), SnapshotSearchObjR);
                g.Dispose();
            }

            SnapshotPos.X = delta.X;
            SnapshotPos.Y = delta.Y;

            // draw snapshot
            e.Graphics.DrawImage(_snapshot, new Rectangle(0, 0, Snapshot.Width, Snapshot.Height), delta, GraphicsUnit.Pixel);
        }

        private void VideoSourcePlayerMonitor_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (monitorMode == MonitorMode.NoBorder)
            {
                // change to editor
                this.FormBorderStyle = FormBorderStyle.Sizable;
                this.VideoMonitor.Dock = DockStyle.None;

                int formWidth = this.ClientSize.Width; // 获取窗体的宽度
                int controlWidth = VideoMonitor.Width; // 获取控件的宽度
                int xPosition = formWidth - controlWidth - 4; // 计算控件左边界的位置，使其右对齐
                VideoMonitor.Location = new Point(xPosition, VideoMonitor.Location.Y);
                this.VideoMonitor.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                monitorMode = MonitorMode.Editor;
                this.Refresh();
            }
            else if (monitorMode == MonitorMode.Editor)
            {
                // change to noborder
                this.FormBorderStyle = FormBorderStyle.None;
                this.VideoMonitor.Dock = DockStyle.Fill;
                monitorMode = MonitorMode.NoBorder;
                this.VideoMonitor.BringToFront();
            }
        }

        private void VideoSourcePlayerMonitor_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Control control = sender as Control;
                int offsetX = -e.X;
                int offsetY = -e.Y;

                if (!(control is Form))
                {
                    offsetX = offsetX - control.Left;
                    offsetY = offsetY - control.Top;
                }
                mouseOffset = new Point(offsetX, offsetY);
                isMouseDown = true;
            }
        }

        private void VideoSourcePlayerMonitor_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isMouseDown = false;
            }
        }

        private void VideoSourcePlayerMonitor_MouseMove(object sender, MouseEventArgs e)
        {
            if (isMouseDown && monitorMode == MonitorMode.NoBorder)
            {
                Point mouse = Control.MousePosition;
                mouse.Offset(mouseOffset.X, mouseOffset.Y);
                Debug.WriteLine($"point({mouseOffset.X} , {mouseOffset.Y})");
                this.Location = mouse;
            }
        }

        private void VideoSourcePlayerMonitor_MouseWheel(object sender, MouseEventArgs e)
        {
            if (monitorMode == MonitorMode.Editor)
                return;

            var newSize = new Size(this.Size.Width, this.Size.Height);
            monitorScale = (e.Delta > 0) ? 1.1 : 0.90909;

            Debug.WriteLine(monitorScale.ToString() + " " + newSize.ToString());

            newSize.Width = (int)(newSize.Width * monitorScale);
            newSize.Height = (int)(newSize.Height * monitorScale);

            Debug.WriteLine($"new size:{newSize}");
            this.Size = newSize;
        }

        private void Snapshot_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                SnapshotLMDP.X = e.X;
                SnapshotLMDP.Y = e.Y;
                SnapshotLMMing = true;
                Snapshot.Focus();
            }
            else if (e.Button == MouseButtons.Right)
            {
                SnapshotRangeMDP.X = e.X;
                SnapshotRangeMDP.Y = e.Y;
                SnapshotRangeMove = true;
                Snapshot.Focus();
            }
        }

        private void Snapshot_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                SnapshotLMMing = false;
            }
            else if (e.Button == MouseButtons.Right)
            {
                SnapshotRangeMove = false;
            }
        }

        private void Snapshot_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && SnapshotLMMing)
            {
                Snapshot.Focus();
                SnapshotLMMD.X = e.X - SnapshotLMDP.X;
                SnapshotLMMD.Y = e.Y - SnapshotLMDP.Y;
                SnapshotLMDP.X = e.X;
                SnapshotLMDP.Y = e.Y;
                Snapshot.Refresh();
            }
            else if (e.Button == MouseButtons.Right && SnapshotRangeMove)
            {
                Snapshot.Focus();
                SnapshotRangeMMP.X = e.X;
                SnapshotRangeMMP.Y = e.Y;
                Snapshot.Refresh();
            }
        }

        private void Snapshot_MouseWheel(object sender, MouseEventArgs e)
        {
            Snapshot.Focus();    //鼠标在picturebox上时才有焦点，此时可以缩放
            snapshotScale.X += (e.Delta > 0) ? -0.1f : 0.1f;
            snapshotScale.Y += (e.Delta > 0) ? -0.1f : 0.1f;

            // limit the scale
            snapshotScale.X = (float)Math.Min(Math.Max(snapshotScale.X, 0.5), 3.0);
            snapshotScale.Y = (float)Math.Min(Math.Max(snapshotScale.Y, 0.5), 3.0);

            Debug.WriteLine("bmpscale:" + snapshotScale.ToString());
            Snapshot.Refresh();
        }
        #endregion

        private void searchImg_test()
        {
            Stopwatch sw = new();
            Debug.WriteLine(ilManager.SearchMethod);

            sw.Reset();
            sw.Start();
            var list = new List<Point>();
            double matchDegree = 0;
            try
            {
                ilManager.Current.GetFrame = GetImage;
                list = ilManager.Current.Search(out matchDegree);
                sw.Stop();

                // load the result
                double max_matchDegree = 0;
                if (list.Count > 0)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        Debug.WriteLine($"{list[i].X},{list[i].Y}");

                        using var result = ilManager.Current.GetResultImg(list[i]);
                        using var g = searchResultImg.CreateGraphics();
                        g.Clear(Color.FromArgb(240, 240, 240));
                        g.DrawImage(result, new Rectangle(0, 0, searchResultImg.Width, searchResultImg.Height), new Rectangle(0, 0, result.Width, result.Height), GraphicsUnit.Pixel);
                    }
                    max_matchDegree = Math.Max(matchDegree, max_matchDegree);

                    matchRltlabel.Text = $"匹配度:{matchDegree:f1}%\n耗时:{sw.ElapsedMilliseconds}毫秒\n最大匹配度:{max_matchDegree:f1}%";
                }
                else
                {
                    matchRltlabel.Text = "无法找到目标";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                matchRltlabel.Text = ex.Message;
            }
        }

        private void showCap(Bitmap capt)
        {
            // get cur bmp
            ss?.Dispose();
            ss = capt.Clone(new Rectangle(0, 0, capt.Width, capt.Height), capt.PixelFormat);
            // need a 9 times of the real pic for display
            _snapshot?.Dispose();
            _snapshot = new Bitmap(ss.Width * 3, ss.Height * 3, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            // draw it at center
            var g = Graphics.FromImage(_snapshot);
            g.Clear(Color.FromArgb(240, 240, 240));
            g.DrawImage(ss, new Rectangle(ss.Width, ss.Height, ss.Width, ss.Height), new Rectangle(0, 0, ss.Width, ss.Height), GraphicsUnit.Pixel);
            g.Dispose();

            // default settings
            SnapshotPos.X = ss.Width;
            SnapshotPos.Y = ss.Height;
            snapshotScale.X = ss.Width / Snapshot.Width;
            snapshotScale.Y = ss.Height / Snapshot.Height;

            // show it
            Snapshot.Refresh();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            searchImg_test();
        }

        private void captureBtn_Click(object sender, EventArgs e)
        {
            using var capture = GetImage();
            capture.Save($"{CapDir}{DateTime.Now.Ticks.ToString()}.png", System.Drawing.Imaging.ImageFormat.Png);

            showCap(capture);
        }

        private void rangeBtn_Click(object sender, EventArgs e)
        {
            if (_snapshot == null)
                return;

            if (snapshotMode == SnapshotMode.SecondRangeSelect)
            {
                if (SnapshotRangeR.Width <= 0 || SnapshotRangeR.Height <= 0)
                {
                    MessageBox.Show("搜索范围太小，请重新圈选");
                    return;
                }

                snapshotMode = SnapshotMode.NoAction;
                rangeBtn.Text = "圈选范围";
                targetBtn.Enabled = true;
            }
            else
            {
                snapshotMode = SnapshotMode.SecondRangeSelect;
                rangeBtn.Text = "确定搜索范围";
                targetBtn.Enabled = false;
            }
        }

        private void searchTestBtn_Click(object sender, EventArgs e)
        {
            if (targetImg.Image != null)
                searchImg_test();
            else
                MessageBox.Show("没有搜索目标");
        }

        private void targetBtn_Click(object sender, EventArgs e)
        {
            if (_snapshot == null)
                return;

            if (snapshotMode == SnapshotMode.ThridObjSelect)
            {
                if (SnapshotSearchObjR.Width <= 0 || SnapshotSearchObjR.Height <= 0)
                {
                    MessageBox.Show("搜索目标太小，请重新圈选");
                    return;
                }

                Rectangle range = new(SnapshotSearchObjR.X + 1, SnapshotSearchObjR.Y + 1, SnapshotSearchObjR.Width - 2, SnapshotSearchObjR.Height - 2);
                var ss = _snapshot.Clone(range, _snapshot.PixelFormat);

                ilManager.Current.SetImage(ss);
                targetImg.Image = ss;

                snapshotMode = SnapshotMode.NoAction;
                targetBtn.Text = "圈选目标";
                rangeBtn.Enabled = true;
            }
            else
            {
                snapshotMode = SnapshotMode.ThridObjSelect;
                targetBtn.Text = "确定搜索目标";
                rangeBtn.Enabled = false;
            }
        }

        private void SaveTagBtn_Click(object sender, EventArgs e)
        {
            if (!ilManager.Current.Valid())
            {
                MessageBox.Show("标签数据不完善无法保存");
                return;
            }

            // save the imglabel to local
            ilManager.Current.Save(ImgDir);
            ilManager.LoadImgLabels(ImgDir);
        }

        private void DynTestBtn_Click(object sender, EventArgs e)
        {
            if (DynTestBtn.Text == "动态测试")
            {
                targetImg.Image?.Dispose();
                targetImg.Image = ilManager.Current.GetBitmap();
                if (targetImg.Image != null)
                    searchImg_test();
                else
                {
                    MessageBox.Show("没有搜索目标");
                    return;
                }

                // 60 fps
                searchTestTimer.Interval = (int)(1000.0 / 60.0);

                // disable some funcs
                captureBtn.Enabled = false;
                rangeBtn.Enabled = false;
                searchTestBtn.Enabled = false;
                targetBtn.Enabled = false;

                searchTestTimer.Start();
                DynTestBtn.Text = "动态测试ing";
            }
            else
            {
                searchTestTimer.Stop();
                DynTestBtn.Text = "动态测试";

                captureBtn.Enabled = true;
                rangeBtn.Enabled = true;
                searchTestBtn.Enabled = true;
                targetBtn.Enabled = true;
            }
        }

        private void targetImg_DoubleClick(object sender, EventArgs e)
        {
            openFileDialog1.InitialDirectory = Application.StartupPath;
            if (openFileDialog1.ShowDialog() != DialogResult.OK)
                return;

            Debug.WriteLine(openFileDialog1.FileName);

            // get new target pic
            var tap = new Bitmap(openFileDialog1.FileName);
            // set new target
            ilManager.TargetHeight = tap.Height;
            ilManager.TargetWidth = tap.Width;
            targetImg.Image = tap;
        }

        private void imgLableList_DoubleClick(object sender, EventArgs e)
        {
            if (imgLableList.SelectedIndex != -1 && imgLableList.SelectedItem != null)
            {
                var item = ilManager.Labels[imgLableList.SelectedIndex];
                ilManager.Current = item.Current;
                Debug.WriteLine($"select: {ilManager.Name}");

                // update ui
                ilManager.Name = item.Name;
                targetImg.Image = ilManager.GetBitmap();

                if (targetImg.Image == null)
                    MessageBox.Show("没有搜索目标图片");

                SnapshotRangeR.X = ilManager.RangeX + CurResolution.X - 2;
                SnapshotRangeR.Y = ilManager.RangeY + CurResolution.Y - 2;
                SnapshotRangeR.Width = ilManager.RangeWidth + 3;
                SnapshotRangeR.Height = ilManager.RangeHeight + 3;

                SnapshotSearchObjR.X = ilManager.TargetX + CurResolution.X - 1;
                SnapshotSearchObjR.Y = ilManager.TargetY + CurResolution.Y - 1;
                SnapshotSearchObjR.Width = ilManager.TargetWidth + 2;
                SnapshotSearchObjR.Height = ilManager.TargetHeight + 2;

                snapshotMode = SnapshotMode.Refresh;
                Snapshot.Refresh();
            }
        }

        private void ResolutionBtn_Click(object sender, EventArgs e)
        {
            if (ResolutionBtn.Text == "当前分辨率：1080P")
            {
                // 720p
                CurResolution = new Point(1280, 720);
                ResolutionBtn.Text = "当前分辨率：720P";
            }
            else if (ResolutionBtn.Text == "当前分辨率：720P")
            {
                // 480p
                CurResolution = new Point(640, 480);
                ResolutionBtn.Text = "当前分辨率：480P";
            }
            else
            {
                // 1080p
                CurResolution = new Point(1920, 1080);
                ResolutionBtn.Text = "当前分辨率：1080P";
            }
        }

        private void monitorVisChk_CheckedChanged(object sender, EventArgs e)
        {
            var checkBox = (CheckBox)sender;
            VideoMonitor.Visible = checkBox.Checked;
        }


        private void CaptureVideo_FormClosed(object sender, FormClosedEventArgs e)
        {
            deviceId = -1;
            cvcap.Release();
        }

        private void monitorTimer_Tick(object sender, EventArgs e)
        {
            if (Monitor.TryEnter(_lock))
            {
                try
                {
                    _image?.Dispose();
                    _image = null;
                    using var mat = cvcap.GetMatFrame();
                    if (!mat.Empty())
                    {
                        _image = BitmapConverter.ToBitmap(mat);
                    }

                    if (monitorVisChk.Checked) VideoMonitor?.Invalidate();
                }
                finally
                {
                    Monitor.Exit(_lock);
                }
            }
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            var checkBox = (CheckBox)sender;
            this.TopMost = checkBox.Checked;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            new HelpTxtDialog(Resources.capturedoc).Show();
        }
    }
}
