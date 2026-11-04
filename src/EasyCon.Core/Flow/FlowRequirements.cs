#nullable enable
using EasyCon.Core.Hosting;
using EasyCon.Core.Script;
using System.Text.Json;

namespace EasyCon.Core.Flow;

/// <summary>能静态判断的输入需求先检查；动态调用仍由运行端口登记并验证。</summary>
public static class FlowRequirements
{
    public static bool RequiresInput(FlowGraph graph, string appDir)
    {
        foreach (FlowNode node in graph.Nodes)
        {
            if (node.Type.StartsWith("pad.", StringComparison.Ordinal)) return true;
            if (node.Type != "script.run") continue;
            string? Text(string name) => node.Params.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
            try
            {
                EasyScriptEngine engine = new();
                ScriptHostOptions options = new() { Compile = ScriptCompileProfiles.Interactive([]) };
                string? inline = Text("script");
                string? file = Text("file");
                IScriptSession? session = !string.IsNullOrWhiteSpace(inline) ? engine.FromSource(inline, options)
                    : !string.IsNullOrWhiteSpace(file) ? engine.LoadFile(Path.IsPathRooted(file) ? file : Path.Combine(appDir, file), options) : null;
                if (session?.Info.KeyAction == true) return true;
            }
            catch (IOException) { } // 保留运行时的原节点错误，而非用缺设备掩盖坏脚本。
        }
        return false;
    }
}