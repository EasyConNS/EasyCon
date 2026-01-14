using System.Collections.Immutable;

namespace EasyCon.Script.Parsing;

internal sealed class FuncDeclBlock(FuncStmt declare, ImmutableArray<Statement> statements, EndFuncStmt end) : Statement
{
    public readonly FuncStmt Declare = declare;
    public ImmutableArray<Statement> Statements = statements;
    public readonly EndFuncStmt End = end;

    protected override string _GetString()
    {
        throw new NotImplementedException();
    }
}

class FuncStmt(string name) : Statement
{
    public readonly string Name = name;

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmBranch.Create());
    //    assembler.FunctionMapping[Label] = assembler.Last() as Assembly.Instructions.AsmBranch;
    //    assembler.Add(Assembly.Instructions.AsmEmpty.Create());
    //    assembler.CallMapping[Label] = assembler.Last() as Assembly.Instructions.AsmEmpty;
    //}

    protected override string _GetString()
    {
        return $"FUNC {Name}";
    }
}

class EndFuncStmt : Statement
{
    [Obsolete]
    public string Label;
    protected override string _GetString() => "ENDFUNC";

    //public override void Exec(Processor processor)
    //{
    //    processor.RetrunCall();
    //}

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmReturn.Create(0));
    //    assembler.Add(Assembly.Instructions.AsmEmpty.Create());
    //    assembler.FunctionMapping[this.Label].Target = assembler.Last();
    //}
}

class ReturnStmt : Statement
{
    protected override string _GetString() => "RETURN";

    //public override void Exec(Processor processor)
    //{
    //    processor.RetrunCall();
    //}

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmReturn.Create(0));
    //}
}

class CallStmt(string fnName, ExprBase[] args, bool buildin = true) : Statement
{
    public readonly string FnName = fnName;
    public readonly ExprBase[] Args = args;
    private readonly bool _buildin = buildin;

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    var callfunc = assembler.CallMapping.GetValueOrDefault(Label, null);
    //    assembler.Add(Assembly.Instructions.AsmCall.Create(callfunc));
    //}


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
        return $"{FnName} {string.Join(" , ", Args.Select(u => u.GetCodeText()))}".Trim();
    }
}
