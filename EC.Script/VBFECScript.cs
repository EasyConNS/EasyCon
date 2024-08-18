using System.Globalization;
using VBF.Compilers;
using VBF.Compilers.Parsers;
using VBF.Compilers.Scanners;
using RE = VBF.Compilers.Scanners.RegularExpression;
using ECP.Ast;

namespace EC.Script;

public class VBFECScript : ParserBase<Program>
{
    #region token define
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
    private Token K_IMPORT;
    private Token K_AS;
    private Token K_TRUE;
    private Token K_FALSE;

    private Token G_DPAD;
    private Token G_STICK;
    private Token G_BTN;
    private Token G_DIR;
    private Token G_RESET;

    private Token LO_AND;
    private Token LO_OR;
    private Token LO_NOT;

    private Token O_LESS;
    private Token O_GREATER;
    private Token O_LESSEQ;
    private Token O_GREATEREQ;
    private Token O_NOTEQ;
    private Token O_EQUAL;
    private Token O_ASSIGN;
    private Token O_PLUS;
    private Token O_MINUS;
    private Token O_MULT;
    private Token O_SLASH;
    private Token O_SLASHI;
    private Token O_AND;
    private Token O_OR;
    private Token O_MOD;
    private Token O_XOR;
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
    private Token T_STR;

    private Token S_COMMENT;
    private Token S_WHITESPACE;
    #endregion

    public VBFECScript(CompilationErrorManager errorManager) : base(errorManager) { }

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
        RE RE_InputChar = null;

        var charSetBuilder = new CharSetExpressionBuilder();

        charSetBuilder.DefineCharSet(c => lettersCategories.Contains(Char.GetUnicodeCategory(c)), re => RE_IdChar = re);
        charSetBuilder.DefineCharSet(c => "\u000D\u000A\u0085\u2028\u2029".IndexOf(c) < 0, re => RE_InputChar = re);

        charSetBuilder.Build();

        var lexer = lexicon.Lexer;
        var literalcase = (string key) => RE.Literal(key.ToLower()) | RE.Literal(key.ToUpper());

