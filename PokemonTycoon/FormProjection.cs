using PokemonTycoon.Graphic;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PokemonTycoon
{
    public partial class FormProjection : Form
    {
        MainForm form;

        public FormProjection(MainForm form)
        {
            this.form = form;
            InitializeComponent();
            VideoCapture.VideoSourceChanged += () => videoSourcePlayerProjection.VideoSource = VideoCapture.VideoSource;
        }

        private void FormProjection_Load(object sender, EventArgs e)
        {
            //Screen scr = Screen.FromControl(this);
            Screen scr = Screen.AllScreens[1];
            Width = scr.Bounds.Width;
            Height = scr.Bounds.Height;
            Left = 0;
            Top = 0;
            videoSourcePlayerProjection.Width = Width;
            videoSourcePlayerProjection.Height = Height;
        }
    }
}
