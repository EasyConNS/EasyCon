using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using System.Runtime.InteropServices;
using System.Text;

namespace EasyCon.Script.Bytecode;

/// <summary>
/// 模拟解释器宿主：与 C VM 的 ecs_host vtable 同构（docs/VmSemanticContract.md §四）。
/// </summary>
/// <summary>平台能力位（对齐 ecs_vm.h 的 ECS_CAP_*；能力注入语义见 docs/VmSemanticContract.md §四）。</summary>
[Flags]
public enum EcsCaps
{
    None = 0,
    Print = 1 << 0,
    Alert = 1 << 1,
    Beep = 1 << 2,
    File = 1 << 3,
    Capture = 1 << 4,
    Ffi = 1 << 5,
    Stdin = 1 << 6,
    AllDesktop = Print | Alert | Beep | File | Capture | Ffi | Stdin,
    AllMcu = None,          // 单片机：全部忽略，脚本副作用仅按键/摇杆/延时
}

/// <summary>
/// 宿主原生的堆上下文：解释器内部堆（字符串/数组句柄）的受控出口。
/// CallN 原生回调经此读取字符串/复合实参、回写字符串结果——
/// 镜像里的句柄值离开解释器堆没有意义，宿主不得自行解释。
/// </summary>
public abstract class EcxNativeContext
{
    /// <summary>字符串解引用（非字符串句柄返回空串）。</summary>
    public abstract string Str(TaggedValue v);

    /// <summary>分配字符串，返回可回传给镜像的句柄值。</summary>
    public abstract TaggedValue Str(string s);

    /// <summary>完整转换为 Value（字符串解引用、数组逐元素复制；结构体返回 Void）。</summary>
    public abstract Value ToValue(TaggedValue v);

    /// <summary>Value → TaggedValue（标量/字符串；数组/结构体不支持，返回 Void）。</summary>
    public abstract TaggedValue FromValue(Value v);
}

public sealed class EcxHost
{
    public EcsCaps Caps { get; set; } = EcsCaps.AllDesktop;
    /// <summary>S-14 行断协议状态（FWRITE/ALERT 尾部 '\' 挂起下一行换行；宿主所有）。</summary>
    public bool PendingBreak;
    public readonly List<string> WaitLog = new();
    public readonly List<string> KeyLog = new();
    public readonly List<string> KeyStateLog = new();
    public readonly List<string> StickSetLog = new();
    public readonly List<string> StickClickLog = new();
    public readonly List<string> AmiiboLog = new();
    public readonly List<string> BeepLog = new();
    public readonly List<string> Lines = new();
    public readonly List<string> Alerts = new();

    public Action<int> WaitMs { get; set; } = _ => { };
    public Action<int, int> Key { get; set; } = (_, _) => { };
    public Action<int, int> KeyState { get; set; } = (_, _) => { };
    public Action<int, int, int> StickSet { get; set; } = (_, _, _) => { };
    public Action<int, int, int, int> StickClick { get; set; } = (_, _, _, _) => { };
    public Func<string, int> ImgLabel { get; set; } = _ => -1;
    public Func<int, int> Rand { get; set; } = _ => 0;
    public Func<int> TimeMs { get; set; } = () => 0;
    public Action<int, int> Beep { get; set; } = (_, _) => { };
    /// <summary>S-13 AMIIBO：槽位选择回调（0–19 合法，越界宿主静默——参考实现已判）。</summary>
    public Action<int> Amiibo { get; set; } = _ => { };
    /// <summary>FWRITE stdout 行断协议的落点（message 已按协议剥尾、newline 已解析）。</summary>
    public Action<string, bool> Print { get; set; } = (_, _) => { };
    public Action<string> Alert { get; set; } = _ => { };
    public Func<string> ReadLine { get; set; } = () => "";
    /// <summary>
    /// 扩展原生（ENCODE/JQ/采集卡洞/FFI）；null 或返回 null = 未实现。
    /// 字符串实参/返回值经 <see cref="EcxNativeContext"/> 解引用/分配——镜像句柄离开解释器堆无意义。
    /// </summary>
    public Func<string, TaggedValue[], EcxNativeContext, TaggedValue?>? Native { get; set; }
    /// <summary>
    /// L2 平台 syscall 处理器（编号语义，docs/VM2.md §9.1）；缺省为桌面参考实现
    /// <see cref="ReferenceSyscall"/>（行断协议 + Caps 门控 + 文件族转发 Native）。返回 null = 未实现。
    /// </summary>
    public Func<int, TaggedValue[], EcxNativeContext, TaggedValue?>? Syscall { get; set; }
    /// <summary>
    /// S-21 strict_caps（默认 off）：off = 能力缺失按 <see cref="EcsCapabilityDefaults"/> 缺省值表
    /// 降级（feats 仅元信息）；on = 恢复响亮行为（L2/L3 miss 即 ERR_NOSUCHNATIVE，
    /// 图像标签 ERR_IL）——迁移保底、诊断模式、「同宿主能力集」对拍严格档。
    /// </summary>
    public bool StrictCaps { get; set; }
    public string[] Args { get; set; } = [];
    public string AppDir { get; set; } = "";

    public EcxHost()
    {
        Syscall = ReferenceSyscall;   // 桌面参考实现；专用宿主可整体替换
    }

    /// <summary>
    /// L2 平台 syscall 桌面参考实现（编号语义的唯一 C# 权威，与 C harness 桩逐字对齐；
    /// 由 VM 核 <c>CallNative/CallSyscall</c> 平移而来）：
    /// FWRITE/FREAD 行断协议 + Caps 门控；文件实现转发 <see cref="Native"/>（规范名，BuiltinMap）。
    /// 返回 null = 未实现（调用方报 ERR_NOSUCHNATIVE）。
    /// </summary>
    private TaggedValue? ReferenceSyscall(int id, TaggedValue[] args, EcxNativeContext ctx)
    {
        switch (id)
        {
            case EcsSyscall.FWrite:
                return FWriteReference(args[0].I64, args, ctx);
            case EcsSyscall.FRead:
                if (args[0].I64 == 0)
                    return Caps.HasFlag(EcsCaps.Stdin) ? ctx.Str(ReadLine()) : ctx.Str("");
                return Caps.HasFlag(EcsCaps.File)
                    ? Native?.Invoke(EcsSyscall.Names[id], args, ctx)
                    : ctx.Str("");
            case EcsSyscall.Alert:
                if (Caps.HasFlag(EcsCaps.Alert))
                {
                    // 行断协议对齐（与 FWRITE stdout / 金标准 ImplAlert 一致）：剥尾 `\` 续行
                    var alert = ctx.Str(args[0]);
                    Alert(alert.EndsWith('\\') ? alert[..^1] : alert);
                }
                return TaggedValue.Void;   // 无 Alert 能力：静默忽略
            case EcsSyscall.Arg:
                {
                    var i = args[0].I32;
                    return ctx.Str(i >= 0 && i < Args.Length ? Args[i] : "");
                }
            case EcsSyscall.Env:
                return ctx.Str(Environment.GetEnvironmentVariable(ctx.Str(args[0])) ?? "");
            case EcsSyscall.App:
                return ctx.Str(AppDir);
            case EcsSyscall.Time:
                return TaggedValue.FromInt(TimeMs());
            case EcsSyscall.OcrConf:
                return Native?.Invoke(EcsSyscall.Names[id], args, ctx) ?? TaggedValue.FromInt(0);
            case EcsSyscall.Beep:
                if (Caps.HasFlag(EcsCaps.Beep))
                    Beep(args[0].I32, args[1].I32);
                return TaggedValue.Void;   // 无 Beep 能力：静默忽略
            case EcsSyscall.Amiibo:
                if (args[0].I32 is >= 0 and <= 19)   // S-13：槽位 0–19，越界静默（宿主内判）
                    Amiibo(args[0].I32);
                return TaggedValue.Void;
            default:
                return null;   // 未知编号 → ERR_NOSUCHNATIVE
        }
    }

    /// <summary>FWRITE：句柄 1=stdout 行断协议；0=no-op 返 len；2=print(newline)；>2=文件族转发。</summary>
    private TaggedValue? FWriteReference(long handle, TaggedValue[] args, EcxNativeContext ctx)
    {
        var s = ctx.Str(args[1]);
        if (handle == 1)
        {
            if (Caps.HasFlag(EcsCaps.Print))
            {
                var output = s.EndsWith('\\') ? s[..^1] : s;
                Print(output, !PendingBreak);
                PendingBreak = s.EndsWith('\\');
            }
            return TaggedValue.FromInt(s.Length);
        }
        if (handle == 0)
            return TaggedValue.FromInt(s.Length);
        if (handle == 2)
        {
            if (Caps.HasFlag(EcsCaps.Print))
                Print(s, true);
            return TaggedValue.FromInt(s.Length);
        }
        return Caps.HasFlag(EcsCaps.File)
            ? Native?.Invoke(EcsSyscall.Names[EcsSyscall.FWrite], args, ctx)
            : TaggedValue.FromInt(-1);
    }

