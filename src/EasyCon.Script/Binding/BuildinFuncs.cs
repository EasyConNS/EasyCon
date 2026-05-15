using EasyCon.Script.Symbols;

namespace EasyCon.Script.Binding;

internal static class BuiltinFunctions
{
    public static readonly FunctionSymbol Wait = new("WAIT", [], [new("duration", ScriptType.Int, hasDefault: true, defaultValue: 50)], ScriptType.Void);
    public static readonly FunctionSymbol Print = new("PRINT", [], [new("message", ScriptType.String, hasDefault: true, defaultValue: "")], ScriptType.Void);
    public static readonly FunctionSymbol Alert = new("ALERT", [], [new("message", ScriptType.String)], ScriptType.Void);
    public static readonly FunctionSymbol Rand = new("RAND", [], [new("max", ScriptType.Int, hasDefault: true, defaultValue: 100)], ScriptType.Int);
    public static readonly FunctionSymbol Timestamp = new("TIME", [], [], ScriptType.Int);
    public static readonly FunctionSymbol Amiibo = new("AMIIBO", [], [new("index", ScriptType.Int)], ScriptType.Void);
    public static readonly FunctionSymbol Beep = new("BEEP", [], [new("freq", ScriptType.Int), new("duration", ScriptType.Int)], ScriptType.Void);
    public static readonly FunctionSymbol Ocr = new("OCR", [], [new("x", ScriptType.Int), new("y", ScriptType.Int), new("width", ScriptType.Int), new("height", ScriptType.Int)], ScriptType.String);

    // --- 泛型集合操作 ---
    private static readonly TypeParameter T = new("T");

    // APPEND<T>(array: Array<T>, value: T): Array<T>
    public static readonly FunctionSymbol Append = new("APPEND", [T],
        [
            new("array", ScriptType.Array.Bind(T)),
            new("value", T)
        ],
        ScriptType.Array.Bind(T)
    );

    // LEN<T>(container: Array<T>): int
    public static readonly FunctionSymbol Length = new("LEN", [T], [new("var", T)], ScriptType.Int);

    // ENCODE(array: Array<byte>): string
    // type: string = "utf8", "unicode"
    public static readonly FunctionSymbol StrEncode = new("ENCODE", [], [new("array", ScriptType.Array.Bind(ScriptType.Byte)), new("type", ScriptType.String, hasDefault: true, defaultValue: "utf8")], ScriptType.String);
    public static readonly FunctionSymbol StrConvert = new("STRING", [T], [new("var", T)], ScriptType.String);
    // INT<T>(var: T): int
    public static readonly FunctionSymbol IntConvert = new("INT", [T], [new("var", T)], ScriptType.Int);

    // JQ<T>(json: string, query: string): T
    public static readonly FunctionSymbol Jq = new("JQ", [T], [new("json", ScriptType.String), new("query", ScriptType.String)], T);

    /// <summary>
    /// 所有内置函数符号的静态缓存，避免每次反射枚举
    /// </summary>
    private static readonly FunctionSymbol[] All =
        [Wait, Print, Alert, Rand, Timestamp, Amiibo, Beep, Ocr, Append, Length, StrEncode, Jq, StrConvert, IntConvert];

    /// <summary>
    /// 获取所有内置函数符号
    /// </summary>
    internal static IReadOnlyList<FunctionSymbol> GetAll() => All;
}