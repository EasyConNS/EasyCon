using EasyCon.Script.Symbols;

namespace EasyCon.Core.Runner;

/// <summary>
/// 扁平执行帧：统一的 TaggedValue 数组，替代 4 路分裂的 Ints/Longs/Doubles/Handles。
/// 类比 CPython 的 localsplus —— 一个连续数组存放所有局部变量。
/// 类型信息在 SSA 操作码中，存储层无需类型分裂。
/// </summary>
internal struct EvalFrame
{
    /// <summary>局部变量槽位（含参数），编译时按 SlotIndex 索引。</summary>
    public TaggedValue[] Locals;

    public EvalFrame(int slotCount)
    {
        Locals = slotCount > 0 ? new TaggedValue[slotCount] : Array.Empty<TaggedValue>();
    }
}