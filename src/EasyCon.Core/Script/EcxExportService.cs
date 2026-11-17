using EasyCon.Core.Hosting;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Text;

namespace EasyCon.Core.Script;

/// <summary>统一的 ECX 分发导出结果；CLI 和 GUI 使用同一编译/容器化路径。</summary>
public sealed class EcxExportResult
{
    internal EcxExportResult(CompileResult compilation, byte[]? bytes, IReadOnlyList<Diagnostic> diagnostics)
    {
        Compilation = compilation;
        Bytes = bytes;
        Diagnostics = diagnostics;
    }

    public CompileResult Compilation { get; }
    public byte[]? Bytes { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public bool Success => Bytes is not null && !Diagnostics.HasErrors();
}

/// <summary>
/// 检查链接计划的离线能力需求，再生成单片机可加载的 ECX 容器。
/// </summary>
public static class EcxExportService
{
    public static EcxExportResult Export(
        string source,
        string? sourcePath,
        IEnumerable<string>? externalLabels = null,
        bool stripDebug = true)
    {
        ArgumentNullException.ThrowIfNull(source);

        CompileOptions options = ScriptCompileProfiles.Interactive(externalLabels);
        CompileResult compilation = string.IsNullOrWhiteSpace(sourcePath)
            ? Compilation.CompileSource(source, options)
            : Compilation.CompileSource(source, Path.GetFullPath(sourcePath), options);
        return Export(compilation, stripDebug);
    }

    public static EcxExportResult Export(CompileResult compilation, bool stripDebug = true)
    {
        List<Diagnostic> diagnostics = compilation.Diagnostics.DistinctStable().ToList();
        if (compilation.Image is not { } image || diagnostics.HasErrors())
            return new EcxExportResult(compilation, null, diagnostics);

        TextLocation location = new(compilation.MainTree?.Text ?? SourceText.From(""), new SourceSpan(0, 0));
        string[] dependencies = DesktopDependencies(image).Distinct(StringComparer.Ordinal).ToArray();
        if (dependencies.Length > 0)
        {
            diagnostics.Add(Diagnostic.Error(location,
                $"脚本依赖 PC 宿主能力（{string.Join("、", dependencies)}），无法生成单片机离线字节码",
                DiagnosticCodes.OfflineCapability));
            return new EcxExportResult(compilation, null, diagnostics);
        }

        EcsFunction? oversized = image.Functions.Find(function => function.NSlots > EcsRegisters.MaxRegisters);
        if (oversized != null)
        {
            diagnostics.Add(Diagnostic.Error(location,
                $"寄存器溢出：函数 '{oversized.Name}' 需要 {oversized.NSlots} 槽，单片机离线导出上限为 {EcsRegisters.MaxRegisters} 槽",
                DiagnosticCodes.ContainerImage));
            return new EcxExportResult(compilation, null, diagnostics);
        }

        try
        {
            byte[] bytes = EcsContainer.WriteImage(image, stripDebug: stripDebug);
            return new EcxExportResult(compilation, bytes, diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add(Diagnostic.Error(location, ex.Message, DiagnosticCodes.ContainerImage));
            return new EcxExportResult(compilation, null, diagnostics.DistinctStable().ToList());
        }
    }

    static IEnumerable<string> DesktopDependencies(EcxImage image)
    {
        if ((image.Features & EcsImageFeatures.Il) != 0)
            yield return "识图、图像采集或 OCR";

        // 名表在链接后只包含可达调用。当前单片机宿主没有按名的原生函数入口。
        foreach (EcsNative native in image.Natives)
            yield return native.Name;

        foreach (EcsFunction function in image.Functions)
        {
            foreach (EcsInstruction instruction in function.Instructions)
            {
                if (instruction.Op != EcsOpcode.CallN || (instruction.Ext & EcsSyscall.CallFlag) == 0)
                    continue;

                int syscall = (int)(instruction.Ext & ~EcsSyscall.CallFlag);
                // 标准流 FWRITE/FREAD 可用于单片机日志和输入。其余文件接口依赖文件系统。
                if (syscall is EcsSyscall.OcrConf or (>= EcsSyscall.FOpen and <= EcsSyscall.FileExists))
                    yield return EcsSyscall.Names[syscall];
            }
        }
    }
}