using System.Diagnostics;
using OmniSharp.Extensions.LanguageServer.Client;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.General;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using ServerCapabilities = OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities.ServerCapabilities;

namespace EasyCon2.Avalonia.Core.Editor.Lsp;

public class LspClientService : IDisposable
{
    private readonly object _lock = new();
    private Process? _process;
    private ILanguageClient? _client;
    private readonly LspDocumentManager _documentManager;
    private bool _disposed;
    private bool _isInitialized;
    private string? _filePath;

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

    public async Task InitializeAsync(string filePath)
    {
        _filePath = filePath;
        await TryConnect();
    }

    private async Task TryConnect()
    {
        lock (_lock) { if (_disposed) return; }

        try
        {
            await StartAndInitialize();
            Connected?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LSP] Connection failed: {ex.Message}");
            ConnectionFailed?.Invoke($"LSP 服务启动失败: {ex.Message}");
            CleanupProcess();
        }
    }

    private async Task StartAndInitialize()
    {
        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = "-m easycon_script_lsp --stdio",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        _process.Exited += OnProcessExited;
        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Debug.WriteLine($"[LSP stderr] {e.Data}");
        };

        _process.Start();
        _process.BeginErrorReadLine();

        DocumentUri? rootUri = null;
        if (_filePath != null)
            rootUri = DocumentUri.FromFileSystemPath(_filePath);

        _client = LanguageClient.Create(options =>
        {
            options.WithInput(_process.StandardOutput.BaseStream)
                   .WithOutput(_process.StandardInput.BaseStream);

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

        await _client.Initialize(CancellationToken.None);

        if (_client is LanguageClient lc)
            ServerCapabilities = lc.ServerSettings?.Capabilities;

        lock (_lock) _isInitialized = true;
        Debug.WriteLine("[LSP] Connected and initialized");
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        lock (_lock)
        {
            _isInitialized = false;
            if (_disposed) return;
        }

        Debug.WriteLine("[LSP] Process exited unexpectedly");
    }

    public async Task<IEnumerable<CompletionItem>?> RequestCompletion(CompletionParams parameters)
    {
        ILanguageClient? client;
        lock (_lock) client = _isInitialized ? _client : null;
        if (client == null) return null;

        try { return await client.RequestCompletion(parameters, CancellationToken.None); }
        catch (Exception ex) { Debug.WriteLine($"[LSP] Completion failed: {ex.Message}"); return null; }
    }

    public async Task<Hover?> RequestHover(HoverParams parameters)
    {
        ILanguageClient? client;
        lock (_lock) client = _isInitialized ? _client : null;
        if (client == null) return null;

        try { return await client.RequestHover(parameters, CancellationToken.None); }
        catch (Exception ex) { Debug.WriteLine($"[LSP] Hover failed: {ex.Message}"); return null; }
    }

    public async Task<LocationOrLocationLinks?> RequestDefinition(DefinitionParams parameters)
    {
        ILanguageClient? client;
        lock (_lock) client = _isInitialized ? _client : null;
        if (client == null) return null;

        try { return await client.RequestDefinition(parameters, CancellationToken.None); }
        catch (Exception ex) { Debug.WriteLine($"[LSP] Definition failed: {ex.Message}"); return null; }
    }

    public async Task<IEnumerable<SymbolInformationOrDocumentSymbol>?> RequestDocumentSymbol(
        DocumentSymbolParams parameters)
    {
        ILanguageClient? client;
        lock (_lock) client = _isInitialized ? _client : null;
        if (client == null) return null;

        try { return await client.RequestDocumentSymbol(parameters); }
        catch (Exception ex) { Debug.WriteLine($"[LSP] DocumentSymbol failed: {ex.Message}"); return null; }
    }

    public async Task<SemanticTokens?> RequestSemanticTokens(SemanticTokensParams parameters)
    {
        ILanguageClient? client;
        lock (_lock) client = _isInitialized ? _client : null;
        if (client == null) return null;

        try { return await client.RequestSemanticTokensFull(parameters, CancellationToken.None); }
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
                await Task.WhenAny(shutdown, Task.Delay(2000));
                client.SendExit();
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LSP] Shutdown error: {ex.Message}");
            }
            client.Dispose();
        }

        CleanupProcess();
    }

    private void CleanupProcess()
    {
        Process? proc;
        lock (_lock)
        {
            proc = _process;
            _process = null;
        }

        if (proc == null) return;
        try
        {
            proc.Exited -= OnProcessExited;
            if (!proc.HasExited) proc.Kill();
        }
        catch (Exception ex) { Debug.WriteLine($"[LSP] Cleanup error: {ex.Message}"); }
        finally { proc.Dispose(); }
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
