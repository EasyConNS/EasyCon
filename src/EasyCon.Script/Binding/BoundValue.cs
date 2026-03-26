using System.Globalization;

namespace EasyCon.Script.Binding;

/// <summary>
/// 表示一个动态值，可以是 int、bool、string 或一维数组（元素为 Value）。
/// 支持相等性比较、有序比较（同类型）和下标索引（字符串按 Unicode 字符，数组按元素）。
/// </summary>
public readonly struct Value : IEquatable<Value>, IComparable<Value>
{
    /// <summary>当前值的类型</summary>
    public ValueType Kind { get; }

    // 实际数据存储：int/bool 直接装箱，string 为字符串，array 为 Value[]
    private readonly object? _value;

    private Value(object? value, ValueType kind)
    {
        _value = value;
        Kind = kind;
    }

    // 工厂方法
    public static Value From(object? o) 
    {
        if (o == null) return Void;
        return o switch
        {
            int i => new Value(i, ValueType.Int),
            bool b => new Value(b, ValueType.Bool),
            string s => new Value(s, ValueType.String),
            Value v => v,
            Value[] array => new Value(array, ValueType.Array),
            _ => throw new ArgumentException($"未知类型的数据 '{o}' 不支持类型 {o?.GetType()}")
        };
    }
    public static Value Void { get; } = new Value(null, ValueType.Void);
    public static implicit operator Value(int i) => new(i, ValueType.Int);
    public static implicit operator Value(bool b) => new(b, ValueType.Bool);
    public static implicit operator Value(string s) => new(s ?? throw new ArgumentNullException(nameof(s)), ValueType.String);
    public static implicit operator Value(Value[] array) =>
        new(array?.ToArray() ?? throw new ArgumentNullException(nameof(array)), ValueType.Array); // 存储副本以保证不可变性

    // 类型安全的取值方法（若类型不匹配则抛出异常）
    public int AsInt() => Kind == ValueType.Int ? (int)_value! : throw new InvalidOperationException("Value is not an int.");
    public bool AsBool() => Kind == ValueType.Bool ? (bool)_value! : throw new InvalidOperationException("Value is not a bool.");
    public string AsString() => Kind == ValueType.String ? (string)_value! : throw new InvalidOperationException("Value is not a string.");

    /// <summary>获取字符串的文本元素长度或数组的长度。</summary>
    public int Length
    {
        get
        {
            return Kind switch
            {
                ValueType.String => new StringInfo(AsString()).LengthInTextElements,
                ValueType.Array => ((Value[])_value!).Length,
                _ => 0,
            };
        }
    }

    /// <summary>下标索引器：字符串按文本元素索引返回单字符字符串，数组按索引返回元素。</summary>
    public Value this[int index]
    {
        get
        {
            return Kind switch
            {
                ValueType.String =>  GetStringElement(AsString(), index),
                ValueType.Array => ((Value[])_value!)[index],
                _ => throw new InvalidOperationException("Indexing not supported for this type.")
            };
        }
    }
    public Value this[Range range]
    {
        get
        {
            return Kind switch
            {
                ValueType.String =>  AsString()[range],
                ValueType.Array => ((Value[])_value!)[range],
                _ => throw new InvalidOperationException("Indexing not supported for this type.")
            };
        }
    }
    // 辅助方法：从字符串中获取指定位置的文本元素（返回字符串形式的 Value）
    private static Value GetStringElement(string s, int index)
    {
        var si = new StringInfo(s);
        if (index < 0 || index >= si.LengthInTextElements)
            throw new IndexOutOfRangeException();
        return new Value(si.SubstringByTextElements(index, 1), ValueType.String);
    }

    public Value Concat(Value other)
    {
        if (Kind != ValueType.Array || other.Kind != ValueType.Array)
            throw new InvalidOperationException("Both values must be arrays to concatenate.");

        return new Value(((Value[])_value!).Concat((Value[])other._value!), ValueType.Array);
    }

    public Value Append(Value other)
    {
        return Kind switch
        {
            ValueType.Array => new Value(((Value[])_value!).Append(other).ToArray(), ValueType.Array),
            _ => throw new InvalidOperationException("Append not supported for this type.")
        };
    }

    /// <summary>转换为布尔值，用于逻辑判断（int: 非0为真；bool: 自身；string: 非空为真；array: 非空为真）</summary>
    public bool ToBoolean()
    {
        return Kind switch
        {
            ValueType.Int => AsInt() != 0,
            ValueType.Bool => AsBool(),
            ValueType.String => !string.IsNullOrEmpty(AsString()),
            ValueType.Array => ((Value[])_value!).Length > 0,
            ValueType.Void => false,
            _ => false
        };
    }

    // IEquatable<Value>
    public bool Equals(Value other)
    {
        if (Kind != other.Kind) return false;
        return Kind switch
        {
            ValueType.Int => AsInt() == other.AsInt(),
            ValueType.Bool => AsBool() == other.AsBool(),
            ValueType.String => AsString() == other.AsString(),
            ValueType.Array => ((Value[])_value!).SequenceEqual((Value[])other._value!), // 直接比较内部数组，无需复制
            ValueType.Void => true, // 两个 void 相等
            _ => false
        };
    }

    public override bool Equals(object? obj) => obj is Value other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + Kind.GetHashCode();
            switch (Kind)
            {
                case ValueType.Int: hash = hash * 31 + AsInt().GetHashCode(); break;
                case ValueType.Bool: hash = hash * 31 + AsBool().GetHashCode(); break;
                case ValueType.String: hash = hash * 31 + AsString().GetHashCode(); break;
                case ValueType.Array:
                    foreach (var v in (Value[])_value!)
                        hash = hash * 31 + v.GetHashCode();
                    break;
            }
            return hash;
        }
    }

    // 有序比较（不同类型不能比较，数组不支持有序比较）
    public int CompareTo(Value other)
    {
        if (Kind != other.Kind)
            throw new InvalidOperationException($"Cannot compare values of different kinds {Kind} <=> {other.Kind}");
        return Kind switch
        {
            ValueType.Int => AsInt().CompareTo(other.AsInt()),
            ValueType.String => string.Compare(AsString(), other.AsString(), StringComparison.Ordinal),
            _ => throw new InvalidOperationException($"Cannot compare {Kind} <=> {other.Kind}"),
        };
    }

    // 重载比较运算符
    public static bool operator ==(Value left, Value right) => left.Equals(right);
    public static bool operator !=(Value left, Value right) => !left.Equals(right);
    public static bool operator <(Value left, Value right) => left.CompareTo(right) < 0;
    public static bool operator >(Value left, Value right) => left.CompareTo(right) > 0;
    public static bool operator <=(Value left, Value right) => left.CompareTo(right) <= 0;
    public static bool operator >=(Value left, Value right) => left.CompareTo(right) >= 0;

    // 重载算数运算符
    public static Value operator +(Value left, Value right)
    {
        // void 参与运算直接抛出异常
        if (left.Kind == ValueType.Void || right.Kind == ValueType.Void)
            throw new InvalidOperationException("Cannot perform '+' operation on void.");

        // 相同类型处理
        if (left.Kind == right.Kind)
        {
            return left.Kind switch
            {
                ValueType.Int => (Value)(left.AsInt() + right.AsInt()),
                ValueType.String => (Value)(left.AsString() + right.AsString()),
                ValueType.Array => left.Concat(right),
                _ => throw new InvalidOperationException($"Operator '+' is not supported for {left.Kind}."),
            };

        }

        // 不同类型与 string 混合
        if (left.Kind == ValueType.String || right.Kind == ValueType.String)
        {
            return left.AsString() + right.AsString();
        }

        // 其他组合均不支持
        throw new InvalidOperationException($"Cannot apply operator '+' to values of types {left.Kind} and {right.Kind}.");
    }

    public override string ToString()
    {
        return Kind switch
        {
            ValueType.Int => AsInt().ToString(),
            ValueType.Bool => AsBool().ToString(),
            ValueType.String => AsString(),
            ValueType.Array => "[" + string.Join(", ", ((Value[])_value!).Select(v => v.ToString())) + "]",
            _ => base.ToString() ?? ""
        };
    }
}

