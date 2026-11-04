using EasyCon2.Avalonia.Editor.Lsp;

namespace EasyCon2.Avalonia.Core.Tests;

[TestFixture]
[NonParallelizable]
public class LspClientServiceTests
{
    [Test]
    public async Task InitializeAsync_EmptyFilePath_ConnectsWithoutPathError()
    {
        List<string> messages = [];
        LspClientService.LogSink = messages.Add;

        await using LspClientService service = new();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));

        try
        {
            await service.InitializeAsync(string.Empty, timeout.Token);

            Assert.That(service.IsConnected, Is.True);
            Assert.That(messages, Has.None.Contains("Given path is null or empty"));
        }
        finally
        {
            LspClientService.LogSink = null;
        }
    }
}