using PTDevice;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon.Script.Parsing
{
    static class NSKeys
    {
        static Dictionary<string, NintendoSwitch.Key> _keyDict = new Dictionary<string, NintendoSwitch.Key>();
        static Dictionary<NintendoSwitch.Key, string> _keyName = new Dictionary<NintendoSwitch.Key, string>();
        static Dictionary<string, NintendoSwitch.DirectionKey> _directions = new Dictionary<string, NintendoSwitch.DirectionKey>();

        static NSKeys()
        {
            foreach (NintendoSwitch.Button e in Enum.GetValues(typeof(NintendoSwitch.Button)))
                _keyDict.Add(e.GetName(), NintendoSwitch.Key.Button(e));
            _keyDict.Add("LEFT", NintendoSwitch.Key.HAT(NintendoSwitch.HAT.LEFT));
            _keyDict.Add("RIGHT", NintendoSwitch.Key.HAT(NintendoSwitch.HAT.RIGHT));
            _keyDict.Add("UP", NintendoSwitch.Key.HAT(NintendoSwitch.HAT.TOP));
            _keyDict.Add("DOWN", NintendoSwitch.Key.HAT(NintendoSwitch.HAT.BOTTOM));
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
            if (_keyDict.ContainsKey(name))
                return _keyDict[name];
            return null;
        }

        public static string GetName(NintendoSwitch.Key key)
        {
            if (_keyName.ContainsKey(key))
                return _keyName[key];
            return string.Empty;
        }

        public static NintendoSwitch.DirectionKey GetDirection(string direction)
        {
            direction = direction.ToUpper();
            if (_directions.ContainsKey(direction))
                return _directions[direction];
            return NintendoSwitch.DirectionKey.None;
        }
    }
}
