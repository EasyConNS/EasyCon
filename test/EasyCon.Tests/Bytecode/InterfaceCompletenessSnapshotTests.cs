using EasyCon.Script.Bytecode;
using EasyCon.Script.Syntax;

namespace EasyCon.Tests.Bytecode;

/// <summary>
/// 接口完备性自动化快照测试（阶段 D1，docs/ModuleSystem.md §4.6 清单 / §4.5 哈希）：
/// 逐类别断言「依赖可见信息变化 → 接口哈希必须变化」（①函数签名/重载 ②参数与返回类型含结构体
/// ③默认值载荷 ④结构体字段 ⑤extern 元数据 ⑥IL 标签 ⑦HasInit），
/// 以及「排除项变化 → 哈希不得变化」（行号、函数体实现、全局变量、依赖表）。
/// 缺口处理约定：先补测试再修实现（上一轮 F5 缺陷即此类缺口）。
/// 注：ECS 语法当前不支持参数默认值（ParameterSyntax 仅有名字+类型），③在接口模型层
/// （InterfaceHasher + ECM 序列化 roundtrip）验证——接口模型已承载该完备性项（R2）。
/// </summary>
[TestFixture]
public class InterfaceCompletenessSnapshotTests
{
    static string InterfaceOf(string libSource, string moduleName = "lib")
        => ModuleInterfaceBuilder.FromSyntaxTree(SyntaxTree.Parse(libSource), moduleName).InterfaceHash;

    static ModuleInterface InterfaceOfFull(string libSource, string moduleName = "lib",
        IEnumerable<ModuleDependency>? dependencies = null)
        => ModuleInterfaceBuilder.FromSyntaxTree(SyntaxTree.Parse(libSource), moduleName, dependencies);

    static void AssertHashChanges(string sourceA, string sourceB, string because)
    {
        var hashA = InterfaceOf(sourceA);
        var hashB = InterfaceOf(sourceB);
        Assert.That(hashB, Is.Not.EqualTo(hashA), because);
    }

    static void AssertHashStable(string sourceA, string sourceB, string because)
    {
        var hashA = InterfaceOf(sourceA);
        var hashB = InterfaceOf(sourceB);
        Assert.That(hashB, Is.EqualTo(hashA), because);
    }

    // ---------- §4.6 ①–⑦：可见信息变化 → 哈希变化 ----------

    [Test]
    public void Completeness_1_FunctionSignatureAndOverload()
    {
        // 函数名变化
        AssertHashChanges(
            "FUNC f($x:INT):INT\n    RETURN $x\nENDFUNC\n",
            "FUNC g($x:INT):INT\n    RETURN $x\nENDFUNC\n",
            "① 函数名进接口");
        // 重载（同名不同参数个数）变化
        AssertHashChanges(
            "FUNC f($x:INT):INT\n    RETURN $x\nENDFUNC\n",
            "FUNC f($x:INT):INT\n    RETURN $x\nENDFUNC\nFUNC f($x:INT, $y:INT):INT\n    RETURN $x + $y\nENDFUNC\n",
            "① 重载（参数个数）进接口");
    }

    [Test]
    public void Completeness_2_ParamAndReturnTypes_IncludingStructs()
    {
        const string structDecl = "STRUCT P\n    $v:INT\nEND\n";
        // 参数类型变化
        AssertHashChanges(
            "FUNC f($x:INT):INT\n    RETURN $x\nENDFUNC\n",
            "FUNC f($x:DOUBLE):DOUBLE\n    RETURN $x\nENDFUNC\n",
            "② 参数类型进接口");
        // 返回类型变化
        AssertHashChanges(
            "FUNC f($x:INT):INT\n    RETURN $x\nENDFUNC\n",
            "FUNC f($x:INT):DOUBLE\n    RETURN $x\nENDFUNC\n",
            "② 返回类型进接口");
        // 结构体参数类型变化（消费端按同形状重算布局，R3）
        AssertHashChanges(
            structDecl + "FUNC f($p:P):INT\n    RETURN $p.v\nENDFUNC\n",
            structDecl + "STRUCT Q\n    $v:INT\nEND\nFUNC f($p:Q):INT\n    RETURN $p.v\nENDFUNC\n",
            "② 结构体参数类型进接口");
    }

