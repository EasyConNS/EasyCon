using EasyScript.Parsing;

namespace EasyScript.Statements;

abstract class Param
{
    public abstract string GetCodeText();
}

class TextParam(string text, string codetext = null) : Param
{
    public readonly string Text = text;
    public readonly string CodeText = codetext ?? text;

    public override string GetCodeText()
    {
        return CodeText;
    }
}

class RegParam(ValReg reg) : Param
{
    public readonly ValReg Reg = reg;

    public override string GetCodeText()
    {
        return Reg.GetCodeText();
    }
}
