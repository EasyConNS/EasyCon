using EasyCon.Script2.Syntax;

namespace EasyScript;

// AST节点基类
public abstract class ExpressionNode
{
    public abstract void Accept(IVisitor visitor);
}

// 算术表达式节点
public class ArithmeticExpression : ExpressionNode
{
    public ExpressionNode Left { get; }
    public ExpressionNode Right { get; }
    public string Operator { get; }

    public ArithmeticExpression(ExpressionNode left, ExpressionNode right, string op)
    {
        Left = left;
        Right = right;
        Operator = op;
    }

    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}

// 条件表达式节点
public class ConditionExpression : ExpressionNode
{
    public ExpressionNode Left { get; }
    public ExpressionNode Right { get; }
    public string Operator { get; }

    public ConditionExpression(ExpressionNode left, ExpressionNode right, string op)
    {
        Left = left;
        Right = right;
        Operator = op;
    }

    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}

// 逻辑表达式节点
public class LogicalExpression : ExpressionNode
{
    public ExpressionNode Left { get; }
    public ExpressionNode Right { get; }
    public string Operator { get; }

    public LogicalExpression(ExpressionNode left, ExpressionNode right, string op)
    {
        Left = left;
        Right = right;
        Operator = op;
    }

    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}

// Not表达式节点
public class NotExpression : ExpressionNode
{
    public ExpressionNode Operand { get; }

    public NotExpression(ExpressionNode operand)
    {
        Operand = operand;
    }

    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}

// 标识符节点
public class IdentifierNode : ExpressionNode
{
    public string Name { get; }

    public IdentifierNode(string name)
    {
        Name = name;
    }

    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}

// 数字节点
public class NumberNode : ExpressionNode
{
    public double Value { get; }

    public NumberNode(double value)
    {
        Value = value;
    }

    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}

// 访问者接口
public interface IVisitor
{
    void Visit(ArithmeticExpression node);
    void Visit(ConditionExpression node);
    void Visit(LogicalExpression node);
    void Visit(NotExpression node);
    void Visit(IdentifierNode node);
    void Visit(NumberNode node);
}

// 语法分析器
public class Parser1
{
    private readonly List<Token> _tokens;
    private int _position;
    private Token _currentToken;

    public Parser1(List<Token> tokens)
    {
        _tokens = tokens;
        _position = 0;
        _currentToken = _tokens[0];
    }

    public ExpressionNode Parse()
    {
        var node = ParseConditionExpression();

        if (_currentToken.Type != TokenType.EOF)
        {
            throw new ArgumentException($"Unexpected token {_currentToken} at position {_currentToken.Span}");
        }

        return node;
    }

    private ExpressionNode ParseConditionExpression()
    {
        var node = ParseLogicalTerm();

        while (_currentToken.Type == TokenType.LogicOr)
        {
            var token = _currentToken;
            Eat(_currentToken.Type);
            var right = ParseLogicalTerm();
            node = new LogicalExpression(node, right, token.Value);
        }

        return node;
    }

    private ExpressionNode ParseLogicalTerm()
    {
        var node = ParseLogicalFactor();

        while (_currentToken.Type == TokenType.LogicAnd)
        {
            var token = _currentToken;
            Eat(_currentToken.Type);
            var right = ParseLogicalFactor();
            node = new LogicalExpression(node, right, token.Value);
        }

        return node;
    }

    private ExpressionNode ParseLogicalFactor()
    {
        if (_currentToken.Type == TokenType.LogicNot)
        {
            Eat(_currentToken.Type);
            var operand = ParseConditionExpression();
            return new NotExpression(operand);
        }
        else if (_currentToken.Type == TokenType.LeftParen)
        {
            Eat(TokenType.LeftParen);
            var node = ParseConditionExpression();
            Eat(TokenType.RightParen);
            return node;
        }
        else
        {
            return ParseSimpleCondition();
        }
    }

    private ExpressionNode ParseSimpleCondition()
    {
        var left = ParseArithmeticExpression();

        if (!_currentToken.Type.IsRelational())
        {
            throw new ArgumentException($"Expected relational operator at position {_currentToken.Span}");
        }

        var relationalOp = _currentToken.Value;
        Eat(_currentToken.Type);

        var right = ParseArithmeticExpression();

        return new ConditionExpression(left, right, relationalOp);
    }

    private ExpressionNode ParseArithmeticExpression()
    {
        var node = ParseTerm();

        while (_currentToken.Type == TokenType.ADD || _currentToken.Type == TokenType.SUB)
        {
            var token = _currentToken;
            Eat(_currentToken.Type);
            var right = ParseTerm();
            node = new ArithmeticExpression(node, right, token.Value);
        }

        return node;
    }

    private ExpressionNode ParseTerm()
    {
        var node = ParseFactor();

        while (_currentToken.Type == TokenType.MUL || _currentToken.Type == TokenType.DIV)
        {
            var token = _currentToken;
            Eat(_currentToken.Type);
            var right = ParseFactor();
            node = new ArithmeticExpression(node, right, token.Value);
        }

        return node;
    }

    private ExpressionNode ParseFactor()
    {
        if (_currentToken.Type == TokenType.INT)
        {
            var node = new NumberNode(double.Parse(_currentToken.Value));
            Eat(TokenType.INT);
            return node;
        }
        else if (_currentToken.Type == TokenType.IDENT)
        {
            var node = new IdentifierNode(_currentToken.Value);
            Eat(TokenType.IDENT);
            return node;
        }
        else if (_currentToken.Type == TokenType.LeftParen)
        {
            Eat(TokenType.LeftParen);
            var node = ParseArithmeticExpression();
            Eat(TokenType.RightParen);
            return node;
        }
        else
        {
            throw new ArgumentException($"Unexpected token {_currentToken} at position {_currentToken.Span}");
        }
    }

