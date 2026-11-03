using EasyCon.Core.Projects;
using System.CommandLine;

namespace EasyCon2.CLI;

/// <summary>
/// init：初始化脚本项目基础框架（main.ecs / lib/mod1.ecs / models/ / ImgLabel/）。
/// 目录约定与运行时识别规则一致：lib/ 自动导入、ImgLabel/ 顶层 *.IL、models/ 相对路径约定。
/// </summary>
public static class InitCommand
{
    public static Command Create()
    {
        var command = new Command("init", "初始化脚本项目框架（main.ecs / lib/mod1.ecs / models/ / ImgLabel/）");
        var pathArgument = new Argument<string>("path")
        {
            Description = "项目目录；缺省为当前目录",
            DefaultValueFactory = _ => ".",
        };
        var forceOption = new Option<bool>("--force", "-f")
        {
            Description = "覆盖已存在的 main.ecs / lib/mod1.ecs（不删除任何目录）",
            DefaultValueFactory = _ => false,
        };
        command.Arguments.Add(pathArgument);
        command.Options.Add(forceOption);
        command.SetAction(parseResult =>
            Run(parseResult.GetValue(pathArgument)!, parseResult.GetValue(forceOption)));
        return command;
    }

    static int Run(string path, bool force)
    {
        ScaffoldResult result;
        try
        {
            result = ProjectScaffold.Create(path, force);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Console.Error.WriteLine($"错误: 无效项目路径: {ex.Message}");
            return 1;
        }

        if (!result.Success)
        {
            if (result.Error != null)
            {
                Console.Error.WriteLine($"错误: {result.Error}");
                foreach (string dir in result.CreatedDirectories)
                    Console.Error.WriteLine($"  已创建目录  {Display(result.RootPath, dir)}/");
                foreach (string file in result.CreatedFiles)
                    Console.Error.WriteLine($"  已写入文件  {Display(result.RootPath, file)}");
            }
            else
            {
                Console.Error.WriteLine("错误: 目标已存在以下文件，未做任何修改：");
                foreach (string conflict in result.Conflicts)
                    Console.Error.WriteLine($"  {Display(result.RootPath, conflict)}");
                Console.Error.WriteLine("使用 --force 覆盖，或换一个空目录。");
            }
            return 1;
        }

        Console.WriteLine($"已初始化脚本项目: {result.RootPath}");
        foreach (string dir in result.CreatedDirectories)
            Console.WriteLine($"  创建目录  {Display(result.RootPath, dir)}/");
        foreach (string dir in result.ExistingDirectories)
            Console.WriteLine($"  已存在    {Display(result.RootPath, dir)}/（保留）");
        foreach (string file in result.CreatedFiles)
            Console.WriteLine($"  写入文件  {Display(result.RootPath, file)}");
        Console.WriteLine();
        Console.WriteLine("下一步:");
        Console.WriteLine($"  cd {result.RootPath}");
        Console.WriteLine("  ezcon run main.ecs --port mock");
        Console.WriteLine("  ezcon compile main.ecs");
        return 0;
    }

    /// <summary>输出统一用 POSIX 分隔符，避免 Windows/Linux 提示不一致。</summary>
    static string Display(string root, string relative) => relative.Replace('\\', '/');
}