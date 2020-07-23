using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Drawing;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using JetBrains.Annotations;
using System.IO;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using AForge.Video;

namespace EasyCon.Graphic
{
    public static class Cloner
    {
        public static T Clone<T>(T source)
        {
            if (ReferenceEquals(source, null))
                return default(T);

            var settings = new JsonSerializerSettings { ContractResolver = new ContractResolver() };

            return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(source, settings), settings);
        }

        class ContractResolver : DefaultContractResolver
        {
            protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
            {
                var props = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Select(p => base.CreateProperty(p, memberSerialization))
                    .Union(type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Select(f => base.CreateProperty(f, memberSerialization)))
                    .ToList();
                props.ForEach(p => { p.Writable = true; p.Readable = true; });
                return props;
            }
        }
    }

    public static class EnumHelper
    {
        /// <summary>
        /// 获取枚举类型的描述
        /// </summary>
        /// <param name="enumeration"></param>
        /// <returns></returns>
        public static string ToDescription(this Enum enumeration)
        {
            Type type = enumeration.GetType();
            MemberInfo[] memInfo = type.GetMember(enumeration.ToString());
            if (null != memInfo && memInfo.Length > 0)
            {
                object[] attrs = memInfo[0].GetCustomAttributes(typeof(DescriptionAttribute), false);
                if (null != attrs && attrs.Length > 0)
                    return ((DescriptionAttribute)attrs[0]).Description;
            }
            return enumeration.ToString();
        }

        public static T GetEnumFromString<T>(string value)
        {
            if (Enum.IsDefined(typeof(T), value))
            {
                return (T)Enum.Parse(typeof(T), value, true);
            }
            else
            {
                string[] enumNames = Enum.GetNames(typeof(T));
                foreach (string enumName in enumNames)
                {
                    object e = Enum.Parse(typeof(T), enumName);
                    if (value == ToDescription((Enum)e))
                    {
                        return (T)e;
                    }
                }
                return default(T);
            }
            throw new ArgumentException("The value '" + value
                + "' does not match a valid enum name or description.");
        }

    }

    // for ui data binding
    public class ImgLabel : INotifyPropertyChanged
    {
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
        public ImgLabel()
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
            List<SearchMethod> list = new List<SearchMethod>();
            //System.Array valueList = System.Enum.GetValues(typeof(SearchMethod));
            //foreach (var value in valueList)
            //{
            //    list.Add((SearchMethod)value);
            //}
            list.Add(SearchMethod.SqDiffNormed);
            list.Add(SearchMethod.CCorrNormed);
            list.Add(SearchMethod.CCoeffNormed);
            return list;
        }

        public List<Point> search(Bitmap sourceObj, Bitmap searchObj, Rectangle range, SearchMethod method, out double matchDegree)
        {
            sourcePic = sourceObj.Clone(new Rectangle(0, 0, sourceObj.Width, sourceObj.Height), sourceObj.PixelFormat);
            searchImg = searchObj.Clone(new Rectangle(0, 0, searchObj.Width, searchObj.Height), searchObj.PixelFormat);
            searchRange = range;
            searchMethod = method;
            result = GraphicSearch.FindPic(0, 0, searchRange.Width, searchRange.Height, sourcePic, searchImg, searchMethod, out matchDegree);
            return result;
        }

        private String ImgToBase64String(Image bmp)
        {
            String strbaser64 = String.Empty;
            var btarr = convertByte(bmp);
            strbaser64 = Convert.ToBase64String(btarr);

            return strbaser64;
        }
        private string ToBase64(Bitmap bmp)
        {
            try
            {
                MemoryStream ms = new MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                byte[] arr = new byte[ms.Length];
                ms.Position = 0;
                ms.Read(arr, 0, (int)ms.Length);
                ms.Close();
                String strbaser64 = Convert.ToBase64String(arr);
                return strbaser64;
            }
            catch (Exception ex)
            {
                MessageBox.Show("ImgToBase64String 转换失败 Exception:" + ex.Message);
                return "";
            }
        }
        private static byte[] convertByte(Image img)
        {
            MemoryStream ms = new MemoryStream();
            img.Save(ms, img.RawFormat);
            byte[] bytes = ms.ToArray();
            ms.Close();
            return bytes;
        }

        private Bitmap Base64StringToImage(string basestr)
        {
            // Convert Base64 String to byte[]
            byte[] imageBytes = Convert.FromBase64String(basestr);
            MemoryStream ms = new MemoryStream(imageBytes, 0, imageBytes.Length);
            // Convert byte[] to Image
            ms.Write(imageBytes, 0, imageBytes.Length);
            Image image = Image.FromStream(ms, true);
            return (Bitmap)image;
        }

        public void setSearchImg(Bitmap bmp)
        {
            if (bmp != null)
                searchImg = bmp.Clone(new Rectangle(0, 0, bmp.Width, bmp.Height), bmp.PixelFormat);
        }

        public Bitmap getSearchImg()
        {
            return searchImg.Clone(new Rectangle(0, 0, searchImg.Width, searchImg.Height), searchImg.PixelFormat);
        }

        public void save()
        {
            // save the imglabel to loc

            string path = System.Windows.Forms.Application.StartupPath + "\\ImgLabel\\";
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            ImgBase64 = ToBase64(searchImg);

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
            //Debug.Print(il.ImgBase64);
            //string path = System.Windows.Forms.Application.StartupPath + "\\test\\";
            searchImg = Base64StringToImage(il.ImgBase64);
            //Debug.WriteLine(searchImg.Width);
            //searchImg.Save(path + "123.bmp", System.Drawing.Imaging.ImageFormat.Bmp);
        }

        public int search()
        {
            Bitmap ss = _VideoSourcePlayerMonitor.GetCurrentVideoFrame();
            sourcePic = ss.Clone(new Rectangle(RangeX,RangeY,RangeWidth,RangeHeight), ss.PixelFormat);
            GraphicSearch.FindPic(0, 0, searchRange.Width, searchRange.Height, sourcePic, searchImg, searchMethod, out matchDegree);
            return (int)(matchDegree*100);
        }

        public void refresh(AForge.Controls.VideoSourcePlayer VideoSourcePlayerMonitor)
        {
            searchImg = Base64StringToImage(ImgBase64);
            searchRange.X = TargetX;
            searchRange.Y = TargetY;
            searchRange.Width = TargetWidth;
            searchRange.Height = TargetHeight;
            _VideoSourcePlayerMonitor = VideoSourcePlayerMonitor;
        }

        public string name;
        public SearchMethod searchMethod;
        public double matchDegree;
        public string ImgBase64;

        private AForge.Controls.VideoSourcePlayer _VideoSourcePlayerMonitor;
        private Bitmap searchImg;
        private Bitmap sourcePic;
        private Rectangle searchRange;
        private List<Point> result = new List<Point>();
    }
}
