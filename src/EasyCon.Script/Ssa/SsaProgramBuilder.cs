using EasyCon.Script.Binding;
using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using System.Collections.Immutable;

namespace EasyCon.Script.Ssa;

/// <summary>
/// 将 BoundProgram 转换为 SsaProgram。
/// 遍历所有函数体，通过 SsaCodeGenerator 生成 SSA IR。
/// </summary>
static class SsaProgramBuilder
{
    public static SsaProgram Build(BoundProgram bound)
    {
        var functions = ImmutableDictionary.CreateBuilder<FunctionSymbol, SsaFunction>();
        int globalValueId = 0;
        int globalBlockId = 0;
        var externSet = bound.ExternFunctions.ToImmutableHashSet();

        // 转换所有函数（包括 lib 函数）
        foreach (var (sym, body) in bound.Functions)
        {
            AllocateLocalSlots(sym, body);

            var gen = new SsaCodeGenerator(sym, body.Syntax, globalValueId, globalBlockId, externSet);
            var ssaFunc = gen.Generate(body);
            ssaFunc.Layout = sym.Layout;
            functions[sym] = ssaFunc;
            // 更新全局 ID 计数器，确保不同函数的 SsaValue/SsaBlock ID 不重叠
            globalValueId = gen.NextValueId;
            globalBlockId = gen.NextBlockId;
        }

        // 转换主函数（$eval）
        SsaFunction? main = null;
        if (bound.MainFunction != null && bound.Functions.TryGetValue(bound.MainFunction, out var mainBody))
        {
            main = functions[bound.MainFunction];
        }

        // KeyAction 由扁平化的 SSA 指令判定（而非 BoundProgram 的顶层扫描）：
        // BoundProgram.KeyAction 仅扫函数体顶层语句，会遗漏嵌套在 if/while/for 内的按键，
        // 进而导致 ScriptService 不创建 pad、运行时 GamePad?. 静默丢弃按键。
        // SSA 已把控制流展开为基本块，扫描指令即可覆盖任意嵌套深度。
        var keyAction = HasKeyAction(functions.Values);

        // 构建 extern 函数列表（保持原样，SsaEvaluator 直接使用 ICallable）
        var externFunctions = bound.ExternFunctions;

        // 先用一个临时 SsaProgram 供分析器遍历
        var tempProgram = new SsaProgram
        {
            MainFunction = main,
            Functions = functions.ToImmutable(),
            ExternFunctions = externFunctions,
            Diagnostics = bound.Diagnostics,
            StructDefinitions = bound.StructDefinitions,
            ILNames = bound.ILNames,
            KeyAction = keyAction,
            NeedIL = bound.NeedIL,
        };

        // 调用图可达性分析：精准判断是否真的需要采集卡
        // （BoundProgram.NeedIL 是乐观假设，标准库 VisionSource 会让它永远为 true）
        var (needCapture, filteredILNames) = CaptureAnalyzer.Analyze(tempProgram, bound.ILNames);

        return new SsaProgram
        {
            MainFunction = main,
            Functions = functions.ToImmutable(),
            ExternFunctions = externFunctions,
            Diagnostics = bound.Diagnostics,
            StructDefinitions = bound.StructDefinitions,
            ILNames = filteredILNames,
            KeyAction = keyAction,
            NeedIL = needCapture,
        };
    }

    /// <summary>
    /// 扫描所有 SSA 函数的指令，判断程序是否包含任意按键动作（press / key action）。
    /// SSA 已扁平化（控制流展开为基本块 + 跳转），因此遍历指令可覆盖任意嵌套深度，
    /// 修正 BoundProgram.KeyAction 仅扫顶层语句而漏检嵌套按键的问题。
    /// </summary>
    private static bool HasKeyAction(IEnumerable<SsaFunction> functions)
    {
        foreach (var fn in functions)
            foreach (var block in fn.Blocks)
                foreach (var value in block.Instructions)
                    if (value.Op is SsaOp.KeyPress or SsaOp.KeyAction)
                        return true;
        return false;
    }

    // ============ Slot 分配（从 Binder 迁移） ============

    private static void AllocateLocalSlots(FunctionSymbol function, BoundBlockStatement body)
    {
        int nextSlot = function.LocalSlotCount;
        AllocateSlotsRecursive(body, ref nextSlot);
        function.LocalSlotCount = nextSlot;

        // 扁平槽位分配：单一计数器（类型信息在 SSA 操作码，不在槽位）
        int slotIdx = 0;

        foreach (var p in function.Parameters)
            p.Slot = new SlotDesc(slotIdx++);

        AssignSlotsRecursive(body, ref slotIdx);

        function.Layout = new FrameLayout(slotIdx);
    }

    private static void AllocateSlotsRecursive(BoundBlockStatement body, ref int nextSlot)
    {
        foreach (var stmt in body.Statements)
        {
            switch (stmt)
            {
                case BoundVariableDeclaration vd
                    when vd.Variable is LocalVariableSymbol local && local.SlotIndex < 0:
                    local.SlotIndex = nextSlot++;
                    break;
                case BoundBlockStatement inner:
                    AllocateSlotsRecursive(inner, ref nextSlot);
                    break;
                case BoundIfStatement ifStmt:
                    AllocateSlotsRecursive(ifStmt.Body, ref nextSlot);
                    foreach (var (_, elifBody) in ifStmt.ElseIfs)
                        AllocateSlotsRecursive(elifBody, ref nextSlot);
                    if (ifStmt.ElseBody != null)
                        AllocateSlotsRecursive(ifStmt.ElseBody, ref nextSlot);
                    break;
                case BoundWhileStatement whileStmt:
                    AllocateSlotsRecursive(whileStmt.Body, ref nextSlot);
                    break;
                case BoundForStatement forStmt:
                    if (forStmt.Variable is LocalVariableSymbol forLocal && forLocal.SlotIndex < 0)
                        forLocal.SlotIndex = nextSlot++;
                    AllocateSlotsRecursive(forStmt.Body, ref nextSlot);
                    break;
                case BoundUntilStatement untilStmt:
                    AllocateSlotsRecursive(untilStmt.Body, ref nextSlot);
                    break;
            }
        }
    }

    private static void AssignSlotsRecursive(BoundBlockStatement body, ref int slotIdx)
    {
        foreach (var stmt in body.Statements)
        {
            switch (stmt)
            {
                case BoundVariableDeclaration vd
                    when vd.Variable is LocalVariableSymbol local:
                    local.Slot = new SlotDesc(slotIdx++);
                    break;
                case BoundBlockStatement inner:
                    AssignSlotsRecursive(inner, ref slotIdx);
                    break;
                case BoundIfStatement ifStmt:
                    AssignSlotsRecursive(ifStmt.Body, ref slotIdx);
                    foreach (var (_, elifBody) in ifStmt.ElseIfs)
                        AssignSlotsRecursive(elifBody, ref slotIdx);
                    if (ifStmt.ElseBody != null)
                        AssignSlotsRecursive(ifStmt.ElseBody, ref slotIdx);
                    break;
                case BoundWhileStatement whileStmt:
                    AssignSlotsRecursive(whileStmt.Body, ref slotIdx);
                    break;
                case BoundForStatement forStmt:
                    if (forStmt.Variable is LocalVariableSymbol forLocal2)
                        forLocal2.Slot = new SlotDesc(slotIdx++);
                    AssignSlotsRecursive(forStmt.Body, ref slotIdx);
                    break;
                case BoundUntilStatement untilStmt:
                    AssignSlotsRecursive(untilStmt.Body, ref slotIdx);
                    break;
            }
        }
    }
}