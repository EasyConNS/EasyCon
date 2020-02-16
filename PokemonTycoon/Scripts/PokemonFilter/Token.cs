using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PokemonTycoon.Scripts.PokemonFilter
{
    abstract partial class Filter
    {
        abstract class Token
        { }

        class Expression : Token
        {
            string text;
            List<Token> tokens = new List<Token>();

            Expression(string str, List<Token> list)
            {
                text = str;
                tokens.AddRange(list);
            }

            Expression(string str)
            {
                text = str;
            }

            public static Expression Parse(string str)
            {
                return Parse(str, 0, str.Length);
            }

            static Expression Parse(string str, int start, int end)
            {
                var expr = new Expression(str);
                expr.Digest(start, end);
                return expr;
            }

            void Digest(int start, int end)
            {
                // scan tokens
                while (start < end)
                {
                    string s = text.Substring(start, end - start);
                    if (s.StartsWith(")"))
                        throw new FormatException($"错误的右括号！");
                    if (s.StartsWith("("))
                    {
                        int bracket = 1;
                        for (int i = 1; i < s.Length; i++)
                        {
                            if (s[i] == ')')
                            {
                                bracket--;
                                if (bracket == 0)
                                {
                                    tokens.Add(Expression.Parse(text, start + 1, start + i));
                                    start += i + 1;
                                    break;
                                }
                            }
                            else if (s[i] == '(')
                                bracket++;
                        }
                        if (bracket > 0)
                            throw new FormatException($"括号未封闭！");
                        continue;
                    }
                    // operators
                    var ro = TokenOperator.Operators.Find(u => s.StartsWith(u.Simbol));
                    if (ro != null)
                    {
                        tokens.Add(TokenOperator.Parse(ro.Simbol));
                        start += ro.Simbol.Length;
                        continue;
                    }
                    // names and values
                    var m = Regex.Match(s, @"^[\d\p{L}-_]+");
                    if (m.Success)
                    {
                        tokens.Add(new TokenVal(m.Value));
                        start += m.Value.Length;
                        continue;
                    }
                    // error
                    throw new FormatException($"无法解析符号'{s[0]}'");
                }

                // build tree
                List<Token> newTokens = new List<Token>();
                List<Token> list = new List<Token>();
                foreach (var item in tokens)
                {
                    if (item is TokenLogical)
                    {
                        if (list.Count == 0)
                            throw new FormatException($"表达式不完整！");
                        newTokens.Add(new Expression(text, list));
                        newTokens.Add(item);
                        list.Clear();
                    }
                    else
                        list.Add(item);
                }
                if (newTokens.Count > 0)
                {
                    if (list.Count == 0)
                        throw new FormatException($"表达式不完整！");
                    newTokens.Add(new Expression(text, list));
                    tokens = newTokens;
                }
            }

            public Filter BuildFilter()
            {
                if (tokens.Count == 0)
                    return new True();
                if (tokens.Count == 1)
                {
                    if (tokens[0] is Expression)
                        return (tokens[0] as Expression).BuildFilter();
                    else if (tokens[0] is TokenVal)
                        return Checker.Create((tokens[0] as TokenVal).Val);
                    throw new FormatException($"语法错误'{tokens[0]}'");
                }
                if (tokens[1] is TokenLogical)
                {
                    List<Tuple<LogicalOperator, Filter>> list = new List<Tuple<LogicalOperator, Filter>>();
                    LogicalOperator op = LogicalOperator.And;
                    for (int i = 0; i < tokens.Count; i++)
                    {
                        list.Add(new Tuple<LogicalOperator, Filter>(op, (tokens[i] as Expression).BuildFilter()));
                        i++;
                        if (i < tokens.Count)
                            op = (tokens[i] as TokenLogical).Operator;
                    }
                    return new Logic(list);
                }
                if (tokens[1] is TokenCompare)
                {
                    if (tokens.Count < 3)
                        throw new FormatException($"表达式不完整！");
                    if (tokens.Count > 3)
                        throw new FormatException($"语法错误'{tokens[3]}'");
                    return Checker.Create((tokens[0] as TokenVal).Val, (tokens[1] as TokenCompare).Operator, (tokens[2] as TokenVal).Val);
                }
                if (tokens[0] is TokenUnary)
                {
                    if (tokens.Count == 2 && tokens[1] is Expression)
                        return UnaryExpr.Create((tokens[0] as TokenUnary).Operator, (tokens[1] as Expression).BuildFilter());
                    if (tokens.Count == 3 && tokens[1] is TokenVal && tokens[2] is Expression)
                        return UnaryExpr.Create((tokens[0] as TokenUnary).Operator, (tokens[2] as Expression).BuildFilter(), (tokens[1] as TokenVal).Val);
                    throw new FormatException($"错误的用法'{tokens[0]}'");
                }
                throw new FormatException($"语法错误'{tokens[0]}'");
            }

            public override string ToString()
            {
                return $"({string.Join(" ", tokens)})";
            }
        }

        abstract class TokenOperator : Token
        {
            public class RegisteredOperator
            {
                public string Simbol;
                public object Operator;

                public RegisteredOperator(string s, object op)
                {
                    Simbol = s;
                    Operator = op;
                }
            }

            public static readonly List<RegisteredOperator> Operators;

            static TokenOperator()
            {
                Operators = new List<RegisteredOperator>();
                foreach (Enum op in Enum.GetValues(typeof(LogicalOperator)))
                    Operators.Add(new RegisteredOperator(op.GetDesc(), op));
                foreach (Enum op in Enum.GetValues(typeof(CompareOperator)))
                    Operators.Add(new RegisteredOperator(op.GetDesc(), op));
                foreach (Enum op in Enum.GetValues(typeof(UnaryOperator)))
                    Operators.Add(new RegisteredOperator(op.GetDesc(), op));
                Operators.Sort((t1, t2) =>
                {
                    var n = t1.Simbol.Length.CompareTo(t2.Simbol.Length);
                    if (n != 0)
                        return -n;
                    return t1.Simbol.CompareTo(t2.Simbol);
                });
            }

            public static TokenOperator Parse(string str)
            {
                var ro = Operators.Find(u => u.Simbol == str);
                if (ro.Operator is LogicalOperator)
                    return new TokenLogical((LogicalOperator)ro.Operator);
                if (ro.Operator is CompareOperator)
                    return new TokenCompare((CompareOperator)ro.Operator);
                if (ro.Operator is UnaryOperator)
                    return new TokenUnary((UnaryOperator)ro.Operator);
                return null;
            }
        }

        class TokenLogical : TokenOperator
        {
            public readonly LogicalOperator Operator;

            public TokenLogical(LogicalOperator op)
            {
                Operator = op;
            }

            public override string ToString()
            {
                return Operator.GetDesc();
            }
        }

        class TokenCompare : TokenOperator
        {
            public readonly CompareOperator Operator;

            public TokenCompare(CompareOperator op)
            {
                Operator = op;
            }

            public override string ToString()
            {
                return Operator.GetDesc();
            }
        }

        class TokenUnary : TokenOperator
        {
            public readonly UnaryOperator Operator;

            public TokenUnary(UnaryOperator op)
            {
                Operator = op;
            }

            public override string ToString()
            {
                return Operator.GetDesc();
            }
        }

        class TokenVal : Token
        {
            public readonly string Val;

            public TokenVal(string val)
            {
                Val = val;
            }

            public override string ToString()
            {
                return Val;
            }
        }
    }
}
