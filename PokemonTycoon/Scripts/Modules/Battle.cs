using PokemonTycoon.Graphic;
using System;
using System.Drawing;
using System.Linq;

namespace PokemonTycoon.Scripts
{
    internal class Battle : ScriptCore
    {
        public bool Check()
        {
            Log($"Battle.Check()");
            return VideoCapture.Match(1800, 540, ImageRes.Get("battle"), DefaultImageCap, VideoCapture.LineSampler(3));
        }

        public bool CheckSelectSkill()
        {
            Log($"Battle.CheckSelectSkill()");
            return VideoCapture.Match(1765, 540, ImageRes.Get("battle_skills"), DefaultImageCap, VideoCapture.LineSampler(3))
                || VideoCapture.Match(1770, 545, ImageRes.Get("battle_skills2"), DefaultImageCap, VideoCapture.LineSampler(3));
        }

        public int GetSkillCursor()
        {
            Log($"Battle.GetSkillCursor()");
            for (int i = 0; i < 4; i++)
                if (VideoCapture.Match(1296, 694 + 103 * i, Color.FromArgb(0, 0, 0), DefaultColorCap))
                    return i;
            return -1;
        }

        public bool IsSkillAvailable(int index)
        {
            Log($"Battle.IsSkillAvailable()");
            return VideoCapture.Match(1890, 695 + 103 * index, Color.FromArgb(0, 0, 0), DefaultColorCap);
        }

        public Bool3VL IsFainted()
        {
            Log($"Battle.IsFainted()");
            if (VideoCapture.Match(850, 10, ImageRes.Get("battle_fainted"), DefaultImageCap, VideoCapture.LineSampler(3)))
                return Bool3VL.True;
            if (VideoCapture.Match(850, 10, ImageRes.Get("battle_notfainted"), DefaultImageCap, VideoCapture.LineSampler(3)))
                return Bool3VL.False;
            return Bool3VL.Unknown;
        }

        public bool IsDisabled()
        {
            Log($"Battle.IsDisabled()");
            return VideoCapture.Match(180, 970, ImageRes.Get("battle_disabled"), DefaultImageCap, VideoCapture.LineSampler(3));
        }

        public class Raid : ScriptCore
        {
            public const int MAX_MEMBERS = 4;

            public bool CheckMenu()
            {
                Log($"Battle.Raid.CheckMenu()");
                return (VideoCapture.Match(445, 880, Color.FromArgb(126, 5, 19), DefaultColorCap) && VideoCapture.Match(455, 880, Color.FromArgb(203, 28, 69), DefaultColorCap) && VideoCapture.Match(455, 900, Color.FromArgb(218, 31, 76), DefaultColorCap))
                    || (VideoCapture.Match(445, 880, Color.FromArgb(132, 52, 10), DefaultColorCap) && VideoCapture.Match(455, 880, Color.FromArgb(204, 99, 29), DefaultColorCap) && VideoCapture.Match(455, 900, Color.FromArgb(218, 107, 33), DefaultColorCap));
            }

            public bool CheckMenuCode()
            {
                Log($"Battle.Raid.CheckMenuCode()");
                return VideoCapture.Match(1830, 1040, ImageRes.Get("battle_raidmenucode"), DefaultImageCap, VideoCapture.LineSampler(3))
                    || VideoCapture.Match(1750, 1040, ImageRes.Get("battle_raidmenucode2"), DefaultImageCap, VideoCapture.LineSampler(3));
            }

            public int GetCursor()
            {
                Log($"Battle.Raid.GetCursor()");
                for (int i = 0; i < 4; i++)
                    if (VideoCapture.Match(1840, 680 + i * 86, Color.FromArgb(0, 0, 0), DefaultColorCap))
                        return i;
                return -1;
            }

            public int GetMenuLength()
            {
                Log($"Battle.Raid.GetMenuLength()");
                int n = 0;
                for (int i = 0; i < 4; i++)
                    if (VideoCapture.Match(1840, 680 + i * 86, Color.FromArgb(254, 254, 254), DefaultColorCap) || VideoCapture.Match(1840, 680 + i * 86, Color.FromArgb(0, 0, 0), DefaultColorCap))
                        n++;
                return n;
            }

            public int GetStars()
            {
                Log($"Battle.Raid.GetStars()");
                int i = 0;
                for (; i < 5; i++)
                    if (!VideoCapture.Match(84 + i * 92, 116, Color.FromArgb(254, 254, 254), DefaultColorCap))
                        break;
                return i;
            }

