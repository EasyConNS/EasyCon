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
        doctor.SetAction(_ =>
        {
            try
            {
                Console.WriteLine(JsonSerializer.Serialize(MaaHostDiagnostics.CheckRuntime()));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Maa 自检失败: {ex.Message}");
                return 1;
            }
        });
        command.Subcommands.Add(doctor);
        return command;
    }
}