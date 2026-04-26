using EasyCon.Script.Binding;
using EasyCon.Script.Symbols;
using EasyScript;
using System.Collections.Immutable;
using System.Diagnostics;
using static EasyCon.Script.Binding.BoundNodeKind;

namespace EasyCon.Script;

public class ScriptException(string message, int address) : Exception(message)
{
    public int Address { get; private set; } = address;
}

internal sealed class Evaluator : IEvalContext, IDisposable
{
    private readonly BoundProgram _program;
    private readonly Dictionary<VariableSymbol, Value> _globals = [];
    private readonly Stack<Dictionary<VariableSymbol, Value>> _locals = new();
    private readonly Dictionary<FunctionSymbol, BoundBlockStatement> _functions = [];
    private readonly ImmutableDictionary<string, Func<int>> _externalGetters = [];

    private readonly long _TIME = DateTime.Now.Ticks;
    private int CurrTimestamp => (int)((DateTime.Now.Ticks - _TIME) / 10_000);

    private readonly Random _rand = new();
    private bool _cancelLineBreak = false;
    private CancellationToken _token;
    private Value _lastValue;

    private readonly Dictionary<FunctionSymbol, ICallable> _callables = [];

    public IOutputAdapter? Output { get; set; }
    public ICGamePad? GamePad { get; set; }

    // IEvalContext
    ICGamePad? IEvalContext.GamePad => GamePad;
    IOutputAdapter? IEvalContext.Output => Output;
    Random IEvalContext.Rand => _rand;
    bool IEvalContext.CancelLineBreak { get => _cancelLineBreak; set => _cancelLineBreak = value; }

    private NativeLoader? _nativeLoader;

    public Evaluator(BoundProgram program, ImmutableDictionary<string, Func<int>> externalGetters, CancellationToken token)
    {
        _program = program;
        _token = token;
        _externalGetters = externalGetters;
        _locals.Push([]);

        foreach (var kv in _program.Functions)
        {
            _functions.Add(kv.Key, kv.Value);
        }

        RegisterCallables();
    }

    private void RegisterCallables()
    {
        // 注册内置函数 callable
        foreach (var (symbol, callable) in BuiltinCallable.GetAll(() => CurrTimestamp))
        {
            _callables[symbol] = callable;
        }

        // 注册用户函数 callable（_functions 中仅含用户函数 + $eval）
        foreach (var fn in _functions.Keys)
        {
            if (_callables.ContainsKey(fn)) continue;
            _callables[fn] = new DelegateCallable((args, ctx, tk) =>
            {
                var locals = new Dictionary<VariableSymbol, Value>();
                for (int i = 0; i < args.Length; i++)
                    locals.Add(fn.Parameters[i], args[i]);
                ctx.PushLocals(locals);
                var result = ctx.EvaluateFunctionBody(fn);
                ctx.PopLocals();
                return result;
            });
        }

        // 注册 EXTERN 函数（从独立列表获取，不再过滤 _functions.Keys）
        if (!_program.ExternFunctions.IsEmpty)
        {
            _nativeLoader = new NativeLoader();
            var externFuncs = _nativeLoader.LoadExternFunctions(_program.ExternFunctions);
            foreach (var ff in externFuncs)
            {
                var symbol = _program.ExternFunctions.First(s => s.Name == ff.Name);
                var capturedInvoke = ff.Invoke;
                _callables[symbol] = new DelegateCallable((args, ctx, tk) => capturedInvoke(args));
            }
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
                case ConstantDeclaration:
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
            return locals[v];
        }
    }

    private Value EvaluateVariableExpression(BoundVariableExpression v)
    {
        return GetValue(v.Variable);
    }

    private Value EvaluateIndexDeclExpression(BoundIndexDeclxpression decl)
    {
        var elements = decl.Items.Select(EvaluateExpression);
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
        if (b.Op.Kind == BoundBinaryOperatorKind.LogicalAnd && !left.AsBool()) return false;
        if (b.Op.Kind == BoundBinaryOperatorKind.LogicalOr && left.AsBool()) return true;
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
        var args = ImmutableArray.CreateBuilder<Value>();
        for (int i = 0; i < node.Arguments.Length; i++)
        {
            var value = EvaluateExpression(node.Arguments[i]);
            Debug.Assert(value != Value.Void);
            args.Add(value);
        }
        var callable = _callables[node.Function];
        return callable.Invoke(args.ToImmutable(), this, _token);
    }

    private void EvaluateKeyAction(BoundKeyActStatement node)
    {
        if (node is BoundKeyPressStatement bps)
        {
            var dur = EvaluateExpression(bps.Duration).AsInt();
            GamePad?.ClickButtons(bps.Act, dur, _token);
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
            GamePad?.ClickStick(bps.Act, bps.X, bps.Y, dur, _token);
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

    // IEvalContext 方法
    public void PushLocals(Dictionary<VariableSymbol, Value> locals) => _locals.Push(locals);
    public void PopLocals() => _locals.Pop();
    public Value EvaluateFunctionBody(FunctionSymbol function)
    {
        var body = _functions[function];
        return EvaluateStatement(body);
    }

    public void Dispose()
    {
        _nativeLoader?.Dispose();
    }
}

public sealed class EvaluationResult(ImmutableArray<Diagnostic> diagnostics, Value value)
{
    public ImmutableArray<Diagnostic> Diagnostics { get; } = diagnostics;
    public Value Result { get; } = value;
}