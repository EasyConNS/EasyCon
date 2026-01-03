namespace EasyCon.Script.Parsing;

public class MetaOperator
{
    public static readonly List<MetaOperator> All = new();
    public readonly Type InstructionType;
    public readonly string Operator;
    public readonly Func<int, int, int> Function;
    public readonly bool OnlyInstant;

    public MetaOperator(Type instructionType, string op, Func<int, int, int> function, bool onlyInstant = false)
    {
        InstructionType = instructionType;
        Operator = op;
        Function = function;
        OnlyInstant = onlyInstant;
        All.Add(this);
    }

    public static readonly MetaOperator Add = new(typeof(Assembly.Instructions.AsmAdd), "+", (a, b) => a + b);
    public static readonly MetaOperator Sub = new(null, "-", (a, b) => a - b);
    public static readonly MetaOperator Mul = new(typeof(Assembly.Instructions.AsmMul), "*", (a, b) => a * b);
    public static readonly MetaOperator Div = new(typeof(Assembly.Instructions.AsmDiv), "/", (a, b) => a / b);
    public static readonly MetaOperator RoundDiv = new(typeof(Assembly.Instructions.AsmDiv), @"\", (a, b) => (int)Math.Round((double)a / b) );
    public static readonly MetaOperator Mod = new(typeof(Assembly.Instructions.AsmMod), "%", (a, b) => a % b);
    public static readonly MetaOperator And = new(typeof(Assembly.Instructions.AsmAnd), "&", (a, b) => a & b);
    public static readonly MetaOperator Or = new(typeof(Assembly.Instructions.AsmOr), "|", (a, b) => a | b);
    public static readonly MetaOperator Xor = new(typeof(Assembly.Instructions.AsmXor), "^", (a, b) => a ^ b);
    public static readonly MetaOperator LShift = new(typeof(Assembly.Instructions.AsmShiftLeft), "<<", (a, b) => a << b, true);
    public static readonly MetaOperator RShift = new(typeof(Assembly.Instructions.AsmShiftRight), ">>", (a, b) => a >> b, true);
}