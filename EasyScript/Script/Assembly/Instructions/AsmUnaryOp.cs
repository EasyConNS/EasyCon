using System.IO;

namespace EasyScript.Assembly.Instructions
{
    abstract class AsmUnaryOp : Instruction
    {
        protected enum Operator
        {
            Negative = 0b0010,
            Not = 0b0011,
            Push = 0b0100,
            Pop = 0b0101,
            LoadFor = 0b0110,
            StoreOp = 0b0111,
            Bool = 0b1000,
            Rand = 0b1001,
        }

        public uint Op;
        public uint Reg;

        protected static Instruction Create<T>(Operator op, uint reg)
            where T : AsmUnaryOp, new()
        {
            var ins = new T
            {
                Op = (uint)op,
                Reg = reg
            };
            return ins;
        }

        public override void WriteBytes(Stream stream)
        {
            WriteBits(stream, 0, 1);
            WriteBits(stream, 0b0101, 4);
            WriteBits(stream, 0b111, 3);
            WriteBits(stream, 1, 1);
            WriteBits(stream, Op, 4);
            WriteBits(stream, Reg, 3);
        }
    }

    class AsmNegative : AsmUnaryOp
    {
        public static Instruction Create(uint reg)
        {
            return Create<AsmNegative>(Operator.Negative, reg);
        }
    }

    class AsmNot : AsmUnaryOp
    {
        public static Instruction Create(uint reg)
        {
            return Create<AsmNot>(Operator.Not, reg);
        }
    }

    class AsmPush : AsmUnaryOp
    {
        public static Instruction Create(uint reg)
        {
            return Create<AsmPush>(Operator.Push, reg);
        }
    }

    class AsmPop : AsmUnaryOp
    {
        public static Instruction Create(uint reg)
        {
            return Create<AsmPop>(Operator.Pop, reg);
        }
    }

    class AsmStoreOp : AsmUnaryOp
    {
        public static Instruction Create(uint reg)
        {
            return Create<AsmStoreOp>(Operator.StoreOp, reg);
        }
    }

    class AsmBool : AsmUnaryOp
    {
        public static Instruction Create(uint reg)
        {
            return Create<AsmBool>(Operator.Bool, reg);
        }
    }

    class AsmRand : AsmUnaryOp
    {
        public static Instruction Create(uint reg)
        {
            return Create<AsmRand>(Operator.Rand, reg);
        }
    }
}
