using PokemonTycoon.Graphic;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PokemonTycoon.Scripts
{
    class TowerRush : Script
    {
        int _rounds;
        int _battles;

        void UpdateSummary()
        {
            var str = new StringBuilder();
            str.Append($"第{_rounds}轮，第{_battles}场战斗");
            if (_battles > 1)
                str.Append($"，平均用时{(int)(DateTime.Now - StartTime).TotalSeconds / (_battles - 1)}秒");
            Summary = str.ToString();
        }

        public bool CheckTeamSelect()
        {
            Log($"TowerRush.CheckTeamSelect()");
            return VideoCapture.Match(925, 70, ImageRes.Get("tower_teamselect"), DefaultImageCap, VideoCapture.LineSampler(3));
        }

        public bool CheckEngage()
        {
            Log($"TowerRush.CheckEngage()");
            return VideoCapture.Match(180, 970, ImageRes.Get("tower_engage"), DefaultImageCap, VideoCapture.LineSampler(3));
        }

        public void Run()
        {
            Msg(Colors.Highlight, "自动刷战斗塔");
            _rounds = 0;
            _battles = 0;
            while (true)
            {
                if (CheckTeamSelect())
                {
                    Msg("选择队伍");
                    _rounds++;
                    UpdateSummary();
                    Press(Keys.A);
                    Wait(500);
                    Press(Keys.A);
                    Wait(1500);
                    Press(Keys.A);
                    Wait(500);
                    Press(Keys.A);
                    Wait(500);
                    Press(Keys.HAT.Down);
                    Wait(500);
                    Press(Keys.A);
                    Wait(500);
                    Press(Keys.A);
                    Wait(500);
                    Press(Keys.HAT.Down);
                    Wait(500);
                    Press(Keys.A);
                    Wait(500);
                    Press(Keys.A);
                    Wait(500);
                    Press(Keys.A);
                    Wait(500);
                    continue;
                }
                if (Module<Battle>().CheckSelectSkill())
                {
                    Msg("选择技能");
                    var cur = Module<Battle>().GetSkillCursor();
                    if (!Module<Battle>().IsSkillAvailable(cur))
                    {
                        Msg("没PP了，换下一个技能");
                        Press(Keys.HAT.Down);
                        Wait(500);
                    }
                    Press(Keys.A);
                    Wait(500);
                    continue;
                }
                if (Module<Battle>().IsDisabled())
                {
                    Msg("技能被禁用，换下一个技能");
                    Wait(1000);
                    Press(Keys.HAT.Down);
                    Wait(500);
                    continue;
                }
                if (Module<Battle>().IsFainted() == Bool3VL.True)
                {
                    Msg(Colors.Fail, "选择下一只精灵");
                    Press(Keys.B);
                    Wait(500);
                    while (true)
                    {
                        Press(Keys.HAT.Down);
                        Wait(500);
                        if (Module<Battle>().IsFainted() == Bool3VL.False)
                            break;
                    }
                    Press(Keys.A);
                    Wait(500);
                    Press(Keys.A);
                    Wait(500);
                    continue;
                }
                if (CheckEngage())
                {
                    Msg(Colors.Start, "进入战斗");
                    _battles++;
                    UpdateSummary();
                    Press(Keys.A);
                    Wait(500);
                    Press(Keys.A);
                    Wait(3000);
                    continue;
                }

                // default
                Press(Keys.A);
                Wait(500);
            }
        }
    }
}
