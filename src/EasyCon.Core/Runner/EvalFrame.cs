namespace EasyCon.Core.Runner;

/// <summary>
/// 类型化执行帧，按类别存储变量，替代 Value[]。
/// Handle 字段存储 1-based 索引到 RuntimeHeap（0 = null）。
/// </summary>
internal struct EvalFrame
{
    public int[] Ints;
    public long[] Longs;
    public double[] Doubles;
    public int[] Handles;

    public EvalFrame(int intSlots, int longSlots, int doubleSlots, int handleSlots)
    {
        Ints = intSlots > 0 ? new int[intSlots] : Array.Empty<int>();
        Longs = longSlots > 0 ? new long[longSlots] : Array.Empty<long>();
        Doubles = doubleSlots > 0 ? new double[doubleSlots] : Array.Empty<double>();
        Handles = handleSlots > 0 ? new int[handleSlots] : Array.Empty<int>();
    }
}