using Compiler.Parsers;
using Compiler.Scanners;
using RE = Compiler.Scanners.RegExpression;

namespace EC.Script;

public class ECScript : ParserBase
{
    private Token K_IF;
    private Token K_ELIF;
    private Token K_ELSE;
    private Token K_ENDIF;
    private Token K_FOR;
    private Token K_TO;
    private Token K_IN;
    private Token K_STEP;
    private Token K_NEXT;
    private Token K_BRK;
    private Token K_CONTU;
    private Token K_FUNC;
    private Token K_RET;
    private Token K_ENDFUNC;

    private Token G_WAIT;
    private Token G_RESET;
    private Token G_DPAD;
    private Token G_STICK;
    private Token G_BTN;

    private Token LO_AND;
    private Token LO_OR;
    private Token LO_NOT;
    private Token O_MOV;
    private Token O_COMPARE;
    private Token O_NOTEQ;
    private Token O_ARITHASSIGN;
    private Token O_LOGIASSIGN;
    private Token O_NEGI;
    private Token O_SHFTL;
    private Token O_SHFTR;
    private Token O_LPH;
    private Token O_RPH;
    private Token O_LBK;
    private Token O_RBK;
    private Token O_LBR;
    private Token O_RBR;
    private Token O_COMA;
    private Token O_COLON;
    private Token O_SEMI;
    private Token O_DOT;

    private Token V_CONST;
    private Token V_VAR;
    private Token V_EXVAR;
    private Token V_NUM;

    private Token T_IDENT;
    private Token T_NEWLINE;
    private Token S_STR;
    private Token S_COMMENT;
    private Token S_WHITESPACE;

