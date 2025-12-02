using EasyScript.Parsing;
using EasyScript.Statements;

namespace EasyScript;

class Processor(Dictionary<string, FunctionStmt> func)
{
    public const uint OfflineMaxRegisterCount = 8;

    public IOutputAdapter Output;
    public ICGamePad GamePad;

    public int PC = 0;
    public bool CancelLineBreak = false;
    public Stack<short> Stack = new();
    public Stack<For> LoopStack = new();
    public Dictionary<For, int> LoopTime = new();
    public Dictionary<For, int> LoopCount = new();

    public readonly RegisterFile Register = new();

    private readonly Stack<int> CallStack = new();
    private readonly Dictionary<string, FunctionStmt> _funcTables = func;
    private readonly ExternTime et = new(DateTime.Now);

    public void Call(string label)
    {
        CallStack.Push(PC);
        if (_funcTables.TryGetValue(label, out FunctionStmt? func))
            PC = func.Address + 1;
        else
            throw new ScriptException("找不到调用函数", PC);
    }

    public void CurTime(ValRegEx var)
    {
        Register[var] = et.CurrTimestamp;
    }

    public void RetrunCall()
    {
        PC = CallStack.Pop();
    }
}

class RegisterFile
{
    private readonly Dictionary<string, int> _variables = new();

    public int this[string tag]
    {
        get => _variables.ContainsKey(tag)? _variables[tag]:0;
        set => _variables[tag] = value;
    }

    public int this[ValRegEx val]
    {
        get => this[val.Tag];

        set => this[val.Tag] = value;
    }
}
