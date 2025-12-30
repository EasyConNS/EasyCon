using EasyScript.Parsing;

namespace EasyScript.Statements;

class FunctionStmt(string lbl) : Statement
{
    public readonly string Label = lbl;

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmBranch.Create());
    //    assembler.FunctionMapping[Label] = assembler.Last() as Assembly.Instructions.AsmBranch;
    //    assembler.Add(Assembly.Instructions.AsmEmpty.Create());
    //    assembler.CallMapping[Label] = assembler.Last() as Assembly.Instructions.AsmEmpty;
    //}
    [Obsolete]
    public EndFuncStat Ret = null;

    public override void Exec(Processor processor)
    {
        processor.PC = Ret.Address + 1;
    }

    protected override string _GetString()
    {
        return $"FUNC {Label}";
    }
}

class CallStat(string fnName, Param[] args, bool buildin = true) : Statement
{
    public readonly string FnName = fnName;
    public readonly Param[] Params = args;
    private readonly bool _buildin = buildin;

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    var callfunc = assembler.CallMapping.GetValueOrDefault(Label, null);
    //    assembler.Add(Assembly.Instructions.AsmCall.Create(callfunc));
    //}

    public override void Exec(Processor processor)
    {
        if (!_buildin)
        {
            processor.Call(FnName);
        }
        else
        {
            processor.BuildinCall(FnName, Params.ToArray());
        }
    }

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    throw new Assembly.AssembleException(ErrorMessage.NotSupported);

    //    //if (RegDst is ValReg)
    //    //    assembler.Add(Assembly.Instruction.CreateInstance(MetaInfo.InstructionType, (RegDst as ValReg).Index));
    //    //else
    //    //    throw new Assembly.AssembleException(ErrorMessage.NotSupported);
    //}

    protected override string _GetString()
    {
        if (!_buildin) return $"CALL {FnName}";
        return $"{FnName} {string.Join(" ", Params.Select(u => u.GetCodeText()))}".Trim();
    }
}

class Wait(ExprBase duration, bool omitted = false) : Statement
{
    protected readonly ExprBase Duration = duration;
    protected bool _omitted = omitted;

    protected override string _GetString()
    {
        if (_omitted) return $"{Duration.GetCodeText()}";
        return $"WAIT {Duration.GetCodeText()}";
    }

    public override void Exec(Processor processor)
    {
        Thread.Sleep(Duration.Get(processor));
    }

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

class AmiiboChanger(ExprBase value) : Statement
{
    protected readonly ExprBase Index = value;

    protected override string _GetString()
    {
        return $"AMIIBO {Index.GetCodeText()}";
    }

    public override void Exec(Processor processor)
    {
        var index = Index.Get(processor);
        if (index > 9)
        {
            // value must between 0~9
            return;
        }
        processor.GamePad.ChangeAmiibo((uint)index);
    }
}

class EndFuncStat : Statement
{
    [Obsolete]
    public string Label;
    protected override string _GetString() => "ENDFUNC";

    public override void Exec(Processor processor)
    {
        processor.RetrunCall();
    }

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmReturn.Create(0));
    //    assembler.Add(Assembly.Instructions.AsmEmpty.Create());
    //    assembler.FunctionMapping[this.Label].Target = assembler.Last();
    //}
}

class ReturnStat : Statement
{
    protected override string _GetString() => "RETURN";

    public override void Exec(Processor processor)
    {
        processor.RetrunCall();
    }

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmReturn.Create(0));
    //}
}
