using Python.Runtime;

namespace ScriptLanguage.Types
{
    // Null类型 (对应Python的None)
    public class ScriptNull : ScriptValueBase
    {
        public static readonly ScriptNull None = new ScriptNull();

        private ScriptNull() { }

        public override ScriptType Type => ScriptType.Null;
        public override object RawValue => null;
        public override bool IsTruthy => false;

        public override string Repr() => "None";
        public override string Str() => "None";

        public override bool Equals(IScriptValue other)
        {
            return other.Type == ScriptType.Null;
        }

        public override PyObject ToPython()
        {
            using (Py.GIL())
            {
                return PyObject.FromManagedObject(null);
            }
        }

        public override object ToClr() => null;
    }

    // 布尔类型
    public class ScriptBool : ScriptValueBase
    {
        public static readonly ScriptBool True = new ScriptBool(true);
        public static readonly ScriptBool False = new ScriptBool(false);

        private readonly bool _value;

        private ScriptBool(bool value) => _value = value;

        public static ScriptBool FromBool(bool value) => value ? True : False;

        public override ScriptType Type => ScriptType.Bool;
        public override object RawValue => _value;
        public override bool IsTruthy => _value;

        public override string Repr() => _value ? "True" : "False";
        public override string Str() => Repr();

        public override bool Equals(IScriptValue other)
        {
            return other is ScriptBool sb && sb._value == _value;
        }

        public override PyObject ToPython()
        {
            using (Py.GIL())
            {
                return PyObject.FromManagedObject(_value);
            }
        }

        public override object ToClr() => _value;
    }

    // 整数类型 (Python的int是任意精度)
    public class ScriptInt : ScriptValueBase
    {
        private readonly System.Numerics.BigInteger _value;

        public ScriptInt(long value) => _value = value;
        public ScriptInt(System.Numerics.BigInteger value) => _value = value;

        public override ScriptType Type => ScriptType.Integer;
        public override object RawValue => _value;

        public override IScriptValue Add(IScriptValue other)
        {
            return other switch
            {
                ScriptInt si => new ScriptInt(_value + si._value),
                ScriptFloat sf => new ScriptFloat((double)_value + sf.Value),
                _ => base.Add(other)
            };
        }

        public override IScriptValue Sub(IScriptValue other)
        {
            return other switch
            {
                ScriptInt si => new ScriptInt(_value - si._value),
                ScriptFloat sf => new ScriptFloat((double)_value - sf.Value),
                _ => base.Sub(other)
            };
        }

        public override IScriptValue Mul(IScriptValue other)
        {
            return other switch
            {
                ScriptInt si => new ScriptInt(_value * si._value),
                ScriptFloat sf => new ScriptFloat((double)_value * sf.Value),
                ScriptString ss => new ScriptString(new string(' ', (int)_value) + ss.Value),
                _ => base.Mul(other)
            };
        }

        public override IScriptValue Div(IScriptValue other)
        {
            return other switch
            {
                ScriptInt si =>
                    _value % si._value == 0
                        ? (IScriptValue)new ScriptInt(_value / si._value)
                        : new ScriptFloat((double)_value / (double)si._value),
                ScriptFloat sf => new ScriptFloat((double)_value / sf.Value),
                _ => base.Div(other)
            };
        }

        public override bool Equals(IScriptValue other)
        {
            return other switch
            {
                ScriptInt si => _value == si._value,
                ScriptFloat sf => (double)_value == sf.Value,
                _ => false
            };
        }

        public override bool LessThan(IScriptValue other)
        {
            return other switch
            {
                ScriptInt si => _value < si._value,
                ScriptFloat sf => (double)_value < sf.Value,
                _ => base.LessThan(other)
            };
        }

        public override string Repr() => _value.ToString();
        public override string Str() => _value.ToString();

        public override PyObject ToPython()
        {
            using (Py.GIL())
            {
                if (_value <= long.MaxValue && _value >= long.MinValue)
                {
                    return PyInt.FromManagedObject((long)_value);
                }
                else
                {
                    // 大整数处理
                    return PyObject.FromManagedObject(_value.ToString());
                }
            }
        }

        public override object ToClr() => _value;
    }

    // 浮点数类型
    public class ScriptFloat : ScriptValueBase
    {
        public double Value { get; }

        public ScriptFloat(double value) => Value = value;

        public override ScriptType Type => ScriptType.Float;
        public override object RawValue => Value;

        public override IScriptValue Add(IScriptValue other)
        {
            return other switch
            {
                ScriptInt si => new ScriptFloat(Value + (double)si.RawValue),
                ScriptFloat sf => new ScriptFloat(Value + sf.Value),
                _ => base.Add(other)
            };
        }

        public override bool Equals(IScriptValue other)
        {
            return other switch
            {
                ScriptInt si => Value == (double)si.RawValue,
                ScriptFloat sf => Value == sf.Value,
                _ => false
            };
        }

        public override string Repr()
        {
            if (double.IsNaN(Value)) return "nan";
            if (double.IsPositiveInfinity(Value)) return "inf";
            if (double.IsNegativeInfinity(Value)) return "-inf";

            return Value.ToString("G15");
        }

        public override PyObject ToPython()
        {
            using (Py.GIL())
            {
                return PyFloat.FromManagedObject(Value);
            }
        }

        public override object ToClr() => Value;
    }

    // 字符串类型
    public class ScriptString : ScriptValueBase
    {
        public string Value { get; }

        public ScriptString(string value) => Value = value ?? "";

        public override ScriptType Type => ScriptType.String;
        public override object RawValue => Value;
        public override bool IsTruthy => !string.IsNullOrEmpty(Value);

        public override IScriptValue Add(IScriptValue other)
        {
            return other switch
            {
                ScriptString ss => new ScriptString(Value + ss.Value),
                ScriptInt si => new ScriptString(Value + si.RawValue),
                ScriptFloat sf => new ScriptString(Value + sf.Value),
                ScriptBool sb => new ScriptString(Value + sb.RawValue),
                _ => base.Add(other)
            };
        }

        public override IScriptValue Mul(IScriptValue other)
        {
            if (other is ScriptInt si)
            {
                int count = (int)si.RawValue;
                if (count <= 0) return new ScriptString("");

                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    sb.Append(Value);
                }
                return new ScriptString(sb.ToString());
            }

            return base.Mul(other);
        }

        public override bool Equals(IScriptValue other)
        {
            return other switch
            {
                ScriptString ss => Value == ss.Value,
                _ => false
            };
        }

        public override bool LessThan(IScriptValue other)
        {
            return other is ScriptString ss && string.Compare(Value, ss.Value, StringComparison.Ordinal) < 0;
        }

        public override IScriptValue GetItem(IScriptValue index)
        {
            if (index is ScriptInt si)
            {
                int idx = (int)si.RawValue;
                if (idx < 0) idx = Value.Length + idx;

                if (idx >= 0 && idx < Value.Length)
                {
                    return new ScriptString(Value[idx].ToString());
                }

                throw new IndexOutOfRangeException($"String index {idx} out of range");
            }

            return base.GetItem(index);
        }

        public override string Repr()
        {
            // 简单转义处理
            var escaped = Value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");

            return $"\"{escaped}\"";
        }

        public override string Str() => Value;

        public override PyObject ToPython()
        {
            using (Py.GIL())
            {
                return PyString.FromManagedObject(Value);
            }
        }

        public override object ToClr() => Value;
    }
}