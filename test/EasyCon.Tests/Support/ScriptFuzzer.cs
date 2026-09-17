using System.Text;

namespace EasyCon.Tests.Support;

/// <summary>
/// 随机 ECS 脚本生成器（wasm-smith 思路的结构化生成：按语法产程序再打印，
/// 而非随机文本——保证语法合法、语义可终止、双端可复现）。
///
/// 确定性约束（解释器 ↔ C VM 对拍前提）：
/// - 不使用 RAND/TIME 等非确定性内建；字面量与控制流全部由种子决定；
/// - 除法恒安全：除数生成为 (expr * expr + 1) ≥ 1，不触发除零；
/// - 终止性：WHILE 采用「预置计数器 + 体尾递减」模板（体内无 CONTINUE/BREAK），FOR 天然有界；
/// - 只引用已初始化变量（池化管理），不产生「找不到变量」类编译错误。
/// </summary>
public static class ScriptFuzzer
{
    /// <summary>生成一个随机脚本。</summary>
    public static string Generate(int seed)
    {
        var gen = new Generator(new Random(seed));
        return gen.Build();
    }

    private sealed class Generator(Random rng)
    {
        private readonly StringBuilder _sb = new();
        private readonly List<string> _vars = [];
        private readonly List<string> _counters = [];
        private readonly List<(string Name, int Params)> _funcs = [];
        private int _depth;

        public string Build()
        {
            // 变量池与循环计数器都在头部初始化（ECS 按语句序做定义前引用检查：
            // 在 IF/FOR 体内新建、体外引用会编译错）；体内只复用已初始化变量
            var poolSize = rng.Next(2, 5);
            for (int i = 0; i < poolSize; i++)
            {
                var v = NewPoolVar();
                _sb.Append($"{v} = {Lit()}\n");
            }
            for (int i = 0; i < 3; i++)
            {
                var c = NewPoolVar();
                _sb.Append($"{c} = 3\n");
                _counters.Add(c);
            }

            var funcCount = rng.Next(0, 3);
            for (int i = 0; i < funcCount; i++)
                EmitFunc();

            EmitBlock(6);
            return _sb.ToString();
        }

        // ---- 变量管理 ----

        private string NewPoolVar()
        {
            var name = $"$v{_vars.Count}";
            _vars.Add(name);
            return name;
        }

        private string PickVar() => _vars[rng.Next(_vars.Count)];

        // ---- 语句 ----

        private void EmitBlock(int maxStatements)
        {
            var count = rng.Next(2, maxStatements + 1);
            for (int i = 0; i < count; i++)
                EmitStatement(i == 0);
        }

        private void EmitStatement(bool isFirst)
        {
            // 首语句不放 BREAK/CONTINUE（脱离循环体的语境会编译错）
            var choice = rng.Next(isFirst ? 7 : 10);
            switch (choice)
            {
                case 0:
                case 1:
                    _sb.Append($"{PickVar()} = {Expr(2)}\n");
                    break;
                case 2:
                    _sb.Append($"PRINT {PickVar()}\n");
                    break;
                case 3:
                    _sb.Append($"PRINT \"s{rng.Next(100)}\"\n");
                    break;
                case 4:
                    EmitIf();
                    break;
                case 5:
                    EmitFor();
                    break;
                case 6:
                    EmitWhile();
                    break;
                case 7:
                    EmitFuncCall();
                    break;
                case 8:
                    _sb.Append($"A {rng.Next(1, 4)}\n");   // 按键带时长 → KEY/KEYST 事件
                    break;
                case 9:
                    EmitIf();   // 提高分支权重（φ/边副本是差分重点）
                    break;
            }
        }

        private void EmitIf()
        {
            _sb.Append($"IF {Cond()}\n");
            _depth++;
            EmitBlock(3);
            _depth--;
            if (rng.Next(2) == 0)
            {
                _sb.Append("ELSE\n");
                _depth++;
                EmitBlock(3);
                _depth--;
            }
            _sb.Append("ENDIF\n");
        }

        private void EmitFor()
        {
            // FOR 有界计数循环；CONTINUE 安全（NEXT 负责步进）
            _sb.Append($"FOR {rng.Next(1, 4)}\n");
            _depth++;
            var body = rng.Next(1, 4);
            for (int i = 0; i < body; i++)
            {
                if (_depth < 3 && rng.Next(4) == 0)
                {
                    _sb.Append($"IF {Cond()}\nCONTINUE\nENDIF\n");
                }
                else
                {
                    _sb.Append($"PRINT {PickVar()}\n");
                }
            }
            _depth--;
            _sb.Append("NEXT\n");
        }

        private void EmitWhile()
        {
            // 终止性模板：复用头部已初始化的计数器 + 体尾递减；体内不放 CONTINUE/BREAK
            var counter = _counters[rng.Next(_counters.Count)];
            _sb.Append($"{counter} = {rng.Next(1, 5)}\n");
            _sb.Append($"WHILE {counter} > 0\n");
            _depth++;
            _sb.Append($"PRINT {PickVar()}\n");
            _sb.Append($"{counter} -= 1\n");
            _depth--;
            _sb.Append("END\n");
        }

        // ---- 函数 ----

        private void EmitFunc()
        {
            var paramCount = rng.Next(1, 3);
            var parameters = new List<string>();
            for (int i = 0; i < paramCount; i++)
                parameters.Add($"$p{_funcs.Count}_{i}");
            var name = $"f{_funcs.Count}_{rng.Next(10, 99)}";

            var savedVars = new List<string>(_vars);
            _vars.Clear();
            foreach (var p in parameters)
                _vars.Add(p);

            _sb.Append($"FUNC {name}({string.Join(", ", parameters)}) : int\n");
            _depth++;
            _sb.Append($"RETURN {Expr(2)}\n");
            _depth--;
            _sb.Append("ENDFUNC\n");

            _vars.Clear();
            _vars.AddRange(savedVars);
            _funcs.Add((name, paramCount));
        }

        private void EmitFuncCall()
        {
            if (_funcs.Count == 0)
            {
                _sb.Append($"{PickVar()} = {Expr(2)}\n");
                return;
            }
            var (name, paramCount) = _funcs[rng.Next(_funcs.Count)];
            var args = Enumerable.Range(0, paramCount).Select(_ => Lit());
            _sb.Append($"{PickVar()} = {name}({string.Join(", ", args)})\n");
        }

        // ---- 表达式 ----

        private string Cond()
        {
            // 两侧均为运行时值（变量池/字面量混合）；不引入除法
            var lhs = PickVar();
            var rhs = rng.Next(2) == 0 ? Lit() : PickVar();
            return rng.Next(2) == 0 ? $"{lhs} == {rhs}" : $"{lhs} != {rhs}";
        }

        /// <summary>算术表达式；除法恒安全（除数 = 平方 + 1 ≥ 1）。</summary>
        private string Expr(int depth)
        {
            if (depth <= 0)
                return rng.Next(2) == 0 ? Lit() : PickVar();
            var lhs = Expr(depth - 1);
            var rhs = Expr(depth - 1);
            return rng.Next(4) switch
            {
                0 => $"({lhs} + {rhs})",
                1 => $"({lhs} - {rhs})",
                2 => $"({lhs} * {rhs})",
                _ => $"({lhs} / (({rhs}) * ({rhs}) + 1))",
            };
        }

        private string Lit() => rng.Next(-9, 10).ToString();
    }
}