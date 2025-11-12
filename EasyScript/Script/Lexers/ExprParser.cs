using EasyScript.Parsing.Statements;
using System.Reflection;
using System.Text.RegularExpressions;

namespace EasyScript.Parsing.Lexers;

internal class ExprPaerser : IStatementParser
{
    const string regExpr = $@"{Formats.RegisterEx}\s*=\s*";


    static IEnumerable<Meta> OpList()
    {
        var types = from domainAssembly in AppDomain.CurrentDomain.GetAssemblies()
                    from assemblyType in domainAssembly.GetTypes()
                    where assemblyType.IsSubclassOf(typeof(BinaryOp))
                    where assemblyType.GetField("_Meta", BindingFlags.NonPublic | BindingFlags.Static) != null
                    select assemblyType.GetField("_Meta", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) as Meta;
        return types;
    }

    Statement? IStatementParser.Parse(ParserArgument args)
    {
        // empty
        if (args.Text.Length == 0)
            return new Empty();
        // expression
        return ExprParse(args);
    }

    private static Statement? ExprParse(ParserArgument args)
    {
        var pre = Regex.Match(args.Text, $@"^{regExpr}(.*)$", RegexOptions.IgnoreCase);
        if (pre.Success)
        {
            var des = FormatterUtil.GetRegEx(pre.Groups[1].Value, true);
            string exprStr = Regex.Replace(pre.Groups[2].Value, @"\s+", "");

            var vR = Regex.Match(exprStr, $@"^{Formats.ValueEx}");
            if (vR.Success)
            {
                var vr = vR.Value;
                // 尝试找到算术运算符
                Meta? op = null;
                int operatorIndex = -1;

                foreach (var o in OpList())
                {
                    int index = exprStr.IndexOf(o.Operator, StringComparison.Ordinal);
                    if (index > 0) // 运算符不能在开头
                    {
                        op = o;
                        operatorIndex = index;
                        break;
                    }
                }
                if (op != null)
                {
                    // 有算术运算符的情况
                    string var1 = exprStr.Substring(0, operatorIndex);
                    string var2 = exprStr.Substring(operatorIndex + op.Operator.Length);

                    if (IsValidVariable(var1) && IsValidVariable(var2))
                    {
                        return new Expr(des, args.Formatter.GetValueEx(var1), op, args.Formatter.GetValueEx(var2));
                    }
                }
                else if (IsValidVariable(exprStr))
                {
                    return new Expr(des, args.Formatter.GetValueEx(vR.Groups[1].Value), null, null);
                }
            }
        }
        return null;
    }

    private static bool IsValidVariable(string variable)
    {
        return !string.IsNullOrEmpty(variable) && Regex.Match(variable, $"^{Formats.ValueEx}$", RegexOptions.IgnoreCase).Success;
    }
}
