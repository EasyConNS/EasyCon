#if bluetooth
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace EasyCon2
{
	static class BTController
    {
		// Token: 0x060000A3 RID: 163
		[DllImport("btkeyLib.dll")]
		public static extern void send_button(uint key, uint waittime);

		// Token: 0x060000A4 RID: 164
		[DllImport("btkeyLib.dll")]
		public static extern void send_stick_r(uint h, uint v, uint waittime);

		// Token: 0x060000A5 RID: 165
		[DllImport("btkeyLib.dll")]
		public static extern void send_stick_l(uint h, uint v, uint waittime);

		// Token: 0x060000A6 RID: 166
		[DllImport("btkeyLib.dll", EntryPoint = "shutdown_gamepad")]
		private static extern void ___shutdown_gamepad();

		// Token: 0x060000A7 RID: 167 RVA: 0x0000CCA4 File Offset: 0x0000AEA4
		[HandleProcessCorruptedStateExceptions]
		public static void shutdown_gamepad()
		{
			try
			{
				BTController.___shutdown_gamepad();
			}
			catch (Exception)
			{
			}
		}

		// Token: 0x060000A8 RID: 168
		[DllImport("btkeyLib.dll")]
		public static extern void start_gamepad();

		// Token: 0x060000A9 RID: 169
		[DllImport("btkeyLib.dll", EntryPoint = "send_padcolor")]
		private static extern void __send_padcolor(uint pad_color, uint button_color, uint leftgrip_color, uint rightgrip_color);

		// Token: 0x060000AA RID: 170 RVA: 0x0000CCCC File Offset: 0x0000AECC
		public static void send_padcolor(Color pad_color, Color button_color, Color leftgrip_color, Color rightgrip_color)
		{
			uint pad_color2 = (uint)((int)pad_color.R | (int)pad_color.G << 8 | (int)pad_color.B << 16);
			uint button_color2 = (uint)((int)button_color.R | (int)button_color.G << 8 | (int)button_color.B << 16);
			uint leftgrip_color2 = (uint)((int)leftgrip_color.R | (int)leftgrip_color.G << 8 | (int)leftgrip_color.B << 16);
			uint rightgrip_color2 = (uint)((int)rightgrip_color.R | (int)rightgrip_color.G << 8 | (int)rightgrip_color.B << 16);
			BTController.__send_padcolor(pad_color2, button_color2, leftgrip_color2, rightgrip_color2);
		}

		// Token: 0x060000AB RID: 171
		[DllImport("libwdi.dll")]
		public static extern void DriverReplace(int vid, int pid);
	}
}
#endif