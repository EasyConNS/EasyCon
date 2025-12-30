using EasyScript.Parsing;

namespace EasyScript.Statements;

abstract class Param
{
    public abstract string GetCodeText();
}

class TextVarParam(List<Param> param) : Param
{
    public readonly List<Param> Params = param;

    public override string GetCodeText()
    {
        return string.Join(" & ", Params.Select(u => u.GetCodeText()));
    }
}

class TextParam(string text) : Param
{
    public readonly string Text = text;

    public override string GetCodeText() => Text;
}

class LiterParam(InstantExpr litr) : Param
{
    public readonly InstantExpr LITR = litr;

    public override string GetCodeText() => LITR.GetCodeText();
}

class RegParam(VariableExpr reg) : Param
{
    public readonly VariableExpr Reg = reg;

    public override string GetCodeText() => Reg.GetCodeText();
}
