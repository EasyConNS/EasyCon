namespace EasyCon2.Script.Parsing
{
    static class Formats
    {
        const string _ident = @"[\d\p{L}_]+";
        const string _Constant = @"_" + _ident;
        const string _ExtVar = @"@" + _ident;
        const string _Variable = @"\${1,2}[\da-zA-Z_]+";
        const string _Number = @"-?\d+";

        //const string _Register = @"\$[\da-zA-Z_]+";

        public const string Constant = "(" + _Constant + ")";
        public const string Constant_F = "^" + _Constant + "$";
        public const string ExtVar = "(" + _ExtVar + ")";
        public const string ExtVar_F = "(" + _ExtVar + ")";
        public const string RegisterEx = "(" + _Variable + ")";
        public const string RegisterEx_F = "^" + _Variable + "$";
        public const string VariableEx = "(" + _Variable + "|" + _ExtVar + ")";
        public const string VariableEx_F = "^" + _Variable + "|" + _ExtVar + "$";
        public const string Instant = "(" + _Constant + "|" + _Number + ")";
        public const string Value = "(" + _Constant + "|" + _Variable + "|" + _Number + ")";
        public const string ValueEx = "(" + _Constant + "|" + _Variable + "|" + _ExtVar + "|" + _Number + ")";
    }
}
