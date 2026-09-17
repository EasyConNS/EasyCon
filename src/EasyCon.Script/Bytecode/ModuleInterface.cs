using EasyCon.Script.Binding;
using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace EasyCon.Script.Bytecode;

/// <summary>
/// 模块接口（.ecm 接口区的内存表示，docs/ModuleSystem.md §4）。
/// 完备性义务（§4.6）：凡是影响消费者绑定/类型检查/代码生成的依赖信息必须进接口；
/// 函数体、局部状态、私有全局留代码区。
/// </summary>
public sealed class ModuleInterface
{
    public required string Name;
    public required string CompilerVersion;
    /// <summary>接口哈希（SHA-256 十六进制，§4.5）：缓存失效与链接闭包的唯一依据。</summary>
    public required string InterfaceHash;
    public required List<ExportedFunction> Functions;
    public required List<ExportedStruct> Structs;
    public required List<string> ILNames;
    /// <summary>模块是否有顶层语句（链接器据此合成 &lt;init:module&gt;，§5.4-4）。</summary>
    public bool HasInit;
    public required List<ModuleDependency> Dependencies;

    /// <summary>当前编译器语义版本（进接口哈希；签名格式变化时递增）。</summary>
    public static string CurrentCompilerVersion =>
        typeof(ModuleInterface).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>结构化相等（roundtrip 与测试用；DeclLine 不参与）。</summary>
    public bool DeepEquals(ModuleInterface? other)
    {
        if (other is null) return false;
        if (Name != other.Name || CompilerVersion != other.CompilerVersion
            || InterfaceHash != other.InterfaceHash || HasInit != other.HasInit)
            return false;
        if (Functions.Count != other.Functions.Count
            || Structs.Count != other.Structs.Count
            || ILNames.Count != other.ILNames.Count
            || Dependencies.Count != other.Dependencies.Count)
            return false;
        for (int i = 0; i < Functions.Count; i++)
            if (!Functions[i].DeepEquals(other.Functions[i])) return false;
        for (int i = 0; i < Structs.Count; i++)
            if (!Structs[i].DeepEquals(other.Structs[i])) return false;
        for (int i = 0; i < ILNames.Count; i++)
            if (ILNames[i] != other.ILNames[i]) return false;
        for (int i = 0; i < Dependencies.Count; i++)
            if (Dependencies[i].Name != other.Dependencies[i].Name
                || Dependencies[i].InterfaceHash != other.Dependencies[i].InterfaceHash)
                return false;
        return true;
    }
}

/// <summary>导出函数（同名多条 = 重载，按参数个数区分）。</summary>
public sealed class ExportedFunction
{
    public required string Name;
    public required List<ExportedParam> Params;
    public required string ReturnTypeName;
    public bool IsExtern;
    /// <summary>extern 的 DLL/库名；非 extern 为 null。</summary>
    public string? ExternLibrary;
    public string? ExternalName;
    /// <summary>声明行号（诊断定位；刻意不参与接口哈希，§4.5 排除项）。</summary>
    public int DeclLine;

    public bool DeepEquals(ExportedFunction? other)
    {
        if (other is null || Name != other.Name || ReturnTypeName != other.ReturnTypeName
            || IsExtern != other.IsExtern || ExternLibrary != other.ExternLibrary
            || ExternalName != other.ExternalName || Params.Count != other.Params.Count)
            return false;
        for (int i = 0; i < Params.Count; i++)
            if (!Params[i].DeepEquals(other.Params[i])) return false;
        return true;
    }
}

public sealed class ExportedParam
{
    public required string Name;
    public required string TypeName;
    public int Ordinal;
    public bool HasDefault;
    /// <summary>默认值载荷（int/double/string/bool，§4.6-③；调用点物化为字面量）。</summary>
    public object? DefaultValue;

    public bool DeepEquals(ExportedParam? other)
    {
        if (other is null || Name != other.Name || TypeName != other.TypeName
            || Ordinal != other.Ordinal || HasDefault != other.HasDefault)
            return false;
        if (!HasDefault)
            return DefaultValue is null && other.DefaultValue is null;
        return DefaultValueEquals(DefaultValue, other.DefaultValue);
    }

    internal static bool DefaultValueEquals(object? a, object? b)
        => (a, b) switch
        {
            (null, null) => true,
            (int x, int y) => x == y,
            (double x, double y) => x.Equals(y),
            (string x, string y) => x == y,
            (bool x, bool y) => x == y,
            _ => false,
        };
}

