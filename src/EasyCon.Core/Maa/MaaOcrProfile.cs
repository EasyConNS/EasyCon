#nullable enable
using EasyCon.Core.Capabilities;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyCon.Core.Maa;

public sealed record MaaOcrModel
{
    public string Id { get; init; } = "";
    public string[] Languages { get; init; } = [];
    public string[] PathAliases { get; init; } = [];
    public string[] EngineModes { get; init; } = ["DEFAULT"];
    public string[] PageModes { get; init; } = ["SINGLE_LINE"];
    public bool OnlyRecognition { get; init; } = true;
    public string OrderBy { get; init; } = "Vertical";
    public Dictionary<string, string> Sha256 { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>显式迁移映射；traineddata 路径仅作别名，绝不当作 Maa 模型加载。</summary>
public sealed class MaaOcrProfile
{
    public string Schema { get; init; } = "easycon.maa-ocr/1";
    public string Resource { get; init; } = "";
    public string DefaultLanguage { get; init; } = "chi_sim";
    public string Compatibility { get; init; } = "pilot-unverified";
    public MaaOcrModel[] Models { get; init; } = [];
    [JsonIgnore] public string Digest { get; private set; } = "";
    [JsonIgnore] public string SourcePath { get; private set; } = "";

    public static MaaOcrProfile Load(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string json = File.ReadAllText(fullPath);
        MaaOcrProfile profile = JsonSerializer.Deserialize<MaaOcrProfile>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("OCR profile 为空");
        if (profile.Schema != "easycon.maa-ocr/1" || profile.Models.Length == 0)
            throw new InvalidDataException("OCR profile schema 或模型列表无效");
        profile.SourcePath = fullPath;
        profile.Digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        foreach (MaaOcrModel model in profile.Models)
        {
            if (model.Id.Length == 0 || model.Id != Path.GetFileName(model.Id) || model.Id is "." or ".." ||
                model.Id.IndexOfAny(['/', '\\', ':']) >= 0 || model.Id.Any(char.IsControl) ||
                model.Languages.Length == 0 || model.PageModes.Length == 0 || model.EngineModes.Length == 0 ||
                model.OrderBy is not ("Horizontal" or "Vertical" or "Area" or "Length" or "Score" or "Random"))
                throw new InvalidDataException("OCR 模型 ID、语言或 PSM 映射无效");
            if (model.Sha256.Keys.Any(file => file is not ("det.onnx" or "rec.onnx" or "keys.txt")))
                throw new InvalidDataException("OCR 模型文件名无效");
            foreach (string file in new[] { "det.onnx", "rec.onnx", "keys.txt" })
                if (!model.Sha256.TryGetValue(file, out string? digest) || digest.Length != 64 || !digest.All(char.IsAsciiHexDigit))
                    throw new InvalidDataException($"OCR 模型 [{model.Id}] 缺少 {file} 的 SHA-256");
        }
        return profile;
    }

    public MaaOcrModel Resolve(OcrConfig config)
    {
        if (config.Options.Keys.Any(key => key is not ("tess:engineMode" or "tess:psmode")))
            throw new InvalidDataException("OCR profile 未声明该选项的等价映射");
        string language = string.IsNullOrEmpty(config.Language) ? DefaultLanguage : config.Language;
        string engine = config.Options.GetValueOrDefault("tess:engineMode", "DEFAULT");
        string psm = config.Options.GetValueOrDefault("tess:psmode", "SINGLE_LINE");
        MaaOcrModel[] matches = Models.Where(model => model.Languages.Contains(language, StringComparer.OrdinalIgnoreCase) &&
            model.EngineModes.Contains(engine, StringComparer.OrdinalIgnoreCase) && model.PageModes.Contains(psm, StringComparer.OrdinalIgnoreCase) &&
            (string.IsNullOrEmpty(config.ModelPath) || model.PathAliases.Any(alias =>
                string.Equals(alias, config.ModelPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(alias, Path.GetFileName(config.ModelPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), StringComparison.OrdinalIgnoreCase))))
            .ToArray();
        return matches.Length == 1 ? matches[0] : throw new InvalidDataException($"OCR 配置没有唯一模型映射: lang={language}, engine={engine}, psm={psm}, path={config.ModelPath}");
    }

    [JsonIgnore] public string ResourceDirectory => Path.GetFullPath(Resource, Path.GetDirectoryName(SourcePath)!);

    /// <summary>复制经校验的模型到不可变版本，避免运行中编辑模型污染当前会话。</summary>
    public string Materialize()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyCon", "maa-ocr-cache");
        string destination = Path.Combine(root, Digest);
        lock (MaterializationGate)
        {
            foreach (MaaOcrModel model in Models)
                foreach ((string file, string hash) in model.Sha256)
                {
                    if (file is not ("det.onnx" or "rec.onnx" or "keys.txt")) throw new InvalidDataException("OCR 模型文件名无效");
                    string path = Path.Combine(ResourceDirectory, "model", "ocr", model.Id, file);
                    if (!File.Exists(path) || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), hash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"OCR 模型缺失或摘要不符: {path}");
                }
            if (!Directory.Exists(destination))
            {
                Directory.CreateDirectory(root);
                string stage = Path.Combine(root, ".building-" + Guid.NewGuid().ToString("N"));
                try
                {
                    foreach (MaaOcrModel model in Models)
                    {
                        string target = Path.Combine(stage, "model", "ocr", model.Id);
                        Directory.CreateDirectory(target);
                        foreach (string file in model.Sha256.Keys) File.Copy(Path.Combine(ResourceDirectory, "model", "ocr", model.Id, file), Path.Combine(target, file));
                        foreach ((string file, string hash) in model.Sha256)
                            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(target, file)))), hash, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException("OCR 模型在复制期间发生变化");
                    }
                    Directory.CreateDirectory(Path.Combine(stage, "pipeline"));
                    File.WriteAllText(Path.Combine(stage, "pipeline", "bootstrap.json"), "{\"EC.Ocr.Ready\":{\"recognition\":\"DirectHit\",\"action\":\"DoNothing\"}}");
                    Directory.Move(stage, destination);
                }
                finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
            }
            VerifyModels(destination);
            return destination;
        }
    }

    private static readonly object MaterializationGate = new();

    internal void VerifyModels(string directory)
    {
        foreach (MaaOcrModel model in Models)
            foreach ((string file, string hash) in model.Sha256)
            {
                string path = Path.Combine(directory, "model", "ocr", model.Id, file);
                if (!File.Exists(path) || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"OCR 派生模型缓存缺失或已损坏: {path}");
            }
    }
}