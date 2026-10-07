using EasyCon.Script.Binding;
using EasyCon.Script.Runtime;
using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;
using System.Collections.Immutable;

namespace EasyCon.Script.Bytecode;

/// <summary>
/// 链接器：模块产物（.ecm）合并 → EcxImage（docs/EcmEcxFormat.md §4 / EcmEcxFormat §2）。
/// 类型表合并、导入解析、入口合成、死函数/死表项消除、采集洞可达性、资源需求计算。
/// 扫描全部经 <see cref="InstructionScanner"/>（解码形态单流——EXT 数据字是指令字段，
/// 不再有「数据字可能等于操作码」的步进铁律）。模块编码（SsaProgram → ModuleArtifact）
/// 见 <see cref="EcxModuleEncoder"/>。
/// </summary>
internal static class EcxPipeline
{
    // ============ 阶段二：链接模块产物 → EcxImage ============

    public static EcxImage Link(IReadOnlyList<ModuleArtifact> artifacts, bool keyAction, bool needIL)
    {
        if (artifacts.Count == 0)
            throw new BytecodeException(new[] { new BytecodeDiagnostic("没有可链接的模块", null, 0) });

        const uint ImportFlag = 0x80000000u;

        // ---- 导出索引：(name, nparams) → (moduleIdx, localFid)，首匹配优先（遮蔽语义）----
        var exportIndex = new Dictionary<(string Module, string Name, int NParams), (int Module, int LocalFid)>();
        for (int m = 0; m < artifacts.Count; m++)
            foreach (var e in artifacts[m].Exports)
                exportIndex.TryAdd((artifacts[m].Name, e.Name, e.NParams), (m, e.LocalFid));

        var imageFunctions = new List<EcsFunction>();
        var imageGlobals = new List<EcsGlobal>();
        var imageConsts = new List<EcsConst>();
        var imageNatives = new List<EcsNative>();
        var imageConstIndex = new Dictionary<(byte Tag, long Bits, string? Str), int>();
        var imageNativeIds = new Dictionary<string, int>();
        var moduleNames = artifacts.Select(a => a.Name).ToList();
        int entry = -1;

        // ---- 类型表合并（ModuleSystem.md §5.4-1）：模块模式各产物携带本模块声明集，
        // 全局 sid 按模块序首现分配；同名结构体形状必须一致（跨模块值语义的前提）。
        var globalStructs = new List<EcsStructLayout>();
        var globalStructSids = new Dictionary<string, int>(StringComparer.Ordinal);
        var moduleSidMaps = new List<Dictionary<int, int>>();
        var nestedFixups = new List<(EcsStructLayout Layout, EcsFieldLayout Field, int LocalNestedSid, int Module)>();
        for (int m = 0; m < artifacts.Count; m++)
        {
            var art = artifacts[m];
            var sidMap = new Dictionary<int, int>();
            for (int sid = 0; sid < art.Structs.Count; sid++)
            {
                var local = art.Structs[sid];
                if (!globalStructSids.TryGetValue(local.Name, out var gid))
                {
                    gid = globalStructs.Count;
                    globalStructSids[local.Name] = gid;
                    var cloned = new EcsStructLayout
                    {
                        Name = local.Name,
                        Fields = local.Fields.Select(f => new EcsFieldLayout
                        {
                            Name = f.Name,
                            Kind = f.Kind,
                            Type = f.Type,
                            ElementType = f.ElementType,
                            Count = f.Count,
                            NestedSid = f.NestedSid,
                            SlotOffset = f.SlotOffset,
                        }).ToImmutableArray(),
                        SlotCount = local.SlotCount,   // 布局展开结果一并转移（嵌套偏移已按本表计算）
                    };
                    globalStructs.Add(cloned);
                    foreach (var f in local.Fields)
                        if (f.Kind == EcsFieldKind.NestedStruct)
                            nestedFixups.Add((cloned, cloned.Fields.First(x => x.Name == f.Name), f.NestedSid, m));
                }
                else
                {
                    var global = globalStructs[gid];
                    if (global.Fields.Length != local.Fields.Length
                        || global.Fields.Zip(local.Fields).Any(p => p.First.Name != p.Second.Name || p.First.Kind != p.Second.Kind || p.First.Type != p.Second.Type))
                        throw new BytecodeException(new[] { new BytecodeDiagnostic($"结构体 {local.Name} 跨模块定义不一致（模块 {art.Name}）", art.Name, 0) });
                }
                sidMap[sid] = gid;
            }
            moduleSidMaps.Add(sidMap);
        }
        foreach (var (layout, field, localNestedSid, m) in nestedFixups)
            field.NestedSid = moduleSidMaps[m][localNestedSid];

        for (int m = 0; m < artifacts.Count; m++)
        {
            var art = artifacts[m];
            int funcBase = imageFunctions.Count;
            int globalBase = imageGlobals.Count;

            // 函数表拷贝 + 单遍全重写。Code 深拷贝：重写在镜像副本上进行，不污染调用方的
            // 模块产物（产物可能随后被 .ecm 序列化/二次链接——模块局部索引与镜像全局索引
            // 不容二次重映射）。EcsInstruction 是 readonly record struct，值拷贝即深拷贝。
            var moduleCopies = new List<EcsFunction>();
            foreach (var f in art.Functions)
            {
                var copy = new EcsFunction
                {
                    Name = f.Name,
                    Module = art.Name,
                    NParams = f.NParams,
                    NSlots = f.NSlots,
                    HasReturn = f.HasReturn,
                    Instructions = f.Instructions.ToList(),
                    LineTable = [.. f.LineTable],   // 行号表随镜像携带（运行错误 pc→行映射）
                };
                moduleCopies.Add(copy);
                imageFunctions.Add(copy);
            }
            foreach (var f in moduleCopies)
            {
                InstructionScanner.Scan(f.Instructions, new InstructionScanner.Callbacks
                {
                    // ABx 绝对索引重写：全局槽（+globalBase）、类型 sid（模块→镜像表）、常量池合并
                    OnInstruction = (ins, op) =>
                    {
                        switch (op)
                        {
                            case EcsOpcode.LoadG or EcsOpcode.StoreG:
                                {
                                    int bx = ins.B + globalBase;
                                    if (bx > 0xFFFF)
                                        throw new BytecodeException(new[] { new BytecodeDiagnostic("镜像全局槽超出 65535", f.Name, 0) });
                                    return ins with { B = bx };
                                }
                            case EcsOpcode.NewSt:
                                return ins with { B = moduleSidMaps[m][ins.B] };
                            case EcsOpcode.LoadK or EcsOpcode.Img:
                                {
                                    var cst = art.Pool.Consts[ins.B];
                                    var bits = cst.Tag == EcsTag.Double ? BitConverter.DoubleToInt64Bits(cst.Float64) : cst.Int64;
                                    var key = (cst.Tag, bits, cst.Str);
                                    if (!imageConstIndex.TryGetValue(key, out var newBx))
                                    {
                                        newBx = imageConsts.Count;
                                        imageConstIndex[key] = newBx;
                                        imageConsts.Add(cst);
                                    }
                                    if (newBx > 0xFFFF)
                                        throw new BytecodeException(new[] { new BytecodeDiagnostic("镜像常量池超出 65535（BC_CONST_OVERFLOW）", f.Name, 0) });
                                    return ins with { B = newBx };
                                }
                            default:
                                return null;
                        }
                    },
                    // Call 目标：导入标记解析 / 模块局部 fid → 镜像全局 fid
                    OnCall = ext => (ext & ImportFlag) != 0
                        ? ResolveImport(artifacts, exportIndex, art, ext & ~ImportFlag)
                        : unchecked((uint)(funcBase + (int)ext)),
                    // CallN 目标：原生名表按名合并；旗标置位 = syscall 编号直传（VM2.md §9.1）
                    OnCallN = oldId =>
                    {
                        if ((oldId & 0x80000000u) != 0)
                            return null;
                        var name = art.Natives[(int)oldId].Name;
                        if (!imageNativeIds.TryGetValue(name, out var newId))
                        {
                            newId = imageNatives.Count;
                            imageNativeIds[name] = newId;
                            imageNatives.Add(new EcsNative { Name = name });
                        }
                        return unchecked((uint)newId);
                    },
                });
            }

            imageGlobals.AddRange(art.Globals);

            // 入口：承载 $eval 的模块的 "$eval" 导出
            if (art.HasEval)
            {
                var evalExport = art.Exports.FirstOrDefault(e => e.Name == "$eval")
                    ?? throw new BytecodeException(new[] { new BytecodeDiagnostic("main 模块缺少 $eval 导出", art.Name, 0) });
                entry = funcBase + evalExport.LocalFid;
            }
        }

        if (entry < 0)
            throw new BytecodeException(new[] { new BytecodeDiagnostic("没有模块承载 $eval 入口", null, 0) });

        // ---- 入口合成（ModuleSystem.md §5.4-4，链接优化）：依赖模块带 <init> 时，把 init 调用
        // 序列前插进 $eval 本体，入口 = $eval——不再合成 <main> 壳。前插只平移既有指令的下标：
        // Jump 为相对指令下标（平移不变），Call/LoadK/LoadG/NewSt/Img 引用的 fid/常量池/全局槽/
        // 类型 sid 均为绝对索引（不受影响）；行号表（单位=指令下标）同步平移。
        // $eval 自身的 Ret 已承载顶层 RETURN 透传（替代原 <main> 的 RET r0）。
        var initFids = new List<uint>();
        for (int m = 0; m < artifacts.Count; m++)
        {
            var art = artifacts[m];
            if (!art.HasEval && art.HasInit && art.InitFid >= 0)
            {
                initFids.Add(unchecked((uint)(BaseFid(artifacts, m) + art.InitFid)));
                imageFunctions[BaseFid(artifacts, m) + art.InitFid].Name = $"<init:{art.Name}>";
            }
        }
        if (initFids.Count > 0)
        {
            var header = initFids
                .Select(fid => new EcsInstruction(EcsOpcode.Call, 0, 0, EcsInstruction.NoSlot, fid))
                .ToList();
            var entryFn = imageFunctions[entry];
            entryFn.Instructions.InsertRange(0, header);
            for (int i = 0; i < entryFn.LineTable.Count; i += 2)
                entryFn.LineTable[i] += header.Count;
        }

        // 入口命名：$eval 是 v1 时代顶层语句序列的占位名，链接后入口统一命名为 <main>
        //（诊断/错误现场/调试名表的入口标识；ECM 导出表的 $eval 键保持不变，仅镜像函数名）。
        imageFunctions[entry].Name = "<main>";

        keyAction |= artifacts.Any(a => a.KeyAction);
        needIL |= artifacts.Any(a => a.NeedIL);

        var image = new EcxImage
        {
            Consts = imageConsts,
            Structs = globalStructs,
            Globals = imageGlobals,
            Natives = imageNatives,
            Functions = imageFunctions,
            Entry = entry,
            Modules = moduleNames,
            KeyAction = keyAction,
            NeedIL = needIL,
        };
        // std/vision 包装函数强制内联（v2.3 对齐）：print/FRAME/ROI 等单块透传壳在调用点
        // 展开——先内联、后清扫，被展开的壳与内部残留由可达性清除 + DeadStoreSweep 收尾。
        if (Environment.GetEnvironmentVariable("ECS_NO_STD_INLINE") != "1")
            InlineStdWrappers(image.Functions);
        // 链接期死函数消除：stdlib/vision 供给的未调用函数体不进镜像（入口可达性为准）
        StripUnreachableFunctions(image);
        // 死存储清扫（防线 3，LLVM DeadMachineInstructionElim 的镜像级等价）：SSA φ 降级/变量
        // 落槽在块边界留下的死 Move/SetVar、无人读取的常量物化不进最终镜像。置于常量池压缩
        // 之前——清扫可能删掉某常量最后一个 LoadK 引用，使其进入死元素集一并消除。
        DeadStoreSweep.Sweep(image);
        // 死元素消除：常量池/原生名表中仅被死函数引用的条目不进镜像（保序压缩 + 索引重映射）。
        // 全局表不参与：每个模块全局的声明即顶层语句，必被 <init:module>/<main> 的 StoreG 引用
        //（init 恒保留），不存在死项；结构体类型表同理暂不裁剪。
        StripUnreferencedTables(image);
        // v1 语义对齐（CaptureAnalyzer 全程序视角）：采集卡需求 = 任意模块含图像标签（乐观）
        // ∪ 自入口可达采集洞函数（精确）。各模块的 SSA 分析器看不见跨模块调用链
        //（接口符号无函数体），这里在合并镜像上补全调用图可达性。
        bool reachesHole = ImageReachesCaptureHole(image);
        image.NeedIL = needIL || reachesHole;

        // 特征需求掩码（VM2.md §9.1）：文件族 syscall / FFI 动态原生 / 采集洞 / IL（NeedIL 投影）。
        // 死函数消除后扫描 = 入口可达语义；FFI 判据 = 名表含 "库!导出名"（'!' 分隔符）。
        image.Features = ComputeFeatures(image, reachesHole);
        ComputeResourceRequirements(image);
        return image;
    }

