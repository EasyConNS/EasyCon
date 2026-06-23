using EasyCon.Core.LLM.Mcp;
using EasyCon.Core.Services;
using EasyCon2.Avalonia.Core.AiAgent.Tools;
using EasyCon2.Avalonia.Core.Mcp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>
/// Tests for MCP-related classes.
/// </summary>
[TestFixture]
public class McpManagerTests
{
    private class FakeLogService : ILogService
    {
        public List<string> Logs { get; } = [];
        public event Action<string?, string?>? LogAppended;

        public void AddLog(string message, string? color = null)
        {
            Logs.Add(message);
            LogAppended?.Invoke(message, color);
        }

        public void Clear() => Logs.Clear();
        public string? Read() => null;

        // IIoAdapter implementation
        public void Print(string message, bool newline = true) { }
        public void Alert(string message) { }
        public string ReadLine() => "";
        public bool TryReadLine(out string line) { line = ""; return false; }
    }

    [Test]
    public void McpServerConnection_InitialState_IsConnecting()
    {
        var config = new McpServerConfig { Name = "Test Server" };
        var conn = new McpServerConnection("test", config);

        Assert.Multiple(() =>
        {
            Assert.That(conn.ServerKey, Is.EqualTo("test"));
            Assert.That(conn.Status, Is.EqualTo(McpConnectionStatus.Connecting));
            Assert.That(conn.Tools, Is.Empty);
            Assert.That(conn.Error, Is.Null);
            Assert.That(conn.Session, Is.Null);
        });
    }

    [Test]
    public void McpServerConnection_StatusTransition()
    {
        var config = new McpServerConfig { Name = "Test Server" };
        var conn = new McpServerConnection("test", config);

        conn.Status = McpConnectionStatus.Connected;
        Assert.That(conn.Status, Is.EqualTo(McpConnectionStatus.Connected));

        conn.Status = McpConnectionStatus.Failed;
        conn.Error = "Connection refused";
        Assert.Multiple(() =>
        {
            Assert.That(conn.Status, Is.EqualTo(McpConnectionStatus.Failed));
            Assert.That(conn.Error, Is.EqualTo("Connection refused"));
        });
    }

    [Test]
    public async Task McpServerConnection_DisposeAsync_SetsDisconnected()
    {
        var config = new McpServerConfig { Name = "Test Server" };
        var conn = new McpServerConnection("test", config);
        conn.Status = McpConnectionStatus.Connected;

        await conn.DisposeAsync();

        Assert.That(conn.Status, Is.EqualTo(McpConnectionStatus.Disconnected));
    }

    [Test]
    public void McpManager_GetConnection_ReturnsNull_WhenEmpty()
    {
        var log = new FakeLogService();
        var manager = new McpManager(log);

        var result = manager.GetConnection("nonexistent");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void McpManager_Connections_ReturnsSnapshot()
    {
        var log = new FakeLogService();
        var manager = new McpManager(log);

        var connections = manager.Connections;

        Assert.That(connections, Is.Empty);
    }

    [Test]
    public void McpManager_Dispose_ClearsConnections()
    {
        var log = new FakeLogService();
        var manager = new McpManager(log);

        manager.Dispose();

        Assert.That(manager.Connections, Is.Empty);
    }

    [Test]
    public void McpConfig_DefaultState()
    {
        var config = new McpConfig();

        Assert.Multiple(() =>
        {
            Assert.That(config.Mcp, Is.Not.Null);
            Assert.That(config.Mcp.Servers, Is.Empty);
        });
    }

    [Test]
    public void McpServerConfig_DefaultValues()
    {
        var config = new McpServerConfig();

        Assert.Multiple(() =>
        {
            Assert.That(config.Name, Is.EqualTo(""));
            Assert.That(config.Transport, Is.EqualTo(McpTransport.Stdio));
            Assert.That(config.Command, Is.EqualTo(""));
            Assert.That(config.Args, Is.Empty);
            Assert.That(config.Env, Is.Empty);
            Assert.That(config.Enabled, Is.True);
        });
    }
}
