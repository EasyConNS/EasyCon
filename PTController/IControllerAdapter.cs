using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PTController
{
    public interface IControllerAdapter
    {
        bool IsRunning();
        Color CurrentLight { get; }
    }
}
