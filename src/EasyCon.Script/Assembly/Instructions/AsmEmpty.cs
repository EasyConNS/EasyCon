namespace EasyCon.Script.Assembly.Instructions;

class AsmEmpty : Instruction
{
    public override int ByteCount => 0;

    public override int InsCount => 0;

    public static Instruction Create()
    {
        return new AsmEmpty();
    }

    public override void WriteBytes(Stream stream)
    { }
}

class AsmLabel : Instruction
{
    public override int ByteCount => 0;

    public override int InsCount => 0;

    public string Label { get; set; } = string.Empty;

    public static Instruction Create(string lbl)
    {
        return new AsmLabel() { Label  = lbl};
    }

    public override void WriteBytes(Stream stream)
    { }
}
