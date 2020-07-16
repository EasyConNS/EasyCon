using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Drawing;

namespace EasyCon.Graphic
{

    public class ImgLabel
    {
        public enum SearchMethod
        {
            SqDiff=0,
            SqDiffNormed=1,
            CCorr=2,
            CCorrNormed=3,
            CCoeff=4,
            CCoeffNormed =5,
            StrictMatch = 6,
            StrictMatchRND = 7,
            OpacityDiff = 8,
            SimilarMatch = 9,
        }

        public ImgLabel(Bitmap sourceObj, Bitmap searchObj, Rectangle range, SearchMethod method)
        {
            sourcePic = sourceObj;
            searchImg = searchObj;
            searchRange = range;
            searchMethod = method;
        }

        public ImgLabel(Bitmap searchObj, Rectangle range)
        {
            searchImg = searchObj;
            searchRange = range;
        }

        public void SetRange(Rectangle range)
        {
            searchRange = range;
        }

        public void SetBmpSource(Bitmap sourceObj)
        {
            sourcePic = sourceObj;
        }

        public List<Point> search()
        {
            result = GraphicSearch.FindPic(searchRange.X, searchRange.Y, searchRange.Width, searchRange.Height, sourcePic, searchImg, searchMethod);
            return result;
        }

        private Bitmap sourcePic;
        private Bitmap searchImg;
        private SearchMethod searchMethod;
        private Rectangle searchRange;
        private List<Point> result = new List<Point>();
    }
}
