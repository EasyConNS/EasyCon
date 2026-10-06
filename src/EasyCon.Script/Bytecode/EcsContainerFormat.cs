using System.Collections.Immutable;
using System.Text;

namespace EasyCon.Script.Bytecode;

/// <summary>
/// 产物容器（v2.3 零期平铺化，docs/EcmEcxFormat.md）：
/// <c>.ecx</c> 镜像 = ECX1 平铺（<see cref="WriteImage"/>），<c>.ecm</c> 模块缓存 = ECM1 平铺
/// （<see cref="WriteModule"/>）。同一纪律：定长 36B 头 + 自计数表依序排列 + 头内全量 CRC；
/// 无逐段框架——格式演进 = 版本断代 + 全量重编（旧产物经魔数不符自然失效）。
/// 指令流经 <see cref="InstructionCodec"/> 投影为 v3 定长字节流。
/// 容量上限不是格式字段；产物只携带关于自身静态极值的事实（ZeroAllocVm.md §4.1）。
/// </summary>
public static class EcsContainer
{
    public const uint FlatMagic = 0x31584345;   // "ECX1"（平铺镜像容器）
    public const ushort FlatFormat = 1;
    public const uint ModuleFlatMagic = 0x314D4345;   // "ECM1"（平铺模块缓存容器）
    public const ushort ModuleFlatFormat = 1;

    public const byte HeaderFlagDebug = 0x01;   // ECX1 flags：bit0=调试块在（ECM1 flags 见 WriteModule）

