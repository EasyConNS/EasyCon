using ECDevice;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EasyCon2.Forms
{
    public partial class Mouse : Form
    {
        internal NintendoSwitch NS;
        public Mouse(NintendoSwitch gamepad)
        {
            NS = gamepad;
            InitializeComponent();
        }
    }
}
