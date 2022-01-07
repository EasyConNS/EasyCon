using EasyCon2.Global;
using ECDevice;

namespace EasyCon2.Script.Parsing
{
    static class NSKeys
    {
        static Dictionary<string, NintendoSwitch.Key> _keyDict = new();
        static Dictionary<NintendoSwitch.Key, string> _keyName = new();
        static Dictionary<string, NintendoSwitch.DirectionKey> _directions = new();

        static NSKeys()
        {
            foreach (NintendoSwitch.Button e in Enum.GetValues(typeof(NintendoSwitch.Button)))
                _keyDict.Add(e.GetName(), NintendoSwitch.Key.Button(e));
            _keyDict.Add("LEFT", NintendoSwitch.Key.HAT(NintendoSwitch.HAT.LEFT));
            _keyDict.Add("RIGHT", NintendoSwitch.Key.HAT(NintendoSwitch.HAT.RIGHT));
            _keyDict.Add("UP", NintendoSwitch.Key.HAT(NintendoSwitch.HAT.TOP));
            _keyDict.Add("DOWN", NintendoSwitch.Key.HAT(NintendoSwitch.HAT.BOTTOM));
            _keyDict.Add("DOWNLEFT", NintendoSwitch.Key.HAT(NintendoSwitch.HAT.BOTTOM_LEFT));
            _keyDict.Add("DOWNRIGHT", NintendoSwitch.Key.HAT(NintendoSwitch.HAT.BOTTOM_RIGHT));
            _keyDict.Add("UPLEFT", NintendoSwitch.Key.HAT(NintendoSwitch.HAT.TOP_LEFT));
            _keyDict.Add("UPRIGHT", NintendoSwitch.Key.HAT(NintendoSwitch.HAT.TOP_RIGHT));
            foreach (var pair in _keyDict)
                _keyName[pair.Value] = pair.Key;
            _directions.Add("UP", NintendoSwitch.DirectionKey.Up);
            _directions.Add("DOWN", NintendoSwitch.DirectionKey.Down);
            _directions.Add("LEFT", NintendoSwitch.DirectionKey.Left);
            _directions.Add("RIGHT", NintendoSwitch.DirectionKey.Right);
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

        public static NintendoSwitch.DirectionKey GetDirection(string direction)
        {
            direction = direction.ToUpper();
            return _directions.GetValueOrDefault(direction, NintendoSwitch.DirectionKey.None);
        }
    }
}
