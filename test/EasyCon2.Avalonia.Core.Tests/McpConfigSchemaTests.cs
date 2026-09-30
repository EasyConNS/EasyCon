using EasyCon.Core.LLM.Mcp;
using System.Text.Json;

namespace EasyCon2.Avalonia.Core.Tests;

/// <summary>
/// mcp.json 落盘 schema 回归测试：配置模型序列化必须输出标准小写键名
/// （否则手写的小写配置文件在第一次保存后被整体改写成 PascalCase），
/// 且读取端必须兼容已被改写成 PascalCase / 数字枚举的历史文件。
/// </summary>
[TestFixture]
public class McpConfigSchemaTests
{
    private static McpConfig SampleConfig() => new()
    {
        Mcp = new McpSection
        {
            Servers = new Dictionary<string, McpServerConfig>
            {
                ["filesystem"] = new McpServerConfig
                {
                    Name = "文件系统",
                    Transport = McpTransport.Stdio,
                    Command = "npx",
                    Args = ["-y", "server-filesystem"],
                    Env = new Dictionary<string, string> { ["KEY"] = "value" },
                    Enabled = true
                }
            }
        }
    };

    [Test]
    public void Serialized_UsesStandardLowercaseKeys()
    {
        var json = JsonSerializer.Serialize(SampleConfig());

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"mcp\""));
            Assert.That(json, Does.Contain("\"servers\""));
            Assert.That(json, Does.Contain("\"name\""));
            Assert.That(json, Does.Contain("\"command\""));
            Assert.That(json, Does.Contain("\"args\""));
            Assert.That(json, Does.Contain("\"env\""));
            Assert.That(json, Does.Contain("\"enabled\""));
            Assert.That(json, Does.Not.Contain("\"Mcp\""), "PascalCase 键名会破坏标准 schema");
            Assert.That(json, Does.Not.Contain("\"Command\""));
        });
    }

    [Test]
    public void Transport_SerializedAsLowercaseString_NotNumber()
    {
        var json = JsonSerializer.Serialize(SampleConfig());

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"transport\":\"stdio\""), "transport 必须是小写字符串而非数字");
            Assert.That(json, Does.Not.Contain("\"transport\":0"));
        });
    }

    [Test]
    public void Deserialization_AcceptsLegacyPascalCaseFile()
    {
        // 已经被旧版本写坏的历史文件必须照常读取（生产读路径为大小写不敏感）
        const string json = """
{
  "Mcp": {
    "Servers": {
      "github": {
        "Name": "GitHub",
        "Transport": 0,
        "Command": "npx",
        "Args": ["-y", "server-github"],
        "Env": {},
        "Enabled": false
      }
    }
  }
}
""";
        var path = Path.Combine(Path.GetTempPath(), $"mcp-schema-test-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json);
            var text = File.ReadAllText(path);
            // 与 ConfigManager 生产读路径一致：大小写不敏感
            var config = JsonSerializer.Deserialize<McpConfig>(
                text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.Multiple(() =>
            {
                Assert.That(config, Is.Not.Null);
                var server = config.Mcp.Servers["github"];
                Assert.That(server.Command, Is.EqualTo("npx"));
                Assert.That(server.Transport, Is.EqualTo(McpTransport.Stdio), "历史文件中的数字枚举必须可读");
                Assert.That(server.Enabled, Is.False);
            });
        }
        finally
        {
            File.Delete(path);
        }
    }
}
