using EasyCon.Core.Config;
using EasyCon.Core.LLM.Mcp;
using EasyCon.Core.Services;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EasyCon2.Avalonia.Mcp;

/// <summary>
/// MCP 服务器管理器实现。
/// 启动时连接所有启用的服务器，配置变更后差量重连。
/// 单台服务器连接失败不会阻塞其他服务器。
/// </summary>
public sealed class McpManager : IMcpManager
{
    private readonly ILogService _logService;
    private readonly IMcpSessionFactory _sessionFactory;
    private readonly List<McpServerConnection> _connections = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public IReadOnlyList<McpServerConnection> Connections
    {
        get
        {
            lock (_connections)
                return [.. _connections];
        }
    }

    /// <summary>
    /// 连接/断开/重连后工具集变化。在 Initialize/Refresh 调用者的 await 续体上触发
    /// （当前全部为 UI 线程 fire-and-forget）；若未来从线程池调用，订阅方需自行封送。
    /// </summary>
    public event Action? ToolsChanged;

    internal McpManager(ILogService logService, IMcpSessionFactory? sessionFactory = null)
    {
        _logService = logService;
        _sessionFactory = sessionFactory ?? new McpSessionFactory();
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_disposed) return;

            var config = ConfigManager.LoadMcpConfig();
            var enabled = config.Mcp.Servers
                .Where(kv => kv.Value.Enabled)
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            // 并行连接所有启用的服务器
            var tasks = enabled.Select(kv => ConnectServerAsync(kv.Key, kv.Value, ct));
            var results = await Task.WhenAll(tasks);

            foreach (var conn in results)
                AddConnection(conn);

            var successCount = _connections.Count(c => c.Status == McpConnectionStatus.Connected);
            _logService.AddLog($"[MCP] 初始化完成：{successCount}/{enabled.Count} 个服务器已连接");

            ToolsChanged?.Invoke();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_disposed) return;

            var config = ConfigManager.LoadMcpConfig();
            var newServers = config.Mcp.Servers;

            // 找出需要移除的连接（配置中不存在或被禁用）
            // 读写与 Remove 统一持 _connections 锁：工具执行线程经 GetConnection/Connections
            // 并发读取，无锁枚举/删除 List 可抛 InvalidOperationException
            var toRemove = new List<McpServerConnection>();
            lock (_connections)
            {
                foreach (var conn in _connections.ToList())
                {
                    if (!newServers.TryGetValue(conn.ServerKey, out var newConfig) || !newConfig.Enabled)
                    {
                        toRemove.Add(conn);
                    }
                    else if (ConfigChanged(conn.Config, newConfig))
                    {
                        // 配置变更 → 断开重连
                        toRemove.Add(conn);
                    }
                }
            }

            // 断开并移除
            foreach (var conn in toRemove)
            {
                // DisposeAsync 的 Kill+WaitForExit(5s) 位于其首个 await 之前，
                // 会在调用方线程同步执行；比照关窗路径放线程池，避免冻结 UI
                await Task.Run(() => conn.DisposeAsync().AsTask());
                lock (_connections)
                {
                    _connections.Remove(conn);
                }
                _logService.AddLog($"[MCP] 服务器 '{conn.ServerKey}' 已断开");
            }

            // 找出需要新增的连接
            var currentKeys = _connections.Select(c => c.ServerKey).ToHashSet();
            var toAdd = newServers
                .Where(kv => kv.Value.Enabled && !currentKeys.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            if (toAdd.Count > 0)
            {
                _logService.AddLog($"[MCP] 正在连接 {toAdd.Count} 个新服务器...");
                var tasks = toAdd.Select(kv => ConnectServerAsync(kv.Key, kv.Value, ct));
                var results = await Task.WhenAll(tasks);
                foreach (var conn in results)
                    AddConnection(conn);
            }

            ToolsChanged?.Invoke();

            var successCount = _connections.Count(c => c.Status == McpConnectionStatus.Connected);
            _logService.AddLog($"[MCP] 刷新完成：{successCount}/{newServers.Count(kv => kv.Value.Enabled)} 个服务器已连接");
        }
        finally
        {
            _lock.Release();
        }
    }

    public McpServerConnection? GetConnection(string serverKey)
    {
        lock (_connections)
            return _connections.FirstOrDefault(c => c.ServerKey == serverKey);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var conn in _connections.ToList())
        {
            try
            {
                await conn.DisposeAsync();
            }
            catch
            {
                // 单台服务器清理失败不阻断其余连接的释放
            }
        }

        _connections.Clear();
        // 不 Dispose _lock：若与 RefreshAsync 并发，Release 已释放的信号量会抛
        // ObjectDisposedException；SemaphoreSlim 不持有需及时释放的原生资源。
    }

    private async Task<McpServerConnection> ConnectServerAsync(string serverKey, McpServerConfig config, CancellationToken ct)
    {
        var conn = new McpServerConnection(serverKey, config);
        try
        {
            _logService.AddLog($"[MCP] 正在连接服务器 '{serverKey}'...");
            conn.Session = await _sessionFactory.CreateAsync(
                config.Transport,
                config.Command,
                config.Args,
                config.Env,
                ct);

            conn.Tools = await conn.Session.ListToolsAsync(ct);
            conn.Status = McpConnectionStatus.Connected;
            conn.Error = null;

            _logService.AddLog($"[MCP] 服务器 '{serverKey}' 已连接，发现 {conn.Tools.Count} 个工具");
        }
        catch (Exception ex)
        {
            conn.Status = McpConnectionStatus.Failed;
            conn.Error = ex.Message;
            _logService.AddLog($"[MCP] 服务器 '{serverKey}' 连接失败: {ex.Message}");
        }
        return conn;
    }

    private void AddConnection(McpServerConnection conn)
    {
        lock (_connections)
            _connections.Add(conn);
    }

    private static bool ConfigChanged(McpServerConfig old, McpServerConfig @new)
    {
        return old.Transport != @new.Transport
            || old.Command != @new.Command
            || !old.Args.SequenceEqual(@new.Args)
            || !old.Env.SequenceEqual(@new.Env);
    }
}