#region 类型
public abstract class ScriptType
{
    public abstract string Name { get; }
    public abstract bool IsAssignableFrom(ScriptType other); // 用于类型兼容性检查

    public static readonly VoidType Void = new();
    public static readonly IntType Int = new();
    public static readonly BoolType Bool = new();
    public static readonly StringType String = new();
}

public sealed class VoidType : ScriptType
{
    public override string Name => "void";
    public override bool IsAssignableFrom(ScriptType other) => false;
}

public class IntType : ScriptType
{
    public override string Name => "int";
    public override bool IsAssignableFrom(ScriptType other) => other is IntType;
}

public class BoolType : ScriptType
{
    public override string Name => "bool";
    public override bool IsAssignableFrom(ScriptType other) => other is BoolType;
}

public class StringType : ScriptType
{
    public override string Name => "string";
    public override bool IsAssignableFrom(ScriptType other) => other is StringType;
}

public class ArrayType : ScriptType
{
    public ScriptType ElementType { get; }
    public ArrayType(ScriptType elementType) => ElementType = elementType;

    public override string Name => $"{ElementType.Name}[]";
    public override bool IsAssignableFrom(ScriptType other)
    {
        // 数组类型兼容：必须是同类型数组，且元素类型兼容（简单场景要求完全相同）
        return other is ArrayType arr && ElementType.IsAssignableFrom(arr.ElementType);
    }
}
#endregion

#region 数值
public abstract class ScriptValue
{
    public abstract ScriptType Type { get; }
    public static ScriptValue Void { get; } = new VoidValue();
}

public sealed class VoidValue : ScriptValue
{
    public override ScriptType Type { get; } = new VoidType();
}

public class IntValue : ScriptValue
{
    public int Value { get; }
    public override ScriptType Type { get; } = new IntType();

    public IntValue(int value) => Value = value;
}

public class BoolValue : ScriptValue
{
    public bool Value { get; }
    public override ScriptType Type { get; } = new BoolType();

    public BoolValue(bool value) => Value = value;
}

public class StringValue : ScriptValue
{
    public string Value { get; }
    public override ScriptType Type { get; } = new StringType();

    public StringValue(string value) => Value = value;
}

public class ArrayValue : ScriptValue
{
    private readonly List<ScriptValue> _elements;
    public override ScriptType Type { get; }

    public ArrayValue(ScriptType elementType, IEnumerable<ScriptValue> elements)
    {
        Type = new ArrayType(elementType);
        _elements = elements.ToList();

        // 可选：检查每个元素类型是否与 elementType 匹配
        foreach (var elem in _elements)
        {
            if (!elem.Type.IsAssignableFrom(elementType))
                throw new ArgumentException($"数组元素类型必须为 {elementType.Name}");
        }
    }

    public ScriptValue this[int index]
    {
        get => _elements[index];
        set
        {
            // 运行时检查赋值类型
            if (!value.Type.IsAssignableFrom(((ArrayType)Type).ElementType))
                throw new InvalidOperationException($"数组元素类型不匹配，需要 {((ArrayType)Type).ElementType.Name}");
            _elements[index] = value;
        }
    }

    public int Length => _elements.Count;
}
#endregion