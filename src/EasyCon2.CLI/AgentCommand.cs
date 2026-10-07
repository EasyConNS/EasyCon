using EasyCon.Core.Config;
using EasyCon.Core.LLM;
using EasyCon.Core.LLM.Agent;
using EasyCon.Core.LLM.Agent.Skills;
using EasyCon.Core.LLM.Agent.Tools;
using EasyCon.Core.LLM.Messages;
using EasyCon.Core.LLM.Models;
using EasyCon.Core.LLM.Skills;
using System.CommandLine;

namespace EasyCon2.CLI;

/// <summary>
/// 无头 AI Agent 命令：与 GUI 共用 EasyCon.Core.LLM.Agent 编排器与工具集，
/// 无 UI 依赖。危险工具（RequiresConfirmation）经控制台 y/N 交互确认，
/// 非交互输入（重定向）一律拒绝（fail-closed）。
/// </summary>
public static class AgentCommand
{
    public static Command Create()
    {
        var command = new Command("agent", "AI Agent：以自然语言驱动工作区（无头模式）");
        var taskArgument = new Argument<string>("task")
        {
            Description = "要完成的任务描述"
        };
        var fileOption = new Option<string?>("--file", "-f")
        {
            Description = "工作区内的脚本文件（决定工作区根目录；缺省为当前目录）"
        };
        var providerOption = new Option<string?>("--provider", "-p")
        {
            Description = "模型供应商 key（缺省取配置中的第一个）"
        };
        var modelOption = new Option<string?>("--model", "-m")
        {
            Description = "模型 ID（缺省取该供应商的第一个模型）"
        };
        var yesOption = new Option<bool>("--yes", "-y")
        {
            Description = "自动允许所有危险操作确认（跳过交互，慎用）"
        };
        command.Arguments.Add(taskArgument);
        command.Options.Add(fileOption);
        command.Options.Add(providerOption);
        command.Options.Add(modelOption);
        command.Options.Add(yesOption);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var task = parseResult.GetValue(taskArgument)!;
            var file = parseResult.GetValue(fileOption);
            var providerKey = parseResult.GetValue(providerOption);
            var modelId = parseResult.GetValue(modelOption);
            var autoYes = parseResult.GetValue(yesOption);
            return await RunAsync(task, file, providerKey, modelId, autoYes, cancellationToken);
        });
        return command;
    }

    private static async Task<int> RunAsync(
        string task,
        string? file,
        string? providerKey,
        string? modelId,
        bool autoYes,
        CancellationToken ct)
    {
        // 工作区根目录：--file 所在目录，否则当前目录
        var root = file is not null
            ? Path.GetFullPath(Path.GetDirectoryName(Path.GetFullPath(file)) ?? ".")
            : Directory.GetCurrentDirectory();
        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"错误: 工作区目录不存在: {root}");
            return 1;
        }

        // 供应商/模型解析：与 GUI 共用 ConfigManager 配置
        var models = ConfigManager.LoadModelsConfig();
        if (models.Models.Providers.Count == 0)
        {
            Console.Error.WriteLine("错误: 未配置任何模型供应商（请在 GUI 模型配置中添加）");
            return 1;
        }
        var provider = (!string.IsNullOrEmpty(providerKey)
                && models.Models.Providers.TryGetValue(providerKey, out var picked)
                    ? picked
                    : null)
            ?? models.Models.Providers.Values.First();
        var model = (!string.IsNullOrEmpty(modelId)
                ? provider.Models.FirstOrDefault(m => m.Id == modelId)
                : null)
            ?? provider.Models.FirstOrDefault();
        if (model is null)
        {
            Console.Error.WriteLine($"错误: 供应商 {provider.Name} 下没有可用模型");
            return 1;
        }

        // 技能：内置 + 用户/项目级（与 GUI 相同的加载逻辑）
        var skills = new SkillRegistry();
        foreach (var skill in BundledSkills.CreateAll())
            skills.Register(skill);
        SkillLoader.LoadToRegistry(skills, SkillLoader.GetSearchPaths(root).ToList());

        var registry = new ToolRegistry();
        WorkspaceFileTools.RegisterAll(registry, () => root);
        var orchestrator = new AgentOrchestrator(registry, skills,
            clientFactory: null,
            projectDirectoryProvider: () => root,
            confirmationHandler: _ => Task.FromResult(autoYes || ConfirmOnConsole()));

        Console.WriteLine($"[agent] 工作区: {root}");
        Console.WriteLine($"[agent] 模型: {provider.Name} / {model.Id}（视觉: {(model.Vision ? "开" : "关")}）");
        Console.WriteLine();

        var history = new List<ChatMessage> { ChatMessage.User(task) };
        try
        {
            await orchestrator.RunAsync(history, model.Id, provider, HandleEvent, ct, visionSupported: false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n[agent] 已取消");
            return 130;
        }
    }

    private static void HandleEvent(AgentEvent evt)
    {
        switch (evt)
        {
            case AgentEvent.ContentDelta d:
                Console.Write(d.Text);
                break;
            case AgentEvent.ToolExecuting t:
                Console.WriteLine($"\n[tool] {t.Name} ...");
                break;
            case AgentEvent.ToolCompleted t:
                Console.WriteLine($"[tool] {t.Name} 完成: {t.Summary}");
                break;
            case AgentEvent.ToolAwaitingConfirmation a:
                Console.WriteLine($"[confirm] {a.Reason}");
                break;
            case AgentEvent.UsageUpdated u:
                Console.WriteLine($"\n[usage] {u.Total} tokens");
                break;
            case AgentEvent.Error e:
                Console.Error.WriteLine($"[错误] {e.Message}");
                break;
            case AgentEvent.Retrying r:
                Console.WriteLine($"[retry {r.Attempt}/{r.MaxAttempts}] {r.Reason}");
                break;
            case AgentEvent.Completed c:
                Console.WriteLine();
                break;
        }
    }

    /// <summary>控制台确认；非交互输入（stdin 重定向）一律拒绝。</summary>
    private static bool ConfirmOnConsole()
    {
        if (Console.IsInputRedirected)
        {
            Console.WriteLine("[confirm] 非交互环境，自动拒绝（如需自动允许请加 --yes）");
            return false;
        }
        Console.Write("[confirm] 允许执行? (y/N): ");
        var line = Console.ReadLine();
        return line is not null && line.Trim().Equals("y", StringComparison.OrdinalIgnoreCase);
    }
}