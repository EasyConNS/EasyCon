using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PokemonTycoon
{
    static class Utils
    {
        static DateTime _starttime;

        public static void TimerStart()
        {
            _starttime = DateTime.Now;
        }

        public static void TimerStop()
        {
            Debug.WriteLine((DateTime.Now - _starttime).TotalMilliseconds);
        }
    }
}
