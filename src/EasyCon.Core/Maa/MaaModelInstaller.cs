using System.Security.Cryptography;
using System.Text.Json;

namespace EasyCon.Core.Maa;

/// <summary>显式安装经摘要固定的官方中英试点模型；普通运行不访问网络。</summary>
public static class MaaModelInstaller
{
    public const string ModelSource = "https://raw.githubusercontent.com/MaaXYZ/MaaCommonAssets/dabcd4681ac990dc4361de26416d986abd80e4aa/OCR/ppocr_v4/zh_cn/";
    public static IReadOnlyDictionary<string, string> ModelHashes { get; } = new Dictionary<string, string>
    {
        ["det.onnx"] = "767e663dded50813dca0abc9a49859e345e0f74a48b387661188f096a9211d74",
        ["rec.onnx"] = "da4815ea386ee8083be319da8d20e70f4fdb0b6404ba0d2da7f675831e1b37fa",
        ["keys.txt"] = "28b2362ad4ab2dc38769aa72feb535e3a9ddb3fd2a7585a05920e6393b1dc7f7",
    };

    public static async Task<string> InstallAsync(string directory, CancellationToken token)
    {
        string root = Path.GetFullPath(directory);
        string destination = Path.Combine(root, "model", "ocr", "ppocr-v4-zh");
        Directory.CreateDirectory(destination);
        using HttpClient http = new() { Timeout = TimeSpan.FromMinutes(3) };
        foreach ((string name, string hash) in ModelHashes)
        {
            string target = Path.Combine(destination, name);
            if (File.Exists(target) && Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(target, token))) == hash) continue;
            byte[] bytes = await http.GetByteArrayAsync(ModelSource + name, token);
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != hash) throw new InvalidDataException($"模型 {name} 摘要不符");
            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllBytesAsync(temporary, bytes, token); File.Move(temporary, target, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        MaaOcrProfile profile = new()
        {
            Resource = ".",
            Compatibility = "pilot-unverified",
            Models = [new() { Id = "ppocr-v4-zh", Languages = ["chi_sim", "eng"], PathAliases = ["Tessdata"], Sha256 = new(ModelHashes) }],
        };
        string path = Path.Combine(root, "ocr-profile.json");
        if (!File.Exists(path))
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(profile, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), token);
        else _ = MaaOcrProfile.Load(path);
        await File.WriteAllTextAsync(Path.Combine(root, "MODEL-SOURCE.txt"),
            "PP-OCR v4 Chinese/English (PaddleOCR / Apache-2.0), distributed by MaaXYZ/MaaCommonAssets (MIT).\n" + ModelSource +
            "\nThis is a pilot model; text and confidence equivalence with legacy Tesseract is not asserted.\n", token);
        foreach ((string name, string url) in new[]
        {
            ("MAA-ASSETS-LICENSE.txt", "https://raw.githubusercontent.com/MaaXYZ/MaaCommonAssets/dabcd4681ac990dc4361de26416d986abd80e4aa/LICENSE"),
            ("PADDLE-OCR-LICENSE.txt", "https://raw.githubusercontent.com/PaddlePaddle/PaddleOCR/v2.7.3/LICENSE"),
        })
            if (!File.Exists(Path.Combine(root, name)))
                await File.WriteAllTextAsync(Path.Combine(root, name), await http.GetStringAsync(url, token), token);
        return path;
    }
}