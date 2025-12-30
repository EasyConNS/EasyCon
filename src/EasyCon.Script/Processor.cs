using EasyScript.Parsing;
using EasyScript.Statements;

namespace EasyScript;

class Processor(Dictionary<string, FunctionStmt> func)
{
    public IOutputAdapter Output;
    public ICGamePad GamePad;

    public int PC = 0;
    public bool CancelLineBreak = false;
    public readonly RegisterFile Register = new();

    private Stack<LiteScope> LiteScope = new();
    private readonly Stack<int> CallStack = new();
    private readonly Dictionary<string, FunctionStmt> _funcTables = func;
    private readonly ExternTime et = new(DateTime.Now);
    private Random _rand = new();

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
        switch (fn.ToUpper())
        {
            case "TIME":
                {
                    var ru = args[0] as RegParam;
                    Register[ru.Reg] = et.CurrTimestamp;
                }
                break;
            case "PRINT":
                {
                    var s = args.Select(u =>
                    {
                        return u switch
                        {

                            TextParam tu => tu.Text,
                            RegParam ru => Register[ru.Reg].ToString(),
                            _ => "",
                        };
                    });
                    var cancelLineBreak = args.LastOrDefault() switch
                    {
                        TextParam tu => tu.CodeText == "\\",
                        _ => false,
                    };

                    Output.Print(string.Join("", s), !CancelLineBreak);
                    CancelLineBreak = cancelLineBreak;
                }
                break;
            case "ALERT":
                {
                    var s = args.Select(u =>
                    {
                        return u switch
                        {
                            TextParam tu => tu.Text,
                            RegParam ru => Register[ru.Reg].ToString(),
                            _ => "",
                        };
                    });
                    Output.Alert(string.Join("", s));
                }
                break;
            case "RAND":
                {
                    var ru = args[0] as RegParam;
                    Register[ru.Reg] = _rand.Next(Register[ru.Reg] == 0 ? 100 : Register[ru.Reg]);
                }
                break;
            case "BEEP":
                {
                    if (args.Length != 0)
                    {
                        var rate = args[0] as LiterParam;
                        var dur = args[1] as LiterParam;

                        Console.Beep(rate.LITR.Get(this), dur.LITR.Get(this));
                    }
                    else
                    {
                        Console.Beep();
                    }
                }
                break;
            default:
                throw new ScriptException($"未实现的内建函数'{fn}'", PC);
        }
    }

    public void RetrunCall()
    {
        if (LiteScope.Count == 0)
            throw new ScriptException("顶层环境不能退出", PC);
        LiteScope.Pop();
        PC = CallStack.Pop();
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
        get => _variables.ContainsKey(tag) ? _variables[tag] : 0;
        set => _variables[tag] = value;
    }

    public int this[ValReg val]
    {
        get => this[val.Tag];

        set => this[val.Tag] = value;
    }
}

public class ExternTime
{
    private readonly long _TIME;

    public ExternTime(DateTime t)
    {
        _TIME = t.Ticks;
    }

    public int CurrTimestamp => (int)GetTimestamp();

    private long GetTimestamp() => (DateTime.Now.Ticks - _TIME) / 10_000;
}
