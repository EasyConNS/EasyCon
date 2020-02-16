using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PokemonTycoon
{
    public class Logger
    {
        public static readonly Logger _instance = new Logger();

        const string LogPath = @"Log\";
        StreamWriter _writer;

        Logger()
        {
            Directory.CreateDirectory(LogPath);
            _writer = new StreamWriter(new FileStream($"{LogPath}Log {DateTime.Now.ToString("MM_dd HH_mm_ss")}.txt", FileMode.Create));
        }

        ~Logger()
        {
            //_writer.Close();
        }

        public static void Write(object message)
        {
            lock (_instance)
            {
                _instance._writer.Write(message);
            }
        }

        public static void WriteLine(object message)
        {
            lock (_instance)
            {
                _instance._writer.WriteLine(message);
                _instance._writer.Flush();
            }
        }
    }
}