        #region key words
        {
            K_IMPORT = lexer.DefineToken(literalcase("import"), "IMPORT");
            K_AS = lexer.DefineToken(literalcase("as"), "AS");
            // program key words
            K_IF = lexer.DefineToken(literalcase("if"), "IF");
            K_ELIF = lexer.DefineToken(literalcase("elif"),"ELIF");
            K_ELSE = lexer.DefineToken(literalcase("else"),"ELSE");
            K_ENDIF = lexer.DefineToken(literalcase("endif"),"ENDIF");
            K_FOR = lexer.DefineToken(literalcase("for"),"FOR");
            K_TO = lexer.DefineToken(literalcase("to"),"TO");
            K_IN = lexer.DefineToken(literalcase("in"), "IN(R)");
            K_STEP = lexer.DefineToken(literalcase("step"),"STEP");
            K_NEXT = lexer.DefineToken(literalcase("next"),"NEXT");
            K_BRK = lexer.DefineToken(literalcase("break"),"BREAK");
            K_CONTU = lexer.DefineToken(literalcase("continue"),"CONTINUE");
            K_FUNC = lexer.DefineToken(literalcase("func"), "FUNCTION");
            K_RET = lexer.DefineToken(literalcase("return"),"RETURN");
            K_ENDFUNC = lexer.DefineToken(literalcase("endfunc"), "ENDFUNC");
            K_TRUE = lexer.DefineToken(literalcase("true"), "true");
            K_FALSE = lexer.DefineToken(literalcase("false"), "false");
        }
        #endregion
        #region gamepad key
        {
            G_RESET = lexer.DefineToken(literalcase("reset"), "RESET(R)");
            G_DPAD = lexer.DefineToken(literalcase("DLEFT")|literalcase("DRIGHT") |literalcase("DUP") |literalcase("DDOWN"), "KEY_DPAD");
            G_STICK = lexer.DefineToken(literalcase("RS")|literalcase("LS"), "KEY_STICK");
            G_BTN = lexer.DefineToken(literalcase("A") | literalcase("B") | literalcase("X") | literalcase("Y") | literalcase("L") | literalcase("R") |
            literalcase("ZL")|literalcase("ZR")|
            literalcase("LCLICK")|literalcase("RCLICK")|
            literalcase("HOME")|literalcase("CAPTURE")|literalcase("PLUS")|literalcase("MINUS")
            , "KEY_GAMEPAD");
            G_DIR = lexer.DefineToken(literalcase("UP") | literalcase("DOWN")|
                literalcase("RIGHT") | literalcase("LEFT"), "direction");
        }
        #endregion
        #region symbols
        {
            LO_AND = lexer.DefineToken(literalcase("and"),"AND");
            LO_OR = lexer.DefineToken(literalcase("or"), "OR");
            LO_NOT = lexer.DefineToken(literalcase("not"), "not");

            O_ASSIGN = lexer.DefineToken(RE.Symbol('='));

            O_LESS = lexer.DefineToken(RE.Symbol('<'));
            O_LESSEQ = lexer.DefineToken(RE.Literal("<="));
            O_GREATER = lexer.DefineToken(RE.Symbol('>'));
            O_GREATEREQ = lexer.DefineToken(RE.Literal(">="));
            O_EQUAL = lexer.DefineToken(RE.Literal("=="));
            O_NOTEQ = lexer.DefineToken(RE.Literal("!="));
            O_PLUS = lexer.DefineToken(RE.Symbol('+'));
            O_MINUS = lexer.DefineToken(RE.Symbol('-'));
            O_MULT = lexer.DefineToken(RE.Symbol('*'));
            O_SLASH = lexer.DefineToken(RE.Symbol('/'));
            O_SLASHI = lexer.DefineToken(RE.Symbol('\\'));
            O_AND = lexer.DefineToken(RE.Symbol('&'));
            O_OR = lexer.DefineToken(RE.Symbol('|'));
            O_MOD = lexer.DefineToken(RE.Symbol('%'));
            O_XOR = lexer.DefineToken(RE.Symbol('^')); 
            O_NEGI = lexer.DefineToken(RE.Symbol('~'));
            O_SHFTL = lexer.DefineToken(RE.Literal("<<"));
            O_SHFTR = lexer.DefineToken(RE.Literal(">>"));
            O_LPH = lexer.DefineToken(RE.Symbol('('));
            O_RPH = lexer.DefineToken(RE.Symbol(')'));
            O_LBK = lexer.DefineToken(RE.Symbol('['));
            O_RBK = lexer.DefineToken(RE.Symbol(']'));
            O_LBR = lexer.DefineToken(RE.Symbol('{'));
            O_RBR = lexer.DefineToken(RE.Symbol('}'));
            O_COMA = lexer.DefineToken(RE.Symbol(','));
            O_COLON = lexer.DefineToken(RE.Symbol(':'));
            O_SEMI = lexer.DefineToken(RE.Symbol(';'));
            O_DOT = lexer.DefineToken(RE.Symbol('.'));
        }
        #endregion
        #region ident
        {
            V_CONST = lexer.DefineToken(RE.Symbol('_') >> (RE_IdChar | RE.Range('0', '9')).Many(), "CONST");
            V_VAR = lexer.DefineToken((RE.Symbol('$') | RE.Literal("$$")) >> (RE_IdChar | RE.Range('0', '9')).Many(), "VAR");
            V_EXVAR = lexer.DefineToken(RE.Symbol('@') >> (RE_IdChar | RE.Range('0', '9')).Many(), "EXVAR");
            V_NUM = lexer.DefineToken(RE.Range('0', '9').Many1() >> (RE.Symbol('.') >> RE.Range('0', '9').Many1()).Optional(), "NUM");

            T_NEWLINE = lexer.DefineToken(RE.CharSet("\u000D\u000A\u0085\u2028\u2029\r\n"), "NEWLINE");
            T_IDENT = lexer.DefineToken(RE_IdChar >>(RE_IdChar | RE.Range('0', '9')).Many(), "IDENT");
        }
        #endregion
        S_COMMENT = lexer.DefineToken(RE.Symbol('#') >> RE_InputChar.Many(), "COMMENT");
        T_STR = lexer.DefineToken(RE.Symbol('"')>>RE_InputChar.Many()>> RE.Symbol('"'), "STRING");
        S_WHITESPACE = lexer.DefineToken(RE.CharSet("\u0020\u0009\u000B\u000C"),"WSPACE");

