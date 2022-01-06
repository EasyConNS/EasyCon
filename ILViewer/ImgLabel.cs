using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;

namespace ILViewer
{
    public class ImgLabel
    {
        public string name { get; set; }
        public int searchMethod { get; set; }
        public string ImgBase64 { get; set; }
        public string searchRange { get; set; }

        public int RangeX { get; set; }
        public int RangeY { get; set; }
        public int RangeWidth { get; set; }
        public int RangeHeight { get; set; }

        public int TargetX { get; set; }
        public int TargetY { get; set; }
        public int TargetWidth { get; set; }
        public int TargetHeight { get; set; }

        public double matchDegree { get; set; }

        public Bitmap GetBitmap()
        {
            return this.Base64StringToImage(ImgBase64);
        }

        public enum SearchMethod
        {
            [Description("平方差匹配")]
            SqDiff = 0,
            [Description("标准差匹配")]
            SqDiffNormed = 1,
            [Description("相关匹配")]
            CCorr = 2,
            [Description("标准相关匹配")]
            CCorrNormed = 3,
            [Description("相关系数匹配")]
            CCoeff = 4,
            [Description("标准相关系数匹配")]
            CCoeffNormed = 5,
            [Description("严格匹配")]
            StrictMatch = 6,
            [Description("随机严格匹配")]
            StrictMatchRND = 7,
            [Description("透明度匹配")]
            OpacityDiff = 8,
            [Description("相似匹配")]
            SimilarMatch = 9,
        }
    }

    static class Ext
    {
        public static Bitmap Base64StringToImage(this ImgLabel self, string basestr)
        {
            byte[] imageBytes = Convert.FromBase64String(basestr);
            var ms = new MemoryStream(imageBytes, 0, imageBytes.Length);
            ms.Write(imageBytes, 0, imageBytes.Length);
            Image image = Image.FromStream(ms, true);
            return (Bitmap)image;
        }
    }
}
