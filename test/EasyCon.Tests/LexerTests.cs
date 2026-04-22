using EasyCon.Script.Syntax;

namespace EasyCon.Tests;

[TestFixture]
public class LexerTests
{
    [Test]
    public void Tokenize_Numbers()
    {
        var tokens = SyntaxTree.ParseTokens("123");
        Assert.That(tokens.Length, Is.EqualTo(2));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.INT));
        Assert.That(tokens[0].Value, Is.EqualTo("123"));
    }

    // [Test]
    // public void Tokenize_DecimalNumbers()
    // {
    //     var tokens = SyntaxTree.ParseTokens("3.14");
    //     Assert.That(tokens.Length, Is.EqualTo(2));
    //     Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Number));
    //     Assert.That(tokens[0].Value, Is.EqualTo("3.14"));
    // }

    [Test]
    public void Tokenize_String()
    {
        var tokens = SyntaxTree.ParseTokens("\"hello\"");
        Assert.That(tokens.Length, Is.EqualTo(2));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.STRING));
        Assert.That(tokens[0].Value, Is.EqualTo("\"hello\""));
    }

    [Test]
    public void Tokenize_Keywords()
    {
        var tokens = SyntaxTree.ParseTokens("if else endif");
        Assert.That(tokens.Length, Is.EqualTo(4));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.IF));
        Assert.That(tokens[1].Type, Is.EqualTo(TokenType.ELSE));
        Assert.That(tokens[2].Type, Is.EqualTo(TokenType.ENDIF));
    }

    [Test]
    public void Tokenize_Keywords_CaseInsensitive()
    {
        var tokens = SyntaxTree.ParseTokens("IF ELSE ENDIF");
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.IF));
        Assert.That(tokens[1].Type, Is.EqualTo(TokenType.ELSE));
    }

    [Test]
    public void Tokenize_Variables()
    {
        var tokens = SyntaxTree.ParseTokens("$var");
        Assert.That(tokens.Length, Is.EqualTo(2));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.VAR));
        Assert.That(tokens[0].Value, Is.EqualTo("$var"));
    }

    [Test]
    public void Tokenize_Constants()
    {
        var tokens = SyntaxTree.ParseTokens("_CONST");
        Assert.That(tokens.Length, Is.EqualTo(2));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.CONST));
        Assert.That(tokens[0].Value, Is.EqualTo("_CONST"));
    }

    [Test]
    public void Tokenize_ExternalVariables()
    {
        var tokens = SyntaxTree.ParseTokens("@extvar");
        Assert.That(tokens.Length, Is.EqualTo(2));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.EX_VAR));
        Assert.That(tokens[0].Value, Is.EqualTo("@extvar"));
    }

    [Test]
    public void Tokenize_Operators()
    {
        var tokens = SyntaxTree.ParseTokens("+ - * / %");
        Assert.That(tokens.Length, Is.EqualTo(6));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.ADD));
        Assert.That(tokens[1].Type, Is.EqualTo(TokenType.SUB));
        Assert.That(tokens[2].Type, Is.EqualTo(TokenType.MUL));
        Assert.That(tokens[3].Type, Is.EqualTo(TokenType.DIV));
        Assert.That(tokens[4].Type, Is.EqualTo(TokenType.MOD));
    }

    [Test]
    public void Tokenize_ComparisonOperators()
    {
        var tokens = SyntaxTree.ParseTokens("= == != < > <= >=");
        Assert.That(tokens.Length, Is.EqualTo(8));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.ASSIGN));
        Assert.That(tokens[1].Type, Is.EqualTo(TokenType.EQL));
        Assert.That(tokens[2].Type, Is.EqualTo(TokenType.NEQ));
        Assert.That(tokens[3].Type, Is.EqualTo(TokenType.LESS));
        Assert.That(tokens[4].Type, Is.EqualTo(TokenType.GTR));
        Assert.That(tokens[5].Type, Is.EqualTo(TokenType.LEQ));
        Assert.That(tokens[6].Type, Is.EqualTo(TokenType.GEQ));
    }

    [Test]
    public void Tokenize_AssignmentOperators()
    {
        var tokens = SyntaxTree.ParseTokens("+= -= *= /= %=");
        Assert.That(tokens.Length, Is.EqualTo(6));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.ADD_ASSIGN));
        Assert.That(tokens[1].Type, Is.EqualTo(TokenType.SUB_ASSIGN));
        Assert.That(tokens[2].Type, Is.EqualTo(TokenType.MUL_ASSIGN));
        Assert.That(tokens[3].Type, Is.EqualTo(TokenType.DIV_ASSIGN));
        Assert.That(tokens[4].Type, Is.EqualTo(TokenType.MOD_ASSIGN));
    }

    [Test]
    public void Tokenize_BitwiseOperators()
    {
        var tokens = SyntaxTree.ParseTokens("& | ^ ~ << >>");
        Assert.That(tokens.Length, Is.EqualTo(7));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.BitAnd));
        Assert.That(tokens[1].Type, Is.EqualTo(TokenType.BitOr));
        Assert.That(tokens[2].Type, Is.EqualTo(TokenType.XOR));
        Assert.That(tokens[3].Type, Is.EqualTo(TokenType.BitNot));
        Assert.That(tokens[4].Type, Is.EqualTo(TokenType.SHL));
        Assert.That(tokens[5].Type, Is.EqualTo(TokenType.SHR));
    }

    [Test]
    public void Tokenize_LogicKeywords()
    {
        var tokens = SyntaxTree.ParseTokens("and or not");
        Assert.That(tokens.Length, Is.EqualTo(4));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.LogicAnd));
        Assert.That(tokens[1].Type, Is.EqualTo(TokenType.LogicOr));
        Assert.That(tokens[2].Type, Is.EqualTo(TokenType.LogicNot));
    }

    [Test]
    public void Tokenize_LoopKeywords()
    {
        var tokens = SyntaxTree.ParseTokens("for to step break continue next");
        Assert.That(tokens.Length, Is.EqualTo(7));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.FOR));
        Assert.That(tokens[1].Type, Is.EqualTo(TokenType.TO));
        Assert.That(tokens[2].Type, Is.EqualTo(TokenType.STEP));
        Assert.That(tokens[3].Type, Is.EqualTo(TokenType.BREAK));
        Assert.That(tokens[4].Type, Is.EqualTo(TokenType.CONTINUE));
        Assert.That(tokens[5].Type, Is.EqualTo(TokenType.NEXT));
    }

    [Test]
    public void Tokenize_FunctionKeywords()
    {
        var tokens = SyntaxTree.ParseTokens("func return endfunc");
        Assert.That(tokens.Length, Is.EqualTo(4));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.FUNC));
        Assert.That(tokens[1].Type, Is.EqualTo(TokenType.RETURN));
        Assert.That(tokens[2].Type, Is.EqualTo(TokenType.ENDFUNC));
    }

    [Test]
    public void Tokenize_WhileKeywords()
    {
        var tokens = SyntaxTree.ParseTokens("while end");
        Assert.That(tokens.Length, Is.EqualTo(3));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.WHILE));
        Assert.That(tokens[1].Type, Is.EqualTo(TokenType.END));
    }

    [Test]
    public void Tokenize_PrintLine()
    {
        var tokens = SyntaxTree.ParseTokens("print ！！！");
        Assert.That(tokens.Length, Is.EqualTo(3));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.IDENT));
        Assert.That(tokens[1].Type, Is.EqualTo(TokenType.STRING));
    }

    [Test]
    public void Tokenize_Comment()
    {
        var tokens = SyntaxTree.ParseTokens("# this is a comment");
        Assert.That(tokens.Length, Is.EqualTo(2));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.COMMENT));
        Assert.That(tokens[0].Value, Is.EqualTo("# this is a comment"));
    }

    [Test]
    public void Tokenize_Punctuation()
    {
        var tokens = SyntaxTree.ParseTokens("( ) [ ] { } , . :");
        Assert.That(tokens.Length, Is.EqualTo(10));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.LeftParen));
        Assert.That(tokens[1].Type, Is.EqualTo(TokenType.RightParen));
        Assert.That(tokens[2].Type, Is.EqualTo(TokenType.LeftBracket));
        Assert.That(tokens[3].Type, Is.EqualTo(TokenType.RightBracket));
        Assert.That(tokens[4].Type, Is.EqualTo(TokenType.OpenBrace));
        Assert.That(tokens[5].Type, Is.EqualTo(TokenType.CloseBrace));
        Assert.That(tokens[6].Type, Is.EqualTo(TokenType.COMMA));
        Assert.That(tokens[7].Type, Is.EqualTo(TokenType.DOT));
        Assert.That(tokens[8].Type, Is.EqualTo(TokenType.COLON));
    }

    [Test]
    public void Tokenize_Identifier()
    {
        var tokens = SyntaxTree.ParseTokens("myFunction");
        Assert.That(tokens.Length, Is.EqualTo(2));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.IDENT));
        Assert.That(tokens[0].Value, Is.EqualTo("myFunction"));
    }

    [Test]
    public void Tokenize_MultiCharacterVariables()
    {
        var tokens = SyntaxTree.ParseTokens("$counter1 $value2 $_test");
        Assert.That(tokens.Length, Is.EqualTo(4));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.VAR));
        Assert.That(tokens[1].Type, Is.EqualTo(TokenType.VAR));
        Assert.That(tokens[2].Type, Is.EqualTo(TokenType.VAR));
    }

    [Test]
    public void Tokenize_Assignment()
    {
        var tokens = SyntaxTree.ParseTokens("$x = 10");
        Assert.That(tokens[1].Type, Is.EqualTo(TokenType.ASSIGN));
    }

    [Test]
    public void Tokenize_PrintString()
    {
        var tokens = SyntaxTree.ParseTokens("print 识别到| 累计： & $count &");
        Assert.That(tokens.Length, Is.EqualTo(6));
        Assert.That(tokens[1].Type, Is.EqualTo(TokenType.STRING));
        Assert.That(tokens[4].Type, Is.EqualTo(TokenType.BitAnd));
    }
}