namespace EasyCon2
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
#if NET6_0_OR_GREATER
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
#endif
#if NETFRAMEWORK
            // Init for .net framework
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(GlobalExceptionLogger);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
#endif
            string dotNetVersion = Environment.Version.ToString();
            if (dotNetVersion.CompareTo("6.0.6") <= 0)
            {
                MessageBox.Show(".Net6�汾���ͣ������������°�");
                return;
            }
            Application.Run(new Forms.EasyConForm());
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