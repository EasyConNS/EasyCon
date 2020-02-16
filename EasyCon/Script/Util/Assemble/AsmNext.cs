using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon.Script.Assemble
{
    abstract class AsmNext : Instruction
    {
        public AsmFor For;

        public static Instruction Create(AsmFor @for)
        {
            var ins = AsmNext_Small.Create(@for);
            if (ins.Success)
                return ins;
            return AsmNext_Large.Create(@for);
        }
    }

    class AsmNext_Small : AsmNext
    {
        public static Instruction Create(AsmFor @for)
        {
            var loopnumber = @for.LoopNumber;
            if (loopnumber >= 1 << 10)
                return Failed.OutOfRange;
            var ins = new AsmNext_Small();
            ins.For = @for;
            return ins;
        }

        public override void WriteBytes(Stream stream)
        {
            WriteBits(stream, 0, 1);
            WriteBits(stream, 0b0011, 4);
            WriteBits(stream, 0, 1);
            WriteBits(stream, For.LoopNumber, 10);
        }
    }

    class AsmNext_Large : AsmNext
    {
        public override uint ByteCount => 4;

        public static Instruction Create(AsmFor @for)
        {
            var loopnumber = @for.LoopNumber;
            if (loopnumber >= 1 << 26)
                return Failed.OutOfRange;
            var ins = new AsmNext_Large();
            ins.For = @for;
            return ins;
        }

        public override void WriteBytes(Stream stream)
        {
            WriteBits(stream, 0, 1);
            WriteBits(stream, 0b0011, 4);
            WriteBits(stream, 1, 1);
            WriteBits(stream, For.LoopNumber, 26);
        }
    }
}
