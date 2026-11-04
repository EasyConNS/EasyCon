using System.Text.Encodings.Web;
using System.Text.Json;

namespace EasyCon.Script;

/// <summary>
/// 诊断 JSON 输出（M1 诊断结构化；CLI --diagnostic-format=json 与 CI 消费）。
/// 字段：code（可 null）/ severity / message / fileName / line / character / endLine / endCharacter / notes。
/// </summary>
public static class DiagnosticFormat
{
    public static string ToJson(IEnumerable<Diagnostic> diagnostics)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream,
            new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartArray();
            foreach (var d in diagnostics)
                WriteOne(writer, d);
            writer.WriteEndArray();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    static void WriteOne(Utf8JsonWriter writer, Diagnostic d)
    {
        writer.WriteStartObject();
        if (d.Code != null)
            writer.WriteString("code", d.Code);
        writer.WriteString("severity", d.Severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            _ => "info",
        });
        writer.WriteString("message", d.Message);
        writer.WriteString("fileName", d.FileName);
        writer.WriteNumber("line", d.Location.StartLine);
        writer.WriteNumber("character", d.Location.StartCharacter);
        writer.WriteNumber("endLine", d.Location.EndLine);
        writer.WriteNumber("endCharacter", d.Location.EndCharacter);
        if (d.Notes.Length > 0)
        {
            writer.WriteStartArray("notes");
            foreach (var n in d.Notes)
            {
                writer.WriteStartObject();
                writer.WriteString("message", n.Message);
                writer.WriteString("fileName", n.Location.FileName);
                writer.WriteNumber("line", n.Location.StartLine);
                writer.WriteNumber("character", n.Location.StartCharacter);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }
}