    /// <summary>
    /// ECX1 平铺镜像容器（v2.3 零期，2026-10-05）：无逐段框架——定长 36B 头 + 主模块名 +
    /// 自计数表依序排列（常量/类型/全局/原生/函数表/.text/可选调试块）+ 全量 CRC。
    /// 调试块 = [linesLen u32][lines][namesLen u32][names]；dbg_size=0 即省略。
    /// </summary>
    public static byte[] WriteImage(EcxImage image, bool stripDebug = false, bool withCrc = true)
    {
        // .text 投影（函数 code_off 依赖字节布局）
        var codeOffsets = new uint[image.Functions.Count];
        var codeBuf = new List<byte>();
        for (int i = 0; i < image.Functions.Count; i++)
        {
            codeOffsets[i] = (uint)codeBuf.Count;
            codeBuf.AddRange(InstructionCodec.Project(image.Functions[i].Instructions));
        }
        var extreme = ComputeStaticExtremes(image.Functions, image.Consts, image.Structs);

        // 调试块（可选）
        byte[] dbg = [];
        if (!stripDebug)
        {
            var p = new MemoryStream();
            using (var dw = new BinaryWriter(p, Encoding.UTF8, leaveOpen: true))
            {
                var lines = new MemoryStream();
                using (var lw = new BinaryWriter(lines, Encoding.UTF8, leaveOpen: true))
                {
                    lw.Write(image.Functions.Count);
                    for (int i = 0; i < image.Functions.Count; i++)
                    {
                        var offs = InstructionLayout(image.Functions[i].Instructions);
                        var table = image.Functions[i].LineTable;
                        lw.Write(table.Count / 2);
                        for (int k = 0; k < table.Count; k += 2)
                        {
                            int idx = table[k];
                            lw.Write(idx >= 0 && idx < offs.Length ? offs[idx] : 0);
                            lw.Write(table[k + 1]);
                        }
                    }
                }
                var lineBytes = lines.ToArray();
                dw.Write(lineBytes.Length);
                dw.Write(lineBytes);
                var names = new MemoryStream();
                using (var nw = new BinaryWriter(names, Encoding.UTF8, leaveOpen: true))
                {
                    nw.Write(image.Functions.Count + image.Globals.Count);
                    foreach (var f in image.Functions)
                        WriteUtf8(nw, f.Name);
                    foreach (var g in image.Globals)
                        WriteUtf8(nw, g.Name);
                }
                var dbgNameBytes = names.ToArray();
                dw.Write(dbgNameBytes.Length);
                dw.Write(dbgNameBytes);
            }
            dbg = p.ToArray();
        }

        // 各表载荷（镜像与模块共用编码：utf8 名 = u16 长度 + UTF-8，常量 = tag + 定宽负载）
        byte[] ConstsPayload() { var p = new MemoryStream(); using var cw = new BinaryWriter(p, Encoding.UTF8, leaveOpen: true); cw.Write(image.Consts.Count); foreach (var c in image.Consts) WriteConst(cw, c); return p.ToArray(); }
        byte[] StructsPayload() { var p = new MemoryStream(); using var sw2 = new BinaryWriter(p, Encoding.UTF8, leaveOpen: true); sw2.Write(image.Structs.Count); foreach (var st in image.Structs) { WriteUtf8(sw2, st.Name); sw2.Write((byte)st.Fields.Length); foreach (var f in st.Fields) { WriteUtf8(sw2, f.Name); sw2.Write((byte)f.Kind); sw2.Write((byte)f.Type); sw2.Write((byte)f.ElementType); sw2.Write((ushort)(f.Kind == EcsFieldKind.NestedStruct ? f.NestedSid : f.Count)); } } return p.ToArray(); }
        byte[] GlobalsPayload() { var p = new MemoryStream(); using var gw = new BinaryWriter(p, Encoding.UTF8, leaveOpen: true); gw.Write(image.Globals.Count); foreach (var g in image.Globals) { gw.Write((byte)Math.Max(0, image.Modules.IndexOf(g.Module))); gw.Write((byte)g.Type); } return p.ToArray(); }
        byte[] NativesPayload() { var p = new MemoryStream(); using var nw2 = new BinaryWriter(p, Encoding.UTF8, leaveOpen: true); nw2.Write(image.Natives.Count); foreach (var n in image.Natives) WriteUtf8(nw2, n.Name); return p.ToArray(); }
        byte[] FuncsPayload() { var p = new MemoryStream(); using var fw = new BinaryWriter(p, Encoding.UTF8, leaveOpen: true); fw.Write(image.Functions.Count); for (int i = 0; i < image.Functions.Count; i++) { fw.Write((ushort)image.Functions[i].NSlots); fw.Write(codeOffsets[i]); } return p.ToArray(); }

        var nameBytes = Encoding.UTF8.GetBytes(image.Modules.Count > 0 ? image.Modules[^1] : "");
        int codeSize = codeBuf.Count;

        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        w.Write(FlatMagic);
        w.Write(FlatFormat);
        w.Write((ushort)EcsSyscall.AbiRevision);
        w.Write((ushort)(image.Features & 0xFFFF));
        w.Write((ushort)image.Entry);
        w.Write((byte)((stripDebug ? 0 : HeaderFlagDebug) | (image.KeyAction ? 0x2 : 0) | (image.NeedIL ? 0x4 : 0)));
        w.Write((byte)nameBytes.Length);
        w.Write((ushort)image.Functions.Count);
        w.Write((uint)codeSize);
        w.Write((uint)dbg.Length);
        w.Write((ushort)Math.Min(extreme.MaxLiteralStringUnits, 0xFFFF));
        w.Write((ushort)Math.Min(extreme.MaxLiteralArrayElems, 0xFFFF));
        w.Write((ushort)Math.Min(extreme.MaxStructSlots, 0xFFFF));
        w.Write((ushort)0);
        w.Write(0u);   // all_crc32 占位
        w.Write(nameBytes);
        w.Write(ConstsPayload());
        w.Write(StructsPayload());
        w.Write(GlobalsPayload());
        w.Write(NativesPayload());
        w.Write(FuncsPayload());
        w.Write(codeBuf.ToArray());
        if (dbg.Length > 0)
            w.Write(dbg);
        w.Flush();

        var bytes = ms.ToArray();
        if (withCrc)
            ResealCrc32(bytes);
        return bytes;
    }

