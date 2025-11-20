namespace EasyScript;

#if false

public class Scope
{
    private readonly Dictionary<string, RuntimeValue> _variables;
    private readonly Dictionary<string, Function> _functions;
    public Scope Parent { get; }

    public Scope(Scope parent = null)
    {
        _variables = new Dictionary<string, RuntimeValue>();
        _functions = new Dictionary<string, Function>();
        Parent = parent;
    }

    public void SetVariable(string name, RuntimeValue value)
    {
        _variables[name] = value;
    }

    public RuntimeValue GetVariable(string name)
    {
        if (_variables.ContainsKey(name))
            return _variables[name];

        return Parent?.GetVariable(name);
    }

    public bool HasVariable(string name)
    {
        return _variables.ContainsKey(name) || (Parent?.HasVariable(name) == true);
    }

    public void SetFunction(string name, Function function)
    {
        _functions[name] = function;
    }

    public Function GetFunction(string name)
    {
        if (_functions.ContainsKey(name))
            return _functions[name];

        return Parent?.GetFunction(name);
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
public class Function
{
    public string Name { get; }
    public List<string> Parameters { get; }
    public List<Statement> Body { get; }

    public Function(string name, List<string> parameters, List<Statement> body)
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
#endif