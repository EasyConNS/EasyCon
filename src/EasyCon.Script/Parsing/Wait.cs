namespace EasyCon.Script.Parsing;

class Wait(ExprBase duration, bool omitted = false) : Statement
{
    protected readonly ExprBase Duration = duration;
    protected bool _omitted = omitted;

    protected override string _GetString()
    {
        if (_omitted) return $"{Duration.GetCodeText()}";
        return $"WAIT {Duration.GetCodeText()}";
    }

    //public override void Exec(Processor processor)
    //{
    //    Thread.Sleep(Duration.Get(processor));
    //}

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    if (Duration is VariableExpr rd)
    //    {
    //        assembler.Add(Assembly.Instructions.AsmStoreOp.Create(rd.Reg));
    //        assembler.Add(Assembly.Instructions.AsmWait.Create(0));
    //    }
    //    else if (Duration is InstantExpr id)
    //        assembler.Add(Assembly.Instructions.AsmWait.Create(id.Val));
    //    else
    //        throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    //}
}