    /// <summary>
    /// ECM1 平铺模块缓存容器：36B 头（magic/format/abi/flags/名长/函数数/codeSize/initFid/
    /// metaSize/静态极值/CRC）+ 模块名 + 自计数表序（常量/类型/全局/原生/函数表/.text）
    /// + 元数据尾块（[linesLen][lines][imports][exports][ilnames][ifaceLen][iface]）+ 全量 CRC。
    /// flags：bit0=hasEval bit1=hasInit bit2=keyAction bit3=needIL。
    /// 仅 PC 侧编译缓存消费（进程缓存 + obj/ 磁盘缓存）；不携带入口/链接结果（链接期内存隐式）。
    /// </summary>
    public static byte[] WriteModule(ModuleArtifact module)
    {
        // .text 投影（函数 code_off 依赖字节布局）
        var codeOffsets = new uint[module.Functions.Count];
        var codeBuf = new List<byte>();
        for (int i = 0; i < module.Functions.Count; i++)
        {
            codeOffsets[i] = (uint)codeBuf.Count;
            codeBuf.AddRange(InstructionCodec.Project(module.Functions[i].Instructions));
        }

        // 行号块（缓存命中路径保留运行错误行号映射；单位 = 函数内相对字节偏移，与镜像调试块同布局）
        var lines = new MemoryStream();
        using (var lw = new BinaryWriter(lines, Encoding.UTF8, leaveOpen: true))
        {
            lw.Write(module.Functions.Count);
            for (int i = 0; i < module.Functions.Count; i++)
            {
                var offs = InstructionLayout(module.Functions[i].Instructions);
                var table = module.Functions[i].LineTable;
                lw.Write(table.Count / 2);
                for (int k = 0; k < table.Count; k += 2)
                {
                    int idx = table[k];
                    lw.Write(idx >= 0 && idx < offs.Length ? offs[idx] : 0);
                    lw.Write(table[k + 1]);
                }
            }
        }
        var lineBytes = lines.ToArray();

        byte[] ifaceBytes = [];
        if (module.Interface is { } iface)
        {
            var p = new MemoryStream();
            using (var iw = new BinaryWriter(p, Encoding.UTF8, leaveOpen: true))
                ModuleInterfaceFormat.Write(iw, iface);
            ifaceBytes = p.ToArray();
        }

        // 元数据尾块
        var meta = new MemoryStream();
        using (var mw = new BinaryWriter(meta, Encoding.UTF8, leaveOpen: true))
        {
            mw.Write(lineBytes.Length);
            mw.Write(lineBytes);
            mw.Write(module.Imports.Count);
            foreach (var i in module.Imports)
            {
                WriteUtf8(mw, i.Name);
                mw.Write((byte)i.NParams);
                mw.Write((byte)(i.HasReturn ? 1 : 0));
            }
            mw.Write(module.Exports.Count);
            foreach (var e in module.Exports)
            {
                WriteUtf8(mw, e.Name);
                mw.Write((uint)e.LocalFid);
                mw.Write((byte)e.NParams);
                mw.Write((byte)(e.HasReturn ? 1 : 0));
            }
            mw.Write(module.ILNames.Count);
            foreach (var ilName in module.ILNames)
                WriteUtf8(mw, ilName);
            mw.Write(ifaceBytes.Length);
            mw.Write(ifaceBytes);
        }
        var metaBytes = meta.ToArray();

        var nameBytes = Encoding.UTF8.GetBytes(module.Name);
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        w.Write(ModuleFlatMagic);
        w.Write(ModuleFlatFormat);
        w.Write((ushort)EcsSyscall.AbiRevision);
        w.Write((byte)((module.HasEval ? 0x1 : 0) | (module.HasInit ? 0x2 : 0)
            | (module.KeyAction ? 0x4 : 0) | (module.NeedIL ? 0x8 : 0)));
        w.Write((byte)nameBytes.Length);
        w.Write((ushort)module.Functions.Count);
        w.Write((uint)codeBuf.Count);
        w.Write(module.HasInit ? module.InitFid : -1);
        w.Write((uint)metaBytes.Length);
        var extreme = ComputeStaticExtremes(module.Functions, module.Pool.Consts, module.Structs);
        w.Write((ushort)Math.Min(extreme.MaxLiteralStringUnits, 0xFFFF));
        w.Write((ushort)Math.Min(extreme.MaxLiteralArrayElems, 0xFFFF));
        w.Write((ushort)Math.Min(extreme.MaxStructSlots, 0xFFFF));
        w.Write((ushort)0);
        w.Write(0u);   // all_crc32 占位
        w.Write(nameBytes);
        w.Write(module.Pool.Consts.Count);
        foreach (var c in module.Pool.Consts)
            WriteConst(w, c);
        w.Write(module.Structs.Count);
        foreach (var st in module.Structs)
        {
            WriteUtf8(w, st.Name);
            w.Write((byte)st.Fields.Length);
            foreach (var f in st.Fields)
            {
                WriteUtf8(w, f.Name);
                w.Write((byte)f.Kind);
                w.Write((byte)f.Type);
                w.Write((byte)f.ElementType);
                w.Write((ushort)(f.Kind == EcsFieldKind.NestedStruct ? f.NestedSid : f.Count));
            }
        }
        w.Write(module.Globals.Count);
        foreach (var g in module.Globals)
        {
            WriteUtf8(w, g.Name);
            w.Write((byte)g.Type);
        }
        w.Write(module.Natives.Count);
        foreach (var n in module.Natives)
            WriteUtf8(w, n.Name);
        w.Write(module.Functions.Count);
        for (int i = 0; i < module.Functions.Count; i++)
        {
            WriteUtf8(w, module.Functions[i].Name);
            w.Write((byte)module.Functions[i].NParams);
            w.Write((byte)(module.Functions[i].HasReturn ? 1 : 0));
            w.Write((ushort)module.Functions[i].NSlots);
            w.Write(codeOffsets[i]);
        }
        w.Write(codeBuf.ToArray());
        w.Write(metaBytes);
        w.Flush();

        var bytes = ms.ToArray();
        ResealCrc32(bytes);
        return bytes;
    }

