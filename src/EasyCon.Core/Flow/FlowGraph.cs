using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyCon.Core.Flow;

/// <summary>
/// Flow 编排图模型：节点 + exec 边（控制流）+ 数据入边（端口引用）。
/// 序列化为 *.flow.json（普通 JSON）。图只在 PC 执行；烧录 MCU 仍走纯 ECS 脚本。
/// </summary>
public sealed class FlowGraph
{
    public int Version { get; set; } = 1;

    public string Name { get; set; } = "";

    /// <summary>图级看门狗：节点执行步数上限。</summary>
    public int MaxSteps { get; set; } = 3000;

    /// <summary>图级看门狗：总时长上限（秒）。</summary>
    public int TimeoutSec { get; set; } = 1800;

    public List<FlowNode> Nodes { get; set; } = [];

    /// <summary>exec 边：from 可为 "nodeId" 或 "nodeId.outPort"（条件出口）。</from></summary>
    public List<FlowExecEdge> Exec { get; set; } = [];

    /// <summary>按 id 查节点。</summary>
    public FlowNode? Node(string id) => Nodes.FirstOrDefault(n => n.Id == id);

    /// <summary>
    /// 解析 flow.json；结构非法抛 <see cref="FlowParseException"/>（带可读原因）。
    /// </summary>
    public static FlowGraph Parse(string json)
    {
        try
        {
            var graph = JsonSerializer.Deserialize<FlowGraph>(json, JsonOpts)
                ?? throw new FlowParseException("flow.json 内容为空");
            graph.Validate();
            return graph;
        }
        catch (JsonException ex)
        {
            throw new FlowParseException($"flow.json 解析失败: {ex.Message}", ex);
        }
        catch (FlowParseException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new FlowParseException($"flow.json 结构不合法: {ex.Message}", ex);
        }
    }

    public static FlowGraph LoadFile(string path) => Parse(File.ReadAllText(path));

    private void Validate()
    {
        var dup = Nodes.GroupBy(n => n.Id).FirstOrDefault(g => g.Count() > 1);
        if (dup != null)
            throw new FlowParseException($"节点 id 重复: {dup.Key}");
        if (Nodes.Count == 0)
            throw new FlowParseException("图中没有任何节点");
        foreach (var e in Exec)
        {
            if (e.FromNode != "start" && Node(e.FromNode) == null)
                throw new FlowParseException($"exec 边引用了不存在的节点: {e.FromNode}");
            if (Node(e.To) == null)
                throw new FlowParseException($"exec 边引用了不存在的节点: {e.To}");
        }
    }

    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new FlowExecEdgeConverter() },
    };
}

/// <summary>单个节点：类型 + 参数 + 数据入边 + 慢感知策略。</summary>
public sealed class FlowNode
{
    public string Id { get; set; } = "";

    /// <summary>节点类型（如 capture.frame / ocr.text / compare / script.run / pad.sequence / wait / start / end）。</summary>
    public string Type { get; set; } = "";

    /// <summary>节点参数（各类型自有 schema，原始 JSON）。</summary>
    public Dictionary<string, JsonElement> Params { get; set; } = new();

    /// <summary>
    /// 数据入边：端口名 → 值。值为 {"node": "id", "port": "name"} 引用，
    /// 或原始字面量（string/int/double/bool，直接作为端口值）。
    /// </summary>
    public Dictionary<string, JsonElement> Inputs { get; set; } = new();

    /// <summary>
    /// 慢感知策略：命中时直接复用本节点上次输出，不重复执行（见 <see cref="FlowSlowPolicy"/>）。
    /// null = 每次都执行（默认）。
    /// </summary>
    public FlowSlowPolicy? Slow { get; set; }
}

/// <summary>
/// 慢感知策略（跨类型节点属性，写在节点上而不是 params 里）：把「多贵」与「多新」解耦——
/// 感知节点（OCR/视觉）不必每步都跑，由帧号/时间/内容变化决定复用还是重算。
///
/// <para>多个条件同时给出时取**最保守**语义：任一条判定「该重算」就重算；
/// 全部判定「可复用」且上次有缓存时才复用。全空对象 = 永不复用（等于不写 slow）。</para>
/// </summary>
public sealed class FlowSlowPolicy
{
    /// <summary>帧号推进不足 N 帧即复用（需采集源提供 <c>FrameIndex</c>）。</summary>
    public int? EveryFrames { get; set; }

    /// <summary>true = 感知输入指纹未变则复用（指纹取 image 入边，缺省取当前帧）。</summary>
    public bool? OnChange { get; set; }

    /// <summary>距上次执行不足 N 毫秒则复用（墙体时钟）。</summary>
    public int? IntervalMs { get; set; }
}

/// <summary>exec 边。From 可能带出口后缀（"nodeId.true"）。</summary>
public sealed class FlowExecEdge
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";

    [JsonIgnore]
    public string FromNode => From.Contains('.') ? From[..From.IndexOf('.')] : From;

    [JsonIgnore]
    public string? FromPort => From.Contains('.') ? From[(From.IndexOf('.') + 1)..] : null;

    public FlowExecEdge() { }

    public FlowExecEdge(string from, string to) { From = from; To = to; }
}

/// <summary>flow.json 结构错误。</summary>
public sealed class FlowParseException(string message, Exception? inner = null)
    : Exception(message, inner)
{
}

/// <summary>
/// exec 边的 JSON 形态：["a","b"] / ["chk.true","hit"]（数组简写）
/// 或 {"from":"chk.true","to":"hit"}（对象完整形式）。
/// </summary>
public sealed class FlowExecEdgeConverter : JsonConverter<FlowExecEdge>
{
    public override FlowExecEdge Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var parts = new List<string?>(2);
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                parts.Add(reader.TokenType == JsonTokenType.String ? reader.GetString() : null);
            if (parts.Count is < 1 or > 2 || parts[0] == null)
                throw new FlowParseException("exec 边数组形式应为 [from, to]");
            return new FlowExecEdge(parts[0]!, parts.Count == 2 ? parts[1] ?? "" : "");
        }

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.TryGetProperty("from", out var from))
            return new FlowExecEdge(from.GetString() ?? "",
                root.TryGetProperty("to", out var to) ? to.GetString() ?? "" : "");
        throw new FlowParseException("exec 边对象形式缺少 from");
    }

    public override void Write(Utf8JsonWriter writer, FlowExecEdge value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteStringValue(value.From);
        if (value.To.Length > 0)
            writer.WriteStringValue(value.To);
        writer.WriteEndArray();
    }
}