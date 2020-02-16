using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PTController;

namespace EasyCon
{
    class ControllerAdapter : IControllerAdapter
    {
        public Color CurrentLight => Color.White;

        public bool IsRunning()
        {
            return false;
        }
    }
}