    /// <summary>SEC_LINES 投影所需的指令字节布局（v3 定长：尺寸只依赖操作码，一遍即得）。</summary>
    static int[] InstructionLayout(IReadOnlyList<EcsInstruction> instructions)
    {
        int n = instructions.Count;
        var offsets = new int[n + 1];
        int off = 0;
        for (int i = 0; i < n; i++)
        {
            offsets[i] = off;
            off += InstructionCodec.SizeOf(instructions[i].Op);
        }
        offsets[n] = off;
        return offsets;
    }

    /// <summary>程序静态极值（ZeroAllocVm.md §4.1）：只记录关于自身的事实，不做容量判定。</summary>
    public readonly record struct StaticExtremes(int MaxLiteralStringUnits, int MaxLiteralArrayElems, int MaxStructSlots);

    public static StaticExtremes ComputeStaticExtremes(List<EcsFunction> functions, List<EcsConst> consts,
        List<EcsStructLayout> structs)
    {
        int maxStr = 0, maxArr = 0, maxSt = 0;
        foreach (var c in consts)
            if (c.Tag == EcsTag.String)
                maxStr = Math.Max(maxStr, (c.Str ?? "").Length);
        foreach (var f in functions)
            foreach (var ins in f.Instructions)
                if (ins.Op == EcsOpcode.NewArrV)
                    maxArr = Math.Max(maxArr, ins.B);
        foreach (var s in structs)
        {
            maxSt = Math.Max(maxSt, s.SlotCount);
            foreach (var f in s.Fields)
                if (f.Kind == EcsFieldKind.FixedArray)
                    maxArr = Math.Max(maxArr, f.Count);
        }
        return new StaticExtremes(maxStr, maxArr, maxSt);
    }

    // ============ 读取 ============