        skippedTokens.Add(S_WHITESPACE);
        skippedTokens.Add(S_COMMENT);
    }

    private readonly Production<Program> PProgram = new();
    private readonly Production<Statement> PStatement = new();
    private readonly Production<Expression> PExpression = new();

    protected override ProductionBase<Program> OnDefineGrammar()
    {
        PProgram.Rule =
            from statements in PStatement.Many()
            select new Program(statements.ToArray());
        /*
         * prestmt ::= importstmt | constdefine
         * 
         * importstmt ::= K_IMPORT T_STR [ K_AS T_IDENT ]
         * 
         * constdefine ::= V_CONST O_ASSIGN expr
         */
        Production<ConstDefine> PConstDefine = new()
        {
            Rule =
            from cn in V_CONST
            from _ in O_ASSIGN
            from num in V_NUM
            from __ in T_NEWLINE
            select new ConstDefine(cn.Value, num.Value)
        };

        // exprlist ::= expr { O_COMA expr }
        Production<Expression> PExpList = new();
        PExpList.Rule = // number
            from eps in PExpression.Many(O_COMA)
            select new ExpressionList(eps);

        //basic
        Production<Expression> PNumberLiteral = new();
        PNumberLiteral.Rule = // number
            from intvalue in V_NUM
            select (Expression)new Number(intvalue.Value);

        Production<Expression> PBoolLiteral = new();
        PBoolLiteral.Rule = // true | false
            from b in K_TRUE.AsTerminal() | K_FALSE.AsTerminal()
            select (Expression)new BooleanLiteral(b.Value);

        Production<Expression> PVariable = new();
        PVariable.Rule =
            from varName in V_VAR
            select (Expression)new Variable(varName.Value);

        // simpleexp ::= V_NUM | V_CONST | V_EXVAR | T_STR | K_TRUE | K_FALSE | suffexp | O_LPH expr O_RPH
        var simpleExp =
                PNumberLiteral |
                PBoolLiteral |
                PVariable |
                PExpression.PackedBy(O_LPH, O_RPH);

        // suffexp ::= V_VAR [ O_LBK expr O_RBK ]
        Production<Expression> PSuffExp = new();
        PSuffExp.Rule =
            from varName in V_VAR
            from idx in (O_LBK.AsTerminal() | O_RBK.AsTerminal())
            select (Expression)new Variable(varName.Value);

        /*
         * stmt ::= [ assignment | funccall | keyaction | stickaction | ifstmt | forstmt | funcstmt ] T_NEWLINE
         * 
         * assignment ::= suffexp restassign
         * 
         * restassign ::= O_COMA suffexp restassign | O_ASSIGN exprlist
         * 
         * funccall ::= [ T_IDENT O_DOT ] T_IDENT funcargs

         * expr ::= ( simpleexp | unop expr ) { binop expr } 
         * 
         * unop ::= O_MINUS | O_NEGI | LO_NOT // '-' , '~' , 'not'
         * 
         * binop ::= O_PLUS | O_MINUS | O_MULT | O_SLASH | '%' | '^' | '&' | '|' | '<<' | '>>' |
         * '>' | '<' | '>=' | '<=' | '==' | '!=' | LO_AND | LO_OR // 'and' , 'or'
         * 
         * ifstmt ::= K_IF expr { K_ELIF } [ K_ELSE ] K_ENDIF
         * 
         * forstmt ::= K_FOR K_NEXT
         * 
         * funcargs ::= O_LPH [ V_VAR { O_COMA V_VAR } ] O_RPH
         * 
         * funcstmt ::= K_FUNC T_IDENT funcargs T_NEWLINE { stmt } K_ENDFUNC
         * 
         * strargs ::= ( T_STR | expr ) { O_AND strargs }
         */

        /*
         * keyaction ::= ( G_BTN | G_DPAD ) { O_PLUS keyaction } [ V_NUM | V_CONST | V_VAR ]
         * 
         * stickaction ::= G_STICK G_DIR [ V_NUM | V_CONST | V_VAR ]
         */

        return PProgram;
    }
}
