using EasyCon.Script.Symbols;

namespace EasyCon.Script.Bytecode;

/// <summary>
/// ECX 模拟执行兼容入口。Desktop 计划分派到 PC 专用入口；两端共用指令语义内核，
/// 不复制算术、数组、引用计数和取消逻辑。可分发编码约束仍由 Portable 编译及容器检查。
/// </summary>
public sealed class EcxInterpreter
{
    readonly InstructionExecutor _executor;

    EcxInterpreter(InstructionExecutor executor) => _executor = executor;

    public int ErrorFunc => _executor.ErrorFunc;
    public int ErrorPc => _executor.ErrorPc;
    public TaggedValue Result => _executor.Result;
    internal int HeapLiveCount => _executor.HeapLiveCount;
    internal int HeapHighWater => _executor.HeapHighWater;
    internal long HeapAllocTotal => _executor.HeapAllocTotal;
    internal long HeapReleaseTotal => _executor.HeapReleaseTotal;
    internal int HeapFreeListCount => _executor.HeapFreeListCount;
    internal bool HeapCountsConsistent => _executor.HeapCountsConsistent;
    internal static bool CopyOnWrite
    {
        get => InstructionExecutor.CopyOnWrite;
        set => InstructionExecutor.CopyOnWrite = value;
    }

    public const int OK = InstructionExecutor.OK;
    public const int YIELD = InstructionExecutor.YIELD;
    public const int CANCELLED = InstructionExecutor.CANCELLED;
    public const int ERR_IMAGE = InstructionExecutor.ERR_IMAGE;
    public const int ERR_OPCODE = InstructionExecutor.ERR_OPCODE;
    public const int ERR_SLOT = InstructionExecutor.ERR_SLOT;
    public const int ERR_TYPE = InstructionExecutor.ERR_TYPE;
    public const int ERR_INDEX = InstructionExecutor.ERR_INDEX;
    public const int ERR_DIVZERO = InstructionExecutor.ERR_DIVZERO;
    public const int ERR_DEPTH = InstructionExecutor.ERR_DEPTH;
    public const int ERR_NOSUCHNATIVE = InstructionExecutor.ERR_NOSUCHNATIVE;
    public const int ERR_HOST = InstructionExecutor.ERR_HOST;
    public const int ERR_OOM = InstructionExecutor.ERR_OOM;
    public const int ERR_IL = InstructionExecutor.ERR_IL;
    public const int ERR_FEAT = InstructionExecutor.ERR_FEAT;
    public const int MaxCallDepth = InstructionExecutor.MaxCallDepth;

    public static int Run(EcxImage image, EcxHost host, CancellationToken token = default)
        => Run(image, host, out _, out _, out _, out _, token);

    public static int Run(EcxImage image, EcxHost host, out Value result, CancellationToken token = default)
        => Run(image, host, out _, out _, out _, out result, token);

    public static int Run(EcxImage image, EcxHost host, out int errorFunc, out int errorPc, CancellationToken token = default)
        => Run(image, host, out _, out errorFunc, out errorPc, out _, token);

    public static int Run(EcxImage image, EcxHost host, out TaggedValue rawResult, out int errorFunc,
        out int errorPc, out Value result, CancellationToken token = default)
        => image.Target == ScriptTarget.Desktop
            ? PcInterpreter.Run(image, host, out rawResult, out errorFunc, out errorPc, out result, token)
            : InstructionExecutor.Run(image, host, out rawResult, out errorFunc, out errorPc, out result, token);

    /// <summary>保留原有诊断观测接口，两种目标都使用同一引用计数语义。</summary>
    public static int RunWithVm(EcxImage image, EcxHost host, out EcxInterpreter vm, CancellationToken token = default)
    {
        int code = InstructionExecutor.RunWithVm(image, host, out InstructionExecutor executor, token);
        vm = new EcxInterpreter(executor);
        return code;
    }
}