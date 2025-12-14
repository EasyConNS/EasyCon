namespace EasyCon2.Forms;

public class PaintControl : Control
{
    public PaintEventHandler PaintEventHandler;

    public PaintControl()
    {
        SetStyle(ControlStyles.UserPaint, true);
        SetStyle(ControlStyles.AllPaintingInWmPaint, true); // 禁止擦除背景.
        SetStyle(ControlStyles.DoubleBuffer, true); // 双缓冲
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        PaintEventHandler?.Invoke(null, e);
    }
}
