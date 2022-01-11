using ECDevice;
using ECDevice.Exts;

namespace EasyCon2.Script.Parsing
{
    static class NSKeys
    {
        static Dictionary<string, NintendoSwitch.Key> _keyDict = new();
        static Dictionary<NintendoSwitch.Key, string> _keyName = new();
        static Dictionary<string, DirectionKey> _directions = new();

        static NSKeys()
        {
            foreach (SwitchButton e in Enum.GetValues(typeof(SwitchButton)))
                _keyDict.Add(e.GetName(), NintendoSwitch.Key.Button(e));
            _keyDict.Add("LEFT", NintendoSwitch.Key.HAT(SwitchHAT.LEFT));
            _keyDict.Add("RIGHT", NintendoSwitch.Key.HAT(SwitchHAT.RIGHT));
            _keyDict.Add("UP", NintendoSwitch.Key.HAT(SwitchHAT.TOP));
            _keyDict.Add("DOWN", NintendoSwitch.Key.HAT(SwitchHAT.BOTTOM));
            _keyDict.Add("DOWNLEFT", NintendoSwitch.Key.HAT(SwitchHAT.BOTTOM_LEFT));
            _keyDict.Add("DOWNRIGHT", NintendoSwitch.Key.HAT(SwitchHAT.BOTTOM_RIGHT));
            _keyDict.Add("UPLEFT", NintendoSwitch.Key.HAT(SwitchHAT.TOP_LEFT));
            _keyDict.Add("UPRIGHT", NintendoSwitch.Key.HAT(SwitchHAT.TOP_RIGHT));
            foreach (var pair in _keyDict)
                _keyName[pair.Value] = pair.Key;
            _directions.Add("UP", DirectionKey.Up);
            _directions.Add("DOWN", DirectionKey.Down);
            _directions.Add("LEFT", DirectionKey.Left);
            _directions.Add("RIGHT", DirectionKey.Right);
        }

        public static NintendoSwitch.Key Get(string name)
        {
            name = name.ToUpper();
            return _keyDict.GetValueOrDefault(name, null);
        }

        public static string GetName(NintendoSwitch.Key key)
        {
            return _keyName.GetValueOrDefault(key, string.Empty);
        }

        public static DirectionKey GetDirection(string direction)
        {
            direction = direction.ToUpper();
            return _directions.GetValueOrDefault(direction, DirectionKey.None);
        }
    }
}
