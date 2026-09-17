using System.Collections.Immutable;
using System.Text;

namespace EasyCon.Script.Bytecode;

/// <summary>
/// .ecm 模块产物序列化（docs/EcmEcxFormat.md ECM 章节）。小端；元数据 UTF-8。
/// </summary>
public static class EcmFormat
{
    public const uint Magic = 0x324D4345;   // "ECM2"
    public const ushort Version = 4;        // v4：函数节增行号表（LineTable，pc→源码行，诊断/运行错误映射用）

    public static byte[] Write(ModuleArtifact module)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write(Magic);
        w.Write(Version);
        w.Write((ushort)(module.HasEval ? 1 : 0));
        // v3 链接标志：&lt;init&gt; 合成与宿主能力判定（KeyAction/NeedIL）必须跨缓存存活
        w.Write((byte)((module.HasEval ? 1 : 0)
            | (module.HasInit ? 2 : 0)
            | (module.KeyAction ? 4 : 0)
            | (module.NeedIL ? 8 : 0)));
        w.Write(module.InitFid);
        WriteUtf8(w, module.Name);

        // ---- 接口区（长度前置，v2；无接口 = 0）----
        if (module.Interface is { } iface)
        {
            using var ibuf = new MemoryStream();
            using (var iw = new BinaryWriter(ibuf, Encoding.UTF8, leaveOpen: true))
                ModuleInterfaceFormat.Write(iw, iface);
            w.Write((uint)ibuf.Length);
            w.Write(ibuf.ToArray());
        }
        else
        {
            w.Write(0u);
        }

        // 类型表快照
        w.Write(module.Structs.Count);
        foreach (var s in module.Structs)
        {
            WriteUtf8(w, s.Name);
            w.Write((byte)s.Fields.Length);
            foreach (var f in s.Fields)
            {
                WriteUtf8(w, f.Name);
                w.Write((byte)f.Kind);
                w.Write((byte)f.Type);
                w.Write((byte)f.ElementType);
                w.Write((ushort)(f.Kind == EcsFieldKind.NestedStruct ? f.NestedSid : f.Count));   // ext 槽：FixedArray=元素数；NestedStruct=嵌套 sid（内存表示存 NestedSid）
            }
        }

        // 模块私有全局
        w.Write(module.Globals.Count);
        foreach (var g in module.Globals)
        {
            WriteUtf8(w, g.Name);
            w.Write((byte)g.Type);
        }

        // 原生名表
        w.Write(module.Natives.Count);
        foreach (var n in module.Natives)
            WriteUtf8(w, n.Name);

        // 函数表 + 代码区
        w.Write(module.Functions.Count);
        uint codeOffset = 0;
        foreach (var f in module.Functions)
        {
            WriteUtf8(w, f.Name);
            w.Write((byte)f.NParams);
            w.Write((byte)f.NSlots);
            w.Write(f.HasReturn ? (byte)1 : (byte)0);
            w.Write(codeOffset);
            w.Write((uint)f.Code.Count);
            codeOffset += (uint)f.Code.Count;
        }
        w.Write(codeOffset);
        foreach (var f in module.Functions)
            foreach (var word in f.Code)
                w.Write(word);

        // 常量池
        w.Write(module.Pool.Consts.Count);
        foreach (var c in module.Pool.Consts)
            WriteConst(w, c);

        // 导入 / 导出
        w.Write(module.Imports.Count);
        foreach (var i in module.Imports)
        {
            WriteUtf8(w, i.Name);
            w.Write((byte)i.NParams);
            w.Write(i.HasReturn ? (byte)1 : (byte)0);
        }
        w.Write(module.Exports.Count);
        foreach (var e in module.Exports)
        {
            WriteUtf8(w, e.Name);
            w.Write((uint)e.LocalFid);
            w.Write((byte)e.NParams);
            w.Write(e.HasReturn ? (byte)1 : (byte)0);
        }

        // IL 名
        w.Write(module.ILNames.Count);
        foreach (var il in module.ILNames)
            WriteUtf8(w, il);

