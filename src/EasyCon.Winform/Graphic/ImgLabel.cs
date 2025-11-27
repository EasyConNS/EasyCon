using EasyCapture;
using JetBrains.Annotations;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace EasyCon2.Graphic;

// for ui data binding
public partial class ImgLabel : INotifyPropertyChanged
{
    public string name { get; set; }
    public SearchMethod searchMethod { get; set; }
    public string ImgBase64 { get; set; }

    private Bitmap searchImg;
    private Bitmap sourcePic;
    private Bitmap resultImg;
    private List<Point> result = new();
    //======================================
    // Actual implementation
    //======================================
    public event PropertyChangedEventHandler PropertyChanged;

    [NotifyPropertyChangedInvocator]
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private Rectangle _round = Rectangle.Empty;
    private Rectangle _target = Rectangle.Empty;
    private double _matchDegree = 95.0;
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

    public delegate Bitmap GetNewFrame();

    [NonSerialized]
    public GetNewFrame getNewFrame;
   
    public ImgLabel()
    {
        sourcePic = null;
        searchImg = null;
        searchMethod = SearchMethod.SqDiffNormed;
    }

    public ImgLabel(GetNewFrame getNewFrame)
    {
        sourcePic = null;
        searchImg = null;
        searchMethod = SearchMethod.SqDiffNormed;
    }

    public List<Point> Search(out double md)
    {
        if (searchImg.Width > RangeWidth || searchImg.Height > RangeHeight)
            throw new Exception("搜索图片大于搜索范围");

        sourcePic?.Dispose();
        Bitmap ss = getNewFrame();
        sourcePic = ss.Clone(new Rectangle(RangeX, RangeY, RangeWidth, RangeHeight), ss.PixelFormat);
        ss.Dispose();

        result = OpenCVSearch.FindPic(0, 0, TargetWidth, TargetHeight, sourcePic, searchImg, searchMethod, out md);
        md *= 100;

        // update the search pic
        //if (md >= _matchDegree)
        //{
        //    searchImg?.Dispose();
        //    Debug.WriteLine("update img");
        //    searchImg = sourcePic.Clone(new Rectangle(result[0].X, result[0].Y, TargetWidth, TargetHeight), sourcePic.PixelFormat);
        //}

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

        result = OpenCVSearch.FindPic(0, 0, TargetWidth, TargetHeight, sourcePic, searchImg, searchMethod, out md);
        md *= 100;

        // update the search pic
        //if (md >= _matchDegree)
        //{
        //    Debug.WriteLine("update img");
        //    searchImg?.Dispose();
        //    searchImg = sourcePic.Clone(new Rectangle(result[0].X, result[0].Y, TargetWidth, TargetHeight), sourcePic.PixelFormat);
        //}

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

        File.WriteAllText(path + name + ".IL", JsonSerializer.Serialize(this));
    }

    public void Copy(ImgLabel il)
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
        searchImg = il.getSearchImg();//Base64StringToImage(il.ImgBase64);
        getNewFrame = il.getNewFrame;
    }

    public void Refresh(GetNewFrame getnew)
    {
        searchImg = this.Base64StringToImage(ImgBase64);
        getNewFrame = getnew;
    }

    public void SetSource(GetNewFrame getnew)
    {
        getNewFrame = getnew;
    }
}
