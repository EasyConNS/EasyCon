using EasyCon.Core.Maa;
using System.CommandLine;
using System.Text.Json;

namespace EasyCon2.CLI;

public static class MaaHostCommand
{
    public static Command Create()
    {
        Command command = new("maa", "Maa 宿主工具");
        Command doctor = new("doctor", "检查发行目录的 Maa 原生依赖、bootstrap、显式图像及 PNG 桥接；不连接硬件");
        Option<string?> ocrProfile = new("--ocr-profile") { Description = "另用显式中英试点模型识别已知图片，检查发行包的 OCR 原生依赖" };
        doctor.Options.Add(ocrProfile);
        doctor.SetAction(parse =>
        {
            try
            {
                MaaRuntimeCheck runtime = MaaHostDiagnostics.CheckRuntime();
                string? profile = parse.GetValue(ocrProfile);
                if (profile == null) Console.WriteLine(JsonSerializer.Serialize(runtime));
                else Console.WriteLine(JsonSerializer.Serialize(new { runtime, ocr = MaaHostDiagnostics.CheckOcr(profile) }));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Maa 自检失败: {ex.Message}");
                return 1;
            }
        });
        command.Subcommands.Add(doctor);
        Command models = new("models", "OCR 试点模型工具");
        Command install = new("install", "显式下载固定摘要的中英 PP-OCR v4 模型，生成 OCR profile；不声明旧置信度等价");
        Argument<string> directory = new("directory") { Description = "独立模型安装目录" };
        install.Arguments.Add(directory);
        install.SetAction(async (parse, token) =>
        {
            try { Console.WriteLine(await MaaModelInstaller.InstallAsync(parse.GetValue(directory)!, token)); return 0; }
            catch (Exception ex) { Console.Error.WriteLine($"模型安装失败: {ex.Message}"); return 1; }
        });
        models.Subcommands.Add(install);
        command.Subcommands.Add(models);
        return command;
    }
}