    private void Eat(TokenType type)
    {
        if (_currentToken.Type == type)
        {
            _position++;
            _currentToken = _position < _tokens.Count ? _tokens[_position] : new Token(null, TokenType.EOF, "", -1, -1);
        }
        else
        {
            throw new ArgumentException($"Expected {type}, got {_currentToken.Type} at position {_currentToken.Span}");
        }
    }
}

// 打印访问者
public class PrintVisitor : IVisitor
{
    private int _indentLevel = 0;

    public void Visit(ArithmeticExpression node)
    {
        PrintIndent($"Arithmetic({node.Operator})");
        _indentLevel++;
        node.Left.Accept(this);
        node.Right.Accept(this);
        _indentLevel--;
    }

    public void Visit(ConditionExpression node)
    {
        PrintIndent($"Condition({node.Operator})");
        _indentLevel++;
        node.Left.Accept(this);
        node.Right.Accept(this);
        _indentLevel--;
    }

    public void Visit(LogicalExpression node)
    {
        PrintIndent($"Logical({node.Operator})");
        _indentLevel++;
        node.Left.Accept(this);
        node.Right.Accept(this);
        _indentLevel--;
    }

    public void Visit(NotExpression node)
    {
        PrintIndent("Not");
        _indentLevel++;
        node.Operand.Accept(this);
        _indentLevel--;
    }

    public void Visit(IdentifierNode node)
    {
        PrintIndent($"Identifier: {node.Name}");
    }

    public void Visit(NumberNode node)
    {
        PrintIndent($"Number: {node.Value}");
    }

    private void PrintIndent(string text)
    {
        Console.WriteLine(new string(' ', _indentLevel * 2) + text);
    }
}

// 字符串生成访问者
public class StringVisitor : IVisitor
{
    public string Result { get; private set; } = "";

    public void Visit(ArithmeticExpression node)
    {
        Result += "(";
        node.Left.Accept(this);
        Result += $" {node.Operator} ";
        node.Right.Accept(this);
        Result += ")";
    }

    public void Visit(ConditionExpression node)
    {
        Result += "(";
        node.Left.Accept(this);
        Result += $" {node.Operator} ";
        node.Right.Accept(this);
        Result += ")";
    }

    public void Visit(LogicalExpression node)
    {
        Result += "(";
        node.Left.Accept(this);
        Result += $" {node.Operator} ";
        node.Right.Accept(this);
        Result += ")";
    }

    public void Visit(NotExpression node)
    {
        Result += "(not ";
        node.Operand.Accept(this);
        Result += ")";
    }

    public void Visit(IdentifierNode node)
    {
        Result += node.Name;
    }

    public void Visit(NumberNode node)
    {
        Result += node.Value.ToString();
    }
}

// 主程序
class Program
{
    static void Main(string[] args)
    {
        // 测试用例
        string[] testCases = new string[]
        {
            "x1 + 5 = 10",                          // 简单条件
            "age >= 18 and score > 60",            // and 连接
            "status = 'active' or not is_deleted", // or 和 not
            "(a3 + b3) * c < 100 and (x3 / y3) >= 5",  // 复杂算术表达式
            "not (x2 = 0 or y2 = 0)",                // not 和括号
            "price * quantity <= budget",          // 变量运算
            "1 + 2 * 3 = 7",                       // 算术优先级
            "a3 and b3 or c and d",                  // 逻辑优先级
            "not a3 and not b3",                     // 多个 not
            "(x1 = 1 or x1 = 2) and y2 > 0"           // 复杂组合
        };

        foreach (var testCase in testCases)
        {
            try
            {
                Console.WriteLine($"\n解析表达式: {testCase}");
                Console.WriteLine(new string('-', 50));

                // 词法分析
                var tokens = SyntaxTree.ParseTokens(testCase);
                
                Console.WriteLine("Tokens:");
                foreach (var token in tokens)
                {
                    Console.WriteLine($"  {token}");
                }

                // 语法分析
                var parser = new Parser1([.. tokens]);
                var ast = parser.Parse();

                // 打印AST
                Console.WriteLine("\n抽象语法树:");
                var printer = new PrintVisitor();
                ast.Accept(printer);

                // 生成字符串
                var stringVisitor = new StringVisitor();
                ast.Accept(stringVisitor);
                Console.WriteLine($"\n重构的表达式: {stringVisitor.Result}");

                Console.WriteLine("✓ 解析成功");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ 解析失败: {ex.Message}");
            }
        }
        

        // 测试错误情况
        Console.WriteLine("\n\n错误测试:");
        string[] errorCases = new string[]
        {
            "x1 = ",                     // 缺少右操作数
            "and y3 = 1",                // 以运算符开头
            "x2 = 1 and",                // 以运算符结尾
            "x2 = 1 and or y3 = 2",       // 连续运算符
            "(x3 = 1",                   // 括号不匹配
            "x3 = )1",                   // 右括号位置错误
            "1 2 = 3",                  // 缺少运算符
            "xa @ 1",                    // 无效字符
            "x3 = 1 and 2 = 3 or"        // 不完整表达式
        };

        foreach (var errorCase in errorCases)
        {
            try
            {
                Console.WriteLine($"\n解析: {errorCase}");
                var tokens = SyntaxTree.ParseTokens(errorCase);
                var parser = new Parser1([.. tokens]);
                var ast = parser.Parse();
                Console.WriteLine("✗ 应该抛出异常但没有");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✓ 正确抛出异常: {ex.Message}");
            }
        }
    }
}
