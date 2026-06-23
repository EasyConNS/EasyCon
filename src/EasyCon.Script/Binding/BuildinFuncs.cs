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

    // --- 内联伪函数判断 ---

    private static readonly HashSet<FunctionSymbol> IntrinsicFunctions = [Append, Length, StrConvert, IntConvert, Wait, CaptureHole, OcrHole, RoiHole, OcrInitHole, Rand];
    public static bool IsIntrinsic(FunctionSymbol fn) => IntrinsicFunctions.Contains(fn);

    // --- 采集卡能力追踪 ---

    private static readonly HashSet<FunctionSymbol> CaptureRequiringFunctions = [CaptureHole, OcrHole, RoiHole, OcrInitHole];
    public const string CapturePlaceholder = "__capture__";
    public static bool RequiresCapture(FunctionSymbol fn) => CaptureRequiringFunctions.Contains(fn);

    // --- 注册到 root scope 的函数列表 ---

    private static readonly FunctionSymbol[] All =
        [Wait, Alert, Rand, Amiibo, Beep, Env, Append, Length, StrEncode, StrConvert, IntConvert, Jq, Arg, OcrConf,
         FOpen, FRead, FWrite, FClose, FEof, ReadFile, WriteFile, AppendFile, FileExists];

    internal static IReadOnlyList<FunctionSymbol> GetAll() => All;

    public static bool IsBuiltin(FunctionSymbol fn) => All.Contains(fn);

    // --- 采集卡洞函数列表（注册到 lib-only scope + callable）---

    private static readonly FunctionSymbol[] CaptureHoles = [CaptureHole, OcrHole, RoiHole, OcrInitHole];
    internal static IReadOnlyList<FunctionSymbol> GetCaptureHoles() => CaptureHoles;
}