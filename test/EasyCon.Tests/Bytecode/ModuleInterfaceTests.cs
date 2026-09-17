using EasyCon.Script;
using EasyCon.Script.Binding;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Resolution;
using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyCon.Script.Text;
using System.Collections.Immutable;
using System.Text;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 模块接口单元测试（docs/ModuleSystem.md M1/M2 交付判据）：
/// 接口区序列化 round-trip、接口哈希稳定性/敏感性、从既有编译提取接口。
/// </summary>
[TestFixture]
public class ModuleInterfaceTests
{
    // ---------- M1：数据模型 + 序列化 round-trip ----------

    [Test]
    public void Roundtrip_HandBuilt_AllDefaultPayloadTypes()
    {
        var iface = new ModuleInterface
        {
            Name = "utils",
            CompilerVersion = "1.2.3",
            InterfaceHash = "",
            HasInit = true,
            Functions =
            [
                new ExportedFunction
                {
                    Name = "add",
                    ReturnTypeName = "int",
                    Params =
                    [
                        new ExportedParam { Name = "a", TypeName = "int", Ordinal = 0 },
                        new ExportedParam { Name = "b", TypeName = "int", Ordinal = 1, HasDefault = true, DefaultValue = 5 },
                        new ExportedParam { Name = "ratio", TypeName = "double", Ordinal = 2, HasDefault = true, DefaultValue = 0.5 },
                        new ExportedParam { Name = "tag", TypeName = "string", Ordinal = 3, HasDefault = true, DefaultValue = "名称:值" },
                        new ExportedParam { Name = "flag", TypeName = "bool", Ordinal = 4, HasDefault = true, DefaultValue = true },
                    ],
                },
                new ExportedFunction
                {
                    Name = "arr3",
                    ReturnTypeName = "int[3]",
                    Params = [new ExportedParam { Name = "xs", TypeName = "int[]" }],
                },
            ],
            Structs =
            [
                new ExportedStruct
                {
                    Name = "Point",
                    Fields = [new ExportedField { Name = "x", TypeName = "int" }, new ExportedField { Name = "path", TypeName = "string" }],
                },
            ],
            ILNames = ["enemy", "portal"],
            Dependencies = [new ModuleDependency { Name = "std", InterfaceHash = "abcd1234" }],
        };
        iface.InterfaceHash = InterfaceHasher.Compute(iface);

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            ModuleInterfaceFormat.Write(w, iface);
        ModuleInterface restored;
        ms.Position = 0;
        using (var r = new BinaryReader(ms, Encoding.UTF8))
            restored = ModuleInterfaceFormat.Read(r);

        Assert.That(restored.DeepEquals(iface), Is.True, "round-trip 应结构全等");
        Assert.That(restored.InterfaceHash, Is.EqualTo(iface.InterfaceHash), "哈希应一致");
        // 默认值载荷逐类型核对（R2）
        var add = restored.Functions.Single(f => f.Name == "add");
        Assert.That((int)add.Params[1].DefaultValue!, Is.EqualTo(5));
        Assert.That((double)add.Params[2].DefaultValue!, Is.EqualTo(0.5));
        Assert.That((string)add.Params[3].DefaultValue!, Is.EqualTo("名称:值"));
        Assert.That((bool)add.Params[4].DefaultValue!, Is.True);
    }

