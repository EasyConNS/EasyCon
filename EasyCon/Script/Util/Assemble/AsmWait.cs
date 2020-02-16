using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon.Script.Assemble
{
    abstract class AsmWait : Instruction
    {
        public static Instruction Create(uint duration)
        {
            var ins = AsmWait_HighPrecision.Create(duration);
            if (ins.Success)
                return ins;
            ins = AsmWait_Standard.Create(duration);
            if (ins.Success)
                return ins;
            return AsmWait_Extended.Create(duration);
        }
    }

    class AsmWait_Standard : AsmWait
    {
        public uint Duration;

        public static Instruction Create(uint duration)
        {
            Scale(ref duration, 10);
            if (duration >= 1 << 10)
                return Failed.OutOfRange;
            var ins = new AsmWait_Standard();
            ins.Duration = duration;
            return ins;
        }

        public override void WriteBytes(Stream stream)
        {
            WriteBits(stream, 0, 1);
            WriteBits(stream, 0b0001, 4);
            WriteBits(stream, 0, 1);
            WriteBits(stream, Duration, 10);
        }
    }

    class AsmWait_Extended : AsmWait
    {
        public uint Duration;

        public override uint ByteCount => 4;

        public static Instruction Create(uint duration)
        {
            Scale(ref duration, 10);
            if (duration >= 1 << 25)
                return Failed.OutOfRange;
            var ins = new AsmWait_Extended();
            ins.Duration = duration;
            return ins;
        }

        public override void WriteBytes(Stream stream)
        {
            WriteBits(stream, 0, 1);
            WriteBits(stream, 0b0001, 4);
            WriteBits(stream, 1, 1);
            WriteBits(stream, 0, 1);
            WriteBits(stream, Duration, 25);
        }
    }

    class AsmWait_HighPrecision : AsmWait
    {
        public uint Duration;

        public static Instruction Create(uint duration)
        {
            if (duration >= 1 << 9)
                return Failed.OutOfRange;
            var ins = new AsmWait_HighPrecision();
            ins.Duration = duration;
            return ins;
        }

        public override void WriteBytes(Stream stream)
        {
            WriteBits(stream, 0, 1);
            WriteBits(stream, 0b0001, 4);
            WriteBits(stream, 1, 1);
            WriteBits(stream, 1, 1);
            WriteBits(stream, Duration, 9);
        }
    }
}
