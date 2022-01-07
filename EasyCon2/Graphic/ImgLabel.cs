using EasyCon2.Capture;
using EasyCon2.Global;
using JetBrains.Annotations;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace EasyCon2.Graphic
{
    // for ui data binding
    public class ImgLabel : INotifyPropertyChanged
    {
        public string name;
        public SearchMethod searchMethod;
        public string ImgBase64;
        public Rectangle searchRange;

        private Bitmap searchImg;
        private Bitmap sourcePic;
        private Bitmap resultImg;
        private List<Point> result = new List<Point>();
        //======================================
        // Actual implementation
        //======================================
        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }

        private int _RangeX = 0;
        public int RangeX
        {
            get { return _RangeX; }
            set
            {
                // do not trigger change event if values are the same
                if (Equals(value, _RangeX)) return;
                _RangeX = value < 0 ? 0 : value;

                //===================
                // Usage in the Source
                //===================
                OnPropertyChanged();
            }
        }

        private int _RangeY = 0;
        public int RangeY
        {
            get { return _RangeY; }
            set
            {
                // do not trigger change event if values are the same
                if (Equals(value, _RangeY)) return;
                _RangeY = value < 0 ? 0 : value;

                //===================
                // Usage in the Source
                //===================
                OnPropertyChanged();
            }
        }

        private int _RangeWidth = 0;
        public int RangeWidth
        {
            get { return _RangeWidth; }
            set
            {
                // do not trigger change event if values are the same
                if (Equals(value, _RangeWidth)) return;
                _RangeWidth = value < 0 ? 0 : value;

                //===================
                // Usage in the Source
                //===================
                OnPropertyChanged();
            }
        }

        private int _RangeHeight = 0;
        public int RangeHeight
        {
            get { return _RangeHeight; }
            set
            {
                // do not trigger change event if values are the same
                if (Equals(value, _RangeHeight)) return;
                _RangeHeight = value < 0 ? 0 : value;

                //===================
                // Usage in the Source
                //===================
                OnPropertyChanged();
            }
        }

        private int _TargetX = 0;
        public int TargetX
        {
            get { return _TargetX; }
            set
            {
                // do not trigger change event if values are the same
                if (Equals(value, _TargetX)) return;
                _TargetX = value < 0 ? 0 : value;

                //===================
                // Usage in the Source
                //===================
                OnPropertyChanged();
            }
        }

        private int _TargetY = 0;
        public int TargetY
        {
            get { return _TargetY; }
            set
            {
                // do not trigger change event if values are the same
                if (Equals(value, _TargetY)) return;
                _TargetY = value < 0 ? 0 : value;

                //===================
                // Usage in the Source
                //===================
                OnPropertyChanged();
            }
        }

        private int _TargetWidth = 0;
        public int TargetWidth
        {
            get { return _TargetWidth; }
            set
            {
                // do not trigger change event if values are the same
                if (Equals(value, _TargetWidth)) return;
                _TargetWidth = value < 0 ? 0 : value;

                //===================
                // Usage in the Source
                //===================
                OnPropertyChanged();
            }
        }

        private int _TargetHeight = 0;
        public int TargetHeight
        {
            get { return _TargetHeight; }
            set
            {
                // do not trigger change event if values are the same
                if (Equals(value, _TargetHeight)) return;
                _TargetHeight = value < 0 ? 0 : value;

                //===================
                // Usage in the Source
                //===================
                OnPropertyChanged();
            }
        }

        private double _matchDegree = 95.0;
        public double matchDegree
        {
            get { return _matchDegree; }
            set
            {
                // do not trigger change event if values are the same
                if (Equals(value, _matchDegree)) return;
                _matchDegree = value < 0 ? 0 : value;

                //===================
                // Usage in the Source
                //===================
                OnPropertyChanged();
            }
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

        public delegate Bitmap GetNewFrame();

        [NonSerialized]
        public GetNewFrame getNewFrame;
        public ImgLabel()
        {
            sourcePic = null;
            searchImg = null;
            searchRange = new Rectangle();
            searchMethod = SearchMethod.SqDiffNormed;
        }

        public ImgLabel(GetNewFrame getNewFrame)
        {
            sourcePic = null;
            searchImg = null;
            searchRange = new Rectangle();
            searchMethod = SearchMethod.SqDiffNormed;
        }

        public ImgLabel(Bitmap sourceObj, Bitmap searchObj, Rectangle range, SearchMethod method)
        {
            sourcePic = sourceObj.Clone(new Rectangle(0, 0, sourceObj.Width, sourceObj.Height), sourceObj.PixelFormat);
            searchImg = searchObj.Clone(new Rectangle(0, 0, searchObj.Width, searchObj.Height), searchObj.PixelFormat);
            searchRange = range;
            searchMethod = method;
        }

        public static List<SearchMethod> GetAllSearchMethod()
        {
            var list = new List<SearchMethod>();
            list.Add(SearchMethod.SqDiffNormed);
            list.Add(SearchMethod.CCorrNormed);
            list.Add(SearchMethod.CCoeffNormed);
            return list;
        }

        public List<Point> Search(out double md)
        {
            if (searchImg.Width > RangeWidth || searchImg.Height > RangeHeight)
                throw new Exception("搜索图片大于搜索范围");

            sourcePic?.Dispose();
            Bitmap ss = getNewFrame();
            sourcePic = ss.Clone(new Rectangle(RangeX, RangeY, RangeWidth, RangeHeight), ss.PixelFormat);
            ss.Dispose();

            result = GraphicSearch.FindPic(0, 0, searchRange.Width, searchRange.Height, sourcePic, searchImg, searchMethod, out md);
            md *= 100;

            // update the search pic
            if (md >= _matchDegree)
            {
                searchImg?.Dispose();
                Debug.WriteLine("update img");
                searchImg = sourcePic.Clone(new Rectangle(result[0].X, result[0].Y, TargetWidth, TargetHeight), sourcePic.PixelFormat);
            }

            return result;
        }

        public int Search()
        {
            if (searchImg.Width > RangeWidth || searchImg.Height > RangeHeight)
                throw new Exception("搜索图片大于搜索范围");

            double md = 0;
            sourcePic?.Dispose();
            Bitmap ss = getNewFrame();
            sourcePic = ss.Clone(new Rectangle(RangeX, RangeY, RangeWidth, RangeHeight), ss.PixelFormat);
            ss.Dispose();

            result = GraphicSearch.FindPic(0, 0, searchRange.Width, searchRange.Height, sourcePic, searchImg, searchMethod, out md);
            md *= 100;

            // update the search pic
            if (md >= _matchDegree)
            {
                Debug.WriteLine("update img");
                searchImg?.Dispose();
                searchImg = sourcePic.Clone(new Rectangle(result[0].X, result[0].Y, TargetWidth, TargetHeight), sourcePic.PixelFormat);
            }

            return (int)md;
        }

        public Bitmap getResultImg(int index)
        {
            if (index < result.Count())
            {
                resultImg?.Dispose();
                resultImg = sourcePic.Clone(new Rectangle(result[index].X, result[index].Y, searchImg.Width, searchImg.Height), sourcePic.PixelFormat);
                return resultImg;
            }

            return null;
        }

        public void setSearchImg(Bitmap bmp)
        {
            if (bmp != null)
            {
                searchImg?.Dispose();
                searchImg = bmp;
                // update cur search range
                searchRange.X = TargetX;
                searchRange.Y = TargetY;
                searchRange.Width = TargetWidth;
                searchRange.Height = TargetHeight;
            }
        }

        public Bitmap getSearchImg()
        {
            if (searchImg != null)
                return searchImg.Clone(new Rectangle(0, 0, searchImg.Width, searchImg.Height), searchImg.PixelFormat);
            else
                return null;
        }

        public void Save()
        {
            // save the imglabel to loc
            string path = Application.StartupPath + "\\ImgLabel\\";
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            ImgBase64 = this.ImageToBase64(searchImg);

            File.WriteAllText(path + name + ".IL", JsonConvert.SerializeObject(this));
        }

        public void copy(ImgLabel il)
        {
            name = il.name;
            RangeX = il.RangeX;
            RangeY = il.RangeY;
            RangeWidth = il.RangeWidth;
            RangeHeight = il.RangeHeight;
            TargetX = il.TargetX;
            TargetY = il.TargetY;
            TargetWidth = il.TargetWidth;
            TargetHeight = il.TargetHeight;
            searchMethod = il.searchMethod;
            matchDegree = il.matchDegree;
            ImgBase64 = il.ImgBase64;

            searchRange.X = TargetX;
            searchRange.Y = TargetY;
            searchRange.Width = TargetWidth;
            searchRange.Height = TargetHeight;
            searchImg = il.getSearchImg();//Base64StringToImage(il.ImgBase64);
            getNewFrame = il.getNewFrame;
        }

        public void Refresh(GetNewFrame getnew)
        {
            searchImg = this.Base64StringToImage(ImgBase64);
            searchRange.X = TargetX;
            searchRange.Y = TargetY;
            searchRange.Width = TargetWidth;
            searchRange.Height = TargetHeight;
            getNewFrame = getnew;
        }

        public void SetSource(GetNewFrame getnew)
        {
            getNewFrame = getnew;
        }
    }
}
