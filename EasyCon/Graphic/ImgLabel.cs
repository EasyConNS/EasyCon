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

namespace EasyCon.Graphic
{
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
    public class ImgLabel
    {
        public enum SearchMethod
        {
            [Description("平方差匹配")]
            SqDiff=0,
            [Description("标准差匹配")]
            SqDiffNormed =1,
            [Description("相关匹配")]
            CCorr =2,
            [Description("标准相关匹配")]
            CCorrNormed =3,
            [Description("相关系数匹配")]
            CCoeff =4,
            [Description("标准相关系数匹配")]
            CCoeffNormed =5,
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
            searchMethod = SearchMethod.StrictMatchRND;
        }

        public ImgLabel(Bitmap sourceObj, Bitmap searchObj, Rectangle range, SearchMethod method)
        {
            sourcePic = sourceObj.Clone(new Rectangle(0, 0, sourceObj.Width, sourceObj.Height), sourceObj.PixelFormat);
            searchImg = searchObj.Clone(new Rectangle(0, 0, searchObj.Width, searchObj.Height), searchObj.PixelFormat);
            searchRange = range;
            searchMethod = method;
        }

        public ImgLabel(Bitmap searchObj, Rectangle range)
        {
            searchImg = searchObj.Clone(new Rectangle(0, 0, searchObj.Width, searchObj.Height), searchObj.PixelFormat);
            searchRange = range;
        }

        public void SetRange(Rectangle range)
        {
            searchRange = range;
        }

        public void SetBmpSource(Bitmap sourceObj)
        {
            sourcePic = sourceObj.Clone(new Rectangle(0, 0, sourceObj.Width, sourceObj.Height), sourceObj.PixelFormat);
        }

        public static List<SearchMethod> GetAllSearchMethod()
        {
            List<SearchMethod> list = new List<SearchMethod>();
            System.Array valueList = System.Enum.GetValues(typeof(SearchMethod));
            foreach (var value in valueList)
            {
                list.Add((SearchMethod)value);
            }
            return list;
        }

        public List<Point> search()
        {
            result = GraphicSearch.FindPic(0, 0, searchRange.Width, searchRange.Height, sourcePic, searchImg, searchMethod);
            return result;
        }

        private Bitmap sourcePic;
        private Bitmap searchImg;
        private SearchMethod searchMethod;
        public Rectangle searchRange{get;set;}
        private List<Point> result = new List<Point>();
    }
}
