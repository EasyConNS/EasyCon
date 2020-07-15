using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Runtime.InteropServices;

namespace EasyCon.Graphic
{

    public class GraphicSearchOpenCv
    {
        public static Mat BitmapToMat(Bitmap srcbit)
        {
            int iwidth = srcbit.Width;
            int iheight = srcbit.Height;
            int iByte = iwidth * iheight * 3;
            byte[] result = new byte[iByte];
            int step;

            Rectangle rect = new Rectangle(0, 0, iwidth, iheight);
            BitmapData bmpData = srcbit.LockBits(rect, ImageLockMode.ReadWrite, srcbit.PixelFormat);
            IntPtr iPtr = bmpData.Scan0;
            Marshal.Copy(iPtr, result, 0, iByte);
            step = bmpData.Stride;
            srcbit.UnlockBits(bmpData);

            return new Mat(srcbit.Height, srcbit.Width, new MatType(MatType.CV_8UC3), result, step);
        }

        public static List<System.Drawing.Point> FindPic(int left, int top, int width, int height, Bitmap S_bmp, Bitmap P_bmp, int method)
        {
            List<System.Drawing.Point> res = new List<System.Drawing.Point>();
            Mat small = BitmapToMat(S_bmp);
            Mat big = BitmapToMat(P_bmp);
            Mat result = new Mat();

            switch (method)
            {
                case 1:
                    Cv2.MatchTemplate(big, small, result, TemplateMatchModes.CCoeffNormed);
                    break;
                case 2:
                    Cv2.MatchTemplate(big, small, result, TemplateMatchModes.SqDiff);
                    break;
                case 3:
                    Cv2.MatchTemplate(big, small, result, TemplateMatchModes.SqDiffNormed);
                    break;
                case 4:
                    // not good for our usage
                    Cv2.MatchTemplate(big, small, result, TemplateMatchModes.CCorr);
                    break;
                case 5:
                    Cv2.MatchTemplate(big, small, result, TemplateMatchModes.CCorrNormed);
                    break;
                case 6:
                    Cv2.MatchTemplate(big, small, result, TemplateMatchModes.CCoeff);
                    break;
                default:
                    Cv2.MatchTemplate(big, small, result, TemplateMatchModes.CCoeffNormed);
                    break;
            }

            OpenCvSharp.Point minLoc = new OpenCvSharp.Point(0, 0);
            OpenCvSharp.Point maxLoc = new OpenCvSharp.Point(0, 0);
            Cv2.MinMaxLoc(result, out minLoc, out maxLoc);

            // the sqD lower is good
            if(method ==3 || method ==2)
            {
                res.Add(new System.Drawing.Point(minLoc.X, minLoc.Y));
            }
            else
            {
                res.Add(new System.Drawing.Point(maxLoc.X, maxLoc.Y));
            }
            return res;
        }
    }
}