public sealed class ExportedStruct
{
    public required string Name;
    public required List<ExportedField> Fields;

    public bool DeepEquals(ExportedStruct? other)
    {
        if (other is null || Name != other.Name || Fields.Count != other.Fields.Count)
            return false;
        for (int i = 0; i < Fields.Count; i++)
            if (Fields[i].Name != other.Fields[i].Name || Fields[i].TypeName != other.Fields[i].TypeName)
                return false;
        return true;
    }
}

public sealed class ExportedField
{
    public required string Name;
    /// <summary>ScriptType.Name 规范形（"int"、"int[3]"、结构体名；布局由消费端重算，§4.3/Q1）。</summary>
    public required string TypeName;
}

public sealed class ModuleDependency
{
    public required string Name;
    public required string InterfaceHash;
}

/// <summary>接口哈希（§4.5）：对导出签名、类型表、IL 名、init 标记的 SHA-256；
/// 刻意排除行号、实现体信息、全局变量与依赖表（依赖哈希由缓存键聚合，§7.2）。</summary>
public static class InterfaceHasher
{
    public static string Compute(ModuleInterface iface)
    {
        var sb = new StringBuilder();
        AppendItem(sb, "v" + iface.CompilerVersion);

        var funcLines = new List<string>();
        foreach (var f in iface.Functions)
        {
            var sbf = new StringBuilder();
            sbf.Append("F");
            AppendItem(sbf, f.Name);
            AppendItem(sbf, f.ReturnTypeName);
            sbf.Append(f.IsExtern ? '1' : '0');
            if (f.IsExtern)
            {
                AppendItem(sbf, f.ExternLibrary ?? "");
                AppendItem(sbf, f.ExternalName ?? "");
            }
            foreach (var p in f.Params)
            {
                AppendItem(sbf, p.Name);
                AppendItem(sbf, p.TypeName);
                sbf.Append(p.Ordinal).Append('|');
                sbf.Append(p.HasDefault ? '1' : '0');
                if (p.HasDefault)
                    AppendItem(sbf, DefaultPayloadText(p.DefaultValue));
            }
            funcLines.Add(sbf.ToString());
        }
        funcLines.Sort(StringComparer.Ordinal);
        foreach (var line in funcLines)
            AppendItem(sb, line);

        var structLines = new List<string>();
        foreach (var s in iface.Structs)
        {
            var sbs = new StringBuilder();
            sbs.Append("S");
            AppendItem(sbs, s.Name);
            foreach (var fld in s.Fields)
            {
                AppendItem(sbs, fld.Name);
                AppendItem(sbs, fld.TypeName);
            }
            structLines.Add(sbs.ToString());
        }
        structLines.Sort(StringComparer.Ordinal);
        foreach (var line in structLines)
            AppendItem(sb, line);

        foreach (var il in iface.ILNames)
            AppendItem(sb, il);
        sb.Append(iface.HasInit ? "INIT1" : "INIT0");

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        var hex = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
            hex.Append(b.ToString("x2"));
        return hex.ToString();
    }

    /// <summary>默认值载荷的规范文本（R2：int/double/string/bool 之外即编码器错误）。</summary>
    static string DefaultPayloadText(object? value) => value switch
    {
        null => "n",
        int v => "i" + v.ToString(System.Globalization.CultureInfo.InvariantCulture),
        double v => "d" + v.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        string v => "s" + v,
        bool v => "b" + (v ? '1' : '0'),
        _ => throw new BytecodeException(new[] { new BytecodeDiagnostic($"默认值载荷类型不支持: {value.GetType().Name}", null, 0) }),
    };

    /// <summary>长度前缀规范项（防拼接歧义：任意内容（含分隔符）无碰撞）。</summary>
    static void AppendItem(StringBuilder sb, string s)
        => sb.Append(s.Length).Append(':').Append(s);
}

