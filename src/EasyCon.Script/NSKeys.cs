using EasyDevice;

namespace EasyCon.Script;

static class NSKeys
{
    static readonly Dictionary<string, ECKey> _keyDict = [];
    static readonly Dictionary<ECKey, string> _keyName = [];

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
    }

    public static ECKey? Get(string name)
    {
        name = name.ToUpper();
        return _keyDict.GetValueOrDefault(name, null);
    }

    public static ECKey GetKey(string keyname, string direction = "0")
    {
        if (int.TryParse(direction, out int degree))
        {
            if (keyname.StartsWith("LS", StringComparison.OrdinalIgnoreCase))
                return ECKeyUtil.LStick(degree);
            else
                return ECKeyUtil.RStick(degree);
        }
        else
        {
            var dk =  direction.ToUpper() switch
            {
                "UP" => DirectionKey.Up,
                "DOWN" => DirectionKey.Down,
                "LEFT" => DirectionKey.Left,
                "RIGHT" => DirectionKey.Right,
                _ => DirectionKey.None,
            };
            if (dk == DirectionKey.None)
                return null;
            if (keyname.StartsWith("LS", StringComparison.OrdinalIgnoreCase))
                return ECKeyUtil.LStick(dk);
            else
                return ECKeyUtil.RStick(dk);
        }
    }
}
