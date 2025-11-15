using EasyScript.Parsing;
using EasyScript.Parsing.Statements;
using System.Threading;

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
            if (_processor.FunctionDefinitionStack.Count == 0)
            {
                cmd.Exec(_processor);
            }
            else
            {
                _processor.FunctionDefinitionStack.Peek().Push(cmd);
            }
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
    public void explain(
        IOutputAdapter output, ICGamePad pad
        )
    {

        var parser = new Parser(Constants, ExtVars);
        var _processor = new Processor
        {
            Output = output,
            GamePad = pad,
            extVars = ExtVars.Values
        };
        var formatter = new Parsing.Formatter(Constants, ExtVars);

        var lines = parser.ParseLines(code).ToArray();
        while (_processor.PC < lines.Count())
        {
            var pline = lines[_processor.PC];
            var cmd = ParserManager.Parse(pline);
            if (cmd != null)
            {
                cmd.Address = _processor.PC;
                if (_processor.FunctionDefinitionStack.Count == 0)  // 如果现在不在定义函数
                {
                    try
                    {
                        if (!_processor.SkipState || cmd is BranchOp)
                        {
                            cmd.Exec(_processor);
                        }
                    }
                    catch (ThreadInterruptedException)
                    {
                        throw;
                    }
                    catch (ParseException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        output.Print($"[Line {_processor.PC + 1}] {ex.GetType().Name}: {ex.Message}", true);
                        throw;
                    }
                    
                }
                else    // 正在定义函数
                {
                    // 不执行，只是添加到函数定义中
                    _processor.FunctionDefinitionStack.Peek().Push(cmd);
                }
            }
            else
            {
                output.Print($"[Line {_processor.PC + 1}] Unknown command: {pline}", true);

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
