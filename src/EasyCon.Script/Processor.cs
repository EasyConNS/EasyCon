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
    public readonly RegisterFile Register = new();

    private Stack<short> Stack = new();
    private Stack<LiteScope> LiteScope = new();
    private readonly Stack<int> CallStack = new();
    private readonly Dictionary<string, FunctionStmt> _funcTables = func;
    private readonly ExternTime et = new(DateTime.Now);

    public LiteScope GetScope()
    {
        if (LiteScope.Count == 0)
            LiteScope.Push(new LiteScope());
        var liteScop = LiteScope.Peek();
        return liteScop;
    }

    public void Call(string label)
    {
        LiteScope.Push(new LiteScope());
        CallStack.Push(PC);
        if (_funcTables.TryGetValue(label, out FunctionStmt? func))
            PC = func.Address + 1;
        else
            throw new ScriptException("找不到调用函数", PC);
    }

    public void BuildinCall(string fn, Param[] args)
    {
        fn = fn.ToUpper();
        if (fn == "TIME")
        {
            var ru = args[0] as RegParam;
            Register[ru.Reg] = et.CurrTimestamp;
        }
        else if (fn == "PRINT")
        {
            var s = args.Select(
                u => {
                    return u switch
                    {
                        TextParam tu => tu.Text,
                        RegParam ru => Register[ru.Reg].ToString(),
                        _ => "",
                    };
                }
            );
            var cancelLineBreak = args.Last() switch
            {
                TextParam tu => tu.CodeText == "\\",
                _ => false,
            };

            Output.Print(string.Join("", s), !CancelLineBreak);
            CancelLineBreak = cancelLineBreak;
        }
        else if (fn == "ALERT")
        {
            var s = args.Select(
                u => {
                    return u switch
                    {
                        TextParam tu => tu.Text,
                        RegParam ru => Register[ru.Reg].ToString(),
                        _ => "",
                    };
                }
            );
            Output.Alert(string.Join("", s));
        }
    }

    public void RetrunCall()
    {
        if(LiteScope.Count == 0)
            throw new ScriptException("顶层环境不能退出", PC);
        LiteScope.Pop();
        PC = CallStack.Pop();
    }

    public void Push(short val)
    {
        Stack.Push(val);
    }

    public short Pop()
    {
        if (Stack.Count <= 0)
            throw new ScriptException("栈为空，无法出栈", PC);
        return Stack.Pop(); 
    }
}

class LiteScope(LiteScope? parent = null)
{
    public LiteScope? Parent { get; }

    private readonly RegisterFile Register = new();

    public Stack<ForStmt> LoopStack = new();
    public Dictionary<ForStmt, int> LoopTime = new();
    public Dictionary<ForStmt, int> LoopCount = new();
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
