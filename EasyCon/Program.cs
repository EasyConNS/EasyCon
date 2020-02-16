using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EasyCon
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            bool newProcess;
            using (Mutex mutex = new Mutex(true, "EasyCon", out newProcess))
            {
                if (newProcess)
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new EasyConForm());
                }
                else
                {
                    SystemSounds.Hand.Play();
                    MessageBox.Show("程序已经在运行了！");
                }
            }
        }
    }
}