        // 行号表（v4；稀疏交错 [pc, line, ...]，仅诊断用不进 MCU .ecx）
        w.Write(module.Functions.Count);
        foreach (var f in module.Functions)
        {
            w.Write(f.LineTable.Count);
            foreach (var v in f.LineTable)
                w.Write(v);
        }

        w.Flush();
        return ms.ToArray();
    }

    public static ModuleArtifact Read(byte[] bytes)
    {
        using var r = new BinaryReader(new MemoryStream(bytes));
        if (r.ReadUInt32() != Magic)
            throw new BytecodeException(new[] { new BytecodeDiagnostic("ECM 魔数不符", null, 0) });
        ushort fileVersion = r.ReadUInt16();
        if (fileVersion != Version)
            throw new BytecodeException(new[] { new BytecodeDiagnostic($"ECM 版本不符 {fileVersion}（期望 {Version}）", null, 0) });
        bool hasEval = r.ReadUInt16() != 0;
        bool hasInit = false, keyAction = false, needIL = false;
        int initFid = -1;
        if (fileVersion >= 3)
        {
            var flags = r.ReadByte();
            hasInit = (flags & 2) != 0;
            keyAction = (flags & 4) != 0;
            needIL = (flags & 8) != 0;
            initFid = r.ReadInt32();
        }
        var name = ReadUtf8(r);

        // ---- 接口区（v2；v1 无此节）----
        ModuleInterface? iface = null;
        if (fileVersion >= 2)
        {
            uint ifaceLen = r.ReadUInt32();
            if (ifaceLen > 0)
            {
                var ifaceBytes = r.ReadBytes((int)ifaceLen);
                using var ir = new BinaryReader(new MemoryStream(ifaceBytes));
                iface = ModuleInterfaceFormat.Read(ir);
            }
        }

        // 类型表（布局偏移在读取完成后统一展开）
        var structs = new List<EcsStructLayout>();
        int structCount = ReadCount(r);
        for (int s = 0; s < structCount; s++)
        {
            var sname = ReadUtf8(r);
            int fieldCount = r.ReadByte();
            var fields = new EcsFieldLayout[fieldCount];
            for (int fi = 0; fi < fieldCount; fi++)
            {
                var fname = ReadUtf8(r);
                var kind = (EcsFieldKind)r.ReadByte();
                var type = (EcsTypeCode)r.ReadByte();
                var elem = (EcsTypeCode)r.ReadByte();
                var ext = r.ReadUInt16();
                fields[fi] = new EcsFieldLayout
                {
                    Name = fname,
                    Kind = kind,
                    Type = type,
                    ElementType = elem,
                    Count = kind == EcsFieldKind.NestedStruct ? 0 : ext,
                    NestedSid = kind == EcsFieldKind.NestedStruct ? ext : 0,
                };
            }
            structs.Add(new EcsStructLayout { Name = sname, Fields = fields.ToImmutableArray() });
        }
        var bySid = new Dictionary<int, EcsStructLayout>();
        for (int s = 0; s < structs.Count; s++)
            bySid[s] = structs[s];
        // 布局展开须递归：sid 按声明名字母序分配，嵌套类型的 sid 可能大于父结构体，
        // 单遍扫描会取到尚未展开的 SlotCount（SlotOffset/SlotCount 全盘错位）
        var computing = new HashSet<int>();
        int SlotCountOf(int sid)
        {
            var layout = structs[sid];
            if (layout.SlotCount > 0)
                return layout.SlotCount;
            if (!computing.Add(sid))
                throw new BytecodeException(new[] { new BytecodeDiagnostic($"结构体 {layout.Name} 存在自嵌套环（产物损坏）", name, 0) });
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

        // 模块私有全局
        var globals = new List<EcsGlobal>();
        int globalCount = ReadCount(r);
        for (int g = 0; g < globalCount; g++)
            globals.Add(new EcsGlobal { Name = ReadUtf8(r), Module = name, Type = (EcsTypeCode)r.ReadByte() });

        // 原生名表
        var natives = new List<EcsNative>();
        int nativeCount = ReadCount(r);
        for (int n = 0; n < nativeCount; n++)
            natives.Add(new EcsNative { Name = ReadUtf8(r) });

        // 函数表（记录 code_off/code_words）+ 代码区
        var funcMetas = new List<(string Name, byte NParams, byte NSlots, bool HasReturn, uint Off, uint Words)>();
        int funcCount = ReadCount(r);
        for (int fi = 0; fi < funcCount; fi++)
        {
            var fname = ReadUtf8(r);
            var nparams = r.ReadByte();
            var nslots = r.ReadByte();
            var hasret = r.ReadByte() != 0;
            var off = r.ReadUInt32();
            var words = r.ReadUInt32();
            funcMetas.Add((fname, nparams, nslots, hasret, off, words));
        }
        int codeWords = ReadCount(r);
        var code = new List<uint>(codeWords);
        for (int i = 0; i < codeWords; i++)
            code.Add(r.ReadUInt32());

        var functions = new List<EcsFunction>();
        foreach (var meta in funcMetas)
        {
            functions.Add(new EcsFunction
            {
                Name = meta.Name,
                Module = name,
                NParams = meta.NParams,
                NSlots = meta.NSlots,
                HasReturn = meta.HasReturn,
                Code = code.Skip((int)meta.Off).Take((int)meta.Words).ToList(),
            });
        }

        // 常量池
        var pool = new ModulePool();
        int constCount = ReadCount(r);
        for (int c = 0; c < constCount; c++)
            pool.Add(ReadConst(r));

        // 导入 / 导出
        var imports = new List<EcsImport>();
        int importCount = ReadCount(r);
        for (int i = 0; i < importCount; i++)
            imports.Add(new EcsImport { Name = ReadUtf8(r), NParams = r.ReadByte(), HasReturn = r.ReadByte() != 0 });

        var exports = new List<EcsExport>();
        int exportCount = ReadCount(r);
        for (int e = 0; e < exportCount; e++)
        {
            var ename = ReadUtf8(r);
            var localFid = (int)r.ReadUInt32();
            var nparams = r.ReadByte();
            var hasret = r.ReadByte() != 0;
            exports.Add(new EcsExport { Name = ename, LocalFid = localFid, NParams = nparams, HasReturn = hasret });
        }

        // IL 名
        var ilNames = new List<string>();
        int ilCount = ReadCount(r);
        for (int i = 0; i < ilCount; i++)
            ilNames.Add(ReadUtf8(r));

        // 行号表（v4；与 Write 尾节同序）
        if (fileVersion >= 4)
        {
            int lineFuncCount = ReadCount(r);
            for (int fi = 0; fi < lineFuncCount && fi < functions.Count; fi++)
            {
                int count = ReadCount(r);
                for (int i = 0; i < count; i++)
                    functions[fi].LineTable.Add(r.ReadInt32());
            }
        }

        if (r.BaseStream.Position != r.BaseStream.Length)
            throw new BytecodeException(new[] { new BytecodeDiagnostic("ECM 存在未消费的尾部字节", name, 0) });

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
            HasEval = hasEval,
            HasInit = hasInit,
            InitFid = initFid,
            KeyAction = keyAction,
            NeedIL = needIL,
            Interface = iface,
        };
    }

    internal static int ReadCount(BinaryReader r)
    {
        var v = r.ReadUInt32();
        if (v > 100_000_000)
            throw new BytecodeException(new[] { new BytecodeDiagnostic("ECM 计数异常（可能已损坏）", null, 0) });
        return (int)v;
    }

    static void WriteConst(BinaryWriter w, EcsConst c)
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
                    w.Write((ushort)(units.Length / 2));
                    w.Write(units);
                    break;
                }
            default:
                throw new BytecodeException(new[] { new BytecodeDiagnostic($"不支持的常量标签 {c.Tag}", null, 0) });
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
                throw new BytecodeException(new[] { new BytecodeDiagnostic($"ECM 常量标签非法 {tag}", null, 0) });
        }
    }

    internal static void WriteUtf8(BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        if (bytes.Length > 0xFFFF)
            throw new BytecodeException(new[] { new BytecodeDiagnostic($"标识符过长: {s}", null, 0) });
        w.Write((ushort)bytes.Length);
        w.Write(bytes);
    }

    internal static string ReadUtf8(BinaryReader r)
    {
        int len = r.ReadUInt16();
        return Encoding.UTF8.GetString(r.ReadBytes(len));
    }
}