using EasyCon.Script.Binding;
using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using System.Collections.Immutable;
using System.Text;

namespace EasyCon.Script.Ssa;

/// <summary>
/// 将 SSA IR 以人类可读格式输出（类似 LLVM IR 风格）。
/// </summary>
public static class SsaPrinter
{
    /// <summary>
    /// 将整个 SsaProgram 格式化为可读文本。
    /// </summary>
    public static string Dump(SsaProgram program)
    {
        var sb = new StringBuilder();

        // 外部函数声明
        if (program.ExternFunctions.Length > 0)
        {
            sb.AppendLine("; ---- External Functions ----");
            foreach (var ext in program.ExternFunctions)
            {
                sb.AppendLine($"declare {ext.ReturnType} {ext.Name}({FormatParams(ext.Parameters)})");
            }
            sb.AppendLine();
        }

        // 结构体定义
        if (program.StructDefinitions.Count > 0)
        {
            sb.AppendLine("; ---- Struct Definitions ----");
            foreach (var (_, def) in program.StructDefinitions)
            {
                sb.AppendLine($"struct {def.Name} {{");
                foreach (var field in def.Fields)
                {
                    sb.AppendLine($"  {field.FieldType} {field.Name};");
                }
                sb.AppendLine("}");
            }
            sb.AppendLine();
        }

        // 所有函数
        foreach (var (symbol, func) in program.Functions)
        {
            DumpFunction(sb, func, symbol == program.MainFunction?.Symbol);
        }

        // 主函数（如果不在 Functions 字典中）
        if (program.MainFunction != null &&
            !program.Functions.ContainsKey(program.MainFunction.Symbol))
        {
            DumpFunction(sb, program.MainFunction, isMain: true);
        }

        return sb.ToString();
    }

