using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon.Script.Assemble
{
    class AsmFor : Instruction
    {
        public uint LoopNumber;
        public AsmNext Next;

        public static Instruction Create(uint loopnumber, AsmNext next = null)
        {
            var ins = new AsmFor();
            ins.LoopNumber = loopnumber;
            ins.Next = next;
            return ins;
        }

        public override void WriteBytes(Stream stream)
        {
            WriteBits(stream, 0, 1);
            WriteBits(stream, 0b0010, 4);
            WriteBits(stream, Next.Address, 11);
        }
    }
}
