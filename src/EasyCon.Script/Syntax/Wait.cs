namespace EasyCon.Script.Syntax;

class Wait(Token syntax, BaseExpr duration, bool omitted = false) : Statement(syntax)
{
    public readonly BaseExpr Duration = duration;
    protected bool _omitted = omitted;

    protected override string _GetString()
    {
        if (_omitted) return $"{Duration.GetCodeText()}";
        return $"WAIT {Duration.GetCodeText()}";
    }

}