using EasyCapture;
using EasyCon2.Graphic;
using EasyCon2.Helper;
using EasyCon2.Properties;
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

        private static readonly List<ImgLabel> imgLabels = [];
        private readonly OpenCVCapture cvcap = new();
        private Point _curResolution = new(1920, 1080);

        private readonly object _lock = new();
        private Bitmap _image;

        public bool IsConnected => cvcap.IsOpened;

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
        private int monitorHorOrVerZoom = 0;
        private MonitorMode monitorMode = MonitorMode.Editor;

        private static Bitmap snapshot;
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

        private ImgLabel curImgLabel = new();
        private int deviceId = -1;

        public ICollection<ImgLabel> LoadedLabels => imgLabels;
        public int DeviceID => deviceId;
        public int LoadedLabelCount => imgLabels.Count;

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
        }

        private void CaptureVideo_Load(object sender, EventArgs e)
        {
            CaptureVideoHelp.Text = Resources.capturedoc;
            Directory.CreateDirectory(CapDir);

            foreach (var method in ImgLabelExt.GetAllSearchMethod())
            {
                searchMethodComBox.Items.Add(method.ToDescription());
            }

            // data binding
            searchXNUD.DataBindings.Add("Value", curImgLabel, "RangeX");
            searchYNUD.DataBindings.Add("Value", curImgLabel, "RangeY");
            searchWNUD.DataBindings.Add("Value", curImgLabel, "RangeWidth");
            searchHNUD.DataBindings.Add("Value", curImgLabel, "RangeHeight");
            targetXNUD.DataBindings.Add("Value", curImgLabel, "TargetX");
            targetYNUD.DataBindings.Add("Value", curImgLabel, "TargetY");
            targetWNUD.DataBindings.Add("Value", curImgLabel, "TargetWidth");
            targetHNUD.DataBindings.Add("Value", curImgLabel, "TargetHeight");
            lowestMatch.DataBindings.Add("Text", curImgLabel, "matchDegree");

            // load the imglabel
            curImgLabel.SetSource(() => GetImage());

            LoadImgLabels();
            UpdateImgListBox();

            VideoMonitor.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            VideoMonitor.PaintEventHandler += new PaintEventHandler(MonitorPaint);
            Snapshot.PaintEventHandler += new PaintEventHandler(SnapshotPaint);
        }

        public void LoadImgLabels()
        {
            Directory.CreateDirectory(ImgDir);
            Debug.WriteLine("load labels");

            imgLabels.Clear();

            foreach (var il in ImgLabelExt.LoadIL(ImgDir))
            {
                il.Refresh(() => GetImage());
                imgLabels.Add(il);
            }
        }

        private Bitmap GetImage()
        {
            if (!IsConnected)
                throw new Exception("请先连接采集卡再执行搜图");
            lock (_lock)
            {
                if (_image == null)
                    return null;

                return _image.Clone(new Rectangle(0, 0, _image.Width, _image.Height), _image.PixelFormat);
            }
        }

        private void UpdateImgListBox()
        {
            imgLableList.BeginUpdate();
            imgLableList.Items.Clear();
            imgLableList.Items.AddRange([.. imgLabels.Select(i => i.name)]);
            imgLableList.EndUpdate();
        }

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
                // DrawImage() is toooooo SLOW, use DirectX instead PLZ!
                var drawImg = newImg.Clone(new Rectangle(0, 0, newImg.Width, newImg.Height), newImg.PixelFormat);
                g.DrawImage(drawImg, new Rectangle(0, 0, VideoMonitor.Width, VideoMonitor.Height), new Rectangle(0, 0, CurResolution.X, CurResolution.Y), GraphicsUnit.Pixel);
            }
            catch
            {
                Debug.WriteLine("something wrong, beacause when closing but the render is paintting");
            }
        }

        private void SnapshotPaint(object sender, PaintEventArgs e)
        {
            if (snapshot == null)
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
                Graphics g = Graphics.FromImage(snapshot);
                g.Clear(Color.FromArgb(240, 240, 240));
                g.DrawImage(ss, new Rectangle(ss.Width, ss.Height, ss.Width, ss.Height), new Rectangle(0, 0, ss.Width, ss.Height), GraphicsUnit.Pixel);

                if (snapshotMode == SnapshotMode.SecondRangeSelect)
                {
                    // cal the range start pos in bitmap
                    SnapshotRangeR.X = SnapshotPos.X + (int)((SnapshotRangeMDP.X) * snapshotScale.X);
                    SnapshotRangeR.Y = SnapshotPos.Y + (int)((SnapshotRangeMDP.Y) * snapshotScale.Y);
                    SnapshotRangeR.Width = (int)((SnapshotRangeMMP.X - SnapshotRangeMDP.X) * snapshotScale.X);
                    SnapshotRangeR.Height = (int)((SnapshotRangeMMP.Y - SnapshotRangeMDP.Y) * snapshotScale.Y);

                    curImgLabel.RangeX = SnapshotRangeR.X + 2 - CurResolution.X;
                    curImgLabel.RangeY = SnapshotRangeR.Y + 2 - CurResolution.Y;
                    curImgLabel.RangeWidth = SnapshotRangeR.Width - 3;
                    curImgLabel.RangeHeight = SnapshotRangeR.Height - 3;
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

                    curImgLabel.TargetX = SnapshotSearchObjR.X + 1 - CurResolution.X;
                    curImgLabel.TargetY = SnapshotSearchObjR.Y + 1 - CurResolution.Y;
                    curImgLabel.TargetWidth = SnapshotSearchObjR.Width - 2;
                    curImgLabel.TargetHeight = SnapshotSearchObjR.Height - 2;
                }

                // range rectangle
                g.DrawRectangle(new Pen(Color.SpringGreen, 2), SnapshotSearchObjR);
                g.Dispose();
            }

            SnapshotPos.X = delta.X;
            SnapshotPos.Y = delta.Y;

            // draw snapshot
            e.Graphics.DrawImage(snapshot, new Rectangle(0, 0, Snapshot.Width, Snapshot.Height), delta, GraphicsUnit.Pixel);
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

        private void VideoSourcePlayerMonitor_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
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
                Debug.WriteLine($"point({mouseOffset.X} , { mouseOffset.Y})");
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

            Debug.WriteLine($"new size:{newSize}");
            monitorHorOrVerZoom = 0;
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

        double max_matchDegree = 0;

        private SearchMethod getSelectedMethod()
        {
            if (searchMethodComBox.SelectedItem == null)
                return SearchMethod.SqDiffNormed;
            else
                return EnumHelper.GetEnumFromString<SearchMethod>(searchMethodComBox.SelectedItem.ToString());
        }
        private void searchImg_test()
        {
            Stopwatch sw = new();
            curImgLabel.searchMethod = getSelectedMethod();
            //Debug.WriteLine(method);

            sw.Reset();
            sw.Start();
            var list = curImgLabel.Search(out double matchDegree);
            sw.Stop();

            // load the result
            reasultListBox.Items.Clear();
            if (list.Count > 0)
            {

                for (int i = 0; i < list.Count; i++)
                {
                    reasultListBox.Items.Add(list[i].X.ToString() + "," + list[i].Y.ToString());

                    var result = curImgLabel.getResultImg(i);
                    var g = searchResultImg.CreateGraphics();
                    g.Clear(Color.FromArgb(240, 240, 240));
                    g.DrawImage(result, new Rectangle(0, 0, searchResultImg.Width, searchResultImg.Height), new Rectangle(0, 0, result.Width, result.Height), GraphicsUnit.Pixel);
                    g.Dispose();
                }
                max_matchDegree = Math.Max(matchDegree, max_matchDegree);

                matchRltlabel.Text = $"匹配度:{matchDegree:f1}%\n" + "耗时:" + sw.ElapsedMilliseconds + "毫秒\n" + $"最大匹配度:{max_matchDegree:f1}%";
            }
            else
            {
                matchRltlabel.Text = "无法找到目标";
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            searchImg_test();
        }

        private void captureBtn_Click(object sender, EventArgs e)
        {
            // get cur bmp
            ss?.Dispose();
            ss = GetImage();
            ss.Save(CapDir + DateTime.Now.Ticks.ToString() + ".bmp", System.Drawing.Imaging.ImageFormat.Bmp);

            // need a 9 times of the real pic for display
            snapshot?.Dispose();
            snapshot = new Bitmap(ss.Width * 3, ss.Height * 3, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            // draw it at center
            var g = Graphics.FromImage(snapshot);
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

        private void rangeBtn_Click(object sender, EventArgs e)
        {
            if (snapshot == null)
                return;

            if (snapshotMode == SnapshotMode.SecondRangeSelect)
            {
                if (SnapshotRangeR.Width <= 0 || SnapshotRangeR.Height <= 0)
                {
                    MessageBox.Show("搜索范围太小，请重新圈选");
                    return;
                }

                snapshotMode = SnapshotMode.NoAction;
                rangeBtn.Text = "开始圈选(红)";
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
            max_matchDegree = 0;
            targetImg.Image?.Dispose();
            targetImg.Image = curImgLabel.getSearchImg();
            if (targetImg.Image != null)
                searchImg_test();
            else
                MessageBox.Show("没有搜索目标");
        }

        private void targetBtn_Click(object sender, EventArgs e)
        {
            if (snapshot == null)
                return;

            if (snapshotMode == SnapshotMode.ThridObjSelect)
            {
                if (SnapshotSearchObjR.Width <= 0 || SnapshotSearchObjR.Height <= 0)
                {
                    MessageBox.Show("搜索目标太小，请重新圈选");
                    return;
                }

                Rectangle range = new(SnapshotSearchObjR.X + 1, SnapshotSearchObjR.Y + 1, SnapshotSearchObjR.Width - 2, SnapshotSearchObjR.Height - 2);

                curImgLabel.setSearchImg(snapshot.Clone(range, snapshot.PixelFormat));
                targetImg.Image = curImgLabel.getSearchImg();

                snapshotMode = SnapshotMode.NoAction;
                targetBtn.Text = "开始圈选(绿)";
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
            if (imgLabelNametxt.Text == "")
            {
                MessageBox.Show("搜图标签为空无法保存");
                return;
            }

            curImgLabel.searchMethod = getSelectedMethod();
            curImgLabel.matchDegree = double.Parse(lowestMatch.Text);

            // save the imglabel to local
            for (int index = 0; index < imgLabels.Count; index++)
            {
                // if the name exist,just overwrite it
                if (imgLabels[index].name == imgLabelNametxt.Text)
                {
                    imgLabels[index].Copy(curImgLabel);
                    imgLabels[index].Save();
                    return;
                }
            }

            // not find, add a new one
            ImgLabel newone = new(() => GetImage());
            curImgLabel.name = imgLabelNametxt.Text;

            newone.Copy(curImgLabel);
            newone.Save();

            // add to list and ui
            imgLabels.Add(newone);
            UpdateImgListBox();
        }

        private void openCapBtn_Click(object sender, EventArgs e)
        {
            openFileDialog1.Title = "打开";
            openFileDialog1.RestoreDirectory = true;
            openFileDialog1.InitialDirectory = Path.GetFullPath(CapDir);
            openFileDialog1.Filter = @"文本文件 (*.bmp)|*.bmp|所有文件 (*.*)|*.*";
            openFileDialog1.FileName = string.Empty;
            if (openFileDialog1.ShowDialog() != DialogResult.OK)
                return;
            Debug.WriteLine(openFileDialog1.FileName);

            // get new snatshot pic
            // get cur bmp
            ss?.Dispose();
            ss = new Bitmap(openFileDialog1.FileName);

            // need a 9 times of the real pic for display
            snapshot?.Dispose();
            snapshot = new Bitmap(ss.Width * 3, ss.Height * 3, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            // draw it at center
            Graphics g = Graphics.FromImage(snapshot);
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

        private void DynTestBtn_Click(object sender, EventArgs e)
        {
            if (DynTestBtn.Text == "动态测试")
            {
                max_matchDegree = 0;
                targetImg.Image?.Dispose();
                targetImg.Image = curImgLabel.getSearchImg();
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
            openFileDialog1.Title = "打开";
            openFileDialog1.RestoreDirectory = true;
            openFileDialog1.InitialDirectory = Application.StartupPath;
            openFileDialog1.Filter = @"图片文件(*.jpg,*.gif,*.bmp,*.png)|*.jpg;*.gif;*.bmp;*.png";
            openFileDialog1.FileName = string.Empty;
            if (openFileDialog1.ShowDialog() != DialogResult.OK)
                return;

            Debug.WriteLine(openFileDialog1.FileName);

            // get new target pic
            var tap = new Bitmap(openFileDialog1.FileName);
            // set new target
            curImgLabel.setSearchImg(tap);
            curImgLabel.TargetHeight = tap.Height;
            curImgLabel.TargetWidth = tap.Width;
            targetImg.Image = curImgLabel.getSearchImg();
            tap?.Dispose();
        }

        private void imgLableList_DoubleClick(object sender, EventArgs e)
        {
            if (imgLableList.SelectedItem != null && imgLableList.SelectedItem.ToString() != "")
            {
                var items = imgLabels.Where(i => i.name == imgLableList.SelectedItem.ToString());

                if (items.Count() == 1)
                {
                    var item = items.First();
                    //Debug.WriteLine("find" + item.name);
                    curImgLabel.Copy(item);
                    curImgLabel.Refresh(() => GetImage());

                    // update ui
                    imgLabelNametxt.Text = curImgLabel.name;
                    searchMethodComBox.SelectedItem = curImgLabel.searchMethod.ToDescription();
                    lowestMatch.Text = curImgLabel.matchDegree.ToString();
                    targetImg.Image = curImgLabel.getSearchImg();
                    if (targetImg.Image == null)
                        MessageBox.Show("没有搜索目标图片");

                    SnapshotRangeR.X = curImgLabel.RangeX + CurResolution.X - 2;
                    SnapshotRangeR.Y = curImgLabel.RangeY + CurResolution.Y - 2;
                    SnapshotRangeR.Width = curImgLabel.RangeWidth + 3;
                    SnapshotRangeR.Height = curImgLabel.RangeHeight + 3;

                    SnapshotSearchObjR.X = curImgLabel.TargetX + CurResolution.X - 1;
                    SnapshotSearchObjR.Y = curImgLabel.TargetY + CurResolution.Y - 1;
                    SnapshotSearchObjR.Width = curImgLabel.TargetWidth + 2;
                    SnapshotSearchObjR.Height = curImgLabel.TargetHeight + 2;

                    snapshotMode = SnapshotMode.Refresh;
                    Snapshot.Refresh();
                }
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
                    _image = cvcap.GetFrame();

                    if(monitorVisChk.Checked) VideoMonitor?.Invalidate();
                }
                finally
                {
                    Monitor.Exit(_lock);
                }
            }
        }
    }
}
