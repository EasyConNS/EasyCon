using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;

namespace EasyCon.Script.Binding;

internal static class BuiltinFunctions
{
    // 内置 Pixel struct
    internal static readonly EcsStructDef PixelStructDef = CreatePixelDef();
    private static EcsStructDef CreatePixelDef()
    {
        var def = new EcsStructDef { Name = "Pixel" };
        def.Fields.Add(new EcsFieldDef { Name = "R", FieldType = ScriptType.Int });
        def.Fields.Add(new EcsFieldDef { Name = "G", FieldType = ScriptType.Int });
        def.Fields.Add(new EcsFieldDef { Name = "B", FieldType = ScriptType.Int });
        def.Fields.Add(new EcsFieldDef { Name = "A", FieldType = ScriptType.Int });
        StructLayout.Calculate(def);
        return def;
    }

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
        ScriptType.Any);

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

    // --- 内联伪函数判断 ---

    private static readonly HashSet<FunctionSymbol> IntrinsicFunctions = [Append, Length, StrConvert, IntConvert, Wait, CaptureHole, OcrHole, RoiHole];
    public static bool IsIntrinsic(FunctionSymbol fn) => IntrinsicFunctions.Contains(fn);

    // --- 采集卡能力追踪 ---

    private static readonly HashSet<FunctionSymbol> CaptureRequiringFunctions = [CaptureHole, OcrHole, RoiHole];
    public const string CapturePlaceholder = "__capture__";
    public static bool RequiresCapture(FunctionSymbol fn) => CaptureRequiringFunctions.Contains(fn);

    // --- 注册到 root scope 的函数列表 ---

    private static readonly FunctionSymbol[] All =
        [Wait, Print, Alert, Rand, Amiibo, Beep, Env, Append, Length, StrEncode, StrConvert, IntConvert, Jq];

    internal static IReadOnlyList<FunctionSymbol> GetAll() => All;

    // --- 注册 callable 的函数列表（不含内联伪函数）---

    private static readonly FunctionSymbol[] Callables =
        [Wait, Print, Alert, Rand, Amiibo, Beep, Env, StrEncode, Jq];

    internal static IReadOnlyList<FunctionSymbol> GetCallables() => Callables;

    // --- 采集卡洞函数列表（注册到 lib-only scope + callable）---

    private static readonly FunctionSymbol[] CaptureHoles = [CaptureHole, OcrHole, RoiHole];
    internal static IReadOnlyList<FunctionSymbol> GetCaptureHoles() => CaptureHoles;
}