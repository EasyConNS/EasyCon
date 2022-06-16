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
    const string KeyABXYLR = "[ABXYLR]";
    const string KeyZLR = "Z[LR]";
    const string KeyStick = "[LR]S{1,2}";
    const string KeyFn = "HOME|^CAPTURE|^PLUS|^MINUS";
    const string KeyClick = "[LR]CLICK";
    const string KeyDPAD = "LEFT|^RIGHT|^UP|^DOWN";

    // idents
    // need escape: $ () * + . [] ? \ ^ {} |
    const string _ident = @"[\d\p{L}_]+";
    const string consts = @"_" + _ident;
    const string extVars = "@" + _ident;
    const string vars = @"\${1,2}[\da-zA-Z_]+";

    const string inputchar = "[^\u000D\u000A\u0085\u2028\u2029]*";

    public static void GetECPLexer(Lexer lexer)
    {
        lexer.DefineToken("#" + inputchar, "COMMENT");
        #region key words
        {
            // program key words
            lexer.DefineToken("if", "IF");
            lexer.DefineToken("elif", "ELSEIF");
            lexer.DefineToken("else", "ELSE");
            lexer.DefineToken("endif", "ENDIF");
        }
        {
            lexer.DefineToken("for", "FOR");
            lexer.DefineToken("to", "TO");
            lexer.DefineToken("step", "STEP");
            lexer.DefineToken("next", "NEXT");
            lexer.DefineToken("break", "BREAK");
            lexer.DefineToken("continue", "CONTINUE");
        }
        {
            lexer.DefineToken("func", "FUNCDEF");
            lexer.DefineToken("ret", "RETURN");
            lexer.DefineToken("call", "CALL(R");
        }
        #endregion
        #region gamepad key
        {
            lexer.DefineToken("wait", "WAIT");
            lexer.DefineToken("reset", "RESET(R)");
            lexer.DefineToken(KeyFn, "KEY_FUNCTION");
            lexer.DefineToken(KeyDPAD, "KEY_DPAD");
            lexer.DefineToken(KeyClick, "KEY_CLICK");
            lexer.DefineToken(KeyStick, "KEY_STICK");
            lexer.DefineToken(KeyZLR, "KEY_ZLR");
            lexer.DefineToken(KeyABXYLR, "KEY_ABXYLR");
        }
        #endregion
        #region symbols
        {
            lexer.DefineToken("&&", "OP_AND");
            lexer.DefineToken(@"\|\|", "OP_OR");
            lexer.DefineToken(@"[\+\-\*/<>\^=]=?", "OP_SYMBOL");
            lexer.DefineToken("%=", "OP_MOD_EQ");
            lexer.DefineToken("!", "OP_NOT");
            lexer.DefineToken("~", "OP_NEGATIVE");
            lexer.DefineToken(@"[&\|]=?", "OP_ALG");
            lexer.DefineToken(@",", "COMMA");
            lexer.DefineToken(@"\.", "DOT");
        }
        #endregion
        #region ident
        {
            lexer.DefineToken(consts, "CONST");
            lexer.DefineToken(vars, "VARIABLE");
            lexer.DefineToken(extVars, "EXVAR");
            lexer.DefineToken(@"\d+", "NUM");
        }
        #endregion
        // must be the last
        lexer.DefineToken(_ident, "IDENT");
    }
}
