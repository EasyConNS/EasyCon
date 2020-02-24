using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon.Script
{
    public interface IOutputAdapter
    {
        void Print(string message, bool newline);
    }
}