    private static void DumpFunction(StringBuilder sb, SsaFunction func, bool isMain)
    {
        var sym = func.Symbol;
        var tag = isMain ? " ; main" : "";
        sb.AppendLine($"func {sym.ReturnType} {sym.Name}({FormatParams(sym.Parameters)}){tag}");
        sb.AppendLine($"  ; frame: slots={func.Layout.SlotCount}");
        sb.AppendLine("{");

        // 重建块 ID -> 索引映射（用于输出简短名称）
        var blockIndex = new Dictionary<SsaBlock, int>(func.Blocks.Count);
        for (int i = 0; i < func.Blocks.Count; i++)
            blockIndex[func.Blocks[i]] = i;

        foreach (var block in func.Blocks)
        {
            DumpBlock(sb, block, blockIndex);
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void DumpBlock(StringBuilder sb, SsaBlock block, Dictionary<SsaBlock, int> blockIndex)
    {
        // 块标签 + 前驱
        var preds = block.Predecessors.Count > 0
            ? string.Join(", ", block.Predecessors.Select(p => $"BB{blockIndex[p]}"))
            : "entry";
        sb.AppendLine($"BB{blockIndex[block]} (preds: {preds}):");

        // Phi 节点
        foreach (var phi in block.Phis)
        {
            sb.Append("  ");
            DumpPhi(sb, phi, blockIndex);
        }

        // 普通指令
        foreach (var inst in block.Instructions)
        {
            sb.Append("  ");
            DumpInstruction(sb, inst, blockIndex);
        }

        // 终结指令
        DumpTerminator(sb, block, blockIndex);
    }

    private static void DumpPhi(StringBuilder sb, SsaValue phi, Dictionary<SsaBlock, int> blockIndex)
    {
        sb.Append($"{FormatDef(phi)} = phi {phi.Type} ");
        var args = phi.ExtraArgs;
        if (args != null)
        {
            var parts = new List<string>(args.Count);
            // Arg0 对应第一个前驱，ExtraArgs[i] 对应第 i+1 个前驱
            // Phi 的输入与 Predecessors 一一对应
            var block = phi.Block;
            for (int i = 0; i < args.Count; i++)
            {
                var predName = i < block.Predecessors.Count
                    ? $"BB{blockIndex[block.Predecessors[i]]}"
                    : $"pred{i}";
                parts.Add($"[{FormatUse(args[i])}, {predName}]");
            }
            sb.Append(string.Join(", ", parts));
        }
        sb.AppendLine();
    }

    private static void DumpInstruction(StringBuilder sb, SsaValue inst, Dictionary<SsaBlock, int> blockIndex)
    {
        // 无副作用的常量和 Nop 不显示 uses
        switch (inst.Op)
        {
            case SsaOp.Nop:
                sb.AppendLine("nop");
                return;

            // 常量
            case SsaOp.ConstBool:
                sb.AppendLine($"{FormatDef(inst)} = const.bool {inst.Const.GetBool()}");
                return;
            case SsaOp.ConstByte:
                sb.AppendLine($"{FormatDef(inst)} = const.byte {inst.Const.GetByte()}");
                return;
            case SsaOp.ConstInt:
                sb.AppendLine($"{FormatDef(inst)} = const.int {inst.Const.GetInt()}");
                return;
            case SsaOp.ConstUInt:
                sb.AppendLine($"{FormatDef(inst)} = const.uint {inst.Const.GetUInt()}");
                return;
            case SsaOp.ConstUInt64:
                sb.AppendLine($"{FormatDef(inst)} = const.uint64 {inst.Const.GetUInt64()}");
                return;
            case SsaOp.ConstDouble:
                sb.AppendLine($"{FormatDef(inst)} = const.double {inst.Const.GetDouble()}");
                return;
            case SsaOp.ConstString:
                sb.AppendLine($"{FormatDef(inst)} = const.string \"{EscapeString(inst.ConstString ?? "")}\"");
                return;
            case SsaOp.ConstPtr:
                sb.AppendLine($"{FormatDef(inst)} = const.ptr {inst.Const.GetPtr()}");
                return;

            // 加载/存储
            case SsaOp.LoadLocal:
                sb.AppendLine($"{FormatDef(inst)} = load.local {inst.Type} {FormatAux(inst)}");
                return;
            case SsaOp.StoreLocal:
                sb.AppendLine($"store.local {FormatAux(inst)}, {FormatUse(inst.Arg0)}");
                return;
            case SsaOp.LoadGlobal:
                sb.AppendLine($"{FormatDef(inst)} = load.global {inst.Type} {FormatAux(inst)}");
                return;
            case SsaOp.StoreGlobal:
                sb.AppendLine($"store.global {FormatAux(inst)}, {FormatUse(inst.Arg0)}");
                return;

            // 调用
            case SsaOp.Call:
                DumpCall(sb, inst, "call");
                return;
            case SsaOp.StaticCall:
                DumpCall(sb, inst, "staticcall");
                return;

            // 返回
            case SsaOp.Return:
                if (inst.Arg0 != null)
                    sb.AppendLine($"ret {FormatUse(inst.Arg0)}");
                else
                    sb.AppendLine("ret void");
                return;

            // Phi 单独处理
            case SsaOp.Phi:
                DumpPhi(sb, inst, blockIndex);
                return;
        }

        // 通用二元/一元/其他指令
        sb.Append($"{FormatDef(inst)} = {inst.Op}({inst.Type})");

        if (inst.Arg0 != null)
        {
            sb.Append($" {FormatUse(inst.Arg0)}");
            if (inst.Arg1 != null)
                sb.Append($", {FormatUse(inst.Arg1)}");
        }

        // ExtraArgs（ArrayInit/Capture/Ocr/Roi 等）
        if (inst.ExtraArgs is { Count: > 0 })
        {
            sb.Append(" |");
            foreach (var arg in inst.ExtraArgs)
                sb.Append($" {FormatUse(arg)}");
        }

        // 附加信息
        if (inst.Aux != null && inst.Op is not (SsaOp.LoadLocal or SsaOp.StoreLocal
            or SsaOp.LoadGlobal or SsaOp.StoreGlobal))
        {
            sb.Append($" ; {FormatAux(inst)}");
        }

        sb.AppendLine();
    }

    private static void DumpCall(StringBuilder sb, SsaValue call, string keyword = "call")
    {
        var funcName = call.Aux is FunctionSymbol fs ? fs.Name : FormatAux(call);
        var args = new List<string>();
        if (call.Arg0 != null) args.Add(FormatUse(call.Arg0));
        if (call.ExtraArgs != null)
        {
            foreach (var arg in call.ExtraArgs)
                args.Add(FormatUse(arg));
        }

        if (call.Type.Name == "void")
            sb.AppendLine($"{keyword} {funcName}({string.Join(", ", args)})");
        else
            sb.AppendLine($"{FormatDef(call)} = {keyword} {funcName}({string.Join(", ", args)})");
    }

    private static void DumpTerminator(StringBuilder sb, SsaBlock block, Dictionary<SsaBlock, int> blockIndex)
    {
        if (block.IsReturn)
        {
            // Return 已在 Instructions 中输出
            return;
        }

        if (block.BranchCondition != null)
        {
            var trueIdx = blockIndex[block.TrueSuccessor!];
            var falseIdx = blockIndex[block.FalseSuccessor!];
            sb.AppendLine($"  cond {FormatUse(block.BranchCondition)} ? BB{trueIdx} : BB{falseIdx}");
        }
        else if (block.JumpTarget != null)
        {
            sb.AppendLine($"  br BB{blockIndex[block.JumpTarget]}");
        }
    }

    // ---- 格式化辅助 ----

    private static string FormatDef(SsaValue v) => $"%{v.Id}";

    private static string FormatUse(SsaValue? v)
    {
        if (v == null) return "null";
        if (v.IsConstant)
        {
            return v.Op switch
            {
                SsaOp.ConstBool => v.Const.GetBool() ? "true" : "false",
                SsaOp.ConstByte => $"{v.Const.GetByte()}b",
                SsaOp.ConstInt => v.Const.GetInt().ToString(),
                SsaOp.ConstUInt => $"{v.Const.GetUInt()}u",
                SsaOp.ConstUInt64 => $"{v.Const.GetUInt64()}ul",
                SsaOp.ConstDouble => $"{v.Const.GetDouble()}d",
                SsaOp.ConstString => $"\"{EscapeString(v.ConstString ?? "")}\"",
                SsaOp.ConstPtr => $"ptr({v.Const.GetPtr()})",
                _ => $"%{v.Id}",
            };
        }
        return $"%{v.Id}";
    }

    private static string FormatAux(SsaValue v)
    {
        return v.Aux switch
        {
            VariableSymbol vs => $"var={vs.Name}",
            FunctionSymbol fs => $"func={fs.Name}",
            EcsFieldDef fd => $"field={fd.Name}",
            EcsStructDef sd => $"struct={sd.Name}",
            string s => $"\"{s}\"",
            _ => v.Aux?.ToString() ?? "",
        };
    }

    private static string FormatParams(ImmutableArray<ParamSymbol> parameters)
    {
        return string.Join(", ", parameters.Select(p => $"{p.Type} {p.Name}"));
    }

    private static string EscapeString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}