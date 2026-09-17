using EasyCon.Core.LLM;
using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Models;
using System.Text;
using System.Text.Json;

namespace EasyCon.Tests;

/// <summary>
/// LLM 流式响应解析集成测试。
/// 实际调用远程 API，验证 SSE 解析是否正确处理各模型的响应格式差异。
/// 需要 models.json 中配置好对应的 provider 和 API key。
/// </summary>
[TestFixture]
[Category("Integration")]
[Explicit("需要网络和 API key，仅在排查模型兼容问题时手动运行")]
public class LlmStreamingTests
{
    private ModelsConfig? _config;
    private readonly List<string> _debugLogs = [];
    private readonly List<StreamDelta> _capturedDeltas = [];

    [SetUp]
    public void SetUp()
    {
        _debugLogs.Clear();
        _capturedDeltas.Clear();

        // 订阅调试日志
        OpenAIChatClient.DebugLog += OnDebugLog;

        // 从 models.json 加载配置
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "easycon", "models.json");

        if (!File.Exists(configPath))
        {
            Assert.Ignore($"models.json 不存在: {configPath}\n请先在 AI Agent 设置中配置模型。");
        }

        var json = File.ReadAllText(configPath);
        _config = JsonSerializer.Deserialize<ModelsConfig>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        });

        if (_config?.Models.Providers == null || _config.Models.Providers.Count == 0)
        {
            Assert.Ignore("models.json 中没有配置任何 provider。");
        }
    }

    [TearDown]
    public void TearDown()
    {
        OpenAIChatClient.DebugLog -= OnDebugLog;
    }

    private void OnDebugLog(string msg)
    {
        _debugLogs.Add(msg);
    }

    /// <summary>
    /// 测试 minicpm provider 下所有模型的流式响应。
    /// </summary>
    [TestCase("minicpm")]
    [TestCase("mimo")]
    public async Task StreamingResponse_ShouldParseContent(string providerKey)
    {
        Assert.That(_config, Is.Not.Null);

        if (!_config!.Models.Providers.TryGetValue(providerKey, out var provider))
        {
            Assert.Ignore($"未找到 provider: {providerKey}，请先在 models.json 中配置。");
        }

        if (provider.Models.Count == 0)
        {
            Assert.Ignore($"provider '{providerKey}' 没有配置任何模型。");
        }

        Assert.That(provider.Api, Is.EqualTo("openai-completions"),
            $"provider '{providerKey}' 的 api 类型不是 openai-completions，无法测试");

        TestContext.Progress.WriteLine($"\n{new string('=', 60)}");
        TestContext.Progress.WriteLine($"Provider: {provider.Name} ({providerKey})");
        TestContext.Progress.WriteLine($"BaseUrl: {provider.BaseUrl}");
        TestContext.Progress.WriteLine($"Models: {provider.Models.Count}");
        TestContext.Progress.WriteLine($"API Key: {(string.IsNullOrEmpty(provider.ApiKey) ? "(空)" : provider.ApiKey[..Math.Min(8, provider.ApiKey.Length)] + "****")}");
        TestContext.Progress.WriteLine(new string('=', 60));

        foreach (var model in provider.Models)
        {
            TestContext.Progress.WriteLine($"\n--- 测试模型: {model.Id} ({model.Name}) ---");

            _debugLogs.Clear();
            _capturedDeltas.Clear();

            using var client = new OpenAIChatClient(provider.BaseUrl, provider.ApiKey);
            var request = new ChatRequest
            {
                Model = model.Id,
                Messages = [ChatMessage.User("你好")]
            };

            var allContent = new StringBuilder();
            var allThinking = new StringBuilder();
            var hasError = false;
            string? errorMsg = null;

            try
            {
                await foreach (var delta in client.SendStreamAsync(request, CancellationToken.None))
                {
                    _capturedDeltas.Add(delta);

                    switch (delta.Type)
                    {
                        case DeltaType.Content:
                            allContent.Append(delta.Text);
                            break;
                        case DeltaType.Thinking:
                            allThinking.Append(delta.Text);
                            break;
                        case DeltaType.Error:
                            hasError = true;
                            errorMsg = delta.Text;
                            break;
                        case DeltaType.ToolCall:
                            break;
                        case DeltaType.Usage:
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                TestContext.Progress.WriteLine($"  ❌ 异常: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    TestContext.Progress.WriteLine($"     内部异常: {ex.InnerException.Message}");
                continue;
            }

            // ── 输出诊断结果 ──

            TestContext.Progress.WriteLine($"  Delta 总数: {_capturedDeltas.Count}");
            TestContext.Progress.WriteLine($"  Content: {_capturedDeltas.Count(d => d.Type == DeltaType.Content)}");
            TestContext.Progress.WriteLine($"  Thinking: {_capturedDeltas.Count(d => d.Type == DeltaType.Thinking)}");
            TestContext.Progress.WriteLine($"  Error: {_capturedDeltas.Count(d => d.Type == DeltaType.Error)}");
            TestContext.Progress.WriteLine($"  Usage: {_capturedDeltas.Count(d => d.Type == DeltaType.Usage)}");

            if (hasError)
            {
                TestContext.Progress.WriteLine($"  ❌ 流式错误: {errorMsg}");
            }

            TestContext.Progress.WriteLine($"  正文内容 ({allContent.Length} 字符):");
            TestContext.Progress.WriteLine($"    \"{Truncate(allContent.ToString(), 200)}\"");

            if (allThinking.Length > 0)
            {
                TestContext.Progress.WriteLine($"  思考内容 ({allThinking.Length} 字符):");
                TestContext.Progress.WriteLine($"    \"{Truncate(allThinking.ToString(), 200)}\"");
            }

            // ── 输出原始 SSE 数据（前 10 条）──
            TestContext.Progress.WriteLine($"  SSE 原始数据 (共 {_debugLogs.Count} 条，显示前 10 条):");
            foreach (var log in _debugLogs.Take(10))
            {
                TestContext.Progress.WriteLine($"    {log}");
            }

            if (_debugLogs.Count > 10)
            {
                TestContext.Progress.WriteLine($"    ... 省略 {_debugLogs.Count - 10} 条");
            }

            // ── 断言 ──
            if (!hasError)
            {
                Assert.That(allContent.Length, Is.GreaterThan(0),
                    $"模型 {model.Id} 返回了空的正文内容。\n" +
                    $"思考内容: {(allThinking.Length > 0 ? allThinking.ToString() : "(无)")}\n" +
                    $"Delta 总数: {_capturedDeltas.Count}\n" +
                    $"SSE 原始数据条数: {_debugLogs.Count}");

                TestContext.Progress.WriteLine($"  ✅ 模型 {model.Id} 流式响应正常");
            }
            else
            {
                Assert.Warn($"模型 {model.Id} 返回错误: {errorMsg}");
            }
        }
    }

    /// <summary>
    /// 列出 models.json 中所有可用的 provider 和模型（不实际调用 API）。
    /// </summary>
    [Test]
    public void ListAvailableModels()
    {
        Assert.That(_config, Is.Not.Null);

        TestContext.Progress.WriteLine($"\nmodels.json 中的 provider 和模型:");
        TestContext.Progress.WriteLine(new string('─', 60));

        foreach (var (key, provider) in _config!.Models.Providers)
        {
            TestContext.Progress.WriteLine($"Provider: {key} ({provider.Name})");
            TestContext.Progress.WriteLine($"  API: {provider.Api}");
            TestContext.Progress.WriteLine($"  BaseUrl: {provider.BaseUrl}");
            TestContext.Progress.WriteLine($"  模型数: {provider.Models.Count}");
            foreach (var model in provider.Models)
            {
                TestContext.Progress.WriteLine($"    - {model.Id} ({model.Name}) [vision={model.Vision}]");
            }
            TestContext.Progress.WriteLine("");
        }
    }

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "(空)";
        return text.Length <= max ? text : text[..max] + "...";
    }
}