/// <summary>
/// 接口序列化（ECM v2 接口区，EcmFormat 内嵌；字节布局小端、UTF-8 长度前置字符串）。
/// </summary>
public static class ModuleInterfaceFormat
{
    public static void Write(BinaryWriter w, ModuleInterface iface)
    {
        EcmFormat.WriteUtf8(w, iface.Name);
        EcmFormat.WriteUtf8(w, iface.CompilerVersion);
        EcmFormat.WriteUtf8(w, iface.InterfaceHash);
        w.Write(iface.HasInit);

        w.Write(iface.Functions.Count);
        foreach (var f in iface.Functions)
        {
            EcmFormat.WriteUtf8(w, f.Name);
            EcmFormat.WriteUtf8(w, f.ReturnTypeName);
            w.Write(f.IsExtern);
            if (f.IsExtern)
            {
                EcmFormat.WriteUtf8(w, f.ExternLibrary ?? "");
                EcmFormat.WriteUtf8(w, f.ExternalName ?? "");
            }
            w.Write(f.DeclLine);
            w.Write(f.Params.Count);
            foreach (var p in f.Params)
            {
                EcmFormat.WriteUtf8(w, p.Name);
                EcmFormat.WriteUtf8(w, p.TypeName);
                w.Write((byte)p.Ordinal);
                w.Write(p.HasDefault);
                if (p.HasDefault)
                    WriteDefault(w, p.DefaultValue);
            }
        }

        w.Write(iface.Structs.Count);
        foreach (var s in iface.Structs)
        {
            EcmFormat.WriteUtf8(w, s.Name);
            w.Write(s.Fields.Count);
            foreach (var fld in s.Fields)
            {
                EcmFormat.WriteUtf8(w, fld.Name);
                EcmFormat.WriteUtf8(w, fld.TypeName);
            }
        }

        w.Write(iface.ILNames.Count);
        foreach (var il in iface.ILNames)
            EcmFormat.WriteUtf8(w, il);

        w.Write(iface.Dependencies.Count);
        foreach (var d in iface.Dependencies)
        {
            EcmFormat.WriteUtf8(w, d.Name);
            EcmFormat.WriteUtf8(w, d.InterfaceHash);
        }
    }

    public static ModuleInterface Read(BinaryReader r)
    {
        var name = EcmFormat.ReadUtf8(r);
        var version = EcmFormat.ReadUtf8(r);
        var hash = EcmFormat.ReadUtf8(r);
        bool hasInit = r.ReadBoolean();

        int funcCount = EcmFormat.ReadCount(r);
        var funcs = new List<ExportedFunction>(funcCount);
        for (int i = 0; i < funcCount; i++)
        {
            var fname = EcmFormat.ReadUtf8(r);
            var ret = EcmFormat.ReadUtf8(r);
            bool isExtern = r.ReadBoolean();
            string? lib = null, extName = null;
            if (isExtern)
            {
                lib = EcmFormat.ReadUtf8(r);
                extName = EcmFormat.ReadUtf8(r);
            }
            int declLine = r.ReadInt32();
            int paramCount = EcmFormat.ReadCount(r);
            var ps = new List<ExportedParam>(paramCount);
            for (int p = 0; p < paramCount; p++)
            {
                var pname = EcmFormat.ReadUtf8(r);
                var ptype = EcmFormat.ReadUtf8(r);
                int ordinal = r.ReadByte();
                bool hasDefault = r.ReadBoolean();
                object? def = hasDefault ? ReadDefault(r) : null;
                ps.Add(new ExportedParam { Name = pname, TypeName = ptype, Ordinal = ordinal, HasDefault = hasDefault, DefaultValue = def });
            }
            funcs.Add(new ExportedFunction
            {
                Name = fname,
                ReturnTypeName = ret,
                IsExtern = isExtern,
                ExternLibrary = lib,
                ExternalName = extName,
                DeclLine = declLine,
                Params = ps,
            });
        }

        int structCount = EcmFormat.ReadCount(r);
        var structs = new List<ExportedStruct>(structCount);
        for (int i = 0; i < structCount; i++)
        {
            var sname = EcmFormat.ReadUtf8(r);
            int fieldCount = EcmFormat.ReadCount(r);
            var fields = new List<ExportedField>(fieldCount);
            for (int f = 0; f < fieldCount; f++)
                fields.Add(new ExportedField { Name = EcmFormat.ReadUtf8(r), TypeName = EcmFormat.ReadUtf8(r) });
            structs.Add(new ExportedStruct { Name = sname, Fields = fields });
        }

        int ilCount = EcmFormat.ReadCount(r);
        var ilNames = new List<string>(ilCount);
        for (int i = 0; i < ilCount; i++)
            ilNames.Add(EcmFormat.ReadUtf8(r));

        int depCount = EcmFormat.ReadCount(r);
        var deps = new List<ModuleDependency>(depCount);
        for (int i = 0; i < depCount; i++)
            deps.Add(new ModuleDependency { Name = EcmFormat.ReadUtf8(r), InterfaceHash = EcmFormat.ReadUtf8(r) });

        return new ModuleInterface
        {
            Name = name,
            CompilerVersion = version,
            InterfaceHash = hash,
            HasInit = hasInit,
            Functions = funcs,
            Structs = structs,
            ILNames = ilNames,
            Dependencies = deps,
        };
    }

