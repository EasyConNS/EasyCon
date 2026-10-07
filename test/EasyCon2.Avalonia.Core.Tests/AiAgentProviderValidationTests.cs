using EasyCon.Core.LLM.Models;
using EasyCon2.Avalonia.AiAgent;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>
/// 发送前供应商配置校验：配置错误必须在发送时给出可读提示，
/// 而不是发出注定失败的请求后静默或抛出晦涩的英文异常。
/// </summary>
[TestFixture]
public class AiAgentProviderValidationTests
{
    private static ProviderConfig ValidProvider() => new()
    {
        Name = "TestProvider",
        BaseUrl = "https://api.example.com/v1",
        ApiKey = "sk-test",
        Api = "openai-completions"
    };

    [Test]
    public void ValidProvider_ReturnsNull()
    {
        Assert.That(AiAgentViewModel.ValidateProvider("key", ValidProvider()), Is.Null);
    }

    [Test]
    public void MissingProvider_ReportsStaleKey()
    {
        var error = AiAgentViewModel.ValidateProvider("deleted-provider", null);

        Assert.That(error, Does.Contain("deleted-provider").And.Contain("models.json"));
    }

    [Test]
    public void UnsupportedApiType_ReportsApiValue()
    {
        var provider = ValidProvider();
        provider.Api = "anthropic";

        var error = AiAgentViewModel.ValidateProvider("key", provider);

        Assert.That(error, Does.Contain("anthropic").And.Contain("openai-completions"));
    }

    [Test]
    public void EmptyBaseUrl_ReportsMissingBaseUrl()
    {
        var provider = ValidProvider();
        provider.BaseUrl = "";

        var error = AiAgentViewModel.ValidateProvider("key", provider);

        Assert.That(error, Does.Contain("BaseUrl"));
    }

    [Test]
    public void RelativeBaseUrl_ReportsInvalidUrl()
    {
        var provider = ValidProvider();
        provider.BaseUrl = "/api";

        var error = AiAgentViewModel.ValidateProvider("key", provider);

        Assert.That(error, Does.Contain("/api"));
    }

    [Test]
    public void EmptyApiKey_ReportsMissingApiKey()
    {
        var provider = ValidProvider();
        provider.ApiKey = "";

        var error = AiAgentViewModel.ValidateProvider("key", provider);

        Assert.That(error, Does.Contain("API Key"));
    }

    [Test]
    public void PlaceholderApiKey_ReportsUnconfiguredKey()
    {
        var provider = ValidProvider();
        provider.ApiKey = "sk-REPLACE_WITH_YOUR_KEY";

        var error = AiAgentViewModel.ValidateProvider("key", provider);

        Assert.That(error, Does.Contain("占位"));
    }
}