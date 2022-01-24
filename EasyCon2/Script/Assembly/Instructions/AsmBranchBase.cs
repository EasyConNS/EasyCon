using System.IO;

namespace EasyCon2.Script.Assembly.Instructions
{
    abstract class AsmBranchBase : Instruction
    {
        protected enum BranchType
        {
            Force = 0b00,
            JmpTrue = 0b01,
            JmpFalse = 0b10,
            Call = 0b11,
        }

        public uint Code;
        public Instruction Target;

        protected static Instruction Create<T>(BranchType code, Instruction target = null)
            where T : AsmBranchBase, new()
        {
            var ins = new T
            {
                Code = (uint)code,
                Target = target
            };
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
            return Create<AsmBranch>(BranchType.Force, target);
        }
    }

    class AsmBranchTrue : AsmBranchBase
    {
        public static Instruction Create(Instruction target = null)
        {
            return Create<AsmBranchTrue>(BranchType.JmpTrue, target);
        }
    }

    class AsmBranchFalse : AsmBranchBase
    {
        public static Instruction Create(Instruction target = null)
        {
            return Create<AsmBranchFalse>(BranchType.JmpFalse, target);
        }
    }

    class AsmCall : AsmBranchBase
    {
        public readonly string lbl;
        public static Instruction Create(Instruction target = null)
        {
            return Create<AsmCall>(BranchType.Call, target);
        }
        public static Instruction Create(string label)
        {
            return Create<AsmCall>(BranchType.Call, null);
        }
    }

    class AsmReturn : Instruction
    {
        public uint CheckFlag;

        public static Instruction Create(uint checkflag)
        {
            var ins = new AsmReturn
            {
                CheckFlag = checkflag
            };
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
