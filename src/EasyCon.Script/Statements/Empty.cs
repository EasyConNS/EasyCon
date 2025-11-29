using EasyScript.Parsing;

namespace EasyScript.Statements;

class Empty(string text = "") : Statement
{
    private readonly string Text = text;

    protected override string _GetString(Formatter _)
    {
        return Text;
    }

    public override void Exec(Processor _)
    { }

    public override void Assemble(Assembly.Assembler _)
    { }
}
