using System.IO;

namespace EasyCon2.Script.Assembly.Instructions
{
    abstract class AsmBranchBase : Instruction
    {
        public uint Code;
        public Instruction Target;

        protected static Instruction Create<T>(uint code, Instruction target = null)
            where T : AsmBranchBase, new()
        {
            var ins = new T();
            ins.Code = code;
            ins.Target = target;
            return ins;
        }

        public override void WriteBytes(Stream stream)
        {
            var offset = (Target.Address - Address - 2) >> 1;
            if (offset < -(1 << 8) || offset >= 1 << 8)
                throw new AssembleException("跳转超出范围");
            WriteBits(stream, 0, 1);
            WriteBits(stream, 0b0110, 4);
            WriteBits(stream, Code, 2);
            WriteBits(stream, offset, 9);
        }
    }

    class AsmBranch : AsmBranchBase
    {
        public static Instruction Create(Instruction target = null)
        {
            return Create<AsmBranch>(0b00, target);
        }
    }

    class AsmBranchTrue : AsmBranchBase
    {
        public static Instruction Create(Instruction target = null)
        {
            return Create<AsmBranchTrue>(0b01, target);
        }
    }

    class AsmBranchFalse : AsmBranchBase
    {
        public static Instruction Create(Instruction target = null)
        {
            return Create<AsmBranchFalse>(0b10, target);
        }
    }

    class AsmCall : AsmBranchBase
    {
        public static Instruction Create(Instruction target = null)
        {
            return Create<AsmCall>(0b11, target);
        }
    }

    class AsmReturn : Instruction
    {
        public uint CheckFlag;

        protected static Instruction Create(uint checkflag)
        {
            var ins = new AsmReturn();
            ins.CheckFlag = checkflag;
            return ins;
        }

        public override void WriteBytes(Stream stream)
        {
            WriteBits(stream, 0, 1);
            WriteBits(stream, 0b0100, 4);
            WriteBits(stream, 0, 1);
            WriteBits(stream, 0b00, 3);
            WriteBits(stream, 0b111, 3);
            WriteBits(stream, CheckFlag, 1);
            WriteBits(stream, 0b0000, 4);
        }
    }
}
