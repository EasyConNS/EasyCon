﻿using Compiler;
using Compiler.Scanners;
using Compiler.Parsers;
using ECP.Ast;

namespace ECP;

public class ECScript : ParserBase<Program>
{
    // idents
    // need escape: $ () * + . [] ? \ ^ {} |
    const string _ident = @"[\d\p{L}_]+";
    const string consts = @"_" + _ident;
    const string extVars = "@" + _ident;
    const string vars = @"\${1,2}[\da-zA-Z_]+";

    const string inputchar = "[^\u000D\u000A\u0085\u2028\u2029]*";

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
    private Token O_LOGISY;
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

    public ECScript(CompilationErrorManager errorManager) : base(errorManager) { }

    protected override void OnDefineLexer(Lexicon lexicon, ICollection<Token> skippedTokens)
    {
        var lexer = lexicon.Lexer;

        S_STR = lexer.DefineToken("\"" + inputchar +"\"$", "STRING");
        S_COMMENT = lexer.DefineToken("#" + inputchar, "COMMENT");
        S_WHITESPACE = lexer.DefineToken("[\u0020\u0009\u000B\u000C]", "WHITESPACE");
        
        T_NEWLINE = lexicon.LineBreaker;
        #region key words
        {
            // program key words
            K_IF = lexer.DefineToken("if");
            K_ELIF = lexer.DefineToken("elif");
            K_ELSE = lexer.DefineToken("else");
            K_ENDIF = lexer.DefineToken("endif");
        }
        {
            K_FOR = lexer.DefineToken("for");
            K_TO = lexer.DefineToken("to");
            K_IN = lexer.DefineToken("in", "IN(R");
            K_STEP = lexer.DefineToken("step");
            K_NEXT = lexer.DefineToken("next");
            K_BRK = lexer.DefineToken("break");
            K_CONTU = lexer.DefineToken("continue");
        }
        {
            K_FUNC = lexer.DefineToken("func", "FUNCTION");
            K_RET = lexer.DefineToken("return");
            K_ENDFUNC = lexer.DefineToken("endfunc", "FUNCEND");
            K_CALL = lexer.DefineToken("call", "CALL(R");
        }
        #endregion
        #region gamepad key
        {
            G_WAIT = lexer.DefineToken("wait");
            G_RESET = lexer.DefineToken("reset", "RESET(R)");
            G_DPAD = lexer.DefineToken("LEFT|^RIGHT|^UP|^DOWN", "KEY_DPAD");
            G_STICK = lexer.DefineToken("[LR]S{1,2}", "KEY_STICK");
            G_BTN = lexer.DefineToken("Z[LR]|^[LR]CLICK|^HOME|^CAPTURE|^PLUS|^MINUS|^[ABXYLR]", "KEY_GAMEPAD");
        }
        #endregion
        #region symbols
        {
            O_AND = lexer.DefineToken("&&", "OP_AND");
            O_OR = lexer.DefineToken(@"\|\|", "OP_OR");
            O_NOT = lexer.DefineToken("!", "OP_NOT");
            O_MOV = lexer.DefineToken("=", "OP_MOVE");
            O_ARH = lexer.DefineToken(@"[\+\-\*/\\<>\^%]", "OP_ARITH");
            O_ARHEQ = lexer.DefineToken(@"[\+\-\*/\\<>\^%=]=", "OP_ARITHEQ");
            O_LOGISY = lexer.DefineToken(@"[&\|]=?", "OP_LOGI");
            O_NEGI = lexer.DefineToken("~", "OP_NEGATIVE");
            O_SHFTL = lexer.DefineToken("<<=", "OP_SHLEFT");
            O_SHFTR = lexer.DefineToken(">>=", "OP_SHRIGHT");
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

        skippedTokens.Add(S_WHITESPACE);
        skippedTokens.Add(S_COMMENT);
    }

    private readonly Production<Program> PProgram = new();
    private readonly Production<Statement> PStatement = new();
    private readonly Production<Statement> PConstDefine = new();
    private readonly Production<Statement> PIfElse = new();
    private readonly Production<Statement> PForWhile = new();

    protected override ProductionBase<Program> OnDefineParser()
    {
        return PProgram;
    }
}
