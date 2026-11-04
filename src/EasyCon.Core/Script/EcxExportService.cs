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
/// 以 Distributable 编译档生成 ECX 容器。该类不负责烧录或固件封装。
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

        CompileOptions options = ScriptCompileProfiles.Distributable(externalLabels);
        CompileResult compilation = string.IsNullOrWhiteSpace(sourcePath)
            ? Compilation.CompileSource(source, options)
            : Compilation.CompileSource(source, Path.GetFullPath(sourcePath), options);
        List<Diagnostic> diagnostics = compilation.Diagnostics.DistinctStable().ToList();
        if (compilation.Image is null || diagnostics.HasErrors())
            return new EcxExportResult(compilation, null, diagnostics);

        try
        {
            byte[] bytes = EcsContainer.WriteImage(compilation.Image, stripDebug: stripDebug);
            return new EcxExportResult(compilation, bytes, diagnostics);
        }
        catch (Exception ex)
        {
            TextLocation location = new(
                SourceText.From("", sourcePath ?? ""),
                new SourceSpan(0, 0));
            diagnostics.Add(Diagnostic.Error(location, ex.Message, DiagnosticCodes.ContainerImage));
            return new EcxExportResult(compilation, null, diagnostics.DistinctStable().ToList());
        }
    }
}