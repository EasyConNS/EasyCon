using EasyCon.Script.Syntax;
using System.Collections.Immutable;

namespace EasyCon.Tests;

[TestFixture]
public class LexerTests
{
    private static ImmutableArray<Token> Tokenize(string code) => SyntaxTree.ParseTokens(code);

    private static Token First(string code) => Tokenize(code)[0];

    #region 字面量

    [Test]
    public void IntLiteral()
    {
        Assert.That(First("42").Type, Is.EqualTo(TokenType.INT));
        Assert.That(First("42").Value, Is.EqualTo("42"));
    }

    [Test]
    public void StringLiteral_DoubleQuote()
    {
        var t = First("\"hello\"");
        Assert.That(t.Type, Is.EqualTo(TokenType.STRING));
        Assert.That(t.Value, Is.EqualTo("\"hello\""));
    }

    [Test]
    public void StringLiteral_SingleQuote()
    {
        var t = First("'single'");
        Assert.That(t.Type, Is.EqualTo(TokenType.STRING));
        Assert.That(t.Value, Does.Contain("single"));
    }

    [Test]
    public void FloatLiteral()
    {
        var t = First("3.14");
        Assert.That(t.Type, Is.EqualTo(TokenType.Number));
        Assert.That(t.Value, Is.EqualTo("3.14"));
    }

    #endregion

    #region 标识符

    [Test]
    public void Variable()
    {
        Assert.That(First("$var").Type, Is.EqualTo(TokenType.VAR));
        Assert.That(First("$var").Value, Is.EqualTo("$var"));
    }

    [Test]
    public void Constant()
    {
        Assert.That(First("_CONST").Type, Is.EqualTo(TokenType.CONST));
        Assert.That(First("_CONST").Value, Is.EqualTo("_CONST"));
    }

    [Test]
    public void Extern()
    {
        Assert.That(First("@ext").Type, Is.EqualTo(TokenType.EX_VAR));
        Assert.That(First("@ext").Value, Is.EqualTo("@ext"));
    }

    [Test]
    public void DoubleDollar_Variable()
    {
        Assert.That(First("$$var").Type, Is.EqualTo(TokenType.VAR));
    }

    [Test]
    public void ChineseIdentifier()
    {
        Assert.That(First("测试函数").Type, Is.EqualTo(TokenType.IDENT));
    }

    #endregion

    #region 关键字

    [Test]
    public void Keywords_AllControlFlow()
    {
        var t = Tokenize("IF ELIF ELSE ENDIF WHILE UNTIL END FOR TO IN STEP BREAK CONTINUE NEXT FUNC ENDFUNC RETURN IMPORT EXTERN FROM");
        var expected = new[]
        {
            TokenType.IF, TokenType.ELIF, TokenType.ELSE, TokenType.ENDIF,
            TokenType.WHILE, TokenType.UNTIL, TokenType.END, TokenType.FOR, TokenType.TO, TokenType.IN, TokenType.STEP,
            TokenType.BREAK, TokenType.CONTINUE, TokenType.NEXT,
            TokenType.FUNC, TokenType.ENDFUNC, TokenType.RETURN,
            TokenType.IMPORT, TokenType.EXTERN, TokenType.FROM
        };
        Assert.That(t.Length - 1, Is.EqualTo(expected.Length));
        for (int i = 0; i < expected.Length; i++)
            Assert.That(t[i].Type, Is.EqualTo(expected[i]), $"Token[{i}] {t[i].Value}");
    }

    [TestCase("if", TokenType.IF)]
    [TestCase("IF", TokenType.IF)]
    [TestCase("If", TokenType.IDENT)]
    public void Keyword_CaseSensitivity(string input, TokenType expected)
    {
        Assert.That(First(input).Type, Is.EqualTo(expected));
    }

    [Test]
    public void LogicKeywords()
    {
        var t = Tokenize("and or not");
        Assert.That(t[0].Type, Is.EqualTo(TokenType.LogicAnd));
        Assert.That(t[1].Type, Is.EqualTo(TokenType.LogicOr));
        Assert.That(t[2].Type, Is.EqualTo(TokenType.LogicNot));
    }

    [Test]
    public void BoolKeywords()
    {
        var t = Tokenize("TRUE FALSE");
        Assert.That(t[0].Type, Is.EqualTo(TokenType.TRUE));
        Assert.That(t[1].Type, Is.EqualTo(TokenType.FALSE));
    }

    #endregion

    #region 运算符

