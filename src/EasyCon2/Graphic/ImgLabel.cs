using EasyCapture;
using JetBrains.Annotations;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using IL = EasyCapture.ImgLabel;

namespace EasyCon2.Graphic;

// for ui data binding
public class ImgLabel(IL label) : INotifyPropertyChanged
{
    //======================================
    // Actual implementation
    //======================================
    public event PropertyChangedEventHandler PropertyChanged;

    [NotifyPropertyChangedInvocator]
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private string _name = label.name;
    private Rectangle _round = new Rectangle(label.RangeX, label.RangeY, label.RangeWidth, label.RangeHeight);
    private Rectangle _target = new Rectangle(label.TargetX, label.TargetY, label.TargetWidth, label.TargetHeight);
    private IL res = label;

    public SearchMethod searchMethod { get; set; } = label.searchMethod;

    public string name
    {
        get { return _name; }
        set
        {
            _name = value;

            //===================
            // Usage in the Source
            //===================
            OnPropertyChanged();
        }
    }

    public int RangeX
    {
        get { return _round.X; }
        set
        {
            // do not trigger change event if values are the same
            if (Equals(value, _round.X)) return;
            _round.X = value < 0 ? 0 : value;

            //===================
            // Usage in the Source
            //===================
            OnPropertyChanged();
        }
    }

    public int RangeY
    {
        get { return _round.Y; }
        set
        {
            // do not trigger change event if values are the same
            if (Equals(value, _round.Y)) return;
            _round.Y = value < 0 ? 0 : value;

            //===================
            // Usage in the Source
            //===================
            OnPropertyChanged();
        }
    }

    public int RangeWidth
    {
        get { return _round.Width; }
        set
        {
            // do not trigger change event if values are the same
            if (Equals(value, _round.Width)) return;
            _round.Width = value < 0 ? 0 : value;

            //===================
            // Usage in the Source
            //===================
            OnPropertyChanged();
        }
    }

    public int RangeHeight
    {
        get { return _round.Height; }
        set
        {
            // do not trigger change event if values are the same
            if (Equals(value, _round.Height)) return;
            _round.Height = value < 0 ? 0 : value;

            //===================
            // Usage in the Source
            //===================
            OnPropertyChanged();
        }
    }

    public int TargetX
    {
        get { return _target.X; }
        set
        {
            // do not trigger change event if values are the same
            if (Equals(value, _target.X)) return;
            _target.X = value < 0 ? 0 : value;

            //===================
            // Usage in the Source
            //===================
            OnPropertyChanged();
        }
    }

    public int TargetY
    {
        get { return _target.Y; }
        set
        {
            // do not trigger change event if values are the same
            if (Equals(value, _target.Y)) return;
            _target.Y = value < 0 ? 0 : value;

            //===================
            // Usage in the Source
            //===================
            OnPropertyChanged();
        }
    }

    public int TargetWidth
    {
        get { return _target.Width; }
        set
        {
            // do not trigger change event if values are the same
            if (Equals(value, _target.Width)) return;
            _target.Width = value < 0 ? 0 : value;

            //===================
            // Usage in the Source
            //===================
            OnPropertyChanged();
        }
    }

    public int TargetHeight
    {
        get { return _target.Height; }
        set
        {
            // do not trigger change event if values are the same
            if (Equals(value, _target.Height)) return;
            _target.Height = value < 0 ? 0 : value;

            //===================
            // Usage in the Source
            //===================
            OnPropertyChanged();
        }
    }

    public Bitmap GetSearchImg() => res.GetBitmap();

    //public Bitmap getResultImg(Point p)
    //{
    //    return sourcePic.Clone(new Rectangle(p.X, p.Y, searchImg.Width, searchImg.Height), sourcePic.PixelFormat);
    //}
}

public static class ILExt
{
    public static IEnumerable<ImgLabel> LoadIL(string dir)
    {
        var imgLabels = ImmutableArray.CreateBuilder<ImgLabel>();

        foreach (var file in Directory.GetFiles(dir, "*.IL"))
        {
            try
            {
                var il = ECSearch.LoadIL(file);
                imgLabels.Add(new ImgLabel(il));
            }
            catch
            {
                Debug.WriteLine("无法加载标签:", file);
            }
        }
        return imgLabels.ToImmutable();
    }

    public static List<Point> Search(this ImgLabel img, out double md)
    {
        md = 1;
        //if (searchImg.Width > RangeWidth || searchImg.Height > RangeHeight)
        //    throw new Exception("搜索图片大于搜索范围");

        //sourcePic?.Dispose();
        //Bitmap ss = getNewFrame();
        //sourcePic = ss.Clone(_round, ss.PixelFormat);
        //ss.Dispose();

        List<Point> result = new();
        //if (searchMethod == SearchMethod.TesserDetect)
        //{
        //    using var targetBmp = sourcePic.Clone(new Rectangle(TargetX - RangeX, TargetY - RangeY, TargetWidth, TargetHeight), sourcePic.PixelFormat);
        //    result = ECSearch.FindOCR(ImgBase64, targetBmp, out md);
        //}
        //else
        //{
        //    result = ECSearch.FindPic(0, 0, TargetWidth, TargetHeight, sourcePic, searchImg, searchMethod, out md);
        //}
        md *= 100;

        // update the search pic
        //if (md >= _matchDegree)
        //{
        //    Debug.WriteLine("update img");
        //    searchImg = sourcePic.Clone(new Rectangle(result[0].X, result[0].Y, TargetWidth, TargetHeight), sourcePic.PixelFormat);
        //}

        return result;
    }
}