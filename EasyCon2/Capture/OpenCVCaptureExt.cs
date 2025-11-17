using OpenCvSharp;
using System.Diagnostics;

namespace EasyCon2.Capture
{
    partial class OpenCVCapture
    {

        public static Dictionary<string, int> GetCaptureTypes()
        {
            var captureTypes = new Dictionary<string, int>();
            var values = Enum.GetValues(typeof(VideoCaptureAPIs));
            foreach (var value in values)
            {
                Debug.WriteLine(value + "--" + (int)value);//获取名称和值
                if (captureTypes.ContainsKey(value.ToString()))
                    continue;
                captureTypes.Add(value.ToString(), (int)value);
            }
            return captureTypes;
        }
    }
}
