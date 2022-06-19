using System;
using System.Collections.Generic;
using System.Linq;

namespace ECDevice
{
    public class SwitchReport : ICloneable
    {
        public ushort Button;
        public byte HAT;
        public byte LX;
        public byte LY;
        public byte RX;
        public byte RY;

        public SwitchReport()
        {
            Reset();
        }

        public void Reset()
        {
            Button = 0;
            HAT = (byte)SwitchHAT.CENTER;
            LX = NSwitchUtil.STICK_CENTER;
            LY = NSwitchUtil.STICK_CENTER;
            RX = NSwitchUtil.STICK_CENTER;
            RY = NSwitchUtil.STICK_CENTER;
        }

        public byte[] GetBytes(bool raw = false)
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
            if (raw)
                return serialized.ToArray();

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
            packet[packet.Count - 1] |= 0x80;
            return packet.ToArray();
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

        public override string ToString()
        {
            var list = new List<string>();
            foreach (SwitchButton button in Enum.GetValues(typeof(SwitchButton)))
                if ((Button & (ushort)button) != 0)
                    list.Add(button.GetName());
            if (HAT != (byte)SwitchHAT.CENTER)
                list.Add($"HAT.{((SwitchHAT)HAT).GetName()}");
            if (LX != NSwitchUtil.STICK_CENTER || LY != NSwitchUtil.STICK_CENTER)
                list.Add($"LS({LX},{LY})");
            if (RX != NSwitchUtil.STICK_CENTER || RY != NSwitchUtil.STICK_CENTER)
                list.Add($"RS({RX},{RY})");
            return string.Join(" ", list);
        }
    }
}