    public static EcxImage ReadImage(byte[] bytes)
    {
        if (bytes.Length < 36 || BitConverter.ToUInt32(bytes, 0) != FlatMagic)
            throw Err("magic 不是 ECX1（平铺镜像）", null);
        int format = BitConverter.ToUInt16(bytes, 4);
        if (format != FlatFormat)
            throw Err($"ECX1 format {format} 不受支持", null);
        int abi = BitConverter.ToUInt16(bytes, 6);
        ushort feats = BitConverter.ToUInt16(bytes, 8);
        int entry = BitConverter.ToUInt16(bytes, 10);
        byte flagsB = bytes[12];
        byte nameLen = bytes[13];
        int funcCount = BitConverter.ToUInt16(bytes, 14);
        int codeSize = (int)BitConverter.ToUInt32(bytes, 16);
        int dbgSize = (int)BitConverter.ToUInt32(bytes, 20);
        if (nameLen > bytes.Length - 36)
            throw Err("主模块名越界（产物损坏）", null);
        string mainModule = Encoding.UTF8.GetString(bytes, 36, nameLen);

        // CRC 校验（字段清零后全量）
        var savedCrc = BitConverter.ToUInt32(bytes, 32);
        var check = (byte[])bytes.Clone();
        Array.Clear(check, 32, 4);
        if (Crc32IEEE(check.AsSpan()) != savedCrc)
            throw Err("ECX1 全量 CRC 校验失败（产物损坏）", null);

        var modules = new List<string>();
        if (nameLen > 0)
            modules.Add(mainModule);

        var image = new EcxImage
        {
            Entry = entry,
            KeyAction = (flagsB & 0x2) != 0,
            NeedIL = (flagsB & 0x4) != 0,
            Features = feats,
            Modules = modules,
        };

        int pos = 36 + nameLen;
        var r = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
        r.BaseStream.Position = pos;

        // 常量池
        int constCount = r.ReadInt32();
        for (int i = 0; i < constCount; i++)
            image.Consts.Add(ReadConst(r));

        // 类型表
        image.Structs = ReadStructTable(r);

        // 全局表
        int globalCount = r.ReadInt32();
        for (int i = 0; i < globalCount; i++)
        {
            int moduleIdx = r.ReadByte();
            var type = (EcsTypeCode)r.ReadByte();
            image.Globals.Add(new EcsGlobal
            {
                Name = $"g{i}",
                Module = moduleIdx >= 0 && moduleIdx < image.Modules.Count ? image.Modules[moduleIdx] : "?",
                Type = type,
            });
        }

        // 原生名表
        int nativeCount = r.ReadInt32();
        for (int i = 0; i < nativeCount; i++)
            image.Natives.Add(new EcsNative { Name = ReadUtf8(r) });

        // 函数表 + .text
        if (funcCount != r.ReadInt32())
            throw Err("函数表计数与头部不一致（产物损坏）", null);
        var codeOffsets = new uint[funcCount];
        var slotCounts = new int[funcCount];
        for (int i = 0; i < funcCount; i++)
        {
            slotCounts[i] = r.ReadUInt16();
            codeOffsets[i] = r.ReadUInt32();
        }
        int codeStart = (int)r.BaseStream.Position;
        var codeBytes = new byte[codeSize];
        Array.Copy(bytes, codeStart, codeBytes, 0, codeSize);
        r.BaseStream.Position = codeStart + codeSize;

        var functions = new List<EcsFunction>(funcCount);
        for (int i = 0; i < funcCount; i++)
        {
            int end = i + 1 < funcCount ? (int)codeOffsets[i + 1] : codeSize;
            var instrs = InstructionCodec.Lift(codeBytes, (int)codeOffsets[i], end, $"func{i}");
            functions.Add(new EcsFunction
            {
                Name = $"f{i}",
                Module = mainModule,
                NSlots = slotCounts[i],
                Instructions = instrs,
                ImageIndex = i,
            });
        }
        image.Functions = functions;

        // 调试块（可选）：行号表（字节偏移→指令下标还原）+ 名字表
        if (dbgSize > 0)
        {
            int linesLen = r.ReadInt32();
            var lr = new BinaryReader(new MemoryStream(bytes, (int)r.BaseStream.Position, linesLen));
            r.BaseStream.Position += linesLen;
            int lineFuncCount = lr.ReadInt32();
            for (int fi = 0; fi < funcCount && fi < lineFuncCount; fi++)
            {
                int pairs = lr.ReadInt32();
                // 起始字节表（函数内相对偏移，与写入侧 InstructionLayout 同基准）
                var startsList = new List<int>();
                int acc = 0;
                foreach (var ins in functions[fi].Instructions)
                {
                    startsList.Add(acc);
                    acc += InstructionCodec.SizeOf(ins.Op);
                }
                var table = new List<int>(pairs * 2);
                for (int k = 0; k < pairs; k++)
                {
                    int byteOff = lr.ReadInt32();
                    int line = lr.ReadInt32();
                    int idx = startsList.BinarySearch(byteOff);
                    if (idx >= 0)
                    {
                        table.Add(idx);
                        table.Add(line);
                    }
                }
                functions[fi].LineTable = table;
            }
            int namesLen = r.ReadInt32();
            var nr = new BinaryReader(new MemoryStream(bytes, (int)r.BaseStream.Position, namesLen));
            r.BaseStream.Position += namesLen;
            int nameCount = nr.ReadInt32();
            for (int i = 0; i < nameCount && i < functions.Count; i++)
                functions[i].Name = ReadUtf8(nr);
            for (int i = functions.Count; i < nameCount; i++)
            {
                if (i - functions.Count < image.Globals.Count)
                    image.Globals[i - functions.Count].Name = ReadUtf8(nr);
                else
                    ReadUtf8(nr);
            }
        }
        else
        {
            for (int i = 0; i < functions.Count; i++)
                functions[i].Name = i == entry ? "<main>" : $"f{i}";
        }

        if (entry < 0 || entry >= functions.Count)
            throw Err($"entry 越界 {entry}", null);
        if (functions[entry].Name == "<main>" || string.IsNullOrEmpty(functions[entry].Name))
            functions[entry].Name = "<main>";
        EcxPipeline.ComputeResourceRequirements(image);
        return image;
    }