    /// <summary>特征需求掩码（清单驱动，BuiltinFunctions.Manifest.FeatureBit）：
    /// syscall 编号 / 名表键 → FILE/VISION；名表原生含 '!' → FFI（FFI 为结构判据）；
    /// 采集洞可达 → CAPTURE；NeedIL → IL（两者同为结构位，不经清单）。</summary>
    static uint ComputeFeatures(EcxImage image, bool reachesHole)
    {
        uint feats = image.NeedIL ? EcsImageFeatures.Il : 0;
        if (reachesHole)
            feats |= EcsImageFeatures.Capture;

        // 清单 → 查询表：L2 syscall 编号 / L3 名表键 → 特征位
        var syscallBits = new Dictionary<int, uint>();
        var nativeBits = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (var d in Binding.BuiltinFunctions.Manifest)
        {
            if (d.FeatureBit == 0)
                continue;
            if (d.Route == Binding.BuiltinFunctions.BuiltinRoute.Syscall
                && EcsSyscall.TryGetTarget(d.Symbol.Name, out var target))
                syscallBits[(int)(target & 0x7FFFFFFFu)] = d.FeatureBit;
            else if (d.Route == Binding.BuiltinFunctions.BuiltinRoute.NativeName)
                nativeBits[d.Symbol.Name] = d.FeatureBit;
        }

        var scan = new InstructionScanner.Callbacks();
        scan.OnCallN = target =>
        {
            if ((target & EcsSyscall.CallFlag) != 0)
            {
                if (syscallBits.TryGetValue((int)(target & 0x7FFFFFFFu), out var bit))
                    feats |= bit;
            }
            else if (target < (uint)image.Natives.Count)
            {
                var name = image.Natives[(int)target].Name;
                if (name.Contains('!'))
                    feats |= EcsImageFeatures.Ffi;
                else if (nativeBits.TryGetValue(name, out var bit))
                    feats |= bit;
            }
            return null;
        };
        foreach (var f in image.Functions)
            InstructionScanner.Scan(f.Instructions, scan);
        return feats;
    }


