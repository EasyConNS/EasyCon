using Python.Runtime;

namespace ScriptLanguage.Types
{
    // 列表类型 (可变序列)
    public class ScriptList : ScriptValueBase
    {
        private readonly List<IScriptValue> _items;

        public IReadOnlyList<IScriptValue> Items => _items;

        public ScriptList() => _items = new List<IScriptValue>();
        public ScriptList(IEnumerable<IScriptValue> items) => _items = new List<IScriptValue>(items);

        public override ScriptType Type => ScriptType.List;
        public override object RawValue => _items;
        public override bool IsTruthy => _items.Count > 0;

        public void Append(IScriptValue value) => _items.Add(value);
        public void Extend(ScriptList other) => _items.AddRange(other._items);
        public IScriptValue Pop() => _items.Count > 0 ? _items[^1] : ScriptNull.None;

        public override IScriptValue GetItem(IScriptValue index)
        {
            if (index is ScriptInt si)
            {
                int idx = (int)si.RawValue;
                if (idx < 0) idx = _items.Count + idx;

                if (idx >= 0 && idx < _items.Count)
                {
                    return _items[idx];
                }

                throw new IndexOutOfRangeException($"List index {idx} out of range");
            }

            return base.GetItem(index);
        }

        public override void SetItem(IScriptValue index, IScriptValue value)
        {
            if (index is ScriptInt si)
            {
                int idx = (int)si.RawValue;
                if (idx < 0) idx = _items.Count + idx;

                if (idx >= 0 && idx < _items.Count)
                {
                    _items[idx] = value;
                    return;
                }

                throw new IndexOutOfRangeException($"List index {idx} out of range");
            }

            base.SetItem(index, value);
        }

        public override IScriptValue Add(IScriptValue other)
        {
            if (other is ScriptList sl)
            {
                var newList = new List<IScriptValue>(_items);
                newList.AddRange(sl._items);
                return new ScriptList(newList);
            }

            return base.Add(other);
        }

        public override bool Equals(IScriptValue other)
        {
            if (other is not ScriptList sl || sl._items.Count != _items.Count)
                return false;

            for (int i = 0; i < _items.Count; i++)
            {
                if (!_items[i].Equals(sl._items[i]))
                    return false;
            }

            return true;
        }

        public override string Repr()
        {
            var items = string.Join(", ", _items.Select(item => item.Repr()));
            return $"[{items}]";
        }

        public override PyObject ToPython()
        {
            using (Py.GIL())
            {
                var pyList = new PyList();
                foreach (var item in _items)
                {
                    pyList.Append(item.ToPython());
                }
                return pyList;
            }
        }

        public override object ToClr() => _items.Select(v => v.ToClr()).ToList();
    }

    // 元组类型 (不可变序列)
    public class ScriptTuple : ScriptValueBase
    {
        private readonly IReadOnlyList<IScriptValue> _items;

        public IReadOnlyList<IScriptValue> Items => _items;

        public ScriptTuple(params IScriptValue[] items) => _items = items.ToList();
        public ScriptTuple(IEnumerable<IScriptValue> items) => _items = items.ToList();

        public override ScriptType Type => ScriptType.Tuple;
        public override object RawValue => _items;
        public override bool IsTruthy => _items.Count > 0;

        public override IScriptValue GetItem(IScriptValue index)
        {
            if (index is ScriptInt si)
            {
                int idx = (int)si.RawValue;
                if (idx < 0) idx = _items.Count + idx;

                if (idx >= 0 && idx < _items.Count)
                {
                    return _items[idx];
                }

                throw new IndexOutOfRangeException($"Tuple index {idx} out of range");
            }

            return base.GetItem(index);
        }

        public override bool Equals(IScriptValue other)
        {
            if (other is not ScriptTuple st || st._items.Count != _items.Count)
                return false;

            for (int i = 0; i < _items.Count; i++)
            {
                if (!_items[i].Equals(st._items[i]))
                    return false;
            }

            return true;
        }

        public override string Repr()
        {
            if (_items.Count == 1)
            {
                return $"({_items[0].Repr()},)";
            }

            var items = string.Join(", ", _items.Select(item => item.Repr()));
            return $"({items})";
        }

        public override PyObject ToPython()
        {
            using (Py.GIL())
            {
                var pyItems = _items.Select(item => item.ToPython()).ToArray();
                return PyObject.FromManagedObject(pyItems);
            }
        }

