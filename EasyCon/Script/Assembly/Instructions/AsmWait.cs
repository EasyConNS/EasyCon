using EasyCon.Script.Assembly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon.Script.Assembly.Instructions
{
    abstract class AsmWait : Instruction
    {
        public abstract int RealDuration { get; }

        public static Instruction Create(int duration)
        {
            if (duration < 0)
                return AsmEmpty.Create();
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
        public override int RealDuration => Duration * Unit;
        public const int Unit = 10;
        public int Duration;

        public static Instruction Create(int duration)
        {
            Scale(ref duration, Unit);
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
        public override int RealDuration => Duration * Unit;
        public const int Unit = 10;
        public int Duration;

        public override int ByteCount => 4;

        public static Instruction Create(int duration)
        {
            Scale(ref duration, Unit);
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
        public override int RealDuration => Duration;
        public int Duration;

        public static Instruction Create(int duration)
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
