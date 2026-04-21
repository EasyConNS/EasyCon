using EasyCon.Script.Symbols;
using System.CodeDom.Compiler;
using static EasyCon.Script.Binding.BoundNodeKind;

namespace EasyCon.Script.Binding;

internal sealed class ControlFlowGraph
{
    private ControlFlowGraph(BasicBlock start, BasicBlock end, List<BasicBlock> blocks, List<BasicBlockBranch> branches)
    {
        Start = start;
        End = end;
        Blocks = blocks;
        Branches = branches;
    }

    public BasicBlock Start { get; }
    public BasicBlock End { get; }
    public List<BasicBlock> Blocks { get; }
    public List<BasicBlockBranch> Branches { get; }

    public sealed class BasicBlock
    {
        public BasicBlock()
        {
        }

        public BasicBlock(bool isStart)
        {
            IsStart = isStart;
            IsEnd = !isStart;
        }

        public bool IsStart { get; }
        public bool IsEnd { get; }
        public List<BoundStmt> Statements { get; } = new List<BoundStmt>();
        public List<BasicBlockBranch> Incoming { get; } = new List<BasicBlockBranch>();
        public List<BasicBlockBranch> Outgoing { get; } = new List<BasicBlockBranch>();

        public override string ToString()
        {
            if (IsStart)
                return "<Start>";

            if (IsEnd)
                return "<End>";

            using (var writer = new StringWriter())
            using (var indentedWriter = new IndentedTextWriter(writer))
            {
                foreach (var statement in Statements)
                    indentedWriter.Write($"{statement}");

                return writer.ToString();
            }
        }
    }

    public sealed class BasicBlockBranch
    {
        public BasicBlockBranch(BasicBlock from, BasicBlock to, BoundExpr? condition)
        {
            From = from;
            To = to;
            Condition = condition;
        }

        public BasicBlock From { get; }
        public BasicBlock To { get; }
        public BoundExpr? Condition { get; }

        public override string ToString()
        {
            if (Condition == null)
                return string.Empty;

            return Condition!.ToString() ?? string.Empty;
        }
    }

    public sealed class BasicBlockBuilder
    {
        private List<BoundStmt> _statements = new List<BoundStmt>();
        private List<BasicBlock> _blocks = new List<BasicBlock>();

        public List<BasicBlock> Build(BoundBlockStatement block)
        {
            foreach (var statement in block.Statements)
            {
                switch (statement.Kind)
                {
                    case Label:
                        StartBlock();
                        _statements.Add(statement);
                        break;
                    case GotoStatement:
                    case ConditionGotoStatement:
                    case Return:
                        _statements.Add(statement);
                        StartBlock();
                        break;
                    case NopStatement:
                    case KeyAction:
                    case StickAction:
                    case VariableDeclaration:
                    case ExpressionStatement:
                        _statements.Add(statement);
                        break;
                    default:
                        throw new Exception($"Unexpected statement: {statement.Kind}");
                }
            }

            EndBlock();

            return _blocks.ToList();
        }

        private void StartBlock()
        {
            EndBlock();
        }

        private void EndBlock()
        {
            if (_statements.Count > 0)
            {
                var block = new BasicBlock();
                block.Statements.AddRange(_statements);
                _blocks.Add(block);
                _statements.Clear();
            }
        }
    }

    public sealed class GraphBuilder
    {
        private Dictionary<BoundStmt, BasicBlock> _blockFromStatement = new Dictionary<BoundStmt, BasicBlock>();
        private Dictionary<BoundLabel, BasicBlock> _blockFromLabel = new Dictionary<BoundLabel, BasicBlock>();
        private List<BasicBlockBranch> _branches = new List<BasicBlockBranch>();
        private BasicBlock _start = new BasicBlock(isStart: true);
        private BasicBlock _end = new BasicBlock(isStart: false);