    /// <summary>开启事件与输出记录（默认委托 → *Log 列表，格式与金标准 mock 一致）。</summary>
    public void EnableRecording()
    {
        var buf = new StringBuilder();
        WaitMs = ms => WaitLog.Add($"WAIT {ms}");
        Key = (k, d) => KeyLog.Add($"KEY {k} {d}");
        KeyState = (k, d) => KeyStateLog.Add($"KEYST {k} {d}");
        StickSet = (s, x, y) => StickSetLog.Add($"STICK {s} {x} {y}");
        StickClick = (s, x, y, d) => StickClickLog.Add($"STICKC {s} {x} {y} {d}");
        Amiibo = i => AmiiboLog.Add($"AMIIBO {i}");
        Beep = (f, d) => BeepLog.Add($"BEEP {f} {d}");
        Print = (s, nl) =>
        {
            buf.Append(s);
            if (nl)
            {
                Lines.Add(buf.ToString());
                buf.Clear();
            }
        };
        Alert = s => Alerts.Add(s);
    }
}

/// <summary>
/// ECX 模拟解释器：字节码语义的 C# 参考实现——语义契约见 docs/VmSemanticContract.md（S-01..S-18）。
/// 在 C 虚拟机落地前承担"模拟验证"；C VM 完成后作为交叉对比的第二方。
/// </summary>
public sealed class EcxInterpreter
{
    sealed class SimArray
    {
        public byte ElemTag;
        public List<TaggedValue> Items = new();
    }

    sealed class SimStruct
    {
        public EcsStructLayout Layout = null!;
        public TaggedValue[] Slots = null!;
        /// <summary>GetF 嵌套视图：本结构是父槽区切片的拷贝，写入须经 <see cref="WriteStructSlot"/>
        /// 回写父槽（对齐 v1 EcsStruct.GetNested 的原生内存写穿透语义）。</summary>
        public SimStruct? ViewParent;
        public int ViewOffset;
    }

    sealed class Frame
    {
        public EcsFunction Fn = null!;
        public int Pc;
        public TaggedValue[] Slots = null!;
        public int RetSlot = -1;
    }

    readonly EcxImage _image;
    readonly EcxHost _host;
    readonly List<object?> _heap = new();      // string / SimArray / SimStruct；索引+1 = 句柄
    readonly List<int> _refCounts = new();     // 与 _heap 平行：槽位对象引用计数（ecs_vm.c §4.2 rc / VM2.md §8.4）
    readonly List<int> _freeSlots = new();     // 已释放下标自由链（复用；句柄 = 列表下标，恒不移除/移动元素）
    readonly List<Frame> _frames = new();
    readonly List<Frame> _framePool = new();    // 弹帧回收池：Slots 容量只增，出租时 Array.Clear 复位
    readonly TaggedValue[] _globals;
    readonly EcxNativeContext _nativeCtx;
    CancellationToken _token;                   // 当前 Run 的取消令牌（Step 装载）
    int _steps;
    int _budget = 1_000_000;

    public int ErrorFunc { get; private set; } = -1;
    public int ErrorPc { get; private set; } = -1;

    // ---- 堆引用计数统计（internal 只读，测试/诊断观测）----
    internal int HeapLiveCount { get; private set; }        // 非空槽位数（存活对象）
    internal int HeapHighWater { get; private set; }        // 存活峰值
    internal long HeapAllocTotal { get; private set; }
    internal long HeapReleaseTotal { get; private set; }
    internal int HeapFreeListCount => _freeSlots.Count;

    /// <summary>计数自洽性：alloc − release = live = 非空槽位数（槽 0 为 null 哨兵），且无负计数。</summary>
    internal bool HeapCountsConsistent
    {
        get
        {
            if (HeapAllocTotal - HeapReleaseTotal != HeapLiveCount)
                return false;
            int live = 0;
            for (int i = 1; i < _heap.Count; i++)
                if (_heap[i] is not null)
                    live++;
            if (live != HeapLiveCount || live != _heap.Count - 1 - _freeSlots.Count)
                return false;
            foreach (var rc in _refCounts)
                if (rc < 0)
                    return false;
            return true;
        }
    }

    /// <summary>入口函数（&lt;main&gt;/$eval）的返回值（顶层 RETURN；无返回值为 Void）。</summary>
    public TaggedValue Result { get; private set; }

    public const int OK = 0;
    public const int YIELD = 1;
    public const int CANCELLED = 2;
    // 错误码 ABI 与 native/ecs_vm.h 结果码枚举一致（VmHeaderContractTests 锁定头文件侧）
    public const int ERR_IMAGE = 3;
    public const int ERR_OPCODE = 4;
    public const int ERR_SLOT = 5;
    public const int ERR_TYPE = 6;
    public const int ERR_INDEX = 7;
    public const int ERR_DIVZERO = 8;
    public const int ERR_DEPTH = 9;
    public const int ERR_NOSUCHNATIVE = 10;
    public const int ERR_HOST = 11;
    public const int ERR_OOM = 12;
    public const int ERR_IL = 13;
    public const int ERR_FEAT = 14;

    /// <summary>调用深度上限（与 C 侧 ECS_MAX_CALL_DEPTH 一致：深递归双端同样报 ERR_DEPTH，
    /// 避免同一镜像在 MCU 报错而 PC 端 OOM 的对拍分裂）。</summary>
    public const int MaxCallDepth = 512;

    EcxInterpreter(EcxImage image, EcxHost host)
    {
        _image = image;
        _host = host;
        _globals = new TaggedValue[image.Globals.Count];
        _heap.Add(null);    // 句柄 0 = null
        _refCounts.Add(0);  // 与 _heap 平行（哨兵槽位）
        _nativeCtx = new NativeContext(this);
    }

    public static int Run(EcxImage image, EcxHost host, CancellationToken token = default)
        => Run(image, host, out _, out _, out _, out _, token);

    /// <summary>执行并取回入口函数返回值（顶层 RETURN 语义，与金标准 SsaEvaluator.Evaluate 一致）。</summary>
    public static int Run(EcxImage image, EcxHost host, out Value result, CancellationToken token = default)
        => Run(image, host, out _, out _, out _, out result, token);

    /// <summary>执行镜像直到完成/取消/错误；出错时经 errorFunc/errorPc 报告现场。</summary>
    public static int Run(EcxImage image, EcxHost host, out int errorFunc, out int errorPc, CancellationToken token = default)
        => Run(image, host, out _, out errorFunc, out errorPc, out _, token);

    /// <summary>执行并返回解释器实例（堆引用计数统计观测用；独立命名避免与 out Value 重载二义）。</summary>
    public static int RunWithVm(EcxImage image, EcxHost host, out EcxInterpreter vm, CancellationToken token = default)
    {
        vm = new EcxInterpreter(image, host);
        int code;
        while (true)
        {
            code = vm.Step(token);
            if (code != YIELD)
                break;
        }
        return code;
    }

    /// <summary>执行镜像直到完成/取消/错误；返回错误码、入口返回值与出错现场（与 C VM 对齐）。</summary>
    public static int Run(EcxImage image, EcxHost host, out TaggedValue rawResult, out int errorFunc, out int errorPc, out Value result, CancellationToken token = default)
    {
        var vm = new EcxInterpreter(image, host);
        int code;
        while (true)
        {
            code = vm.Step(token);
            if (code != YIELD)
                break;
        }
        rawResult = vm.Result;
        result = vm.ToPublicValue(vm.Result);
        errorFunc = vm.ErrorFunc;
        errorPc = vm.ErrorPc;
        return code;
    }

    // ---- 堆 ----

    /// <summary>分配堆槽位：自由链优先复用下标（句柄 = 列表下标，释放 = 对象置 null + 下标入自由链，
    /// 恒不移除/移动元素）。出生引用 rc=1 归属接收槽（调用方以 StoreFresh 直写，不再 retain）。</summary>
    int Store(object obj)
    {
        HeapAllocTotal++;
        int h;
        if (_freeSlots.Count > 0)
        {
            h = _freeSlots[^1];
            _freeSlots.RemoveAt(_freeSlots.Count - 1);
            _heap[h] = obj;
            _refCounts[h] = 1;
        }
        else
        {
            h = _heap.Count;
            _heap.Add(obj);
            _refCounts.Add(1);
        }
        HeapLiveCount++;
        if (HeapLiveCount > HeapHighWater)
            HeapHighWater = HeapLiveCount;
        return h;
    }

    /// <summary>静态（pinned）常量串的数据源（S-20）：直读镜像常量池，零拷贝零分配（对齐 C 侧 str_or_null）。</summary>
    string? StaticStr(int kx) => kx >= 0 && kx < _image.Consts.Count ? _image.Consts[kx].Str ?? "" : null;

    string Str(int h) => h < 0 ? StaticStr(h & 0x7FFFFFFF) ?? "" : h > 0 && _heap[h] is string s ? s : throw new SimError(ERR_TYPE, "解引用非字符串句柄");
    string? StrOrNull(int h) => h < 0 ? StaticStr(h & 0x7FFFFFFF) : h > 0 && _heap[h] is string s ? s : null;
    SimArray Arr(int h) => _heap[h] as SimArray ?? throw new SimError(ERR_TYPE, "解引用非数组句柄");
    SimStruct St(int h) => _heap[h] as SimStruct ?? throw new SimError(ERR_TYPE, "解引用非结构体句柄");

    TaggedValue NewString(string s) => TaggedValue.FromStringHandle(Store(s));

