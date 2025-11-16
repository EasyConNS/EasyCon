namespace EasyScript.Parsing;

class CompareOperator
{
    public static readonly List<CompareOperator> All = new();
    public static readonly Dictionary<string, CompareOperator> AllDict = new();
    public readonly string Operator;
    public readonly Func<int, int, bool> Compare;
    public readonly Func<uint, uint, Assembly.Instruction> Assemble;

    public CompareOperator(string op, Func<int, int, bool> compare, Func<uint, uint, Assembly.Instruction> assemble)
    {
        Operator = op;
        Compare = compare;
        Assemble = assemble;
        All.Add(this);
        AllDict.Add(op, this);
    }

    public static readonly CompareOperator EqualOld = new("=", (v0, v1) => v0 == v1, (r0, r1) => Assembly.Instructions.AsmEqual.Create(Assembly.Instructions.AsmCompare.AssignType.Assign, r0, r1));
    public static readonly CompareOperator Equal = new("==", (v0, v1) => v0 == v1, (r0, r1) => Assembly.Instructions.AsmEqual.Create(Assembly.Instructions.AsmCompare.AssignType.Assign, r0, r1));
    public static readonly CompareOperator NotEqual = new("!=", (v0, v1) => v0 != v1, (r0, r1) => Assembly.Instructions.AsmNotEqual.Create(Assembly.Instructions.AsmCompare.AssignType.Assign, r0, r1));
    public static readonly CompareOperator LessThan = new("<", (v0, v1) => v0 < v1, (r0, r1) => Assembly.Instructions.AsmLessThan.Create(Assembly.Instructions.AsmCompare.AssignType.Assign, r0, r1));
    public static readonly CompareOperator LessOrEqual = new("<=", (v0, v1) => v0 <= v1, (r0, r1) => Assembly.Instructions.AsmLessOrEqual.Create(Assembly.Instructions.AsmCompare.AssignType.Assign, r0, r1));
    public static readonly CompareOperator GreaterThan = new(">", (v0, v1) => v0 > v1, (r0, r1) => Assembly.Instructions.AsmLessThan.Create(Assembly.Instructions.AsmCompare.AssignType.Assign, r1, r0));
    public static readonly CompareOperator GreaterOrEqual = new(">=", (v0, v1) => v0 >= v1, (r0, r1) => Assembly.Instructions.AsmLessOrEqual.Create(Assembly.Instructions.AsmCompare.AssignType.Assign, r1, r0));
}
