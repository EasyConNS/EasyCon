namespace EasyScript.Parsing;

public class MetaOperator
{
    public static readonly List<MetaOperator> All = new();
    public readonly Type StatementType;
    public readonly Type InstructionType;
    public readonly string Operator;
    public readonly Func<int, int, int> Function;
    public readonly bool OnlyInstant;

    public MetaOperator(Type statementType, Type instructionType, string op, Func<int, int, int> function, bool onlyInstant = false)
    {
        StatementType = statementType;
        InstructionType = instructionType;
        Operator = op;
        Function = function;
        OnlyInstant = onlyInstant;
        All.Add(this);
    }

    public static readonly MetaOperator Add = new(typeof(Statements.Add), typeof(Assembly.Instructions.AsmAdd), "+", (a, b) => a + b);
    public static readonly MetaOperator Sub = new(typeof(Statements.Sub), null, "-", (a, b) => a - b);
    public static readonly MetaOperator Mul = new(typeof(Statements.Mul), typeof(Assembly.Instructions.AsmMul), "*", (a, b) => a * b);
    public static readonly MetaOperator Div = new(typeof(Statements.Div), typeof(Assembly.Instructions.AsmDiv), "/", (a, b) => a / b);
    public static readonly MetaOperator RoundDiv = new(typeof(Statements.RoundDiv), typeof(Assembly.Instructions.AsmDiv), @"\", (a, b) => (int)Math.Round((double)a / b) );
    public static readonly MetaOperator Mod = new(typeof(Statements.Mod), typeof(Assembly.Instructions.AsmMod), "%", (a, b) => a % b);
    public static readonly MetaOperator And = new(typeof(Statements.And), typeof(Assembly.Instructions.AsmAnd), "&", (a, b) => a & b);
    public static readonly MetaOperator Or = new(typeof(Statements.Or), typeof(Assembly.Instructions.AsmOr), "|", (a, b) => a | b);
    public static readonly MetaOperator Xor = new(typeof(Statements.Xor), typeof(Assembly.Instructions.AsmXor), "^", (a, b) => a ^ b);
    public static readonly MetaOperator LShift = new(typeof(Statements.ShiftLeft), typeof(Assembly.Instructions.AsmShiftLeft), "<<", (a, b) => a << b, true);
    public static readonly MetaOperator RShift = new(typeof(Statements.ShiftRight), typeof(Assembly.Instructions.AsmShiftRight), ">>", (a, b) => a >> b, true);
}