    /// <summary>retain（对齐 ecs_vm.c）：句柄值引用 +1；null 哨兵/已释放槽静默容忍（Str/Arr/St 守卫同源）。</summary>
    void Retain(in TaggedValue v)
    {
        if (!v.IsHandle)
            return;
        var h = v.Handle;
        if (h > 0 && h < _heap.Count && _heap[h] is not null)
            _refCounts[h]++;
    }

    /// <summary>release：计数归零时递归释放子引用，随后对象置 null 并入自由链（下标可复用）。
    /// 重复释放已释放槽位为 no-op（对齐 C VM heap_obj NULL 守卫）。</summary>
    void Release(in TaggedValue v)
    {
        if (!v.IsHandle)
            return;
        ReleaseHandle(v.Handle);
    }

    void ReleaseHandle(int h)
    {
        if (h <= 0 || h >= _heap.Count)
            return;
        var obj = _heap[h];
        if (obj is null)
            return;
        if (--_refCounts[h] > 0)
            return;
        switch (obj)
        {
            case SimArray arr:
                for (int i = 0; i < arr.Items.Count; i++)
                    Release(arr.Items[i]);
                break;
            case SimStruct st:
                for (int i = 0; i < st.Slots.Length; i++)
                    Release(st.Slots[i]);
                // 槽位残留句柄清零：结构体可能经 ViewParent 被后死的视图回写，
                // 清零使回写路径的 release/retain 落在 Void 上（句柄复用后不误伤新对象）
                Array.Clear(st.Slots);
                break;
        }
        _heap[h] = null;
        _refCounts[h] = 0;
        _freeSlots.Add(h);
        HeapLiveCount--;
        HeapReleaseTotal++;
    }

    /// <summary>【槽写规则】任何帧槽 / 容器元素 / 结构体槽的写入必须经过
    /// <see cref="MoveToSlot"/> / <see cref="StoreFresh"/> / <see cref="OverwriteItem"/> /
    /// <see cref="WriteStructSlot"/> 之一（裸写 = 跳过旧值释放，破坏 RC 双端锁步）。
    /// 唯一例外：全新分配、尚无旧值的槽区（如新帧实参槽）可直接写。</summary>
    /// <summary>槽位覆写（move_to 语义，对齐 ecs_vm.c）：旧值引用释放；新值为其他活跃槽位的借用 → retain 补引用。
    /// 同柄自赋值（槽位与新值同句柄）为 no-op：槽位已持有引用，先释放会悬垂。</summary>
    void MoveToSlot(ref TaggedValue slot, TaggedValue v)
    {
        if (v.IsHandle && slot.IsHandle && v.Handle == slot.Handle)
            return;
        Release(slot);
        slot = v;
        Retain(v);
    }

    /// <summary>槽位写入「出生引用」值（Store 新建对象 / DeepCopy 结果 / 标量）：仅释放旧值——
    /// 出生引用（Store 时 rc=1 / DeepCopy 的 retain）即本槽位的引用。</summary>
    void StoreFresh(ref TaggedValue slot, TaggedValue v)
    {
        Release(slot);
        slot = v;
    }

    /// <summary>容器元素覆写（SetI 语义，对齐 ecs_vm.c）：旧元素释放，写入值 retain（借用补引用）。</summary>
    void OverwriteItem(List<TaggedValue> items, int idx, TaggedValue v)
    {
        var old = items[idx];
        if (v.IsHandle && old.IsHandle && v.Handle == old.Handle)
            return;
        Release(old);
        Retain(v);
        items[idx] = v;
    }

    /// <summary>弹帧并释放被弹帧槽位持有的全部句柄（Ret/Ret0，对齐 ecs_vm.c 帧退出语义）；
    /// 帧体回收进池（出租时复位）。</summary>
    void PopFrame(Frame frame)
    {
        _frames.RemoveAt(_frames.Count - 1);
        var slots = frame.Slots;
        for (int i = 0; i < slots.Length; i++)
            Release(slots[i]);
        _framePool.Add(frame);
    }

    /// <summary>S-01 深拷贝：数组/结构体一层新容器（出生引用，子项 retain）；字符串共享（retain 补接收槽引用）。
    /// 返回值恰携带一个接收槽引用，接收方以 StoreFresh 直写槽位。</summary>
    TaggedValue DeepCopy(TaggedValue v)
    {
        if (!v.IsHandle)
            return v;
        switch (v.Tag)
        {
            case EcsTag.Array:
                {
                    var src = Arr(v.Handle);
                    var copy = new SimArray { ElemTag = src.ElemTag, Items = new List<TaggedValue>(src.Items.Count) };
                    for (int i = 0; i < src.Items.Count; i++)
                    {
                        copy.Items.Add(src.Items[i]);
                        Retain(src.Items[i]);
                    }
                    return TaggedValue.FromArrayHandle(Store(copy));
                }
            case EcsTag.Struct:
                {
                    var src = St(v.Handle);
                    var copy = new SimStruct { Layout = src.Layout, Slots = (TaggedValue[])src.Slots.Clone() };
                    for (int i = 0; i < copy.Slots.Length; i++)
                        Retain(copy.Slots[i]);
                    return TaggedValue.FromStructHandle(Store(copy));
                }
            default:
                Retain(v);   // 字符串共享
                return v;
        }
    }

    /// <summary>COW 开关（运行时常量；ECS_COW=0 关闭，供全量测试矩阵双态验证）。</summary>
    internal static bool CopyOnWrite { get; set; } = Environment.GetEnvironmentVariable("ECS_COW") != "0";

    /// <summary>COW（move-on-unique，Q1-A）：句柄唯一引用（rc==1，恒为出生临时槽持有）时共享移交，
    /// 跳过容器深拷贝；否则维持 S-17 深拷贝。可观察值语义逐字不变——变量/全局槽永不与另一
    /// 变量/全局共享容器（共享只发生在「出生临时槽 ↔ 接收槽」之间，临时槽对程序不可见）。</summary>
    TaggedValue DeepCopyCopyOnWrite(TaggedValue v)
    {
        if (CopyOnWrite && v.IsHandle)
        {
            var h = v.Handle;
            if (h > 0 && h < _heap.Count && _heap[h] is not null && _refCounts[h] == 1)
            {
                Retain(v);   // 接收槽补引用；出生临时槽的既持引用随其消亡释放
                return v;
            }
        }
        return DeepCopy(v);
    }

    // ---- TOSTR 两层上下文（S-07/S-08）----

    string ToStringTop(TaggedValue v)
    {
        switch (v.Tag)
        {
            case EcsTag.Bool: return v.I32 != 0 ? "true" : "false";
            case EcsTag.Byte:
            case EcsTag.Int:
            case EcsTag.UInt:
                return v.I32.ToString();
            case EcsTag.UInt64: return unchecked((ulong)v.I64).ToString();
            case EcsTag.Ptr: return v.I64.ToString();
            case EcsTag.Double: return v.F64.ToString();
            case EcsTag.String: return StrOrNull(v.Handle) ?? "";
            case EcsTag.Array:
            case EcsTag.Struct: return ToStringNested(v);
            default: return "";
        }
    }

    string ToStringNested(TaggedValue v)
    {
        switch (v.Tag)
        {
            case EcsTag.Array:
                {
                    var arr = Arr(v.Handle);
                    var sb = new StringBuilder("[");
                    for (int i = 0; i < arr.Items.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(ToStringNested(arr.Items[i]));
                    }
                    sb.Append(']');
                    return sb.ToString();
                }
            case EcsTag.Struct:
                return $"struct:{St(v.Handle).Layout.Name}";
            case EcsTag.Ptr:
                return $"0x{v.I64:X}";
            case EcsTag.Bool: return v.I32 != 0 ? "true" : "false";
            case EcsTag.UInt64: return unchecked((ulong)v.I64).ToString();
            case EcsTag.String: return StrOrNull(v.Handle) ?? "";
            case EcsTag.Double: return v.F64.ToString();
            case EcsTag.Void: return "void";
            default: return v.I32.ToString();
        }
    }

    // ---- 主循环 ----

    /// <summary>帧出租：池空新建；复用时槽位 Array.Clear 复位为 Void（StoreFresh「无旧值」直写契约），
    /// 容量不足则扩容（容量只增不缩，递归峰值即池上限——池随弹帧回收，天然 ≤ 峰值深度）。</summary>
    Frame RentFrame(int nslots)
    {
        if (_framePool.Count > 0)
        {
            var f = _framePool[^1];
            _framePool.RemoveAt(_framePool.Count - 1);
            if (f.Slots.Length < nslots)
                f.Slots = new TaggedValue[nslots];   // 新数组自带零值
            else
                Array.Clear(f.Slots);
            return f;
        }
        return new Frame { Slots = new TaggedValue[nslots] };
    }

    /// <summary>常量池串 → 静态（pinned）值（契约 S-20 / docs/ZeroAllocVm.md §2）：**不再新建堆对象**，
    /// 直接返回指向镜像常量池的句柄（bit31 置位）；不参与 RC，因此无需驻留表、无需补引用。
    /// 语义不变：串不可变、EqS/Cont/GetI/Len/Cat/TOSTR 一律按内容处理
    /// （<see cref="StrOrNull"/> / <see cref="StaticStr"/> 同时服务动态与静态句柄）。</summary>
    TaggedValue InternedString(int kx) => TaggedValue.FromStaticString(kx);

