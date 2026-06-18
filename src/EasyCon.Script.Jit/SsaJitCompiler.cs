using System.Text;
using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace EasyCon.Script.Jit;

/// <summary>
/// SSA IR → C# 源码 → Roslyn 运行时编译 JIT。
/// </summary>
public static class SsaJitCompiler
{
    public static Func<int> CompileToDelegate(SsaProgram program, object evaluator, SsaValue[] ops)
    {
        var source = GenerateCSharp(program);
        var coreAssembly = evaluator.GetType().Assembly;
        var assembly = CompileSource(source, coreAssembly);
        var type = assembly.GetType("JitProgram")!;

        var evalField = type.GetField("Evaluator")!;
        evalField.SetValue(null, evaluator);

        var opsField = type.GetField("Ops")!;
        opsField.SetValue(null, ops);

        var method = type.GetMethod("Run")!;
        return (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), method);
    }

    public static string GenerateCSharp(SsaProgram program)
    {
        var func = program.MainFunction!;
        var blocks = func.Blocks;

        var blockIndex = new Dictionary<SsaBlock, int>();
        for (int i = 0; i < blocks.Count; i++)
            blockIndex[blocks[i]] = i;

        var sb = new StringBuilder();
        sb.AppendLine("using EasyCon.Core.Runner;");
        sb.AppendLine("using EasyCon.Script.Ssa;");
        sb.AppendLine("public static class JitProgram");
        sb.AppendLine("{");
        sb.AppendLine("    public static SsaEvaluator Evaluator = null!;");
        sb.AppendLine("    public static SsaValue[] Ops = null!;");
        sb.AppendLine("    public static int Run()");
        sb.AppendLine("    {");

        var globalNames = new HashSet<string>();
        var localNames = new HashSet<string>();
        foreach (var block in blocks)
        {
            foreach (var inst in block.Instructions)
            {
                if (inst.Op is SsaOp.LoadGlobal or SsaOp.StoreGlobal && inst.Aux is VariableSymbol vs)
                    globalNames.Add(vs.Name);
                if (inst.Op is SsaOp.LoadLocal or SsaOp.StoreLocal && inst.Aux is VariableSymbol lvs)
                    localNames.Add(lvs.Name);
            }
        }
        foreach (var name in globalNames)
            sb.AppendLine($"        int g_{Sanitize(name)} = 0;");
        foreach (var name in localNames)
            sb.AppendLine($"        int l_{Sanitize(name)} = 0;");

        var allValues = new HashSet<int>();
        foreach (var block in blocks)
        {
            foreach (var phi in block.Phis)
                allValues.Add(phi.Id);
            foreach (var inst in block.Instructions)
            {
                if (!inst.IsConstant)
                    allValues.Add(inst.Id);
            }
        }
        var valueTypes = new Dictionary<int, string>();
        foreach (var block in blocks)
        {
            foreach (var phi in block.Phis)
                valueTypes[phi.Id] = CSharpType(phi);
            foreach (var inst in block.Instructions)
            {
                if (!inst.IsConstant)
                    valueTypes[inst.Id] = CSharpType(inst);
            }
        }
        foreach (var id in allValues.OrderBy(x => x))
        {
            var type = valueTypes.GetValueOrDefault(id, "int");
            sb.AppendLine($"        {type} v{id} = default;");
        }

        foreach (var block in blocks)
        {
            var idx = blockIndex[block];
            sb.AppendLine();
            sb.AppendLine($"        // BB{idx}{(block.Predecessors.Count > 0 ? $" (preds: {string.Join(", ", block.Predecessors.Select(p => $"BB{blockIndex[p]}"))})" : " (entry)")}");
            sb.AppendLine($"        BB{idx}:");

            foreach (var inst in block.Instructions)
            {
                if (inst.IsConstant) continue;
                GenerateInstruction(sb, inst);
            }

            GenerateTerminator(sb, block, blockIndex);
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void GenerateInstruction(StringBuilder sb, SsaValue inst)
    {
        switch (inst.Op)
        {
            case SsaOp.ConstInt:    sb.AppendLine($"        v{inst.Id} = {inst.Const.GetInt()};"); break;
            case SsaOp.ConstBool:   sb.AppendLine($"        v{inst.Id} = {(inst.Const.GetBool() ? 1 : 0)};"); break;
            case SsaOp.ConstByte:   sb.AppendLine($"        v{inst.Id} = {inst.Const.GetByte()};"); break;
            case SsaOp.ConstUInt:   sb.AppendLine($"        v{inst.Id} = unchecked((int){inst.Const.GetUInt()}u);"); break;
            case SsaOp.ConstUInt64: sb.AppendLine($"        v{inst.Id} = unchecked((long){inst.Const.GetUInt64()}UL);"); break;
            case SsaOp.ConstDouble: sb.AppendLine(Culture($"        v{inst.Id} = {inst.Const.GetDouble():G17}d;", inst.Const.GetDouble())); break;
            case SsaOp.ConstString: sb.AppendLine($"        v{inst.Id} = \"{Escape(inst.ConstString ?? "")}\";"); break;
            case SsaOp.ConstPtr:    sb.AppendLine($"        v{inst.Id} = {inst.Const.GetPtr()}L;"); break;

            case SsaOp.LoadGlobal:  sb.AppendLine($"        v{inst.Id} = g_{Sanitize((inst.Aux as VariableSymbol)!.Name)};"); break;
            case SsaOp.StoreGlobal: sb.AppendLine($"        g_{Sanitize((inst.Aux as VariableSymbol)!.Name)} = {Fmt(inst.Arg0)};"); break;
            case SsaOp.LoadLocal:   sb.AppendLine($"        v{inst.Id} = l_{Sanitize((inst.Aux as VariableSymbol)!.Name)};"); break;
            case SsaOp.StoreLocal:  sb.AppendLine($"        l_{Sanitize((inst.Aux as VariableSymbol)!.Name)} = {Fmt(inst.Arg0)};"); break;

            case SsaOp.AddInt: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} + {Fmt(inst.Arg1)};"); break;
            case SsaOp.SubInt: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} - {Fmt(inst.Arg1)};"); break;
            case SsaOp.MulInt: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} * {Fmt(inst.Arg1)};"); break;
            case SsaOp.DivInt: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} / {Fmt(inst.Arg1)};"); break;
            case SsaOp.ModInt: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} % {Fmt(inst.Arg1)};"); break;
            case SsaOp.RoundDivInt:
                sb.AppendLine($"        v{inst.Id} = ({Fmt(inst.Arg0)} + {Fmt(inst.Arg1)} / 2) / {Fmt(inst.Arg1)};"); break;

            case SsaOp.AddUInt: sb.AppendLine($"        v{inst.Id} = unchecked((int)((uint){Fmt(inst.Arg0)} + (uint){Fmt(inst.Arg1)}));"); break;
            case SsaOp.SubUInt: sb.AppendLine($"        v{inst.Id} = unchecked((int)((uint){Fmt(inst.Arg0)} - (uint){Fmt(inst.Arg1)}));"); break;
            case SsaOp.MulUInt: sb.AppendLine($"        v{inst.Id} = unchecked((int)((uint){Fmt(inst.Arg0)} * (uint){Fmt(inst.Arg1)}));"); break;
            case SsaOp.DivUInt: sb.AppendLine($"        v{inst.Id} = unchecked((int)((uint){Fmt(inst.Arg0)} / (uint){Fmt(inst.Arg1)}));"); break;
            case SsaOp.ModUInt: sb.AppendLine($"        v{inst.Id} = unchecked((int)((uint){Fmt(inst.Arg0)} % (uint){Fmt(inst.Arg1)}));"); break;

            case SsaOp.AddDouble: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} + {Fmt(inst.Arg1)};"); break;
            case SsaOp.SubDouble: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} - {Fmt(inst.Arg1)};"); break;
            case SsaOp.MulDouble: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} * {Fmt(inst.Arg1)};"); break;
            case SsaOp.DivDouble: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} / {Fmt(inst.Arg1)};"); break;

            case SsaOp.AddUInt64: sb.AppendLine($"        v{inst.Id} = unchecked((long)((ulong){Fmt(inst.Arg0)} + (ulong){Fmt(inst.Arg1)}));"); break;
            case SsaOp.SubUInt64: sb.AppendLine($"        v{inst.Id} = unchecked((long)((ulong){Fmt(inst.Arg0)} - (ulong){Fmt(inst.Arg1)}));"); break;
            case SsaOp.MulUInt64: sb.AppendLine($"        v{inst.Id} = unchecked((long)((ulong){Fmt(inst.Arg0)} * (ulong){Fmt(inst.Arg1)}));"); break;
            case SsaOp.DivUInt64: sb.AppendLine($"        v{inst.Id} = unchecked((long)((ulong){Fmt(inst.Arg0)} / (ulong){Fmt(inst.Arg1)}));"); break;
            case SsaOp.ModUInt64: sb.AppendLine($"        v{inst.Id} = unchecked((long)((ulong){Fmt(inst.Arg0)} % (ulong){Fmt(inst.Arg1)}));"); break;

            case SsaOp.AndInt: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} & {Fmt(inst.Arg1)};"); break;
            case SsaOp.OrInt:  sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} | {Fmt(inst.Arg1)};"); break;
            case SsaOp.XorInt: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} ^ {Fmt(inst.Arg1)};"); break;
            case SsaOp.ShlInt: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} << {Fmt(inst.Arg1)};"); break;
            case SsaOp.ShrInt: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} >> {Fmt(inst.Arg1)};"); break;
            case SsaOp.NotInt: sb.AppendLine($"        v{inst.Id} = ~{Fmt(inst.Arg0)};"); break;

            case SsaOp.EqInt:  sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} == {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.NeqInt: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} != {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.LtInt:  sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} < {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.LeqInt: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} <= {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.GtInt:  sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} > {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.GeqInt: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} >= {Fmt(inst.Arg1)} ? 1 : 0;"); break;

            case SsaOp.EqUInt:  sb.AppendLine($"        v{inst.Id} = (uint){Fmt(inst.Arg0)} == (uint){Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.NeqUInt: sb.AppendLine($"        v{inst.Id} = (uint){Fmt(inst.Arg0)} != (uint){Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.LtUInt:  sb.AppendLine($"        v{inst.Id} = (uint){Fmt(inst.Arg0)} < (uint){Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.LeqUInt: sb.AppendLine($"        v{inst.Id} = (uint){Fmt(inst.Arg0)} <= (uint){Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.GtUInt:  sb.AppendLine($"        v{inst.Id} = (uint){Fmt(inst.Arg0)} > (uint){Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.GeqUInt: sb.AppendLine($"        v{inst.Id} = (uint){Fmt(inst.Arg0)} >= (uint){Fmt(inst.Arg1)} ? 1 : 0;"); break;

            case SsaOp.EqDouble:  sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} == {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.NeqDouble: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} != {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.LtDouble:  sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} < {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.LeqDouble: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} <= {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.GtDouble:  sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} > {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.GeqDouble: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} >= {Fmt(inst.Arg1)} ? 1 : 0;"); break;

            case SsaOp.EqUInt64:  sb.AppendLine($"        v{inst.Id} = (ulong){Fmt(inst.Arg0)} == (ulong){Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.NeqUInt64: sb.AppendLine($"        v{inst.Id} = (ulong){Fmt(inst.Arg0)} != (ulong){Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.LtUInt64:  sb.AppendLine($"        v{inst.Id} = (ulong){Fmt(inst.Arg0)} < (ulong){Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.LeqUInt64: sb.AppendLine($"        v{inst.Id} = (ulong){Fmt(inst.Arg0)} <= (ulong){Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.GtUInt64:  sb.AppendLine($"        v{inst.Id} = (ulong){Fmt(inst.Arg0)} > (ulong){Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.GeqUInt64: sb.AppendLine($"        v{inst.Id} = (ulong){Fmt(inst.Arg0)} >= (ulong){Fmt(inst.Arg1)} ? 1 : 0;"); break;

            case SsaOp.EqBool:  sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} == {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.NeqBool: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} != {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.EqByte:  sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} == {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.NeqByte: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} != {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.LtByte:  sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} < {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.LeqByte: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} <= {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.GtByte:  sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} > {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.GeqByte: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} >= {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.EqPtr:   sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} == {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.NeqPtr:  sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} != {Fmt(inst.Arg1)} ? 1 : 0;"); break;

            case SsaOp.EqString:
                sb.AppendLine($"        v{inst.Id} = string.Equals({Fmt(inst.Arg0)}, {Fmt(inst.Arg1)}, StringComparison.Ordinal) ? 1 : 0;"); break;
            case SsaOp.NeqString:
                sb.AppendLine($"        v{inst.Id} = !string.Equals({Fmt(inst.Arg0)}, {Fmt(inst.Arg1)}, StringComparison.Ordinal) ? 1 : 0;"); break;

            case SsaOp.LogicNot:
                sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)} == 0 ? 1 : 0;"); break;

            case SsaOp.ConvBoolToInt:   sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvByteToInt:   sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvIntToUInt:   sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvIntToUInt64: sb.AppendLine($"        v{inst.Id} = (long){Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvIntToDouble: sb.AppendLine($"        v{inst.Id} = (double){Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvIntToByte:   sb.AppendLine($"        v{inst.Id} = (byte){Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvIntToPtr:    sb.AppendLine($"        v{inst.Id} = (long){Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvUIntToUInt64: sb.AppendLine($"        v{inst.Id} = (long)(uint){Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvUInt64ToPtr: sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvPtrToInt:    sb.AppendLine($"        v{inst.Id} = (int){Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvDoubleToInt: sb.AppendLine($"        v{inst.Id} = (int){Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvUInt64ToInt: sb.AppendLine($"        v{inst.Id} = unchecked((int)(ulong){Fmt(inst.Arg0)});"); break;
            case SsaOp.ConvToInt:       sb.AppendLine($"        v{inst.Id} = {Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvToString:
                SyncArgs(sb, inst);
                sb.AppendLine($"        Evaluator.ExecuteInstructionPublic(Ops[{inst.Id}]);");
                ReadResult(sb, inst);
                break;

            case SsaOp.Phi:
            case SsaOp.CondBranch:
            case SsaOp.Branch:
            case SsaOp.Return:
                break;

            case SsaOp.Call:
            case SsaOp.StaticCall:
                GenerateCall(sb, inst);
                break;

            // Tier 3/4 — 内联缓存同步，回调解释器
            case SsaOp.ArrayInit:
            case SsaOp.LoadIndex:
            case SsaOp.StoreIndex:
            case SsaOp.Slice:
            case SsaOp.ArrayLen:
            case SsaOp.Contains:
            case SsaOp.Concat:
            case SsaOp.ArrayAppend:
            case SsaOp.DeepCopy:
            case SsaOp.StructInit:
            case SsaOp.LoadField:
            case SsaOp.StoreField:
            case SsaOp.LoadFieldIndex:
            case SsaOp.StoreFieldIndex:
            case SsaOp.KeyAction:
            case SsaOp.KeyPress:
            case SsaOp.StickAction:
            case SsaOp.StickPress:
            case SsaOp.Wait:
            case SsaOp.Rand:
            case SsaOp.OcrInit:
            case SsaOp.Capture:
            case SsaOp.Ocr:
            case SsaOp.Roi:
            case SsaOp.RuntimeValue:
            case SsaOp.ImageLabel:
            case SsaOp.Nop:
                SyncArgs(sb, inst);
                sb.AppendLine($"        Evaluator.ExecuteInstructionPublic(Ops[{inst.Id}]);");
                ReadResult(sb, inst);
                break;

            default:
                sb.AppendLine($"        // [UNSUPPORTED] v{inst.Id} = {inst.Op}");
                break;
        }
    }

    private static void GenerateCall(StringBuilder sb, SsaValue inst)
    {
        var args = new List<SsaValue>();
        if (inst.Arg0 != null) args.Add(inst.Arg0);
        if (inst.ExtraArgs != null) args.AddRange(inst.ExtraArgs);

        var argCount = args.Count;
        var argStr = string.Join(", ", args.Select(a => Fmt(a)));

        if (argCount <= 4 && args.All(a => IsIntType(a)))
        {
            var method = argCount switch
            {
                0 => "CallInt0",
                1 => "CallInt1",
                2 => "CallInt2",
                3 => "CallInt3",
                4 => "CallInt4",
                _ => "CallInt4"
            };
            sb.AppendLine($"        v{inst.Id} = SsaJitInterop.{method}(Evaluator, Ops, {inst.Id}, {argStr});");
        }
        else
        {
            SyncArgs(sb, inst);
            sb.AppendLine($"        Evaluator.ExecuteInstructionPublic(Ops[{inst.Id}]);");
            ReadResult(sb, inst);
        }
    }

    private static bool IsIntType(SsaValue v)
    {
        if (v.IsConstant)
            return v.Op is SsaOp.ConstInt or SsaOp.ConstBool or SsaOp.ConstByte or SsaOp.ConstUInt;
        return v.Type == ScriptType.Int || v.Type == ScriptType.Bool
            || v.Type == ScriptType.Byte || v.Type == ScriptType.UInt;
    }

    private static void SyncArgs(StringBuilder sb, SsaValue inst)
    {
        SyncArg(sb, inst.Arg0);
        SyncArg(sb, inst.Arg1);
        if (inst.ExtraArgs != null)
            foreach (var arg in inst.ExtraArgs)
                SyncArg(sb, arg);
    }

    private static void SyncArg(StringBuilder sb, SsaValue? arg)
    {
        if (arg == null || arg.IsConstant) return;
        var cache = CacheName(arg);
        sb.AppendLine($"        Evaluator.{cache}[{arg.Id}] = v{arg.Id};");
    }

    private static void ReadResult(StringBuilder sb, SsaValue inst)
    {
        var cache = CacheName(inst);
        sb.AppendLine($"        v{inst.Id} = Evaluator.{cache}[{inst.Id}];");
    }

    private static string CacheName(SsaValue v)
    {
        if (v.Type == ScriptType.Double) return "DoubleCache";
        if (v.Type == ScriptType.UInt64 || v.Type == ScriptType.Ptr) return "LongCache";
        if (v.Type == ScriptType.String || v.Type is ArrayType or StructType) return "ObjCache";
        return "IntCache";
    }

    private static void GenerateTerminator(StringBuilder sb, SsaBlock block, Dictionary<SsaBlock, int> blockIndex)
    {
        foreach (var succ in block.GetSuccessors())
        {
            int predIdx = succ.Predecessors.IndexOf(block);
            if (predIdx < 0) continue;

            foreach (var phi in succ.Phis)
            {
                if (phi.ExtraArgs == null || predIdx >= phi.ExtraArgs.Count) continue;
                var arm = phi.ExtraArgs[predIdx];
                sb.AppendLine($"        v{phi.Id} = {Fmt(arm)};  // phi for BB{blockIndex[succ]}");
            }
        }

        if (block.IsReturn)
        {
            var retInst = block.Instructions.LastOrDefault(i => i.Op == SsaOp.Return);
            if (retInst?.Arg0 != null)
            {
                var retVal = ResolveReturnValue(retInst.Arg0);
                sb.AppendLine($"        return (int)({Fmt(retVal)});");
            }
            else
            {
                var printCall = block.Instructions.LastOrDefault(i => i.Op == SsaOp.StaticCall);
                if (printCall?.Arg0 != null)
                {
                    var retVal = ResolveReturnValue(printCall.Arg0);
                    sb.AppendLine($"        return (int)({Fmt(retVal)});  // from PRINT arg");
                }
                else
                {
                    sb.AppendLine("        return 0;");
                }
            }
        }
        else if (block.BranchCondition != null)
        {
            var cond = block.BranchCondition;
            sb.AppendLine($"        if (v{cond.Id} != 0) goto BB{blockIndex[block.TrueSuccessor!]}; else goto BB{blockIndex[block.FalseSuccessor!]};");
        }
        else if (block.JumpTarget != null)
        {
            sb.AppendLine($"        goto BB{blockIndex[block.JumpTarget]};");
        }
    }

    private static string Fmt(SsaValue? v)
    {
        if (v == null) return "0";
        if (v.IsConstant)
        {
            return v.Op switch
            {
                SsaOp.ConstInt    => v.Const.GetInt().ToString(),
                SsaOp.ConstBool   => v.Const.GetBool() ? "1" : "0",
                SsaOp.ConstByte   => v.Const.GetByte().ToString(),
                SsaOp.ConstUInt   => $"unchecked((int){v.Const.GetUInt()}u)",
                SsaOp.ConstUInt64 => $"unchecked((long){v.Const.GetUInt64()}UL)",
                SsaOp.ConstDouble => $"{v.Const.GetDouble():G17}d",
                SsaOp.ConstString => $"\"{Escape(v.ConstString ?? "")}\"",
                SsaOp.ConstPtr    => $"{v.Const.GetPtr()}L",
                _ => "0"
            };
        }
        return $"v{v.Id}";
    }

    private static string CSharpType(SsaValue v)
    {
        if (v.Type == ScriptType.Double) return "double";
        if (v.Type == ScriptType.UInt64 || v.Type == ScriptType.Ptr) return "long";
        if (v.Type == ScriptType.String) return "string";
        return "int";
    }

    private static string Sanitize(string name) => name.TrimStart('$');

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

    private static SsaValue ResolveReturnValue(SsaValue v)
    {
        if (v.Op == SsaOp.ConvToString)
        {
            if (v.Arg0 != null)
                return ResolveReturnValue(v.Arg0);
        }
        return v;
    }

    private static string Culture(string template, double value) =>
        template.Replace($"{value:G17}d", $"{value:G17}d");

    private static int _assemblyCounter;

    private static Assembly CompileSource(string source, Assembly coreAssembly)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToList();

        if (!string.IsNullOrEmpty(coreAssembly.Location))
            references.Add(MetadataReference.CreateFromFile(coreAssembly.Location));

        var name = $"JitAssembly_{System.Threading.Interlocked.Increment(ref _assemblyCounter)}";
        var compilation = CSharpCompilation.Create(
            name,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));

        using var ms = new System.IO.MemoryStream();
        var result = compilation.Emit(ms);

        if (!result.Success)
        {
            var errors = string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            throw new InvalidOperationException($"JIT compilation failed:\n{source}\n\nErrors:\n{errors}");
        }

        ms.Seek(0, System.IO.SeekOrigin.Begin);
        return Assembly.Load(ms.ToArray());
    }
}