    static void WriteDefault(BinaryWriter w, object? value)
    {
        switch (value)
        {
            case null:
                w.Write((byte)0);
                break;
            case int v:
                w.Write((byte)1);
                w.Write(v);
                break;
            case double v:
                w.Write((byte)2);
                w.Write(v);
                break;
            case string v:
                w.Write((byte)3);
                EcmFormat.WriteUtf8(w, v);
                break;
            case bool v:
                w.Write((byte)4);
                w.Write(v);
                break;
            default:
                throw new BytecodeException(new[] { new BytecodeDiagnostic($"默认值载荷类型不支持: {value.GetType().Name}", null, 0) });
        }
    }

    static object? ReadDefault(BinaryReader r)
    {
        return r.ReadByte() switch
        {
            0 => null,
            1 => r.ReadInt32(),
            2 => r.ReadDouble(),
            3 => EcmFormat.ReadUtf8(r),
            4 => r.ReadBoolean(),
            var t => throw new BytecodeException(new[] { new BytecodeDiagnostic($"默认值载荷标签非法 {t}", null, 0) }),
        };
    }
}

/// <summary>
/// 接口提取（M2，ModuleSystem.md §5.2）：从一次模块编译提取 ModuleInterface。
/// 提取点在**绑定层**（BoundProgram）：接口由声明定义，必须先于优化——
/// 内联/死函数消除会让后优化视图丢失导出函数（导出即便未被 main 调用也对外可见）。
/// 输入符号来自 Binder 的声明收集——提取即序列化既有元数据，不重新推导语义。
/// </summary>
public static class ModuleInterfaceBuilder
{
    /// <summary>从模块源码树直接提取（模块编译路径；不经 Resolver/Bind）。</summary>
    public static ModuleInterface FromSyntaxTree(
        SyntaxTree mainTree,
        string moduleName,
        IEnumerable<ModuleDependency>? dependencies = null,
        string? compilerVersion = null)
    {
        var collector = new Resolution.DeclarationCollector();
        var (scope, _, structDefs, diags) = collector.Collect(
            [mainTree], ImmutableDictionary<string, SyntaxTree>.Empty);
        if (diags.HasErrors())
            throw new InvalidOperationException(
                "模块接口收集诊断错误：" + string.Join("; ", diags.Where(d => d.IsError).Select(d => d.Message)));

        var exports = new List<ExportedFunction>();
        var seen = new HashSet<string>();
        foreach (var member in mainTree.Root.Members)
        {
            string name = member switch
            {
                FuncDeclBlock func => func.Declare.Name,
                ExternFuncStmt ext => ext.Name,
                _ => "",
            };
            if (name == "" || !seen.Add(name))
                continue;
            foreach (var symbol in scope.TryLookupFuncs(name))
                exports.Add(FromSymbol(symbol, member is ExternFuncStmt, member.Location.StartLine + 1));
        }

        var structs = new List<ExportedStruct>();
        foreach (var (name, def) in structDefs.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var fields = new List<ExportedField>();
            foreach (var fld in def.Fields)
                fields.Add(new ExportedField { Name = fld.Name, TypeName = fld.FieldType.Name });
            structs.Add(new ExportedStruct { Name = name, Fields = fields });
        }

        var iface = new ModuleInterface
        {
            Name = moduleName,
            CompilerVersion = compilerVersion ?? ModuleInterface.CurrentCompilerVersion,
            InterfaceHash = "",
            Functions = exports,
            Structs = structs,
            ILNames = ExtractILNames(mainTree),
            HasInit = mainTree.Root.Members.Any(m =>
                m is not FuncDeclBlock and not ExternFuncStmt and not ImportStmt and not StructDeclBlock
                and not EmptyStmt),
            Dependencies = dependencies?.ToList() ?? [],
        };
        iface.InterfaceHash = InterfaceHasher.Compute(iface);
        return iface;
    }

