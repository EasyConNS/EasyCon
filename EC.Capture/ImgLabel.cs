namespace EC.Capture;

public record class ImgLabel
{
    public int RangeX { get; set; }
    public int RangeY { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }


    public int TargetX { get; set; }
    public int TargetY { get; set; }

    public int matchDegree { get; set; }

    public int searchMethod { get; set; }

    public string searchData { get; set; } = "";

    public void Save()
    {
        // TODO
    }
}
