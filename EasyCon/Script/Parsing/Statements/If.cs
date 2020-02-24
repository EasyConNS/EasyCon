using EasyCon.Script.Assembly;
using EasyCon.Script.Assembly.Instructions;
using EasyCon.Script.Parsing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EasyCon.Script.Parsing.Statements
{
    class If : Statement
    {
        public class CompareOperator
        {
            public static readonly List<CompareOperator> All = new List<CompareOperator>();
            public readonly string Operator;
            public readonly Func<int, int, bool> Compare;
            public readonly Func<uint, uint, Instruction> Assemble;

            public CompareOperator(string op, Func<int, int, bool> compare, Func<uint, uint, Instruction> assemble)
            {
                Operator = op;
                Compare = compare;
                Assemble = assemble;
                All.Add(this);
            }

            public static readonly CompareOperator Equal = new CompareOperator("=", (v0, v1) => v0 == v1, (r0, r1) => AsmEqual.Create(AsmCompare.AssignType.Assign, r0, r1));
            public static readonly CompareOperator NotEqual = new CompareOperator("!=", (v0, v1) => v0 != v1, (r0, r1) => AsmNotEqual.Create(AsmCompare.AssignType.Assign, r0, r1));
            public static readonly CompareOperator LessThan = new CompareOperator("<", (v0, v1) => v0 < v1, (r0, r1) => AsmLessThan.Create(AsmCompare.AssignType.Assign, r0, r1));
            public static readonly CompareOperator LessOrEqual = new CompareOperator("<=", (v0, v1) => v0 <= v1, (r0, r1) => AsmLessOrEqual.Create(AsmCompare.AssignType.Assign, r0, r1));
            public static readonly CompareOperator GreaterThan = new CompareOperator(">", (v0, v1) => v0 > v1, (r0, r1) => AsmLessThan.Create(AsmCompare.AssignType.Assign, r1, r0));
            public static readonly CompareOperator GreaterOrEqual = new CompareOperator(">=", (v0, v1) => v0 >= v1, (r0, r1) => AsmLessOrEqual.Create(AsmCompare.AssignType.Assign, r1, r0));
        }

        public static IStatementParser Parser = new StatementParser(Parse);
        public Else Else;
        public EndIf EndIf;
        public readonly CompareOperator Operater;
        public readonly ValReg Left;
        public readonly ValBase Right;

        public If(CompareOperator op, ValReg left, ValBase right)
        {
            Operater = op;
            Left = left;
            Right = right;
        }

        public static Statement Parse(ParserArgument args)
        {
            foreach (var op in CompareOperator.All)
            {
                var m = Regex.Match(args.Text, $@"^if\s+{Formats.Register}\s*{op.Operator}\s*{Formats.Value}$", RegexOptions.IgnoreCase);
                if (m.Success)
                    return new If(op, args.Formatter.GetReg(m.Groups[1].Value), args.Formatter.GetValue(m.Groups[2].Value));
            }
            return null;
        }

        public override void Exec(Processor processor)
        {
            if (Operater.Compare(Left.Evaluate(processor), Right.Evaluate(processor)))
            {
                // do nothing
            }
            else
            {
                // jump
                if (Else != null)
                    processor.PC = Else.Address + 1;
                else
                    processor.PC = EndIf.Address + 1;
            }
        }

        protected override string _GetString(Formats.Formatter formatter)
        {
            return $"IF {Left.GetCodeText(formatter)} {Operater.Operator} {Right.GetCodeText(formatter)}";
        }

        public override void Assemble(Assembler assembler)
        {
            if (Right is ValInstant)
            {
                assembler.Add(AsmMov.Create(Assembler.IReg, Right));
                assembler.Add(Operater.Assemble(Left.Index, Assembler.IReg));
            }
            else
            {
                assembler.Add(Operater.Assemble(Left.Index, (Right as ValReg).Index));
            }
            assembler.Add(AsmBranchFalse.Create());
            assembler.IfMapping[this] = assembler.Last() as AsmBranchFalse;
        }
    }

    class Else : Statement
    {
        public static IStatementParser Parser = new StatementParser(Parse);
        public If If;

        public static Statement Parse(ParserArgument args)
        {
            if (args.Text.Equals("else", StringComparison.OrdinalIgnoreCase))
                return new Else();
            return null;
        }

        public override void Exec(Processor processor)
        {
            // end of if-block
            processor.PC = If.EndIf.Address + 1;
        }

        protected override string _GetString(Formats.Formatter formatter)
        {
            return "ELSE";
        }

        public override void Assemble(Assembler assembler)
        {
            assembler.Add(AsmBranch.Create());
            assembler.ElseMapping[this] = assembler.Last() as AsmBranch;
            assembler.Add(AsmEmpty.Create());
            assembler.IfMapping[If].Target = assembler.Last();
        }
    }

    class EndIf : Statement
    {
        public static IStatementParser Parser = new StatementParser(Parse);
        public If If;

        public static Statement Parse(ParserArgument args)
        {
            if (args.Text.Equals("endif", StringComparison.OrdinalIgnoreCase))
                return new EndIf();
            return null;
        }

        public override void Exec(Processor processor)
        { }

        protected override string _GetString(Formats.Formatter formatter)
        {
            return "ENDIF";
        }

        public override void Assemble(Assembler assembler)
        {
            assembler.Add(AsmEmpty.Create());
            if (If.Else == null)
                assembler.IfMapping[If].Target = assembler.Last();
            else
                assembler.ElseMapping[If.Else].Target = assembler.Last();
        }
    }
}
