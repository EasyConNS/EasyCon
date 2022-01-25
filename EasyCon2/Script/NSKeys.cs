using ECDevice;
using ECDevice.Exts;

namespace EasyCon2.Script
{
    static class NSKeys
    {
        static Dictionary<string, ECKey> _keyDict = new();
        static Dictionary<ECKey, string> _keyName = new();
        static Dictionary<string, DirectionKey> _directions = new();

        static NSKeys()
        {
            foreach (SwitchButton e in Enum.GetValues(typeof(SwitchButton)))
                _keyDict.Add(e.GetName(), ECKeyUtil.Button(e));
            _keyDict.Add("LEFT", ECKeyUtil.HAT(SwitchHAT.LEFT));
            _keyDict.Add("RIGHT", ECKeyUtil.HAT(SwitchHAT.RIGHT));
            _keyDict.Add("UP", ECKeyUtil.HAT(SwitchHAT.TOP));
            _keyDict.Add("DOWN", ECKeyUtil.HAT(SwitchHAT.BOTTOM));
            _keyDict.Add("DOWNLEFT", ECKeyUtil.HAT(SwitchHAT.BOTTOM_LEFT));
            _keyDict.Add("DOWNRIGHT", ECKeyUtil.HAT(SwitchHAT.BOTTOM_RIGHT));
            _keyDict.Add("UPLEFT", ECKeyUtil.HAT(SwitchHAT.TOP_LEFT));
            _keyDict.Add("UPRIGHT", ECKeyUtil.HAT(SwitchHAT.TOP_RIGHT));
            foreach (var pair in _keyDict)
                _keyName[pair.Value] = pair.Key;
            _directions.Add("UP", DirectionKey.Up);
            _directions.Add("DOWN", DirectionKey.Down);
            _directions.Add("LEFT", DirectionKey.Left);
            _directions.Add("RIGHT", DirectionKey.Right);
        }

        public static ECKey Get(string name)
        {
            name = name.ToUpper();
            return _keyDict.GetValueOrDefault(name, null);
        }

        public static string GetName(ECKey key)
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
