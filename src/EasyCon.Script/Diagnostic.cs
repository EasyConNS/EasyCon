using EasyCon.Script.Text;
using System.Collections.Immutable;

namespace EasyCon.Script;

/// <summary>诊断严重级（统一替代历史 IsError/IsWarning 双 bool；诊断结构化 M1，SCRIPT_MODERNIZATION_PLAN.md）。</summary>
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>诊断附注（相关位置链：主诊断的「另见」/级联原因）。</summary>
public sealed record DiagnosticNote(TextLocation Location, string Message);

public sealed class Diagnostic
{
    private Diagnostic(DiagnosticSeverity severity, TextLocation location, string message,
        string? code, ImmutableArray<DiagnosticNote> notes)
    {
        Severity = severity;
        Location = location;
        Message = message;
        Code = code;
        Notes = notes;
    }

    public DiagnosticSeverity Severity { get; }
    /// <summary>错误码（DiagnosticCodes 分配表；null = 未分配——历史散点诊断）。</summary>
    public string? Code { get; }
    public TextLocation Location { get; }
    public string Message { get; }
    public ImmutableArray<DiagnosticNote> Notes { get; }

    // ---- 历史兼容视图（消费方众多，逐步迁移到 Severity）----
    public bool IsError => Severity == DiagnosticSeverity.Error;
    public bool IsWarning => Severity == DiagnosticSeverity.Warning;

    public string FileName => Location.FileName;

    /// <summary>诊断所属编译阶段，供统一去重键和宿主展示使用。</summary>
    public string Phase => Code switch
    {
        not null when Code.StartsWith("ECX00", StringComparison.Ordinal) => "parse",
        not null when Code.StartsWith("ECX01", StringComparison.Ordinal)
            || Code.StartsWith("ECX02", StringComparison.Ordinal) => "bind",
        DiagnosticCodes.BytecodeEmit => "emit",
        DiagnosticCodes.ContainerImage => "container",
        DiagnosticCodes.Link => "link",
        not null when Code.StartsWith("ECX04", StringComparison.Ordinal) => "module",
        _ => "compile",
    };

    public override string ToString() => Message;

    public static Diagnostic Error(TextLocation location, string message, string? code = null)
        => new(DiagnosticSeverity.Error, location, message, code, []);

    public static Diagnostic Warning(TextLocation location, string message, string? code = null)
        => new(DiagnosticSeverity.Warning, location, message, code, []);

    /// <summary>附带相关位置链（不可变：返回携带 notes 的新实例）。</summary>
    public Diagnostic WithNotes(params ReadOnlySpan<DiagnosticNote> notes)
        => new(Severity, Location, Message, Code, [.. notes]);
}

public static class DiagnosticExtensions
{
    public static bool HasErrors(this ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d => d.IsError);
    }

    public static bool HasErrors(this IEnumerable<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d => d.IsError);
    }

    /// <summary>
    /// 按阶段、错误码、文件、范围和消息稳定去重。
    /// 该去重在编译器结果层完成，CLI、GUI、LSP 和测试共享同一诊断集合。
    /// </summary>
    public static IEnumerable<Diagnostic> DistinctStable(this IEnumerable<Diagnostic> diagnostics)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (Diagnostic diagnostic in diagnostics)
        {
            string key = string.Join("|", diagnostic.Phase, diagnostic.Code ?? "",
                diagnostic.FileName, diagnostic.Location.StartLine, diagnostic.Location.StartCharacter,
                diagnostic.Location.EndLine, diagnostic.Location.EndCharacter, diagnostic.Message);
            if (seen.Add(key))
                yield return diagnostic;
        }
    }
}