    public static ModuleArtifact ReadModule(byte[] bytes)
    {
        if (bytes.Length < 36 || BitConverter.ToUInt32(bytes, 0) != ModuleFlatMagic)
            throw Err("magic 不是 ECM1（平铺模块缓存；旧 ECSC kind=module 产物已断代，按缓存未命中重编）", null);
        int format = BitConverter.ToUInt16(bytes, 4);
        if (format != ModuleFlatFormat)
            throw Err($"ECM1 format {format} 不受支持", null);

        // CRC 校验（字段清零后全量）
        var savedCrc = BitConverter.ToUInt32(bytes, 32);
        var check = (byte[])bytes.Clone();
        Array.Clear(check, 32, 4);
        if (Crc32IEEE(check.AsSpan()) != savedCrc)
            throw Err("ECM1 全量 CRC 校验失败（产物损坏）", null);

        byte flags = bytes[8];
        int nameLen = bytes[9];
        int funcCount = BitConverter.ToUInt16(bytes, 10);
        int codeSize = (int)BitConverter.ToUInt32(bytes, 12);
        int initFid = BitConverter.ToInt32(bytes, 16);
        int metaSize = (int)BitConverter.ToUInt32(bytes, 20);
        if (nameLen > bytes.Length - 36)
            throw Err("模块名越界（产物损坏）", null);
        var name = Encoding.UTF8.GetString(bytes, 36, nameLen);

        var r = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
        r.BaseStream.Position = 36 + nameLen;

        // 常量池（ModulePool 重建去重索引）
        var pool = new ModulePool();
        int constCount = r.ReadInt32();
        for (int i = 0; i < constCount; i++)
            pool.Add(ReadConst(r));

        var structs = ReadStructTable(r);

        // 全局表（模块味：名字 + 类型；Module 归一为本模块名）
        var globals = new List<EcsGlobal>();
        int globalCount = r.ReadInt32();
        for (int i = 0; i < globalCount; i++)
            globals.Add(new EcsGlobal { Name = ReadUtf8(r), Module = name, Type = (EcsTypeCode)r.ReadByte() });

        var natives = new List<EcsNative>();
        int nativeCount = r.ReadInt32();
        for (int i = 0; i < nativeCount; i++)
            natives.Add(new EcsNative { Name = ReadUtf8(r) });

        // 函数表 + .text
        if (funcCount != r.ReadInt32())
            throw Err("函数表计数与头部不一致（产物损坏）", null);
        var metas = new List<(string Name, int NParams, bool HasReturn, int NSlots, uint Off)>(funcCount);
        for (int i = 0; i < funcCount; i++)
        {
            var fname = ReadUtf8(r);
            int nparams = r.ReadByte();
            bool hasret = r.ReadByte() != 0;
            int nslots = r.ReadUInt16();
            uint off = r.ReadUInt32();
            metas.Add((fname, nparams, hasret, nslots, off));
        }
        int codeStart = (int)r.BaseStream.Position;
        var codeBytes = new byte[codeSize];
        Array.Copy(bytes, codeStart, codeBytes, 0, codeSize);
        r.BaseStream.Position = codeStart + codeSize;

        var functions = new List<EcsFunction>(funcCount);
        var startsPerFunc = new List<List<int>>(funcCount);
        for (int i = 0; i < funcCount; i++)
        {
            int end = i + 1 < funcCount ? (int)metas[i + 1].Off : codeSize;
            var (fname, nparams, hasret, nslots, off) = metas[i];
            functions.Add(new EcsFunction
            {
                Name = fname,
                Module = name,
                NParams = nparams,
                NSlots = nslots,
                HasReturn = hasret,
                Instructions = InstructionCodec.LiftWithStarts(codeBytes, (int)off, end, out var starts, fname),
            });
            for (int k = 0; k < starts.Count; k++)
                starts[k] -= (int)off;   // 行号表基准 = 函数内相对偏移（写入侧 InstructionLayout 同基准）
            startsPerFunc.Add(starts);
        }

        // 元数据尾块：[linesLen][lines][imports][exports][ilnames][ifaceLen][iface]
        long metaEnd = (long)r.BaseStream.Position + metaSize;
        int linesLen = r.ReadInt32();
        if (linesLen > 0)
        {
            using var lr = new BinaryReader(new MemoryStream(bytes, (int)r.BaseStream.Position, linesLen));
            r.BaseStream.Position += linesLen;
            int lineFuncCount = lr.ReadInt32();
            for (int fi = 0; fi < functions.Count && fi < lineFuncCount; fi++)
            {
                int pairs = lr.ReadInt32();
                var starts = startsPerFunc[fi];
                var table = functions[fi].LineTable;
                for (int k = 0; k < pairs; k++)
                {
                    int byteOff = lr.ReadInt32();
                    int line = lr.ReadInt32();
                    int idx = starts.BinarySearch(byteOff);
                    if (idx >= 0)
                    {
                        table.Add(idx);
                        table.Add(line);
                    }
                }
            }
        }
        var imports = new List<EcsImport>();
        int importCount = r.ReadInt32();
        for (int i = 0; i < importCount; i++)
            imports.Add(new EcsImport { Name = ReadUtf8(r), NParams = r.ReadByte(), HasReturn = r.ReadByte() != 0 });
        var exports = new List<EcsExport>();
        int exportCount = r.ReadInt32();
        for (int i = 0; i < exportCount; i++)
        {
            var ename = ReadUtf8(r);
            var localFid = (int)r.ReadUInt32();
            var nparams = r.ReadByte();
            var hasret = r.ReadByte() != 0;
            exports.Add(new EcsExport { Name = ename, LocalFid = localFid, NParams = nparams, HasReturn = hasret });
        }
        var ilNames = new List<string>();
        int ilCount = r.ReadInt32();
        for (int i = 0; i < ilCount; i++)
            ilNames.Add(ReadUtf8(r));
        ModuleInterface? iface = null;
        int ifaceLen = r.ReadInt32();
        if (ifaceLen > 0)
        {
            using var ir = new BinaryReader(new MemoryStream(bytes, (int)r.BaseStream.Position, ifaceLen));
            r.BaseStream.Position += ifaceLen;
            iface = ModuleInterfaceFormat.Read(ir);
        }
        if (r.BaseStream.Position != metaEnd || r.BaseStream.Position != bytes.Length)
            throw Err("ECM1 元数据尾块长度不符（产物损坏）", null);

        return new ModuleArtifact
        {
            Name = name,
            Functions = functions,
            Pool = pool,
            Imports = imports,
            Exports = exports,
            Globals = globals,
            Natives = natives,
            Structs = structs,
            ILNames = ilNames,
            HasEval = (flags & 0x1) != 0,
            HasInit = (flags & 0x2) != 0,
            InitFid = initFid,
            KeyAction = (flags & 0x4) != 0,
            NeedIL = (flags & 0x8) != 0,
            Interface = iface,
        };
    }

