using EasyScript.Parsing;

namespace EasyScript.Statements;

class Print : Statement
{
    private readonly Content[] Contents;
    private readonly bool CancelLineBreak;

    public Print(Content[] contents, bool cancellinebreak)
    {
        Contents = contents;
        CancelLineBreak = cancellinebreak;
    }

    protected override string _GetString(Formatter formatter)
    {
        return Contents.Length == 0 ? "PRINT" : $"PRINT {string.Join(" & ", Contents.Select(u => u.GetCodeString(formatter)))}";
    }

    public override void Exec(Processor processor)
    {
        processor.Output.Print(string.Join("", Contents.Select(u => u.GetPrintString(processor))), !processor.CancelLineBreak);
        processor.CancelLineBreak = CancelLineBreak;
    }

    public override void Assemble(Assembly.Assembler assembler)
    { }
}

class Alert : Parsing.Statement
{
    protected readonly Content[] Contents;

    public Alert(Content[] contents)
    {
        Contents = contents;
    }

    protected override string _GetString(Formatter formatter)
    {
        return Contents.Length == 0 ? "ALERT" : $"ALERT {string.Join(" & ", Contents.Select(u => u.GetCodeString(formatter)))}";
    }

    public override void Exec(Processor processor)
    {
        processor.Output.Alert(string.Join("", Contents.Select(u => u.GetPrintString(processor))));
    }

    public override void Assemble(Assembly.Assembler assembler)
    { }
}