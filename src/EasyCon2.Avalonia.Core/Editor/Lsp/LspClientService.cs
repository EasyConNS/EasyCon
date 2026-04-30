using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.General;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using System.Diagnostics;
using System.IO.Pipes;
using ServerCapabilities = OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities.ServerCapabilities;

namespace EasyCon2.Avalonia.Core.Editor.Lsp;

public class LspClientService : IDisposable, IAsyncDisposable
{
    private readonly object _lock = new();
    private ILanguageClient? _client;
    private readonly LspDocumentManager _documentManager;
    private bool _disposed;
    private bool _isInitialized;
    private string? _filePath;
    private Task? _serverTask;
    private NamedPipeServerStream? _serverPipe;
    private NamedPipeClientStream? _clientPipe;

    public bool IsConnected
    {
        get { lock (_lock) return _client != null && _isInitialized; }
    }

    public ServerCapabilities? ServerCapabilities { get; private set; }
    public LspDocumentManager DocumentManager => _documentManager;

    public event Action? Connected;
    public event Action<string>? ConnectionFailed;

    public LspClientService()
    {
        _documentManager = new LspDocumentManager(this);
    }

    public async Task InitializeAsync(string filePath, CancellationToken ct = default)
    {
        _filePath = filePath;
        await TryConnectAsync(ct);
    }

