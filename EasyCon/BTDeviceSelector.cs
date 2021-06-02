using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EasyCon
{
	public partial class BTDeviceSelector : Form
	{
		public BTDeviceSelector()
		{
			InitializeComponent();
		}

		private void BTDeviceSelector_Load(object sender, EventArgs e)
		{
			this.listBox1.Items.Clear();
			this.usbDevices = BTDeviceSelector.GetUSBDevices();
			foreach (BTDeviceSelector.USBDeviceInfo usbdeviceInfo in this.usbDevices)
			{
				Console.WriteLine(usbdeviceInfo.DeviceID);
				Console.WriteLine(usbdeviceInfo.PnpDeviceID);
				Console.WriteLine(usbdeviceInfo.Description);
				this.listBox1.Items.Add(usbdeviceInfo.Description);
			}
		}

		private static List<BTDeviceSelector.USBDeviceInfo> GetUSBDevices()
		{
			List<BTDeviceSelector.USBDeviceInfo> list = new List<BTDeviceSelector.USBDeviceInfo>();
			ManagementObjectCollection managementObjectCollection;
			using (ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("Select * From Win32_PnPEntity"))
			{
				managementObjectCollection = managementObjectSearcher.Get();
			}
			foreach (ManagementBaseObject managementBaseObject in managementObjectCollection)
			{
				string text = (string)managementBaseObject.GetPropertyValue("Description");
				if (!string.IsNullOrEmpty(text) && (Regex.IsMatch(text, ".*Bluetooth.*Adapter.*") || Regex.IsMatch(text, ".*Bluetooth.*Radio.*") || Regex.IsMatch(text, ".*Bluetooth.*ラジオ.*") || Regex.IsMatch(text, ".*Bluetooth.*アダプタ.*") || Regex.IsMatch(text, ".*CSR.*Bluetooth.*") || Regex.IsMatch(text, ".*TOSHIBA.*Bluetooth.*") || Regex.IsMatch(text, ".*CSR.*Bluetooth.*") || Regex.IsMatch(text, ".*Intel.*Wireless.*Bluetooth.*") || Regex.IsMatch(text, ".*インテル.*ワイヤレス.*Bluetooth.*")))
				{
					Console.WriteLine("--------------------------");
					Console.WriteLine(managementBaseObject.ClassPath.ClassName);
					Console.WriteLine("--------------------------");
					foreach (PropertyData propertyData in managementBaseObject.Properties)
					{
						string name = propertyData.Name;
						string str = "=";
						object value = propertyData.Value;
						Console.WriteLine(name + str + ((value != null) ? value.ToString() : null));
					}
					Console.WriteLine("--------------------------");
					foreach (QualifierData qualifierData in managementBaseObject.Qualifiers)
					{
						string name2 = qualifierData.Name;
						string str2 = "=";
						object value2 = qualifierData.Value;
						Console.WriteLine(name2 + str2 + ((value2 != null) ? value2.ToString() : null));
					}
					Console.WriteLine("--------------------------");
					foreach (PropertyData propertyData2 in managementBaseObject.SystemProperties)
					{
						string name3 = propertyData2.Name;
						string str3 = "=";
						object value3 = propertyData2.Value;
						Console.WriteLine(name3 + str3 + ((value3 != null) ? value3.ToString() : null));
					}
					if (managementBaseObject.GetPropertyValue("Name") != null)
					{
						list.Add(new BTDeviceSelector.USBDeviceInfo((string)managementBaseObject.GetPropertyValue("DeviceID"), (string)managementBaseObject.GetPropertyValue("ClassGuid"), (string)managementBaseObject.GetPropertyValue("Name")));
					}
				}
			}
			managementObjectCollection.Dispose();
			return list;
		}

		private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (this.listBox1.SelectedIndex < 0)
			{
				this.button1.Enabled = false;
				return;
			}
			this.button1.Enabled = true;
			this.label4.Text = this.usbDevices[this.listBox1.SelectedIndex].Description;
			this.label5.Text = this.usbDevices[this.listBox1.SelectedIndex].PnpDeviceID;
			string deviceID = this.usbDevices[this.listBox1.SelectedIndex].DeviceID;
			string str = deviceID.Substring(deviceID.IndexOf("VID_") + 4, 4);
			string str2 = deviceID.Substring(deviceID.IndexOf("PID_") + 4, 4);
			this.label6.Text = str + "/" + str2;
		}

		private void button1_Click(object sender, EventArgs e)
		{
			if (MessageBox.Show("注意！\r\n一旦安装模拟控制switch驱动会导致该蓝牙无法作为正常蓝牙使用\r\n如果要恢复需要手动卸载CSR8510 A10驱动，然后重新插入蓝牙适配器\r\n安装驱动需要一点时间，确定要继续操作吗？", "警告", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes)
			{
				try
				{
					string deviceID = this.usbDevices[this.listBox1.SelectedIndex].DeviceID;
					int VID = int.Parse(deviceID.Substring(deviceID.IndexOf("VID_") + 4, 4), NumberStyles.HexNumber);
					int PID = int.Parse(deviceID.Substring(deviceID.IndexOf("PID_") + 4, 4), NumberStyles.HexNumber);
					BTController.DriverReplace(VID, PID);
					MessageBox.Show("替换驱动完成\r\n如果驱动管理器中没有出现CSR8510 A10，请重启电脑后重试", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
					base.Close();
				}
				catch
				{
					MessageBox.Show("替换驱动失败\r\n建议重启电脑或者重插蓝牙适配器后再次尝试", "错误", MessageBoxButtons.OK, MessageBoxIcon.Hand);
				}
				this.Close();
			}
			this.Close();
			this.Hide();
		}

		private static void Win32Shutdown(int shutdownFlags)
		{
			Thread thread = new Thread(delegate ()
			{
				using (ManagementClass managementClass = new ManagementClass("Win32_OperatingSystem"))
				{
					managementClass.Get();
					managementClass.Scope.Options.EnablePrivileges = true;
					foreach (ManagementBaseObject managementBaseObject in managementClass.GetInstances())
					{
						ManagementObject managementObject = (ManagementObject)managementBaseObject;
						managementObject.InvokeMethod("Win32Shutdown", new object[]
						{
							shutdownFlags,
							0
						});
						managementObject.Dispose();
					}
				}
			});
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();
			thread.Join();
		}

		private void RunWin32Shutdown(BTDeviceSelector.Win32ShutdownFlags shutdownFlags)
		{
			BTDeviceSelector.Win32Shutdown((int)shutdownFlags);
		}

		private List<BTDeviceSelector.USBDeviceInfo> usbDevices = new List<BTDeviceSelector.USBDeviceInfo>();

		private ListBox listBox1;

		private Label label1;

		private Label label2;

		private Label label3;

		private Label label4;

		private Label label5;

		private Label label6;

		private Button button1;

		private class USBDeviceInfo
		{
			public USBDeviceInfo(string deviceID, string pnpDeviceID, string description)
			{
				this.DeviceID = deviceID;
				this.PnpDeviceID = pnpDeviceID;
				this.Description = description;
			}

			public string DeviceID { get; private set; }

			public string PnpDeviceID { get; private set; }

			public string Description { get; private set; }
		}

		private enum Win32ShutdownFlags
		{
			Logoff,
			Shutdown,
			Reboot,
			PowerOff = 8,
			Forced = 4
		}

		public const int CM_LOCATE_DEVNODE_NORMAL = 0x00000000;
		public const int CM_REENUMERATE_NORMAL = 0x00000000;
		public const int CR_SUCCESS = 0x00000000;

		[DllImport("CfgMgr32.dll", SetLastError = true)]
		public static extern int CM_Locate_DevNodeA(ref int pdnDevInst, string pDeviceID, int ulFlags);

		[DllImport("CfgMgr32.dll", SetLastError = true)]
		public static extern int CM_Reenumerate_DevNode(int dnDevInst, int ulFlags);

		private void button2_Click(object sender, EventArgs e)
		{
			int pdnDevInst = 0;
			if (CM_Locate_DevNodeA(ref pdnDevInst, null, CM_LOCATE_DEVNODE_NORMAL) != CR_SUCCESS)
			{
				MessageBox.Show("需要管理员权限允许-0");
			}
			int ret = CM_Reenumerate_DevNode(pdnDevInst, CM_REENUMERATE_NORMAL);
			if (ret != CR_SUCCESS)
			{
				MessageBox.Show("需要管理员权限允许-1");
			}

			this.listBox1.Items.Clear();
			this.usbDevices = BTDeviceSelector.GetUSBDevices();
			foreach (BTDeviceSelector.USBDeviceInfo usbdeviceInfo in this.usbDevices)
			{
				Console.WriteLine(usbdeviceInfo.DeviceID);
				Console.WriteLine(usbdeviceInfo.PnpDeviceID);
				Console.WriteLine(usbdeviceInfo.Description);
				this.listBox1.Items.Add(usbdeviceInfo.Description);
			}
		}
	}

}