    /// <summary>
    /// 常量池/原生名表死元素消除：扫描保留函数的引用（LoadK/Img 的常量 Bx、CallN 的原生 EXT），
    /// 未引用条目不进镜像，保序压缩后重映射保留代码中的全部索引。
    /// 索引只缩不增（16 位 Bx 无溢出风险）；重映射发生在函数消除之后（引用集已最终），
    /// 且先于 <see cref="ImageReachesCaptureHole"/>（其按重映射后的 nid 查新表，自洽）。
    /// </summary>
    // ============ std/vision 包装函数强制内联（v2.3 对齐，2026-10-05） ============
    //
    // std/vision 的内建包装（print/PRINT/time/TIME/FRAME/ROI…）都是单块透传壳：参数转发给
    // syscall/native 后返回。链接期在调用点展开，消除 CALL/RET 往返、壳函数体；被展开的壳
    // 由随后的可达性清除删除。守卫（违一即放弃该调用点，保持调用形态）：
    //   壳体：std/vision 模块、单块无跳转/无体内 Call/无中间 Ret、≤24 条、末条 Ret/Ret0；
    //   调用点：NParams == 实参数、内联后调用者槽位 ≤254（v3 槽位 u8）；
    //   壳体指令的操作数重映射按「槽位性」白名单执行（立即数/坐标/长度不映射）；
    //   体内含函数调用的壳（vision OCR→OCR_INIT 链）不内联，保持包装形态。
    // 不动点迭代 ≤4 轮。行号表（指令下标单位）与跳转（指令下标增量）随拼接平移重算。
    static void InlineStdWrappers(List<EcsFunction> fns)
    {
        // 每调用者一个共享 fresh 块（首内联点从 NSlots 起建）：不同内联点的临时区在直线
        // 代码中互不重叠、且出拼接区即死——跨点复用同一块槽位是安全的，NSlots 只涨一次。
        var freshBases = new Dictionary<EcsFunction, int>();
        for (int round = 0; round < 4; round++)
        {
            bool any = false;
            foreach (var caller in fns)
            {
                for (int ci = 0; ci < caller.Instructions.Count; ci++)
                {
                    var call = caller.Instructions[ci];
                    if (call.Op != EcsOpcode.Call || call.Ext >= (uint)fns.Count)
                        continue;
                    var g = fns[(int)call.Ext];
                    if (ReferenceEquals(g, caller) || !IsInlineableWrapper(g))
                        continue;
                    if (!freshBases.TryGetValue(caller, out var freshBase))
                        freshBase = caller.NSlots;
                    if (TryInlineStdWrapperAt(caller, ci, g, freshBase))
                    {
                        any = true;
                        freshBases[caller] = Math.Max(freshBase, caller.NSlots);
                        ci--;   // 重扫拼接体首条
                    }
                }
            }
            if (!any)
                return;
        }
    }

