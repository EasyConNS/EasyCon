using System.Reflection;

namespace EasyCon.Script.Binding;

internal static class BuiltinFunctions
{
    public static readonly FunctionSymbol Print = new("PRINT", 1);
    public static readonly FunctionSymbol Alert = new("ALERT", 1);
    public static readonly FunctionSymbol Rand = new("RAND", 1);
    public static readonly FunctionSymbol Timestamp = new("TIME", 1);
    public static readonly FunctionSymbol Amiibo = new("AMIIBO", 1);
    public static readonly FunctionSymbol Beep = new("BEEP", 2);
    internal static IEnumerable<FunctionSymbol> GetAll()
            => typeof(BuiltinFunctions).GetFields(BindingFlags.Public | BindingFlags.Static)
                                       .Where(f => f.FieldType == typeof(FunctionSymbol))
                                       .Select(f => (FunctionSymbol)f.GetValue(null)!);
}