    // ---- 读取辅助 ----

    /// <summary>结构体类型表（ECX1/ECM1 共用布局）：u32 计数 + 每项 utf8 名 + 字段集；读后展开槽布局。</summary>
    static List<EcsStructLayout> ReadStructTable(BinaryReader r)
    {
        int count = r.ReadInt32();
        var structs = new List<EcsStructLayout>(count);
        for (int i = 0; i < count; i++)
        {
            var sname = ReadUtf8(r);
            int nfields = r.ReadByte();
            var fields = new EcsFieldLayout[nfields];
            for (int f = 0; f < nfields; f++)
            {
                var fname = ReadUtf8(r);
                var kind = (EcsFieldKind)r.ReadByte();
                var type = (EcsTypeCode)r.ReadByte();
                var elem = (EcsTypeCode)r.ReadByte();
                var ext = r.ReadUInt16();
                fields[f] = new EcsFieldLayout
                {
                    Name = fname, Kind = kind, Type = type, ElementType = elem,
                    Count = kind == EcsFieldKind.NestedStruct ? 0 : ext,
                    NestedSid = kind == EcsFieldKind.NestedStruct ? ext : 0,
                };
            }
            structs.Add(new EcsStructLayout { Name = sname, Fields = fields.ToImmutableArray() });
        }
        ExpandLayouts(structs);
        return structs;
    }

