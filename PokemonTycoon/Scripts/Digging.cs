using PTDevice;
using PokemonTycoon.Graphic;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PokemonTycoon.Scripts
{
    class Digging : Script
    {
        int _rounds;
        int _digs;
        int _maxDigs;
        int _totalDigs;
        int _totalRounds;

        void UpdateSummary()
        {
            var str = new StringBuilder();
            str.Append($"第{_rounds + 1}轮挖掘，本轮{_digs}次，最高纪录{_maxDigs}次");
            if (_totalRounds > 0)
                str.Append($"，平均{(double)_totalDigs / _totalRounds:0.00}次");
            Summary = str.ToString();
        }

        public bool CheckNext()
        {
            Log($"Digging.CheckNext()");
            return VideoCapture.Match(465, 895, ImageRes.Get("digging_next"), DefaultImageCap, VideoCapture.LineSampler(3));
        }

        public bool CheckEnd()
        {
            Log($"Digging.CheckEnd()");
            return VideoCapture.Match(630, 895, ImageRes.Get("digging_end"), DefaultImageCap, VideoCapture.LineSampler(3));
        }

        public void Run()
        {
            Msg(Colors.Highlight, "自动挖宝");
            _rounds = 0;
            _digs = 0;
            _maxDigs = 0;
            UpdateSummary();
            while (true)
            {
                if (CheckNext())
                {
                    Msg("继续挖");
                    _digs++;
                    UpdateSummary();
                    Press(Keys.A);
                    Wait(1000);
                    continue;
                }
                if (CheckEnd())
                {
                    _rounds++;
                    _totalDigs += _digs;
                    _totalRounds++;
                    if (_digs > _maxDigs)
                    {
                        _maxDigs = _digs;
                        Msg(Colors.Success, $"新纪录：{_maxDigs}次");
                        if (_maxDigs > 30)
                            ScreenShot("Digging Record");
                    }
                    Msg(Colors.End, "本轮结束");
                    _digs = 0;
                    UpdateSummary();
                    Press(Keys.A);
                    Wait(1000);
                    continue;
                }

                // default
                Press(Keys.A);
                Wait(1000);
            }
        }
    }
}
