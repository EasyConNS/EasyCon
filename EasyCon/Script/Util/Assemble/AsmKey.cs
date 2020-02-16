using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon.Script.Assemble
{
    abstract class AsmKey : Instruction, IAsmKey
    {
        public uint KeyCode { get; set; }
    }

    class AsmKey_Standard : AsmKey
    {
        public uint Duration;

        public static Instruction Create(uint keycode, uint duration)
        {
            Scale(ref duration, 10);
            if (duration >= 1 << 8)
                return Failed.OutOfRange;
            var ins = new AsmKey_Standard();
            ins.KeyCode = keycode;
            ins.Duration = duration;
            return ins;
        }

        public override void WriteBytes(Stream stream)
        {
            WriteBits(stream, 0b10, 2);
            WriteBits(stream, KeyCode, 5);
            WriteBits(stream, 0, 1);
            WriteBits(stream, Duration, 8);
        }
    }

    class AsmKey_Compressed : AsmKey
    {
        public uint WaitTime;

        public override uint InsCount => 2;

        public static Instruction Create(uint keycode, uint duration, uint waittime)
        {
            if (duration != 50)
                return Failed.InvalidArgument;
            if (waittime == 0 || !Scale(ref waittime, 50))
                return Failed.InvalidArgument;
            if (waittime >= 1 << 7)
                return Failed.OutOfRange;
            var ins = new AsmKey_Compressed();
            ins.KeyCode = keycode;
            ins.WaitTime = waittime;
            return ins;
        }

        public override void WriteBytes(Stream stream)
        {
            WriteBits(stream, 0b10, 2);
            WriteBits(stream, KeyCode, 5);
            WriteBits(stream, 0b10, 2);
            WriteBits(stream, WaitTime, 7);
        }
    }

    class AsmKey_Hold : AsmKey, IAsmHold
    {
        public uint HoldUntil { get; set; }

        public static Instruction Create(uint keycode)
        {
            var ins = new AsmKey_Hold();
            ins.KeyCode = keycode;
            return ins;
        }

        public override void WriteBytes(Stream stream)
        {
            WriteBits(stream, 0b10, 2);
            WriteBits(stream, KeyCode, 5);
            WriteBits(stream, 0b11, 2);
            WriteBits(stream, HoldUntil, 7);
        }
    }

    class AsmKey_Release : AsmKey
    {
        public override uint InsCount => 0;
        public override uint ByteCount => 0;

        public static Instruction Create(uint keycode)
        {
            var ins = new AsmKey_Release();
            ins.KeyCode = keycode;
            return ins;
        }

        public override void WriteBytes(Stream stream)
        { }
    }
}