    static bool IsInlineableWrapper(EcsFunction g)
    {
        // 字面量对齐 ProjectCompiler.StdModule/VisionModule（Bytecode 层不反向依赖 Modules 层）
        if (g.Module is not ("std" or "vision"))
            return false;
        int n = g.Instructions.Count;
        if (n == 0 || n > 24)
            return false;
        if (g.Instructions[^1].Op is not (EcsOpcode.Ret or EcsOpcode.Ret0))
            return false;
        for (int i = 0; i < n - 1; i++)
        {
            var op = g.Instructions[i].Op;
            if (op is EcsOpcode.Ret or EcsOpcode.Ret0
                or EcsOpcode.Jmp or EcsOpcode.Jpt or EcsOpcode.Jpf
                or EcsOpcode.ForStep or EcsOpcode.CmpJ
                or EcsOpcode.Call)   // 体内函数调用链不展开（OCR→OCR_INIT 保持包装）
                return false;
        }
        return true;
    }

    static int MapSlot(int s, int argsBase, int nParams, int freshBase)
        => s < nParams ? argsBase + s : freshBase + (s - nParams);

    /// <summary>操作数重映射：按槽位性白名单；未列操作码 → false（放弃该调用点内联）。</summary>
    static bool TryRemapForInline(EcsInstruction ins, int argsBase, int nParams, int freshBase,
        out EcsInstruction remapped)
    {
        int M(int s) => MapSlot(s, argsBase, nParams, freshBase);
        switch (ins.Op)
        {
            // A/B/C 全槽位族
            case EcsOpcode.Move or EcsOpcode.SetVar
                or EcsOpcode.AddI or EcsOpcode.SubI or EcsOpcode.MulI or EcsOpcode.DivI or EcsOpcode.ModI or EcsOpcode.RDivI
                or EcsOpcode.AddU or EcsOpcode.SubU or EcsOpcode.MulU or EcsOpcode.DivU or EcsOpcode.ModU
                or EcsOpcode.AddL or EcsOpcode.SubL or EcsOpcode.MulL or EcsOpcode.DivL or EcsOpcode.ModL
                or EcsOpcode.AddD or EcsOpcode.SubD or EcsOpcode.MulD or EcsOpcode.DivD
                or EcsOpcode.BandI or EcsOpcode.BorI or EcsOpcode.BxorI or EcsOpcode.ShlI or EcsOpcode.ShrI or EcsOpcode.BnotI
                or EcsOpcode.EqI or EcsOpcode.LtI or EcsOpcode.LeI or EcsOpcode.GtI or EcsOpcode.GeI
                or EcsOpcode.EqU or EcsOpcode.LtU or EcsOpcode.LeU or EcsOpcode.GtU or EcsOpcode.GeU
                or EcsOpcode.EqD or EcsOpcode.LtD or EcsOpcode.LeD or EcsOpcode.GtD or EcsOpcode.GeD
                or EcsOpcode.EqL or EcsOpcode.LtL or EcsOpcode.LeL or EcsOpcode.GtL or EcsOpcode.GeL
                or EcsOpcode.EqS or EcsOpcode.EqP or EcsOpcode.Not or EcsOpcode.NegI or EcsOpcode.NegD
                or EcsOpcode.GetI or EcsOpcode.SetI or EcsOpcode.Cont or EcsOpcode.Append or EcsOpcode.Cat or EcsOpcode.Len
                or EcsOpcode.Rand or EcsOpcode.WaitV
                or EcsOpcode.Ret:
                remapped = ins with { A = M(ins.A), B = M(ins.B), C = M(ins.C) };
                return true;

            // A/B/C 槽位 + ext 槽位/哨兵
            case EcsOpcode.Slice:
                remapped = ins with
                {
                    A = M(ins.A),
                    B = M(ins.B),
                    C = M(ins.C),
                    Ext = ins.Ext == 0xFFFFFFFFu ? ins.Ext : unchecked((uint)M(unchecked((int)ins.Ext))),
                };
                return true;
            case EcsOpcode.GetFI or EcsOpcode.PutFI:
                remapped = ins with { A = M(ins.A), B = M(ins.B), C = M(ins.C), Ext = unchecked((uint)M(unchecked((int)ins.Ext))) };
                return true;

            // A/B 槽位、C = 立即数（Conv 种类 / 字段下标）
            case EcsOpcode.Conv or EcsOpcode.GetF or EcsOpcode.PutF:
                remapped = ins with { A = M(ins.A), B = M(ins.B) };
                return true;

            // A/C 槽位（B = 参数个数/计数）
            case EcsOpcode.Call or EcsOpcode.CallN:
                remapped = ins with { A = M(ins.A), C = ins.C == EcsInstruction.NoSlot ? ins.C : M(ins.C) };
                return true;

            // 仅 A 槽位（B/C = 常量/索引/立即数）
            case EcsOpcode.LoadI or EcsOpcode.LoadBool or EcsOpcode.LoadK
                or EcsOpcode.LoadG or EcsOpcode.StoreG or EcsOpcode.NewArrE or EcsOpcode.NewSt or EcsOpcode.Img:
                remapped = ins with { A = M(ins.A) };
                return true;

            default:   // KeySt/Stick*/WaitI/KeyI（坐标/时长/键码）/跳转类 → 不重映射（出现在壳体即放弃）
                remapped = default;
                return false;
        }
    }

