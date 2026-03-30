using System.Reflection;

namespace EasyCon.Script.Binding;

internal static class BuiltinFunctions
{
    // --- 基础控制与 IO ---
    public static readonly FunctionSymbol Wait = FunctionSymbol.CreateNormal("WAIT",
        [new ParamSymbol("duration", ScriptType.Int)], ScriptType.Void); //

    public static readonly FunctionSymbol Print = FunctionSymbol.CreateNormal("PRINT",
        [new ParamSymbol("message", ScriptType.String)], ScriptType.Void); //

    public static readonly FunctionSymbol Alert = FunctionSymbol.CreateNormal("ALERT",
        [new ParamSymbol("message", ScriptType.String)], ScriptType.Void); //

    // --- 系统与数值 ---
    public static readonly FunctionSymbol Rand = FunctionSymbol.CreateNormal("RAND",
        [new ParamSymbol("max", ScriptType.Int)], ScriptType.Int); //

    public static readonly FunctionSymbol Timestamp = FunctionSymbol.CreateNormal("TIME",
        [], ScriptType.Int); //

    public static readonly FunctionSymbol Beep = FunctionSymbol.CreateNormal("BEEP",
        [new ParamSymbol("freq", ScriptType.Int), new ParamSymbol("duration", ScriptType.Int)], ScriptType.Void); //

    // --- 特定功能 ---
    public static readonly FunctionSymbol Amiibo = FunctionSymbol.CreateNormal("AMIIBO",
        [new ParamSymbol("index", ScriptType.Int)], ScriptType.Void); //

    // --- 泛型集合操作 ---
    private static readonly TypeParameter T = new("T");

    // APPEND<T>(array: Array<T>, value: T): Array<T>
    // 取代了原先硬编码的 [Array, Int] -> Array
    public static readonly FunctionSymbol Append = new(
        "APPEND",
        [T],
        [
            new ParamSymbol("array", ScriptType.ArrayDefinition.Bind(T)),
            new ParamSymbol("value", T)
        ],
        ScriptType.ArrayDefinition.Bind(T)
    );

    // LEN<T>(container: Array<T>): int 
    // 注意：如果是字符串长度，通常由 Value.Length 属性处理，或单独定义 String 重载
    public static readonly FunctionSymbol Length = new(
        "LEN",
        [T],
        [new ParamSymbol("array", ScriptType.ArrayDefinition.Bind(T))],
        ScriptType.Int
    );

    /// <summary>
    /// 获取所有内置函数符号
    /// </summary>
    internal static IEnumerable<FunctionSymbol> GetAll()
        => typeof(BuiltinFunctions).GetFields(BindingFlags.Public | BindingFlags.Static)
                                   .Where(f => f.FieldType == typeof(FunctionSymbol))
                                   .Select(f => (FunctionSymbol)f.GetValue(null)!);
}