        public ControlFlowGraph Build(List<BasicBlock> blocks)
        {
            if (!blocks.Any())
                Connect(_start, _end);
            else
                Connect(_start, blocks.First());

            foreach (var block in blocks)
            {
                foreach (var statement in block.Statements)
                {
                    _blockFromStatement.Add(statement, block);
                    if (statement is BoundLabelStatement labelStatement)
                        _blockFromLabel.Add(labelStatement.Label, block);
                }
            }

            for (int i = 0; i < blocks.Count; i++)
            {
                var current = blocks[i];
                var next = i == blocks.Count - 1 ? _end : blocks[i + 1];

                foreach (var statement in current.Statements)
                {
                    var isLastStatementInBlock = statement == current.Statements.Last();
                    switch (statement.Kind)
                    {
                        case GotoStatement:
                            var gs = (BoundGotoStatement)statement;
                            var toBlock = _blockFromLabel[gs.Label];
                            Connect(current, toBlock);
                            break;
                        case ConditionGotoStatement:
                            var cgs = (BoundConditionalGotoStatement)statement;
                            var thenBlock = _blockFromLabel[cgs.Label];
                            var elseBlock = next;
                            var negatedCondition = Negate(cgs.Condition);
                            var thenCondition = cgs.JumpIfTrue ? cgs.Condition : negatedCondition;
                            var elseCondition = cgs.JumpIfTrue ? negatedCondition : cgs.Condition;
                            Connect(current, thenBlock, thenCondition);
                            Connect(current, elseBlock, elseCondition);
                            break;
                        case Return:
                            Connect(current, _end);
                            break;
                        case NopStatement:
                        case VariableDeclaration:
                        case Label:
                        case KeyAction:
                        case StickAction:
                        case ExpressionStatement:
                            if (isLastStatementInBlock)
                                Connect(current, next);
                            break;
                        default:
                            throw new Exception($"Unexpected statement: {statement.Kind}");
                    }
                }
            }

        ScanAgain:
            foreach (var block in blocks)
            {
                if (!block.Incoming.Any())
                {
                    RemoveBlock(blocks, block);
                    goto ScanAgain;
                }
            }

            blocks.Insert(0, _start);
            blocks.Add(_end);

            return new ControlFlowGraph(_start, _end, blocks, _branches);
        }

        private void Connect(BasicBlock from, BasicBlock to, BoundExpr? condition = null)
        {
            if (condition is BoundLiteralExpression l)
            {
                if (l.ConstantValue != Value.Void && l.ConstantValue.ToBoolean())
                {
                    condition = null;
                }
                else
                    return;
            }

            var branch = new BasicBlockBranch(from, to, condition);
            from.Outgoing.Add(branch);
            to.Incoming.Add(branch);
            _branches.Add(branch);
        }

        private void RemoveBlock(List<BasicBlock> blocks, BasicBlock block)
        {
            foreach (var branch in block.Incoming)
            {
                branch.From.Outgoing.Remove(branch);
                _branches.Remove(branch);
            }

            foreach (var branch in block.Outgoing)
            {
                branch.To.Incoming.Remove(branch);
                _branches.Remove(branch);
            }

            blocks.Remove(block);
        }

        private BoundExpr Negate(BoundExpr condition)
        {
            var negated = BoundFactory.Not(condition.Syntax, condition);
            if (negated.ConstantValue != Value.Void)
                return new BoundLiteralExpression(condition.Syntax, negated.ConstantValue);

            return negated;
        }
    }

    public void WriteTo(TextWriter writer)
    {
        string Quote(string text)
        {
            return "\"" + text.TrimEnd().Replace("\\", "\\\\").Replace("\"", "\\\"").Replace(Environment.NewLine, "\\l") + "\"";
        }

        writer.WriteLine("digraph G {");

        var blockIds = new Dictionary<BasicBlock, string>();

        for (int i = 0; i < Blocks.Count; i++)
        {
            var id = $"N{i}";
            blockIds.Add(Blocks[i], id);
        }

        foreach (var block in Blocks)
        {
            var id = blockIds[block];
            var label = Quote(block.ToString());
            writer.WriteLine($"    {id} [label = {label}, shape = box]");
        }

        foreach (var branch in Branches)
        {
            var fromId = blockIds[branch.From];
            var toId = blockIds[branch.To];
            var label = Quote(branch.ToString());
            writer.WriteLine($"    {fromId} -> {toId} [label = {label}]");
        }

        writer.WriteLine("}");
    }

    public static ControlFlowGraph Create(BoundBlockStatement body)
    {
        var basicBlockBuilder = new BasicBlockBuilder();
        var blocks = basicBlockBuilder.Build(body);

        var graphBuilder = new GraphBuilder();
        return graphBuilder.Build(blocks);
    }

    public static bool AllPathsReturn(BoundBlockStatement body)
    {
        var graph = Create(body);

        foreach (var branch in graph.End.Incoming)
        {
            var lastStatement = branch.From.Statements.LastOrDefault();
            if (lastStatement == null || lastStatement.Kind != Return)
                return false;
        }

        return true;
    }
}