    [Test]
    public void Operators_Arithmetic()
    {
        var t = Tokenize("+ - * / % \\");
        Assert.That(t[0].Type, Is.EqualTo(TokenType.ADD));
        Assert.That(t[1].Type, Is.EqualTo(TokenType.SUB));
        Assert.That(t[2].Type, Is.EqualTo(TokenType.MUL));
        Assert.That(t[3].Type, Is.EqualTo(TokenType.DIV));
        Assert.That(t[4].Type, Is.EqualTo(TokenType.MOD));
        Assert.That(t[5].Type, Is.EqualTo(TokenType.SlashI));
    }

    [Test]
    public void Operators_Comparison()
    {
        var t = Tokenize("== != < > <= >=");
        Assert.That(t[0].Type, Is.EqualTo(TokenType.EQL));
        Assert.That(t[1].Type, Is.EqualTo(TokenType.NEQ));
        Assert.That(t[2].Type, Is.EqualTo(TokenType.LESS));
        Assert.That(t[3].Type, Is.EqualTo(TokenType.GTR));
        Assert.That(t[4].Type, Is.EqualTo(TokenType.LEQ));
        Assert.That(t[5].Type, Is.EqualTo(TokenType.GEQ));
    }

    [Test]
    public void Operators_Bitwise()
    {
        var t = Tokenize("& | ^ ~ << >>");
        Assert.That(t[0].Type, Is.EqualTo(TokenType.BitAnd));
        Assert.That(t[1].Type, Is.EqualTo(TokenType.BitOr));
        Assert.That(t[2].Type, Is.EqualTo(TokenType.XOR));
        Assert.That(t[3].Type, Is.EqualTo(TokenType.BitNot));
        Assert.That(t[4].Type, Is.EqualTo(TokenType.SHL));
        Assert.That(t[5].Type, Is.EqualTo(TokenType.SHR));
    }

    [Test]
    public void Operators_AugmentedAssignment()
    {
        var t = Tokenize("+= -= *= /= %= &= |= ^= <<= >>=");
        var expected = new[]
        {
            TokenType.ADD_ASSIGN, TokenType.SUB_ASSIGN, TokenType.MUL_ASSIGN,
            TokenType.DIV_ASSIGN, TokenType.MOD_ASSIGN,
            TokenType.BitAnd_ASSIGN, TokenType.BitOr_ASSIGN, TokenType.XOR_ASSIGN,
            TokenType.SHL_ASSIGN, TokenType.SHR_ASSIGN
        };
        Assert.That(t.Length - 1, Is.EqualTo(expected.Length));
        for (int i = 0; i < expected.Length; i++)
            Assert.That(t[i].Type, Is.EqualTo(expected[i]));
    }

    [Test]
    public void Assignment_Equals()
    {
        Assert.That(First("=").Type, Is.EqualTo(TokenType.ASSIGN));
    }

    #endregion

    #region 游戏手柄

    [Test]
    public void Gamepad_Buttons()
    {
        var t = Tokenize("A B X Y L R ZL ZR");
        for (int i = 0; i < 8; i++)
            Assert.That(t[i].Type, Is.EqualTo(TokenType.ButtonKeyword), $"Token[{i}] {t[i].Value}");
    }

    [Test]
    public void Gamepad_Sticks()
    {
        var t = Tokenize("LS RS");
        Assert.That(t[0].Type, Is.EqualTo(TokenType.StickKeyword));
        Assert.That(t[1].Type, Is.EqualTo(TokenType.StickKeyword));
    }

    [Test]
    public void Gamepad_Directions()
    {
        var t = Tokenize("UP DOWN LEFT RIGHT");
        // UP 独立出现为 ButtonKeyword，DOWN 跟在按钮后变为 StateKeyword
        Assert.That(t[0].Type, Is.EqualTo(TokenType.ButtonKeyword)); // UP
        Assert.That(t[1].Type, Is.EqualTo(TokenType.StateKeyword));  // DOWN (after UP)
        Assert.That(t[2].Type, Is.EqualTo(TokenType.ButtonKeyword)); // LEFT
        Assert.That(t[3].Type, Is.EqualTo(TokenType.ButtonKeyword)); // RIGHT
    }

    [Test]
    public void Gamepad_AfterButton_BecomesStateKeyword()
    {
        // A UP — UP 跟在按钮后变为 StateKeyword
        var t = Tokenize("A UP");
        Assert.That(t[0].Type, Is.EqualTo(TokenType.ButtonKeyword));
        Assert.That(t[1].Type, Is.EqualTo(TokenType.StateKeyword));
    }

