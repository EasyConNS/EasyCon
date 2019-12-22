using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PokemonTycoon
{
    abstract class Script : IScriptOutput
    {
        const double DefaultColorCap = 0.98;
        const double DefaultImageCap = 0.9;

        protected IScriptOutput _output;
        bool _logEnabled = true;

        // ---- interfaces ----

        public void Log(object message)
        {
            if (_logEnabled)
                _output.Log(message);
        }

        protected void Log(string message)
        {
            if (_logEnabled)
                _output.Log(message);
        }

        protected void Log<T>(IEnumerable<T> list)
        {
            Log(string.Join(",", list));
        }

        public void PushMessage(object message = null, Color? color = null)
        {
            _output.PushMessage(message, color);
        }

        protected void Msg(params object[] args)
        {
            Color? color = null;
            foreach (var obj in args)
            {
                if (obj is Color)
                    color = obj as Color?;
                else
                    PushMessage(obj, color);
            }
            PushMessage();
        }

        public void Summary(string message)
        {
            _output.Summary(message);
        }

        public void Light(Color color)
        {
            _output.Light(color);
        }

        public void Light(bool b)
        {
            if (b)
                Light(Color.Lime);
            else
                Light(Color.Red);
        }

        public void Run(IScriptOutput output)
        {
            _output = output;
            Log($"-- Script Start --");
            Msg(Color.Lime, $"-- Script Start --");
            Run();
            Log($"-- Script End --");
            Msg(Color.Lime, $"-- Script End --");
        }

        protected abstract void Run();

        // ---- actions and checks ----

        protected void Wait(int milli)
        {
            Log($"Wait({milli})");
            Thread.Sleep(milli);
        }

        protected void WaitUntil(Func<bool> func)
        {
            Log($"WaitUntil({func.Method.Name})");
            try
            {
                _logEnabled = false;
                while (!func())
                    Thread.Sleep(300);
            }
            finally
            {
                _logEnabled = true;
            }
        }

        protected void WaitUntilNot(Func<bool> func)
        {
            Log($"WaitUntilNot({func.Method.Name})");
            try
            {
                _logEnabled = false;
                while (func())
                    Thread.Sleep(300);
            }
            finally
            {
                _logEnabled = true;
            }
        }

        protected bool Idle()
        {
            Log($"Idle()");
            return Monitor.Match(85, 982, ImageRes.Get("idle"), DefaultImageCap, Monitor.LineSampler(3));
        }

        protected bool Menu()
        {
            Log($"Menu()");
            return Monitor.Match(1632, 740, ImageRes.Get("menu"), DefaultImageCap, Monitor.LineSampler(3));
        }

        protected Tuple<int, int> Menu_GetIndex()
        {
            Log($"Menu_GetIndex()");
            for (int x = 0; x < 5; x++)
                for (int y = 0; y < 2; y++)
                    if (Monitor.Match(255 + x * 352, 140 + y * 360, Color.FromArgb(0, 0, 0), DefaultColorCap))
                        return new Tuple<int, int>(x, y);
            return new Tuple<int, int>(-1, -1);
        }

        protected bool Pokelist()
        {
            Log($"Pokelist()");
            return Monitor.Match(1655, 980, ImageRes.Get("pokelist"), DefaultImageCap, Monitor.LineSampler(3));
        }

        protected int Pokelist_GetSelection()
        {
            Log($"Pokelist_GetSelection()");
            for (int i = 0; i < 6; i++)
                if (Monitor.Match(450, 172 + i * 144, Color.FromArgb(0, 0, 0), DefaultColorCap))
                    return i;
            return -1;
        }

        protected List<bool> Pokelist_GetEggs()
        {
            Log($"Pokelist_GetEggs()");
            List<bool> list = new List<bool>(6);
            for (int i = 0; i < 6; i++)
            {
                int x = 145;
                int y = 256 + i * 144;
                list.Add(Monitor.Match(x, y, ImageRes.Get("pokelist_egg"), DefaultImageCap));
            }
            Log(list);
            return list;
        }

        protected bool Pokebox()
        {
            Log($"Pokebox()");
            return Monitor.Match(48, 28, ImageRes.Get("pokebox"), DefaultImageCap);
        }

        protected int Pokebox_GetSummaryPage()
        {
            Log($"Pokebox_GetSummaryPage()");
            if (!Monitor.Match(1473, 338, Color.FromArgb(218, 218, 218), DefaultColorCap))
                return 0;
            if (Monitor.Match(1723, 1054, Color.FromArgb(254, 254, 254), DefaultColorCap))
                return 1;
            return 2;
        }

        protected List<int> Pokebox_GetIVs()
        {
            Log($"Pokebox_GetIVs()");
            List<int> list = new List<int>(6);
            for (int i = 0; i < 6; i++)
            {
                int x = 1500;
                int y = 225 + i * 56;
                int iv = -1;
                if (Monitor.Match(x, y, ImageRes.Get("pokebox_iv31"), DefaultImageCap, Monitor.LineSampler(3)))
                    iv = 31;
                else if (Monitor.Match(x, y, ImageRes.Get("pokebox_iv0"), DefaultImageCap, Monitor.LineSampler(3)))
                    iv = 0;
                list.Add(iv);
            }
            Log(list);
            return list;
        }

        protected bool Pokebox_IsShiny()
        {
            Log($"Pokebox_IsShiny()");
            return Monitor.Match(1880, 170, ImageRes.Get("pokebox_shiny"), DefaultImageCap);
        }

        protected bool Battle()
        {
            Log($"Battle()");
            return Monitor.Match(1800, 540, ImageRes.Get("battle"), DefaultImageCap, Monitor.LineSampler(3));
        }

        protected bool Battle_RaidCatchCheck()
        {
            Log($"Battle_RaidCatchCheck()");
            return Monitor.Match(1680, 915, ImageRes.Get("battle_raidcatch"), DefaultImageCap, Monitor.LineSampler(3));
        }
    }

    class ScTest : Script
    {
        protected override void Run()
        {
            //WaitUntil(Idle);
            //Msg($"Idle");
            //WaitUntil(Menu);
            //Msg($"Menu");
            //var t = Menu_GetIndex();
            //WaitUntil(Pokelist);
            //Msg($"Pokelist");
            //WaitUntil(Pokebox);
            //Msg($"Pokebox");

            //while (true)
            //{
            //    var shadow = Monitor.MatchShadow();
            //    Light(shadow != null);
            //    if (shadow != null)
            //        Msg(shadow.Name);
            //    Wait(100);
            //}

            while (true)
            {
                Light(Battle());
                Wait(100);
            }
        }
    }
}
