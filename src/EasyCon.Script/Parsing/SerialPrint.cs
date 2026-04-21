namespace EasyCon.Script.Syntax;

class SerialPrint(uint value, bool mem) : Statement(null!)
{
    public readonly uint Value = value;
    public readonly bool Mem = mem;

    protected override string _GetString()
    {
        return Mem ? $"SMEM {Value}" : $"SPRINT {Value}";
    }

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmSerialPrint.Create(Mem ? 1u : 0, Value));
    //}
}