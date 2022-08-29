using System.Globalization;
using VBF.Compilers;
using VBF.Compilers.Parsers;
using VBF.Compilers.Scanners;
using RE = VBF.Compilers.Scanners.RegularExpression;
using ECP.Ast;

namespace ECP;

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
    private Token K_CALL;

    private Token G_WAIT;
    private Token G_RESET;
    private Token G_DPAD;
    private Token G_STICK;
    private Token G_BTN;

    private Token LO_AND;
    private Token LO_OR;
    private Token LO_NOT;

    private Token O_LESS;
    private Token O_GREATER;
    private Token O_LESSEQ;
    private Token O_GREATEREQ;
    private Token O_NOTEQ;
    private Token O_EQUAL;
    private Token O_MOV;
    private Token O_PLUS;
    private Token O_MINUS;
    private Token O_ASTERISK;
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
        RE RE_SpaceChar = null;
        RE RE_InputChar = null;

        var charSetBuilder = new CharSetExpressionBuilder();

        charSetBuilder.DefineCharSet(c => lettersCategories.Contains(Char.GetUnicodeCategory(c)), re => RE_IdChar = re | RE.Symbol('_'));
        charSetBuilder.DefineCharSet(c => Char.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator, re => RE_SpaceChar = re);
        charSetBuilder.DefineCharSet(c => "\u000D\u000A\u0085\u2028\u2029".IndexOf(c) < 0, re => RE_InputChar = re);

        charSetBuilder.Build();

        var lexer = lexicon.Lexer;
        var literalcase = (string key) => {
            return RE.Literal(key.ToLower()) | RE.Literal(key.ToUpper());
        };
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
            
            G_DPAD = lexer.DefineToken(literalcase("LEFT")|literalcase("RIGHT")|literalcase("UP")|literalcase("DOWN"), "KEY_DPAD");
            G_STICK = lexer.DefineToken(literalcase("RS")|literalcase("LS")|literalcase("RSS")|literalcase("LSS"), "KEY_STICK");
            G_BTN = lexer.DefineToken(literalcase("ZL")|literalcase("ZR")|
            literalcase("LCLICK")|literalcase("RCLICK")|
            literalcase("HOME")|literalcase("CAPTURE")|literalcase("PLUS")|literalcase("MINUS")|
            literalcase("A")|literalcase("B")|literalcase("X")|literalcase("Y")|literalcase("L")|literalcase("R")
            , "KEY_GAMEPAD");
        }
        #endregion
        #region symbols
        {
            LO_AND = lexer.DefineToken(RE.Literal("&&"));
            LO_OR = lexer.DefineToken(RE.Literal("||"));
            LO_NOT = lexer.DefineToken(RE.Symbol('!'));
            O_LESS = lexer.DefineToken(RE.Symbol('<'));
            O_LESSEQ = lexer.DefineToken(RE.Literal("<="));
            O_GREATER = lexer.DefineToken(RE.Symbol('>'));
            O_GREATEREQ = lexer.DefineToken(RE.Literal(">="));
            O_NOTEQ = lexer.DefineToken(RE.Literal("!="));
            O_EQUAL = lexer.DefineToken(RE.Literal("=="));
            O_MOV = lexer.DefineToken(RE.Symbol('='));
            O_PLUS = lexer.DefineToken(RE.Symbol('+'));
            O_MINUS = lexer.DefineToken(RE.Symbol('-'));
            O_ASTERISK = lexer.DefineToken(RE.Symbol('*'));
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
            V_VAR = lexer.DefineToken((RE.Symbol('$') | RE.Literal("$$")) >> (RE_IdChar | RE.Range('0', '9')).Many(), "VARIABLE");
            V_EXVAR = lexer.DefineToken(RE.Symbol('@') >> (RE_IdChar | RE.Range('0', '9')).Many(), "EXVAR");
            V_NUM = lexer.DefineToken(RE.Range('0', '9').Many1(), "NUM");

            T_STR = lexer.DefineToken(RE.Symbol('"') >> RE_InputChar.Many() >> RE.Symbol('"'), "STRING");
            T_NEWLINE = lexer.DefineToken(
                RE.CharSet("\u000D\u000A\u0085\u2028\u2029") |
                RE.Literal("\r\n")
            , "NEWLINE");
            T_IDENT = lexer.DefineToken(RE_IdChar >>
                (RE_IdChar | RE.Range('0', '9')).Many(), "IDENT");
        }
        #endregion
        S_COMMENT = lexer.DefineToken(RE.Symbol('#') >> RE_InputChar.Many() >> RE.Literal("\r\n"), "COMMENT");
        S_WHITESPACE = lexer.DefineToken(RE_SpaceChar | RE.CharSet("\u0009\u000B\u000C"));

        skippedTokens.Add(S_WHITESPACE);
        skippedTokens.Add(S_COMMENT);
        //skippedTokens.Add(T_NEWLINE);
    }

    private readonly Production<Program> PProgram = new();
    private readonly Production<Statement> PStatement = new();
    private readonly Production<Statement> PConstDefine = new();
    private readonly Production<Statement> BinaryEq = new();
    private readonly Production<Statement> PMovExp = new();
    private readonly Production<Statement> PIfElse = new();
    private readonly Production<Binary> PIfExp = new();
    private readonly Production<Statement> PForWhile = new();
    private readonly Production<Statement> PBreak = new();
    private readonly Production<Statement> PContinue = new();
    private readonly Production<Statement> PWait = new();
    private readonly Production<Expression> PExp = new();
    private readonly Production<Number> PInstant = new();
    private readonly Production<Number> PNum = new();
    private readonly Production<Number> PValue = new();
    private readonly Production<Statement> PSTD = new();
    private readonly Production<Statement> PKeyAction = new();
    private readonly Production<Statement> PStickAction = new();

    protected override ProductionBase<Program> OnDefineGrammar()
    {
        PProgram.Rule =
            from statements in PStatement.Many()
            select new Program(statements.ToArray());

        Production<Statement> PEmpty = new();
        PEmpty.Rule =
            from _1 in S_COMMENT
            select (Statement)new Empty(_1.Value.Content);

        PStatement.Rule =
            PEmpty |
            PForWhile |
            PIfElse |
            PConstDefine |
            PMovExp |
            PWait |
            PKeyAction |
            PStickAction |
            PSTD;

        PConstDefine.Rule =
            from constVal in V_CONST
            from mov in O_MOV
            from number in (V_NUM.AsTerminal() | V_CONST.AsTerminal())
            from _nl in T_NEWLINE.Many1()
            select (Statement)new ConstDefine(constVal.Value.Content, number.Value);
        
        PInstant.Rule =
            from number in (V_CONST.AsTerminal() | V_NUM.AsTerminal())
            select new Number(number.Value);
        PNum.Rule =
            from number in (V_CONST.AsTerminal() | V_VAR.AsTerminal() | V_NUM.AsTerminal())
            select new Number(number.Value);
        PValue.Rule =
            from number in (V_CONST.AsTerminal() | V_VAR.AsTerminal() | V_NUM.AsTerminal() | V_EXVAR.AsTerminal())
            select new Number(number.Value);

        BinaryEq.Rule =
            from dVal in V_VAR
            from op in (O_PLUS.AsTerminal() | O_MINUS.AsTerminal() | O_ASTERISK.AsTerminal() | O_SLASH.AsTerminal() | O_SLASHI.AsTerminal() |
                        O_MOD.AsTerminal() | O_AND.AsTerminal() | O_OR.AsTerminal() | O_XOR.AsTerminal()|
                        O_SHFTL.AsTerminal() | O_SHFTR.AsTerminal())
            from eq in O_MOV
            from number in PValue
            from _nl in T_NEWLINE.Many1()
            select (Statement)new Binary(dVal.Value, number);
        PMovExp.Rule =
            BinaryEq |
            (from dVal in V_VAR
            from mov in O_MOV
            from number in PValue
            from _nl in T_NEWLINE.Many1()
            select (Statement)new MovStatement(dVal.Value, number));

        PIfElse.Rule =
            from _if in K_IF
            from condExp in PIfExp
            from _nlif1 in T_NEWLINE.Many1()
            from statements in PStatement.Many()
            from _endif in K_ENDIF
            from _nlif2 in T_NEWLINE.Many1()
            select (Statement)new IfElse(condExp, statements.ToArray());

        PIfExp.Rule =
            from _1 in PValue
            from op in (O_LESS.AsTerminal()|O_LESSEQ.AsTerminal() |O_GREATER.AsTerminal() | O_GREATEREQ.AsTerminal()|O_NOTEQ.AsTerminal() |O_EQUAL.AsTerminal() | O_MOV.AsTerminal())
            from _2 in PValue
            select new Binary(_1, _2);

        Production<ForStatement> PForFull = new()
        {
            Rule =
            from dVal in V_VAR
            from mov in O_MOV
            from _start in PNum
            from _to in K_TO
            from _end in PNum
            select new ForStatement()
        };
        Production<ForStatement> PForExp = new()
        {
            Rule =
            PForFull |
            (from _num in PNum
             select new ForStatement())
        };
        PForWhile.Rule =
            from _1 in K_FOR
            from _forExp in PForExp.Optional()
            from _nl1 in T_NEWLINE.Many1()
            from statements in (PStatement.Many())
            from _next in K_NEXT
            from _nl2 in T_NEWLINE.Many1()
            select (Statement)new ForWhile(_forExp, statements.ToArray());
        
        PWait.Rule =
            from _1 in G_WAIT.Optional()
            from number in (V_VAR.AsTerminal() | V_NUM.AsTerminal())
            from _nl2 in T_NEWLINE.Many1()
            select (Statement)new WaitExp(new Number(number.Value));

        Production<Statement> PKeyHold = new()
        {
            Rule =
            from _key in (G_BTN.AsTerminal() | G_DPAD.AsTerminal())
            from dest in G_DPAD
            where (dest.Value.Content == "UP" || dest.Value.Content == "DOWN")
            from _nl2 in T_NEWLINE.Many1()
            select (Statement)new ButtonAction(_key.Value)
        };
        PKeyAction.Rule =
            PKeyHold |
            from _key in (G_BTN.AsTerminal() | G_DPAD.AsTerminal())
            from dur in PValue.Optional()
            from _nl2 in T_NEWLINE.Many1()
            select (Statement)new ButtonAction(_key.Value);

        PStickAction.Rule =
            from _key in G_STICK
            from dest in G_DPAD
            from _end in T_NEWLINE.Many1()
            select (Statement)new ButtonAction(_key.Value);

        PSTD.Rule =
            from _k in T_IDENT
            from arg in (T_STR.AsTerminal() | V_VAR.AsTerminal())
            from _end in T_NEWLINE.Many1()
            select (Statement)new BuildinState(_k.Value.Content);

        return PProgram;
    }
}
