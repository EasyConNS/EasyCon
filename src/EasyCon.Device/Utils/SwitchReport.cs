namespace EasyDevice;

public class SwitchReport : ICloneable
{
    public ushort Button = 0;
    public byte HAT = (byte)SwitchHAT.CENTER;
    public byte LX = SwitchStick.STICK_CENTER;
    public byte LY = SwitchStick.STICK_CENTER;
    public byte RX = SwitchStick.STICK_CENTER;
    public byte RY = SwitchStick.STICK_CENTER;

    public void Reset()
    {
        Button = 0;
        HAT = (byte)SwitchHAT.CENTER;
        LX = SwitchStick.STICK_CENTER;
        LY = SwitchStick.STICK_CENTER;
        RX = SwitchStick.STICK_CENTER;
        RY = SwitchStick.STICK_CENTER;
    }

    public byte[] GetBytes()
    {
        // Protocal packet structure:
        // bit 7 (highest):    0 = data byte, 1 = end flag
        // bit 6~0:            data (Big-Endian)
        // serialize data
        var serialized = new List<byte>();
        serialized.AddRange(BitConverter.GetBytes(Button).Reverse());
        serialized.Add(HAT);
        serialized.Add(LX);
        serialized.Add(LY);
        serialized.Add(RX);
        serialized.Add(RY);

        // generate packet
        var packet = new List<byte>();
        long n = 0;
        int bits = 0;
        foreach (var b in serialized)
        {
            n = (n << 8) | b;
            bits += 8;
            while (bits >= 7)
            {
                bits -= 7;
                packet.Add((byte)(n >> bits));
                n &= (1 << bits) - 1;
            }
        }
        packet[^1] |= 0x80;
        return [.. packet];
    }

    public object Clone()
    {
        return new SwitchReport
        {
            Button = Button,
            HAT = HAT,
            LX = LX,
            LY = LY,
            RX = RX,
            RY = RY,
        };
    }
}
