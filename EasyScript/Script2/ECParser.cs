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

    public static Token K_IF;
    public static Token K_ELIF;
    public static Token K_ELSE;
    public static Token K_ENDIF;
    public static Token K_FOR;
    public static Token K_TO;
    public static Token K_IN;
    public static Token K_STEP;
    public static Token K_NEXT;
    public static Token K_BRK;
    public static Token K_CONTU;
    public static Token K_FUNC;
    public static Token K_RET;
    public static Token K_ENDFUNC;
    public static Token K_CALL;

    public static Token G_WAIT;
    public static Token G_RESET;
    public static Token G_DPAD;
    public static Token G_STICK;
    public static Token G_BTN;

    public static Token O_AND;
    public static Token O_OR;
    public static Token O_NOT;
    public static Token O_ARH;
    public static Token O_SHFT;
    public static Token O_LPH;
    public static Token O_RPH;
    public static Token O_LBK;
    public static Token O_RBK;
    public static Token O_COMA;
    public static Token O_DOT;

    public static Token V_CONST;
    public static Token V_VAR;
    public static Token V_EXVAR;
    public static Token V_NUM;

    public static Token S_STR;
    public static Token S_COMMENT;
    public static Token T_IDENT;

    public static Token T_NEWLINE;

    public static void OnDefineLexer(Lexer lexer)
    {
        S_STR = lexer.DefineToken("\"" + inputchar +"\"$", "STRING");
        S_COMMENT = lexer.DefineToken("#" + inputchar, "COMMENT");
        T_NEWLINE = lexer.lineToken;
        #region key words
        {
            // program key words
            K_IF = lexer.DefineToken("if", "IF");
            K_ELIF = lexer.DefineToken("elif", "ELSEIF");
            K_ELSE = lexer.DefineToken("else", "ELSE");
            K_ENDIF = lexer.DefineToken("endif", "ENDIF");
        }
        {
            K_FOR = lexer.DefineToken("for", "FOR");
            K_TO = lexer.DefineToken("to", "TO");
            K_IN = lexer.DefineToken("in", "IN");
            K_STEP = lexer.DefineToken("step", "STEP");
            K_NEXT = lexer.DefineToken("next", "NEXT");
            K_BRK = lexer.DefineToken("break", "BREAK");
            K_CONTU = lexer.DefineToken("continue", "CONTINUE");
        }
        {
            K_FUNC = lexer.DefineToken("func", "FUNCDEF");
            K_RET = lexer.DefineToken("return", "RETURN");
            K_ENDFUNC = lexer.DefineToken("endfunc", "FUNCEND");
            K_CALL = lexer.DefineToken("call", "CALL(R");
        }
        #endregion
        #region gamepad key
        {
            G_WAIT = lexer.DefineToken("wait", "WAIT");
            G_RESET = lexer.DefineToken("reset", "RESET(R)");
            G_DPAD = lexer.DefineToken("LEFT|^RIGHT|^UP|^DOWN", "KEY_DPAD");
            G_STICK = lexer.DefineToken("[LR]S{1,2}", "KEY_STICK");
            G_BTN = lexer.DefineToken("[ABXYLR]|^Z[LR]|^[LR]CLICK|^HOME|^CAPTURE|^PLUS|^MINUS", "KEY_GAMEPAD");
        }
        #endregion
        #region symbols
        {
            O_AND = lexer.DefineToken("&&", "OP_AND");
            O_OR = lexer.DefineToken(@"\|\|", "OP_OR");
            O_NOT = lexer.DefineToken("!", "OP_NOT");
            O_ARH = lexer.DefineToken(@"[\+\-\*/\\<>\^%=]=?", "OP_ARITH");
            lexer.DefineToken(@"[&\|]=?", "OP_LOGI");
            lexer.DefineToken("~", "OP_NEGATIVE");
            O_SHFT = lexer.DefineToken("<<=|^>>=", "OP_SHIFT");
            O_LPH = lexer.DefineToken(@"\(", "LEFT_PH");
            O_RPH = lexer.DefineToken(@"\)", "RIGHT_PH");
            O_LBK = lexer.DefineToken(@"\[", "LEFT_BK");
            O_RBK = lexer.DefineToken(@"\]", "RIGHT_BK");
            O_COMA = lexer.DefineToken(@",", "COMMA");
            O_DOT = lexer.DefineToken(@"\.", "DOT");
        }
        #endregion
        #region ident
        {
            V_CONST = lexer.DefineToken(consts, "CONST");
            V_VAR = lexer.DefineToken(vars, "VARIABLE");
            V_EXVAR = lexer.DefineToken(extVars, "EXVAR");
            V_NUM = lexer.DefineToken(@"-?\d+", "NUM");
        }
        #endregion
        // must be the last
        T_IDENT = lexer.DefineToken(_ident, "IDENT");
    }
}