    [Test]
    public void Completeness_3_DefaultValuePayload_ModelLevel()
    {
        // 语言层暂无默认值语法；接口模型层验证：默认值载荷变化 → 哈希变化 + roundtrip 保留载荷（R2）
        var iface = InterfaceOfFull("FUNC f($x:INT):INT\n    RETURN $x\nENDFUNC\n");
        Assert.That(iface.Functions.Single().Params.Single().HasDefault, Is.False);

        var withDefault = new ModuleInterface
        {
            Name = iface.Name,
            CompilerVersion = iface.CompilerVersion,
            InterfaceHash = "",
            Functions = [new ExportedFunction
            {
                Name = "f",
                ReturnTypeName = "int",
                Params =
                [
                    new ExportedParam { Name = "x", TypeName = "int", Ordinal = 0 },
                    new ExportedParam { Name = "y", TypeName = "int", Ordinal = 1, HasDefault = true, DefaultValue = 1 },
                ],
            }],
            Structs = [],
            ILNames = [],
            HasInit = false,
            Dependencies = [],
        };
        var hashNoDefault = InterfaceHasher.Compute(withDefault);

        var withOtherDefault = CloneInterface(withDefault);
        withOtherDefault.Functions[0].Params[1].DefaultValue = 2;
        Assert.That(InterfaceHasher.Compute(withOtherDefault), Is.Not.EqualTo(hashNoDefault),
            "③ 默认值载荷进哈希");

        var withOtherType = CloneInterface(withDefault);
        withOtherType.Functions[0].Params[1].DefaultValue = "one";
        Assert.That(InterfaceHasher.Compute(withOtherType), Is.Not.EqualTo(hashNoDefault),
            "③ 默认值载荷类型变化进哈希");

        // 序列化 roundtrip 保留载荷（M2/R2：int/double/string/bool 全覆盖）
        foreach (var payload in new object[] { 7, 1.5, "txt", true })
        {
            var ifaceP = CloneInterface(withDefault);
            ifaceP.Functions[0].Params[1].DefaultValue = payload;
            var roundtrip = Roundtrip(ifaceP);
            Assert.That(roundtrip.Functions[0].Params[1].DefaultValue, Is.EqualTo(payload),
                $"③ 默认值载荷 {payload.GetType().Name} roundtrip 保留");
        }
    }

    [Test]
    public void Completeness_4_StructFields()
    {
        // 字段新增
        AssertHashChanges(
            "STRUCT P\n    $x:INT\nEND\n",
            "STRUCT P\n    $x:INT\n    $y:INT\nEND\n",
            "④ 结构体字段进接口");
        // 字段类型变化（含数组长度：ArrayType 进 TypeName）
        AssertHashChanges(
            "STRUCT P\n    $x:INT\nEND\n",
            "STRUCT P\n    $x:DOUBLE\nEND\n",
            "④ 字段类型进接口");
        AssertHashChanges(
            "STRUCT P\n    $x:INT\nEND\n",
            "STRUCT P\n    $x:INT[3]\nEND\n",
            "④ 固定数组字段长度进接口");
    }

    [Test]
    public void Completeness_5_ExternMetadata()
    {
        // 导出名变化
        AssertHashChanges(
            "EXTERN FUNC f($x:INT):INT AS \"abs\" FROM \"libc\"\n",
            "EXTERN FUNC f($x:INT):INT AS \"labs\" FROM \"libc\"\n",
            "⑤ extern 导出名进接口");
        // 库名变化
        AssertHashChanges(
            "EXTERN FUNC f($x:INT):INT AS \"abs\" FROM \"libc\"\n",
            "EXTERN FUNC f($x:INT):INT AS \"abs\" FROM \"user32\"\n",
            "⑤ extern 库名进接口");
    }

    [Test]
    public void Completeness_6_IlNames()
    {
        AssertHashChanges(
            "FUNC f():INT\n    RETURN @a\nENDFUNC\n",
            "FUNC f():INT\n    RETURN @b\nENDFUNC\n",
            "⑥ IL 标签名进接口");
    }

    [Test]
    public void Completeness_7_HasInit()
    {
        AssertHashChanges(
            "FUNC f():INT\n    RETURN 1\nENDFUNC\n",
            "$g = 1\nFUNC f():INT\n    RETURN $g\nENDFUNC\n",
            "⑦ HasInit（顶层语句 → <init:module> 合成）进接口");
    }

    // ---------- §4.5 排除项：变化 → 哈希不变（Merkle 失效收益的前提） ----------

    [Test]
    public void Exclusion_DeclLines()
    {
        AssertHashStable(
            "FUNC f($x:INT):INT\n    RETURN $x\nENDFUNC\n",
            "\n\n\n\nFUNC f($x:INT):INT\n    RETURN $x\nENDFUNC\n",
            "§4.5 行号刻意排除（不触发下游失效）");
    }

    [Test]
    public void Exclusion_FunctionBody()
    {
        AssertHashStable(
            "FUNC f($x:INT):INT\n    RETURN $x * 2\nENDFUNC\n",
            "FUNC f($x:INT):INT\n    RETURN $x * 3 + $x - 1\nENDFUNC\n",
            "§4.5 实现体信息排除（接口未变仅本模块重编，下游全命中）");
    }