    private async Task TryConnectAsync(CancellationToken ct)
    {
        lock (_lock) { if (_disposed) return; }

        try
        {
            await StartAndInitializeAsync(ct);
            Connected?.Invoke();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LSP] Connection failed: {ex.Message}");
            ConnectionFailed?.Invoke($"LSP 服务启动失败: {ex.Message}");
            Cleanup();
        }
    }

    private async Task StartAndInitializeAsync(CancellationToken ct)
    {
        var pipeName = $"ecs-lsp-{Guid.NewGuid():N}";

        _serverPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        _serverTask = Task.Run(async () =>
        {
            try
            {
                await _serverPipe.WaitForConnectionAsync();
                await EasyCon.Lsp.EcsLanguageServer.RunAsync(_serverPipe, _serverPipe);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LSP] Server exited: {ex.Message}");
            }
        });

        _clientPipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await _clientPipe.ConnectAsync();

        DocumentUri? rootUri = null;
        if (_filePath != null)
            rootUri = DocumentUri.FromFileSystemPath(_filePath);

        _client = LanguageClient.Create(options =>
        {
            options.WithInput(_clientPipe)
                   .WithOutput(_clientPipe);

            if (rootUri != null)
                options.WithRootUri(rootUri);

            options.WithClientCapabilities(new ClientCapabilities
            {
                TextDocument = new TextDocumentClientCapabilities
                {
                    Completion = new CompletionCapability
                    {
                        DynamicRegistration = false,
                        CompletionItem = new CompletionItemCapabilityOptions
                        {
                            SnippetSupport = false,
                            DocumentationFormat = new Container<MarkupKind>(
                                MarkupKind.PlainText)
                        }
                    },
                    Hover = new HoverCapability
                    {
                        DynamicRegistration = false,
                        ContentFormat = new Container<MarkupKind>(
                            MarkupKind.PlainText, MarkupKind.Markdown)
                    },
                    Definition = new DefinitionCapability
                    {
                        DynamicRegistration = false
                    },
                    DocumentSymbol = new DocumentSymbolCapability
                    {
                        DynamicRegistration = false,
                        HierarchicalDocumentSymbolSupport = true
                    }
                }
            });
        });

        await _client.Initialize(ct);

        if (_client is LanguageClient lc)
            ServerCapabilities = lc.ServerSettings?.Capabilities;

        lock (_lock) _isInitialized = true;
    }

    public async Task<IEnumerable<CompletionItem>?> RequestCompletionAsync(CompletionParams parameters, CancellationToken ct = default)
    {
        ILanguageClient? client;
        lock (_lock) client = _isInitialized ? _client : null;
        if (client == null) return null;

        try { return await client.RequestCompletion(parameters, ct); }
        catch (Exception ex) { Debug.WriteLine($"[LSP] Completion failed: {ex.Message}"); return null; }
    }

    public async Task<Hover?> RequestHoverAsync(HoverParams parameters, CancellationToken ct = default)
    {
        ILanguageClient? client;
        lock (_lock) client = _isInitialized ? _client : null;
        if (client == null) return null;

        try { return await client.RequestHover(parameters, ct); }
        catch (Exception ex) { Debug.WriteLine($"[LSP] Hover failed: {ex.Message}"); return null; }
    }

    public async Task<LocationOrLocationLinks?> RequestDefinitionAsync(DefinitionParams parameters, CancellationToken ct = default)
    {
        ILanguageClient? client;
        lock (_lock) client = _isInitialized ? _client : null;
        if (client == null) return null;

        try { return await client.RequestDefinition(parameters, ct); }
        catch (Exception ex) { Debug.WriteLine($"[LSP] Definition failed: {ex.Message}"); return null; }
    }

    public async Task<IEnumerable<SymbolInformationOrDocumentSymbol>?> RequestDocumentSymbolAsync(
        DocumentSymbolParams parameters, CancellationToken ct = default)
    {
        ILanguageClient? client;
        lock (_lock) client = _isInitialized ? _client : null;
        if (client == null) return null;

        try { return await client.RequestDocumentSymbol(parameters, ct); }
        catch (Exception ex) { Debug.WriteLine($"[LSP] DocumentSymbol failed: {ex.Message}"); return null; }
    }

    public async Task<SemanticTokens?> RequestSemanticTokensAsync(SemanticTokensParams parameters, CancellationToken ct = default)
    {
        ILanguageClient? client;
        lock (_lock) client = _isInitialized ? _client : null;
        if (client == null) return null;

        try { return await client.RequestSemanticTokensFull(parameters, ct); }
        catch (Exception ex) { Debug.WriteLine($"[LSP] SemanticTokens failed: {ex.Message}"); return null; }
    }

    public void SendDidOpen(DidOpenTextDocumentParams parameters)
    {
        ILanguageClient? client;
        lock (_lock) client = _isInitialized ? _client : null;
        if (client == null) return;
        try { client.DidOpenTextDocument(parameters); }
        catch (Exception ex) { Debug.WriteLine($"[LSP] didOpen failed: {ex.Message}"); }
    }

    public void SendDidChange(DidChangeTextDocumentParams parameters)
    {
        ILanguageClient? client;
        lock (_lock) client = _isInitialized ? _client : null;
        if (client == null) return;
        try { client.DidChangeTextDocument(parameters); }
        catch (Exception ex) { Debug.WriteLine($"[LSP] didChange failed: {ex.Message}"); }
    }

    public void SendDidClose(DidCloseTextDocumentParams parameters)
    {
        ILanguageClient? client;
        lock (_lock) client = _isInitialized ? _client : null;
        if (client == null) return;
        try { client.DidCloseTextDocument(parameters); }
        catch (Exception ex) { Debug.WriteLine($"[LSP] didClose failed: {ex.Message}"); }
    }

    public async ValueTask ShutdownAsync()
    {
        ILanguageClient? client;
        lock (_lock)
        {
            _isInitialized = false;
            client = _client;
            _client = null;
        }

        if (client != null)
        {
            try
            {
                var shutdown = client.Shutdown();
                var completed = await Task.WhenAny(shutdown, Task.Delay(2000));
                if (completed == shutdown)
                {
                    client.SendExit();
                    await Task.Delay(500);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LSP] Shutdown error: {ex.Message}");
            }
            client.Dispose();
        }

        Cleanup();

        if (_serverTask != null)
        {
            try { await Task.WhenAny(_serverTask, Task.Delay(2000)); }
            catch { }
            _serverTask = null;
        }
    }

    private void Cleanup()
    {
        lock (_lock)
        {
            if (_clientPipe != null)
            {
                try { _clientPipe.Close(); } catch { }
                _clientPipe = null;
            }

            if (_serverPipe != null)
            {
                try { _serverPipe.Close(); } catch { }
                _serverPipe = null;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        await ShutdownAsync();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _ = ShutdownAsync();
    }
}