        public override object ToClr() => _items.Select(v => v.ToClr()).ToArray();
    }

    // 字典类型
    public class ScriptDict : ScriptValueBase
    {
        private readonly Dictionary<IScriptValue, IScriptValue> _dict;

        public ScriptDict() => _dict = new Dictionary<IScriptValue, IScriptValue>(new ScriptValueComparer());

        public override ScriptType Type => ScriptType.Dict;
        public override object RawValue => _dict;
        public override bool IsTruthy => _dict.Count > 0;

        public void Set(IScriptValue key, IScriptValue value) => _dict[key] = value;
        public IScriptValue Get(IScriptValue key) => _dict.TryGetValue(key, out var value) ? value : ScriptNull.None;
        public bool ContainsKey(IScriptValue key) => _dict.ContainsKey(key);

        public override IScriptValue GetItem(IScriptValue key)
        {
            if (_dict.TryGetValue(key, out var value))
            {
                return value;
            }

            throw new KeyNotFoundException($"Key not found: {key.Repr()}");
        }

        public override void SetItem(IScriptValue key, IScriptValue value)
        {
            _dict[key] = value;
        }

        public override bool Equals(IScriptValue other)
        {
            if (other is not ScriptDict sd || sd._dict.Count != _dict.Count)
                return false;

            foreach (var kvp in _dict)
            {
                if (!sd._dict.TryGetValue(kvp.Key, out var otherValue) ||
                    !kvp.Value.Equals(otherValue))
                {
                    return false;
                }
            }

            return true;
        }

        public override string Repr()
        {
            var items = string.Join(", ", _dict.Select(kvp => $"{kvp.Key.Repr()}: {kvp.Value.Repr()}"));
            return $"{{{items}}}";
        }

        public override PyObject ToPython()
        {
            using (Py.GIL())
            {
                var pyDict = new PyDict();
                foreach (var kvp in _dict)
                {
                    pyDict.SetItem(kvp.Key.ToPython(), kvp.Value.ToPython());
                }
                return pyDict;
            }
        }

        public override object ToClr()
        {
            var dict = new Dictionary<object, object>();
            foreach (var kvp in _dict)
            {
                dict[kvp.Key.ToClr()] = kvp.Value.ToClr();
            }
            return dict;
        }

        // 自定义比较器
        private class ScriptValueComparer : IEqualityComparer<IScriptValue>
        {
            public bool Equals(IScriptValue x, IScriptValue y) => x?.Equals(y) ?? y == null;
            public int GetHashCode(IScriptValue obj) => obj?.GetHashCode() ?? 0;
        }
    }

    // 集合类型
    public class ScriptSet : ScriptValueBase
    {
        private readonly HashSet<IScriptValue> _set;

        public ScriptSet() => _set = new HashSet<IScriptValue>(new ScriptValueComparer());
        public ScriptSet(IEnumerable<IScriptValue> items) : this() => UnionWith(items);

        //public void Add(IScriptValue item) => _set.Add(item);
        public void Remove(IScriptValue item) => _set.Remove(item);
        public bool Contains(IScriptValue item) => _set.Contains(item);
        public void UnionWith(IEnumerable<IScriptValue> other)
        {
            foreach (var item in other)
                _set.Add(item);
        }

        public override ScriptType Type => ScriptType.Set;
        public override object RawValue => _set;
        public override bool IsTruthy => _set.Count > 0;

        public override IScriptValue Add(IScriptValue other)
        {
            if (other is ScriptSet ss)
            {
                var newSet = new ScriptSet(_set);
                newSet.UnionWith(ss._set);
                return newSet;
            }

            return base.Add(other);
        }

        public override string Repr()
        {
            if (_set.Count == 0) return "set()";

            var items = string.Join(", ", _set.Select(item => item.Repr()));
            return $"{{{items}}}";
        }

        public override PyObject ToPython()
        {
            using (Py.GIL())
            {
                var pySet = PyObject.FromManagedObject(_set.Select(item => item.ToPython()).ToArray());
                return pySet;
            }
        }

        public override object ToClr() => new HashSet<object>(_set.Select(v => v.ToClr()));

        private class ScriptValueComparer : IEqualityComparer<IScriptValue>
        {
            public bool Equals(IScriptValue x, IScriptValue y) => x?.Equals(y) ?? y == null;
            public int GetHashCode(IScriptValue obj) => obj?.GetHashCode() ?? 0;
        }
    }
}