    int Step(CancellationToken token)
    {
        _token = token;
        if (_frames.Count == 0)
        {
            var fn = _image.Functions[_image.Entry];
            var f = RentFrame(fn.NSlots);
            f.Fn = fn;
            f.Pc = 0;
            f.RetSlot = -1;
            _frames.Add(f);
        }
        if (_token.IsCancellationRequested)
            return CANCELLED;

        // 预算计数局部化：字段读改写 → JIT 寄存器驻留（每指令省 2 次内存访问；
        // 预算值 Run 期内不变、_steps 仅本方法读写，回写点 = 切片边界）
        var steps = _steps;
        var budget = _budget;
        // 取指通路局部化（P0b）：frame/指令流 span/R/pc 提升为循环局部，JIT 可驻留寄存器，
        // 每指令省 4 次 _frames/frame 字段链解引用。不变量：指令流运行期只读（链接后不可变），
        // span 恒指向当前帧函数的指令列表；换帧点（Call/Ret/Ret0）置 frameSwitched 由循环头重载；
        // 切片出口（YIELD/宿主后 CANCELLED）写回 frame.Pc——Run 吸收 YIELD 后重入 Step 续跑。
        Frame frame = null!;
        ReadOnlySpan<EcsInstruction> code = default;
        TaggedValue[] R = null!;
        int pc = 0;
        bool frameSwitched = true;
        while (true)
        {
            if (frameSwitched)
            {
                frame = _frames[^1];
                code = CollectionsMarshal.AsSpan(frame.Fn.Instructions);
                R = frame.Slots;
                pc = frame.Pc;
                frameSwitched = false;
            }

            // 取消检查合并：每指令只做预算计数（1 分支）；token 在入口、预算边界与宿主调用后检查
            // （纯计算最坏 1M 步 ≈ 十几毫秒感知；等待/按键型脚本在宿主调用返回后立即感知）
            if (++steps >= budget)
            {
                frame.Pc = pc;
                _steps = 0;
                if (_token.IsCancellationRequested)
                    return CANCELLED;
                return YIELD;
            }

            int instructionPc = pc;
            // 单一分派臂（SingleStreamFormat §2）：解码形态是唯一指令表示，
            // 不再有 Code/PcCode 双流与宽窄两条取指路径。
            var ins = code[pc++];
            var op = ins.Op;
            int a = ins.A;
            int b = ins.B;
            int c = ins.C;
            uint ext = ins.Ext;

            try
            {
                switch (op)
                {
                    case EcsOpcode.Nop: break;

                    // ---- 常量与移动 ----
                    case EcsOpcode.LoadI:
                        StoreFresh(ref R[a], TaggedValue.FromInt(b));
                        break;
                    case EcsOpcode.LoadK:
                        {
                            int kx = b;
                            var k = _image.Consts[kx];
                            if (k.Tag == EcsTag.String)
                                MoveToSlot(ref R[a], InternedString(kx));   // 驻留串：借用 + retain，槽旧值照常释放
                            else
                                StoreFresh(ref R[a], k.Tag switch
                                {
                                    EcsTag.Double => TaggedValue.FromDouble(k.Float64),
                                    EcsTag.UInt64 => TaggedValue.FromUInt64(unchecked((ulong)k.Int64)),
                                    EcsTag.Ptr => TaggedValue.FromPtr(k.Int64),
                                    EcsTag.UInt => TaggedValue.FromUInt(unchecked((uint)k.Int64)),
                                    _ => TaggedValue.FromInt((int)k.Int64),
                                });
                            break;
                        }
                    case EcsOpcode.LoadBool:
                        StoreFresh(ref R[a], TaggedValue.FromBool(b != 0));
                        break;
                    case EcsOpcode.Move:
                        MoveToSlot(ref R[a], R[b]);
                        break;
                    case EcsOpcode.SetVar:
                        StoreFresh(ref R[a], DeepCopyCopyOnWrite(R[b]));
                        break;
                    case EcsOpcode.LoadG:
                        MoveToSlot(ref R[a], _globals[b]);
                        break;
                    case EcsOpcode.StoreG:
                        {
                            var nv = DeepCopyCopyOnWrite(R[a]);
                            int gx = b;
                            Release(_globals[gx]);
                            _globals[gx] = nv;
                            break;
                        }

                    case EcsOpcode.Jmp:
                        pc += ins.Jump;
                        break;
                    case EcsOpcode.Jpt:
                        pc += R[a].I32 != 0 ? ins.Jump : 0;
                        break;
                    case EcsOpcode.Jpf:
                        pc += R[a].I32 == 0 ? ins.Jump : 0;
                        break;

                    // ---- 比较跳转融合（P2′）：R[a] kind R[b] 成立 → 跳 Jump；否则落入后继 ----
                    case EcsOpcode.CmpJ:
                        {
                            bool taken;
                            switch (c / 6)
                            {
                                case 0: { int l = R[a].I32, r = R[b].I32; taken = (c % 6) switch { 0 => l == r, 1 => l != r, 2 => l < r, 3 => l <= r, 4 => l > r, _ => l >= r }; break; }
                                case 1: { uint l = (uint)R[a].I32, r = (uint)R[b].I32; taken = (c % 6) switch { 0 => l == r, 1 => l != r, 2 => l < r, 3 => l <= r, 4 => l > r, _ => l >= r }; break; }
                                case 2: { double l = R[a].F64, r = R[b].F64; taken = (c % 6) switch { 0 => l == r, 1 => l != r, 2 => l < r, 3 => l <= r, 4 => l > r, _ => l >= r }; break; }
                                default: { long l = R[a].I64, r = R[b].I64; taken = (c % 6) switch { 0 => l == r, 1 => l != r, 2 => l < r, 3 => l <= r, 4 => l > r, _ => l >= r }; break; }
                            }
                            if (taken)
                                pc += ins.Jump;
                            break;
                        }

                    // ---- FOR 快速路径：tmp=i+1 写入 R[c]；tmp>limit → 跳 Jump（出循环，$i 保持 ==upper）；否则落入后继 ----
                    case EcsOpcode.ForStep:
                        {
                            int next = R[a].I32 + 1;
                            StoreFresh(ref R[c], TaggedValue.FromInt(next));
                            if (next > R[b].I32)
                            {
                                int exitTarget = instructionPc + 1 + ins.Jump;
                                if ((uint)exitTarget >= (uint)code.Length)
                                    throw new SimError(ERR_OPCODE, $"ForStep 出口越界 {exitTarget}");
                                pc = exitTarget;
                            }
                            break;
                        }

                    // ---- 调用 ----
                    case EcsOpcode.Call:
                        {
                            uint target = ext;
                            var callee = _image.Functions[(int)target];
                            if (_frames.Count >= MaxCallDepth)
                                throw new SimError(ERR_DEPTH, $"调用深度超过上限 {MaxCallDepth}");
                            var nf = RentFrame(callee.NSlots);
                            nf.Fn = callee;
                            nf.RetSlot = c;   // NoSlot(-1)：无接收槽
                            nf.Pc = 0;
                            for (int i = 0; i < b; i++)
                                StoreFresh(ref nf.Slots[i], DeepCopyCopyOnWrite(R[a + i]));   // S-17 实参（COW：唯一引用移交；池出租槽已清零，StoreFresh 恒等价）
                            frame.Pc = pc;   // 调用者续跑点写回（Ret 时经重载读回）
                            _frames.Add(nf);
                            frameSwitched = true;
                            break;
                        }
                    case EcsOpcode.CallN:
                        {
                            uint target = ext;
                            if (b > 8)
                                throw new SimError(ERR_NOSUCHNATIVE, $"原生调用实参超过上限 8: {b}");   // 与 C 侧一致拒绝
                            var args = new TaggedValue[b];   // 实参按值快照：原生看不见帧槽
                            for (int i = 0; i < b; i++)
                                args[i] = R[a + i];
                            TaggedValue ret;
                            if ((target & EcsSyscall.CallFlag) != 0)
                            {
                                // L2：编号 syscall，语义在宿主参考实现（VM 核纯调度）
                                int id = (int)(target & 0x7FFFFFFFu);
                                var resolved = _host.Syscall?.Invoke(id, args, _nativeCtx);
                                if (resolved is null)
                                {
                                    // S-21：miss → 缺省值表降级；strict 或表外编号 → 响亮
                                    resolved = EcsCapabilityDefaults.GetSyscallDefault(id, args, _nativeCtx);
                                    if (resolved is null || _host.StrictCaps)
                                        throw new SimError(ERR_NOSUCHNATIVE, $"syscall 未实现: 编号 {id}");
                                }
                                ret = resolved.Value;
                            }
                            else
                            {
                                if (target >= (uint)_image.Natives.Count)
                                    throw new SimError(ERR_NOSUCHNATIVE, $"原生索引越界 {target}");
                                ret = HostNative(_image.Natives[(int)target].Name, args);   // L3 名表路径：FFI/采集洞/ENCODE/JQ
                            }
                            if (c >= 0)
                                StoreFresh(ref R[c], ret);   // NoSlot(-1)：无接收槽；原生返回值恒为新建对象/标量（EcxNativeContext 契约），出生引用即接收槽引用
                            if (_token.IsCancellationRequested)
                            {
                                frame.Pc = pc;
                                return CANCELLED;   // 宿主调用后即时取消（原生内含 PRINT/READ/WAIT 类长延迟）
                            }
                            break;
                        }
                    case EcsOpcode.Ret:
                        {
                            var val = R[a];
                            if (_frames.Count == 1)
                            {
                                Result = DeepCopyCopyOnWrite(val);   // 入口返回值（顶层 RETURN）：Result 持独立引用，跨弹帧存活
                                PopFrame(frame);
                                return OK;
                            }
                            TaggedValue copy = default;
                            var hasCopy = frame.RetSlot >= 0;
                            if (hasCopy)
                                copy = DeepCopyCopyOnWrite(val);   // S-17 返回（COW：唯一引用移交；先于弹帧：val 所属槽位将被释放）
                            int retSlot = frame.RetSlot;
                            PopFrame(frame);
                            if (hasCopy)
                                StoreFresh(ref _frames[^1].Slots[retSlot], copy);
                            frameSwitched = true;
                            break;
                        }
                    case EcsOpcode.Ret0:
                        PopFrame(frame);
                        if (_frames.Count == 0)
                            return OK;
                        frameSwitched = true;
                        break;

                    default:
                        if (ExecOther(op, a, b, c, ext, R))
                        {
                            frame.Pc = pc;
                            return CANCELLED;
                        }
                        break;
                }
            }
            catch (SimError e)
            {
                if (e.Code == OK)
                    return OK;   // Halt 正常停机：不留错误现场（与 C VM 对齐）
                ErrorFunc = _image.Functions.IndexOf(frame.Fn);
                ErrorPc = instructionPc;
                return e.Code;
            }
        }
    }

