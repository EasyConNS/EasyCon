using EasyCon.Script.Bytecode;
using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;

namespace EasyCon.Script.Binding;

public static class BuiltinFunctions
{
    // --- 保留内置（注册 callable + root scope）---

    public static readonly FunctionSymbol Wait = new("WAIT", [new("duration", ScriptType.Int, hasDefault: true, defaultValue: 50)], ScriptType.Void);
    public static readonly FunctionSymbol Print = new("PRINT", [new("message", ScriptType.String, hasDefault: true, defaultValue: "")], ScriptType.Void);
    public static readonly FunctionSymbol Alert = new("ALERT", [new("message", ScriptType.String)], ScriptType.Void);
    public static readonly FunctionSymbol Rand = new("RAND", [new("max", ScriptType.Int, hasDefault: true, defaultValue: 100)], ScriptType.Int);
    public static readonly FunctionSymbol Amiibo = new("AMIIBO", [new("index", ScriptType.Int)], ScriptType.Void);
    public static readonly FunctionSymbol Beep = new("BEEP", [new("freq", ScriptType.Int), new("duration", ScriptType.Int)], ScriptType.Void);
    public static readonly FunctionSymbol Env = new("ENV", [new("name", ScriptType.String)], ScriptType.String);
    public static readonly FunctionSymbol StrEncode = new("ENCODE",
        [new("array", ScriptType.ArrayOf(ScriptType.Byte)), new("type", ScriptType.String, hasDefault: true, defaultValue: "utf8")],
        ScriptType.String);
    public static readonly FunctionSymbol Jq = new("JQ",
        [new("json", ScriptType.String), new("query", ScriptType.String)],
        ScriptType.String);

    // --- 文件 IO（低级句柄） ---
    public static readonly FunctionSymbol FOpen = new("FOPEN", [new("path", ScriptType.String), new("mode", ScriptType.String)], ScriptType.Ptr);
    public static readonly FunctionSymbol FRead = new("FREAD", [new("handle", ScriptType.Ptr), new("count", ScriptType.Int)], ScriptType.String);
    public static readonly FunctionSymbol FWrite = new("FWRITE", [new("handle", ScriptType.Ptr), new("data", ScriptType.String)], ScriptType.Int);
    public static readonly FunctionSymbol FClose = new("FCLOSE", [new("handle", ScriptType.Ptr)], ScriptType.Void);
    public static readonly FunctionSymbol FEof = new("FEOF", [new("handle", ScriptType.Ptr)], ScriptType.Bool);

    // --- 文件 IO（高级便捷） ---
    public static readonly FunctionSymbol ReadFile = new("READFILE", [new("path", ScriptType.String)], ScriptType.String);
    public static readonly FunctionSymbol WriteFile = new("WRITEFILE", [new("path", ScriptType.String), new("data", ScriptType.String)], ScriptType.Void);
    public static readonly FunctionSymbol AppendFile = new("APPENDFILE", [new("path", ScriptType.String), new("data", ScriptType.String)], ScriptType.Void);
    public static readonly FunctionSymbol FileExists = new("FILE_EXISTS", [new("path", ScriptType.String)], ScriptType.Bool);

    // --- 编译器内联伪函数（保留符号供 binder，不注册 callable）---

    public static readonly FunctionSymbol Append = new("APPEND",
        [new("array", ScriptType.Any), new("value", ScriptType.Any)],
        ScriptType.Any);
    public static readonly FunctionSymbol Length = new("LEN",
        [new("var", ScriptType.Any)],
        ScriptType.Int);
    public static readonly FunctionSymbol StrConvert = new("STRING",
        [new("var", ScriptType.Any)],
        ScriptType.String);
    public static readonly FunctionSymbol IntConvert = new("INT",
        [new("var", ScriptType.Any)],
        ScriptType.Int);

    // --- 脚本参数 ---
    public static readonly FunctionSymbol Arg = new("ARG", [new("index", ScriptType.Int)], ScriptType.String);

    // --- OCR 置信度查询（普通 callable，注册到 root scope）---
    public static readonly FunctionSymbol OcrConf = new("OCR_CONF", [], ScriptType.Int);