    [Test]
    public void Hash_ExcludesLineNumbers_IncludesSignatures()
    {
        ExportedFunction MakeFunc(int declLine, string ret = "int", object? def = null, bool hasDefault = false)
            => new ExportedFunction
            {
                Name = "f",
                ReturnTypeName = ret,
                DeclLine = declLine,
                Params = [new ExportedParam { Name = "a", TypeName = "int", Ordinal = 0, HasDefault = hasDefault, DefaultValue = def }],
            };

        ModuleInterface Make(ExportedFunction f, string fieldType = "int", bool hasInit = false)
        {
            var iface = new ModuleInterface
            {
                Name = "m",
                CompilerVersion = "1.0.0",
                InterfaceHash = "",
                HasInit = hasInit,
                Functions = [f],
                Structs = [new ExportedStruct { Name = "P", Fields = [new ExportedField { Name = "x", TypeName = fieldType }] }],
                ILNames = [],
                Dependencies = [],
            };
            iface.InterfaceHash = InterfaceHasher.Compute(iface);
            return iface;
        }

        // 行号是 §4.5 排除项：改变行号不改变哈希
        var base_ = Make(MakeFunc(10));
        var movedDecl = Make(MakeFunc(999));
        Assert.That(movedDecl.InterfaceHash, Is.EqualTo(base_.InterfaceHash), "行号不应触发接口失效");

        // 签名/默认值/类型表/init 变化 → 哈希变化（Merkle 失效源）
        Assert.That(Make(MakeFunc(10, ret: "double")).InterfaceHash, Is.Not.EqualTo(base_.InterfaceHash), "返回类型变化应失效");
        Assert.That(Make(MakeFunc(10, def: 1, hasDefault: true)).InterfaceHash, Is.Not.EqualTo(base_.InterfaceHash), "默认值载荷变化应失效");
        Assert.That(Make(MakeFunc(10), fieldType: "double").InterfaceHash, Is.Not.EqualTo(base_.InterfaceHash), "结构体字段类型变化应失效");
        Assert.That(Make(MakeFunc(10), hasInit: true).InterfaceHash, Is.Not.EqualTo(base_.InterfaceHash), "init 标记变化应失效");
    }

    // ---------- M2：从既有编译提取接口 ----------

