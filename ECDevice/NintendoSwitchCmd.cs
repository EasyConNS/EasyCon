using System;
using System.Collections.Generic;

namespace ECDevice
{
    public partial class NintendoSwitch
    {
        public bool Flash(byte[] asmBytes)
        {
            const int PacketSize = 20;
            List<byte> list = new List<byte>(asmBytes);
            for (int i = 0; i < list.Count; i += PacketSize)
            {
                int len = Math.Min(PacketSize, list.Count - i);
                var packet = list.GetRange(i, len).ToArray();
                while (true)
                {
                    if (!SendSync(
                            b => b == Reply.FlashStart,
                            200,
                            Command.Ready,
                            (byte)(i & 0x7F),
                            (byte)(i >> 7),
                            (byte)(len & 0x7F),
                            (byte)(len >> 7),
                            Command.Flash)
                        || !SendSync(
                            b => b == Reply.FlashEnd,
                            200,
                            packet)
                        )
                    {
                        // error, retry
                        if (!ResetControl())
                            return false;
                        continue;
                    }
                    break;
                }
            }
            return true;
        }

        public bool RemoteStart()
        {
            return SendSync(b => b == Reply.ScriptAck, 200, Command.Ready, Command.ScriptStart);
        }

        public bool RemoteStop()
        {
            return SendSync(b => b == Reply.ScriptAck, 200, Command.Ready, Command.ScriptStop);
        }

        public int GetVersion()
        {
            int ver = -1;
            SendSync(b =>
            {
                if (b >= 0x40 && b <= 0x80)
                {
                    ver = b;
                    return true;
                }
                return false;
            }, 200,
            Command.Ready,
            Command.Version);
            return ver;
        }

        public bool TriggerLED()
        {
            return SendSync(b => b == 0, 200, Command.Ready, Command.LED);
        }
    }
}
