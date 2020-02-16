using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon.Script.Assemble
{
    abstract class AsmStick : Instruction, IAsmKey
    {
        public uint KeyCode { get; set; }
        public uint LR => KeyCode & 1;
        public uint DIndex;
    }

    class AsmStick_Standard : AsmStick
    {
        public uint Duration;

        public static Instruction Create(uint keycode, uint dindex, uint duration)
        {
            Scale(ref duration, 50);
            if (duration >= 1 << 7)
                return Failed.OutOfRange;
            var ins = new AsmStick_Standard();
            ins.KeyCode = keycode;
            ins.DIndex = dindex;
            ins.Duration = duration;
            return ins;
        }

        public override void WriteBytes(Stream stream)
        {
            WriteBits(stream, 0b11, 2);
            WriteBits(stream, LR, 1);
            WriteBits(stream, DIndex, 5);
            WriteBits(stream, 0, 1);
            WriteBits(stream, Duration, 7);
        }
    }

    class AsmStick_Hold : AsmStick, IAsmHold
    {
        public uint HoldUntil { get; set; }

        public static Instruction Create(uint keycode, uint dindex)
        {
            var ins = new AsmStick_Hold();
            ins.KeyCode = keycode;
            ins.DIndex = dindex;
            return ins;
        }

        public override void WriteBytes(Stream stream)
        {
            WriteBits(stream, 0b11, 2);
            WriteBits(stream, LR, 1);
            WriteBits(stream, DIndex, 5);
            WriteBits(stream, 1, 1);
            WriteBits(stream, HoldUntil, 7);
        }
    }

    class AsmStick_Reset : AsmStick
    {
        public override uint InsCount => 0;
        public override uint ByteCount => 0;

        public static Instruction Create(uint keycode)
        {
            var ins = new AsmStick_Reset();
            ins.KeyCode = keycode;
            return ins;
        }

        public override void WriteBytes(Stream stream)
        { }
    }
}
