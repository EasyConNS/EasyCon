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
                foreach (var deviceInstance in directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AttachedOnly))
                {
                    using var gamePad = new Joystick(directInput, deviceInstance.InstanceGuid);
                    Debug.WriteLine("Device Found: " + deviceInstance.InstanceName);

                    // Query all suported ForceFeedback effects
                    var allEffects = gamePad.GetEffects();
                    foreach (var effectInfo in allEffects)
                        Debug.WriteLine("Effect available {0}", effectInfo.Name);

                    gamePad.Properties.BufferSize = 128;
                    gamePad.Acquire();
                    while (true)
                    {
                        gamePad.Poll();

                        var state = gamePad.GetCurrentState();
                        Debug.WriteLine($"State: [{string.Join(",", Enumerable.Range(0, state.Buttons.Length).Where(i => state.Buttons[i]).Select(i => getKeyName(i)))}]," +
                            $"X:{state.X:X},Y:{state.Y:X},Z:{state.Z:X}, RZ:{state.RotationZ:X}");
                        Thread.Sleep(500);
                    }
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
