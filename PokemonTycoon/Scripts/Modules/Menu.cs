using PTDevice;
using PokemonTycoon.Graphic;
using System;
using System.Diagnostics;
using System.Drawing;

namespace PokemonTycoon.Scripts
{
    public class Menu : ScriptCore
    {
        public enum Entry
        {
            Dex,
            Pokelist,
            Bag,
            Card,
            Save,
            Map,
            Camp,
            Gift,
            VS,
            Options,
        }

        public bool Check()
        {
            Log($"Menu.Check()");
            return VideoCapture.Match(1632, 740, ImageRes.Get("menu"), DefaultImageCap, VideoCapture.LineSampler(3)) || VideoCapture.Match(1632, 740, ImageRes.Get("menu2"), DefaultImageCap, VideoCapture.LineSampler(3));
        }

        public Tuple<int, int> GetCursorPos()
        {
            Log($"Menu.GetIndex()");
            for (int x = 0; x < 5; x++)
                for (int y = 0; y < 2; y++)
                    if (VideoCapture.Match(255 + x * 352, 140 + y * 360, Color.FromArgb(0, 0, 0), DefaultColorCap))
                        return new Tuple<int, int>(x, y);
            return new Tuple<int, int>(-1, -1);
        }

        public Tuple<int, int> LocateEntry(Entry entry)
        {
            Log($"Menu.LocateEntry({entry.GetName()})");
            var image = ImageRes.Get($"menuentry_{entry.GetName().ToLower()}");
            for (int x = 0; x < 5; x++)
                for (int y = 0; y < 2; y++)
                    if (VideoCapture.Match(235 + x * 352, 180 + y * 360, image, DefaultImageCap, VideoCapture.LineSampler(3)))
                        return new Tuple<int, int>(x, y);
            return new Tuple<int, int>(-1, -1);
        }

        public bool Open()
        {
            Log($"Menu.Open()");
            if (Check())
                return true;
            var f = WaitUntil(3, Module<Idle>().Check, Check);
            if (f == null)
                return false;
            if (f == Module<Idle>().Check)
                PressUntil(NintendoSwitch.Key.Button(NintendoSwitch.Button.X), 1000, Check);
            return true;
        }

        public void OpenEntry(Entry entry)
        {
            Log($"Menu.OpenEntry({entry.GetName()})");
            var t0 = GetCursorPos();
            var t1 = LocateEntry(entry);
            int x0 = t0.Item1, y0 = t0.Item2;
            int x1 = t1.Item1, y1 = t1.Item2;
            while (x1 >= 0)
            {
                NintendoSwitch.Key key = null;
                if (x0 < x1)
                {
                    key = Keys.HAT.Right;
                    x0++;
                }
                else if (x0 > x1)
                {
                    key = Keys.HAT.Left;
                    x0--;
                }
                else if (y0 < y1)
                {
                    key = Keys.HAT.Down;
                    y0++;
                }
                else if (y0 > y1)
                {
                    key = Keys.HAT.Up;
                    y0--;
                }
                if (key == null)
                    break;
                Press(key);
                Wait(300);
            }
            Press(Keys.A);
        }

        public void Save()
        {
            Log($"Menu.Save()");
            Open();
            Press(Keys.R);
            Wait(1800);
            Press(Keys.A);
            WaitUntil(Keys.A, 5, Module<Idle>().Check);
        }
    }
}
