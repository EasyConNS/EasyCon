namespace EasyCon2.Script.Parsing
{
    static class Formats
    {
        const string _ident = @"[\d\p{L}_]+";
        const string _Constant = @"_" + _ident;
        const string _Register = @"\$[\da-zA-Z_]+";
        const string _Register32 = @"\$" + _Register;
        const string _Number = @"-?\d+";
        const string _ExtVar = @"@" + _ident;

        public const string Constant = "(" + _Constant + ")";
        public const string Constant_F = "^" + _Constant + "$";
        public const string Register = "(" + _Register + ")";
        public const string Register_F = "^" + _Register + "$";
        public const string Register32_F = "^" + _Register32 + "$";
        public const string ExtVar = "(" + _ExtVar + ")";
        public const string ExtVar_F = "(" + _ExtVar + ")";
        public const string RegisterEx = "(" + _Register + "|" + _Register32 + ")";
        public const string RegisterEx_F = "^" + _Register + "|" + _Register32 + "$";
        public const string VariableEx = "(" + _Register + "|" + _Register32 + "|" + _ExtVar + ")";
        public const string VariableEx_F = "^" + _Register + "|" + _Register32 + "|" + _ExtVar + "$";
        public const string Instant = "(" + _Constant + "|" + _Number + ")";
        public const string Value = "(" + _Constant + "|" + _Register + "|" + _Number + ")";
        public const string ValueEx = "(" + _Constant + "|" + _Register + "|" + _Register32 + "|" + _ExtVar + "|" + _Number + ")";
    }
}
