using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PokemonTycoon
{
    public partial class Form1 : Form
    {
        class TabSettings : TabModule
        {
            Form1 form;

            public TabSettings(Form1 form)
            {
                this.form = form;
            }

            public override void Activate()
            {
                PreviewScreen();
            }

            public void PreviewScreen()
            {
                form.buttonSettingsScreen.Text = $"显示器：{Monitor.ScreenIndex}";
                form.pictureBoxSettingsScreenPreview.Image = Monitor.ScreenShot();
            }

            public void ChangeScreen()
            {
                Monitor.ChangeScreen();
                PreviewScreen();
            }
        }
    }
}
