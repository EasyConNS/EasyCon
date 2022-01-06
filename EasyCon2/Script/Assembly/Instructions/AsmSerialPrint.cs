using System.IO;

namespace EasyCon2.Script.Assembly.Instructions
{
    class AsmSerialPrint : Instruction
    {
        public uint Mem;
        public uint Value;

        public static Instruction Create(uint mem, uint value)
        {
            if (value >= 1 << 9)
                return Failed.OutOfRange;
            var ins = new AsmSerialPrint();
            ins.Mem = mem;
            ins.Value = value;
            return ins;
        }

        public override void WriteBytes(Stream stream)
        {
            WriteBits(stream, 0, 1);
            WriteBits(stream, 0b0000, 4);
            WriteBits(stream, 1, 1);
            WriteBits(stream, Mem, 1);
            WriteBits(stream, Value, 9);
        }
    }
}