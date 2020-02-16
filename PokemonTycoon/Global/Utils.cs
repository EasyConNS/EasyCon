using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PokemonTycoon
{
    static class Utils
    {
        public const int WM_APP = 0x8000;
        public const int WM_APP_Activate = WM_APP + 1;

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int wMsg, IntPtr wParam, IntPtr lParam);

        public static Type[] GetDerivedTypes(Type type)
        {
            return (from domainAssembly in AppDomain.CurrentDomain.GetAssemblies()
                    from assemblyType in domainAssembly.GetTypes()
                    where assemblyType.IsSubclassOf(type)
                    select assemblyType).ToArray();
        }
    }
}