    // --- ONNX 推理实验函数（L3 名表，宿主能力 IInference；Vision 特征位，P5）。
    //     纯标量协议：原生边界（EcxNativeContext）不携带数组，输出经 NET_OUT 逐元素读取。
    public static readonly FunctionSymbol NetLoad = new("NET_LOAD", [new("path", ScriptType.String)], ScriptType.Int);
    public static readonly FunctionSymbol NetRun = new("NET_RUN",
        [new("net", ScriptType.Int), new("input", ScriptType.Any)], ScriptType.Int);
    public static readonly FunctionSymbol NetOut = new("NET_OUT", [new("index", ScriptType.Int)], ScriptType.Double);

    // --- 采集卡打洞函数（lib-only scope 可见，不放入 root scope）---

    public static readonly FunctionSymbol CaptureHole = new("__CAPTURE__",
        [new("x", ScriptType.Int), new("y", ScriptType.Int), new("width", ScriptType.Int), new("height", ScriptType.Int)],
        ScriptType.String);
    public static readonly FunctionSymbol OcrHole = new("__OCR__",
        [new("x", ScriptType.Int), new("y", ScriptType.Int), new("width", ScriptType.Int), new("height", ScriptType.Int), new("lang", ScriptType.String)],
        ScriptType.String);
    public static readonly FunctionSymbol RoiHole = new("__ROI__",
        [new("image", ScriptType.String), new("x", ScriptType.Int), new("y", ScriptType.Int), new("width", ScriptType.Int), new("height", ScriptType.Int)],
        ScriptType.String);
    public static readonly FunctionSymbol OcrInitHole = new("__OCR_INIT__",
        [new("lang", ScriptType.String), new("dataPath", ScriptType.String), new("engineMode", ScriptType.String), new("psmode", ScriptType.String)],
        ScriptType.Bool);

    // --- 路由分类（内置函数实现的单一事实源，docs/VM2.md §7） ---

    /// <summary>内置函数的实现路由。</summary>
    public enum BuiltinRoute
    {
        /// <summary>SSA 内联：编译为域指令/纯指令，无宿主调用（WAIT/RAND/LEN/APPEND/STRING/INT/@采集洞）。</summary>
        Intrinsic,
        /// <summary>L2 平台 syscall：CallN 编号直传（EcsSyscall），语义在宿主参考实现
        /// （C# EcxHost.ReferenceSyscall / C ecs_main 桩）。</summary>
        Syscall,
        /// <summary>L3 名表原生：CallN 按名分发，语义在宿主 Native（BuiltinMap：ENCODE/JQ）。</summary>
        NativeName,
        /// <summary>stdlib ECS 源码函数：符号仅供 binder，实现在 StdLib 源码（PRINT→FWRITE、TIME→__TIME__）。</summary>
        StdlibSource,
    }

    /// <summary>
    /// 内置函数能力清单条目（VM2.md §7 路由总表 + §9.1 特征位 + MCU 可用性的单一事实源）。
    /// binder/encoder 消费 <see cref="Routes"/>，链接器特征扫描消费 <see cref="FeatureBit"/>。
    /// </summary>
    /// <param name="FeatureBit">镜像特征位需求（EcsImageFeatures；0 = 无）。
    /// CAPTURE/IL 为结构位（链接器按入口可达性/NeedIL 计算），不经本清单。</param>
    /// <param name="McuAvailable">C 参考桩（ecs_main.c h_syscall / native=NULL）能否执行该函数；
    /// 仅作 ABI 文档与差异对照，不参与桌面行为。</param>
    public sealed record BuiltinDescriptor(
        FunctionSymbol Symbol,
        BuiltinRoute Route,
        uint FeatureBit = 0,
        bool McuAvailable = false);

