using System.Globalization;
using VBF.Compilers;
using VBF.Compilers.Parsers.Combinators;
using VBF.Compilers.Scanners;
using RE = VBF.Compilers.Scanners.RegularExpression;
using ECP.Ast;

namespace ECP;

public class ECScript : ParserFrame<Program>
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
    private Token K_CALL;

    private Token G_WAIT;
    private Token G_RESET;
    private Token G_DPAD;
    private Token G_STICK;
    private Token G_BTN;

    private Token O_AND;
    private Token O_OR;
    private Token O_NOT;
    private Token O_MOV;
    private Token O_ARH;
    private Token O_ARHEQ;
    private Token O_NEGI;
    private Token O_SHFTL;
    private Token O_SHFTR;
    private Token O_LPH;
    private Token O_RPH;
    private Token O_LBK;
    private Token O_RBK;
    private Token O_COMA;
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

    public ECScript(CompilationErrorManager errorManager) : base(errorManager, 101, 201, 202) { }

    protected override void OnDefineLexer(Lexicon lexicon, ICollection<Token> skippedTokens)
    {
        var lettersCategories = new HashSet<UnicodeCategory>()
        { 
            UnicodeCategory.LetterNumber,
            UnicodeCategory.LowercaseLetter,
            UnicodeCategory.ModifierLetter,
            UnicodeCategory.OtherLetter,
            UnicodeCategory.TitlecaseLetter,
            UnicodeCategory.UppercaseLetter
        };

        RE RE_IdChar = null;
        RE RE_SpaceChar = null;
        RE RE_InputChar = null;

        CharSetExpressionBuilder charSetBuilder = new CharSetExpressionBuilder();

        charSetBuilder.DefineCharSet(c => lettersCategories.Contains(Char.GetUnicodeCategory(c)), re => RE_IdChar = re | RE.Symbol('_'));
        charSetBuilder.DefineCharSet(c => Char.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator, re => RE_SpaceChar = re);
        charSetBuilder.DefineCharSet(c => "\u000D\u000A\u0085\u2028\u2029".IndexOf(c) < 0, re => RE_InputChar = re);

        charSetBuilder.Build();

        var lexer = lexicon.Lexer;
        var literalcase = (string key) => {
            return RE.Literal(key.ToLower()) | RE.Literal(key.ToUpper());
        } ;

        S_STR = lexer.DefineToken(RE.Symbol('"') >> RE_InputChar.Many() >> RE.Symbol('"') , "STRING");
        S_COMMENT = lexer.DefineToken(RE.Symbol('#') >> RE_InputChar.Many(), "COMMENT");
        S_WHITESPACE = lexer.DefineToken(RE_SpaceChar | RE.CharSet("\u0009\u000B\u000C"));
        
        T_NEWLINE = lexer.DefineToken(
            RE.CharSet("\u000D\u000A\u0085\u2028\u2029") |
            RE.Literal("\r\n")
        , "NEWLINE");
        #region key words
        {
            // program key words
            K_IF = lexer.DefineToken(literalcase("if"), "IF");
            K_ELIF = lexer.DefineToken(literalcase("elif"),"ELIF");
            K_ELSE = lexer.DefineToken(literalcase("else"));
            K_ENDIF = lexer.DefineToken(literalcase("endif"));
        }
        {
            K_FOR = lexer.DefineToken(literalcase("for"));
            K_TO = lexer.DefineToken(literalcase("to"));
            K_IN = lexer.DefineToken(literalcase("in"), "IN(R");
            K_STEP = lexer.DefineToken(literalcase("step"));
            K_NEXT = lexer.DefineToken(literalcase("next"));
            K_BRK = lexer.DefineToken(literalcase("break"));
            K_CONTU = lexer.DefineToken(literalcase("continue"));
        }
        {
            K_FUNC = lexer.DefineToken(literalcase("func"), "FUNCTION");
            K_RET = lexer.DefineToken(literalcase("return"));
            K_ENDFUNC = lexer.DefineToken(literalcase("endfunc"), "FUNCEND");
            K_CALL = lexer.DefineToken(literalcase("call"), "call");
        }
        #endregion
        #region gamepad key
        {
            G_WAIT = lexer.DefineToken(literalcase("wait"), "wait");
            G_RESET = lexer.DefineToken(literalcase("reset"), "RESET(R)");
        //    G_DPAD = lexer.DefineToken("LEFT|^RIGHT|^UP|^DOWN", "KEY_DPAD");
        //    G_STICK = lexer.DefineToken("[LR]S{1,2}", "KEY_STICK");
        //    G_BTN = lexer.DefineToken("Z[LR]|^[LR]CLICK|^HOME|^CAPTURE|^PLUS|^MINUS|^[ABXYLR]", "KEY_GAMEPAD");
        }
        #endregion
        #region symbols
        {
            O_AND = lexer.DefineToken(RE.Literal("&&"));
            O_OR = lexer.DefineToken(RE.Literal("||"));
            O_NOT = lexer.DefineToken(RE.Symbol('!'));
            O_MOV = lexer.DefineToken(RE.Symbol('='));
            O_ARH = lexer.DefineToken(RE.CharSet(@"+-*/\<>^%&|"), "OP_ARITH");
            O_ARHEQ = lexer.DefineToken(RE.CharSet(@"+-*/\<>^%&|") >> RE.Symbol('='), "OP_ARITHEQ");
            O_NEGI = lexer.DefineToken(RE.Symbol('~'));
            O_SHFTL = lexer.DefineToken(RE.Literal("<<="));
            O_SHFTR = lexer.DefineToken(RE.Literal(">>="));
            O_LPH = lexer.DefineToken(RE.Symbol('('));
            O_RPH = lexer.DefineToken(RE.Symbol(')'));
            O_LBK = lexer.DefineToken(RE.Symbol('['));
            O_RBK = lexer.DefineToken(RE.Symbol(']'));
            O_COMA = lexer.DefineToken(RE.Symbol(','));
            O_DOT = lexer.DefineToken(RE.Symbol('.'));
        }
        #endregion
        #region ident
        {
            V_CONST = lexer.DefineToken(RE.Symbol('_') >> (RE_IdChar | RE.Range('0', '9')).Many(), "CONST");
            V_VAR = lexer.DefineToken((RE.Symbol('$') | RE.Literal("$$")) >> (RE_IdChar | RE.Range('0', '9')).Many(), "VARIABLE");
            V_EXVAR = lexer.DefineToken(RE.Symbol('@') >> (RE_IdChar | RE.Range('0', '9')).Many(), "EXVAR");
            V_NUM = lexer.DefineToken(RE.Range('0', '9').Many1(), "NUM");
        }
        #endregion
        T_IDENT = lexer.DefineToken(RE_IdChar >>
            (RE_IdChar | RE.Range('0', '9')).Many(), "IDENT");

        skippedTokens.Add(S_WHITESPACE);
        skippedTokens.Add(S_COMMENT);
    }

    private readonly ParserReference<Program> PProgram = new();

    protected override Parser<Program> OnDefineParser()
    {
        return PProgram;
    }
}
