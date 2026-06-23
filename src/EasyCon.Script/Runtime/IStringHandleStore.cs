namespace EasyCon.Script.Runtime;

/// <summary>
/// 字符串 handle 存储抽象：解耦 ScriptArray 与具体堆实现。
/// 设计参考：
///   - CPython 的 PyUnicodeObject 全局 intern 表（相同内容 → 相同对象）
///   - LuaJIT 的 GCstr 全局 string hash table（指针相等即字符串相等）
///
/// 由 RuntimeHeap 实现。ScriptArray 通过此接口以 handle 形式持有字符串，
/// 使字符串数组的内部存储从 string?[] (8B/槽) 压缩为 int[] (4B/槽)，
/// 并使 == 比较退化为 handle 整数比较（intern 后）。
/// </summary>
public interface IStringHandleStore
{
    /// <summary>
    /// 驻留字符串：已存在则返回已有 handle，否则分配新 handle。
    /// 保证相同内容 → 相同 handle（content-addressed）。
    /// </summary>
    int Intern(string value);

    /// <summary>按 handle 读取字符串内容。</summary>
    string Get(int handle);

    /// <summary>复制 handle 指向的字符串为新的独立 handle（用于数组深拷贝/值语义）。</summary>
    int Copy(int handle);

    /// <summary>释放 handle（引用计数语义：数组销毁时释放其持有的所有字符串 handle）。</summary>
    void Free(int handle);
}
