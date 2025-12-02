using EasyCon.Script2.Ast;

namespace EasyCon.Script2;

internal sealed class Scope(Scope? parent)
{
    private readonly Dictionary<string, RuntimeValue> _variables = [];
    private readonly Dictionary<string, FunctionSymbol> _functions = [];
    public Scope? Parent { get; } = parent;

    public bool TryDeclareVariable(string name, RuntimeValue value)
    {
        if (_variables.ContainsKey(name)) return false;
        _variables.Add(name, value);
        return true;
    }

    public RuntimeValue? TryLookupVariable(string name)
    {
        if (_variables.TryGetValue(name, out RuntimeValue? value))
            return value;

        return Parent?.TryLookupVariable(name);
    }

    public bool HasVariable(string name)
    {
        return _variables.ContainsKey(name) || (Parent?.HasVariable(name) == true);
    }

    public bool TryDeclareFunction(string name, FunctionSymbol function)
    {
        if (_functions.ContainsKey(name)) return false;
        _functions.Add(name, function);
        return true;
    }

    public FunctionSymbol? TryLookupFunction(string name)
    {
        if (_functions.TryGetValue(name, out FunctionSymbol? value))
            return value;

        return Parent?.TryLookupFunction(name);
    }
}

// 运行时值类型
public class RuntimeValue
{
    public object Value { get; set; }
    public ValueType Type { get; set; }

    public RuntimeValue(object value, ValueType type)
    {
        Value = value;
        Type = type;
    }

    public override string ToString()
    {
        return Value?.ToString() ?? "null";
    }
}

public enum ValueType
{
    Integer,
    Decimal,
    String,
    Array,
    Boolean,
    Null
}

// 函数定义
public class FunctionSymbol
{
    public string Name { get; }
    public List<string> Parameters { get; }
    public List<Statement> Body { get; }

    public FunctionSymbol(string name, List<string> parameters, List<Statement> body)
    {
        Name = name;
        Parameters = parameters;
        Body = body;
    }
}

public struct ShortNumber
{
    private short _value;

    public ShortNumber(short value) => _value = value;
    public ShortNumber(int value) => _value = checked((short)value);

    public short Value => _value;

    public static implicit operator short(ShortNumber n) => n._value;
    public static implicit operator int(ShortNumber n) => n._value;
    public static implicit operator double(ShortNumber n) => n._value;
    public static implicit operator ShortNumber(short n) => new ShortNumber(n);
    public static implicit operator ShortNumber(int n) => new ShortNumber(checked((short)n));

    public override string ToString() => _value.ToString();
}

public struct FloatNumber
{
    private float _value;

    public FloatNumber(float value) => _value = value;

    public float Value => _value;

    public static implicit operator float(FloatNumber n) => n._value;
    public static implicit operator double(FloatNumber n) => n._value;
    public static implicit operator FloatNumber(float n) => new FloatNumber(n);

    public override string ToString() => _value.ToString("F2");
}
