using EasyCon.Core.Capabilities;
using EasyCon.Core.LLM.Agent;
using EasyCon.Core.LLM.Agent.Tools;
using EasyCon.Core.Script;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using OpenCvSharp;
using System.Collections.Immutable;
using System.Text.Json;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>
/// eval_ecs 工具：ECS 片段即时求值——标准库可用、结果经 PRINT 回传、
/// 编译/运行错误可读回传、无 Pad 时设备指令被拒。
/// </summary>
[TestFixture]
public class EvalEcsToolTests
{
    private static Dictionary<string, JsonElement> Args(params (string Key, object Value)[] pairs)
    {
        var dict = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in pairs)
            dict[key] = JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement.Clone();
        return dict;
    }

    private static EvalEcsTool CreateTool(ICaptureSource? capture = null, IOcrService? ocr = null)
        => new(() => capture, () => ocr);

    [Test]
    public async Task SimpleCompute_ReturnsPrintOutput()
    {
        var result = await CreateTool().ExecuteAsync(Args(("code", "$v = 1 + 1\nPRINT $v")));
        Assert.That(result.Status, Is.EqualTo(ToolResultStatus.Success));
        Assert.That(result.Content, Is.EqualTo("2"));
    }

    [Test]
    public async Task StdLib_PpocrNorm_ReachableWithoutRealModel()
    {
        // 标准库可达性：PPOCR_NORM → NET_SCALE → TransformTensor（无模型也应返回新句柄）
        var tool = CreateTool(capture: new DelegateCaptureSource((_, _, _, _) => MakePng(8, 8)));
        var result = await tool.ExecuteAsync(Args(("code",
            "$img = NET_IMAGE(-1, -1, -1, -1, 4, 4, \"rgb\")\n" +
            "$t = PPOCR_NORM($img, \"rec\")\n" +
            "PRINT $t")));
        Assert.That(result.Status, Is.EqualTo(ToolResultStatus.Success), result.Content);
        Assert.That(int.Parse(result.Content), Is.GreaterThan(0), "应返回有效张量句柄");
    }

    [Test]
    public async Task CompileError_ReturnsReadableDiagnostics()
    {
        var result = await CreateTool().ExecuteAsync(Args(("code", "$x = ===")));
        Assert.That(result.Status, Is.EqualTo(ToolResultStatus.Error));
        Assert.That(result.Content, Does.Contain("编译失败"));
    }

    [Test]
    public async Task RuntimeError_ReturnsMessage()
    {
        // S-21 语义：未知会话句柄 → 静默 0（无 PRINT 输出）
        var result = await CreateTool().ExecuteAsync(Args(("code", "NET_UNLOAD(999)")));
        Assert.That(result.Status, Is.EqualTo(ToolResultStatus.Success));
        Assert.That(result.Content, Is.EqualTo("(无 PRINT 输出)"));
    }

    [Test]
    public async Task DeviceCommands_UnavailableWithoutPad()
    {
        // 无 Pad 装配 → KEY 指令运行失败（fail-closed），错误可读回传
        var result = await CreateTool().ExecuteAsync(Args(("code", "KEY A\nPRINT \"pressed\"")));
        Assert.That(result.Status, Is.EqualTo(ToolResultStatus.Error), "无 Pad 时设备指令应失败");
        Assert.That(result.Content, Does.Contain("pressed").Or.Contain("错误"));
    }

    [Test]
    public async Task EmptyCode_ReturnsError()
    {
        var result = await CreateTool().ExecuteAsync(Args(("code", "")));
        Assert.That(result.Status, Is.EqualTo(ToolResultStatus.Error));
    }


    static string MakePng(int w, int h)
    {
        using var mat = new Mat(h, w, MatType.CV_8UC3, Scalar.Blue);
        return Convert.ToBase64String(mat.ToBytes(".png"));
    }
}