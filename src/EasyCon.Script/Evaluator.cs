using EasyCon.Script.Binding;
using System.Diagnostics;
using static EasyCon.Script2.Binding.BoundNodeKind;

namespace EasyCon.Script;

internal sealed class Evaluator
{

    private readonly BoundProgram _program;
    private readonly Dictionary<VariableSymbol, int> _globals = [];
    private readonly Stack<Dictionary<VariableSymbol, int>> _locals = new();
    private readonly Dictionary<FunctionSymbol, BoundBlockStatement> _functions = [];

    private readonly ExternTime et = new(DateTime.Now);
    private readonly Random _rand = new();
    private object? _lastValue;

    public Evaluator(BoundProgram program)
    {
        _program = program;
        _locals.Push([]);

        foreach (var kv in _program.Functions)
        {
            var function = kv.Key;
            var body = kv.Value;
            _functions.Add(function, body);
        }
    }

    public object? Evaluate()
    {
        var function = _program.MainFunction;
        if (function == null)
            return null;

        var body = _functions[function];
        return EvaluateStatement(body);
    }

    private object? EvaluateStatement(BoundBlockStatement body)
    {
        var labelToIndex = new Dictionary<BoundLabel, int>();

        for (var i = 0; i < body.Statements.Length; i++)
        {
            if (body.Statements[i] is BoundLabelStatement l)
                labelToIndex.Add(l.Label, i + 1);
        }
        var index = 0;

        while (index < body.Statements.Length)
        {
            var s = body.Statements[index];
            switch(s.Kind)
            {
                case NopStatement:
                    index++;
                    break;
                case Expression:
                    EvaluateAssignment((BoundAssignStatement)s);
                    index++;
                    break;
                case CallExpression:
                    EvaluateCall((BoundCallStatement)s);
                    index++;
                    break;
                case KeyAction:
                    EvaluateKeyAction((BoundKeyActStatement)s);
                    index++;
                    break;
                case Goto:
                    var gs = (BoundGotoStatement)s;
                    index = labelToIndex[gs.Label];
                    break;
                case ConditionGoto:
                    var cgs = (BoundConditionalGotoStatement)s;
                    var condition = EvaluateExpr(cgs.Condition)! > 0;
                    if (condition == cgs.JumpIfTrue)
                        index = labelToIndex[cgs.Label];
                    else
                        index++;
                    break;
                case Label:
                    index++;
                    break;
                case Return:
                    var rs = (BoundReturnStatement)s;
                    return _lastValue;
                default:
                    throw new ScriptException($"Unexpected node", index);
            }
        }
        return _lastValue;
    }

    private void EvaluateAssignment(BoundAssignStatement node)
    {
        var value = EvaluateExpr(node.Expression);
        Debug.Assert(value != null);

        _lastValue = value;
        Assign(node.Variable, value);
    }

    public int EvaluateExpr(BoundExpr expression)
    {
        throw new NotImplementedException(); 
    }

    private void EvaluateCall(BoundCallStatement node)
    {
        var statement = _functions[node.Function];
        var result = EvaluateStatement(statement);

        _lastValue = result;
    }

    private void EvaluateKeyAction(BoundKeyActStatement ndoe)
    {
        throw new NotImplementedException();
    }

    private void Assign(VariableSymbol variable, int value)
    {
        if (variable is GlobalVariableSymbol gvar)
        {
            _globals[variable] = value;
        }
        else
        {
            var locals = _locals.Peek();
            locals[variable] = value;
        }
    }
}
