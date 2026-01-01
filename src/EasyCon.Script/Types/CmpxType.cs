using Python.Runtime;
using System.ComponentModel;
#if false
namespace ScriptLanguage.Types
{
    // Python对象包装器
    public class ScriptPyObject : ScriptValueBase
    {
        private readonly PyObject _pyObject;

        public PyObject PyObject => _pyObject;

        public ScriptPyObject(PyObject pyObject)
        {
            _pyObject = pyObject ?? throw new ArgumentNullException(nameof(pyObject));
        }

        public override ScriptType Type
        {
            get
            {
                using (Py.GIL())
                {
                    if (_pyObject.IsNone()) return ScriptType.Null;
                    if (IsBool()) return ScriptType.Bool;
                    if (IsInt()) return ScriptType.Integer;
                    if (IsFloat()) return ScriptType.Float;
                    if (IsString()) return ScriptType.String;
                    if (IsList() || IsTuple()) return ScriptType.List;
                    if (IsDict()) return ScriptType.Dict;
                    if (IsSet()) return ScriptType.Set;
                    if (IsFunction()) return ScriptType.Function;
                    if (IsModule()) return ScriptType.Module;

                    return ScriptType.Object;
                }
            }
        }

        public override object RawValue => _pyObject;
        public override bool IsTruthy => !_pyObject.IsFalse();

        // Python类型检查
        private bool IsBool() => _pyObject.IsBool();
        private bool IsInt() => _pyObject.IsInt();
        private bool IsFloat() => _pyObject.IsFloat();
        private bool IsString() => _pyObject.IsString();
        private bool IsList() => _pyObject.IsList();
        private bool IsTuple() => _pyObject.IsTuple();
        private bool IsDict() => _pyObject.IsDict();
        private bool IsSet() => _pyObject.IsSet();
        private bool IsFunction() => _pyObject.IsCallable() && !_pyObject.IsInstance();
        private bool IsModule() => _pyObject.HasAttr("__name__") && _pyObject.HasAttr("__file__");

        public override IScriptValue Add(IScriptValue other)
        {
            using (Py.GIL())
            {
                try
                {
                    var result = _pyObject.InvokeMethod("__add__", other.ToPython());
                    return TypeConverter.FromPython(result);
                }
                catch (PythonException)
                {
                    return base.Add(other);
                }
            }
        }

        public override IScriptValue GetItem(IScriptValue index)
        {
            using (Py.GIL())
            {
                var result = _pyObject.GetItem(index.ToPython());
                return TypeConverter.FromPython(result);
            }
        }

        public override void SetItem(IScriptValue index, IScriptValue value)
        {
            using (Py.GIL())
            {
                _pyObject.SetItem(index.ToPython(), value.ToPython());
            }
        }

        public override bool Equals(IScriptValue other)
        {
            using (Py.GIL())
            {
                try
                {
                    var pyOther = other.ToPython();
                    var result = _pyObject.InvokeMethod("__eq__", pyOther);
                    return result.IsTrue();
                }
                catch (PythonException)
                {
                    return false;
                }
            }
        }

        public IScriptValue Invoke(params IScriptValue[] args)
        {
            using (Py.GIL())
            {
                var pyArgs = args.Select(arg => arg.ToPython()).ToArray();
                var result = _pyObject.Invoke(pyArgs);
                return TypeConverter.FromPython(result);
            }
        }

        public IScriptValue GetAttribute(string name)
        {
            using (Py.GIL())
            {
                if (_pyObject.HasAttr(name))
                {
                    var attr = _pyObject.GetAttr(name);
                    return TypeConverter.FromPython(attr);
                }

                throw new InvalidOperationException($"Object has no attribute '{name}'");
            }
        }

        public void SetAttribute(string name, IScriptValue value)
        {
            using (Py.GIL())
            {
                _pyObject.SetAttr(name, value.ToPython());
            }
        }

        public override string Repr()
        {
            using (Py.GIL())
            {
                try
                {
                    var repr = _pyObject.InvokeMethod("__repr__");
                    return repr.ToString();
                }
                catch (PythonException)
                {
                    return $"<Python object at {_pyObject.Handle:X}>";
                }
            }
        }

        public override string Str()
        {
            using (Py.GIL())
            {
                try
                {
                    var str = _pyObject.InvokeMethod("__str__");
                    return str.ToString();
                }
                catch (PythonException)
                {
                    return Repr();
                }
            }
        }

        public override PyObject ToPython() => _pyObject;

        public override object ToClr()
        {
            using (Py.GIL())
            {
                return _pyObject.As<object>();
            }
        }
    }

    // 函数类型
    public class ScriptFunction : ScriptValueBase
    {
        private readonly Func<IScriptValue[], IScriptValue> _func;
        private readonly string _name;

        public ScriptFunction(string name, Func<IScriptValue[], IScriptValue> func)
        {
            _name = name;
            _func = func;
        }

        public override ScriptType Type => ScriptType.Function;
        public override object RawValue => _func;

        public IScriptValue Invoke(params IScriptValue[] args) => _func(args);

        public override string Repr() => $"<function {_name}>";
        public override string Str() => $"<function {_name}>";

        public override PyObject ToPython()
        {
            using (Py.GIL())
            {
                // 将C#函数包装为Python可调用对象
                var wrapper = new Func<PyObject[], PyObject>(pyArgs =>
                {
                    var args = pyArgs.Select(TypeConverter.FromPython).ToArray();
                    var result = Invoke(args);
                    return result.ToPython();
                });

                return PyObject.FromManagedObject(wrapper);
            }
        }

        public override object ToClr() => _func;
    }

    // 模块类型
    public class ScriptModule : ScriptValueBase
    {
        private readonly string _name;
        private readonly ScriptDict _attributes;

        public ScriptModule(string name)
        {
            _name = name;
            _attributes = new ScriptDict();
        }

        public override ScriptType Type => ScriptType.Module;
        public override object RawValue => _attributes;

        public void SetAttribute(string name, IScriptValue value) => _attributes.Set(new ScriptString(name), value);
        public IScriptValue GetAttribute(string name) => _attributes.Get(new ScriptString(name));

        public override string Repr() => $"<module '{_name}'>";
        public override string Str() => $"<module '{_name}'>";

        public override IScriptValue GetItem(IScriptValue index)
        {
            if (index is ScriptString ss)
            {
                return GetAttribute(ss.Value);
            }

            return base.GetItem(index);
        }

        public override PyObject ToPython()
        {
            using (Py.GIL())
            {
                var module = PyModule.Import(_name);
                return module;
            }
        }

        public override object ToClr() => _attributes.ToClr();
    }
}
#endif