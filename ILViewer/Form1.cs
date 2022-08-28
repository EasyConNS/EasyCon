using System;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace ILViewer
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void openBtn_Click(object sender, EventArgs e)
        {
            if(!File.Exists(ILpathBox.Text))
            {
                MessageBox.Show("找不到文件");
                return;
            }
            var by = File.ReadAllBytes(ILpathBox.Text);
            var readOnlySpan = new ReadOnlySpan<byte>(by);
            ImgLabel? deIL = JsonSerializer.Deserialize<ImgLabel>(readOnlySpan);

            if (deIL != null)
            {
                LoadIL(deIL);
            }
        }

        private void LoadIL(ImgLabel il)
        {
            nameBox.Text = il.name;
            targetPicBox.Image = il.GetBitmap();
            //
            targRangX.Text = il.TargetX.ToString();
            targRangY.Text = il.TargetY.ToString();
            targRangW.Text = il.TargetWidth.ToString();
            targRangH.Text = il.TargetHeight.ToString();
            //
            searchRangX.Text = il.RangeX.ToString();
            searchRangY.Text = il.RangeY.ToString();
            searchRangW.Text = il.RangeWidth.ToString();
            searchRangH.Text = il.RangeHeight.ToString();
            //
            matchDegreeBox.Text = il.matchDegree.ToString();
        }

        private void Form1_DragDrop(object sender, DragEventArgs e)
        {
            try
            {
                var path = (string[])e.Data.GetData(DataFormats.FileDrop, false);
                if (Path.GetExtension(path[0]) != ".IL")
                {
                    return;
                }
                ILpathBox.Text = path[0];
            }
            catch
            {
                MessageBox.Show("打开失败了，原因未知", "打开脚本");
            }
        }

        private void Form1_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.All;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }
    }
}