    /// <summary>非控制流/非调用的算术与数据指令。返回 true = 本指令调用了宿主且取消已请求，
    /// 调用方立即终止（等待/按键类长延迟宿主调用的取消感知点）。</summary>
    bool ExecOther(EcsOpcode op, int a, int b, int c, uint ext, TaggedValue[] R)
    {
        switch (op)
        {
            // ---- 算术（S-02/S-05）----
            case EcsOpcode.AddI: StoreFresh(ref R[a], TaggedValue.FromInt(R[b].I32 + R[c].I32)); break;
            case EcsOpcode.SubI: StoreFresh(ref R[a], TaggedValue.FromInt(R[b].I32 - R[c].I32)); break;
            case EcsOpcode.MulI: StoreFresh(ref R[a], TaggedValue.FromInt(R[b].I32 * R[c].I32)); break;
            case EcsOpcode.DivI: RequireDivisor(R[c]); StoreFresh(ref R[a], TaggedValue.FromInt(R[b].I32 / R[c].I32)); break;
            case EcsOpcode.ModI: RequireDivisor(R[c]); StoreFresh(ref R[a], TaggedValue.FromInt(R[b].I32 % R[c].I32)); break;
            case EcsOpcode.RDivI: RequireDivisor(R[c]); StoreFresh(ref R[a], TaggedValue.FromInt((R[b].I32 + R[c].I32 / 2) / R[c].I32)); break;

            case EcsOpcode.AddU: StoreFresh(ref R[a], TaggedValue.FromUInt(unchecked((uint)R[b].I32 + (uint)R[c].I32))); break;
            case EcsOpcode.SubU: StoreFresh(ref R[a], TaggedValue.FromUInt(unchecked((uint)R[b].I32 - (uint)R[c].I32))); break;
            case EcsOpcode.MulU: StoreFresh(ref R[a], TaggedValue.FromUInt(unchecked((uint)R[b].I32 * (uint)R[c].I32))); break;
            case EcsOpcode.DivU: RequireDivisor(R[c]); StoreFresh(ref R[a], TaggedValue.FromUInt((uint)R[b].I32 / (uint)R[c].I32)); break;
            case EcsOpcode.ModU: RequireDivisor(R[c]); StoreFresh(ref R[a], TaggedValue.FromUInt((uint)R[b].I32 % (uint)R[c].I32)); break;

            case EcsOpcode.AddL: StoreFresh(ref R[a], TaggedValue.FromUInt64(unchecked((ulong)R[b].I64 + (ulong)R[c].I64))); break;
            case EcsOpcode.SubL: StoreFresh(ref R[a], TaggedValue.FromUInt64(unchecked((ulong)R[b].I64 - (ulong)R[c].I64))); break;
            case EcsOpcode.MulL: StoreFresh(ref R[a], TaggedValue.FromUInt64(unchecked((ulong)R[b].I64 * (ulong)R[c].I64))); break;
            case EcsOpcode.DivL: RequireDivisor(R[c]); StoreFresh(ref R[a], TaggedValue.FromUInt64((ulong)R[b].I64 / (ulong)R[c].I64)); break;
            case EcsOpcode.ModL: RequireDivisor(R[c]); StoreFresh(ref R[a], TaggedValue.FromUInt64((ulong)R[b].I64 % (ulong)R[c].I64)); break;

            case EcsOpcode.AddD: StoreFresh(ref R[a], TaggedValue.FromDouble(R[b].F64 + R[c].F64)); break;
            case EcsOpcode.SubD: StoreFresh(ref R[a], TaggedValue.FromDouble(R[b].F64 - R[c].F64)); break;
            case EcsOpcode.MulD: StoreFresh(ref R[a], TaggedValue.FromDouble(R[b].F64 * R[c].F64)); break;
            case EcsOpcode.DivD: StoreFresh(ref R[a], TaggedValue.FromDouble(R[b].F64 / R[c].F64)); break;

            // ---- 位运算（S-03 掩码）----
            case EcsOpcode.BandI: StoreFresh(ref R[a], TaggedValue.FromInt(R[b].I32 & R[c].I32)); break;
            case EcsOpcode.BorI: StoreFresh(ref R[a], TaggedValue.FromInt(R[b].I32 | R[c].I32)); break;
            case EcsOpcode.BxorI: StoreFresh(ref R[a], TaggedValue.FromInt(R[b].I32 ^ R[c].I32)); break;
            case EcsOpcode.ShlI: StoreFresh(ref R[a], TaggedValue.FromInt(R[b].I32 << (R[c].I32 & 31))); break;
            case EcsOpcode.ShrI: StoreFresh(ref R[a], TaggedValue.FromInt(R[b].I32 >> (R[c].I32 & 31))); break;
            case EcsOpcode.BnotI: StoreFresh(ref R[a], TaggedValue.FromInt(~R[b].I32)); break;

            // ---- 比较 ----
            // 比较族直写（P0）：委托间接调用 + CompareTo 是热点路径纯开销；
            // 浮点直接用 C# 比较符（NaN → false），与 C VM 的 C 语义一致
            //（原 CmpD 的 CompareTo 把 NaN 排在最前，与 C VM 存在潜在分歧）
            case EcsOpcode.EqI: StoreFresh(ref R[a], TaggedValue.FromBool(R[b].I32 == R[c].I32)); break;
            case EcsOpcode.LtI: StoreFresh(ref R[a], TaggedValue.FromBool(R[b].I32 < R[c].I32)); break;
            case EcsOpcode.LeI: StoreFresh(ref R[a], TaggedValue.FromBool(R[b].I32 <= R[c].I32)); break;
            case EcsOpcode.GtI: StoreFresh(ref R[a], TaggedValue.FromBool(R[b].I32 > R[c].I32)); break;
            case EcsOpcode.GeI: StoreFresh(ref R[a], TaggedValue.FromBool(R[b].I32 >= R[c].I32)); break;
            case EcsOpcode.EqU: StoreFresh(ref R[a], TaggedValue.FromBool((uint)R[b].I32 == (uint)R[c].I32)); break;
            case EcsOpcode.LtU: StoreFresh(ref R[a], TaggedValue.FromBool((uint)R[b].I32 < (uint)R[c].I32)); break;
            case EcsOpcode.LeU: StoreFresh(ref R[a], TaggedValue.FromBool((uint)R[b].I32 <= (uint)R[c].I32)); break;
            case EcsOpcode.GtU: StoreFresh(ref R[a], TaggedValue.FromBool((uint)R[b].I32 > (uint)R[c].I32)); break;
            case EcsOpcode.GeU: StoreFresh(ref R[a], TaggedValue.FromBool((uint)R[b].I32 >= (uint)R[c].I32)); break;
            case EcsOpcode.EqD: StoreFresh(ref R[a], TaggedValue.FromBool(R[b].F64 == R[c].F64)); break;
            case EcsOpcode.LtD: StoreFresh(ref R[a], TaggedValue.FromBool(R[b].F64 < R[c].F64)); break;
            case EcsOpcode.LeD: StoreFresh(ref R[a], TaggedValue.FromBool(R[b].F64 <= R[c].F64)); break;
            case EcsOpcode.GtD: StoreFresh(ref R[a], TaggedValue.FromBool(R[b].F64 > R[c].F64)); break;
            case EcsOpcode.GeD: StoreFresh(ref R[a], TaggedValue.FromBool(R[b].F64 >= R[c].F64)); break;
            case EcsOpcode.EqL: StoreFresh(ref R[a], TaggedValue.FromBool(R[b].I64 == R[c].I64)); break;
            case EcsOpcode.LtL: StoreFresh(ref R[a], TaggedValue.FromBool(R[b].I64 < R[c].I64)); break;
            case EcsOpcode.LeL: StoreFresh(ref R[a], TaggedValue.FromBool(R[b].I64 <= R[c].I64)); break;
            case EcsOpcode.GtL: StoreFresh(ref R[a], TaggedValue.FromBool(R[b].I64 > R[c].I64)); break;
            case EcsOpcode.GeL: StoreFresh(ref R[a], TaggedValue.FromBool(R[b].I64 >= R[c].I64)); break;
            case EcsOpcode.EqS: StoreFresh(ref R[a], TaggedValue.FromBool(string.Equals(StrOrNull(R[b].Handle) ?? "", StrOrNull(R[c].Handle) ?? "", StringComparison.Ordinal))); break;
            case EcsOpcode.EqP: StoreFresh(ref R[a], TaggedValue.FromBool(R[b].I64 == R[c].I64)); break;

            case EcsOpcode.Not: StoreFresh(ref R[a], TaggedValue.FromBool(R[b].I32 == 0)); break;
            case EcsOpcode.NegI: StoreFresh(ref R[a], TaggedValue.FromInt(-R[b].I32)); break;
            case EcsOpcode.NegD: StoreFresh(ref R[a], TaggedValue.FromDouble(-R[b].F64)); break;
            case EcsOpcode.Conv: StoreFresh(ref R[a], Conv((EcsConvKind)c, R[b])); break;

            // ---- 数组/字符串 ----
            case EcsOpcode.NewArrV:
                {
                    var arr = new SimArray { ElemTag = (byte)ext };
                    for (int i = 0; i < b; i++)
                    {
                        arr.Items.Add(R[c + i]);
                        Retain(R[c + i]);   // 新容器持有元素引用
                    }
                    StoreFresh(ref R[a], TaggedValue.FromArrayHandle(Store(arr)));
                    break;
                }
            case EcsOpcode.NewArrE:   // ABx：元素类型码在 Bx
                StoreFresh(ref R[a], TaggedValue.FromArrayHandle(Store(new SimArray { ElemTag = (byte)b })));
                break;
            case EcsOpcode.GetI:
                {
                    var container = R[b];
                    var idx = R[c].I32;
                    if (container.IsString)
                    {
                        var s = StrOrNull(container.Handle) ?? "";
                        if (idx < 0 || idx >= s.Length) throw new SimError(ERR_INDEX, $"字符串索引越界 {idx}/{s.Length}");
                        StoreFresh(ref R[a], NewString(s[idx].ToString()));
                    }
                    else
                    {
                        var arr = Arr(container.Handle);
                        if (idx < 0 || idx >= arr.Items.Count) throw new SimError(ERR_INDEX, $"数组索引越界 {idx}/{arr.Items.Count}");
                        var item = CoerceRead(arr.ElemTag, arr.Items[idx]);
                        Retain(item);   // S-10 读共享：接收槽补引用
                        StoreFresh(ref R[a], item);
                    }
                    break;
                }
            case EcsOpcode.SetI:
                {
                    var arr = Arr(R[b].Handle);
                    var idx = R[c].I32;
                    if (idx < 0 || idx >= arr.Items.Count) throw new SimError(ERR_INDEX, $"数组索引越界 {idx}/{arr.Items.Count}");
                    OverwriteItem(arr.Items, idx, CoerceWrite(arr.ElemTag, R[a]));
                    break;
                }
            case EcsOpcode.Slice:
                {
                    var container = R[b];
                    int start = R[c].I32;
                    int end;
                    if (container.IsString)
                    {
                        var s = StrOrNull(container.Handle) ?? "";
                        end = ext == 0xFFFFFFFF ? s.Length : R[(int)ext].I32;
                        if (start > s.Length || end > s.Length || start > end) throw new SimError(ERR_INDEX, "切片越界");
                        StoreFresh(ref R[a], NewString(s[start..end]));
                    }
                    else
                    {
                        var arr = Arr(container.Handle);
                        end = ext == 0xFFFFFFFF ? arr.Items.Count : R[(int)ext].I32;
                        if (start > arr.Items.Count || end > arr.Items.Count || start > end) throw new SimError(ERR_INDEX, "切片越界");
                        var sliced = new SimArray { ElemTag = arr.ElemTag, Items = arr.Items.GetRange(start, end - start) };
                        for (int i = 0; i < sliced.Items.Count; i++)
                            Retain(sliced.Items[i]);   // 新容器持有元素引用
                        StoreFresh(ref R[a], TaggedValue.FromArrayHandle(Store(sliced)));
                    }
                    break;
                }
            case EcsOpcode.Cont:
                {
                    var item = R[b];
                    var container = R[c];
                    if (container.IsString)
                    {
                        var s = StrOrNull(container.Handle) ?? "";
                        var sub = StrOrNull(item.Handle) ?? throw new SimError(ERR_TYPE, "in 运算类型不匹配");
                        StoreFresh(ref R[a], TaggedValue.FromBool(s.Contains(sub, StringComparison.Ordinal)));
                    }
                    else if (container.IsArray)
                    {
                        var arr = Arr(container.Handle);
                        if (item.Tag != arr.ElemTag)
                        {
                            StoreFresh(ref R[a], TaggedValue.FromBool(false));   // S-09 元素类型不匹配 → false
                        }
                        else
                        {
                            bool found = false;
                            foreach (var it in arr.Items)
                                if (ValueEquals(it, item)) { found = true; break; }
                            StoreFresh(ref R[a], TaggedValue.FromBool(found));
                        }
                    }
                    else
                    {
                        throw new SimError(ERR_TYPE, "in 容器类型非法");
                    }
                    break;
                }
            case EcsOpcode.Append:
                {
                    var src = Arr(R[b].Handle);
                    var dst = new SimArray { ElemTag = src.ElemTag, Items = new List<TaggedValue>(src.Items) { R[c] } };
                    for (int i = 0; i < dst.Items.Count; i++)
                        Retain(dst.Items[i]);   // 新容器持有元素引用
                    StoreFresh(ref R[a], TaggedValue.FromArrayHandle(Store(dst)));
                    break;
                }
            case EcsOpcode.Cat:
                {
                    var l = R[b];
                    var r = R[c];
                    if (l.IsString || r.IsString)
                    {
                        StoreFresh(ref R[a], NewString(ToStringTop(l) + ToStringTop(r)));
                    }
                    else if (l.IsArray && r.IsArray)
                    {
                        var la = Arr(l.Handle);
                        var ra = Arr(r.Handle);
                        if (la.ElemTag != ra.ElemTag) throw new SimError(ERR_TYPE, "拼接元素类型不一致");
                        var merged = new SimArray { ElemTag = la.ElemTag, Items = new List<TaggedValue>(la.Items) };
                        merged.Items.AddRange(ra.Items);
                        for (int i = 0; i < merged.Items.Count; i++)
                            Retain(merged.Items[i]);   // 新容器持有元素引用
                        StoreFresh(ref R[a], TaggedValue.FromArrayHandle(Store(merged)));
                    }
                    else
                    {
                        throw new SimError(ERR_TYPE, "CAT 仅支持字符串/数组");
                    }
                    break;
                }
            case EcsOpcode.Len:
                {
                    var v = R[b];
                    if (v.IsString) StoreFresh(ref R[a], TaggedValue.FromInt((StrOrNull(v.Handle) ?? "").Length));
                    else if (v.IsArray) StoreFresh(ref R[a], TaggedValue.FromInt(Arr(v.Handle).Items.Count));
                    else throw new SimError(ERR_TYPE, "LEN 类型非法");
                    break;
                }

            // ---- 结构体 ----
            case EcsOpcode.NewSt:   // ABx：类型表索引在 Bx
                {
                    var layout = _image.Structs[b];
                    var st = new SimStruct { Layout = layout, Slots = new TaggedValue[layout.SlotCount] };
                    ZeroFill(layout, st.Slots);
                    StoreFresh(ref R[a], TaggedValue.FromStructHandle(Store(st)));
                    break;
                }
            case EcsOpcode.GetF:
                StoreFresh(ref R[a], GetField(R[b], c));   // GetField 返回值恒携带一个接收槽引用
                break;
            case EcsOpcode.PutF: PutField(R[b], c, R[a]); break;
            case EcsOpcode.GetFI:
                {
                    var st = St(R[b].Handle);
                    var f = st.Layout.Fields[c];
                    if (f.Kind != EcsFieldKind.FixedArray) throw new SimError(ERR_TYPE, "GETFI 仅支持固定数组字段");
                    var item = CoerceRead((byte)f.Type, st.Slots[f.SlotOffset + R[(int)ext].I32]);
                    Retain(item);   // 读共享：接收槽补引用（标量 no-op）
                    StoreFresh(ref R[a], item);
                    break;
                }
            case EcsOpcode.PutFI:
                {
                    var st = St(R[b].Handle);
                    var f = st.Layout.Fields[c];
                    if (f.Kind != EcsFieldKind.FixedArray) throw new SimError(ERR_TYPE, "PUTFI 仅支持固定数组字段");
                    WriteStructSlot(st, f.SlotOffset + R[(int)ext].I32, CoerceWrite((byte)f.Type, R[a]));
                    break;
                }

            // ---- 域操作（宿主调用：返回取消状态，取消感知点）----
            case EcsOpcode.WaitI: _host.WaitMs(unchecked((int)ext)); return _token.IsCancellationRequested;
            case EcsOpcode.WaitV: _host.WaitMs(R[a].I32); return _token.IsCancellationRequested;
            case EcsOpcode.KeyI: _host.Key(a, unchecked((int)ext)); return _token.IsCancellationRequested;
            case EcsOpcode.KeyV: _host.Key(a, R[b].I32); return _token.IsCancellationRequested;
            case EcsOpcode.KeySt: _host.KeyState(a, b); return _token.IsCancellationRequested;
            case EcsOpcode.StickSet: _host.StickSet(a, b, c); return _token.IsCancellationRequested;
            case EcsOpcode.StickP:
                {
                    _host.StickClick(a, b, c, unchecked((int)ext));
                    return _token.IsCancellationRequested;
                }
            case EcsOpcode.StickPv:
                {
                    _host.StickClick(a, (int)(ext & 0xFF), (int)((ext >> 16) & 0xFF), R[c].I32);
                    return _token.IsCancellationRequested;
                }
            case EcsOpcode.Img:   // ABx：标签名 = 常量池[Bx]（EcsOpcode.cs 注释为权威）
                {
                    if (_host.StrictCaps)
                        throw new SimError(ERR_IL, $"图像标签能力缺失（strict_caps）: {_image.Consts[b].Str}");
                    var name = _image.Consts[b].Str ?? "";
                    StoreFresh(ref R[a], TaggedValue.FromInt(_host.ImgLabel(name)));
                    return _token.IsCancellationRequested;
                }
            case EcsOpcode.Rand:
                {
                    var max = R[b].I32;
                    if (max < 0) throw new SimError(ERR_INDEX, "RAND 参数为负");
                    StoreFresh(ref R[a], TaggedValue.FromInt(max == 0 ? 0 : _host.Rand(max)));
                    return _token.IsCancellationRequested;
                }

            case EcsOpcode.Halt:
                throw new SimError(OK, "halt");
            default:
                throw new SimError(ERR_OPCODE, $"未实现操作码 {op}");
        }
        return false;
    }

