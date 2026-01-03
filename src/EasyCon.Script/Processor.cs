using EasyCon.Script.Parsing;

namespace EasyCon.Script;

//sealed class Processor(Dictionary<string, FuncStmt> func)
//{
//    public IOutputAdapter Output;
//    public ICGamePad GamePad;

//    public int PC = 0;
//    public bool CancelLineBreak = false;
//    public readonly RegisterFile Register = new();

//    private Stack<LiteScope> LiteScope = new();
//    private readonly Stack<int> CallStack = new();
//    private readonly Dictionary<string, FuncStmt> _funcTables = func;
//    private readonly ExternTime et = new(DateTime.Now);
//    private Random _rand = new();

//    public LiteScope GetScope()
//    {
//        if (LiteScope.Count == 0)
//            LiteScope.Push(new LiteScope());
//        var liteScop = LiteScope.Peek();
//        return liteScop;
//    }

//    public void Call(string label)
//    {
//        LiteScope.Push(new LiteScope());
//        CallStack.Push(PC);
//        if (_funcTables.TryGetValue(label, out FuncStmt? func))
//            PC = func.Address + 1;
//        else
//            throw new ScriptException($"找不到调用函数\"{label}\"", PC);
//    }

//    public void BuildinCall(string fn, ExprBase[] args)
//    {
//        //if(!BuiltinFunctions.GetAll().Any(fs => 
//        //fs.Name.Equals(fn, StringComparison.CurrentCultureIgnoreCase) && 
//        //  fs.Parameters.Length == args.Length))
//        //{
//        //    throw new ScriptException($"内置函数{fn}参数个数不满足", PC);
//        //}
//        switch (fn)
//        {
//            //case "AMIIBO":
//            //    {
//            //        var index = args[0] switch
//            //        {
//            //            RegParam r => Register[r.Reg],
//            //            LiterParam l => l.LITR.Get(this),
//            //        };
//            //        if (index > 9)
//            //        {
//            //            // value must between 0~9
//            //            return;
//            //        }
//            //        GamePad.ChangeAmiibo((uint)index);
//            //    }
//            //    break;
//            //case "TIME":
//            //    {
//            //        var ru = args[0] as RegParam;
//            //        Register[ru.Reg] = et.CurrTimestamp;
//            //    }
//            //    break;
//            //case "PRINT":
//            //    {
//            //        var s = args[0] switch
//            //        {
//            //            TextVarParam tvu => string.Join("", tvu.Params.Select(u =>
//            //            {
//            //                return u switch
//            //                {
//            //                    TextParam tu => tu.Text,
//            //                    RegParam ru => Register[ru.Reg].ToString(),
//            //                    LiterParam lu => lu.LITR.Get(null).ToString(),
//            //                    _ => "",
//            //                };
//            //            })),
//            //            TextParam tu => tu.Text,
//            //            RegParam ru => Register[ru.Reg].ToString(),
//            //            LiterParam lu => lu.LITR.Get(null).ToString(),
//            //            _ => "",
//            //        };
//            //        Output.Print(string.Join("", s), !CancelLineBreak);
//            //        CancelLineBreak = args.LastOrDefault() switch
//            //        {
//            //            TextParam tu => tu.Text == "\\",
//            //            _ => false,
//            //        }; // true不换行
//            //    }
//            //    break;
//            //case "ALERT":
//            //    {
//            //        var s = args.Select(u =>
//            //        {
//            //            return u switch
//            //            {
//            //                TextParam tu => tu.Text,
//            //                RegParam ru => Register[ru.Reg].ToString(),
//            //                LiterParam lu => lu.LITR.Get(null).ToString(),
//            //                _ => "",
//            //            };
//            //        });
//            //        Output.Alert(string.Join("", s));
//            //    }
//            //    break;
//            //case "RAND":
//            //    {
//            //        var ru = args[0] as RegParam;
//            //        Register[ru.Reg] = _rand.Next(Register[ru.Reg] == 0 ? 100 : Register[ru.Reg]);
//            //    }
//            //    break;
//            //case "BEEP":
//            //    {
//            //        var rate = args[0] as LiterParam;
//            //        var dur = args[1] as LiterParam;

//            //        Console.Beep(rate.LITR.Get(this), dur.LITR.Get(this));
//            //    }
//            //    break;
//            default:
//                throw new ScriptException($"未实现的内建函数'{fn}'", PC);
//        }
//    }

//    public void RetrunCall()
//    {
//        if (LiteScope.Count == 0)
//            throw new ScriptException("顶层环境不能退出", PC);
//        LiteScope.Pop();
//        PC = CallStack.Pop();
//    }
//}

class LiteScope(LiteScope? parent = null)
{
    public LiteScope? Parent { get; }

    public Stack<ForStmt> LoopStack = new();
    public Dictionary<ForStmt, int> LoopTime = new();
    public Dictionary<ForStmt, int> LoopCount = new();
}

class RegisterFile
{
    private readonly Dictionary<string, int> _variables = new();

    public int this[string tag]
    {
        get => _variables.TryGetValue(tag, out int value) ? value : 0;
        set => _variables[tag] = value;
    }

    public int this[VariableExpr val]
    {
        get => this[val.Tag];

        set => this[val.Tag] = value;
    }
}

class ExternTime(DateTime t)
{
    private readonly long _TIME = t.Ticks;

    public int CurrTimestamp => (int)GetTimestamp();

    private long GetTimestamp() => (DateTime.Now.Ticks - _TIME) / 10_000;
}
