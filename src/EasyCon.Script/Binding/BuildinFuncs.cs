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

    public static readonly FunctionSymbol Wait = new("WAIT", [new("duration", ScriptType.Int, hasDefault: true, defaultValue: 50)], ScriptType.Void);
    public static readonly FunctionSymbol Print = new("PRINT", [new("message", ScriptType.String, hasDefault: true, defaultValue: "")], ScriptType.Void);
    public static readonly FunctionSymbol Alert = new("ALERT", [new("message", ScriptType.String)], ScriptType.Void);
    public static readonly FunctionSymbol Rand = new("RAND", [new("max", ScriptType.Int, hasDefault: true, defaultValue: 100)], ScriptType.Int);
    public static readonly FunctionSymbol Timestamp = new("TIME", [], ScriptType.Int);
    public static readonly FunctionSymbol Amiibo = new("AMIIBO", [new("index", ScriptType.Int)], ScriptType.Void);
    public static readonly FunctionSymbol Beep = new("BEEP", [new("freq", ScriptType.Int), new("duration", ScriptType.Int)], ScriptType.Void);
    public static readonly FunctionSymbol Ocr = new("OCR", [new("x", ScriptType.Int), new("y", ScriptType.Int), new("width", ScriptType.Int), new("height", ScriptType.Int), new("lang", ScriptType.String, hasDefault: true, defaultValue: "chi_sim")], ScriptType.String);
    public static readonly FunctionSymbol Env = new("ENV", [new("name", ScriptType.String)], ScriptType.String);
    public static readonly FunctionSymbol Pixel = new("PIXEL", [new("x", ScriptType.Int), new("y", ScriptType.Int)], new StructType(PixelStructDef));
    public static readonly FunctionSymbol Frame = new("FRAME", [], ScriptType.String);
    public static readonly FunctionSymbol FrameRoi = new("FRAME", [new("x", ScriptType.Int), new("y", ScriptType.Int), new("width", ScriptType.Int), new("height", ScriptType.Int)], ScriptType.String);
    public static readonly FunctionSymbol ImageRoi = new("ROI", [new("image", ScriptType.String), new("x", ScriptType.Int), new("y", ScriptType.Int), new("width", ScriptType.Int), new("height", ScriptType.Int)], ScriptType.String);

    // --- 多态集合操作（参数类型用 Any 占位，由 binder 在调用点解析具体类型）---

    // APPEND(array, value): array
    public static readonly FunctionSymbol Append = new("APPEND",
        [new("array", ScriptType.Any), new("value", ScriptType.Any)],
        ScriptType.Any);

    // LEN(var): int
    public static readonly FunctionSymbol Length = new("LEN",
        [new("var", ScriptType.Any)],
        ScriptType.Int);

    // ENCODE(array: Array<byte>): string
    public static readonly FunctionSymbol StrEncode = new("ENCODE",
        [new("array", ScriptType.ArrayOf(ScriptType.Byte)), new("type", ScriptType.String, hasDefault: true, defaultValue: "utf8")],
        ScriptType.String);

    // STRING(var): string
    public static readonly FunctionSymbol StrConvert = new("STRING",
        [new("var", ScriptType.Any)],
        ScriptType.String);

    // INT(var): int
    public static readonly FunctionSymbol IntConvert = new("INT",
        [new("var", ScriptType.Any)],
        ScriptType.Int);

    // JQ(json, query): any
    public static readonly FunctionSymbol Jq = new("JQ",
        [new("json", ScriptType.String), new("query", ScriptType.String)],
        ScriptType.Any);

    /// <summary>
    /// 所有内置函数符号的静态缓存，避免每次反射枚举
    /// </summary>
    private static readonly FunctionSymbol[] All =
        [Wait, Print, Alert, Rand, Timestamp, Amiibo, Beep, Ocr, Env, Append, Length, StrEncode, StrConvert, IntConvert, Jq, Pixel, Frame, FrameRoi, ImageRoi];

    /// <summary>
    /// 获取所有内置函数符号
    /// </summary>
    internal static IReadOnlyList<FunctionSymbol> GetAll() => All;
}