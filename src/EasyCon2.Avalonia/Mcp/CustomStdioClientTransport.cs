using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace EasyCon2.Avalonia.Mcp;

/// <summary>
/// 自定义 StdioClientTransport，绕过 SDK 内置实现。
/// 直接启动目标进程，不经过 cmd.exe 包装，解决 Windows 编码和参数解析问题。
/// </summary>
internal sealed class CustomStdioClientTransport : IClientTransport
{
    private readonly string _command;
    private readonly IReadOnlyList<string> _args;
    private readonly IReadOnlyDictionary<string, string> _env;
    private ProcessManagedTransport? _lastTransport;

    public string Name { get; }

    public CustomStdioClientTransport(string command, IReadOnlyList<string> args, IReadOnlyDictionary<string, string> env)
    {
        _command = command ?? throw new ArgumentNullException(nameof(command));
        _args = args ?? throw new ArgumentNullException(nameof(args));
        _env = env ?? throw new ArgumentNullException(nameof(env));
        Name = $"stdio-{Path.GetFileName(command)}";
    }

    public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _command,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false),
        };

        foreach (var arg in _args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        foreach (var (key, value) in _env)
        {
            startInfo.Environment[key] = value;
        }

        var process = new Process { StartInfo = startInfo };

        if (!process.Start())
        {
            throw new IOException($"Failed to start MCP server process: {_command}");
        }

        // stderr 必须持续排空：MCP 服务器普遍向 stderr 打日志，4KB 管道缓冲写满后
        // 子进程 WriteFile 阻塞 → 服务器整体挂死、所有工具调用超时。丢弃或转日志均可，唯独不能不读。
        _ = Task.Run(async () =>
        {
            try
            {
                var reader = process.StandardError;
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                    System.Diagnostics.Debug.WriteLine($"[mcp:{Name}] {line}");
            }
            catch
            {
                // 进程退出/流关闭后的读取失败属正常收尾
            }
        });

        // 使用自定义 Transport 实现
        var transport = new StdioProcessTransport(
            process.StandardInput.BaseStream,
            process.StandardOutput.BaseStream,
            Name);

        _lastTransport = new ProcessManagedTransport(transport, process);
        return Task.FromResult<ITransport>(_lastTransport);
    }

    /// <summary>
    /// 释放最近一次 <see cref="ConnectAsync"/> 产出的传输（含 Kill 子进程树）。
    /// SDK 握手（McpClient.CreateAsync）失败时不会处置传输，调用方必须显式调用，
    /// 否则 MCP 服务器进程会成为孤儿。
    /// </summary>
    public async ValueTask DisposeLastTransportAsync()
    {
        if (_lastTransport != null)
        {
            await _lastTransport.DisposeAsync();
            _lastTransport = null;
        }
    }
}

/// <summary>
/// 基于 Process 的 Stdio 传输实现。
/// 直接读写 stdin/stdout，不经过 SDK 的 StreamClientSessionTransport。
/// </summary>
internal sealed class StdioProcessTransport : TransportBase
{
    private readonly Stream _inputStream;
    private readonly StreamReader _outputReader;
    private readonly CancellationTokenSource _cts = new();
    private Task? _readTask;
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public StdioProcessTransport(Stream inputStream, Stream outputStream, string name)
        : base(name, null)
    {
        _inputStream = inputStream;
        _outputReader = new StreamReader(outputStream, new UTF8Encoding(false));
        SetConnected();
        _readTask = Task.Run(ReadMessagesAsync);
    }

    public override async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var bytes = new UTF8Encoding(false).GetBytes(json + "\n");
        await _inputStream.WriteAsync(bytes, cancellationToken);
        await _inputStream.FlushAsync(cancellationToken);
    }

    private async Task ReadMessagesAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var line = await _outputReader.ReadLineAsync(_cts.Token);
                if (line == null) break;

                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var message = JsonSerializer.Deserialize<JsonRpcMessage>(line, _jsonOptions);
                    if (message != null)
                    {
                        await WriteMessageAsync(message, _cts.Token);
                    }
                }
                catch (Exception ex)
                {
                    // 单条解析失败继续收（可能是服务器日志混入 stdout），但要留痕
                    System.Diagnostics.Debug.WriteLine($"[{Name}] 消息解析失败: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (Exception ex)
        {
            // 读循环死亡 = 服务器崩溃/管道断开：静默吞掉会让后续所有调用
            // 变成晦涩的管道错误，至少送进诊断通道
            System.Diagnostics.Debug.WriteLine($"[{Name}] 读循环异常退出: {ex.Message}");
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_readTask != null)
        {
            try { await _readTask.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch { /* 忽略 */ }
        }
        _cts.Dispose();
        _inputStream.Dispose();
        _outputReader.Dispose();
    }
}

/// <summary>
/// 包装传输，管理进程生命周期。
/// </summary>
internal sealed class ProcessManagedTransport : ITransport
{
    private readonly ITransport _inner;
    private readonly Process _process;

    public string? SessionId => _inner.SessionId;
    public ChannelReader<JsonRpcMessage> MessageReader => _inner.MessageReader;

    public ProcessManagedTransport(ITransport inner, Process process)
    {
        _inner = inner;
        _process = process;
    }

    public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
        => _inner.SendMessageAsync(message, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        catch { /* 忽略关闭错误 */ }

        await _inner.DisposeAsync();
        _process.Dispose();
    }
}