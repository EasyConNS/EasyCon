using EasyCon.Core.LLM;

namespace EasyCon.Tests;

/// <summary>
/// 流式 chunk 解析对错误负载的处理：
/// 部分供应商/网关在 HTTP 200 的流内以 data: {"error":...} 报错（无 choices 字段），
/// 不解析会被静默丢弃，最终表现为无任何提示的空回复。
/// </summary>
[TestFixture]
public class OpenAIChatClientErrorDeltaTests
{
    [Test]
    public void ErrorObject_WithMessage_ProducesNonRetryableErrorDelta()
    {
        var deltas = OpenAIChatClient.ExtractDeltas(
            """{"error":{"message":"Incorrect API key provided","type":"invalid_request_error"}}""");

        Assert.Multiple(() =>
        {
            Assert.That(deltas, Has.Count.EqualTo(1));
            Assert.That(deltas[0].Type, Is.EqualTo(DeltaType.Error));
            Assert.That(deltas[0].Retryable, Is.False);
            Assert.That(deltas[0].Text, Is.EqualTo("Incorrect API key provided"));
        });
    }

    [Test]
    public void ErrorString_ProducesErrorDeltaWithRawText()
    {
        var deltas = OpenAIChatClient.ExtractDeltas("""{"error":"quota exceeded"}""");

        Assert.Multiple(() =>
        {
            Assert.That(deltas, Has.Count.EqualTo(1));
            Assert.That(deltas[0].Type, Is.EqualTo(DeltaType.Error));
            Assert.That(deltas[0].Text, Is.EqualTo("quota exceeded"));
        });
    }

    [Test]
    public void ErrorObject_WithoutMessage_FallsBackToRawJson()
    {
        var deltas = OpenAIChatClient.ExtractDeltas("""{"error":{"code":404,"status":"not_found"}}""");

        Assert.Multiple(() =>
        {
            Assert.That(deltas, Has.Count.EqualTo(1));
            Assert.That(deltas[0].Type, Is.EqualTo(DeltaType.Error));
            Assert.That(deltas[0].Text, Does.Contain("\"code\":404"));
        });
    }

    [Test]
    public void ContentChunk_StillParsesAfterErrorSupport()
    {
        var deltas = OpenAIChatClient.ExtractDeltas("""{"choices":[{"delta":{"content":"你好"}}]}""");

        Assert.Multiple(() =>
        {
            Assert.That(deltas, Has.Count.EqualTo(1));
            Assert.That(deltas[0].Type, Is.EqualTo(DeltaType.Content));
            Assert.That(deltas[0].Text, Is.EqualTo("你好"));
        });
    }

    [Test]
    public void UsageChunk_StillParses()
    {
        var deltas = OpenAIChatClient.ExtractDeltas(
            """{"usage":{"prompt_tokens":3,"completion_tokens":5,"total_tokens":8}}""");

        Assert.That(deltas.Any(d => d.Type == DeltaType.Usage && d.TotalTokens == 8), Is.True);
    }

    [Test]
    public void FinishReasonChunkWithoutDelta_ProducesNoDeltas()
    {
        var deltas = OpenAIChatClient.ExtractDeltas("""{"choices":[{"delta":{},"finish_reason":"stop"}]}""");

        Assert.That(deltas, Is.Empty);
    }
}