    [Test]
    public void Gamepad_AfterStick_BecomesDirectionKeyword()
    {
        // LS RIGHT — RIGHT 跟在摇杆后变为 DirectionKeyword
        var t = Tokenize("LS RIGHT");
        Assert.That(t[0].Type, Is.EqualTo(TokenType.StickKeyword));
        Assert.That(t[1].Type, Is.EqualTo(TokenType.DirectionKeyword));
    }

    #endregion

    #region 标点符号

    [Test]
    public void Punctuation()
    {
        var t = Tokenize("( ) [ ] , :");
        Assert.That(t[0].Type, Is.EqualTo(TokenType.LeftParen));
        Assert.That(t[1].Type, Is.EqualTo(TokenType.RightParen));
        Assert.That(t[2].Type, Is.EqualTo(TokenType.LeftBracket));
        Assert.That(t[3].Type, Is.EqualTo(TokenType.RightBracket));
        Assert.That(t[4].Type, Is.EqualTo(TokenType.COMMA));
        Assert.That(t[5].Type, Is.EqualTo(TokenType.COLON));
    }

    #endregion

    #region 注释

    [Test]
    public void Comment_Hash()
    {
        var t = First("# comment");
        Assert.That(t.Type, Is.EqualTo(TokenType.COMMENT));
        Assert.That(t.Value, Is.EqualTo("# comment"));
    }

    [Test]
    public void Comment_Empty()
    {
        var t = First("#");
        Assert.That(t.Type, Is.EqualTo(TokenType.COMMENT));
    }

    #endregion

    #region PRINT/ALERT 特殊解析

    [Test]
    public void Print_SpecialParsing()
    {
        // PRINT 触发 ReadPrintArguments，& 作为分隔符保留为 BitAnd
        var t = Tokenize("PRINT hello & $count & !");
        Assert.That(t[0].Type, Is.EqualTo(TokenType.IDENT));
        Assert.That(t[0].Value, Is.EqualTo("PRINT"));
        Assert.That(t.Any(tok => tok.Type == TokenType.BitAnd), Is.True);
    }

    [Test]
    public void Alert_SpecialParsing()
    {
        var t = Tokenize("ALERT warning");
        Assert.That(t[0].Type, Is.EqualTo(TokenType.IDENT));
        Assert.That(t[0].Value, Is.EqualTo("ALERT"));
    }

    #endregion

    #region 边界情况

    [Test]
    public void EmptyInput_ProducesOnlyEOF()
    {
        var t = Tokenize("");
        Assert.That(t.Length, Is.EqualTo(1));
        Assert.That(t[0].Type, Is.EqualTo(TokenType.EOF));
    }

    [Test]
    public void OnlyNewlines()
    {
        var t = Tokenize("\n\n\n");
        // NEWLINE tokens + EOF
        Assert.That(t.All(tok => tok.Type is TokenType.NEWLINE or TokenType.EOF), Is.True);
    }

    [Test]
    public void OnlyWhitespace()
    {
        var t = Tokenize("   \t  ");
        Assert.That(t.Length, Is.EqualTo(1));
        Assert.That(t[0].Type, Is.EqualTo(TokenType.EOF));
    }

    [Test]
    public void String_EscapeSequences()
    {
        var t = First(@"""hello\nworld\ttab\\slash""");
        Assert.That(t.Type, Is.EqualTo(TokenType.STRING));
        // 转义序列在 token 值中被解析为实际字符
        Assert.That(t.Value, Does.Contain("\n"));
        Assert.That(t.Value, Does.Contain("\t"));
    }

    [Test]
    public void String_Empty()
    {
        var t = First("\"\"");
        Assert.That(t.Type, Is.EqualTo(TokenType.STRING));
    }

    [Test]
    public void Int_Zero()
    {
        Assert.That(First("0").Type, Is.EqualTo(TokenType.INT));
        Assert.That(First("0").Value, Is.EqualTo("0"));
    }

    [Test]
    public void Int_LargeValue()
    {
        Assert.That(First("999999999").Type, Is.EqualTo(TokenType.INT));
    }

    #endregion

    #region 诊断（错误 token）

    [Test]
    public void Diagnostic_UnterminatedString()
    {
        var tree = SyntaxTree.Parse("\"unterminated");
        Assert.That(tree.Diagnostics.Count, Is.GreaterThan(0));
    }

    [Test]
    public void Diagnostic_InvalidDecimalNumber()
    {
        var tree = SyntaxTree.Parse("$x = 1.2.3");
        Assert.That(tree.Diagnostics.Count, Is.GreaterThan(0));
    }

    [Test]
    public void Diagnostic_BadCharacter()
    {
        var tree = SyntaxTree.Parse("$x = 10 ");
        Assert.That(tree.Diagnostics.Count, Is.GreaterThan(0));
    }

    #endregion
}