    void ZeroFill(EcsStructLayout layout, TaggedValue[] slots)
    {
        foreach (var f in layout.Fields)
        {
            switch (f.Kind)
            {
                case EcsFieldKind.FixedArray:
                    for (int i = 0; i < f.Count; i++)
                        slots[f.SlotOffset + i] = ZeroOf(f.ElementType);
                    break;
                case EcsFieldKind.NestedStruct:
                    {
                        var nested = _image.Structs[f.NestedSid];
                        var tmp = new TaggedValue[nested.SlotCount];
                        ZeroFill(nested, tmp);
                        Array.Copy(tmp, 0, slots, f.SlotOffset, nested.SlotCount);
                        break;
                    }
                default:
                    slots[f.SlotOffset] = ZeroOf(f.Type);
                    break;
            }
        }
    }

    static TaggedValue ZeroOf(EcsTypeCode t) => t switch
    {
        EcsTypeCode.Double => TaggedValue.FromDouble(0),
        EcsTypeCode.UInt64 => TaggedValue.FromUInt64(0),
        EcsTypeCode.Ptr => TaggedValue.FromPtr(0),
        EcsTypeCode.String => TaggedValue.FromStringHandle(0),
        _ => TaggedValue.FromInt(0),
    };

    static TaggedValue CoerceRead(byte elemTag, TaggedValue v)
    {
        if (elemTag == EcsTag.Byte) return TaggedValue.FromByte((byte)v.I32);
        if (elemTag == EcsTag.Bool) return TaggedValue.FromBool(v.I32 != 0);
        return v;
    }

