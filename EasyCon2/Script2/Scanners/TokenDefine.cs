using System.Globalization;

namespace Compiler.Scanners;
static class TokenDefine
{
    private static ICollection<UnicodeCategory> lettersCategories = new HashSet<UnicodeCategory>()
    { 
        UnicodeCategory.LetterNumber,
        UnicodeCategory.LowercaseLetter,
        UnicodeCategory.ModifierLetter,
        UnicodeCategory.OtherLetter,
        UnicodeCategory.TitlecaseLetter,
        UnicodeCategory.UppercaseLetter
    };
    // button key words
    const string KeyABXYLR = "A|B|X|Y|L|R";
    const string KeyZLR = "Z[LR]";
    const string KeyStick = "[LR]S{1,2}";
    const string KeyHome = "HOME";
    const string KeyCapt = "CAPTURE";
    const string KeyPLUS = "PLUS";
    const string KeyMINUS = "MINUS";
    const string KeyClick = "[LR]CLICK";
    const string KeyDPAD_Left = "LEFT";
    const string KeyDPAD_Right = "RIGHT";
    const string KeyDPAD_Up = "UP";
    const string KeyDPAD_Down = "DOWN";
    const string KeyReset = "reset";
    const string KeyWait = "wait";
    // program key words
    const string _KIf = "if";
    const string _KElif = "elif";
    const string _KElse = "else";
    const string _KEndif = "endif";

    const string _KFor = "for";
    const string _KTo = "to";
    const string _KStep = "step";
    const string _KNext = "next";
    const string _KBreak = "break";
    const string _KContinue = "continue";

    const string _KFunc = "func";
    const string _KReturn = "ret";
    const string _RCall = "call";

    // idents
    // need escape: $ () * + . [] ? \ ^ {} |
    const string _ident = @"[\d\p{L}_]+";
    const string consts = @"_" + _ident;
    const string extVars = "@" + _ident;
    const string vars = @"\${1,2}[\da-zA-Z_]+";
    const string nums = @"-?\d+";
    const string segma = @",";

    const string comment = "#[^\u000D\u000A\u0085\u2028\u2029]*";
    // symbols
    const string _SAnd = "&&";
    const string _SOr = "||";
    const string _SALGOP = @"[&\|]=?";
    const string _SOP_NOT = "~";
    const string _SOP = @"[\+\-\*/<>\^=]=?";
    const string _SOP_MOD_EQ = "%=";

    public static void GetECPLexer(Lexer lexer)
    {
        lexer.DefineToken(comment, "COMMENT");
        #region key words
        {
            lexer.DefineToken(_KIf, "IF");
            lexer.DefineToken(_KElif, "ELSEIF");
            lexer.DefineToken(_KElse, "ELSE");
            lexer.DefineToken(_KEndif, "ENDIF");
        }
        {
            lexer.DefineToken(_KFor, "FOR");
            lexer.DefineToken(_KTo, "TO");
            lexer.DefineToken(_KStep, "STEP(R)");
            lexer.DefineToken(_KNext, "NEXT");
            lexer.DefineToken(_KBreak, "BREAK");
            lexer.DefineToken(_KContinue, "CONTINUE");
        }
        #endregion
        {
            lexer.DefineToken(_SOP, "OP_SYMBOL");
            lexer.DefineToken(_SOP_MOD_EQ, "OP_MOD_EQ");
            lexer.DefineToken(_SALGOP, "OP_ALG");
            lexer.DefineToken(_SOP_NOT, "OP_NOT");
        }
        #region gamepad key
        {
            lexer.DefineToken(KeyWait, "WAIT");
            lexer.DefineToken(KeyReset, "RESET");
            lexer.DefineToken(KeyHome, "KEY_HOME");
            lexer.DefineToken(KeyCapt, "KEY_CAPTURE");
            lexer.DefineToken(KeyPLUS, "KEY_PLUS");
            lexer.DefineToken(KeyMINUS, "KEY_MINUS");
            lexer.DefineToken(KeyDPAD_Right, "DPAD_RIGHT");
            lexer.DefineToken(KeyDPAD_Left, "DPAD_LEFT");
            lexer.DefineToken(KeyDPAD_Up, "DPAD_UP");
            lexer.DefineToken(KeyDPAD_Down, "DPAD_DOWN");
            lexer.DefineToken(KeyClick, "KEY_CLICK");
            lexer.DefineToken(KeyStick, "KEY_STICK");
            lexer.DefineToken(KeyZLR, "KEY_ZLR");
            // no need for single key alpha
            //lexer.DefineToken(KeyABXYLR, "KEY_ABXYLR");
        }
        #endregion
        #region ident
        {
            lexer.DefineToken(consts, "CONST");
            lexer.DefineToken(vars, "VARIABLE");
            lexer.DefineToken(extVars, "EXVAR");
            lexer.DefineToken(nums, "NUM");
            lexer.DefineToken(segma, "SEGMA");
        }
        #endregion
        lexer.DefineToken(_ident, "IDENT");
    }
}
