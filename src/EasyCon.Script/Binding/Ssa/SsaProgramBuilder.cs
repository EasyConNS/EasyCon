using EasyCon.Script.Runtime;
using EasyCon.Script.Symbols;
using System.Collections.Immutable;

namespace EasyCon.Script.Binding.Ssa;

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
            KeyAction = bound.KeyAction,
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
            KeyAction = bound.KeyAction,
            NeedIL = needCapture,
        };
    }

    // ============ Slot 分配（从 Binder 迁移） ============

    private static void AllocateLocalSlots(FunctionSymbol function, BoundBlockStatement body)
    {
        int nextSlot = function.LocalSlotCount;
        AllocateSlotsRecursive(body, ref nextSlot);
        function.LocalSlotCount = nextSlot;

        int intIdx = 0, longIdx = 0, doubleIdx = 0, handleIdx = 0;

        foreach (var p in function.Parameters)
            AssignSlotDesc(p, ref intIdx, ref longIdx, ref doubleIdx, ref handleIdx);

        AssignSlotDescsRecursive(body, ref intIdx, ref longIdx, ref doubleIdx, ref handleIdx);

        function.Layout = new FrameLayout(intIdx, longIdx, doubleIdx, handleIdx);
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

    private static void AssignSlotDesc(LocalVariableSymbol local, ref int intIdx, ref int longIdx, ref int doubleIdx, ref int handleIdx)
    {
        var cat = GetSlotCategory(local.Type);
        local.Slot = new SlotDesc(cat, cat switch
        {
            SlotCategory.Int => intIdx++,
            SlotCategory.Long => longIdx++,
            SlotCategory.Double => doubleIdx++,
            SlotCategory.Handle => handleIdx++,
            _ => intIdx++
        });
    }

    private static void AssignSlotDescsRecursive(BoundBlockStatement body, ref int intIdx, ref int longIdx, ref int doubleIdx, ref int handleIdx)
    {
        foreach (var stmt in body.Statements)
        {
            switch (stmt)
            {
                case BoundVariableDeclaration vd
                    when vd.Variable is LocalVariableSymbol local:
                    AssignSlotDesc(local, ref intIdx, ref longIdx, ref doubleIdx, ref handleIdx);
                    break;
                case BoundBlockStatement inner:
                    AssignSlotDescsRecursive(inner, ref intIdx, ref longIdx, ref doubleIdx, ref handleIdx);
                    break;
                case BoundIfStatement ifStmt:
                    AssignSlotDescsRecursive(ifStmt.Body, ref intIdx, ref longIdx, ref doubleIdx, ref handleIdx);
                    foreach (var (_, elifBody) in ifStmt.ElseIfs)
                        AssignSlotDescsRecursive(elifBody, ref intIdx, ref longIdx, ref doubleIdx, ref handleIdx);
                    if (ifStmt.ElseBody != null)
                        AssignSlotDescsRecursive(ifStmt.ElseBody, ref intIdx, ref longIdx, ref doubleIdx, ref handleIdx);
                    break;
                case BoundWhileStatement whileStmt:
                    AssignSlotDescsRecursive(whileStmt.Body, ref intIdx, ref longIdx, ref doubleIdx, ref handleIdx);
                    break;
                case BoundForStatement forStmt:
                    if (forStmt.Variable is LocalVariableSymbol forLocal2)
                        AssignSlotDesc(forLocal2, ref intIdx, ref longIdx, ref doubleIdx, ref handleIdx);
                    AssignSlotDescsRecursive(forStmt.Body, ref intIdx, ref longIdx, ref doubleIdx, ref handleIdx);
                    break;
                case BoundUntilStatement untilStmt:
                    AssignSlotDescsRecursive(untilStmt.Body, ref intIdx, ref longIdx, ref doubleIdx, ref handleIdx);
                    break;
            }
        }
    }

    private static SlotCategory GetSlotCategory(ScriptType type)
    {
        if (type.Equals(ScriptType.Bool) || type.Equals(ScriptType.Byte) ||
            type.Equals(ScriptType.Int) || type.Equals(ScriptType.UInt))
            return SlotCategory.Int;
        if (type.Equals(ScriptType.UInt64) || type.Equals(ScriptType.Ptr))
            return SlotCategory.Long;
        if (type.Equals(ScriptType.Double))
            return SlotCategory.Double;
        return SlotCategory.Handle;
    }
}