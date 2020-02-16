using PokemonTycoon.Graphic;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace PokemonTycoon.Scripts
{
    public class Pokelist : ScriptCore
    {
        public const int MAX_LENGTH = 6;

        public bool Check()
        {
            Log($"Pokelist.Check()");
            return VideoCapture.Match(1655, 980, ImageRes.Get("pokelist"), DefaultImageCap, VideoCapture.LineSampler(3)) || VideoCapture.Match(96, 70, ImageRes.Get("pokelist2"), DefaultImageCap, VideoCapture.LineSampler(3));
        }

        public int GetSelection()
        {
            Log($"Pokelist.GetSelection()");
            for (int i = 0; i < 6; i++)
                if (VideoCapture.Match(450, 172 + i * 144, Color.FromArgb(0, 0, 0), DefaultColorCap))
                    return i;
            return -1;
        }

        public enum SlotState
        {
            Empty,
            Pokemon,
            Selected,
        }

        public List<SlotState> CheckSlot()
        {
            Log($"Pokelist.CheckSlot()");
            List<SlotState> list = new List<SlotState>(6);
            for (int i = 0; i < 6; i++)
            {
                int x = 660;
                int y = 230 + i * 144;
                SlotState slot = SlotState.Empty;
                if (VideoCapture.Match(x, y, Color.FromArgb(254, 254, 254), DefaultColorCap))
                    slot = SlotState.Pokemon;
                else if (VideoCapture.Match(x, y, Color.FromArgb(0, 0, 0), DefaultColorCap))
                    slot = SlotState.Selected;
                list.Add(slot);
            }
            Log(list);
            return list;
        }

        public List<bool> CheckEggs()
        {
            Log($"Pokelist.CheckEggs()");
            List<bool> list = new List<bool>(6);
            for (int i = 0; i < 6; i++)
            {
                int x = 145;
                int y = 256 + i * 144;
                list.Add(VideoCapture.Match(x, y, ImageRes.Get("pokelist_egg"), DefaultImageCap));
            }
            Log(list);
            return list;
        }
    }
}
