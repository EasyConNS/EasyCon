namespace EasyScript.Parsing;

static class Formats
{
    const string _ident = @"[\d\p{L}_]+";
    const string _Constant = @"_" + _ident;
    const string _ExtVar = @"@" + _ident;
    const string _Variable = @"\$" + _ident;
    const string _Number = @"-?\d+";

    public const string Constant_F = "^" + _Constant + "$";
    public const string ExtVar_F = "(" + _ExtVar + ")";
    public const string RegisterEx = "(" + _Variable + ")";
    public const string RegisterEx_F = "^" + _Variable + "$";
    public const string VariableEx = "(" + _Variable + "|" + _ExtVar + ")";
    public const string VariableEx_F = "^" + _Variable + "|" + _ExtVar + "$";
    public const string ValueEx = "(" + _Constant + "|" + _Variable + "|" + _ExtVar + "|" + _Number + ")";
}
