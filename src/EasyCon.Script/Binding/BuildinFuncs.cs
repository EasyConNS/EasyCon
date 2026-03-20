using System.Reflection;

namespace EasyCon.Script.Binding;

internal static class BuiltinFunctions
{
    public static readonly FunctionSymbol Wait = new("WAIT", [new ParamSymbol("duration", ValueType.Int)], ValueType.Void);
    public static readonly FunctionSymbol Print = new("PRINT", [new ParamSymbol("message", ValueType.String)], ValueType.Void);
    public static readonly FunctionSymbol Alert = new("ALERT", [new ParamSymbol("message", ValueType.String)], ValueType.Void);
    public static readonly FunctionSymbol Rand = new("RAND", [new ParamSymbol("max", ValueType.Int)], ValueType.Int);
    public static readonly FunctionSymbol Timestamp = new("TIME", [], ValueType.Int);
    public static readonly FunctionSymbol Amiibo = new("AMIIBO", [new ParamSymbol("index", ValueType.Int)], ValueType.Void);
    public static readonly FunctionSymbol Beep = new("BEEP", [new ParamSymbol("freq", ValueType.Int), new ParamSymbol("duration", ValueType.Int)], ValueType.Void);

    public static readonly FunctionSymbol Append = new("APPEND", [new ParamSymbol("array", ValueType.Array), new ParamSymbol("value", ValueType.Int)], ValueType.Array);
    public static readonly FunctionSymbol Length = new("LEN", [new ParamSymbol("array", ValueType.Any)], ValueType.Int);
    internal static IEnumerable<FunctionSymbol> GetAll()
            => typeof(BuiltinFunctions).GetFields(BindingFlags.Public | BindingFlags.Static)
                                       .Where(f => f.FieldType == typeof(FunctionSymbol))
                                       .Select(f => (FunctionSymbol)f.GetValue(null)!);
}
