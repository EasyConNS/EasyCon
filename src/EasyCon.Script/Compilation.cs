using EasyCon.Script.Binding;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Modules;
using EasyCon.Script.Ssa;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyCon.Script.Text;
using EasyScript;
using System.CodeDom.Compiler;
using System.Collections.Immutable;

namespace EasyCon.Script;

public class ScriptException(string message, int address = 0) : Exception(message)
{
    public int Address { get; private set; } = address;
}

/// <summary>
/// 编译结果（统一链路）：诊断 + main 模块 SSA + 链接后的可执行镜像。
/// 当存在编译错误时 Image 为 null。
/// </summary>
public sealed class CompileResult
{
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    /// <summary>main 模块 SSA（现编路径恒有；DumpIr/IR 结构观测用）。</summary>
    public SsaProgram? Program { get; }
    public bool KeyAction { get; }
    public bool NeedIL { get; }
    public CompilationTiming? Timing { get; }
    /// <summary>链接后的可执行镜像（统一链路产物：桌面 EcxInterpreter 与 MCU .ecx 共用；失败为 null）。</summary>
    public EcxImage? Image { get; }
    /// <summary>链接序模块产物（std → vision → 依赖拓扑序 → main）。</summary>
    public List<ModuleArtifact> Artifacts { get; }
    /// <summary>主脚本语法树（format/ToCode 用）。</summary>
    public SyntaxTree? MainTree { get; }
    /// <summary>全项目 extern 符号并集（FFI 原生分发的签名来源）。</summary>
    public ImmutableArray<FunctionSymbol> NativeSymbols { get; }

    internal CompileResult(ImmutableArray<Diagnostic> diagnostics, SsaProgram? program, bool keyAction, bool needIL, CompilationTiming? timing = null,
        EcxImage? image = null, List<ModuleArtifact>? artifacts = null, SyntaxTree? mainTree = null,
        ImmutableArray<FunctionSymbol> nativeSymbols = default)
    {
        Diagnostics = diagnostics;
        Program = program;
        KeyAction = keyAction;
        NeedIL = needIL;
        Timing = timing;
        Image = image;
        Artifacts = artifacts ?? [];
        MainTree = mainTree;
        NativeSymbols = nativeSymbols.IsDefault ? [] : nativeSymbols;
    }

    /// <summary>格式化主脚本源码（format 命令；main 树输出）。</summary>
    public string FormatCode()
    {
        var mainTree = MainTree ?? throw new ArgumentNullException("无代码");
        using var writer = new StringWriter();
        using var printer = new IndentedTextWriter(writer, "    ");
        foreach (var statement in mainTree.Root.Members)
        {
            statement.WriteTo(printer);
        }
        return writer.ToString().Trim();
    }
}

/// <summary>统一编译链路入口选项（Compilation.CompileSource/CompileFile）。</summary>
public sealed class CompileOptions
{
    /// <summary>外部变量名集合（识图标签等）。</summary>
    public ImmutableHashSet<string>? ExtVars { get; set; }
    /// <summary>启用 obj/ 磁盘缓存（默认 true：compile/MCU 分发路径收益）。
    /// 桌面解释器路径应显式置 false 每次现编（与 v1 等价、无回归，ModuleSystem.md §7）。</summary>
    public bool UseDiskCache { get; set; } = true;
    /// <summary>false 时跳过 SSA 优化（DumpIr --raw 用）。</summary>
    public bool Optimize { get; set; } = true;
    /// <summary>保留各模块 SSA 于 Artifacts[i].Ssa（对拍/诊断用；不进 .ecm）。</summary>
    public bool KeepSsa { get; set; }
    /// <summary>obj/ 缓存目录覆盖（缺省 = 脚本同目录 obj/）。</summary>
    public string? ObjDir { get; set; }
    /// <summary>缓存 GC 文件超龄阈值（缺省 30 天）。</summary>
    public TimeSpan? GcMaxAge { get; set; }
    /// <summary>
    /// 进程级产物缓存（仅 UseDiskCache=false 的现编路径生效）：同源码⊕依赖接口⊕选项的模块
    /// 免重编，命中反序列化出新实例（无别名共享）。与磁盘缓存正交；统计见
    /// ModuleProjectResult.ProcessCacheHits/Misses。
    /// </summary>
    public bool UseProcessCache { get; set; } = true;
    /// <summary>旧版语法兼容（v1 PRINT/IF= 语义）。默认 true 保持行为；影响产物 → 进缓存键
    /// （见 <see cref="ProductFingerprint"/>）。</summary>
    public bool LegacySyntax { get; set; } = true;

    /// <summary>
    /// 影响编译产物的选项指纹（缓存键成分，docs/ModuleSystem.md §7.2）。
    /// 新增产物影响型选项时只需在此列出；KeepSsa（不写入序列化产物）与
    /// UseDiskCache/UseProcessCache/ObjDir/GcMaxAge（与产物内容无关）恒不进键。
    /// </summary>
    public string ProductFingerprint()
    {
        return $"O={(Optimize ? '1' : '0')}|L={(LegacySyntax ? '1' : '0')}|E={Modules.ModuleCacheKeys.ExtVarsKey(ExtVars)}|B={Bytecode.EcsSyscall.AbiRevision}";
    }
}

public sealed class Compilation
{
    // ============ 统一编译链路入口（docs/Pipeline.md）============

    /// <summary>
    /// 统一链路编译（内存源码）：Parser → ProjectCompiler（模块图，std/vision 隐式模块）
    /// → 逐模块独立编译 → EcxPipeline.Link → EcxImage。
    /// 单文件场景：无 lib/ 自动加载、无磁盘缓存（与 v1 现编等价）。
    /// </summary>
    public static CompileResult CompileSource(string code, CompileOptions? options = null)
    {
        options ??= new CompileOptions();
        var project = ProjectCompiler.CompileProject(SyntaxTree.Parse(code), options);
        return FromProject(project);
    }

    /// <summary>
    /// 统一链路编译（脚本文件）：Parser → ProjectCompiler（模块图 + lib/ 自动加载）
    /// → 逐模块独立编译（obj/ 缓存按 options.UseDiskCache）→ EcxPipeline.Link → EcxImage。
    /// </summary>
    public static CompileResult CompileFile(string path, CompileOptions? options = null)
    {
        options ??= new CompileOptions();
        var project = ProjectCompiler.CompileProject(path, options);
        return FromProject(project);
    }

    /// <summary>仅解析并格式化源码，不做绑定/编译（格式化单元测试与纯语法工具用）。</summary>
    public static string FormatSource(string code)
    {
        using var writer = new StringWriter();
        using var printer = new IndentedTextWriter(writer, "    ");
        foreach (var statement in SyntaxTree.Parse(code).Root.Members)
        {
            statement.WriteTo(printer);
        }
        return writer.ToString().Trim();
    }

    static CompileResult FromProject(ModuleProjectResult project)
    {
        var diagnostics = project.Diagnostics.ToList();
        // MD_AMBIGUOUS_EXPORT 等项目级警告并入诊断流（v1 诊断数组本就含警告）
        var noLocation = new TextLocation(SourceText.From("", ""), new SourceSpan(0, 0));
        diagnostics.AddRange(project.Warnings.Select(w => Diagnostic.Warning(noLocation, w)));

        var image = project.Image;
        return new CompileResult(
            [.. diagnostics],
            project.Program,
            image?.KeyAction ?? false,
            image?.NeedIL ?? false,
            project.Timing,
            image,
            project.Artifacts,
            project.MainTree,
            project.NativeSymbols);
    }
}