/// <summary>
/// 生产环境的 MCP 会话工厂，使用 SDK 的 McpClient.CreateAsync。
/// </summary>
internal sealed class McpSessionFactory : IMcpSessionFactory
{
    public async Task<IMcpSession> CreateAsync(
        McpTransport transport,
        string command,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string> env,
        CancellationToken ct)
    {
        if (transport != McpTransport.Stdio)
            throw new NotSupportedException($"暂不支持传输类型: {transport}");

        var customTransport = new CustomStdioClientTransport(command, args.ToList(), env.ToDictionary(kv => kv.Key, kv => kv.Value));
        try
        {
            var client = await McpClient.CreateAsync(customTransport, cancellationToken: ct);
            return new McpSessionWrapper(client);
        }
        catch
        {
            // 握手失败时 ConnectAsync 已经启动了子进程；不释放它就成了孤儿进程
            await customTransport.DisposeLastTransportAsync();
            throw;
        }
    }
}

/// <summary>
/// 将 SDK 的 McpClient 包装为 IMcpSession。
/// </summary>
internal sealed class McpSessionWrapper(McpClient client) : IMcpSession
{
    public ValueTask<IList<McpClientTool>> ListToolsAsync(CancellationToken ct)
        => client.ListToolsAsync(cancellationToken: ct);

    public ValueTask<CallToolResult> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?> args, CancellationToken ct)
        => client.CallToolAsync(toolName, args, cancellationToken: ct);

    public ValueTask<IList<McpClientResource>> ListResourcesAsync(CancellationToken ct)
        => client.ListResourcesAsync(cancellationToken: ct);

    public ValueTask<ReadResourceResult> ReadResourceAsync(string uri, CancellationToken ct)
        => client.ReadResourceAsync(uri, cancellationToken: ct);

    public ValueTask<IList<McpClientPrompt>> ListPromptsAsync(CancellationToken ct)
        => client.ListPromptsAsync(cancellationToken: ct);

    public ValueTask<GetPromptResult> GetPromptAsync(string promptName, IReadOnlyDictionary<string, object?>? args, CancellationToken ct)
        => client.GetPromptAsync(promptName, args, cancellationToken: ct);

    public async ValueTask DisposeAsync()
    {
        await client.DisposeAsync();
    }
}