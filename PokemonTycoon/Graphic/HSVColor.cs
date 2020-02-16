using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PokemonTycoon.Graphic
{
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
    }
}