    /// <summary>
    /// 能力清单。新增内置函数在此登记一行（路由 + 特征位 + MCU 标记）。
    /// </summary>
    public static readonly IReadOnlyList<BuiltinDescriptor> Manifest =
    [
        // L1 内联（单片机域指令直出）
        new(Wait, BuiltinRoute.Intrinsic, McuAvailable: true),
        new(Rand, BuiltinRoute.Intrinsic, McuAvailable: true),
        new(Append, BuiltinRoute.Intrinsic, McuAvailable: true),
        new(Length, BuiltinRoute.Intrinsic, McuAvailable: true),
        new(StrConvert, BuiltinRoute.Intrinsic, McuAvailable: true),
        new(IntConvert, BuiltinRoute.Intrinsic, McuAvailable: true),
        new(CaptureHole, BuiltinRoute.Intrinsic),
        new(OcrHole, BuiltinRoute.Intrinsic),
        new(RoiHole, BuiltinRoute.Intrinsic),
        new(OcrInitHole, BuiltinRoute.Intrinsic),
        // L2 平台 syscall（McuAvailable = ecs_main.c h_syscall 实测语义：
        // FWRITE 行断协议 / FREAD 空串 / ALERT-BEEP-AMIIBO no-op / ARG-ENV 可用 /
        // APP-TIME 恒默认 / OCR_CONF 与文件族其余未实现 → ERR）
        new(Alert, BuiltinRoute.Syscall, McuAvailable: true),
        new(Arg, BuiltinRoute.Syscall, McuAvailable: true),
        new(Env, BuiltinRoute.Syscall, McuAvailable: true),
        new(Amiibo, BuiltinRoute.Syscall, McuAvailable: true),
        new(Beep, BuiltinRoute.Syscall, McuAvailable: true),
        new(OcrConf, BuiltinRoute.Syscall),
        new(FOpen, BuiltinRoute.Syscall, EcsImageFeatures.File),
        new(FRead, BuiltinRoute.Syscall, EcsImageFeatures.File, McuAvailable: true),
        new(FWrite, BuiltinRoute.Syscall, EcsImageFeatures.File, McuAvailable: true),
        new(FClose, BuiltinRoute.Syscall, EcsImageFeatures.File),
        new(FEof, BuiltinRoute.Syscall, EcsImageFeatures.File),
        new(ReadFile, BuiltinRoute.Syscall, EcsImageFeatures.File),
        new(WriteFile, BuiltinRoute.Syscall, EcsImageFeatures.File),
        new(AppendFile, BuiltinRoute.Syscall, EcsImageFeatures.File),
        new(FileExists, BuiltinRoute.Syscall, EcsImageFeatures.File),
        // L3 名表原生（MCU native=NULL → 不可用）
        new(StrEncode, BuiltinRoute.NativeName),
        new(Jq, BuiltinRoute.NativeName),
        new(NetLoad, BuiltinRoute.NativeName, EcsImageFeatures.Vision),
        new(NetRun, BuiltinRoute.NativeName, EcsImageFeatures.Vision),
        new(NetOut, BuiltinRoute.NativeName, EcsImageFeatures.Vision),
        // stdlib 源码（PRINT→FWRITE）
        new(Print, BuiltinRoute.StdlibSource, McuAvailable: true),
    ];

    /// <summary>符号 → 路由（由清单派生，binder/encoder 消费）。</summary>
    public static readonly IReadOnlyDictionary<FunctionSymbol, BuiltinRoute> Routes =
        Manifest.ToDictionary(d => d.Symbol, d => d.Route);

    public static BuiltinRoute RouteOf(FunctionSymbol fn) =>
        Routes.TryGetValue(fn, out var route) ? route : BuiltinRoute.NativeName;

    // --- 内联伪函数判断（由路由表派生） ---

    public static bool IsIntrinsic(FunctionSymbol fn) => RouteOf(fn) == BuiltinRoute.Intrinsic;

    // --- 采集卡能力追踪 ---

    private static readonly HashSet<FunctionSymbol> CaptureRequiringFunctions = [CaptureHole, OcrHole, RoiHole, OcrInitHole];
    public const string CapturePlaceholder = "__capture__";
    public static bool RequiresCapture(FunctionSymbol fn) => CaptureRequiringFunctions.Contains(fn);

    // --- 注册到 root scope 的函数列表 ---

    private static readonly FunctionSymbol[] All =
        [Wait, Alert, Rand, Amiibo, Beep, Env, Append, Length, StrEncode, StrConvert, IntConvert, Jq, Arg, OcrConf,
         FOpen, FRead, FWrite, FClose, FEof, ReadFile, WriteFile, AppendFile, FileExists, NetLoad, NetRun, NetOut];

    internal static IReadOnlyList<FunctionSymbol> GetAll() => All;

    public static bool IsBuiltin(FunctionSymbol fn) => All.Contains(fn);

    // --- 采集卡洞函数列表（注册到 lib-only scope + callable）---

    private static readonly FunctionSymbol[] CaptureHoles = [CaptureHole, OcrHole, RoiHole, OcrInitHole];
    internal static IReadOnlyList<FunctionSymbol> GetCaptureHoles() => CaptureHoles;
}