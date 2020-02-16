using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PokemonTycoon.Graphic;
using PTDevice;
using System.Drawing;

namespace PokemonTycoon.Scripts
{
    public class Test : Script
    {
        public void Run()
        {
            for (int i = 0; i < 10; i++)
            {
                Module<SwitchOS>().ChangeDateCoreFast();
            }
            //PressUntil(Keys.A, 1000, Module<Battle.Raid>().CheckMenu, TimeoutThrow(10));
            //Msg(Module<Battle.Raid>().CheckMenu());

            //DateTime date = DateTime.Now;
            //bool first = false;
            ////Module<SwitchOS>().ChangeDateCoreFast_Reset(ref date, ref first);
            //int n = 100;
            //for (int i = 0; i < n; i++)
            //{
            //    Module<SwitchOS>().ChangeDateCoreFast(ref date, ref first, true);
            //}
            //Msg((DateTime.Now - StartTime).TotalMilliseconds / 1000 / n);

            //Msg(Module<Pokelist>().Check());
        }
    }
}