    static bool TryInlineStdWrapperAt(EcsFunction caller, int ci, EcsFunction g, int freshBase)
    {
        var code = caller.Instructions;
        var call = code[ci];
        if (call.B != g.NParams)
            return false;   // 实参数与壳参数不匹配 → 保持调用
        int need = freshBase + (g.NSlots - g.NParams);
        if (need > 254)
            return false;   // v3 槽位 u8 上限：预算不足 → 保持调用

        var body = g.Instructions.Take(g.Instructions.Count - 1).ToList();
        var spliced = new List<EcsInstruction>(body.Count + 1);
        foreach (var ins in body)
        {
            if (!TryRemapForInline(ins, call.A, g.NParams, freshBase, out var remapped))
                return false;
            spliced.Add(remapped);
        }
        var tail = g.Instructions[^1];
        if (tail.Op == EcsOpcode.Ret && call.C != EcsInstruction.NoSlot)
            spliced.Add(new EcsInstruction(EcsOpcode.Move, call.C, MapSlot(tail.A, call.A, g.NParams, freshBase), 0));

        // 跳转快照（壳体无跳转，仅调用者侧）；拼接后重算下标增量
        var jumps = new List<(int J, int T)>();
        for (int j = 0; j < code.Count; j++)
        {
            var ins = code[j];
            if (ins.Op is EcsOpcode.Jmp or EcsOpcode.Jpt or EcsOpcode.Jpf or EcsOpcode.ForStep or EcsOpcode.CmpJ)
                jumps.Add((j, j + 1 + ins.Jump));
        }

        int d = spliced.Count - 1;
        caller.NSlots = Math.Max(caller.NSlots, need);
        code.RemoveAt(ci);
        code.InsertRange(ci, spliced);
        foreach (var (j, t) in jumps)
        {
            int nj = j + (j > ci ? d : 0);
            int nt = t + (t > ci ? d : 0);
            code[nj] = code[nj] with { Jump = nt - (nj + 1) };
        }
        for (int i = 0; i < caller.LineTable.Count; i += 2)
            if (caller.LineTable[i] > ci)
                caller.LineTable[i] += d;
        return true;
    }

