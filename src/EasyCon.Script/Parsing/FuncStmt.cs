using EasyCon.Script.Binding;
using System.Collections.Immutable;

namespace EasyCon.Script.Syntax;

internal sealed class FuncDeclBlock(FuncStmt declare, ImmutableArray<Statement> statements, EndBlockStmt end) : Statement(declare.Syntax)
{
    public override StatementKind Kind => StatementKind.FuncDeclBlock;
    public readonly FuncStmt Declare = declare;
    public ImmutableArray<Statement> Statements = statements;
    public readonly EndBlockStmt End = end;

    protected override string _GetString()
    {
        throw new NotImplementedException();
    }
}

internal sealed class TypeClauseSyntax(Token colonToken, Token identifier)
{
    public readonly Token Colon = colonToken;
    public readonly Token Identifier = identifier;
}

class FuncStmt(Token identifier, ImmutableArray<ParameterSyntax> paramters, bool omitParn, TypeClauseSyntax? type) : StartBlockStmt(identifier)
{
    public override StatementKind Kind => StatementKind.FuncStmt;
    public readonly Token Identifier = identifier;
    public string Name => Identifier.Value;
    public ImmutableArray<ParameterSyntax> Paramters = paramters;
    public readonly TypeClauseSyntax? Type = type;

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmBranch.Create());
    //    assembler.FunctionMapping[Label] = assembler.Last() as Assembly.Instructions.AsmBranch;
    //    assembler.Add(Assembly.Instructions.AsmEmpty.Create());
    //    assembler.CallMapping[Label] = assembler.Last() as Assembly.Instructions.AsmEmpty;
    //}

    protected override string _GetString()
    {
        var parm = string.Join(", ", Paramters.Select(arg => arg.ToString()));
        parm = Paramters.Length == 0 && !omitParn ? "" : $"({parm})";
        var type = Type == null ? "" : $": {Type.Identifier.Value.ToUpper()}";
        return $"FUNC {Name}{parm}{type}";
    }
}

internal sealed class ParameterSyntax(VariableExpr varExpr, TypeClauseSyntax? type)
{
    public readonly VariableExpr Identifier = varExpr;
    public readonly TypeClauseSyntax? Type = type;

    public override string ToString()
    {
        var type = Type == null ? "" : $": {Type.Identifier.Value.ToUpper()}";
        return Identifier.GetCodeText() + type;
    }
}

class EndFuncStmt(Token syntax) : EndBlockStmt(syntax)
{
    public override StatementKind Kind => StatementKind.EndFuncStmt;
    protected override string _GetString() => "ENDFUNC";

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmReturn.Create(0));
    //    assembler.Add(Assembly.Instructions.AsmEmpty.Create());
    //    assembler.FunctionMapping[this.Label].Target = assembler.Last();
    //}
}

class ReturnStmt(Token syntax, ExprBase? expression = null) : Statement(syntax)
{
    public readonly ExprBase? Expression = expression;
    protected override string _GetString()
    {
        return $"RETURN {Expression?.GetCodeText()}".TrimEnd();
    }

    //public override void Assemble(Assembly.Assembler assembler)
    //{
    //    assembler.Add(Assembly.Instructions.AsmReturn.Create(0));
    //}
}

class CallStmt(Token syntax, string fnName, ExprBase[] args, CallType callType = CallType.CallStmt) : Statement(syntax)
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
        var name = FnName;
        if (BuiltinFunctions.GetAll().Select(f => f.Name).Contains(FnName.ToUpper()))
        {
            name = FnName.ToUpper();
        }
        return callType switch
        {
            CallType.CallStmt => $"CALL {name}",
            _ => $"{name} {string.Join(", ", Args.Select(u => u.GetCodeText()))}".Trim(),
        };

    }
}

enum CallType
{
    CallStmt,
    CallStmtWithArgs,
    CallExpression
}