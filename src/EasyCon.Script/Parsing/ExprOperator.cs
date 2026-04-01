using EasyCon.Script.Assembly;

namespace EasyCon.Script.Parsing;

[Obsolete]
class CompareOperator1
{
    public static readonly List<CompareOperator1> All = [];
    public readonly string Operator;
    //public readonly Func<int, int, bool> Compare;
    public readonly Func<uint, uint, Instruction> Assemble;


    public readonly Type InstructionType;
    public readonly bool OnlyInstant;

    public CompareOperator1(string op, Func<int, int, bool> compare, Func<uint, uint, Instruction> assemble)
    {
        Operator = op;
        //Compare = compare;
        Assemble = assemble;
        All.Add(this);
    }

    public CompareOperator1(Type instructionType, string op, Func<int, int, int> function, bool onlyInstant = false)
    {
        InstructionType = instructionType;
        Operator = op;
        //Function = function;
        OnlyInstant = onlyInstant;
        All.Add(this);
    }

    public static readonly CompareOperator1 Equal = new("==", (v0, v1) => v0 == v1, (r0, r1) => Assembly.Instructions.AsmEqual.Create(Assembly.Instructions.AsmCompare.AssignType.Assign, r0, r1));
    public static readonly CompareOperator1 NotEqual = new("!=", (v0, v1) => v0 != v1, (r0, r1) => Assembly.Instructions.AsmNotEqual.Create(Assembly.Instructions.AsmCompare.AssignType.Assign, r0, r1));
    public static readonly CompareOperator1 LessThan = new("<", (v0, v1) => v0 < v1, (r0, r1) => Assembly.Instructions.AsmLessThan.Create(Assembly.Instructions.AsmCompare.AssignType.Assign, r0, r1));
    public static readonly CompareOperator1 LessOrEqual = new("<=", (v0, v1) => v0 <= v1, (r0, r1) => Assembly.Instructions.AsmLessOrEqual.Create(Assembly.Instructions.AsmCompare.AssignType.Assign, r0, r1));
    public static readonly CompareOperator1 GreaterThan = new(">", (v0, v1) => v0 > v1, (r0, r1) => Assembly.Instructions.AsmLessThan.Create(Assembly.Instructions.AsmCompare.AssignType.Assign, r1, r0));
    public static readonly CompareOperator1 GreaterOrEqual = new(">=", (v0, v1) => v0 >= v1, (r0, r1) => Assembly.Instructions.AsmLessOrEqual.Create(Assembly.Instructions.AsmCompare.AssignType.Assign, r1, r0));

    public static readonly CompareOperator1 Add = new(typeof(Assembly.Instructions.AsmAdd), "+", (a, b) => a + b);
    public static readonly CompareOperator1 Sub = new(null, "-", (a, b) => a - b);
    public static readonly CompareOperator1 Mul = new(typeof(Assembly.Instructions.AsmMul), "*", (a, b) => a * b);
    public static readonly CompareOperator1 Div = new(typeof(Assembly.Instructions.AsmDiv), "/", (a, b) => a / b);
    public static readonly CompareOperator1 RoundDiv = new(typeof(Assembly.Instructions.AsmDiv), @"\", (a, b) => (int)Math.Round((double)a / b) );
    public static readonly CompareOperator1 Mod = new(typeof(Assembly.Instructions.AsmMod), "%", (a, b) => a % b);
    public static readonly CompareOperator1 And = new(typeof(Assembly.Instructions.AsmAnd), "&", (a, b) => a & b);
    public static readonly CompareOperator1 Or = new(typeof(Assembly.Instructions.AsmOr), "|", (a, b) => a | b);
    public static readonly CompareOperator1 Xor = new(typeof(Assembly.Instructions.AsmXor), "^", (a, b) => a ^ b);
    public static readonly CompareOperator1 LShift = new(typeof(Assembly.Instructions.AsmShiftLeft), "<<", (a, b) => a << b, true);
    public static readonly CompareOperator1 RShift = new(typeof(Assembly.Instructions.AsmShiftRight), ">>", (a, b) => a >> b, true);
}
[Obsolete]
public class UnaryOperator1
{
    public static readonly List<UnaryOperator1> All = new();
    public readonly Type InstructionType;
    public readonly string KeyWord;
    public readonly Func<int, int> Function;

    public UnaryOperator1(Type instructionType, string keyword, Func<int, int> function)
    {
        InstructionType = instructionType;
        KeyWord = keyword;
        Function = function;
        All.Add(this);
    }

    public static readonly UnaryOperator1 Not = new(typeof(Assembly.Instructions.AsmNot), "~", a => ~a);
    public static readonly UnaryOperator1 Negative = new(typeof(Assembly.Instructions.AsmNegative), "-", a => -a);
}