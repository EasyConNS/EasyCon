using System.IO;

namespace EasyCon2.Script.Assembly.Instructions
{
    abstract class AsmStick : Instruction
    {
        public int KeyCode;
        public int LR => KeyCode & 1;
        public int DIndex;
    }

    class AsmStick_Standard : AsmStick
    {
        public int Duration;

        public static Instruction Create(int keycode, int dindex, int duration)
        {
            Scale(ref duration, 50);
            if (duration < 0)
                return AsmEmpty.Create();
            if (duration >= 1 << 7)
                return Failed.OutOfRange;
            var ins = new AsmStick_Standard
            {
                KeyCode = keycode,
                DIndex = dindex,
                Duration = duration
            };
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

    class AsmStick_Hold : AsmStick
    {
        public Instruction HoldUntil;

        public static Instruction Create(int keycode, int dindex)
        {
            var ins = new AsmStick_Hold();
            ins.KeyCode = keycode;
            ins.DIndex = dindex;
            return ins;
        }

        public override void WriteBytes(Stream stream)
        {
            var offset = HoldUntil?.Index - Index ?? 0;
            if (offset >= 1 << 7)
                offset = 0;
            WriteBits(stream, 0b11, 2);
            WriteBits(stream, LR, 1);
            WriteBits(stream, DIndex, 5);
            WriteBits(stream, 1, 1);
            WriteBits(stream, offset, 7);
        }
    }
}
