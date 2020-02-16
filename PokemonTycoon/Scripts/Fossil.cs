using PokemonTycoon.Graphic;
using PokemonTycoon.Scripts.PokemonFilter;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PokemonTycoon.Scripts
{
    class Fossil : Script
    {
        class Var : Filter.Vars
        {
            public Filter Filter;
            public string FilterDesc;
            public bool NoFilter;
            public int Rounds;
            public int RoundCount;
            public int RoundMaxCount;
        }

        Var _vars = new Var();

        public bool DracozoltNormal()
        {
            Log($"Fossil.DracozoltNormal()");
            return VideoCapture.Match(927, 652, Color.FromArgb(249, 249, 169), DefaultColorCap);
        }

        public bool ArctozoltNormal()
        {
            Log($"Fossil.ArctozoltNormal()");
            return VideoCapture.Match(1041, 761, Color.FromArgb(37, 135, 179), DefaultColorCap);
        }

        public bool DracovishNormal()
        {
            Log($"Fossil.DracovishNormal()");
            return VideoCapture.Match(993, 740, Color.FromArgb(15, 119, 93), DefaultColorCap);
        }

        public bool ArctovishNormal()
        {
            Log($"Fossil.ArctovishNormal()");
            return VideoCapture.Match(806, 517, Color.FromArgb(68, 109, 168), DefaultColorCap);
        }

        void UpdateSummary()
        {
            var str = new StringBuilder();
            if (!_vars.NoFilter)
                str.AppendLine($"筛选条件：{_vars.FilterDesc}");
            str.Append($"第{_vars.Rounds + 1}轮，第({_vars.RoundCount}/{_vars.RoundMaxCount})次合成，总计合成{_vars.Count}次");
            if (_vars.Count > 0)
                str.Append($"，效率={_vars.Count / (DateTime.Now - StartTime).TotalMinutes:0.00}/min");
            Summary = str.ToString();
        }

        public void Run(int resetCount, string filterStr, int first, int second, bool doublecheck, bool movetobox)
        {
            Msg(Colors.Highlight, "自动合成化石");
            try
            {
                _vars.Filter = Filter.Parse(filterStr);
                _vars.Filter.Init(_vars);
            }
            catch (FormatException ex)
            {
                throw new ScriptException(ex.Message);
            }
            _vars.NoFilter = _vars.Filter.Result.IsKnown;
            _vars.FilterDesc = _vars.Filter.ToString();
            _vars.Count = 0;
            _vars.Rounds = 0;
            _vars.RoundCount = 0;
            _vars.RoundMaxCount = resetCount;
            Func<bool> isNormal = new Func<bool>[] {
                DracozoltNormal,
                ArctozoltNormal,
                DracovishNormal,
                ArctovishNormal,
            }[(first << 1) | second] ?? (() => true);

            // check empty slots
            Msg($"检查队伍");
            WaitUntil(Keys.B, 10, Module<Idle>().Check);
            Module<Menu>().Open();
            Wait(200);
            Module<Menu>().OpenEntry(Menu.Entry.Pokelist);
            WaitUntil(Module<Pokelist>().Check);
            Wait(200);
            int emptySlots = Module<Pokelist>().CheckSlot().Count(u => u == Pokelist.SlotState.Empty);
            if (emptySlots == 0)
                throw new ScriptException("队伍中必须有空位才能合成化石");
            Msg($"空位：{emptySlots}");
            PressUntil(Keys.B, 300, Module<Idle>().Check);

            // start
            bool[] firstcheck = new bool[Pokelist.MAX_LENGTH];
            while (true)
            {
                int k = 0;
                Msg(Colors.Start, $"合成化石");
                int max = Math.Min(emptySlots, _vars.RoundMaxCount - _vars.RoundCount);
                while (k < max)
                {
                    // combine fossil
                    Msg($"第({k + 1}/{max})只");
                    if (first > 0 || second > 0)
                    {
                        Press(Keys.A);
                        Wait(1200);
                        Press(Keys.A);
                        Wait(1200);
                        for (int i = 0; i < first; i++)
                        {
                            Press(Keys.HAT.Down, 100);
                            Wait(300);
                        }
                        if (second > 0)
                        {
                            Press(Keys.A);
                            Wait(1200);
                            for (int i = 0; i < second; i++)
                            {
                                Press(Keys.HAT.Down, 100);
                                Wait(300);
                            }
                        }
                    }
                    PressUntil(Keys.A, 300, Module<Dialog>().CheckGetPokemon);
                    var r = PressUntil(Keys.B, 300, Module<Idle>().Check, isNormal);
                    if (r == isNormal)
                    {
                        Msg(Colors.Trivial, $"似乎不是闪光");
                        firstcheck[k] = false;
                        PressUntil(Keys.B, 300, Module<Idle>().Check);
                    }
                    else
                    {
                        Msg(Colors.Highlight, $"似乎是闪光");
                        ScreenShot("Fossil");
                        firstcheck[k] = true;
                        Wait(1500);
                    }
                    _vars.Count++;
                    _vars.RoundCount++;
                    UpdateSummary();
                    k++;
                    if (firstcheck[k - 1])
                        break;
                }

                // check pokemon
                Msg(Colors.End, "检查新合成的宝可梦");
                Module<Menu>().Open();
                Wait(200);
                Module<Menu>().OpenEntry(Menu.Entry.Pokelist);
                WaitUntil(Module<Pokelist>().Check);
                Wait(200);
                Press(Keys.R);
                WaitUntil(Module<Pokebox>().Check);
                Wait(200);
                if (firstcheck[k - 1])
                {
                    Module<Pokebox>().SelectList(6 - emptySlots + k - 1);
                    Wait(1000);
                    Capture();
                }
                Module<Pokebox>().SelectList(6 - emptySlots);
                Wait(200);
                for (int p = 0; p < k; p++)
                {
                    Msg($"第({p + 1}/{k})只");
                    if ((!doublecheck && !firstcheck[p]) || !Module<Pokebox>().CheckFilter(_vars.Filter))
                    {
                        if (!movetobox && _vars.RoundCount < _vars.RoundMaxCount)
                        {
                            Msg(Colors.Fail, "不符合条件，放生");
                            Module<Pokebox>().Release();
                            Wait(500);
                        }
                        else
                        {
                            if (doublecheck)
                                Msg(Colors.Fail, "不符合条件");
                            else
                                Msg(Colors.Trivial, "跳过");
                            if (p + 1 < k)
                            {
                                Press(Keys.HAT.Down);
                                Wait(300);
                            }
                        }
                    }
                    else
                    {
                        Msg(Colors.Highlight, "符合条件，脚本结束");
                        Light(Taskbar.Highlight);
                        throw new ScriptDoneException();
                    }
                }
                

                // check reset
                if (_vars.RoundCount >= _vars.RoundMaxCount)
                {
                    Msg(Colors.End, $"本轮结束，重启游戏");
                    Module<SwitchOS>().RestartGame();
                    _vars.RoundCount = 0;
                    _vars.Rounds++;
                }
                else if (movetobox)
                {
                    Msg(Colors.End, $"移动入箱");
                    Module<Pokebox>().SetCursorMode(Pokebox.CursorMode.Multi);
                    Press(Keys.A);
                    Wait(300);
                    for (int i = 0; i < k - 1; i++)
                    {
                        Press(Keys.HAT.Up);
                        Wait(300);
                    }
                    Press(Keys.A);
                    Wait(300);
                    Module<Pokebox>().SelectBoxList();
                    Press(Keys.A);
                    Wait(1000);
                    Press(Keys.A);
                    Wait(300);
                    PressUntil(Keys.B, 500, Module<Idle>().Check);
                }
                else
                    PressUntil(Keys.B, 500, Module<Idle>().Check);
                Wait(300);
                UpdateSummary();
            }
        }
    }
}
