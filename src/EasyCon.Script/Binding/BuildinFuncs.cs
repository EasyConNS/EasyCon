using EasyCon.Script.Symbols;
using System.Reflection;

namespace EasyCon.Script.Binding;

internal static class BuiltinFunctions
{
    public static readonly FunctionSymbol Wait = new("WAIT", [], [new("duration", ScriptType.Int, hasDefault: true, defaultValue: 50)], ScriptType.Void);
    public static readonly FunctionSymbol Print = new("PRINT", [], [new("message", ScriptType.String, hasDefault : true, defaultValue : "")], ScriptType.Void);
    public static readonly FunctionSymbol Alert = new("ALERT", [], [new("message", ScriptType.String)], ScriptType.Void);
    public static readonly FunctionSymbol Rand = new("RAND", [], [new("max", ScriptType.Int, hasDefault: true, defaultValue: 100)], ScriptType.Int);
    public static readonly FunctionSymbol Timestamp = new("TIME", [], [], ScriptType.Int);
    public static readonly FunctionSymbol Amiibo = new("AMIIBO", [], [new("index", ScriptType.Int)], ScriptType.Void);
    public static readonly FunctionSymbol Beep = new("BEEP", [], [new("freq", ScriptType.Int), new("duration", ScriptType.Int)], ScriptType.Void);

    // --- 泛型集合操作 ---
    private static readonly TypeParameter T = new("T");

    // APPEND<T>(array: Array<T>, value: T): Array<T>
    public static readonly FunctionSymbol Append = new("APPEND", [T],
        [
            new("array", ScriptType.ArrayDefinition.Bind(T)),
            new("value", T)
        ],
        ScriptType.ArrayDefinition.Bind(T)
    );

    // LEN<T>(container: Array<T>): int 
    public static readonly FunctionSymbol Length = new("LEN", [T], [new("var", T)], ScriptType.Int);

    /// <summary>
    /// 获取所有内置函数符号
    /// </summary>
    internal static IEnumerable<FunctionSymbol> GetAll()
        => typeof(BuiltinFunctions).GetFields(BindingFlags.Public | BindingFlags.Static)
                                   .Where(f => f.FieldType == typeof(FunctionSymbol))
                                   .Select(f => (FunctionSymbol)f.GetValue(null)!);
}