    static TaggedValue CoerceWrite(byte elemTag, TaggedValue v)
    {
        if (elemTag == EcsTag.Bool) return TaggedValue.FromBool(v.I32 != 0);
        return v;
    }

    TaggedValue GetField(TaggedValue obj, int fieldIdx)
    {
        var st = St(obj.Handle);
        var f = st.Layout.Fields[fieldIdx];
        switch (f.Kind)
        {
            case EcsFieldKind.Scalar:
                {
                    var v = CoerceRead((byte)f.Type, st.Slots[f.SlotOffset]);
                    Retain(v);   // 接收槽引用（标量 no-op；字符串字段共享 +1）
                    return v;
                }
            case EcsFieldKind.FixedArray:
                {
                    var arr = new SimArray { ElemTag = (byte)f.ElementType, Items = new List<TaggedValue>(st.Slots[f.SlotOffset..(f.SlotOffset + f.Count)]) };
                    for (int i = 0; i < arr.Items.Count; i++)
                        Retain(arr.Items[i]);   // 新容器持有元素引用
                    return TaggedValue.FromArrayHandle(Store(arr));
                }
            case EcsFieldKind.NestedStruct:
                {
                    var nested = _image.Structs[f.NestedSid];
                    // 嵌套视图：切片拷贝 + 回写父槽（写穿透，v1 EcsStruct.GetNested 语义）
                    var view = new SimStruct
                    {
                        Layout = nested,
                        Slots = st.Slots[f.SlotOffset..(f.SlotOffset + nested.SlotCount)].ToArray(),
                        ViewParent = st,
                        ViewOffset = f.SlotOffset,
                    };
                    for (int i = 0; i < view.Slots.Length; i++)
                        Retain(view.Slots[i]);   // 视图按普通堆对象计数：槽区是拷贝快照，视图持有自己的引用
                    return TaggedValue.FromStructHandle(Store(view));
                }
            default:
                throw new SimError(ERR_TYPE, "Boxed 字段访问不受支持");
        }
    }

    void PutField(TaggedValue obj, int fieldIdx, TaggedValue value)
    {
        var st = St(obj.Handle);
        var f = st.Layout.Fields[fieldIdx];
        switch (f.Kind)
        {
            case EcsFieldKind.Scalar:
                WriteStructSlot(st, f.SlotOffset, CoerceWrite((byte)f.Type, value));
                break;
            case EcsFieldKind.NestedStruct:
                {
                    var src = St(value.Handle);
                    // src 布局须匹配字段的嵌套类型（而非父结构体总槽数；与 GetF 取嵌套布局一致）
                    var nested = _image.Structs[f.NestedSid];
                    if (src.Slots.Length != nested.SlotCount)
                        throw new SimError(ERR_TYPE, "嵌套结构体布局不匹配");
                    for (int i = 0; i < src.Slots.Length; i++)
                        MoveToSlot(ref st.Slots[f.SlotOffset + i], src.Slots[i]);
                    PropagateToParent(st);
                    break;
                }
            default:
                throw new SimError(ERR_TYPE, "该字段种类不支持整体赋值");
        }
    }

    /// <summary>结构体槽写入；嵌套视图沿 ViewParent 链回写父槽区（写穿透，逐槽 release/retain 计数）。</summary>
    void WriteStructSlot(SimStruct st, int idx, TaggedValue v)
    {
        MoveToSlot(ref st.Slots[idx], v);
        PropagateToParent(st);
    }

    /// <summary>视图槽区整体回写父槽区（逐槽 move_to；同柄槽 no-op 快路径，多数槽无变化）。</summary>
    /// <summary>视图槽区整体回写父槽区（逐槽 move_to；同柄槽 no-op 快路径，多数槽无变化）。
    /// 目标偏移 = cur.ViewOffset（对齐改造前 Array.Copy 的写穿透位置）。</summary>
    void PropagateToParent(SimStruct view)
    {
        for (var cur = view; cur.ViewParent is { } parent; cur = parent)
        {
            var region = cur.Slots;
            var parentSlots = parent.Slots;
            for (int i = 0; i < region.Length; i++)
                MoveToSlot(ref parentSlots[cur.ViewOffset + i], region[i]);
        }
    }

    int LayoutSlotCount(SimStruct st) => st.Layout.SlotCount;

    TaggedValue Conv(EcsConvKind kind, TaggedValue v)
    {
        switch (kind)
        {
            case EcsConvKind.IntToDouble: return TaggedValue.FromDouble(v.I32);
            case EcsConvKind.DoubleToInt: return TaggedValue.FromInt(SaturateD2I(v.F64));
            case EcsConvKind.IntToUInt: return TaggedValue.FromUInt(unchecked((uint)v.I32));
            case EcsConvKind.UIntToInt: return TaggedValue.FromInt(v.I32);
            case EcsConvKind.IntToByte: return TaggedValue.FromByte((byte)v.I32);
            case EcsConvKind.BoolToInt: return TaggedValue.FromInt(v.I32);
            case EcsConvKind.IntToUInt64: return TaggedValue.FromUInt64(unchecked((ulong)(long)v.I32));
            case EcsConvKind.UIntToUInt64: return TaggedValue.FromUInt64(unchecked((uint)v.I32));
            case EcsConvKind.UInt64ToInt: return TaggedValue.FromInt(unchecked((int)v.I64));
            case EcsConvKind.IntToPtr: return TaggedValue.FromPtr(v.I32);
            case EcsConvKind.PtrToInt: return TaggedValue.FromInt(unchecked((int)v.I64));
            case EcsConvKind.UInt64ToPtr: return TaggedValue.FromPtr(v.I64);
            case EcsConvKind.PtrToUInt64: return TaggedValue.FromUInt64(unchecked((ulong)v.I64));
            case EcsConvKind.DoubleToUInt64: return TaggedValue.FromUInt64(unchecked((ulong)SaturateD2L(v.F64)));
            case EcsConvKind.UInt64ToDouble: return TaggedValue.FromDouble(unchecked((double)(ulong)v.I64));
            case EcsConvKind.ToStr: return NewString(ToStringTop(v));
            case EcsConvKind.ToInt:
                if (v.Tag is EcsTag.Bool or EcsTag.Byte or EcsTag.Int or EcsTag.UInt)
                    return TaggedValue.FromInt(v.I32);
                if (v.Tag == EcsTag.Double)
                    return TaggedValue.FromInt(SaturateD2I(v.F64));
                if (v.Tag == EcsTag.String)
                    return TaggedValue.FromInt(EcsConvText.ParseIntLiteral(StrOrNull(v.Handle)));   // PC 端：数字字符串解析，失败 0
                throw new SimError(ERR_TYPE, "无法转换为 int");
            default:
                throw new SimError(ERR_TYPE, $"未知转换 {kind}");
        }
    }

