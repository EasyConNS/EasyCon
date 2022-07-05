using System.IO;

namespace EasyScript.Assembly.Instructions
{
    abstract class AsmKey : Instruction
    {
        public int KeyCode;
    }

    class AsmKey_Standard : AsmKey
    {
        public int RealDuration => Duration * Unit;
        public const int Unit = 10;
        public int Duration;

        public static Instruction Create(int keycode, int duration)
        {
            Scale(ref duration, Unit);
            if (duration < 0)
                return AsmEmpty.Create();
            if (duration >= 1 << 8)
                return Failed.OutOfRange;
            var ins = new AsmKey_Standard
            {
                KeyCode = keycode,
                Duration = duration
            };
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
        public int WaitTime;

        public override int InsCount => 2;

        public static Instruction Create(int keycode, int duration, int waittime)
        {
            if (duration != 50)
                return Failed.InvalidArgument;
            if (waittime < 0)
                waittime = 0;
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

    class AsmKey_Hold : AsmKey
    {
        public Instruction HoldUntil;

        public static Instruction Create(int keycode)
        {
            var ins = new AsmKey_Hold();
            ins.KeyCode = keycode;
            return ins;
        }

        public override void WriteBytes(Stream stream)
        {
            var offset = HoldUntil?.Index - Index ?? 0;
            if (offset >= 1 << 7)
                offset = 0;
            WriteBits(stream, 0b10, 2);
            WriteBits(stream, KeyCode, 5);
            WriteBits(stream, 0b11, 2);
            WriteBits(stream, offset, 7);
        }
    }
}
