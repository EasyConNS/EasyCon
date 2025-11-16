using EasyScript.Parsing;
using EasyScript.Parsing.Statements;

namespace EasyScript;

public class Scripter
{
    readonly Dictionary<string, int> Constants = new();
    readonly Dictionary<string, ExternalVariable> ExtVars = new();

    List<Parsing.Statement> _statements = new();
    string code = string.Empty;

    public bool HasKeyAction {
        get
        {
            foreach(var stat in _statements)
            {
                if (stat is Parsing.Statements.KeyAction)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public void Parse(string code, IEnumerable<ExternalVariable> extVars)
    {
        ExtVars.Clear();
        foreach (var ev in extVars)
            ExtVars[ev.Name] = ev;
        _statements = new Parsing.Parser(Constants, ExtVars).Parse(code);
    }

    public void Run(IOutputAdapter output, ICGamePad pad)
    {
        var _processor = new Processor
        {
            Output = output,
            GamePad = pad,
            extVars = ExtVars.Values
        };

        while (_processor.PC < _statements.Count)
        {
            
            var cmd = _statements[_processor.PC];
            _processor.PC++;
            cmd.Exec(_processor);
            
        }
    }

    public void load(
        string code, IEnumerable<ExternalVariable> extVars)
    {
        this.code = code;
        ExtVars.Clear();
        foreach (var ev in extVars)
            ExtVars[ev.Name] = ev;
    }

    public void explain(IOutputAdapter output, ICGamePad pad)
    {
        explain(new Processor
            {
                Output = output,
                GamePad = pad,
                extVars = ExtVars.Values,
            }
        );
    }

    internal Processor explain(
        Processor _processor
        )
    {
        var parser = new Parser(Constants, ExtVars);
        var formatter = new Parsing.Formatter(Constants, ExtVars);

        var lines = parser.ParseLines(code).ToArray();
        _processor.PC = 0;
        while (_processor.PC < lines.Length)
        {
            var pline = lines[_processor.PC];
            var cmd = ParserManager.Parse(pline);
            if (cmd != null)
            {
                cmd.Address = _processor.PC;
                if (_processor.FunctionDefinitionStack.Count == 0)  // 没在定义函数
                {
                    // 这里不处理错误，由GUI处理
                    if (!_processor.SkipState || cmd is BranchOp)
                    {
                        cmd.Exec(_processor);
                    }
                }
                else    // 在定义函数，对于普通指令只添加到函数栈，不执行
                {
                    if (cmd is ReturnStat || cmd is Function)   // 对于函数定义语句还是要执行的
                    {
                        cmd.Exec(_processor);
                    }
                    else 
                    { 
                        _processor.FunctionDefinitionStack.Peek().Push(pline); 
                    }
                }
            }
            else
            {
                throw new ParseException("Unknown command", _processor.PC);
            }

            _processor.PC++;
        }
        List<ParseException> errors = new List<ParseException>();
        while (_processor.ControlStack.Count > 0)
        {
            var cmd = _processor.ControlStack.Pop();
            if (cmd is If)
            {
                errors.Add(new ParseException("Unclosed if statement", cmd.Address));
            }
            else if (cmd is For)
            {
                errors.Add(new ParseException("Unclosed loop statement", cmd.Address));
            }    
        }
        if (errors.Count > 0)
        {
            throw new AggregateException(errors.ToArray());
        }
        return _processor;
    }

    public string ToCode()
    {
        var formatter = new Parsing.Formatter(Constants, ExtVars);
        return string.Join(Environment.NewLine, _statements.Select(u => u.GetString(formatter)));
    }

    public byte[] Assemble(bool auto = true)
    {
        return new Assembly.Assembler().Assemble(_statements, auto);
    }

    public void Reset()
    {
        _statements = new();
        ExtVars.Clear();
    }
}
