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
        var sb = new StringBuilder();
        sb.AppendLine("using EasyCon.Core.Runner;");
        sb.AppendLine("using EasyCon.Script.Ssa;");
        sb.AppendLine("public static class JitProgram");
        sb.AppendLine("{");
        sb.AppendLine("    public static SsaEvaluator Evaluator = null!;");
        sb.AppendLine("    public static SsaValue[] Ops = null!;");
        sb.AppendLine("    public static System.Random Rnd = new System.Random();");

        // 收集用户函数名（用于 GenerateCall 判断）
        var userFuncNames = new HashSet<string>();
        foreach (var (sym, func) in program.Functions)
        {
            userFuncNames.Add(sym.Name);
            if (IsBuiltinName(sym.Name) || sym.Name.StartsWith('$'))
                userFuncNames.Remove(sym.Name); // 内置函数不走直接调用
        }

        // 1. 生成所有用户函数方法（跳过内置函数）
        int funcIdx = 0;
        foreach (var (sym, func) in program.Functions)
        {
            // 跳过内置函数：它们引用全局变量，不能作为独立方法编译
            if (IsBuiltinName(sym.Name) || sym.Name.StartsWith('$'))
                continue;
            GenerateFunctionMethod(sb, func, sym, funcIdx);
            funcIdx++;
        }

        // 2. 生成 Run() 主函数
        sb.AppendLine();
        sb.AppendLine("    public static int Run()");
        sb.AppendLine("    {");
        GenerateFunctionBody(sb, program.MainFunction!, isMain: true, userFuncNames: userFuncNames, labelPrefix: "_M_");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void GenerateFunctionMethod(StringBuilder sb, SsaFunction func, FunctionSymbol sym, int index)
    {
        var parameters = sym.Parameters;
        var retType = sym.ReturnType.Equals(ScriptType.Void) ? "void" : "int";
        var paramDecls = string.Join(", ", parameters.Select(p => $"int _{Sanitize(p.Name)}"));
        var labelPrefix = $"_F{index}_";

        sb.AppendLine();
        sb.AppendLine($"    static {retType} Func_{Sanitize(sym.Name)}({paramDecls})");
        sb.AppendLine("    {");

        // 参数映射为局部变量（与 LoadLocal/StoreLocal 命名一致）
        foreach (var p in parameters)
            sb.AppendLine($"        int l_{Sanitize(p.Name)} = _{Sanitize(p.Name)};");

        GenerateFunctionBody(sb, func, isMain: false, userFuncNames: null, labelPrefix: labelPrefix);
        sb.AppendLine("    }");
    }

    /// <summary>生成函数体（SSA 变量声明 + 基本块）。</summary>
    private static void GenerateFunctionBody(StringBuilder sb, SsaFunction func, bool isMain,
        HashSet<string>? userFuncNames, string labelPrefix)
    {
        var blocks = func.Blocks;
        var blockIndex = new Dictionary<SsaBlock, int>();
        for (int i = 0; i < blocks.Count; i++)
            blockIndex[blocks[i]] = i;

        var indent = "        ";

        if (isMain)
        {
            // 主函数：收集并声明全局变量和局部变量
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
            var globalTypes = new Dictionary<string, string>();
            var localTypes = new Dictionary<string, string>();
            foreach (var block in blocks)
            {
                foreach (var inst in block.Instructions)
                {
                    if (inst.Op is SsaOp.LoadGlobal && inst.Aux is VariableSymbol vs)
                        globalTypes[vs.Name] = CSharpType(inst);
                    if (inst.Op is SsaOp.LoadLocal && inst.Aux is VariableSymbol lvs)
                        localTypes[lvs.Name] = CSharpType(inst);
                }
            }
            foreach (var name in globalNames)
            {
                var type = globalTypes.GetValueOrDefault(name, "int");
                sb.AppendLine($"{indent}{type} g_{Sanitize(name)} = {(type == "string" ? "null!" : "0")};");
            }
            foreach (var name in localNames)
            {
                var type = localTypes.GetValueOrDefault(name, "int");
                sb.AppendLine($"{indent}{type} l_{Sanitize(name)} = {(type == "string" ? "null!" : "0")};");
            }
        }

        // SSA 值声明
        var allValues = new HashSet<int>();
        var valueTypes = new Dictionary<int, string>();
        foreach (var block in blocks)
        {
            foreach (var phi in block.Phis) { allValues.Add(phi.Id); valueTypes[phi.Id] = CSharpType(phi); }
            foreach (var inst in block.Instructions)
            {
                if (!inst.IsConstant) { allValues.Add(inst.Id); valueTypes[inst.Id] = CSharpType(inst); }
            }
        }
        foreach (var id in allValues.OrderBy(x => x))
        {
            var type = valueTypes.GetValueOrDefault(id, "int");
            sb.AppendLine($"{indent}{type} v{id} = default;");
        }

        // 生成基本块
        foreach (var block in blocks)
        {
            var idx = blockIndex[block];
            sb.AppendLine();
            sb.AppendLine($"{indent}// {labelPrefix}BB{idx}{(block.Predecessors.Count > 0 ? $" (preds: {string.Join(", ", block.Predecessors.Select(p => $"{labelPrefix}BB{blockIndex[p]}"))})" : " (entry)")}");
            sb.AppendLine($"{indent}{labelPrefix}BB{idx}:");

            foreach (var inst in block.Instructions)
            {
                if (inst.IsConstant) continue;
                GenerateInstruction(sb, inst, indent, userFuncNames, labelPrefix);
            }

            GenerateTerminator(sb, block, blockIndex, indent, labelPrefix);
        }
    }

    private static void GenerateInstruction(StringBuilder sb, SsaValue inst, string indent, HashSet<string>? userFuncNames, string labelPrefix)
    {
        switch (inst.Op)
        {
            case SsaOp.ConstInt:    sb.AppendLine($"{indent}v{inst.Id} = {inst.Const.GetInt()};"); break;
            case SsaOp.ConstBool:   sb.AppendLine($"{indent}v{inst.Id} = {(inst.Const.GetBool() ? 1 : 0)};"); break;
            case SsaOp.ConstByte:   sb.AppendLine($"{indent}v{inst.Id} = {inst.Const.GetByte()};"); break;
            case SsaOp.ConstUInt:   sb.AppendLine($"{indent}v{inst.Id} = unchecked((int){inst.Const.GetUInt()}u);"); break;
            case SsaOp.ConstUInt64: sb.AppendLine($"{indent}v{inst.Id} = unchecked((long){inst.Const.GetUInt64()}UL);"); break;
            case SsaOp.ConstDouble: sb.AppendLine(Culture($"        v{inst.Id} = {inst.Const.GetDouble():G17}d;", inst.Const.GetDouble())); break;
            case SsaOp.ConstString: sb.AppendLine($"{indent}v{inst.Id} = \"{Escape(inst.ConstString ?? "")}\";"); break;
            case SsaOp.ConstPtr:    sb.AppendLine($"{indent}v{inst.Id} = {inst.Const.GetPtr()}L;"); break;

            case SsaOp.LoadGlobal:  sb.AppendLine($"{indent}v{inst.Id} = g_{Sanitize((inst.Aux as VariableSymbol)!.Name)};"); break;
            case SsaOp.StoreGlobal: sb.AppendLine($"{indent}g_{Sanitize((inst.Aux as VariableSymbol)!.Name)} = {Fmt(inst.Arg0)};"); break;
            case SsaOp.LoadLocal:   sb.AppendLine($"{indent}v{inst.Id} = l_{Sanitize((inst.Aux as VariableSymbol)!.Name)};"); break;
            case SsaOp.StoreLocal:  sb.AppendLine($"{indent}l_{Sanitize((inst.Aux as VariableSymbol)!.Name)} = {Fmt(inst.Arg0)};"); break;

            case SsaOp.AddInt: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} + {Fmt(inst.Arg1)};"); break;
            case SsaOp.SubInt: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} - {Fmt(inst.Arg1)};"); break;
            case SsaOp.MulInt: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} * {Fmt(inst.Arg1)};"); break;
            case SsaOp.DivInt: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} / {Fmt(inst.Arg1)};"); break;
            case SsaOp.ModInt: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} % {Fmt(inst.Arg1)};"); break;
            case SsaOp.RoundDivInt:
                sb.AppendLine($"{indent}v{inst.Id} = ({Fmt(inst.Arg0)} + {Fmt(inst.Arg1)} / 2) / {Fmt(inst.Arg1)};"); break;

            case SsaOp.AddUInt: sb.AppendLine($"{indent}v{inst.Id} = unchecked((int)((uint){Fmt(inst.Arg0)} + (uint){Fmt(inst.Arg1)}));"); break;
            case SsaOp.SubUInt: sb.AppendLine($"{indent}v{inst.Id} = unchecked((int)((uint){Fmt(inst.Arg0)} - (uint){Fmt(inst.Arg1)}));"); break;
            case SsaOp.MulUInt: sb.AppendLine($"{indent}v{inst.Id} = unchecked((int)((uint){Fmt(inst.Arg0)} * (uint){Fmt(inst.Arg1)}));"); break;
            case SsaOp.DivUInt: sb.AppendLine($"{indent}v{inst.Id} = unchecked((int)((uint){Fmt(inst.Arg0)} / (uint){Fmt(inst.Arg1)}));"); break;
            case SsaOp.ModUInt: sb.AppendLine($"{indent}v{inst.Id} = unchecked((int)((uint){Fmt(inst.Arg0)} % (uint){Fmt(inst.Arg1)}));"); break;

            case SsaOp.AddDouble: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} + {Fmt(inst.Arg1)};"); break;
            case SsaOp.SubDouble: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} - {Fmt(inst.Arg1)};"); break;
            case SsaOp.MulDouble: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} * {Fmt(inst.Arg1)};"); break;
            case SsaOp.DivDouble: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} / {Fmt(inst.Arg1)};"); break;

            case SsaOp.AddUInt64: sb.AppendLine($"{indent}v{inst.Id} = unchecked((long)((ulong){Fmt(inst.Arg0)} + (ulong){Fmt(inst.Arg1)}));"); break;
            case SsaOp.SubUInt64: sb.AppendLine($"{indent}v{inst.Id} = unchecked((long)((ulong){Fmt(inst.Arg0)} - (ulong){Fmt(inst.Arg1)}));"); break;
            case SsaOp.MulUInt64: sb.AppendLine($"{indent}v{inst.Id} = unchecked((long)((ulong){Fmt(inst.Arg0)} * (ulong){Fmt(inst.Arg1)}));"); break;
            case SsaOp.DivUInt64: sb.AppendLine($"{indent}v{inst.Id} = unchecked((long)((ulong){Fmt(inst.Arg0)} / (ulong){Fmt(inst.Arg1)}));"); break;
            case SsaOp.ModUInt64: sb.AppendLine($"{indent}v{inst.Id} = unchecked((long)((ulong){Fmt(inst.Arg0)} % (ulong){Fmt(inst.Arg1)}));"); break;

            case SsaOp.AndInt: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} & {Fmt(inst.Arg1)};"); break;
            case SsaOp.OrInt:  sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} | {Fmt(inst.Arg1)};"); break;
            case SsaOp.XorInt: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} ^ {Fmt(inst.Arg1)};"); break;
            case SsaOp.ShlInt: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} << {Fmt(inst.Arg1)};"); break;
            case SsaOp.ShrInt: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} >> {Fmt(inst.Arg1)};"); break;
            case SsaOp.NotInt: sb.AppendLine($"{indent}v{inst.Id} = ~{Fmt(inst.Arg0)};"); break;

            case SsaOp.EqInt:  sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} == {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.NeqInt: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} != {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.LtInt:  sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} < {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.LeqInt: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} <= {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.GtInt:  sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} > {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.GeqInt: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} >= {Fmt(inst.Arg1)} ? 1 : 0;"); break;

            case SsaOp.EqUInt:  sb.AppendLine($"{indent}v{inst.Id} = (uint){Fmt(inst.Arg0)} == (uint){Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.NeqUInt: sb.AppendLine($"{indent}v{inst.Id} = (uint){Fmt(inst.Arg0)} != (uint){Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.LtUInt:  sb.AppendLine($"{indent}v{inst.Id} = (uint){Fmt(inst.Arg0)} < (uint){Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.LeqUInt: sb.AppendLine($"{indent}v{inst.Id} = (uint){Fmt(inst.Arg0)} <= (uint){Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.GtUInt:  sb.AppendLine($"{indent}v{inst.Id} = (uint){Fmt(inst.Arg0)} > (uint){Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.GeqUInt: sb.AppendLine($"{indent}v{inst.Id} = (uint){Fmt(inst.Arg0)} >= (uint){Fmt(inst.Arg1)} ? 1 : 0;"); break;

            case SsaOp.EqDouble:  sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} == {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.NeqDouble: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} != {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.LtDouble:  sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} < {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.LeqDouble: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} <= {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.GtDouble:  sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} > {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.GeqDouble: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} >= {Fmt(inst.Arg1)} ? 1 : 0;"); break;

            case SsaOp.EqUInt64:  sb.AppendLine($"{indent}v{inst.Id} = (ulong){Fmt(inst.Arg0)} == (ulong){Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.NeqUInt64: sb.AppendLine($"{indent}v{inst.Id} = (ulong){Fmt(inst.Arg0)} != (ulong){Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.LtUInt64:  sb.AppendLine($"{indent}v{inst.Id} = (ulong){Fmt(inst.Arg0)} < (ulong){Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.LeqUInt64: sb.AppendLine($"{indent}v{inst.Id} = (ulong){Fmt(inst.Arg0)} <= (ulong){Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.GtUInt64:  sb.AppendLine($"{indent}v{inst.Id} = (ulong){Fmt(inst.Arg0)} > (ulong){Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.GeqUInt64: sb.AppendLine($"{indent}v{inst.Id} = (ulong){Fmt(inst.Arg0)} >= (ulong){Fmt(inst.Arg1)} ? 1 : 0;"); break;

            case SsaOp.EqBool:  sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} == {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.NeqBool: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} != {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.EqByte:  sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} == {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.NeqByte: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} != {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.LtByte:  sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} < {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.LeqByte: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} <= {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.GtByte:  sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} > {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.GeqByte: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} >= {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.EqPtr:   sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} == {Fmt(inst.Arg1)} ? 1 : 0;"); break;
            case SsaOp.NeqPtr:  sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} != {Fmt(inst.Arg1)} ? 1 : 0;"); break;

            case SsaOp.EqString:
                sb.AppendLine($"{indent}v{inst.Id} = string.Equals({Fmt(inst.Arg0)}, {Fmt(inst.Arg1)}, StringComparison.Ordinal) ? 1 : 0;"); break;
            case SsaOp.NeqString:
                sb.AppendLine($"{indent}v{inst.Id} = !string.Equals({Fmt(inst.Arg0)}, {Fmt(inst.Arg1)}, StringComparison.Ordinal) ? 1 : 0;"); break;

            case SsaOp.LogicNot:
                sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} == 0 ? 1 : 0;"); break;

            case SsaOp.ConvBoolToInt:   sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvByteToInt:   sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvIntToUInt:   sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvIntToUInt64: sb.AppendLine($"{indent}v{inst.Id} = (long){Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvIntToDouble: sb.AppendLine($"{indent}v{inst.Id} = (double){Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvIntToByte:   sb.AppendLine($"{indent}v{inst.Id} = (byte){Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvIntToPtr:    sb.AppendLine($"{indent}v{inst.Id} = (long){Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvUIntToUInt64: sb.AppendLine($"{indent}v{inst.Id} = (long)(uint){Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvUInt64ToPtr: sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvPtrToInt:    sb.AppendLine($"{indent}v{inst.Id} = (int){Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvDoubleToInt: sb.AppendLine($"{indent}v{inst.Id} = (int){Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvUInt64ToInt: sb.AppendLine($"{indent}v{inst.Id} = unchecked((int)(ulong){Fmt(inst.Arg0)});"); break;
            case SsaOp.ConvToInt:       sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)};"); break;
            case SsaOp.ConvToString:
                SyncArgs(sb, inst, indent);
                sb.AppendLine($"{indent}Evaluator.ExecuteInstructionPublic(Ops[{inst.Id}]);");
                ReadResult(sb, inst, indent);
                break;

            case SsaOp.Phi:
            case SsaOp.CondBranch:
            case SsaOp.Branch:
            case SsaOp.Return:
                break;

            case SsaOp.Call:
            case SsaOp.StaticCall:
                GenerateCall(sb, inst, indent, userFuncNames);
                break;

            // Tier 3/4 — 内联缓存同步，回调解释器
            case SsaOp.ArrayLen:
                if (inst.Arg0 != null && inst.Arg0.Type == ScriptType.String)
                    sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)}.Length;");
                else goto default;
                break;
            case SsaOp.Concat:
                if (inst.Arg0 != null && inst.Arg1 != null
                    && inst.Arg0.Type == ScriptType.String && inst.Arg1.Type == ScriptType.String)
                    sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)} + {Fmt(inst.Arg1)};");
                else goto default;
                break;
            case SsaOp.Contains:
                if (inst.Arg0 != null && inst.Arg1 != null
                    && inst.Arg0.Type == ScriptType.String && inst.Arg1.Type == ScriptType.String)
                    sb.AppendLine($"{indent}v{inst.Id} = {Fmt(inst.Arg0)}.Contains({Fmt(inst.Arg1)}) ? 1 : 0;");
                else goto default;
                break;
            case SsaOp.Rand:
                sb.AppendLine($"{indent}v{inst.Id} = Rnd.Next({Fmt(inst.Arg0)});");
                break;
            case SsaOp.Wait:
            case SsaOp.OcrInit:
            case SsaOp.Capture:
            case SsaOp.Ocr:
            case SsaOp.Roi:
            case SsaOp.RuntimeValue:
            case SsaOp.ImageLabel:
            case SsaOp.Nop:
            case SsaOp.ArrayInit:
            case SsaOp.LoadIndex:
            case SsaOp.StoreIndex:
            case SsaOp.Slice:
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
            default:
                SyncArgs(sb, inst, indent);
                sb.AppendLine($"{indent}Evaluator.ExecuteInstructionPublic(Ops[{inst.Id}]);");
                ReadResult(sb, inst, indent);
                break;
        }
    }

    private static void GenerateCall(StringBuilder sb, SsaValue inst, string indent, HashSet<string>? userFuncNames)
    {
        var args = new List<SsaValue>();
        if (inst.Arg0 != null) args.Add(inst.Arg0);
        if (inst.ExtraArgs != null) args.AddRange(inst.ExtraArgs);

        var argCount = args.Count;
        var argStr = string.Join(", ", args.Select(a => Fmt(a)));

        // 用户函数 → 直接调用生成的 JIT 方法
        if (inst.Aux is FunctionSymbol fs && userFuncNames != null && userFuncNames.Contains(fs.Name))
        {
            sb.AppendLine($"{indent}v{inst.Id} = Func_{Sanitize(fs.Name)}({argStr});");
            return;
        }

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
            sb.AppendLine($"{indent}v{inst.Id} = SsaJitInterop.{method}(Evaluator, Ops, {inst.Id}, {argStr});");
        }
        else
        {
            SyncArgs(sb, inst, indent);
            sb.AppendLine($"{indent}Evaluator.ExecuteInstructionPublic(Ops[{inst.Id}]);");
            ReadResult(sb, inst, indent);
        }
    }

    private static bool IsIntType(SsaValue v)
    {
        if (v.IsConstant)
            return v.Op is SsaOp.ConstInt or SsaOp.ConstBool or SsaOp.ConstByte or SsaOp.ConstUInt;
        return v.Type == ScriptType.Int || v.Type == ScriptType.Bool
            || v.Type == ScriptType.Byte || v.Type == ScriptType.UInt;
    }

    private static void SyncArgs(StringBuilder sb, SsaValue inst, string indent)
    {
        SyncArg(sb, inst.Arg0, indent);
        SyncArg(sb, inst.Arg1, indent);
        if (inst.ExtraArgs != null)
            foreach (var arg in inst.ExtraArgs)
                SyncArg(sb, arg, indent);
    }

    private static void SyncArg(StringBuilder sb, SsaValue? arg, string indent)
    {
        if (arg == null || arg.IsConstant) return;
        // 统一缓存：一次 struct copy，无需按类型分流
        sb.AppendLine($"{indent}Evaluator.Cache[{arg.Id}] = v{arg.Id};");
    }

    private static void ReadResult(StringBuilder sb, SsaValue inst, string indent)
    {
        // 统一缓存读取：根据 SSA 类型选择正确的字段
        var field = CacheField(inst);
        sb.AppendLine($"{indent}v{inst.Id} = Evaluator.Cache[{inst.Id}]{field};");
    }

    /// <summary>根据 SSA 值类型选择 TaggedValue 的字段访问器。</summary>
    private static string CacheField(SsaValue v)
    {
        if (v.Type == ScriptType.Double) return ".F64";
        if (v.Type == ScriptType.UInt64 || v.Type == ScriptType.Ptr) return ".I64";
        // string/array/struct 在 JIT 中作为 handle (int) 处理；int/bool/byte/uint 直接 .I32
        return ".I32";
    }

    private static void GenerateTerminator(StringBuilder sb, SsaBlock block, Dictionary<SsaBlock, int> blockIndex, string indent, string labelPrefix)
    {
        foreach (var succ in block.GetSuccessors())
        {
            int predIdx = succ.Predecessors.IndexOf(block);
            if (predIdx < 0) continue;

            foreach (var phi in succ.Phis)
            {
                if (phi.ExtraArgs == null || predIdx >= phi.ExtraArgs.Count) continue;
                var arm = phi.ExtraArgs[predIdx];
                sb.AppendLine($"{indent}v{phi.Id} = {Fmt(arm)};  // phi for {labelPrefix}BB{blockIndex[succ]}");
            }
        }

        if (block.IsReturn)
        {
            var retInst = block.Instructions.LastOrDefault(i => i.Op == SsaOp.Return);
            if (retInst?.Arg0 != null)
            {
                var retVal = ResolveReturnValue(retInst.Arg0);
                sb.AppendLine($"{indent}return (int)({Fmt(retVal)});");
            }
            else
            {
                var printCall = block.Instructions.LastOrDefault(i => i.Op == SsaOp.StaticCall);
                if (printCall?.Arg0 != null)
                {
                    var retVal = ResolveReturnValue(printCall.Arg0);
                    sb.AppendLine($"{indent}return (int)({Fmt(retVal)});  // from PRINT arg");
                }
                else
                {
                    sb.AppendLine($"{indent}return 0;");
                }
            }
        }
        else if (block.BranchCondition != null)
        {
            var cond = block.BranchCondition;
            sb.AppendLine($"{indent}if (v{cond.Id} != 0) goto {labelPrefix}BB{blockIndex[block.TrueSuccessor!]}; else goto {labelPrefix}BB{blockIndex[block.FalseSuccessor!]};");
        }
        else if (block.JumpTarget != null)
        {
            sb.AppendLine($"{indent}goto {labelPrefix}BB{blockIndex[block.JumpTarget]};");
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

    private static bool IsBuiltinName(string name) => name is "PRINT" or "PRINTLN" or "LEN" or "WAIT" or "RAND" or "APPEND"
        or "STRING" or "INT" or "ALERT" or "AMIIBO" or "BEEP" or "ENV" or "ENCODE"
        or "JQ" or "FOPEN" or "FREAD" or "FWRITE" or "FCLOSE" or "FEOF"
        or "READFILE" or "WRITEFILE" or "APPENDFILE" or "FILE_EXISTS" or "OCR_CONF"
        or "__CAPTURE__" or "__OCR__" or "__ROI__" or "__OCR_INIT__";

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
            var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()).ToList();
            var shortErrors = string.Join("\n", errors.Take(5));
            throw new InvalidOperationException($"JIT compilation failed: {shortErrors}");
        }

        ms.Seek(0, System.IO.SeekOrigin.Begin);
        return Assembly.Load(ms.ToArray());
    }
}