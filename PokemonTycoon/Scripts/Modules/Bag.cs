using PTDevice;
using PokemonTycoon.Graphic;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Collections.Generic;

namespace PokemonTycoon.Scripts
{
    public class Bag : ScriptCore
    {
        public enum Catagory
        {
            Medicine = 0,
            Pokeballs = 1,
            BattleItems = 2,
            Berries = 3,
            OtherItems = 4,
            TMs = 5,
            Treasures = 6,
            Ingredients = 7,
            KeyItems = 8,
        }

        static Dictionary<Catagory, Point> _dictCatagory = new Dictionary<Catagory, Point>();

        static Bag()
        {
            _dictCatagory[Catagory.Medicine] = new Point(1065, 109);
            _dictCatagory[Catagory.Pokeballs] = new Point(1142, 104);
            _dictCatagory[Catagory.BattleItems] = new Point(1219, 70);
            _dictCatagory[Catagory.Berries] = new Point(1313, 85);
            _dictCatagory[Catagory.OtherItems] = new Point(1372, 92);
            _dictCatagory[Catagory.TMs] = new Point(1468, 110);
            _dictCatagory[Catagory.Treasures] = new Point(1575, 84);
            _dictCatagory[Catagory.Ingredients] = new Point(1661, 124);
            _dictCatagory[Catagory.KeyItems] = new Point(1736, 82);
        }

        public bool Check()
        {
            Log($"Bag.Check()");
            return VideoCapture.Match(35, 50, ImageRes.Get("bag"), DefaultImageCap, VideoCapture.LineSampler(3));
        }

        public Catagory GetSelectedCatagory()
        {
            Log($"Bag.GetSelectedCatagory()");
            foreach (Catagory c in Enum.GetValues(typeof(Catagory)))
                if (VideoCapture.Match(_dictCatagory[c].X, _dictCatagory[c].Y, Color.FromArgb(0, 0, 0), DefaultColorCap))
                    return c;
            throw new ScriptException("背包分类检测失败");
        }

        public void SelectCatagory(Catagory c)
        {
            Log($"Bag.SelectCatagory()");
            var c0 = (int)GetSelectedCatagory();
            var c1 = (int)c;
            while (c0 < c1)
            {
                Press(Keys.HAT.Right);
                Wait(200);
                c0++;
            }
            while (c0 > c1)
            {
                Press(Keys.HAT.Left);
                Wait(200);
                c0--;
            }
        }
    }
}
