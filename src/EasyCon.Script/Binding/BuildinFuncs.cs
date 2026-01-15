using System.Reflection;

namespace EasyCon.Script.Binding;

internal static class BuiltinFunctions
{
    public static readonly FunctionSymbol Wait = new("WAIT", [ValueType.Int]);
    public static readonly FunctionSymbol Print = new("PRINT", [ValueType.String]);
    public static readonly FunctionSymbol Alert = new("ALERT", [ValueType.String]);
    public static readonly FunctionSymbol Rand = new("RAND", [ValueType.Int]);
    public static readonly FunctionSymbol Timestamp = new("TIME", [ValueType.Int]);
    public static readonly FunctionSymbol Amiibo = new("AMIIBO", [ValueType.Int]);
    public static readonly FunctionSymbol Beep = new("BEEP", [ValueType.Int, ValueType.Int]);
    internal static IEnumerable<FunctionSymbol> GetAll()
            => typeof(BuiltinFunctions).GetFields(BindingFlags.Public | BindingFlags.Static)
                                       .Where(f => f.FieldType == typeof(FunctionSymbol))
                                       .Select(f => (FunctionSymbol)f.GetValue(null)!);
}
