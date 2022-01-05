using EasyCon2.Script.Assembly;
using EasyCon2.Script.Parsing;
using ECDevice;

namespace EasyCon2.Script
{
    internal static class ScripterUtil
    {
        public static int GetDirectionIndex(NintendoSwitch.Key key)
        {
            int x = key.StickX;
            int y = key.StickY;
            if (x == NintendoSwitch.STICK_CENTER && y == NintendoSwitch.STICK_CENTER)
                return -1;
            x = (int)Math.Round(x / 32d);
            y = (int)Math.Round(y / 32d);
            return x >= y ? x + y : 32 - x - y;
        }

        public static Instruction Assert(Instruction ins)
        {
            if (ins is Instruction.Failed)
                throw new AssembleException((ins as Instruction.Failed).Message);
            return ins;
        }

        public static NintendoSwitch.Key GetKey(string keyname, string direction = "0")
        {
            if (int.TryParse(direction, out int degree))
            {
                if (keyname.Equals("LS", StringComparison.OrdinalIgnoreCase))
                    return NintendoSwitch.Key.LStick(degree);
                else
                    return NintendoSwitch.Key.RStick(degree);
            }
            else
            {
                var dk = NSKeys.GetDirection(direction);
                if (dk == NintendoSwitch.DirectionKey.None)
                    return null;
                if (keyname.Equals("LS", StringComparison.OrdinalIgnoreCase))
                    return NintendoSwitch.Key.LStick(dk);
                else
                    return NintendoSwitch.Key.RStick(dk);
            }
        }

    }
}
