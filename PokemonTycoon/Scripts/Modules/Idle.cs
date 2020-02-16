using PokemonTycoon.Graphic;
using System;
using System.Drawing;

namespace PokemonTycoon.Scripts
{
    public class Idle : ScriptCore
    {
        public bool Check()
        {
            Log($"Idle.Check()");
            return VideoCapture.Match(85, 982, ImageRes.Get("idle"), DefaultImageCap, VideoCapture.LineSampler(3))
                || VideoCapture.Match(85, 982, ImageRes.Get("idle2"), DefaultImageCap, VideoCapture.LineSampler(3));
        }

        public Bool3VL CheckConnected()
        {
            Log($"Idle.CheckConnected()");
            if (VideoCapture.Match(5, 1075, Color.FromArgb(45, 74, 244), DefaultColorCap))
                return Bool3VL.True;
            if (VideoCapture.Match(5, 1075, Color.FromArgb(254, 254, 254), DefaultColorCap))
                return Bool3VL.False;
            return Bool3VL.Unknown;
        }

        public bool CheckDialog()
        {
            Log($"Idle.CheckDialog()");
            return VideoCapture.Match(338, 1022, Color.FromArgb(34, 38, 32), DefaultColorCap) && VideoCapture.Match(347, 1039, Color.FromArgb(51, 51, 51), DefaultColorCap);
        }
    }
}