    static CompileResult Compile(string source)
    {
        var result = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError).ToList(), Is.Empty,
            "编译诊断：" + string.Join("\n", result.Diagnostics));
        return result;
    }

    const string SnapshotSource = """
        STRUCT Point
            $x:INT
            $y:INT
        END
        FUNC add($a, $b):INT
            RETURN $a + $b
        ENDFUNC
        $p = Point{}
        $r = add(1, 2)
        PRINT $r
        """;

    [Test]
    public void Extract_FromCompiledProgram_Snapshot()
    {
        var mainTree = SyntaxTree.Parse(SourceText.From(SnapshotSource, "probe.ecs"));
        var iface = ModuleInterfaceBuilder.FromSyntaxTree(mainTree, "main");

        Assert.That(iface.Name, Is.EqualTo("main"));
        Assert.That(iface.HasInit, Is.True, "含顶层语句的模块应有 init 标记");

        var add = iface.Functions.Single(f => f.Name == "add");
        Assert.That(add.ReturnTypeName, Is.EqualTo("int"));
        Assert.That(add.Params.Select(p => (p.Name, p.TypeName, p.Ordinal)).ToList(),
            Is.EqualTo(new[] { ("$a", "int", 0), ("$b", "int", 1) }));
        Assert.That(add.Params.All(p => !p.HasDefault), Is.True, "当前源码语法不产生默认值");

        var st = iface.Structs.Single(s => s.Name == "Point");
        Assert.That(st.Fields.Select(f => (f.Name, f.TypeName)).ToList(),
            Is.EqualTo(new[] { ("x", "int"), ("y", "int") }));

        Assert.That(InterfaceHasher.Compute(iface), Is.EqualTo(iface.InterfaceHash), "哈希字段应与内容自洽");

        // 同源码两次提取 → 接口逐字节相同（缓存可命中的前提）
        var iface2 = ModuleInterfaceBuilder.FromSyntaxTree(
            SyntaxTree.Parse(SourceText.From(SnapshotSource, "probe.ecs")), "main");
        Assert.That(iface2.InterfaceHash, Is.EqualTo(iface.InterfaceHash), "同源码哈希应确定");
        Assert.That(iface2.DeepEquals(iface), Is.True, "同源码接口应全等");
    }

    [Test]
    public void Extract_Extern_CarriesFfiMetadata()
    {
        var result = Compilation.CompileSource("""
            EXTERN FUNC Sleep($ms:INT) FROM "kernel32.dll"
            $a = 1
            """, new CompileOptions { UseDiskCache = false });
        Assert.That(result.Diagnostics.Where(d => d.IsError).ToList(), Is.Empty,
            "编译诊断：" + string.Join("\n", result.Diagnostics));

        var iface = ModuleInterfaceBuilder.FromProgram(result.Program!, "extmod");
        var sleep = iface.Functions.Single(f => f.Name == "Sleep");
        Assert.That(sleep.IsExtern, Is.True, "extern 声明应进导出表（§4.6-⑤）");
        Assert.That(sleep.ExternLibrary, Is.EqualTo("kernel32.dll"));
        Assert.That(sleep.Params.Single().TypeName, Is.EqualTo("int"));
    }

    // ---------- M1：ECM v2 接口区嵌入 round-trip ----------

    [Test]
    public void EcmV2_Roundtrip_WithAndWithoutInterface()
    {
        var artifacts = Compile(SnapshotSource).Artifacts;

        // 无接口（显式置 null）：ECM v2 仍可 round-trip（兼容无接口区产物）
        foreach (var a in artifacts)
        {
            var iface = a.Interface;
            a.Interface = null;
            var restored = EcmFormat.Read(EcmFormat.Write(a));
            a.Interface = iface;
            Assert.That(restored.Interface, Is.Null, "未挂接口的产物读回应为 null");
            Assert.That(restored.Name, Is.EqualTo(a.Name));
        }

        // 接口区随 .ecm 往返（统一链路产物本就携带接口）
        var restored2 = artifacts.Select(a => EcmFormat.Read(EcmFormat.Write(a))).ToList();
        for (int i = 0; i < artifacts.Count; i++)
        {
            Assert.That(restored2[i].Interface, Is.Not.Null, "接口区应写盘");
            Assert.That(restored2[i].Interface!.DeepEquals(artifacts[i].Interface), Is.True, "接口应逐字段还原");
            Assert.That(restored2[i].Interface!.InterfaceHash, Is.EqualTo(artifacts[i].Interface!.InterfaceHash));
        }

        // EcxInterpreter 链接执行不受接口区影响（既有对拍路径回归）
        var host = new EcxHost();
        host.EnableRecording();
        var image = EcxPipeline.Link(restored2,
            artifacts.Any(a => a.KeyAction), artifacts.Any(a => a.NeedIL));
        Assert.That(EcxInterpreter.Run(image, host), Is.EqualTo(0));
        Assert.That(host.Lines, Does.Contain("3"));
    }
    // ---------- M3：接口后端（InterfaceScopeSynthesizer）绑定等价 ----------

    const string LibSource = """
        FUNC twice($x):INT
            RETURN $x * 2
        ENDFUNC
        FUNC area($w:INT, $h:INT):INT
            RETURN $w * $h
        ENDFUNC
        """;
    const string MainSource = """
        IMPORT "utils.ecs"
        $r = twice(21)
        PRINT $r
        """;

    // 最小 std 接口源（main 只用 PRINT；接口提取只收集声明，函数体不求值）
    const string StdMinimalSource = """
        FUNC print($output: STRING)
            FWRITE $STDOUT, $output
        ENDFUNC
        FUNC PRINT($output: STRING)
            FWRITE $STDOUT, $output
        ENDFUNC
        """;

    [Test]
    public void M3_InterfaceBackend_BindingEquivalence()
    {
        // ---- 路径 A（统一链路）：lib/ 落盘 + CompileFile 全管线编译执行 ----
        var dir = Path.Combine(Path.GetTempPath(), "ecs-mod-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "lib"));
            File.WriteAllText(Path.Combine(dir, "lib", "utils.ecs"), LibSource);
            var mainPath = Path.Combine(dir, "main.ecs");
            File.WriteAllText(mainPath, MainSource);
            var resultA = Compilation.CompileFile(mainPath, new CompileOptions { UseDiskCache = false });
            Assert.That(resultA.Diagnostics.Where(d => d.IsError), Is.Empty, "统一链路应编译通过");
            var hostA = new EcxHost();
            hostA.EnableRecording();
            Assert.That(EcxInterpreter.Run(resultA.Image!, hostA), Is.EqualTo(0));
            Assert.That(hostA.Lines, Is.EqualTo(new[] { "42" }), "统一链路执行");

            // ---- 路径 B（接口后端）：依赖以接口区提供，Synthesize 后绑定 ----
            var ifaceU = ModuleInterfaceBuilder.FromSyntaxTree(
                SyntaxTree.Parse(SourceText.From(LibSource, "utils.ecs")), "utils");
            var ifaceStd = ModuleInterfaceBuilder.FromSyntaxTree(
                SyntaxTree.Parse(SourceText.From(StdMinimalSource, "std.ecs")), "std");
            // 接口后端替代 IMPORT 解析：main 树不含 IMPORT 语句
            var mainBody = MainSource.Replace("IMPORT \"utils.ecs\"", "");
            var mainTree = SyntaxTree.Parse(SourceText.From(mainBody, "main.ecs"));
            var (resolution, _) = InterfaceScopeSynthesizer.Synthesize(mainTree,
                [new ImportedInterface(ifaceStd, null), new ImportedInterface(ifaceU, null)]);
            var bound = Binder.BindProgram(resolution, ImmutableHashSet<string>.Empty);
            Assert.That(bound.Diagnostics.Where(d => d.IsError), Is.Empty,
                "接口路径绑定应无错误：" + string.Join("; ", bound.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));

            // 绑定决策等价：$eval 里对 twice 的调用解析到同签名符号（int→int）
            var twice = resolution.GlobalScope!.TryLookupFuncs("twice").Single();
            Assert.That(twice.Parameters.Single().Type, Is.EqualTo(ScriptType.Int));
            Assert.That(twice.ReturnType, Is.EqualTo(ScriptType.Int));
            Assert.That(twice.Declaration, Is.Null, "接口符号应无声明体（绑体跳过机制）");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public void M3_InterfaceBackend_TypeMismatchMatchesSourceBackend()
    {
        // 参数个数不匹配 → 两条后端产生同一诊断文本（解析决策等价）
        var badMainSource = """
            IMPORT "utils.ecs"
            $r = twice(1, 2)
            """;
        var dir = Path.Combine(Path.GetTempPath(), "ecs-mod-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "lib"));
            File.WriteAllText(Path.Combine(dir, "lib", "utils.ecs"), LibSource);
            var mainPath = Path.Combine(dir, "main.ecs");
            File.WriteAllText(mainPath, badMainSource);
            var resultA = Compilation.CompileFile(mainPath, new CompileOptions { UseDiskCache = false });
            var msgsA = resultA.Diagnostics.Where(d => d.IsError).Select(d => d.Message).Distinct().ToList();
            Assert.That(msgsA, Is.Not.Empty, "统一链路应报错");

            var ifaceU = ModuleInterfaceBuilder.FromSyntaxTree(
                SyntaxTree.Parse(SourceText.From(LibSource, "utils.ecs")), "utils");
            var badBody = badMainSource.Replace("IMPORT \"utils.ecs\"", "");
            var mainTree = SyntaxTree.Parse(SourceText.From(badBody, "main.ecs"));
            var (resolution, _) = InterfaceScopeSynthesizer.Synthesize(mainTree,
                [new ImportedInterface(ifaceU, null)]);
            var bound = Binder.BindProgram(resolution, ImmutableHashSet<string>.Empty);
            var msgsB = bound.Diagnostics.Where(d => d.IsError).Select(d => d.Message).Distinct().ToList();
            Assert.That(msgsB, Is.Not.Empty, "接口路径应报错");
            Assert.That(msgsB, Is.EqualTo(msgsA), "诊断文本应逐条一致");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public void M3_AliasedInterface_ModuleScopeOnly()
    {
        var ifaceU = ModuleInterfaceBuilder.FromSyntaxTree(
            SyntaxTree.Parse(SourceText.From(LibSource, "utils.ecs")), "utils");
        var mainTree = SyntaxTree.Parse(SourceText.From("$a = 1", "main.ecs"));
        var (resolution, _) = InterfaceScopeSynthesizer.Synthesize(mainTree, [new ImportedInterface(ifaceU, "u")]);

        Assert.That(resolution.GlobalScope!.TryLookupFuncs("twice"), Is.Empty, "alias 接口不得进全局作用域（§6 限定访问）");
        var moduleScope = resolution.ModuleScopes["u"];
        Assert.That(moduleScope.TryLookupFuncs("twice").Count, Is.EqualTo(1), "alias 作用域可见");
        Assert.That(resolution.AliasedTrees, Is.Empty, "接口后端无源码树");
    }
}