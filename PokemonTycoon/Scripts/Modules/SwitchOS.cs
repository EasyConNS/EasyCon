using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PTDevice;
using PokemonTycoon.Graphic;

namespace PokemonTycoon.Scripts
{
    class SwitchOS : ScriptCore
    {
        public bool Check()
        {
            Log($"SwitchOS.Check()");
            return VideoCapture.Match(522, 787, ImageRes.Get("os"), DefaultImageCap, VideoCapture.LineSampler(3));
        }

        public bool CheckBlackScreen()
        {
            Log($"SwitchOS.CheckBlackScreen()");
            try
            {
                VideoCapture.Freeze();
                for (int x = 500; x <= 1000; x += 100)
                    for (int y = 100; y <= 200; y += 100)
                        if (!VideoCapture.Match(x, y, Color.FromArgb(0, 0, 0), DefaultColorCap))
                            return false;
                return true;
            }
            finally
            {
                VideoCapture.Unfreeze();
            }
        }

        public bool CheckSystemMessage()
        {
            return VideoCapture.Match(1000, 1000, Color.FromArgb(93, 92, 99), DefaultColorCap)
                && VideoCapture.Match(420, 360, Color.FromArgb(57, 57, 59), DefaultColorCap);
        }

        public void GoToMenu()
        {
            Log($"SwitchOS.GoToMenu()");
            if (!Check())
            {
                Press(Keys.HOME);
                WaitUntil(Keys.HOME, 5, Check);
                Wait(300);
            }
        }

        public void CloseGame()
        {
            Log($"SwitchOS.CloseGame()");
            GoToMenu();
            Press(Keys.X);
            Wait(1000);
            if (!Check())
            {
                Press(Keys.A);
                Wait(2000);
                WaitUntil(Check, TimeoutThrow(10));
            }
        }

        public void RestartGame()
        {
            Log($"SwitchOS.RestartGame()");
            CloseGame();
            PressUntil(Keys.A, 500, CheckBlackScreen, TimeoutThrow(15));
            WaitUntil(Keys.A, 20, () => !CheckBlackScreen());
            PressUntil(Keys.A, 500, CheckBlackScreen, Module<Idle>().Check, TimeoutThrow(30));
            WaitUntil(Module<Idle>().Check, TimeoutThrow(15));
        }

        int GetDateCursor()
        {
            if (VideoCapture.Match(348, 895, ImageRes.Get("os_yearselected"), DefaultImageCap, VideoCapture.LineSampler(3)))
                return 0;
            if (VideoCapture.Match(602, 895, ImageRes.Get("os_monthselected"), DefaultImageCap, VideoCapture.LineSampler(3)))
                return 1;
            if (VideoCapture.Match(798, 895, ImageRes.Get("os_dayselected"), DefaultImageCap, VideoCapture.LineSampler(3)))
                return 2;
            if (VideoCapture.Match(1052, 895, ImageRes.Get("os_hourselected"), DefaultImageCap, VideoCapture.LineSampler(3)))
                return 3;
            if (VideoCapture.Match(1247, 895, ImageRes.Get("os_minuteselected"), DefaultImageCap, VideoCapture.LineSampler(3)))
                return 4;
            return 5;
        }

        public void ChangeDateCore()
        {
            Log($"SwitchOS.ChangeDateCore()");
            Press(Keys.A);
            Wait(500);
            var cur = GetDateCursor();
            while (cur > 2)
            {
                Press(Keys.HAT.Left);
                Wait(200);
                cur--;
            }
            while (cur < 2)
            {
                Press(Keys.HAT.Right);
                Wait(200);
                cur++;
            }
            Press(Keys.HAT.Up);
            Wait(300);
            if (VideoCapture.Match(772, 676, ImageRes.Get("os_day01"), DefaultImageCap, VideoCapture.LineSampler(3)))
            {
                // change month
                Press(Keys.HAT.Left);
                Wait(200);
                Press(Keys.HAT.Up);
                Wait(300);
                if (VideoCapture.Match(573, 671, ImageRes.Get("os_month01"), DefaultImageCap, VideoCapture.LineSampler(3)))
                {
                    // change years
                    Press(Keys.HAT.Left);
                    Wait(200);
                    Press(Keys.HAT.Up);
                    Wait(200);
                    Press(Keys.HAT.Right);
                    Wait(200);
                }
                Press(Keys.HAT.Right);
                Wait(200);
            }
            Press(Keys.HAT.Right);
            Wait(200);
            Press(Keys.HAT.Right);
            Wait(200);
            Press(Keys.HAT.Right);
            Wait(200);
            Press(Keys.A);
            Wait(500);
        }

        public int ChangeDateCoreFast(bool first = false)
        {
            Log($"SwitchOS.ChangeDateCoreFast({first})");
            const int WAIT_MS = 50;
            int reset = 0;
            while (true)
            {
                NS.Down(Keys.A);
                Wait(200);
                if (first)
                {
                    NS.Down(Keys.LStick.Right);
                    Wait(WAIT_MS);
                    NS.Down(Keys.RStick.Right);
                    Wait(WAIT_MS);
                    NS.Reset();
                }
                else
                {
                    NS.Down(Keys.LStick.Left);
                    Wait(WAIT_MS);
                    NS.Down(Keys.RStick.Left);
                    Wait(WAIT_MS);
                    NS.Down(Keys.HAT.Left);
                    Wait(WAIT_MS);
                }
                // change date
                NS.Up(Keys.A);
                NS.Down(Keys.LStick.Up);
                Wait(WAIT_MS);
                NS.Down(Keys.RStick.Right);
                Wait(WAIT_MS);
                NS.Down(Keys.LStick.Right);
                Wait(WAIT_MS);
                NS.Down(Keys.HAT.Right);
                Wait(WAIT_MS);
                if (!VideoCapture.Match(45, 478, Color.FromArgb(105, 105, 105), DefaultColorCap))
                {
                    // reset
                    Reset();
                    Wait(300);
                    Press(Keys.HAT.Down);
                    Wait(100);
                    Press(Keys.HAT.Down);
                    Wait(100);
                    reset++;
                    continue;
                }
                NS.Down(Keys.A);
                Wait(WAIT_MS);
                NS.Reset();
                Wait(200);
                return reset;
            }
        }

