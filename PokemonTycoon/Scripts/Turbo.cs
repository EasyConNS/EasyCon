using PTDevice;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PokemonTycoon.Scripts
{
    class Turbo : Script
    {
        public void Run(NintendoSwitch.Key key, int duration, int interval)
        {
            Msg(Colors.Highlight, "连发：" + key.Name);
            Light(Color.LightYellow);
            Light(Taskbar.Highlight);
            while (true)
            {
                Press(key, duration);
                Wait(interval);
            }
        }
    }
}