            public bool CheckCatch()
            {
                Log($"Battle.Raid.CheckCatch()");
                return VideoCapture.Match(1680, 915, ImageRes.Get("battle_raidcatch"), DefaultImageCap, VideoCapture.LineSampler(3))
                    || VideoCapture.Match(180, 990, ImageRes.Get("battle_raidcatch2"), DefaultImageCap, VideoCapture.LineSampler(3));
            }

            public bool CheckEnd()
            {
                Log($"Battle.Raid.CheckEnd()");
                return VideoCapture.Match(880, 170, ImageRes.Get("battle_raidend"), DefaultImageCap, VideoCapture.LineSampler(3));
            }

            public bool CheckSeatEmpty(int index)
            {
                Log($"Battle.Raid.CheckSeatEmpty({index})");
                return VideoCapture.Match(1246, 395 + index * 84, Color.FromArgb(254, 254, 254), DefaultColorCap);
            }

            public bool CheckSeatReady(int index)
            {
                Log($"Battle.Raid.CheckSeatReady({index})");
                return VideoCapture.Match(1145, 395 + index * 84, Color.FromArgb(18, 217, 72), DefaultColorCap);
            }

            public bool CheckTimeout()
            {
                Log($"Battle.Raid.CheckTimeout()");
                return VideoCapture.Match(665, 945, ImageRes.Get("battle_raidtimeout"), DefaultImageCap, VideoCapture.LineSampler(3))
                    || VideoCapture.Match(540, 950, ImageRes.Get("battle_raidtimeout2"), DefaultImageCap, VideoCapture.LineSampler(3));
            }

            public void OpenRaidMenu()
            {
                var timeout = DateTime.Now.AddSeconds(3);
                int dialogs = 0;
                while (!CheckMenu())
                {
                    if (DateTime.Now >= timeout)
                        throw new ScriptException("等待超时");
                    if (Module<Idle>().Check())
                    {
                        Press(Keys.A);
                        timeout = timeout.AddSeconds(10);
                        Wait(500);
                        continue;
                    }
                    if (Module<Idle>().CheckDialog())
                    {
                        dialogs++;
                        if (dialogs > 2)
                            throw new ScriptException("对话过多");
                        Press(Keys.A);
                        timeout = timeout.AddSeconds(10);
                        Wait(500);
                        continue;
                    }
                    else
                        dialogs = 0;
                    Wait(500);
                }
                Wait(500);
            }

            public void Quit()
            {
                Log($"Battle.Raid.Quit()");
                var timeout = DateTime.Now + TimeSpan.FromSeconds(30);
                while (true)
                {
                    if (DateTime.Now >= timeout)
                        throw new ScriptException("退出失败");
                    var r = WaitUntil(Module<Idle>().Check, CheckMenu);
                    if (r == Module<Idle>().Check)
                        return;
                    var l = GetMenuLength();
                    if (l == 4)
                    {
                        Press(Keys.B);
                        WaitUntil(5, Module<Idle>().Check);
                    }
                    else if (l == 3)
                    {
                        Press(Keys.B);
                        Wait(1000);
                        Press(Keys.A);
                        WaitUntil(5, Module<Idle>().Check);
                    }
                    else
                        Wait(500);
                }
            }

            public enum BeamColor
            {
                Unknown,
                Red,
                Purple,
            }

            public BeamColor GetBeamColor()
            {
                Log($"Battle.Raid.GetBeamColor()");
                int red = 0;
                int purple = 0;
                var colors = VideoCapture.GetPixels(915, 540, 90, 1).Select(u => new HSVColor(u));
                foreach (var color in colors)
                {
                    //Log(color.H, " ", color.S, " ", color.V);
                    if ((color.H >= 330 || color.H <= 10) && color.S >= 25)
                        red++;
                    if ((color.H >= 280 && color.H <= 310) && color.S >= 25)
                        purple++;
                }
                Log(red, " ", purple);
                if (red > 5 && red >= purple * 2)
                    return BeamColor.Red;
                if (purple > 5 && purple >= red * 2)
                    return BeamColor.Purple;
                return BeamColor.Unknown;
            }
        }

        public void RunAway()
        {
            Log($"Battle.RunAway()");
            while (true)
            {
                Press(Keys.HAT.Up);
                Wait(300);
                Press(Keys.A);
                WaitUntil(30, Module<Idle>().Check);
                if (!Check())
                    break;
                Press(Keys.B);
                Wait(300);
            }
        }
    }
}
