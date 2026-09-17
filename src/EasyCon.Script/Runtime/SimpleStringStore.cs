using System.Collections.Generic;

namespace EasyCon.Script.Runtime;

/// <summary>
/// 简单的 IStringHandleStore 实现：用于单元测试和不需要 RuntimeHeap 的场景。
/// 提供 intern 能力但不做内存回收（Free 为空操作）。
/// </summary>
public sealed class SimpleStringStore : IStringHandleStore
{
    private readonly List<string> _strings = new() { "" }; // index 0 = null sentinel
    private readonly Dictionary<string, int> _intern = new(StringComparer.Ordinal);

    public int Intern(string value)
    {
        if (_intern.TryGetValue(value, out int h)) return h;
        int handle = _strings.Count;
        _strings.Add(value);
        _intern[value] = handle;
        return handle;
    }

    public string Get(int handle) =>
        handle > 0 && handle < _strings.Count ? _strings[handle] : string.Empty;

    public int Copy(int handle) => handle; // 简单实现：直接返回同一 handle（无引用计数）

    public void Free(int handle) { /* no-op：简单 store 不回收 */ }
}