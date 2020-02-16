using PokemonTycoon.Graphic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PokemonTycoon.Scripts
{
    class MaxHunt : Script
    {
        public const int FramesToSave = 100;

        public class TargetPM : IComparable<TargetPM>
        {
            public int ID = -1;
            public char Variance = '\0';

            public TargetPM(int id, char variance)
            {
                ID = id;
                Variance = variance;
            }

            public int CompareTo(TargetPM other)
            {
                var n = ID.CompareTo(other.ID);
                if (n != 0)
                    return n;
                return Variance.CompareTo(other.Variance);
            }

            public override string ToString()
            {
                return Shadow.GetString(ID, Variance);
            }
        }

        public class Target
        {
            public List<TargetPM> Pokemons;
            public bool[] Stars;

            public Target()
            {
                Pokemons = new List<TargetPM>();
                Stars = new bool[6];
                for (int i = 0; i < Stars.Length; i++)
                    Stars[i] = true;
            }

            public Target(List<TargetPM> pokemons)
            {
                Pokemons = pokemons;
                Stars = new bool[6];
                for (int i = 0; i < Stars.Length; i++)
                    Stars[i] = true;
            }
        }

        class Vars
        {
            public int[] StarCount = new int[6];

            public Vars()
            {
                for (int i = 0; i < StarCount.Length; i++)
                    StarCount[i] = 0;
            }
        }

        public class DenArgs
        {
            public Target Target = null;
            public int Days = 0;
            public bool Auto = false;
            public bool AutoBattle = false;
            public bool AutoCatch = false;

        }

        Vars _vars = new Vars();

        void UpdateSummary()
        {
            var str = new StringBuilder();
            for (int i = 1; i <= 5; i++)
            {
                if (_vars.StarCount[i] == 0)
                    continue;
                if (str.Length > 0)
                    str.Append("，");
                str.Append($"[{i}★] {_vars.StarCount[i]}个");
            }
            Summary = str.ToString();
        }

        bool CheckTarget(Target target, bool print = true)
        {
            try
            {
                if (!print)
                    LogEnabled = false;
                var stars = Module<Battle.Raid>().GetStars();
                _vars.StarCount[stars]++;
                var shadows = VideoCapture.GetMatchedShadows();
                UpdateSummary();
                Msg(new string('★', stars));
                if (target != null && target.Stars[stars] == false)
                {
                    Msg(Colors.Fail, "星数不匹配");
                    return false;
                }
                bool success = false;
                if (shadows.Length == 0)
                {
                    Msg("#??? 未知剪影");
                    success = target != null && target.Pokemons.Any(u => u.ID == 0);
                }
                else
                {
                    StringBuilder message = new StringBuilder();
                    foreach (var shadow in shadows)
                    {
                        if (message.Length > 0)
                            message.Append(" / ");
                        message.Append(shadow);
                        if (target != null && target.Pokemons.Any(u => u.ID == shadow.ID && u.Variance == shadow.Variance))
                            success = true;
                    }
                    Msg(message);
                }
                if (success)
                {
                    Msg(Colors.Success, "已找到目标");
                    return true;
                }
                else if (target != null && target.Pokemons.Count == 0)
                    return true;
                if (target != null)
                    Msg(Colors.Fail, "剪影不匹配");
                return false;
            }
            finally
            {
                if (!print)
                    LogEnabled = true;
            }
        }

        public void Next(bool restart, bool save)
        {
            Log($"MaxHunt.Next()");
            if (restart)
            {
                Msg($"正在重启");
                Module<SwitchOS>().RestartGame();
                Wait(300);
                Module<Battle.Raid>().OpenRaidMenu();
            }
            Msg($"正在修改日期");
            Press(Keys.A);
            WaitUntil(Keys.A, 5, () => Module<Battle.Raid>().CheckSeatEmpty(1), TimeoutThrow(20));
            Wait(500);
            Module<SwitchOS>().GoToMenu();
            Module<SwitchOS>().ChangeDate(1);
            Press(Keys.HOME);
            Wait(2000);
            Press(Keys.A);
            WaitUntil(() => Module<Battle.Raid>().CheckSeatEmpty(1), TimeoutThrow(20));
            Press(Keys.B);
            Wait(1000);
            Press(Keys.A);
            WaitUntil(Module<Idle>().Check, TimeoutThrow(20));
            if (save)
            {
                Msg($"正在保存");
                Module<Menu>().Save();
            }
            Module<Battle.Raid>().OpenRaidMenu();
        }

        public void Leap(DenArgs args, bool restart)
        {
            if (!args.Auto)
                Light(Taskbar.Progress);
            while (true)
            {
                if (restart)
                {
                    Msg($"重启游戏");
                    Module<SwitchOS>().RestartGame();
                    Wait(300);
                }
                if (args.Days == 0)
                    return;
                Msg($"开始推移");
                Module<Battle.Raid>().OpenRaidMenu();
                bool retry = false;
                for (int i = 0; i < args.Days; i++)
                {
                    Msg(Colors.Start, $"第{i + 1}次");
                    if (!args.Auto)
                        Progress = (double)(i + 1) / args.Days;
                    Next(false, false);
                    if (i == args.Days - 1 && args.Auto)
                    {
                        var success = CheckTarget(args.Target);
                        retry = !success;
                        if (success && args.AutoBattle)
                            AutoBattle(args);
                    }
                    else
                        CheckTarget(null);
                }
                Msg($"推移完毕");
                if (retry)
                    restart = true;
                else
                    break;
            }
        }

        public void AutoBattle(DenArgs args)
        {
            // start battle
            Press(Keys.HAT.Down);
            Wait(300);
            Press(Keys.A);
            Wait(500);
            Msg($"开始战斗");
            PressUntil(Keys.A, 500, Module<Battle.Raid>().CheckCatch);
            Wait(3000);

            // catch
            Msg($"战斗完成，开始捕获");
            Press(Keys.A);
            Wait(800);
            Press(Keys.A);
            WaitUntil(Module<Battle.Raid>().CheckEnd, TimeoutThrow(60));

            // end battle
            Msg($"战斗结束");
            Wait(300);
            Press(Keys.A);
            Wait(300);
            PressUntil(Keys.B, 300, Module<Idle>().Check);

            if (!args.AutoCatch)
                return;

            // feed candy
            Msg($"正在喂糖");
            Module<Menu>().Open();
            Wait(300);
            Module<Menu>().OpenEntry(Menu.Entry.Bag);
            WaitUntil(Module<Bag>().Check);
            Wait(500);
            Module<Bag>().SelectCatagory(Bag.Catagory.OtherItems);
            Wait(300);
            for (int i = 0; i < 6; i++)
            {
                Press(Keys.HAT.Down);
                Wait(200);
            }
            Press(Keys.A);
            Wait(500);
            Press(Keys.A);
            Wait(500);
            Press(Keys.HAT.Up);
            Wait(500);
            Press(Keys.A);
            Wait(500);
            Press(Keys.HAT.Down);
            Wait(500);
            Press(Keys.A);
            Wait(500);
            Msg($"正在升级");
            PressUntil(Keys.B, 300, Module<Idle>().Check, Module<Menu>().Check, () =>
            {
                if (VideoCapture.Match(1560, 729, Color.FromArgb(13, 13, 13), DefaultColorCap) && VideoCapture.Match(1579, 729, Color.FromArgb(254, 254, 254), DefaultColorCap))
                {
                    // ignore new skill
                    Press(Keys.HAT.Down);
                    Wait(300);
                    Press(Keys.A);
                    Wait(300);
                }
                return false;
            });

            // take picture
            Msg($"准备截图");
            Wait(300);
            Module<Menu>().Open();
            Wait(300);
            Module<Menu>().OpenEntry(Menu.Entry.Pokelist);
            WaitUntil(Keys.A, 10, Module<Pokelist>().Check);
            Wait(200);
            Press(Keys.HAT.Up);
            Wait(500);
            Press(Keys.A);
            Wait(500);
            Press(Keys.A);
            Wait(2000);
            Press(Keys.HAT.Right);
            Wait(500);
            Msg($"截图1");
            ScreenShot("");
            Press(Keys.HAT.Right);
            Wait(500);
            Press(Keys.HAT.Right);
            Wait(500);
            Msg($"截图2");
            ScreenShot("");
        }

        public void RunSearch(Target target)
        {
            Msg(Colors.Highlight, $"开始刷坑");
            Module<Battle.Raid>().OpenRaidMenu();
            for (int i = 0; ; i++)
            {
                Msg(Colors.Start, $"第{i + 1}次");
                Next(false, false);
                if (CheckTarget(target))
                    return;
            }
        }

        public void RunLeap(DenArgs args)
        {
            Msg(Colors.Highlight, $"推移");
            Leap(args, false);
        }

        public void RunRestartLeap(DenArgs args)
        {
            Msg(Colors.Highlight, $"重推");
            Leap(args, true);
        }

        public void RunNG(DenArgs args)
        {
            Msg(Colors.Highlight, $"NG+1");
            Next(true, false);
            PressUntil(Keys.B, 1000, Module<Idle>().Check);
            Msg($"正在保存");
            Module<Menu>().Save();
            Wait(300);
            Module<Battle.Raid>().OpenRaidMenu();
            if (args.Auto)
                Leap(args, false);
        }

        public void RunAutoBattle(DenArgs args)
        {
            Msg(Colors.Highlight, $"自动战斗");
            AutoBattle(args);
        }

        public void RunSkip(DenArgs args, bool fast = false)
        {
            Light(Taskbar.Progress);
            Progress = 0;
            if (WaitUntil(2, Module<Idle>().Check) == null)
                throw new ScriptException("必须从游戏主界面开始");
            Msg(Colors.Start, $"进入日期设置");
            Press(Keys.HOME);
            Wait(1500);
            Module<SwitchOS>().ChangeDate(0, false);
            if (fast)
            {
                int reset = 0;
                bool first = true;
                for (int i = 0; i < args.Days; i++)
                {
                    Msg($"第{i + 1}次");
                    var r = Module<SwitchOS>().ChangeDateCoreFast(first);
                    first = false;
                    if (r > 0)
                        Msg(Colors.Fail, "已复位");
                    reset += r;
                    //Msg(Colors.Trivial, date.ToString("yyyy.MM.dd"));
                    Progress = (double)i / args.Days;
                    // summary
                    var v = (i + 1) / (DateTime.Now - StartTime).TotalHours;
                    Summary = $"效率={v:0.00}/h，复位次数={reset}";
                }
            }
            else
            {
                for (int i = 0; i < args.Days; i++)
                {
                    if (i > 0 && i % FramesToSave == 0)
                    {
                        // save
                        Light(Taskbar.Highlight);
                        Msg(Colors.Start, $"已达{FramesToSave}帧，保存游戏");
                        Press(Keys.HOME);
                        Wait(2000);
                        Press(Keys.A);
                        Wait(3000);
                        if (WaitUntil(10, Module<Idle>().Check) == null)
                            throw new ScriptException("返回游戏失败，可能已崩溃");
                        //ScreenShot($"Save {i}");
                        Wait(300);
                        Module<Menu>().Save();
                        Press(Keys.HOME);
                        Wait(1500);
                        Module<SwitchOS>().ChangeDate(0);
                        //ScreenShot($"Saved");
                        Light(Taskbar.Progress);
                    }
                    Msg($"第{i + 1}帧");
                    Module<SwitchOS>().ChangeDateCore();
                    //ScreenShot($"Frame {i}");
                    Progress = (double)i / args.Days;
                    // summary
                    var v = (i + 1) / (DateTime.Now - StartTime).TotalHours;
                    Summary = $"效率={v:0.00}/h";
                }
            }
        }

        public class RaidArgs
        {
            public Target Target;
            public int Code;
            public int AccountIndex;
            public int Countdown;
            public int SoftResetCount;
            public int HardResetDays;
            public bool Single;
            public bool Restart;
        }

        public void RunCreateRaid(RaidArgs args)
        {
            Msg(Colors.Highlight, "无限开车");
            DenArgs den = new DenArgs();
            den.Target = args.Target;
            den.Days = args.HardResetDays;
            den.Auto = true;
            if (args.Restart)
                Leap(den, true);
            int softreset = 0;
            // raid loop
            while (true)
            {
                // timeout loop
                while (true)
                {
                    // reset
                    PressUntil(Keys.B, 300, Module<Idle>().Check);

                    // connect to internet
                    Wait(1500);
                    if (!Module<Idle>().CheckConnected().Boolean)
                    {
                        Msg($"连接互联网");
                        Press(Keys.Y);
                        Wait(1500);
                        Press(Keys.PLUS);
                        Wait(500);
                        PressUntil(Keys.B, 300, Module<Idle>().Check);
                        Wait(300);
                    }

                    // create raid
                    Msg(Colors.Start, $"建房间");
                    Module<Battle.Raid>().OpenRaidMenu();
                    if (WaitUntil(10, Module<Battle.Raid>().CheckMenuCode) == null)
                    {
                        Capture();
                        ScreenShot("CheckCode Failed");
                        continue;
                    }
                    Wait(1000);
                    if (args.Code >= 0)
                    {
                        // set link code
                        Msg($"设置密码：{args.Code:0000}");
                        Press(Keys.PLUS);
                        if (WaitUntil(Keys.PLUS, 2, Module<SwitchOS>().CheckInput, Timeout(20)) != Module<SwitchOS>().CheckInput)
                        {
                            Capture();
                            ScreenShot("CheckInput Failed");
                            continue;
                        }
                        Wait(200);
                        Module<SwitchOS>().InputNumber(args.Code, 4);
                        Wait(800);
                        Press(Keys.A);
                        Wait(1000);
                    }
                    Press(Keys.A);

                    // wait for party
                    Msg($"等待乘客");
                    bool[] seated = new bool[4];
                    bool[] ready = new bool[4];
                    for (int i = 0; i < 4; i++)
                    {
                        seated[i] = false;
                        ready[i] = false;
                    }
                    var starttime = DateTime.MaxValue;
                    bool timeout = false;
                    bool started = false;
                    WaitUntil(Keys.A, 5, () => Module<Battle.Raid>().CheckSeatEmpty(1), TimeoutThrow(20));
                    while (true)
                    {
                        Wait(1000);

                        if (Module<SwitchOS>().CheckBlackScreen())
                        {
                            Msg(Colors.Fail, $"时间到自动发车");
                            started = true;
                            break;
                        }
                        if (Module<Battle.Raid>().CheckTimeout())
                        {
                            // 3 minutes have passed
                            Msg(Colors.Fail, $"3分钟已过，重新开车");
                            PressUntil(Keys.B, 500, Module<Idle>().Check);
                            Wait(500);
                            timeout = true;
                            break;
                        }

                        // check seats
                        var ns = 0;
                        var nr = 0;
                        for (int i = 1; i < Battle.Raid.MAX_MEMBERS; i++)
                        {
                            var tr = Module<Battle.Raid>().CheckSeatReady(i);
                            if (tr)
                            {
                                if (!ready[i])
                                    Msg(Colors.Success, $"{i}号乘客已就绪");
                                nr++;
                            }
                            ready[i] = tr;
                            var ts = tr || !Module<Battle.Raid>().CheckSeatEmpty(i);
                            if (ts)
                            {
                                if (!seated[i])
                                    Msg($"{i}号乘客已上车");
                                ns++;
                            }
                            seated[i] = ts;
                        }
                        if (ns > nr)
                            starttime = DateTime.MaxValue;
                        else if (ns > 0 && ns == nr && starttime == DateTime.MaxValue)
                            starttime = DateTime.Now + TimeSpan.FromSeconds(args.Countdown);
                        if (nr == 3 || DateTime.Now >= starttime)
                            break;
                    }

                    if (timeout)
                    {
                        // time out
                        if (args.Single)
                            return;
                        if (args.SoftResetCount == 0 && args.HardResetDays >= 3)
                            break;

                        // check friend request
                        Msg($"接受好友申请");
                        Module<SwitchOS>().AcceptFriends(args.AccountIndex);
                    }
                    else
                    {
                        // start raid
                        Msg(Colors.End, $"出发");
                        if (!started)
                        {
                            Press(Keys.HAT.Up);
                            Wait(500);
                        }
                        int blackscreen = 0;
                        DateTime time = DateTime.MinValue;
                        Func<bool> check_blackscreen = () =>
                        {
                            if (Module<SwitchOS>().CheckBlackScreen())
                            {
                                blackscreen++;
                                return false;
                            }
                            else if (blackscreen >= 1)
                            {
                                if (time == DateTime.MinValue)
                                    time = DateTime.Now.AddSeconds(2.5);
                                return DateTime.Now >= time;
                            }
                            return false;
                        };
                        DateTime time2 = DateTime.MaxValue;
                        Func<bool> check_leavemenu = () =>
                        {
                            if (blackscreen == 0 && !Module<Battle.Raid>().CheckMenu())
                            {
                                if (time2 == DateTime.MaxValue)
                                    time2 = DateTime.Now.AddSeconds(3);
                            }
                            else
                                time2 = DateTime.MaxValue;
                            return blackscreen == 0 && DateTime.Now >= time2;
                        };
                        var r = PressUntil(Keys.A, 300, check_blackscreen, check_leavemenu, Module<SwitchOS>().CheckSystemMessage, Module<Idle>().Check, TimeoutThrow(30));
                        if (r != check_blackscreen)
                        {
                            Msg(Colors.Fail, $"卡门了");
                            Log(r.GetName());
                            continue;
                        }
                        break;
                    }
                }

                if (softreset < args.SoftResetCount || args.SoftResetCount < 0)
                {
                    softreset++;
                    Msg(Colors.Start, $"断网重置");
                    Press(Keys.HOME, 1000);
                    Wait(100);
                    Press(Keys.A);
                    Wait(2500);
                    {
                        var t = Timeout(20);
                        var rt = PressUntil(Keys.HOME, 50, t, () => !VideoCapture.Match(1900, 10, Color.FromArgb(0, 0, 0), DefaultColorCap));
                        if (rt == t)
                        {
                            //throw new ScriptException("唤醒失败");
                            Msg(Colors.Error, "唤醒失败，请手动开机");
                            Beep(Sounds.Error);
                        }
                    }
                    var r = PressUntil(Keys.B, 300, Module<SwitchOS>().Check, Module<Idle>().Check);
                    if (r == Module<SwitchOS>().Check)
                    {
                        Wait(500);
                        Press(Keys.A);
                        Wait(500);
                        var rt = PressUntil(Keys.B, 300, Module<Battle.Raid>().CheckCatch, Module<Idle>().Check);
                        if (rt == Module<Battle.Raid>().CheckCatch)
                            throw new ScriptException("炸车了");
                    }
                }
                else
                {
                    softreset = 0;
                    Leap(den, true);
                    PressUntil(Keys.B, 300, Module<Idle>().Check);
                }
                if (args.Single)
                    return;

                // check friend request
                Msg($"接受好友申请");
                Module<SwitchOS>().AcceptFriends(args.AccountIndex);
            }
        }

        public void RunGetRareDen()
        {
            Msg(Colors.Highlight, "SL紫光柱");
            while (true)
            {
                Press(Keys.A);
                Wait(3300);
                Press(Keys.A);
                Wait(2500);
                Press(Keys.A);
                Wait(1300);

                Battle.Raid.BeamColor c;
                try
                {
                    VideoCapture.Freeze();
                    Press(Keys.HOME);
                    c = Module<Battle.Raid>().GetBeamColor();
                }
                finally
                {
                    VideoCapture.Unfreeze();
                }

                if (c == Battle.Raid.BeamColor.Red)
                {
                    // red beam
                    Msg(Colors.Fail, "红光柱，重新读档");
                    Wait(1000);
                    Module<SwitchOS>().RestartGame();
                    Wait(500);
                    continue;
                }
                else if (c == Battle.Raid.BeamColor.Purple)
                {
                    // purple beam
                    Msg(Colors.Success, "成功获得紫光柱");
                    Wait(1000);
                    Press(Keys.HOME);
                    return;
                }
                else
                {
                    ScreenShot("RareDen Failed");
                    throw new ScriptException("光柱检测失败");
                }
            }
        }
    }
}