    /// <summary>布局展开须递归：嵌套类型的 sid 可能大于父结构体（名字字母序），单遍扫描会错位。</summary>
    internal static void ExpandLayouts(List<EcsStructLayout> structs)
    {
        var bySid = new Dictionary<int, EcsStructLayout>();
        for (int s = 0; s < structs.Count; s++)
            bySid[s] = structs[s];
        var computing = new HashSet<int>();
        int SlotCountOf(int sid)
        {
            var layout = structs[sid];
            if (layout.SlotCount > 0)
                return layout.SlotCount;
            if (!computing.Add(sid))
                throw Err($"结构体 {layout.Name} 存在自嵌套环（产物损坏）", null);
            int total = 0;
            foreach (var f in layout.Fields)
            {
                f.SlotOffset = total;
                total += f.Kind switch
                {
                    EcsFieldKind.FixedArray => f.Count,
                    EcsFieldKind.NestedStruct => bySid.ContainsKey(f.NestedSid) ? SlotCountOf(f.NestedSid) : 1,
                    _ => 1,
                };
            }
            computing.Remove(sid);
            layout.SlotCount = total;
            return total;
        }
        for (int s = 0; s < structs.Count; s++)
            SlotCountOf(s);
    }


    // ---- 基础编码 ----

    internal static void WriteConst(BinaryWriter w, EcsConst c)
    {
        w.Write(c.Tag);
        switch (c.Tag)
        {
            case EcsTag.Int:
            case EcsTag.UInt:
                w.Write((int)c.Int64);
                break;
            case EcsTag.UInt64:
            case EcsTag.Ptr:
                w.Write(c.Int64);
                break;
            case EcsTag.Double:
                w.Write(c.Float64);
                break;
            case EcsTag.String:
                {
                    var units = Encoding.Unicode.GetBytes(c.Str ?? "");
                    if (units.Length / 2 > 0xFFFF)
                        throw Err($"常量池字符串超过 65535 code units: {c.Str?[..64]}…", null);
                    w.Write((ushort)(units.Length / 2));
                    w.Write(units);
                    break;
                }
            default:
                throw Err($"不支持的常量标签 {c.Tag}", null);
        }
    }

    static EcsConst ReadConst(BinaryReader r)
    {
        var tag = r.ReadByte();
        switch (tag)
        {
            case EcsTag.Int:
            case EcsTag.UInt:
                return new EcsConst { Tag = tag, Int64 = r.ReadInt32() };
            case EcsTag.UInt64:
            case EcsTag.Ptr:
                return new EcsConst { Tag = tag, Int64 = r.ReadInt64() };
            case EcsTag.Double:
                return new EcsConst { Tag = tag, Float64 = r.ReadDouble() };
            case EcsTag.String:
                {
                    int units = r.ReadUInt16();
                    var bytes = r.ReadBytes(units * 2);
                    return new EcsConst { Tag = tag, Str = Encoding.Unicode.GetString(bytes) };
                }
            default:
                throw Err($"常量标签非法 {tag}", null);
        }
    }

    internal static void WriteUtf8(BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        if (bytes.Length > 0xFFFF)
            throw Err($"标识符过长: {s}", null);
        w.Write((ushort)bytes.Length);
        w.Write(bytes);
    }

    internal static string ReadUtf8(BinaryReader r)
    {
        int len = r.ReadUInt16();
        return Encoding.UTF8.GetString(r.ReadBytes(len));
    }

    internal static int ReadCount(BinaryReader r)
    {
        var v = r.ReadUInt32();
        if (v > 100_000_000)
            throw Err("容器计数异常（可能已损坏）", null);
        return (int)v;
    }

    internal static BytecodeException Err(string message, string? context)
        => new(new[] { new BytecodeDiagnostic(context == null ? message : $"{context}: {message}", null, 0) });

    // CRC-32（IEEE 802.3，反射多项式 0xEDB88320）
    internal static uint Crc32IEEE(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
                crc = (crc >> 1) ^ (0xEDB88320u & ~(uint)((crc & 1) - 1));
        }
        return ~crc;
    }

    /// <summary>
    /// 重算并写回 ECX1 全量 CRC（u32@32，计算前先清零该字段）。
    /// 供镜像字节被外部工具/测试改写后重封；加载期 CRC 校验覆盖整个镜像，补丁不重封必被拒。
    /// </summary>
    public static void ResealCrc32(byte[] flatImage)
    {
        Array.Clear(flatImage, 32, 4);
        uint crc = Crc32IEEE(flatImage.AsSpan());
        flatImage[32] = (byte)crc;
        flatImage[33] = (byte)(crc >> 8);
        flatImage[34] = (byte)(crc >> 16);
        flatImage[35] = (byte)(crc >> 24);
    }
}
