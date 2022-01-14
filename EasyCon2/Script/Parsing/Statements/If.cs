﻿using System.Text.RegularExpressions;

namespace EasyCon2.Script.Parsing.Statements
{
    class If : Statement
    {
        public static IStatementParser Parser = new StatementParser(Parse);
        public override int IndentNext => 1;
        public Else Else;
        public EndIf EndIf;
        public readonly CompareOperator Operater;
        public readonly ValVar Left;
        public readonly ValBase Right;

        public If(CompareOperator op, ValVar left, ValBase right)
        {
            Operater = op;
            Left = left;
            Right = right;
        }

        public static Statement Parse(ParserArgument args)
        {
            foreach (var op in CompareOperator.All)
            {
                var m = Regex.Match(args.Text, $@"^if\s+{Formats.VariableEx}\s*{op.Operator}\s*{Formats.ValueEx}$", RegexOptions.IgnoreCase);
                if (m.Success)
                    return new If(op, args.Formatter.GetVar(m.Groups[1].Value), args.Formatter.GetValueEx(m.Groups[2].Value));
            }
            return null;
        }

        public override void Exec(Processor processor)
        {
            if (Operater.Compare(Left.Get(processor), Right.Get(processor)))
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

        protected override string _GetString(Formatter formatter)
        {
            return $"IF {Left.GetCodeText(formatter)} {Operater.Operator} {Right.GetCodeText(formatter)}";
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            var left = Left as ValRegEx;
            if (left == null)
                throw new Assembly.AssembleException("外部变量仅限联机模式使用");
            if (Right is ValInstant)
            {
                assembler.Add(Assembly.Instructions.AsmMov.Create(Assembly.Assembler.IReg, Right));
                assembler.Add(Operater.Assemble(left.Index, Assembly.Assembler.IReg));
            }
            else
            {
                assembler.Add(Operater.Assemble(left.Index, (Right as ValReg).Index));
            }
            assembler.Add(Assembly.Instructions.AsmBranchFalse.Create());
            assembler.IfMapping[this] = assembler.Last() as Assembly.Instructions.AsmBranchFalse;
        }
    }

    class Else : Statement
    {
        public static IStatementParser Parser = new StatementParser(Parse);
        public override int IndentThis => -1;
        public override int IndentNext => 1;
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

        protected override string _GetString(Formatter formatter)
        {
            return "ELSE";
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            assembler.Add(Assembly.Instructions.AsmBranch.Create());
            assembler.ElseMapping[this] = assembler.Last() as Assembly.Instructions.AsmBranch;
            assembler.Add(Assembly.Instructions.AsmEmpty.Create());
            assembler.IfMapping[If].Target = assembler.Last();
        }
    }

    class EndIf : Statement
    {
        public static IStatementParser Parser = new StatementParser(Parse);
        public override int IndentThis => -1;
        public If If;

        public static Statement Parse(ParserArgument args)
        {
            if (args.Text.Equals("endif", StringComparison.OrdinalIgnoreCase))
                return new EndIf();
            return null;
        }

        public override void Exec(Processor processor)
        { }

        protected override string _GetString(Formatter formatter)
        {
            return "ENDIF";
        }

        public override void Assemble(Assembly.Assembler assembler)
        {
            assembler.Add(Assembly.Instructions.AsmEmpty.Create());
            if (If.Else == null)
                assembler.IfMapping[If].Target = assembler.Last();
            else
                assembler.ElseMapping[If.Else].Target = assembler.Last();
        }
    }
}