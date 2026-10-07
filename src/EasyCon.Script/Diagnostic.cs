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
}