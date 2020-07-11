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
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(GlobalExceptionLogger);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new EasyConForm());
        }

        private static void GlobalExceptionLogger(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var ex = e.ExceptionObject as Exception;
                if (ex == null)
                    return;
                System.Text.StringBuilder builder = new System.Text.StringBuilder();
                builder.AppendLine(DateTime.Now.ToString("[yyyy.M.d h:mm:ss.fff]"));
                builder.AppendLine(ex.ToString());
                builder.AppendLine();
                System.IO.File.AppendAllText("error.log", builder.ToString());
            }
            catch
            { }
        }
    }
}
