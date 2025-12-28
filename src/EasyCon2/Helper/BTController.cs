using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace EasyCon2.Helper
{
    static class BTController
    {
        public static bool opened = false;

        [DllImport("btkeyLib.dll")]
        public static extern void send_button(uint key, uint waittime);

        [DllImport("btkeyLib.dll")]
        public static extern void send_stick_r(uint h, uint v, uint waittime);

        [DllImport("btkeyLib.dll")]
        public static extern void send_stick_l(uint h, uint v, uint waittime);

        [DllImport("btkeyLib.dll", EntryPoint = "shutdown_gamepad")]
        private static extern void shutdown_gamepad();

        [HandleProcessCorruptedStateExceptions]
        public static void close()
        {
            try
            {
                BTController.shutdown_gamepad();
                opened = false;
            }
            catch (Exception)
            {
                opened = false;
            }
        }

        [DllImport("btkeyLib.dll")]
        private static extern void start_gamepad();

        [HandleProcessCorruptedStateExceptions]
        public static void open()
        {
            if (opened)
                return;

            try
            {
                Task.Factory.StartNew(delegate ()
                {
                    opened = true;
                    BTController.start_gamepad();
                });
            }
            catch (Exception)
            {
            }
        }


        [DllImport("btkeyLib.dll", EntryPoint = "send_padcolor")]
        private static extern void __send_padcolor(uint pad_color, uint button_color, uint leftgrip_color, uint rightgrip_color);

        public static void send_padcolor(Color pad_color, Color button_color, Color leftgrip_color, Color rightgrip_color)
        {
            uint pad_color2 = (uint)((int)pad_color.R | (int)pad_color.G << 8 | (int)pad_color.B << 16);
            uint button_color2 = (uint)((int)button_color.R | (int)button_color.G << 8 | (int)button_color.B << 16);
            uint leftgrip_color2 = (uint)((int)leftgrip_color.R | (int)leftgrip_color.G << 8 | (int)leftgrip_color.B << 16);
            uint rightgrip_color2 = (uint)((int)rightgrip_color.R | (int)rightgrip_color.G << 8 | (int)rightgrip_color.B << 16);
            BTController.__send_padcolor(pad_color2, button_color2, leftgrip_color2, rightgrip_color2);
        }

        [DllImport("libwdi.dll")]
        public static extern void DriverReplace(int vid, int pid);
    }
}
