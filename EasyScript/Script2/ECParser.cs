using Compiler.Scanners;

namespace ECP.Parser;

public static class ECParser
{
    // idents
    // need escape: $ () * + . [] ? \ ^ {} |
    const string _ident = @"[\d\p{L}_]+";
    const string consts = @"_" + _ident;
    const string extVars = "@" + _ident;
    const string vars = @"\${1,2}[\da-zA-Z_]+";

    const string inputchar = "[^\u000D\u000A\u0085\u2028\u2029]*";

    public static void OnDefineLexer(Lexer lexer)
    {
        lexer.DefineToken("\"" + inputchar +"\"$", "STRING");
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
            lexer.DefineToken("in", "IN");
            lexer.DefineToken("step", "STEP");
            lexer.DefineToken("next", "NEXT");
            lexer.DefineToken("break", "BREAK");
            lexer.DefineToken("continue", "CONTINUE");
        }
        {
            lexer.DefineToken("func", "FUNCDEF");
            lexer.DefineToken("return", "RETURN");
            lexer.DefineToken("endfunc", "FUNCEND");
            lexer.DefineToken("call", "CALL(R");
        }
        #endregion
        #region gamepad key
        {
            lexer.DefineToken("wait", "WAIT");
            lexer.DefineToken("reset", "RESET(R)");
            lexer.DefineToken("LEFT|^RIGHT|^UP|^DOWN", "KEY_DPAD");
            lexer.DefineToken("[LR]S{1,2}", "KEY_STICK");
            lexer.DefineToken("[ABXYLR]|^Z[LR]|^[LR]CLICK|^HOME|^CAPTURE|^PLUS|^MINUS", "KEY_GAMEPAD");
        }
        #endregion
        #region symbols
        {
            lexer.DefineToken("&&", "OP_AND");
            lexer.DefineToken(@"\|\|", "OP_OR");
            lexer.DefineToken("!", "OP_NOT");
            lexer.DefineToken(@"[\+\-\*/\\<>\^=]=?", "OP_ARITH");
            lexer.DefineToken(@"[&\|]=?", "OP_LOGI");
            lexer.DefineToken("%=", "OP_MOD_EQ");
            lexer.DefineToken("~", "OP_NEGATIVE");
            lexer.DefineToken(@"\(", "LEFT_PH");
            lexer.DefineToken(@"\)", "RIGHT_PH");
            lexer.DefineToken(@"\[", "LEFT_BK");
            lexer.DefineToken(@"\]", "RIGHT_BK");
            lexer.DefineToken(@",", "COMMA");
            lexer.DefineToken(@"\.", "DOT");
        }
        #endregion
        #region ident
        {
            lexer.DefineToken(consts, "CONST");
            lexer.DefineToken(vars, "VARIABLE");
            lexer.DefineToken(extVars, "EXVAR");
            lexer.DefineToken(@"-?\d+", "NUM");
        }
        #endregion
        // must be the last
        lexer.DefineToken(_ident, "IDENT");
    }
}
