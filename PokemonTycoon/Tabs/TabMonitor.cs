using AForge.Video;
using PokemonTycoon.Graphic;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PokemonTycoon
{
    public partial class MainForm : Form
    {
        internal class TabMonitor : TabModule
        {
            MainForm form;

            public TabMonitor(MainForm form)
            {
                this.form = form;
                VideoCapture.VideoSourceChanged += () => form.videoSourcePlayerMonitor.VideoSource = VideoCapture.VideoSource;
            }
        }
    }
}
