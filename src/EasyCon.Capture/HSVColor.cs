using System.Drawing;

namespace EasyCapture;

public struct HSVColor
{
    public double H { get; private set; }
    public double S { get; private set; }
    public double V { get; private set; }

    Color _c;

    public HSVColor(Color c)
    {
        _c = c;
        var r = _c.R / 255.0;
        var g = _c.G / 255.0;
        var b = _c.B / 255.0;
        var cmax = Math.Max(Math.Max(r, g), b);
        var cmin = Math.Min(Math.Min(r, g), b);
        var dt = cmax - cmin;
        if (dt == 0)
            H = 0;
        else if (cmax == r)
            H = 60 * (((g - b) / dt + 6) % 6);
        else if (cmax == g)
            H = 60 * ((b - r) / dt + 2);
        else
            H = 60 * ((r - g) / dt + 4);
        if (cmax == 0)
            S = 0;
        else
            S = dt / cmax * 100;
        V = cmax * 100;
    }

    public static implicit operator HSVColor(Color c)
    {
        return new HSVColor(c);
    }

    public static implicit operator Color(HSVColor hsv)
    {
        return HSVColorExt.ColorFromHSV(hsv.H, hsv.S, hsv.V);
    }
}

public static class HSVColorExt
{
    public static void ColorToHSV(Color color, out double hue, out double saturation, out double value)
    {
        int max = Math.Max(color.R, Math.Max(color.G, color.B));
        int min = Math.Min(color.R, Math.Min(color.G, color.B));

        hue = color.GetHue();
        saturation = (max == 0) ? 0 : 1d - (1d * min / max);
        value = max / 255d;
    }

    public static Color ColorFromHSV(double hue, double saturation, double value)
    {
        int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
        double f = hue / 60 - Math.Floor(hue / 60);

        value = value * 255;
        int v = Convert.ToInt32(value);
        int p = Convert.ToInt32(value * (1 - saturation));
        int q = Convert.ToInt32(value * (1 - f * saturation));
        int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

        if (hi == 0)
            return Color.FromArgb(255, v, t, p);
        else if (hi == 1)
            return Color.FromArgb(255, q, v, p);
        else if (hi == 2)
            return Color.FromArgb(255, p, v, t);
        else if (hi == 3)
            return Color.FromArgb(255, p, q, v);
        else if (hi == 4)
            return Color.FromArgb(255, t, p, v);
        else
            return Color.FromArgb(255, v, p, q);
    }
}