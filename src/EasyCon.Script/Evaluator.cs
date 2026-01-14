using EasyCon.Script.Binding;
using EasyScript;
using System.Collections.Immutable;
using System.Diagnostics;
using static EasyCon.Script2.Binding.BoundNodeKind;

namespace EasyCon.Script;

internal sealed class Evaluator
{
    private readonly BoundProgram _program;
    private readonly Dictionary<VariableSymbol, object> _globals = [];
    private readonly Stack<Dictionary<VariableSymbol, object>> _locals = new();
    private readonly Dictionary<FunctionSymbol, BoundBlockStatement> _functions = [];

    private readonly ExternTime et = new(DateTime.Now);
    private readonly Random _rand = new();
    private bool CancelLineBreak = false;
    private CancellationToken _token;
    private object? _lastValue;

    public IOutputAdapter Output;
    public ICGamePad GamePad;

    public Evaluator(BoundProgram program, CancellationToken token)
    {
        _program = program;
        _token = token;
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

        while (!_token.IsCancellationRequested && index < body.Statements.Length)
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
                    var condition = (bool)EvaluateExpr(cgs.Condition)!;
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
            Thread.Sleep(1);
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

    public object EvaluateExpr(BoundExpr node)
    {
        if (node.ConstantValue != null)
            return node.ConstantValue;

        switch (node.Kind)
        {
            case Variable:
                return EvaluateVariableExpression((BoundVariableExpression)node);
            case ExLabelVariable:
                var imglabel = (BoundExternalVariableExpression)node;
                return imglabel.Label.Get();
            case UnaryExpression:
                return EvaluateUnaryExpression((BoundUnaryExpression)node);
            case BinaryExpression:
                return EvaluateBinaryExpression((BoundBinaryExpression)node);
            case ConversionExpression:
                return EvaluateConversionExpression((BoundConversionExpression)node);
            default:
                throw new Exception($"无法执行的表达式{node.Kind}");
        }
    }

    private object EvaluateVariableExpression(BoundVariableExpression v)
    {
        if (v.Variable is GlobalVariableSymbol)
        {
            return _globals[v.Variable];
        }
        else
        {
            var locals = _locals.Peek();
            return locals[v.Variable];
        }
    }
    private object? EvaluateConversionExpression(BoundConversionExpression node)
    {
        var value = EvaluateExpr(node.Expression);
        if (node.Type == Binding.ValueType.Bool)
            return Convert.ToBoolean(value);
        else if (node.Type == Binding.ValueType.Int)
            return Convert.ToInt32(value);
        else if (node.Type == Binding.ValueType.String)
            return Convert.ToString(value);
        else
            throw new Exception($"无效的类型转换{node.Type}");
    }

    private object EvaluateUnaryExpression(BoundUnaryExpression u)
    {
        var operand = EvaluateExpr(u.Operand);

        Debug.Assert(operand != null);

        switch (u.Op.Kind)
        {
            case BoundUnaryOperatorKind.Subtraction:
                return -(int)operand;
            case BoundUnaryOperatorKind.BitwiseNot:
                return ~(int)operand;
            default:
                throw new Exception($"不支持的运算符{u.Op}");
        }
    }

    private object EvaluateBinaryExpression(BoundBinaryExpression b)
    {
        var left = EvaluateExpr(b.Left);
        var right = EvaluateExpr(b.Right);

        Debug.Assert(left != null && right != null);

        switch (b.Op.Kind)
        {
            case BoundBinaryOperatorKind.Addition:
                if (b.Type == Binding.ValueType.Int)
                    return (int)left + (int)right;
                else
                    return $"{left}{right}";
            case BoundBinaryOperatorKind.Equals:
                return Equals(left, right);
            case BoundBinaryOperatorKind.NotEquals:
                return !Equals(left, right);
            case BoundBinaryOperatorKind.Less:
                return (int)left < (int)right;
            case BoundBinaryOperatorKind.LessOrEquals:
                return (int)left <= (int)right;
            case BoundBinaryOperatorKind.Greater:
                return (int)left > (int)right;
            case BoundBinaryOperatorKind.GreaterOrEquals:
                return (int)left >= (int)right;
            default:
                throw new Exception($"不支持的运算符{b.Op}");
        }
    }
    private void EvaluateCall(BoundCallStatement node)
    {
        if (BuiltinFunctions.GetAll().Any(f=>f==node.Function))
        {
            var args = ImmutableArray.CreateBuilder<object>();
            for (int i = 0; i < node.Arguments.Length; i++)
            {
                var value = EvaluateExpr(node.Arguments[i]);
                Debug.Assert(value != null);
                args.Add(value);
            }
            EvaluteBuildin(node.Function, args.ToImmutable());
        }
        else
        {
            _locals.Push([]);
            var statement = _functions[node.Function];
            var result = EvaluateStatement(statement);
            _locals.Pop();

            _lastValue = result;
        }
    }

    private void EvaluateKeyAction(BoundKeyActStatement ndoe)
    {
        throw new NotImplementedException();
    }

    private void Assign(VariableSymbol variable, object value)
    {
        if (variable is GlobalVariableSymbol)
        {
            _globals[variable] = value;
        }
        else
        {
            var locals = _locals.Peek();
            locals[variable] = value;
        }
    }

    private void EvaluteBuildin(FunctionSymbol fn, ImmutableArray<object> args)
    {
        switch (fn.Name)
        {
            case "AMIIBO":
                {
                    var index = (int)args[0];
                    if (index > 9)
                    {
                        // value must between 0~9
                        return;
                    }
                    GamePad.ChangeAmiibo((uint)index);
                }
                break;
            case "TIME":
                {
                    _lastValue = et.CurrTimestamp;
                }
                break;
            case "PRINT":
              {
                    var s = (string)args[0];
                    Output.Print(s.TrimEnd('\\'), !CancelLineBreak);
                    CancelLineBreak = s.EndsWith('\\'); // true不换行
              }
              break;
            case "ALERT":
                {
                    var s = (string)args[0];
                    Output.Alert(s.TrimEnd('\\'));
                }
                break;
                case "RAND":
                {
                    var max = (int)args[0];
                    _rand.Next(max == 0 ? 100 : max);
                    _lastValue = max;
                }
                break;
            case "BEEP":
              {
                    var freq = (int)args[0];
                    if(freq < 37 || freq > 32767) throw new Exception($"BEEP参数freq范围不争取(37~32767)");
                    Console.Beep(freq, (int)args[1]);
              }
              break;
        }
    }
}

class ExternTime(DateTime t)
{
    private readonly long _TIME = t.Ticks;

    public int CurrTimestamp => (int)GetTimestamp();

    private long GetTimestamp() => (DateTime.Now.Ticks - _TIME) / 10_000;
}