    [Test]
    public void Exclusion_GlobalVariables()
    {
        AssertHashStable(
            "$__mult = 2\nFUNC f($x:INT):INT\n    RETURN $x * $__mult\nENDFUNC\n",
            "$__mult = 3\nFUNC f($x:INT):INT\n    RETURN $x * $__mult\nENDFUNC\n",
            "§4.5 模块私有全局变量排除");
    }

    [Test]
    public void Exclusion_DependencyTable()
    {
        // 依赖表不参与接口哈希（依赖哈希由缓存键聚合，§7.2）：同接口、不同依赖声明 → 同哈希
        var withDep = InterfaceOfFull("FUNC f($x:INT):INT\n    RETURN $x\nENDFUNC\n",
            dependencies: [new ModuleDependency { Name = "utils", InterfaceHash = "aaaa" }]);
        var withoutDep = InterfaceOfFull("FUNC f($x:INT):INT\n    RETURN $x\nENDFUNC\n");
        Assert.That(withDep.InterfaceHash, Is.EqualTo(withoutDep.InterfaceHash),
            "§4.5 依赖表排除");
    }

    // ---------- 快照：完整模块的接口哈希（防意外扩散） ----------

    [Test]
    public void Snapshot_KnownModuleHash()
    {
        // 固定哈希快照：接口模型任何意外变化（字段遗漏/排序漂移/编码变化）都会在此暴露；
        // 有意的规格演进应连同此快照一起更新并在 PR 说明中引用 §4.6 清单
        var hash = InterfaceOf("""
            _scale = 3
            STRUCT Point
                $x:INT
                $y:INT
            END
            FUNC twice($x:INT):INT
                RETURN $x * _scale
            ENDFUNC
            FUNC add($a:INT, $b:INT):INT
                RETURN $a + $b
            ENDFUNC
            FUNC plen($p:Point):INT
                RETURN $p.x + $p.y
            ENDFUNC
            EXTERN FUNC ffi_abs($x:INT):INT AS "abs" FROM "libc"
            FUNC il():INT
                RETURN @label1
            ENDFUNC
            """);
        Assert.That(hash, Is.EqualTo(ComputeExpectedSnapshotHash()),
            "接口哈希快照漂移：接口模型发生意外变化，或为有意的规格演进（请更新快照并引用 §4.6 清单）");
    }

    /// <summary>快照期望值 = 当前实现计算一次后固化；此处固化的是「模型自洽」而非魔法数字——
    /// 与 InterfaceHasher 的算法演进绑定（算法/字段变化时刷新）。</summary>
    static string ComputeExpectedSnapshotHash()
        => "aeef19d3005ca711baa8d75c1243dc0c2088890c4db83c6b080dc983cc6a8272";

    // ---------- 辅助 ----------

    static ModuleInterface CloneInterface(ModuleInterface src)
    {
        var clone = new ModuleInterface
        {
            Name = src.Name,
            CompilerVersion = src.CompilerVersion,
            InterfaceHash = src.InterfaceHash,
            HasInit = src.HasInit,
            Functions = [],
            Structs = [],
            ILNames = [.. src.ILNames],
            Dependencies = [.. src.Dependencies],
        };
        foreach (var f in src.Functions)
        {
            var fn = new ExportedFunction
            {
                Name = f.Name,
                ReturnTypeName = f.ReturnTypeName,
                IsExtern = f.IsExtern,
                ExternLibrary = f.ExternLibrary,
                ExternalName = f.ExternalName,
                DeclLine = f.DeclLine,
                Params = [],
            };
            foreach (var p in f.Params)
                fn.Params.Add(new ExportedParam
                {
                    Name = p.Name,
                    TypeName = p.TypeName,
                    Ordinal = p.Ordinal,
                    HasDefault = p.HasDefault,
                    DefaultValue = p.DefaultValue,
                });
            clone.Functions.Add(fn);
        }
        foreach (var s in src.Structs)
        {
            var st = new ExportedStruct { Name = s.Name, Fields = [] };
            foreach (var fld in s.Fields)
                st.Fields.Add(new ExportedField { Name = fld.Name, TypeName = fld.TypeName });
            clone.Structs.Add(st);
        }
        return clone;
    }

    static ModuleInterface Roundtrip(ModuleInterface iface)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            ModuleInterfaceFormat.Write(w, iface);
        ms.Position = 0;
        using var r = new BinaryReader(ms, System.Text.Encoding.UTF8);
        return ModuleInterfaceFormat.Read(r);
    }
}