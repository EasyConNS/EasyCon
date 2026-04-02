using EasyCon.Script.Binding;
using EasyCon.Script.Symbols;
using System.Collections.Immutable;
using System.Diagnostics;
using EasyScript;
using static EasyCon.Script.Binding.BoundNodeKind;

namespace EasyCon.Script;

public class ScriptException(string message, int address) : Exception(message)
{
    public int Address { get; private set; } = address;
}

internal sealed class Evaluator
{
    private readonly BoundProgram _program;
    private readonly Dictionary<VariableSymbol, Value> _globals = [];
    private readonly Stack<Dictionary<VariableSymbol, Value>> _locals = new();
    private readonly Dictionary<FunctionSymbol, BoundBlockStatement> _functions = [];
    private readonly Dictionary<string, Func<int>> _externalGetters = [];

    private readonly long _TIME = DateTime.Now.Ticks;
    private int CurrTimestamp => (int)((DateTime.Now.Ticks - _TIME) / 10_000);

    private readonly Random _rand = new();
    private bool CancelLineBreak = false;
    private CancellationToken _token;
    private Value _lastValue;

    public IOutputAdapter? Output;
    public ICGamePad? GamePad;

    public Evaluator(BoundProgram program, CancellationToken token, Dictionary<string, Func<int>> externalGetters)
    {
        _program = program;
        _token = token;
        _externalGetters = externalGetters;
        _locals.Push([]);

        foreach (var kv in _program.Functions)
        {
            _functions.Add(kv.Key, kv.Value);
        }
    }

    public Value Evaluate()
    {
        var function = _program.MainFunction;
        if (function == null)
            return Value.Void;

        var body = _functions[function];
        return EvaluateStatement(body);
    }

