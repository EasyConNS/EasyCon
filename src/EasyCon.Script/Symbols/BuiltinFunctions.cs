using System.Reflection;

namespace EasyCon.Script2.Symbols;

internal static class BuiltinFunctions
{
    public static readonly FunctionSymbol Timestamp = new("time", [new("t", TypeSymbol.Int, 0)], TypeSymbol.Int);
    public static readonly FunctionSymbol Print = new("print", [new("text", TypeSymbol.String, 0)], TypeSymbol.Void);
    public static readonly FunctionSymbol Alert = new("alert", [new("message", TypeSymbol.String, 0)], TypeSymbol.Void);
    public static readonly FunctionSymbol Rnd = new("rand", [new("max", TypeSymbol.Int, 0)], TypeSymbol.Int);
    public static readonly FunctionSymbol Beep = new("beep", [new("freq", TypeSymbol.Int, 0), new("duration", TypeSymbol.Int, 0)], TypeSymbol.Void);

    internal static IEnumerable<FunctionSymbol> GetAll()
        => typeof(BuiltinFunctions).GetFields(BindingFlags.Public | BindingFlags.Static)
                                   .Where(f => f.FieldType == typeof(FunctionSymbol))
                                   .Select(f => (FunctionSymbol)f.GetValue(null)!);
}