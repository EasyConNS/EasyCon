using System.IO;

namespace EasyCon2
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            var version = Environment.Version;
            if (version.Major < 6 && version.Build <= 6)
            {
                MessageBox.Show(".Net版本过低，请升级到最新版");
                return;
            }
            try
            {
                Application.Run(new Forms.EasyConForm());
            }
            catch (Exception ex)
            {
                System.Text.StringBuilder builder = new System.Text.StringBuilder();
                builder.AppendLine(DateTime.Now.ToString("[yyyy.M.d h:mm:ss.fff]"));
                builder.AppendLine(ex.ToString());
                builder.AppendLine(ex.StackTrace);
                builder.AppendLine();
                File.AppendAllText("dump.log", builder.ToString());
                MessageBox.Show("程序崩溃了，快看看<dump.log>文件里都写了什么吧");
            }
        }
    }
}