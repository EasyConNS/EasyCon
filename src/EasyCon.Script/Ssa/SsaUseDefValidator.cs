namespace EasyCon.Script.Ssa;

/// <summary>
/// Validates that every SSA operand has a live definition available at its use site.
/// This runs immediately before bytecode encoding so optimizer defects produce a
/// compiler diagnostic instead of an encoder slot lookup failure.
/// </summary>
internal static class SsaUseDefValidator
{
    internal static void ValidateProgram(SsaProgram program)
    {
        var functions = program.MainFunction == null
            ? program.Functions.Values
            : program.Functions.Values.Prepend(program.MainFunction);
        foreach (var function in functions.Distinct())
            ValidateFunction(function, "before bytecode encoding");
    }

    private static void ValidateFunction(SsaFunction function, string stage)
    {
        if (function.Blocks.Count == 0)
            throw new InvalidOperationException($"SSA validation failed {stage}: {function.Symbol.Name} has no entry block");

        var definitions = new Dictionary<SsaValue, (SsaBlock Block, int Index, bool IsPhi)>();
        foreach (var block in function.Blocks)
        {
            for (int i = 0; i < block.Phis.Count; i++)
                AddDefinition(block.Phis[i], (block, i, true));
            for (int i = 0; i < block.Instructions.Count; i++)
                AddDefinition(block.Instructions[i], (block, i, false));
        }

        var reachable = new HashSet<SsaBlock>();
        var pending = new Stack<SsaBlock>();
        pending.Push(function.Entry);
        while (pending.Count > 0)
        {
            var block = pending.Pop();
            if (!reachable.Add(block))
                continue;
            foreach (var successor in block.GetSuccessors())
                pending.Push(successor);
        }

        var dominators = Dominators.Compute(function);

        foreach (var block in function.Blocks)
        {
            foreach (var phi in block.Phis)
            {
                if (phi.ExtraArgs == null || phi.ExtraArgs.Count != block.Predecessors.Count)
                    throw new InvalidOperationException(
                        $"SSA validation failed {stage}: {function.Symbol.Name} b{block.Id} " +
                        $"v{phi.Id}:Phi has {phi.ExtraArgs?.Count ?? 0} inputs for {block.Predecessors.Count} predecessors");

                for (int i = 0; i < phi.ExtraArgs.Count; i++)
                    CheckUse(phi, phi.ExtraArgs[i], block.Predecessors[i], null, "phi input");
            }

            for (int i = 0; i < block.Instructions.Count; i++)
            {
                var instruction = block.Instructions[i];
                if (instruction.Op == SsaOp.Phi)
                    throw new InvalidOperationException(
                        $"SSA validation failed {stage}: {function.Symbol.Name} b{block.Id} " +
                        $"contains v{instruction.Id}:Phi in Instructions");

                CheckUse(instruction, instruction.Arg0, block, i, "Arg0");
                CheckUse(instruction, instruction.Arg1, block, i, "Arg1");
                if (instruction.ExtraArgs != null)
                    foreach (var value in instruction.ExtraArgs)
                        CheckUse(instruction, value, block, i, "ExtraArgs");
            }

            // A branch condition is consumed after the block's instructions have run.
            CheckUse(null, block.BranchCondition, block, null, "branch condition");
        }

        void AddDefinition(SsaValue value, (SsaBlock Block, int Index, bool IsPhi) definition)
        {
            if (!definitions.TryAdd(value, definition))
                throw new InvalidOperationException(
                    $"SSA validation failed {stage}: {function.Symbol.Name} defines v{value.Id}:{value.Op} more than once");
        }

        void CheckUse(
            SsaValue? consumer,
            SsaValue? value,
            SsaBlock useBlock,
            int? useIndex,
            string operand)
        {
            if (value == null || value.IsConstant)
                return;

            string consumerLabel = consumer == null ? "terminator" : $"v{consumer.Id}:{consumer.Op}";
            if (!definitions.TryGetValue(value, out var definition))
                throw new InvalidOperationException(
                    $"SSA validation failed {stage}: {function.Symbol.Name} b{useBlock.Id} " +
                    $"{consumerLabel} {operand} references missing v{value.Id}:{value.Op}");

            if (definition.Block == useBlock)
            {
                if (useIndex.HasValue && !definition.IsPhi && definition.Index >= useIndex.Value)
                    throw new InvalidOperationException(
                        $"SSA validation failed {stage}: {function.Symbol.Name} b{useBlock.Id} " +
                        $"{consumerLabel} {operand} uses v{value.Id}:{value.Op} before its definition");
                return;
            }

            if (reachable.Contains(useBlock) && !dominators.Dominates(definition.Block, useBlock))
                throw new InvalidOperationException(
                    $"SSA validation failed {stage}: {function.Symbol.Name} b{useBlock.Id} " +
                    $"{consumerLabel} {operand} uses v{value.Id}:{value.Op} from non-dominating b{definition.Block.Id}");
        }
    }
}