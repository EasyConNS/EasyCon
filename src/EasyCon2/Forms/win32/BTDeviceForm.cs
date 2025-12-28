using System.ComponentModel;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace EasyCon2.Forms.win32
{
    public partial class BTDeviceForm : Form
    {
        private BindingList<USBDeviceInfo> udlist = new();
        public BTDeviceForm()
        {
            InitializeComponent();

            BTDList_LB.DataSource = udlist;
            BTDList_LB.DisplayMember = "Description";
        }

        private void BTDeviceForm_Load(object sender, EventArgs e)
        {
            udlist.Clear();
            foreach (var u in GetUSBDevices())
            {
                udlist.Add(u);
            }
        }

        private void BTDList_LB_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (BTDList_LB.SelectedIndex < 0)
            {
                Install_BTN.Enabled = false;
                return;
            }

            Install_BTN.Enabled = true;
            label4.Text = udlist[BTDList_LB.SelectedIndex].Description;
            label5.Text = udlist[BTDList_LB.SelectedIndex].PnpDeviceID;
            var deviceID = udlist[BTDList_LB.SelectedIndex].DeviceID;
            var str = deviceID.Substring(deviceID.IndexOf("VID_") + 4, 4);
            var str2 = deviceID.Substring(deviceID.IndexOf("PID_") + 4, 4);
            label6.Text = str + "/" + str2;
        }

        private void Refresh_BTN_Click(object sender, EventArgs e)
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
            // refresh
            udlist.Clear();
            foreach (var u in GetUSBDevices())
            {
                udlist.Add(u);
            }
        }

        #region Win32
        private static List<USBDeviceInfo> GetUSBDevices()
        {
            var list = new List<USBDeviceInfo>();
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
                        list.Add(new USBDeviceInfo((string)managementBaseObject.GetPropertyValue("DeviceID"), (string)managementBaseObject.GetPropertyValue("ClassGuid"), (string)managementBaseObject.GetPropertyValue("Name")));
                    }
                }
            }
            managementObjectCollection.Dispose();
            return list;
        }

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
        #endregion
    }
}
