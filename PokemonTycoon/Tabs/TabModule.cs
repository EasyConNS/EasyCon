using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PokemonTycoon
{
    public partial class MainForm : Form
    {
        internal abstract class TabModule
        {
            public virtual void Init()
            { }
            public virtual void Activate()
            { }
            public virtual void Close()
            { }
        }
    }
}
