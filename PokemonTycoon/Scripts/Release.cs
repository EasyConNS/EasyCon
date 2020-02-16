using PokemonTycoon.Graphic;
using PokemonTycoon.Scripts.PokemonFilter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PokemonTycoon.Scripts
{
    class Release : Script
    {
        class Var : Filter.Vars
        {
            public Filter Filter;
        }

        Var _vars = new Var();

        public bool Check()
        {
            return VideoCapture.Match(1340, 715, ImageRes.Get("pokebox_release"), DefaultImageCap, VideoCapture.LineSampler(3));
        }

        public void Run(int boxcount, string filterStr)
        {
            Msg(Colors.Highlight, "自动放生");
            _vars.Count = 0;
            try
            {
                _vars.Filter = Filter.Parse(filterStr);
                _vars.Filter.Init(_vars);
            }
            catch (FormatException ex)
            {
                throw new ScriptException(ex.Message);
            }
            var cur = Module<Pokebox>().GetCursor();
            if (cur.Section != Pokebox.Section.Box)
                return;
            int x = cur.X, y = cur.Y;
            int n = 0, max = boxcount * Pokebox.MAX_X * Pokebox.MAX_Y - y * Pokebox.MAX_X - x;
            Progress = 0;
            Light(Taskbar.Progress);
            int box = 0;
            while (true)
            {
                n++;
                Msg($"第({n}/{max})只");
                Progress = (double)n / max;
                if (!Module<Pokebox>().IsEmptySlot())
                {
                    if (!Module<Pokebox>().CheckFilter(_vars.Filter))
                        Module<Pokebox>().Release();
                    else
                        _vars.Count++;
                }
                if (x == Pokebox.MAX_X - 1 && y == Pokebox.MAX_Y - 1)
                {
                    box++;
                    if (box >= boxcount)
                        break;
                }
                Module<Pokebox>().SelectNext(ref x, ref y);
            }
        }
    }
}