    static void StripUnreferencedTables(EcxImage image)
    {
        var usedConsts = new HashSet<int>();
        var usedNatives = new HashSet<int>();
        var refs = new InstructionScanner.Callbacks
        {
            OnConstRef = bx => usedConsts.Add(bx),
            // 旗标置位 = syscall 编号，非名表引用
            OnCallN = nid => { if ((nid & 0x80000000u) == 0) usedNatives.Add((int)nid); return null; },
        };
        foreach (var fn in image.Functions)
            InstructionScanner.Scan(fn.Instructions, refs);

        if (usedConsts.Count == image.Consts.Count && usedNatives.Count == image.Natives.Count)
            return;   // 无死条目：零重映射开销

        // 保序压缩
        var constNewIndex = new int[image.Consts.Count];
        var keptConsts = new List<EcsConst>();
        for (int i = 0; i < image.Consts.Count; i++)
        {
            if (!usedConsts.Contains(i)) continue;
            constNewIndex[i] = keptConsts.Count;
            keptConsts.Add(image.Consts[i]);
        }
        var nativeNewIndex = new int[image.Natives.Count];
        var keptNatives = new List<EcsNative>();
        for (int i = 0; i < image.Natives.Count; i++)
        {
            if (!usedNatives.Contains(i)) continue;
            nativeNewIndex[i] = keptNatives.Count;
            keptNatives.Add(image.Natives[i]);
        }

        var remapTable = new InstructionScanner.Callbacks
        {
            OnConstRef = null,
            OnInstruction = (ins, op) => op is EcsOpcode.LoadK or EcsOpcode.Img ? ins with { B = constNewIndex[ins.B] } : null,
            // 旗标置位 = syscall 编号直传，不参与名表重映射
            OnCallN = nid => (nid & 0x80000000u) == 0 ? unchecked((uint)nativeNewIndex[(int)nid]) : null,
        };
        foreach (var fn in image.Functions)
            InstructionScanner.Scan(fn.Instructions, remapTable);
        image.Consts = keptConsts;
        image.Natives = keptNatives;
    }