    private Value EvaluateStatement(BoundBlockStatement body)
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
            switch (s.Kind)
            {
                case NopStatement:
                    index++;
                    break;
                case VariableDeclaration:
                    EvaluateVariableDeclaration((BoundVariableDeclaration)s);
                    index++;
                    break;
                case ExpressionStatement:
                    EvaluateExpressionStatement((BoundExprStatement)s);
                    index++;
                    break;
                case KeyAction:
                    EvaluateKeyAction((BoundKeyActStatement)s);
                    index++;
                    break;
                case StickAction:
                    EvaluateStickKeyAction((BoundStickActStatement)s);
                    index++;
                    break;
                case GotoStatement:
                    var gs = (BoundGotoStatement)s;
                    index = labelToIndex[gs.Label];
                    Thread.Yield();
                    break;
                case ConditionGotoStatement:
                    var cgs = (BoundConditionalGotoStatement)s;
                    var condition = EvaluateExpression(cgs.Condition).AsBool();
                    if (condition == cgs.JumpIfTrue)
                        index = labelToIndex[cgs.Label];
                    else
                        index++;
                    Thread.Yield();
                    break;
                case Label:
                    index++;
                    break;
                case Return:
                    var rs = (BoundReturnStatement)s;
                    if (rs.Expression != null)
                    {
                        _lastValue = EvaluateExpression(rs.Expression);
                    }
                    return _lastValue;
                default:
                    throw new ScriptException($"执行语句类型未知", index);
            }
        }
        return _lastValue;
    }

    private void EvaluateVariableDeclaration(BoundVariableDeclaration node)
    {
        var value = EvaluateExpression(node.Initializer);
        Debug.Assert(value != Value.Void);

        _lastValue = value;
        Assign(node.Variable, value);
    }

    private void EvaluateExpressionStatement(BoundExprStatement node)
    {
        _lastValue = EvaluateExpression(node.Expression);
    }
    public Value EvaluateExpression(BoundExpr node)
    {
        if (node.ConstantValue != Value.Void)
            return EvaluateConstantExpression(node);

        switch (node.Kind)
        {
            case Variable:
                return EvaluateVariableExpression((BoundVariableExpression)node);
            case IndexDecl:
                return EvaluateIndexDeclExpression((BoundIndexDeclxpression)node);
            case IndexVariable:
                return EvaluateIndexVariableExpression((BoundIndexVariableExpression)node);
            case SliceVariable:
                return EvaluateSliceExpression((BoundSliceExpression)node);
            case ExLabelVariable:
                var imglabel = (BoundExternalVariableExpression)node;
                if (!_externalGetters.TryGetValue(imglabel.Name, out var getter))
                    throw new Exception($"找不到外部变量 \"{imglabel.Name}\" 的getter");
                return getter();
            case UnaryExpression:
                return EvaluateUnaryExpression((BoundUnaryExpression)node);
            case BinaryExpression:
                return EvaluateBinaryExpression((BoundBinaryExpression)node);
            case ConversionExpression:
                return EvaluateConversionExpression((BoundConversionExpression)node);
            case AssignmentExpression:
                return EvaluateAssignmentExpression((BoundAssignExpression)node);
            case CallExpression:
                return EvaluateCallExpression((BoundCallExpression)node);
            default:
                throw new Exception($"无法执行的表达式{node.Kind}");
        }
    }

    private static Value EvaluateConstantExpression(BoundExpr n)
    {
        Debug.Assert(n.ConstantValue != Value.Void);

        return n.ConstantValue;
    }

    private Value GetValue(VariableSymbol v)
    {
        if (v is GlobalVariableSymbol)
        {
            return _globals.TryGetValue(v, out Value value) ? value : 0;
        }
        else
        {
            var locals = _locals.Peek();
            return locals.TryGetValue(v, out Value value) ? value : 0;
        }
    }

    private Value EvaluateVariableExpression(BoundVariableExpression v)
    {
        return GetValue(v.Variable);
    }

    private Value EvaluateIndexDeclExpression(BoundIndexDeclxpression decl)
    {
        var elements = decl.Items.Select(EvaluateExpression);
        // 使用之前重构的 CreateArray 保持泛型正确
        var elemType = ((GenericType)decl.Type).TypeArguments[0];
        return Value.CreateArray(elemType, elements);
    }

    private Value EvaluateIndexVariableExpression(BoundIndexVariableExpression idxExpr)
    {
        var container = EvaluateExpression(idxExpr.BaseExpression);
        var index = EvaluateExpression(idxExpr.Index).AsInt();
        if (index >= container.Length) throw new Exception($"数组下标越界");
        return container[index];
    }

    private Value EvaluateSliceExpression(BoundSliceExpression sliceExpr)
    {
        var target = EvaluateExpression(sliceExpr.BaseExpression);
        var start = EvaluateExpression(sliceExpr.Start).AsInt();
        var end = EvaluateExpression(sliceExpr.End);

        var endidx = end.Type == ScriptType.Int ? end.AsInt() : target.Length;
        if (start >= target.Length || endidx > target.Length || start > endidx) throw new Exception($"数组下标越界");
        return target[start..endidx];
    }

    private Value EvaluateConversionExpression(BoundConversionExpression node)
    {
        var value = EvaluateExpression(node.Expression);
        if (node.Type == ScriptType.Bool)
            return value.ToBoolean();
        else if (node.Type == ScriptType.Int)
            return Convert.ToInt32(value);
        else if (node.Type == ScriptType.String)
            return value.ToString();
        else
            throw new Exception($"无效的类型转换{node.Type}");
    }

    private Value EvaluateUnaryExpression(BoundUnaryExpression u)
    {
        var operand = EvaluateExpression(u.Operand);

        Debug.Assert(operand != Value.Void);

        return Value.From(u.Op.Operate(operand));
    }

    private Value EvaluateBinaryExpression(BoundBinaryExpression b)
    {
        var left = EvaluateExpression(b.Left);
        var right = EvaluateExpression(b.Right);

        Debug.Assert(left != Value.Void && right != Value.Void);

        return Value.From(b.Op.Operate(left, right));
    }

    private Value EvaluateAssignmentExpression(BoundAssignExpression node)
    {
        var value = EvaluateExpression(node.Expression);
        Debug.Assert(value != Value.Void);

        Assign(node.Variable, value);
        return value;
    }

    private Value EvaluateCallExpression(BoundCallExpression node)
    {
        if (BuiltinFunctions.GetAll().Any(f => f == node.Function))
        {
            var args = ImmutableArray.CreateBuilder<Value>();
            for (int i = 0; i < node.Arguments.Length; i++)
            {
                var value = EvaluateExpression(node.Arguments[i]);
                Debug.Assert(value != Value.Void);
                args.Add(value);
            }
            return EvaluteBuildin(node.Function, args.ToImmutable());
        }
        else
        {
            var locals = new Dictionary<VariableSymbol, Value>();
            for (int i = 0; i < node.Arguments.Length; i++)
            {
                var parameter = node.Function.Parameters[i];
                var value = EvaluateExpression(node.Arguments[i]);
                Debug.Assert(value != Value.Void);
                locals.Add(parameter, value);
            }
            _locals.Push(locals);
            var statement = _functions[node.Function];
            var result = EvaluateStatement(statement);
            _locals.Pop();

            return result;
        }
    }

    private void EvaluateKeyAction(BoundKeyActStatement node)
    {
        if (node is BoundKeyPressStatement bps)
        {
            var dur = EvaluateExpression(bps.Duration).AsInt();
            GamePad?.ClickButtons(bps.Act, dur);
        }
        else
        {
            if (node.Up)
            {
                GamePad?.ReleaseButtons(node.Act);
            }
            else
            {
                GamePad?.PressButtons(node.Act);
            }
        }
    }

    private void EvaluateStickKeyAction(BoundStickActStatement node)
    {
        if (node is BoundStickPressStatement bps)
        {
            var dur = EvaluateExpression(bps.Duration).AsInt();
            GamePad?.ClickStick(bps.Act, bps.X, bps.Y, dur);
        }
        else
        {
            GamePad?.SetStick(node.Act, node.X, node.Y);
        }
    }

    private void Assign(VariableSymbol variable, Value value)
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

    private Value EvaluteBuildin(FunctionSymbol fn, ImmutableArray<Value> args)
    {
        Value result = Value.Void;
        switch (fn.Name)
        {
            case "WAIT":
                var ms = args[0].AsInt();
                switch (GamePad?.DelayMethod)
                {
                    case DelayType.Normal: Thread.Sleep(ms); break;
                    case DelayType.LowCPU: CustomDelay.AISleep(ms); break;
                    case DelayType.HighResolution:
                    default:
                        CustomDelay.Delay(ms);
                        break;
                }
                break;
            case "AMIIBO":
                {
                    var index = args[0].AsInt();
                    if (index > 9)
                    {
                        // value must between 0~9
                        return result;
                    }
                    GamePad?.ChangeAmiibo((uint)index);
                }
                break;
            case "TIME":
                result = CurrTimestamp;
                break;
            case "PRINT":
                {
                    var s = args[0].AsString();
                    var output = s.EndsWith('\\') ? s[..^1] : s;
                    Output?.Print(output, !CancelLineBreak);
                    CancelLineBreak = s.EndsWith('\\'); // true不换行
                }
                break;
            case "ALERT":
                {
                    var s = args[0].AsString();
                    var output = s.EndsWith('\\') ? s[..^1] : s;
                    Output?.Alert(output);
                }
                break;
            case "RAND":
                {
                    var max = args[0].AsInt();
                    max = max < 0 ? 0 : max;
                    result = _rand.Next(max);
                }
                break;
            case "BEEP":
                {
                    var freq = args[0].AsInt();
                    if (freq < 37 || freq > 32767) throw new Exception($"BEEP参数freq范围不正确(37~32767)");
                    Console.Beep(freq, args[1].AsInt());
                }
                break;
            case "LEN":
                result = args[0].Length;
                break;
            case "APPEND":
                result = args[0].Append(args[1]);
                break;
        }
        return result;
    }
}

public sealed class EvaluationResult(ImmutableArray<Diagnostic> diagnostics, Value value)
{
    public ImmutableArray<Diagnostic> Diagnostics { get; } = diagnostics;
    public Value Result { get; } = value;
}