    /// <summary>本模块图像标签名（§4.6-⑥）：@name 语法，词法层收集（声明序、去重）。</summary>
    internal static List<string> ExtractILNames(SyntaxTree mainTree)
    {
        var names = new List<string>();
        var seen = new HashSet<string>();
        foreach (var tok in SyntaxTree.ParseTokens(mainTree.Text.ToString()))
        {
            if (tok.Type != TokenType.EX_VAR)
                continue;
            var name = tok.Value[1..];
            if (seen.Add(name))
                names.Add(name);
        }
        return names;
    }

    internal static ModuleInterface FromBound(
        BoundProgram bound,
        string moduleName,
        IEnumerable<ModuleDependency>? dependencies = null,
        string? compilerVersion = null)
    {
        var exports = new List<ExportedFunction>();
        var seen = new HashSet<FunctionSymbol>();
        foreach (var symbol in bound.Functions.Keys)
            if (seen.Add(symbol))
                exports.Add(FromSymbol(symbol));
        foreach (var symbol in bound.ExternFunctions)
            if (seen.Add(symbol))
                exports.Add(FromSymbol(symbol));

        var structs = new List<ExportedStruct>();
        foreach (var (name, def) in bound.StructDefinitions.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var fields = new List<ExportedField>();
            foreach (var fld in def.Fields)
                fields.Add(new ExportedField { Name = fld.Name, TypeName = fld.FieldType.Name });
            structs.Add(new ExportedStruct { Name = name, Fields = fields });
        }

        var iface = new ModuleInterface
        {
            Name = moduleName,
            CompilerVersion = compilerVersion ?? ModuleInterface.CurrentCompilerVersion,
            InterfaceHash = "",
            Functions = exports,
            Structs = structs,
            ILNames = bound.ILNames.ToList(),
            HasInit = bound.MainFunction != null,
            Dependencies = dependencies?.ToList() ?? [],
        };
        iface.InterfaceHash = InterfaceHasher.Compute(iface);
        return iface;
    }

    /// <summary>从优化后的 SsaProgram 提取。注意：内联/不可达消除会丢导出——
    /// 仅适用于「函数集已冻结」的产物视图，模块编译请用 FromBound/FromCompilation。</summary>
    public static ModuleInterface FromProgram(
        SsaProgram program,
        string moduleName,
        IEnumerable<ModuleDependency>? dependencies = null,
        string? compilerVersion = null)
    {
        var exports = new List<ExportedFunction>();
        var seen = new HashSet<FunctionSymbol>();
        foreach (var symbol in program.Functions.Keys)
            if (seen.Add(symbol))
                exports.Add(FromSymbol(symbol));
        foreach (var symbol in program.ExternFunctions)
            if (seen.Add(symbol))
                exports.Add(FromSymbol(symbol));

        var structs = new List<ExportedStruct>();
        foreach (var (name, def) in program.StructDefinitions.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var fields = new List<ExportedField>();
            foreach (var fld in def.Fields)
                fields.Add(new ExportedField { Name = fld.Name, TypeName = fld.FieldType.Name });
            structs.Add(new ExportedStruct { Name = name, Fields = fields });
        }

        var iface = new ModuleInterface
        {
            Name = moduleName,
            CompilerVersion = compilerVersion ?? ModuleInterface.CurrentCompilerVersion,
            InterfaceHash = "",
            Functions = exports,
            Structs = structs,
            ILNames = program.ILNames.ToList(),
            HasInit = program.MainFunction != null,
            Dependencies = dependencies?.ToList() ?? [],
        };
        iface.InterfaceHash = InterfaceHasher.Compute(iface);
        return iface;
    }

    static ExportedFunction FromSymbol(FunctionSymbol symbol, bool isExternDecl = false, int declLine = 0)
    {
        bool isExtern = !string.Equals(symbol.LibraryName, "internal", StringComparison.Ordinal);
        var ps = new List<ExportedParam>();
        foreach (var p in symbol.Parameters)
        {
            ps.Add(new ExportedParam
            {
                Name = p.Name,
                TypeName = p.Type.Name,
                Ordinal = p.Ordinal,
                HasDefault = p.HasDefaultValue,
                DefaultValue = p.DefaultValue,
            });
        }
        return new ExportedFunction
        {
            Name = symbol.Name,
            ReturnTypeName = symbol.ReturnType.Name,
            IsExtern = isExtern,
            ExternLibrary = isExtern ? symbol.LibraryName : null,
            ExternalName = isExtern ? symbol.ExternalName : null,
            DeclLine = 0,   // 行号定位在 M4（缓存/文件对接）接入语法跨度后填充
            Params = ps,
        };
    }
}