namespace EasyCon.Script.Assembly.Instructions;

abstract class AsmCompare : Instruction
{
    public enum AssignType
    {
        Assign = 0b00,
        And = 0b01,
        Or = 0b10,
        Xor = 0b11,
    }

    public uint Code;
    public AssignType Type;
    public uint Reg0;
    public uint Reg1;

    protected static Instruction Create<T>(uint code, AssignType type, uint reg0, uint reg1)
        where T : AsmCompare, new()
    {
        var ins = new T();
        ins.Code = code;
        ins.Type = type;
        ins.Reg0 = reg0;
        ins.Reg1 = reg1;
        return ins;
    }

    public override void WriteBytes(Stream stream)
    {
        WriteBits(stream, 0, 1);
        WriteBits(stream, 0b0100, 4);
        WriteBits(stream, 1, 1);
        WriteBits(stream, Code, 2);
        WriteBits(stream, (uint)Type, 2);
        WriteBits(stream, Reg0, 3);
        WriteBits(stream, Reg1, 3);
    }
}

class AsmEqual : AsmCompare
{
    public static Instruction Create(AssignType type, uint reg0, uint reg1)
    {
        return Create<AsmEqual>(0b00, type, reg0, reg1);
    }
}

class AsmNotEqual : AsmCompare
{
    public static Instruction Create(AssignType type, uint reg0, uint reg1)
    {
        return Create<AsmNotEqual>(0b01, type, reg0, reg1);
    }
}

class AsmLessThan : AsmCompare
{
    public static Instruction Create(AssignType type, uint reg0, uint reg1)
    {
        return Create<AsmLessThan>(0b10, type, reg0, reg1);
    }
}

class AsmLessOrEqual : AsmCompare
{
    public static Instruction Create(AssignType type, uint reg0, uint reg1)
    {
        return Create<AsmLessOrEqual>(0b11, type, reg0, reg1);
    }
}
