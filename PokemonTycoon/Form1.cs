using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PokemonTycoon
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            Debug.WriteLine($"down {e.KeyCode}");
            Debug.WriteLine($"X:{Cursor.Position.X} Y:{Cursor.Position.Y} Color:{ScreenMon.GetColor()}");
        }
    }
}
