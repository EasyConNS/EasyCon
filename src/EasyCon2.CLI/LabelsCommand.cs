using EasyCon.Capture;
using EasyCon.Core.Maa;
using System.CommandLine;
using System.Text.Json;

namespace EasyCon2.CLI;

public static class LabelsCommand
{
    public static Command Create()
    {
        Command labels = new("labels", "旧 IL 标签资源工具");
        Command convert = new("convert", "将直接指定的 ImgLabel 目录转换为独立 Maa 资源；不改写源文件");
        Argument<string> directory = new("directory") { Description = "ImgLabel 目录本身；不再追加 ImgLabel" };
        Option<string?> output = new("--output") { Description = "派生资源缓存目录；默认使用本机 EasyCon 缓存" };
        Option<bool> recursive = new("--recursive") { Description = "递归静态盘点；旧脚本运行的发现规则仍不递归" };
        Option<LegacyCompatibilityProfile> profile = new("--compatibility") { DefaultValueFactory = _ => LegacyCompatibilityProfile.LegacyGui };
        Option<string?> ocrProfile = new("--ocr-profile") { Description = "文字标签所需的显式 Maa OCR profile" };
        convert.Arguments.Add(directory);
        convert.Options.Add(output);
        convert.Options.Add(recursive);
        convert.Options.Add(profile);
        convert.Options.Add(ocrProfile);
        convert.SetAction(parse =>
        {
            try
            {
                string source = Path.GetFullPath(parse.GetValue(directory)!);
                if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
                LabelDiscovery discovery = LegacyLabelCatalog.Scan([source], parse.GetValue(recursive));
                string destination = parse.GetValue(output) ?? MaaLabelConverter.CacheRoot(source);
                string? ocrPath = parse.GetValue(ocrProfile) ?? Environment.GetEnvironmentVariable("EASYCON_MAA_OCR_PROFILE");
                LabelConversionResult result = MaaLabelConverter.Build(discovery, destination, parse.GetValue(profile), strict: true,
                    ocrProfile: string.IsNullOrWhiteSpace(ocrPath) ? null : MaaOcrProfile.Load(ocrPath));
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    result.Published,
                    generation = result.Generation?.Id,
                    directory = result.Generation?.Directory,
                    result.Report,
                }, new JsonSerializerOptions { WriteIndented = true }));
                return result.Report.ErrorCount == 0 && result.Published ? 0 : 1;
            }
            catch (Exception ex) { Console.Error.WriteLine($"标签转换失败: {ex.Message}"); return 1; }
        });
        labels.Subcommands.Add(convert);
        return labels;
    }
}