    /// <summary>
    /// 链接期死函数消除：自入口 BFS 调用图（Call 边；CallN 经原生名表分发，不构成函数边），
    /// 仅保留可达函数并重映射 fid——stdlib/vision 供给的未调用函数体（OCR/FRAME/采集洞等）
    /// 不进镜像。不可达函数无其他引用途径（ECS 无函数指针/间接调用，
    /// 全部调用在编码期闭合为 Call fid），消除是安全的。
    /// &lt;init:module&gt; 经入口头部的调用序列必然可达；原地重写在镜像副本上进行。
    /// </summary>
    static void StripUnreachableFunctions(EcxImage image)
    {
        var keep = new HashSet<int>();
        var queue = new Queue<int>();
        if (image.Entry < image.Functions.Count)
        {
            keep.Add(image.Entry);
            queue.Enqueue(image.Entry);
        }
        var callEdges = new InstructionScanner.Callbacks
        {
            OnCall = target =>
            {
                if (keep.Add((int)target))
                    queue.Enqueue((int)target);
                return null;
            },
        };
        while (queue.Count > 0)
            InstructionScanner.Scan(image.Functions[queue.Dequeue()].Instructions, callEdges);

        if (keep.Count == image.Functions.Count)
            return;   // 全可达：零重映射开销

        var newIndexOf = new int[image.Functions.Count];
        var kept = new List<EcsFunction>();
        for (int old = 0; old < image.Functions.Count; old++)
        {
            if (!keep.Contains(old))
                continue;
            newIndexOf[old] = kept.Count;
            kept.Add(image.Functions[old]);
        }
        var remapFid = new InstructionScanner.Callbacks
        {
            OnCall = target => unchecked((uint)newIndexOf[(int)target]),
        };
        foreach (var fn in kept)
            InstructionScanner.Scan(fn.Instructions, remapFid);
        image.Functions = kept;
        image.Entry = newIndexOf[image.Entry];
    }

    /// <summary>自镜像入口 BFS 调用图，判断是否可达任何采集洞函数（CallN "__CAPTURE__" 等）。</summary>
    static bool ImageReachesCaptureHole(EcxImage image)
    {
        var holeNames = Binding.BuiltinFunctions.GetCaptureHoles()
            .Select(h => h.Name)
            .ToHashSet(StringComparer.Ordinal);

        var visited = new HashSet<int>();
        var queue = new Queue<int>();
        if (image.Entry < image.Functions.Count)
        {
            visited.Add(image.Entry);
            queue.Enqueue(image.Entry);
        }
        bool foundHole = false;
        var holeScan = new InstructionScanner.Callbacks();
        holeScan.OnCallN = nid =>
        {
            if ((nid & 0x80000000u) == 0 && nid < (uint)image.Natives.Count && holeNames.Contains(image.Natives[(int)nid].Name))
            {
                foundHole = true;
                holeScan.Stop = true;   // 命中即停（等价原实现的即时 return true）
            }
            return null;
        };
        holeScan.OnCall = target =>
        {
            if (target < (uint)image.Functions.Count && visited.Add((int)target))
                queue.Enqueue((int)target);
            return null;
        };
        while (queue.Count > 0 && !foundHole)
            InstructionScanner.Scan(image.Functions[queue.Dequeue()].Instructions, holeScan);
        return foundHole;
    }

