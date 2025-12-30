namespace EasyScript.Statements;

class SerialPrint : Statement
{
    public readonly uint Value;
    public readonly bool Mem;

    public SerialPrint(uint value, bool mem)
    {
        Value = value;
        Mem = mem;
    }

    protected override string _GetString()
    {
        return Mem ? $"SMEM {Value}" : $"SPRINT {Value}";
    }

    public override void Exec(Processor _) { }

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmSerialPrint.Create(Mem ? 1u : 0, Value));
    //}
}