    protected override void OnDefineLexer(Lexicon lexicon, ICollection<Token> skippedTokens)
    {
        var lexer = lexicon.Lexer;
        var inputchar = RE.Simple("[^\u000D\u000A\u0085\u2028\u2029]*");
        var ident = RE.Simple(@"[\d\p{L}_]+");

        S_STR = lexer.DefineToken(inputchar.PackBy("\""), "STRING");
        S_COMMENT = lexer.DefineToken(RE.Simple("#").Concat(inputchar), "COMMENT");
        S_WHITESPACE = lexer.DefineToken(RE.Simple("[\u0020\u0009\u000B\u000C]+"), "WHITESPACE");
        T_NEWLINE = lexer.DefineToken(RE.Simple("[\u000D\u000A\u0085\u2028\u2029\r\n]+"), Scanner.LINEBREAKER);

#region key words
        {
            // program key words
            K_IF = lexer.DefineToken(RE.IgnoreCase("if"));
            K_ELIF = lexer.DefineToken(RE.IgnoreCase("elif"));
            K_ELSE = lexer.DefineToken(RE.IgnoreCase("else"));
            K_ENDIF = lexer.DefineToken(RE.IgnoreCase("endif"));
        }
        {
            K_FOR = lexer.DefineToken(RE.IgnoreCase("for"));
            K_TO = lexer.DefineToken(RE.IgnoreCase("to"));
            K_IN = lexer.DefineToken(RE.IgnoreCase("in"), "IN(R");
            K_STEP = lexer.DefineToken(RE.IgnoreCase("step"));
            K_NEXT = lexer.DefineToken(RE.IgnoreCase("next"));
            K_BRK = lexer.DefineToken(RE.IgnoreCase("break"));
            K_CONTU = lexer.DefineToken(RE.IgnoreCase("continue"));
        }
        {
            K_FUNC = lexer.DefineToken(RE.IgnoreCase("func"), "FUNCTION");
            K_RET = lexer.DefineToken(RE.IgnoreCase("return"));
            K_ENDFUNC = lexer.DefineToken(RE.IgnoreCase("endfunc"), "FUNCEND");
        }
#endregion
#region gamepad key
        {
            G_WAIT = lexer.DefineToken(RE.IgnoreCase("wait"));
            G_RESET = lexer.DefineToken(RE.IgnoreCase("reset"), "RESET(R)");
            G_DPAD = lexer.DefineToken(RE.IgnoreCase("DLEFT")|RE.IgnoreCase("DRIGHT")|RE.IgnoreCase("DUP")|RE.IgnoreCase("DDOWN"), "KEY_DPAD");
            G_STICK = lexer.DefineToken(RE.IgnoreCase("RS")|RE.IgnoreCase("LS"), "KEY_STICK");
            G_BTN = lexer.DefineToken(RE.IgnoreCase("ZL")|RE.IgnoreCase("ZR")|
            RE.IgnoreCase("LCLICK")|RE.IgnoreCase("RCLICK")|
            RE.IgnoreCase("HOME")|RE.IgnoreCase("CAPTURE")|RE.IgnoreCase("PLUS")|RE.IgnoreCase("MINUS")|
            RE.IgnoreCase("A")|RE.IgnoreCase("B")|RE.IgnoreCase("X")|RE.IgnoreCase("Y")|RE.IgnoreCase("L")|RE.IgnoreCase("R")
            , "KEY_GAMEPAD");

        }
#endregion
#region symbols
        {
            // need escape: $ () * + . [] ? \ ^ {} |
            LO_AND = lexer.DefineToken(RE.Simple("&&"), "OP_AND");
            LO_OR = lexer.DefineToken(RE.Simple(@"\|\|"), "OP_OR");
            LO_NOT = lexer.DefineToken(RE.Simple("!"), "OP_NOT");
            O_MOV = lexer.DefineToken(RE.Simple("="), "OP_MOVE");
            O_COMPARE = lexer.DefineToken(RE.Simple(@"[<>=]=?"), "OP_COMPARE");
            O_NOTEQ = lexer.DefineToken(RE.Simple(@"!="), "O_NOTEQ");
            O_ARITHASSIGN = lexer.DefineToken(RE.Simple(@"[\+\-\*/\\\^%]=?"), "OP_ARITHASSIGN");
            O_LOGIASSIGN = lexer.DefineToken(RE.Simple(@"[&\|]=?"), "OP_LOGIASSIGN");
            O_NEGI = lexer.DefineToken(RE.Simple("~"), "OP_NEGATIVE");
            O_SHFTL = lexer.DefineToken(RE.Simple("<<"), "OP_SHLEFT");
            O_SHFTR = lexer.DefineToken(RE.Simple(">>"), "OP_SHRIGHT");
            O_LPH = lexer.DefineToken(RE.Simple(@"\("), "LEFT_PH");
            O_RPH = lexer.DefineToken(RE.Simple(@"\)"), "RIGHT_PH");
            O_LBK = lexer.DefineToken(RE.Simple(@"\["), "LEFT_BK");
            O_RBK = lexer.DefineToken(RE.Simple(@"\]"), "RIGHT_BK");
            O_COMA = lexer.DefineToken(RE.Simple(","), "COMMA");
            O_COLON = lexer.DefineToken(RE.Simple(":"), "COLON");
            O_SEMI = lexer.DefineToken(RE.Simple(";"), "SEMICOLON");
            O_DOT = lexer.DefineToken(RE.Simple(@"\."), "DOT");
        }
#endregion
#region ident
        {
            V_CONST = lexer.DefineToken(RE.Simple(@"_").Concat(ident), "CONST");
            V_EXVAR = lexer.DefineToken(RE.Simple("@").Concat(ident), "EXVAR");
            V_VAR = lexer.DefineToken(RE.Simple(@"\${1,2}\w+"), "VARIABLE");
            V_NUM = lexer.DefineToken(RE.Simple(@"\d+(\.\d*)?"), "NUM");
        }
#endregion
        // must be the last
        T_IDENT = lexer.DefineToken(ident, "IDENT");

        skippedTokens.Add(S_WHITESPACE);
        skippedTokens.Add(T_NEWLINE);
    }
}