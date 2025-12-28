using EasyDevice;
using SharpDX.DirectInput;
using System.Diagnostics;

namespace DirectInput
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            using (var directInput = new SharpDX.DirectInput.DirectInput())
            {
                // Find a Joystick Guid
                DeviceInstance di = null; 

                foreach (var deviceInstance in directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AttachedOnly))
                    di=deviceInstance;

                var joystickGuid = di?.InstanceGuid ?? Guid.Empty;
                // If Gamepad not found, look for a Joystick
                if (joystickGuid == Guid.Empty)
                {
                    Debug.WriteLine("no gamepad device found, try joystick device");
                    foreach (var deviceInstance in directInput.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AttachedOnly))
                        di = deviceInstance;
                }

                joystickGuid = di?.InstanceGuid ?? Guid.Empty;
                // If Joystick not found, throws an error
                if (joystickGuid == Guid.Empty)
                {
                    Debug.WriteLine("No joystick/Gamepad found.");
                   Close();
                }
                using var gamePad = new Joystick(directInput, joystickGuid);
                Debug.WriteLine("Found Joystick/Gamepad with GUID: {0}", joystickGuid);
                Debug.WriteLine("Device Found: " + di!.InstanceName);

                // Query all suported ForceFeedback effects
                var allEffects = gamePad.GetEffects();
                foreach (var effectInfo in allEffects)
                    Debug.WriteLine("Effect available {0}", effectInfo.Name);

                gamePad.Properties.BufferSize = 128;
                // 设置轴的范围
                gamePad.Properties.Range = new InputRange(0, 255);

                gamePad.Acquire();
                while (true)
                {
                    gamePad.Poll();

                    var datas = gamePad.GetBufferedData();
                    //foreach (var st in datas)
                    //    Debug.WriteLine(st);

                    var state = gamePad.GetCurrentState();
                    Debug.WriteLine($"State: [{string.Join(",", Enumerable.Range(0, state.Buttons.Length).Where(i => state.Buttons[i]).Select(i => getKeyName(i)))}]," +
                        $"X:{state.X},Y:{state.Y},Z:{state.Z}, RX:{state.RotationX}, ,RY:{state.RotationY}, RZ:{state.RotationZ}");
                    Thread.Sleep(500);
                }
            }
        }

        private string getKeyName(int index)
        {
            int k = 1 << index;
            foreach (SwitchButton e in Enum.GetValues<SwitchButton>())
                if ((int)e == k)
                {
                    return e.GetName();
                }
            return "unkown";
        }
    }
}
