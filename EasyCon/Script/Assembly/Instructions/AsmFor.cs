using EasyCon.Script.Assembly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyCon.Script.Assembly.Instructions
{
    class AsmFor : Instruction
    {
        public AsmNext Next;

        public static Instruction Create(AsmNext next = null)
        {
            var ins = new AsmFor();
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

    abstract class AsmNext : Instruction
    {
        public int LoopNumber;

        public static Instruction Create(int loopnumber)
        {
            var ins = AsmNext_Small.Create(loopnumber);
            if (ins.Success)
                return ins;
            return AsmNext_Large.Create(loopnumber);
        }
    }

    class AsmNext_Small : AsmNext
    {
        public static new Instruction Create(int loopnumber)
        {
            if (loopnumber >= 1 << 10)
                return Failed.OutOfRange;
            var ins = new AsmNext_Small();
            ins.LoopNumber = loopnumber;
            return ins;
        }

        public override void WriteBytes(Stream stream)
        {
            WriteBits(stream, 0, 1);
            WriteBits(stream, 0b0011, 4);
            WriteBits(stream, 0, 1);
            WriteBits(stream, LoopNumber, 10);
        }
    }

    class AsmNext_Large : AsmNext
    {
        public override int ByteCount => 4;

        public static new Instruction Create(int loopnumber)
        {
            if (loopnumber >= 1 << 26)
                return Failed.OutOfRange;
            var ins = new AsmNext_Large();
            ins.LoopNumber = loopnumber;
            return ins;
        }

        public override void WriteBytes(Stream stream)
        {
            WriteBits(stream, 0, 1);
            WriteBits(stream, 0b0011, 4);
            WriteBits(stream, 1, 1);
            WriteBits(stream, LoopNumber, 26);
        }
    }

    abstract class AsmLoopControl : Instruction
    {
        public uint Code;
        public uint CheckFlag;
        public int Level;

        protected static Instruction Create<T>(uint code, uint checkflag, int level)
            where T : AsmLoopControl, new()
        {
            if (level < 0 || level >= 1 << 4)
                return Failed.OutOfRange;
            var ins = new T();
            ins.Code = code;
            ins.CheckFlag = checkflag;
            ins.Level = level;
            return ins;
        }

        public override void WriteBytes(Stream stream)
        {
            WriteBits(stream, 0, 1);
            WriteBits(stream, 0b0100, 4);
            WriteBits(stream, 0, 1);
            WriteBits(stream, 0b00, 2);
            WriteBits(stream, Code, 3);
            WriteBits(stream, CheckFlag, 1);
            WriteBits(stream, Level, 4);
        }
    }

    class AsmBreak : AsmLoopControl
    {
        public static Instruction Create(uint checkflag, int level)
        {
            return Create<AsmBreak>(0b000, checkflag, level);
        }
    }

    class AsmContinue : AsmLoopControl
    {
        public static Instruction Create(uint checkflag, int level)
        {
            return Create<AsmContinue>(0b001, checkflag, level);
        }
    }
}