    static uint ResolveImport(
        IReadOnlyList<ModuleArtifact> artifacts,
        Dictionary<(string Module, string Name, int NParams), (int Module, int LocalFid)> exportIndex,
        ModuleArtifact importer, uint importIdx)
    {
        var imp = importer.Imports[(int)importIdx];
        // 导入名模块限定（N2）："模块!函数" → 按目标模块精确解析（跨模块同名可区分）；
        // 裸名 → 按名/参数数首命中（防御路径，现管线导入恒带模块限定）
        var bang = imp.Name.IndexOf('!');
        (string Module, string Name, int NParams) key = bang > 0
            ? (imp.Name[..bang], imp.Name[(bang + 1)..], imp.NParams)
            : (null!, imp.Name, imp.NParams);
        if (!exportIndex.TryGetValue(key, out var resolved))
        {
            if (key.Module != null)
                throw new BytecodeException(new[] { new BytecodeDiagnostic(
                    $"LC_MISSING_FUNC：模块 {importer.Name} 导入的函数 {imp.Name}/{imp.NParams} 在目标模块中无导出", importer.Name, 0) });
            var hit = exportIndex.FirstOrDefault(kv => kv.Key.Name == imp.Name && kv.Key.NParams == imp.NParams);
            if (hit.Key.Name == null)
                throw new BytecodeException(new[] { new BytecodeDiagnostic(
                    $"LC_MISSING_FUNC：模块 {importer.Name} 导入的函数 {imp.Name}/{imp.NParams} 无可解析导出", importer.Name, 0) });
            resolved = hit.Value;
        }
        int funcBase = BaseFid(artifacts, resolved.Module);
        return unchecked((uint)(funcBase + resolved.LocalFid));
    }

    /// <summary>静态资源需求：max_slots 与调用图最长链（EcmEcxFormat §2.1，静态预分配用）。
    /// 容量元数据不进产物（R-3）；内存模型保留供宿主预检/诊断，序列化侧省略。</summary>
    internal static void ComputeResourceRequirements(EcxImage image)
    {
        image.MaxSlots = image.Functions.Count == 0 ? 0 : image.Functions.Max(f => f.NSlots);

        // 调用边：Call ext（链接后已是全局 fid）
        var calls = new List<uint>[image.Functions.Count];
        var edgeScan = new InstructionScanner.Callbacks();
        for (int i = 0; i < image.Functions.Count; i++)
        {
            var edges = new List<uint>();
            edgeScan.OnCall = target => { edges.Add(target); return null; };
            InstructionScanner.Scan(image.Functions[i].Instructions, edgeScan);
            calls[i] = edges;
        }

        var memo = new Dictionary<int, int>();
        var visiting = new HashSet<int>();
        int Depth(int fid)
        {
            if (memo.TryGetValue(fid, out var d))
                return d;
            if (!visiting.Add(fid))
                return 1;   // 递归环：按 1 计（运行深度由 ECS_MAX_CALL_DEPTH 约束）
            int best = 0;
            foreach (var callee in calls[fid])
                best = Math.Max(best, Depth((int)callee));
            visiting.Remove(fid);
            memo[fid] = best + 1;
            return best + 1;
        }

        int maxDepth = image.Entry < image.Functions.Count ? Depth(image.Entry) : 0;
        for (int i = 0; i < image.Functions.Count; i++)
            maxDepth = Math.Max(maxDepth, Depth(i));
        image.MaxDepth = maxDepth;
    }

    static int BaseFid(IReadOnlyList<ModuleArtifact> artifacts, int moduleIdx)
    {
        int baseFid = 0;
        for (int i = 0; i < moduleIdx; i++)
            baseFid += artifacts[i].Functions.Count;
        return baseFid;
    }

}