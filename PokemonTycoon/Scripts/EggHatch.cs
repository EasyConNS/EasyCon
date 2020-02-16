using PTDevice;
using PokemonTycoon.Graphic;
using PokemonTycoon.Scripts.PokemonFilter;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PokemonTycoon.Scripts
{
    public class EggHatch : Script
    {
        internal enum ListState
        {
            [Description("带队")]
            Sitter,
            [Description("蛋")]
            Egg,
            [Description("精灵")]
            Pokemon,
        }

        public override double Progress => _vars.MaxCount > 0 ? _vars.PassedCount / (double)_vars.MaxCount : 0;

        void UpdateSummary()
        {
            var str = new StringBuilder();
            str.AppendLine($"筛选条件：{_vars.FilterDesc}");
            str.Append($"已孵化{_vars.HatchedCount}个蛋");
            if (!_vars.NoFilter)
            {
                if (_vars.BatchMode)
                    str.Append($"（{_vars.BatchCount}只待检查）");
                str.Append($"，获得合格宝可梦{_vars.PassedCount}只，闪光{ _vars.ShinyCount}只");
                foreach (var pair in _vars.ExCount)
                    str.Append($"，{(pair.Key.Length == 0 ? "特殊计数" : pair.Key)}{pair.Value}只");
            }
            str.AppendLine();
            double rh = 0;
            if (DateTime.Now > StartTime)
                rh = _vars.HatchedCount / (DateTime.Now - StartTime).TotalMinutes;
            str.Append($"效率={rh:0.00}/min");
            double rp = 0;
            if (_vars.HatchedCount > 0)
                rp = (double)_vars.PassedCount / _vars.HatchedCount;
            if (!_vars.NoFilter)
                str.Append($"，产出率={rp * 100:0.0}%");
            Summary = str.ToString();
        }

        public bool HasEgg()
        {
            Log($"EggHatch.HasEgg()");
            return VideoCapture.Match(710, 900, ImageRes.Get("egg_hasegg"), DefaultImageCap, VideoCapture.LineSampler(3));
        }

        public bool NoEgg()
        {
            Log($"EggHatch.NoEgg()");
            return VideoCapture.Match(420, 900, ImageRes.Get("egg_noegg"), DefaultImageCap, VideoCapture.LineSampler(3));
        }

        public bool EggToList()
        {
            Log($"EggHatch.EggToList()");
            return VideoCapture.Match(550, 890, ImageRes.Get("egg_tolist"), DefaultImageCap, VideoCapture.LineSampler(3));
        }

        public bool EggToBox()
        {
            Log($"EggHatch.EggToBox()");
            return VideoCapture.Match(545, 890, ImageRes.Get("egg_tobox"), DefaultImageCap, VideoCapture.LineSampler(3));
        }

        public bool Hatching()
        {
            Log($"EggHatch.Hatched()");
            return VideoCapture.Match(415, 895, ImageRes.Get("egg_hatched"), DefaultImageCap, VideoCapture.LineSampler(3));
        }

        void PrintPokeList()
        {
            string[] s = new string[6];
            System.Drawing.Color[] c = new System.Drawing.Color[6];
            for (int i = 0; i < 6; i++)
            {
                if (i >= _vars.Pokelist.Count)
                {
                    s[i] = "空";
                    c[i] = Colors.Trivial;
                }
                else
                {
                    s[i] = _vars.Pokelist[i].GetDesc();
                    switch (_vars.Pokelist[i])
                    {
                        case ListState.Egg:
                            c[i] = Colors.Success;
                            break;
                        case ListState.Pokemon:
                            c[i] = Colors.Fail;
                            break;
                        default:
                            c[i] = Colors.Default;
                            break;
                    }
                }
            }
            List<object> list = new List<object>();
            list.Add("宝可梦 = ");
            for (int i = 0; i < 6; i++)
            {
                if (i > 0)
                {
                    list.Add(Colors.Default);
                    list.Add(" , ");
                }
                list.Add(c[i]);
                list.Add(s[i]);
            }
            Msg(list.ToArray());
        }

        void UpdatePokeList()
        {
            _vars.Pokelist.Clear();
            Module<Pokelist>().CheckSlot().ForEach(item =>
            {
                if (item != Pokelist.SlotState.Empty)
                    _vars.Pokelist.Add(ListState.Pokemon);
            });
            _vars.Pokelist[0] = ListState.Sitter;
            Module<Pokelist>().CheckEggs().ForEach((i, item) =>
            {
                if (item)
                    _vars.Pokelist[i] = ListState.Egg;
            });
            PrintPokeList();
        }

        internal enum MovePhase
        {
            P1_Left,
            P2_Right,
            P3_Right,
            P4_Left,
            P5_Right,

            // relocate
            RL_Right,
            RL_Up,
        }

        const int MoveInterval = 150;

        internal class Vars : Filter.Vars
        {
            // inherit
            public override int Count => PassedCount;

            // settings
            internal PokemonFilter.Filter Filter;
            internal bool NoFilter;
            internal string FilterDesc;
            internal int MaxCount;
            internal bool ShinyEgg;
            internal bool BatchMode;

            // statistics
            internal int HatchedCount;
            internal int PassedCount;
            internal int ShinyCount;
            internal int BatchCount;
            internal bool[] BatchChecked;

            // local for Run()
            internal List<ListState> Pokelist = new List<ListState>();
            internal DateTime ResetTimer;
            internal int ErrorCount = 0;
            internal bool Moving;
            internal MovePhase MovePhase;
            internal DateTime MoveStopTime;
            internal int MoveRemainTime;
            internal int P1TimeCorrection = 0;
        }

        Vars _vars = new Vars();

        const int DefaultTimeout = 180;

        void InitList(bool quit = true)
        {
            Module<Menu>().Open();
            Wait(200);
            Module<Menu>().OpenEntry(Menu.Entry.Pokelist);
            WaitUntil(Module<Pokelist>().Check);
            Wait(200);
            UpdatePokeList();
            if (quit)
                PressUntil(Keys.B, 1000, Module<Idle>().Check);
        }

        void CheckSave()
        {
            if (_vars.ShinyEgg && _vars.HatchedCount % 5 == 0)
            {
                Module<Menu>().Save();
                Msg(Colors.Start, "游戏已保存");
            }
        }

        void Move()
        {
            Func<NintendoSwitch.Key, int, bool> move = (key, time) =>
            {
                if (_vars.MoveRemainTime != 0)
                {
                    // resume
                    if (!_vars.Moving)
                        Msg(Colors.Trivial, "继续移动");
                    else
                        Log("继续移动");
                    _vars.Moving = true;
                    Press(key, -1);
                    _vars.MoveStopTime = DateTime.Now + TimeSpan.FromMilliseconds(_vars.MoveRemainTime);
                    _vars.MoveRemainTime = 0;
                    Wait(MoveInterval);
                    return true;
                }
                else if (_vars.MoveStopTime > DateTime.Now)
                {
                    // continue
                    //Log("移动");
                    Wait(MoveInterval);
                    return true;
                }
                else if (_vars.MoveStopTime == DateTime.MinValue)
                {
                    // phase start
                    if (!_vars.Moving)
                        Msg(Colors.Trivial, "开始移动");
                    else
                        Log("开始新阶段移动");
                    _vars.Moving = true;
                    Press(key, -1);
                    _vars.MoveStopTime = DateTime.Now + TimeSpan.FromMilliseconds(time);
                    Wait(MoveInterval);
                    return true;
                }
                else
                {
                    // phase end
                    Log("终止移动");
                    _vars.MoveStopTime = DateTime.MinValue;
                    return false;
                }
            };
            Log(_vars.MovePhase.GetName(), " ", (int)(_vars.MoveStopTime - DateTime.Now).TotalMilliseconds, " ", _vars.MoveRemainTime);
            switch (_vars.MovePhase)
            {
                case MovePhase.P1_Left:
                    if (_vars.P1TimeCorrection > 500)
                        _vars.P1TimeCorrection = 500;
                    if (!move(NintendoSwitch.Key.LStick(180), 1500 - _vars.P1TimeCorrection))
                        StartMoving(MovePhase.P2_Right);
                    break;
                case MovePhase.P2_Right:
                    if (!move(NintendoSwitch.Key.LStick(45), 3000))
                    {
                        StartMoving(MovePhase.P3_Right);
                        if (NeedEgg())
                            TryGetEgg();
                    }
                    break;
                case MovePhase.P3_Right:
                    if (!move(NintendoSwitch.Key.LStick(0d), 6000))
                        StartMoving(MovePhase.P4_Left);
                    break;
                case MovePhase.P4_Left:
                    if (!move(NintendoSwitch.Key.LStick(180), 6700))
                        StartMoving(MovePhase.P5_Right);
                    break;
                case MovePhase.P5_Right:
                    if (!move(NintendoSwitch.Key.LStick(45), 2500))
                    {
                        StartMoving(MovePhase.P3_Right);
                        if (NeedEgg())
                            TryGetEgg();
                    }
                    break;
                case MovePhase.RL_Right:
                    if (!move(NintendoSwitch.Key.LStick(0d), 3500))
                        StartMoving(MovePhase.RL_Up);
                    break;
                case MovePhase.RL_Up:
                    if (!move(NintendoSwitch.Key.LStick(90), 5500))
                        StartMoving(MovePhase.P1_Left);
                    break;
            }
        }

        void StopMoving(bool pause = true, bool send = false)
        {
            Reset();
            _vars.Moving = false;
            if (pause)
            {
                if (_vars.MoveRemainTime == 0)
                {
                    double correction;
                    if (_vars.MovePhase == MovePhase.P1_Left)
                        correction = MoveInterval * 1;
                    else
                        correction = MoveInterval * 3;
                    _vars.MoveRemainTime = (int)((_vars.MoveStopTime - DateTime.Now).TotalMilliseconds + correction).Clamp(0, int.MaxValue);
                }
            }
            else
            {
                _vars.MoveRemainTime = 0;
                _vars.MoveStopTime = DateTime.MinValue;
            }
        }

        void StartMoving(MovePhase phase)
        {
            _vars.MoveRemainTime = 0;
            _vars.MoveStopTime = DateTime.MinValue;
            _vars.MovePhase = phase;
        }

        bool NeedEgg()
        {
            return _vars.PassedCount < _vars.MaxCount && _vars.Pokelist.Count(u => u == ListState.Egg) < 5;
        }

        void TryGetEgg()
        {
            Msg("尝试取蛋");
            StopMoving();
            Press(Keys.A);
            Wait(500);
            if (HasEgg())
            {
                ResetTimeout();
                ClearError();
                Msg(Colors.Start, "领到一颗蛋");
                var f = PressUntil(Keys.A, 1000, EggToList, EggToBox);
                if (f == EggToList)
                {
                    Msg("自动加入队伍");
                    PressUntil(Keys.B, 500, Module<Idle>().Check);
                    _vars.Pokelist.Add(ListState.Egg);
                    PrintPokeList();
                }
                else
                {
                    Msg("替换队伍成员");
                    Press(Keys.A);
                    Wait(800);
                    Press(Keys.A);
                    WaitUntil(Keys.A, 5, Module<Pokelist>().Check);
                    UpdatePokeList();
                    var index = _vars.Pokelist.IndexOf(ListState.Pokemon);
                    if (index == -1)
                        index = 5;
                    Msg($"替换{index + 1}号精灵");
                    for (int i = 0; i < index; i++)
                    {
                        Press(Keys.HAT.Down);
                        Wait(300);
                    }
                    Press(Keys.A);
                    Wait(1000);
                    PressUntil(Keys.B, 500, Module<Idle>().Check);
                    _vars.Pokelist[index] = ListState.Egg;
                    PrintPokeList();
                    if (_vars.BatchMode)
                    {
                        _vars.BatchCount++;
                        UpdateSummary();
                        TryBatchRelease();
                    }
                }
            }
            else if (NoEgg())
            {
                ResetTimeout();
                ClearError();
                Msg("没出");
                PressUntil(Keys.B, 500, Module<Idle>().Check);
            }
            else
            {
                Msg("未知状况");
                Error();
            }
            if (NeedEgg())
                StartMoving(MovePhase.P1_Left);
            else
                StartMoving(MovePhase.P3_Right);
        }

        void ResetTimeout(int timeoutSec = DefaultTimeout)
        {
            _vars.ResetTimer = DateTime.Now + TimeSpan.FromSeconds(timeoutSec);
            _vars.ErrorCount = 0;
        }

        void Error()
        {
            _vars.ErrorCount += 1;
            if (_vars.ErrorCount >= 3)
                ResetTimeout(0);
        }

        void ClearError()
        {
            _vars.ErrorCount = 0;
        }

        bool IsTimeout()
        {
            return _vars.ResetTimer <= DateTime.Now;
        }

        void ResetLocation()
        {
            ScreenShot("ResetLocation");
            Msg(Colors.Fail, "重置初始位置");
            StopMoving(false);
            if (!Module<Menu>().Open())
                return;
            Wait(200);
            Module<Menu>().OpenEntry(Menu.Entry.Map);
            Wait(4000);
            Press(Keys.A);
            Wait(1000);
            Press(Keys.A);
            if (WaitUntil(15, Module<Idle>().Check) == null)
            {
                // in door
                Msg(Colors.Fail, "猜测在室内，尝试出门");
                PressUntil(Keys.B, 1000, Module<Idle>().Check);
                ScreenShot("In Door");
                Press(NintendoSwitch.Key.LStick(135), 1000);
                Press(NintendoSwitch.Key.LStick(270), 1000);
                Press(NintendoSwitch.Key.LStick(225), 3000);
                Press(NintendoSwitch.Key.LStick(315), 3000);
                Wait(300);
                Press(Keys.PLUS);
                return;
            }
            Wait(200);
            StartMoving(MovePhase.RL_Right);
            ResetTimeout(NeedEgg() ? 30 : DefaultTimeout);
        }

        void Hatched()
        {
            _vars.HatchedCount++;
            UpdateSummary();

            Msg(Colors.Start, "一个蛋孵化了");
            StopMoving(true, true);
            Wait(300);
            PressUntil(Keys.B, 500, Module<Idle>().Check);

            // check pokemon
            if (!_vars.NoFilter && !_vars.BatchMode)
            {
                // go to pokelist
                Msg("检查新孵化的精灵");
                Module<Menu>().Open();
                Wait(200);
                Module<Menu>().OpenEntry(Menu.Entry.Pokelist);
                WaitUntil(Keys.A, 10, Module<Pokelist>().Check);
                Wait(200);

                // get hatched pokemon index
                var list = Module<Pokelist>().CheckEggs();
                int index = -1;
                for (int i = 0; i < _vars.Pokelist.Count; i++)
                {
                    if (_vars.Pokelist[i] == ListState.Egg && list[i] == false)
                    {
                        index = i;
                        break;
                    }
                }
                Msg($"位于{index + 1}号位");
                _vars.Pokelist[index] = ListState.Pokemon;
                PrintPokeList();
                
                // check pokemon
                Press(Keys.R);
                WaitUntil(Keys.R, 10, Module<Pokebox>().Check);
                Wait(300);
                Press(Keys.HAT.Left);
                Wait(300);
                for (int i = 0; i < index; i++)
                {
                    Press(Keys.HAT.Down);
                    Wait(300);
                }
                Msg($"定位完毕，开始检测");
                bool shiny;
                var passed = Module<Pokebox>().CheckFilter(_vars.Filter, out shiny);
                if (shiny)
                {
                    Msg(Colors.Highlight, $"闪光！！！");
                    Light(Taskbar.Highlight);
                    _vars.ShinyCount++;
                    UpdateSummary();
                    Capture();
                    if (_vars.ShinyEgg)
                    {
                        Msg(Colors.Start, $"已获得闪光，停止运行");
                        return;
                    }
                }
                if (passed)
                {
                    Msg(Colors.Success, $"检测合格，留在队中等待替换入箱");
                    _vars.PassedCount++;
                    UpdateSummary();
                }
                else
                {
                    Msg(Colors.Fail, $"检测不合格，放生");
                    Module<Pokebox>().Release();
                    _vars.Pokelist.RemoveAt(index);
                }
                PrintPokeList();
            }
            else
            {
                // assume the first egg
                _vars.Pokelist[_vars.Pokelist.FindIndex(u => u == ListState.Egg)] = ListState.Pokemon;
            }

            // check hatched count
            if (_vars.PassedCount >= _vars.MaxCount && !_vars.Pokelist.Any(item => item == ListState.Egg))
            {
                Msg(Colors.Start, "已达最大孵蛋数量，结束运行");
                Done();
            }

            // done
            PressUntil(Keys.B, 500, Module<Idle>().Check, Module<Menu>().Check);
            CheckSave();
            PressUntil(Keys.B, 500, Module<Idle>().Check);
            ResetTimeout(NeedEgg() ? 30 : 60);
        }

        public void TryBatchRelease()
        {
            if (_vars.BatchCount + _vars.PassedCount < _vars.MaxCount)
                return;

            // check batched pokemons
            Msg(Colors.End, $"开始检查新一批宝可梦");
            Module<Menu>().Open();
            Wait(200);
            Module<Menu>().OpenEntry(Menu.Entry.Pokelist);
            WaitUntil(Keys.A, 10, Module<Pokelist>().Check);
            Wait(200);
            Press(Keys.R);
            WaitUntil(Keys.R, 10, Module<Pokebox>().Check);
            Wait(300);

            // checking
            int x = 0, y = 0;
            for (int i = 0; i < _vars.MaxCount; i++)
            {
                if (!_vars.BatchChecked[i])
                {
                    bool shiny;
                    var passed = Module<Pokebox>().CheckFilter(_vars.Filter, out shiny);
                    if (shiny)
                    {
                        Msg(Colors.Highlight, $"闪光！！！");
                        Light(Taskbar.Highlight);
                        _vars.ShinyCount++;
                    }
                    if (passed)
                    {
                        Msg(Colors.Success, $"检测合格，保留");
                        _vars.PassedCount++;
                        _vars.BatchChecked[i] = true;
                    }
                    else
                    {
                        Msg(Colors.Fail, $"检测不合格，放生");
                        Module<Pokebox>().Release();
                    }
                    _vars.BatchCount--;
                    UpdateSummary();
                }
                if (i < _vars.MaxCount - 1)
                    Module<Pokebox>().SelectNext(ref x, ref y);
            }

            // reset
            _vars.BatchCount = 0;
            for (int i = 0; i < (_vars.MaxCount - 1) / Pokebox.MAX_X / Pokebox.MAX_Y; i++)
            {
                Press(Keys.L);
                Wait(300);
            }
            PressUntil(Keys.B, 500, Module<Idle>().Check);
            ResetTimeout();
        }

        public void Run(string filterStr, int count, bool shinyegg, bool batchrelease)
        {
            Msg(Colors.Highlight, "自动孵蛋");
            try
            {
                _vars.Filter = Filter.Parse(filterStr);
                _vars.Filter.Init(_vars);
            }
            catch (FormatException ex)
            {
                throw new ScriptException(ex.Message);
            }
            _vars.NoFilter = _vars.Filter.Result == Bool3VL.True;
            _vars.FilterDesc = _vars.NoFilter ? "无 <高速模式>" : _vars.Filter.ToString();
            _vars.MaxCount = count;
            _vars.ShinyEgg = shinyegg;
            _vars.BatchMode = batchrelease;

            _vars.HatchedCount = 0;
            _vars.PassedCount = 0;
            _vars.ShinyCount = 0;
            _vars.BatchCount = 0;
            _vars.BatchChecked = new bool[_vars.MaxCount];
            for (int i = 0; i < _vars.MaxCount; i++)
                _vars.BatchChecked[i] = false;
            UpdateSummary();

            // check pokelist
            Msg("初始化宝可梦列表");
            InitList(false);
            Press(Keys.B);
            WaitUntil(Keys.B, 5, Module<Menu>().Check);
            CheckSave();
            PressUntil(Keys.B, 1000, Module<Idle>().Check);

            // start looping
            Msg("开始孵蛋");
            _vars.Moving = true;
            ResetTimeout();
            StartMoving(MovePhase.P3_Right);
            if (NeedEgg())
                TryGetEgg();
            while (true)
            {
                if (Module<Idle>().Check())
                {
                    if (IsTimeout())
                        ResetLocation();
                    else
                        Move();
                }
                else if (Hatching())
                    Hatched();
                else if (Module<Battle>().Check())
                {
                    Msg(Colors.Fail, "检测到战斗，准备逃跑");
                    StopMoving();
                    _vars.P1TimeCorrection += 100;
                    Module<Battle>().RunAway();
                    ResetLocation();
                }
                else
                {
                    Msg("未知状况，准备重试");
                    StopMoving();
                    Error();
                    if (IsTimeout())
                    {
                        ScreenShot("Unknown");
                        Press(Keys.B);
                    }
                    Wait(1000);
                }
            }
        }
    }
}
