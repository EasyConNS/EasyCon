using System.Collections.Immutable;

namespace EasyCon.Script.Parsing;

internal sealed class FuncDeclBlock(FuncStmt declare, ImmutableArray<Statement> statements, EndBlockStmt end) : Statement
{
    public readonly FuncStmt Declare = declare;
    public ImmutableArray<Statement> Statements = statements;
    public readonly EndBlockStmt End = end;

    protected override string _GetString()
    {
        throw new NotImplementedException();
    }
}

class FuncStmt(string name, ImmutableArray<VariableExpr> paramters) : Statement
{
    public readonly string Name = name;
    public ImmutableArray<VariableExpr> Paramters = paramters;

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmBranch.Create());
    //    assembler.FunctionMapping[Label] = assembler.Last() as Assembly.Instructions.AsmBranch;
    //    assembler.Add(Assembly.Instructions.AsmEmpty.Create());
    //    assembler.CallMapping[Label] = assembler.Last() as Assembly.Instructions.AsmEmpty;
    //}

    protected override string _GetString()
    {
        var parm = string.Join(", ", Paramters.Select(arg => arg.GetCodeText()));
        parm = Paramters.Length == 0 ? "" : $"({parm})";
        return $"FUNC {Name}{parm}";
    }
}

class EndFuncStmt : EndBlockStmt
{
    protected override string _GetString() => "ENDFUNC";

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

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmReturn.Create(0));
    //}
}

class CallStmt(string fnName, ExprBase[] args) : Statement
{
    public readonly string FnName = fnName;
    public readonly ExprBase[] Args = args;

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
        if (Args.Length == 0) return $"CALL {FnName}";
        return $"{FnName} {string.Join(" , ", Args.Select(u => u.GetCodeText()))}".Trim();
    }
}