        public void ChangeDate(int days = 1, bool resetdate = false)
        {
            Log($"SwitchOS.ChangeDate({days},{resetdate})");
            const int WAIT_MS = 100;
            Press(Keys.HAT.Down);
            Wait(WAIT_MS);
            Press(Keys.HAT.Right);
            Wait(WAIT_MS);
            Press(Keys.HAT.Right);
            Wait(WAIT_MS);
            Press(Keys.HAT.Right);
            Wait(WAIT_MS);
            Press(Keys.HAT.Right);
            Wait(WAIT_MS);
            Press(Keys.A);
            Wait(1500);
            Press(Keys.HAT.Down, 2000);
            Wait(100);
            Press(Keys.HAT.Right);
            Wait(WAIT_MS);
            Press(Keys.HAT.Down);
            Wait(WAIT_MS);
            Press(Keys.HAT.Down);
            Wait(WAIT_MS);
            Press(Keys.HAT.Down);
            Wait(WAIT_MS);
            Press(Keys.HAT.Down);
            Wait(WAIT_MS);
            Press(Keys.A);
            Wait(500);
            var sync = VideoCapture.Match(1510, 240, ImageRes.Get("os_synctime"), DefaultImageCap, VideoCapture.LineSampler(3));
            if (sync)
            {
                Press(Keys.A);
                Wait(500);
            }
            else if (!sync && resetdate)
            {
                Press(Keys.A);
                Wait(500);
                Press(Keys.A);
                Wait(500);
            }
            Press(Keys.HAT.Down);
            Wait(WAIT_MS);
            Press(Keys.HAT.Down);
            Wait(WAIT_MS);
            for (int i = 0; i < days; i++)
                ChangeDateCore();
        }

        public bool CheckInput()
        {
            Log($"SwitchOS.CheckInput()");
            return VideoCapture.Match(1530, 600, ImageRes.Get("os_input"), DefaultImageCap, VideoCapture.LineSampler(3));
        }

        public void InputNumber(long number, int length)
        {
            Log($"SwitchOS.InputNumber({number},{length})");
            int x0 = 0, y0 = 0;
            int[] digit = new int[length];
            for (int i = 0; i < length; i++)
            {
                digit[length - 1 - i] = (int)(number % 10);
                number /= 10;
            }
            List<NintendoSwitch.Key> keys = new List<NintendoSwitch.Key>();
            for (int i = 0; i < length; i++)
            {
                int d = digit[i] != 0 ? digit[i] - 1 : 10;
                int x1 = d % 3;
                int y1 = d / 3;
                if (y1 == 3)
                    x0 = x1;
                for (; y0 > y1; y0--)
                    keys.Add(Keys.HAT.Up);
                for (; y0 < y1; y0++)
                    keys.Add(Keys.HAT.Down);
                for (; x0 > x1; x0--)
                    keys.Add(Keys.HAT.Left);
                for (; x0 < x1; x0++)
                    keys.Add(Keys.HAT.Right);
                keys.Add(Keys.A);
            }
            keys.Add(Keys.PLUS);
            foreach (var key in keys)
            {
                Press(key);
                Wait(200);
            }
        }

        public int GetProfileCursor()
        {
            Log($"SwitchOS.GetProfileCursor()");
            for (int i = 0; i < 6; i++)
                if (VideoCapture.Match(135, 240 + i * 105, Color.FromArgb(55, 72, 230), DefaultColorCap))
                    return i;
            return -1;
        }

        public bool NoFriendRequest()
        {
            Log($"SwitchOS.NoFriendRequest()");
            return VideoCapture.Match(805, 525, ImageRes.Get("os_nofriendrequest"), DefaultImageCap, VideoCapture.LineSampler(3));
        }

        public void AcceptFriends(int accountindex = 1)
        {
            Log($"SwitchOS.AcceptFriends()");
            if (accountindex <= 0)
                return;
            Press(Keys.HOME);
            Wait(1000);
            Press(Keys.HAT.Up);
            Wait(500);
            for (int i = 0; i < accountindex - 1; i++)
            {
                Press(Keys.HAT.Right);
                Wait(500);
            }
            Press(Keys.A);
            Wait(2500);
            if (VideoCapture.Match(1210, 685, ImageRes.Get("os_disconnect"), DefaultImageCap, VideoCapture.LineSampler(3)))
            {
                Press(Keys.A);
                Wait(500);
            }
            int cur = -1;
            WaitUntil(Keys.A, 5, () => (cur = GetProfileCursor()) != -1);
            while (cur < 4)
            {
                Press(Keys.HAT.Down);
                Wait(300);
                cur++;
            }
            while (cur > 4)
            {
                Press(Keys.HAT.Up);
                Wait(300);
                cur--;
            }
            PressUntil(Keys.A, 300, NoFriendRequest, TimeoutThrow(30));
            Wait(300);
            Press(Keys.HOME);
            Wait(1500);
            Press(Keys.A);
            WaitUntil(Keys.A, 10, Module<Idle>().Check);
            Wait(500);
        }
    }
}
