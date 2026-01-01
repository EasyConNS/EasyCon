using System;
using System.Collections.Generic;
using System.Dynamic;
using Python.Runtime;

namespace ScriptLanguage.Types
{
    // 类型标记枚举
    public enum ScriptType
    {
        Null,
        Bool,
        Integer,
        Float,
        String,
        List,
        Tuple,
        Dict,
        Set,
        Function,
        Object,
        Module,
        Complex,
        Bytes,
        ByteArray,
        Range,
        Slice,
        Generator,
        Coroutine,
        NotImplemented,
        Ellipsis
    }

    // 值接口
    public interface IScriptValue
    {
        ScriptType Type { get; }
        object RawValue { get; }
        bool IsTruthy { get; }
        string Repr();
        string Str();

        // Python兼容的魔术方法
        IScriptValue Add(IScriptValue other);
        IScriptValue Sub(IScriptValue other);
        IScriptValue Mul(IScriptValue other);
        IScriptValue Div(IScriptValue other);
        IScriptValue Mod(IScriptValue other);
        IScriptValue Pow(IScriptValue other);

        bool Equals(IScriptValue other);
        bool LessThan(IScriptValue other);
        bool GreaterThan(IScriptValue other);

        IScriptValue GetItem(IScriptValue index);
        void SetItem(IScriptValue index, IScriptValue value);

        // 转换为Python对象
        PyObject ToPython();

        // 转换为C#对象
        object ToClr();
    }

    // 抽象基类
    public abstract class ScriptValueBase : IScriptValue
    {
        public abstract ScriptType Type { get; }
        public abstract object RawValue { get; }

        public virtual bool IsTruthy => true;

        public virtual string Repr() => ToString();
        public virtual string Str() => ToString();

        // 运算符重载默认实现
        public virtual IScriptValue Add(IScriptValue other) =>
            throw new InvalidOperationException($"Unsupported operation: {Type} + {other.Type}");

        public virtual IScriptValue Sub(IScriptValue other) =>
            throw new InvalidOperationException($"Unsupported operation: {Type} - {other.Type}");

        public virtual IScriptValue Mul(IScriptValue other) =>
            throw new InvalidOperationException($"Unsupported operation: {Type} * {other.Type}");

        public virtual IScriptValue Div(IScriptValue other) =>
            throw new InvalidOperationException($"Unsupported operation: {Type} / {other.Type}");

        public virtual IScriptValue Mod(IScriptValue other) =>
            throw new InvalidOperationException($"Unsupported operation: {Type} % {other.Type}");

        public virtual IScriptValue Pow(IScriptValue other) =>
            throw new InvalidOperationException($"Unsupported operation: {Type} ** {other.Type}");

        public virtual bool Equals(IScriptValue other) => false;
        public virtual bool LessThan(IScriptValue other) => false;
        public virtual bool GreaterThan(IScriptValue other) => false;

        public virtual IScriptValue GetItem(IScriptValue index) =>
            throw new InvalidOperationException($"Type {Type} is not subscriptable");

        public virtual void SetItem(IScriptValue index, IScriptValue value) =>
            throw new InvalidOperationException($"Type {Type} does not support item assignment");

        public abstract PyObject ToPython();
        public abstract object ToClr();

        public override string ToString() => Str();
    }
}