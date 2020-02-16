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
        internal class TabSettings : TabModule
        {
            MainForm form;

            public TabSettings(MainForm form)
            {
                this.form = form;
            }

            public override void Activate()
            {
            }
        }
    }
}
