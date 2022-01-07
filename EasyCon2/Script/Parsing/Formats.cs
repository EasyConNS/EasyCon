namespace EasyCon2.Script.Parsing
{
    static class Formats
    {
        const string __Constant = @"_[\d\p{L}_]+";
        const string __Register = @"\$\d+";
        const string __Register32 = @"\$\$\d+";
        const string __Number = @"-?\d+";
        const string __ExtVar = @"@[\d\p{L}_]+";

        public const string Constant = "(" + __Constant + ")";
        public const string Constant_F = "^" + __Constant + "$";
        public const string Register = "(" + __Register + ")";
        public const string Register_F = "^" + __Register + "$";
        public const string Register32_F = "^" + __Register32 + "$";
        public const string ExtVar = "(" + __ExtVar + ")";
        public const string ExtVar_F = "(" + __ExtVar + ")";
        public const string RegisterEx = "(" + __Register + "|" + __Register32 + ")";
        public const string RegisterEx_F = "^" + __Register + "|" + __Register32 + "$";
        public const string VariableEx = "(" + __Register + "|" + __Register32 + "|" + __ExtVar + ")";
        public const string VariableEx_F = "^" + __Register + "|" + __Register32 + "|" + __ExtVar + "$";
        public const string Instant = "(" + __Constant + "|" + __Number + ")";
        public const string Value = "(" + __Constant + "|" + __Register + "|" + __Number + ")";
        public const string ValueEx = "(" + __Constant + "|" + __Register + "|" + __Register32 + "|" + __ExtVar + "|" + __Number + ")";
    }
}