    static int SaturateD2I(double d)
    {
        if (double.IsNaN(d)) return 0;
        if (d >= int.MaxValue) return int.MaxValue;
        if (d <= int.MinValue) return int.MinValue;
        return (int)d;
    }

    static long SaturateD2L(double d)
    {
        if (double.IsNaN(d)) return 0;
        if (d >= long.MaxValue) return long.MaxValue;
        if (d <= long.MinValue) return long.MinValue;
        return (long)d;
    }

    static void RequireDivisor(TaggedValue v)
    {
        if (v.I32 == 0 && v.Tag != EcsTag.Double)
            throw new SimError(ERR_DIVZERO, "整数除零");
    }

    bool ValueEquals(TaggedValue x, TaggedValue y)
    {
        if (x.Tag != y.Tag)
            return false;
        if (x.IsString)
        {
            var sx = StrOrNull(x.Handle);
            var sy = StrOrNull(y.Handle);
            return string.Equals(sx, sy, StringComparison.Ordinal);
        }
        if (x.Tag == EcsTag.Double)
            return x.F64 == y.F64;
        return x.I64 == y.I64;
    }

    TaggedValue HostNative(string name, TaggedValue[] args)
    {
        // S-21：miss（无处理器/未实现名）→ strict 恢复响亮；非 strict 查缺省值表（表外未知名仍响亮）
        if (_host.Native is { } native && native(name, args, _nativeCtx) is { } ret)
            return ret;
        if (_host.StrictCaps)
            throw new SimError(ERR_NOSUCHNATIVE, $"原生函数未实现: {name}");
        return EcsCapabilityDefaults.GetNativeDefault(name, _nativeCtx)
            ?? throw new SimError(ERR_NOSUCHNATIVE, $"原生函数未注册: {name}");
    }

    /// <summary>堆上下文实现：宿主原生回调读写解释器堆的唯一通道。</summary>
    sealed class NativeContext(EcxInterpreter vm) : EcxNativeContext
    {
        public override string Str(TaggedValue v) => v.IsString ? vm.StrOrNull(v.Handle) ?? "" : "";

        public override TaggedValue Str(string s) => vm.NewString(s);

        public override Value ToValue(TaggedValue v)
        {
            if (v.IsString)
                return Value.FromString(vm.StrOrNull(v.Handle) ?? "");
            if (v.IsArray)
            {
                var arr = vm.Arr(v.Handle);
                var items = new List<Value>(arr.Items.Count);
                foreach (var item in arr.Items)
                    items.Add(ToValue(item));
                var elemType = ElemScriptType(arr.ElemTag);
                return Value.FromArray(ScriptArray.Create(elemType, items), elemType);
            }
            return v.ToValue();
        }

        public override TaggedValue FromValue(Value v)
        {
            var t = v.Type;
            if (t.Equals(ScriptType.String))
                return vm.NewString(v.AsString());
            return TaggedValue.FromValue(v);   // 标量；数组/结构体原生不返回
        }

        static ScriptType ElemScriptType(byte tag) => tag switch
        {
            EcsTag.Bool => ScriptType.Bool,
            EcsTag.Byte => ScriptType.Byte,
            EcsTag.UInt => ScriptType.UInt,
            EcsTag.Double => ScriptType.UInt64,
            EcsTag.String => ScriptType.String,
            EcsTag.Ptr => ScriptType.Ptr,
            _ => ScriptType.Int,
        };
    }

    /// <summary>入口返回值转公共 Value：字符串解引用、数组转 ScriptArray、结构体重建为
    /// EcsStruct（堆在返回前仍存活）。堆外无句柄语义，转换必须在此完成。</summary>
    Value ToPublicValue(TaggedValue v) => ToPublicValue(v, new Dictionary<SimStruct, EcsStruct>());

    Value ToPublicValue(TaggedValue v, Dictionary<SimStruct, EcsStruct> structs)
    {
        if (v.IsString)
            return Value.FromString(StrOrNull(v.Handle) ?? "");
        if (v.IsArray)
        {
            var arr = Arr(v.Handle);
            var items = new List<Value>(arr.Items.Count);
            foreach (var item in arr.Items)
                items.Add(ToPublicValue(item, structs));
            var elemType = ElemScriptTypeOf(arr.ElemTag);
            return Value.FromArray(ScriptArray.Create(elemType, items, new SimpleStringStore()), elemType);
        }
        if (v.IsStruct)
            return Value.FromStruct(PublicStruct(St(v.Handle), structs));
        return v.ToValue();
    }

    static ScriptType ElemScriptTypeOf(byte tag) => tag switch
    {
        EcsTag.Bool => ScriptType.Bool,
        EcsTag.Byte => ScriptType.Byte,
        EcsTag.UInt => ScriptType.UInt,
        EcsTag.Double => ScriptType.Double,
        EcsTag.String => ScriptType.String,
        EcsTag.Ptr => ScriptType.Ptr,
        _ => ScriptType.Int,
    };

    /// <summary>SimStruct → EcsStruct（按镜像类型表重建定义；句柄槽逐字段展开）。</summary>
    EcsStruct PublicStruct(SimStruct st, Dictionary<SimStruct, EcsStruct> structs)
    {
        if (structs.TryGetValue(st, out var cached))
            return cached;
        var def = PublicStructDef(st.Layout, []);
        var es = new EcsStruct(def);
        structs[st] = es;
        for (int i = 0; i < st.Layout.Fields.Length; i++)
        {
            var f = st.Layout.Fields[i];
            var defField = def.Fields[i];
            switch (f.Kind)
            {
                case EcsFieldKind.FixedArray:
                    for (int e = 0; e < f.Count; e++)
                        es.SetFieldElement(defField, e, PublicRaw(st.Slots[f.SlotOffset + e], TypeLayout.GetElementType(defField.FieldType)));
                    break;
                case EcsFieldKind.NestedStruct:
                    {
                        var nestedLayout = _image.Structs[f.NestedSid];
                        var nested = new SimStruct
                        {
                            Layout = nestedLayout,
                            Slots = st.Slots[f.SlotOffset..(f.SlotOffset + nestedLayout.SlotCount)].ToArray(),
                        };
                        es.SetField(defField, PublicStruct(nested, structs));
                        break;
                    }
                default:
                    es.SetField(defField, PublicRaw(st.Slots[f.SlotOffset], defField.FieldType));
                    break;
            }
        }
        return es;
    }

    EcsStructDef PublicStructDef(EcsStructLayout layout, Dictionary<EcsStructLayout, EcsStructDef> defs)
    {
        if (defs.TryGetValue(layout, out var cached))
            return cached;
        var def = new EcsStructDef { Name = layout.Name };
        defs[layout] = def;
        foreach (var f in layout.Fields)
        {
            var fieldType = f.Kind switch
            {
                EcsFieldKind.FixedArray => new ArrayType(ElemScriptTypeOf((byte)f.ElementType), f.Count),
                EcsFieldKind.NestedStruct => new StructType(PublicStructDef(_image.Structs[f.NestedSid], defs)),
                _ => ElemScriptTypeOf((byte)f.Type),
            };
            def.Fields.Add(new EcsFieldDef { Name = f.Name, FieldType = fieldType });
        }
        StructLayout.Calculate(def);
        return def;
    }

    object PublicRaw(TaggedValue v, ScriptType type) => type switch
    {
        _ when type.Equals(ScriptType.Bool) => v.I32 != 0,
        _ when type.Equals(ScriptType.Byte) => (byte)v.I32,
        _ when type.Equals(ScriptType.UInt) => unchecked((uint)v.I32),
        _ when type.Equals(ScriptType.UInt64) => unchecked((ulong)v.I64),
        _ when type.Equals(ScriptType.Ptr) => new IntPtr(v.I64),
        _ when type.Equals(ScriptType.Double) => v.F64,
        _ when type.Equals(ScriptType.String) => StrOrNullPub(v),
        _ => v.I32,
    };

    string? StrOrNullPub(TaggedValue v) => v.IsString ? StrOrNull(v.Handle) : null;


    sealed class SimError : Exception
    {
        public readonly int Code;
        public SimError(int code, string message) : base(message) { Code = code; }
    }
}