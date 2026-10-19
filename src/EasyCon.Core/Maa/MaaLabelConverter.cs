#nullable enable
using EasyCon.Capture;
using MaaFramework.Binding;
using OpenCvSharp;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EasyCon.Core.Maa;

public sealed record MaaLabelEntry(string Name, string Source, string SourceDigest, string SourceJson, SearchMethod Method,
    int[] Roi, string? TemplateDigest, string? NativeNode, string? NativeType, string? NativeParameters,
    string CompatNode, string CompatType, string CompatParameters, string RoutingPolicy,
    string ScoreTransform, string Rounding, string Preprocessing, string ReplayCoverage = "pending", string? Error = null)
{
    public ImgLabel ReadLabel()
    {
        ImgLabel label = JsonSerializer.Deserialize<ImgLabel>(SourceJson) ?? throw new InvalidDataException("标签内容为空");
        label.name = Name;
        return label;
    }
}

public sealed record LabelConversionReport(string Converter, int TotalFiles, int SelectedLabels, int ConvertedLabels,
    IReadOnlyList<LabelDiagnostic> Diagnostics)
{
    public int ErrorCount => Diagnostics.Count(item => item.Severity == "error");
}

public sealed record LabelGeneration(string Id, string Directory, LegacyCompatibilityProfile Profile,
    IReadOnlyDictionary<string, MaaLabelEntry> Labels, LabelConversionReport Report)
{
    public void Preflight(IEnumerable<string> requiredLabels)
    {
        foreach (string name in requiredLabels)
            if (!Labels.TryGetValue(name, out MaaLabelEntry? label) || label.Error != null)
                throw new InvalidDataException($"标签 [{name}] 资源预检失败: {label?.Error ?? "没有该标签"}");
    }
}

public sealed record LabelConversionResult(LabelGeneration? Generation, LabelConversionReport Report, bool Published);

/// <summary>只写派生资源。临时版本通过真实 Maa 装载后才发布，失败保留原 current.json。</summary>
public static class MaaLabelConverter
{
    public const string Version = "easycon.labels/3";
    private static readonly object PublicationGate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ScoreCondition(MaaLabelEntry entry, string comparison, int threshold, LegacyCompatibilityProfile profile)
    {
        if (!Enum.IsDefined(profile)) throw new InvalidDataException($"未知兼容 profile {profile}");
        _ = LegacyAlgorithmAdapter.Compare(0, comparison, threshold);
        return JsonSerializer.Serialize(new
        {
            custom_recognition = "EasyCon.LegacyScoreCondition",
            roi = entry.Roi,
            custom_recognition_param = new { label_id = entry.Name, comparison, threshold, compatibility = profile.ToString() },
        });
    }

    public static string CacheRoot(string projectDirectory)
    {
        string key = Digest(Encoding.UTF8.GetBytes(Path.GetFullPath(projectDirectory)));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyCon", "maa-cache", key);
    }

    public static LabelConversionResult Build(LabelDiscovery discovery, string cacheRoot, LegacyCompatibilityProfile profile,
        bool strict = false, string normalizationId = "BGR8/1920x1080", string modelDigest = "none", MaaOcrProfile? ocrProfile = null)
    {
        lock (PublicationGate)
        {
            if (!Enum.IsDefined(profile)) throw new InvalidDataException($"未知兼容 profile {profile}");
            List<LabelDiagnostic> diagnostics = [.. discovery.Diagnostics];
            Dictionary<string, MaaLabelEntry> entries = new(StringComparer.Ordinal);
            Dictionary<string, byte[]> images = new(StringComparer.Ordinal);
            JsonObject nativeNodes = new(), compatNodes = new();
            foreach (LegacyLabelSource source in discovery.Selected)
            {
                ImgLabel label = source.Read();
                string identity = Digest(Encoding.UTF8.GetBytes(source.Name));
                string nativeName = "EC.Legacy.Native." + identity;
                string compatName = "EC.Legacy.Compat." + identity;
                int[] roi = [label.RangeX, label.RangeY, label.RangeWidth, label.RangeHeight];
                string compatParameters = JsonSerializer.Serialize(new { custom_recognition = "EasyCon.LegacyTemplateMatch", custom_recognition_param = new { label_id = label.name }, roi });
                try
                {
                    ValidateRoi(label);
                    if (label.ImgBase64 == null) throw new InvalidDataException("模板或文本内容为 null");
                    if (label.searchMethod == SearchMethod.TesserDetect)
                    {
                        MaaOcrModel model = (ocrProfile ?? throw new NotSupportedException("文字标签需要显式 OCR 模型 profile"))
                            .Resolve(new Capabilities.OcrConfig { Language = "chi_sim" });
                        string ocrParameters = JsonSerializer.Serialize(new { roi, model = model.Id, only_rec = model.OnlyRecognition, order_by = model.OrderBy, threshold = 0 });
                        nativeNodes[nativeName] = Node("OCR", JsonNode.Parse(ocrParameters)!.AsObject());
                        compatNodes[compatName] = Node("Custom", JsonNode.Parse(compatParameters)!.AsObject());
                        entries.Add(source.Name, new(source.Name, source.Path, source.Digest, source.Content, label.searchMethod, roi, null,
                            nativeName, "OCR", ocrParameters, compatName, "Custom", compatParameters, "ocr-profile-pilot",
                            "100*text-similarity*confidence", profile == LegacyCompatibilityProfile.LegacyCli ? "Ceiling" : "Truncate", "legacy-text-trim/1"));
                        continue;
                    }
                    if (label.searchMethod is not (SearchMethod.SqDiffNormed or SearchMethod.CCorrNormed or SearchMethod.CCoeffNormed or
                        SearchMethod.MaskedSqDiffNormed or SearchMethod.EdgeDetectXY or SearchMethod.EdgeDetectLaplacian))
                        throw new NotSupportedException($"不支持算法 {label.searchMethod}");
                    if (label.TargetWidth > label.RangeWidth || label.TargetHeight > label.RangeHeight)
                        throw new InvalidDataException("声明的目标区域大于搜索范围");
                    using Mat original = Cv2.ImDecode(Convert.FromBase64String(label.ImgBase64), ImreadModes.Unchanged);
                    if (original.Empty() || original.Depth() != MatType.CV_8U)
                        throw new InvalidDataException("模板为空或位深不是 8 位");
                    if (original.Width > roi[2] || original.Height > roi[3])
                        throw new InvalidDataException($"实际模板 {original.Width}×{original.Height} 大于 ROI {roi[2]}×{roi[3]}");
                    using Mat color = new();
                    if (original.Channels() == 4) Cv2.CvtColor(original, color, ColorConversionCodes.BGRA2BGR);
                    else if (original.Channels() == 1) Cv2.CvtColor(original, color, ColorConversionCodes.GRAY2BGR);
                    else if (original.Channels() == 3) original.CopyTo(color);
                    else throw new InvalidDataException("模板通道数不支持");
                    bool mask = label.searchMethod == SearchMethod.MaskedSqDiffNormed;
                    bool nativeAvailable = label.searchMethod is SearchMethod.SqDiffNormed or SearchMethod.CCorrNormed or SearchMethod.CCoeffNormed;
                    if (mask)
                    {
                        if (original.Channels() != 4) throw new InvalidDataException("透明匹配需要四通道模板");
                        bool visible = false, collision = false;
                        int height = original.Height, width = original.Width;
                        for (int y = 0; y < height; y++)
                            for (int x = 0; x < width; x++)
                            {
                                Vec4b pixel = original.At<Vec4b>(y, x);
                                if (pixel.Item3 == 0) color.Set(y, x, new Vec3b(0, 255, 0));
                                else
                                {
                                    visible = true;
                                    collision |= pixel.Item0 == 0 && pixel.Item1 == 255 && pixel.Item2 == 0;
                                }
                            }
                        if (!visible) throw new InvalidDataException("模板完全透明");
                        nativeAvailable = !collision;
                        if (collision) diagnostics.Add(new("warning", "green-mask-collision", source.Path, source.Name, "不导出有损的原生 green_mask 节点；使用兼容识别"));
                    }
                    byte[] png = color.ToBytes(".png");
                    string imageDigest = Digest(png);
                    images.TryAdd(imageDigest, png);
                    JsonObject parameters = new()
                    {
                        ["template"] = "legacy/" + imageDigest + ".png",
                        ["roi"] = JsonSerializer.SerializeToNode(roi),
                        ["method"] = mask ? 1 : (int)label.searchMethod,
                        ["green_mask"] = mask,
                        ["threshold"] = label.searchMethod is SearchMethod.SqDiffNormed or SearchMethod.MaskedSqDiffNormed ? 1e12 : -1.1,
                        ["order_by"] = "Score",
                    };
                    string route = mask ? "mask-semantics" : nativeAvailable ? "native-with-integer-verification" : "preprocess-semantics";
                    if (nativeAvailable) nativeNodes[nativeName] = Node("TemplateMatch", parameters);
                    JsonObject compat = JsonNode.Parse(compatParameters)!.AsObject();
                    compatNodes[compatName] = Node("Custom", compat);
                    entries.Add(source.Name, new(source.Name, source.Path, source.Digest, source.Content, label.searchMethod, roi, imageDigest,
                        nativeAvailable ? nativeName : null, nativeAvailable ? "TemplateMatch" : null, nativeAvailable ? parameters.ToJsonString() : null,
                        compatName, "Custom", compatParameters, route,
                        label.searchMethod is SearchMethod.SqDiffNormed or SearchMethod.MaskedSqDiffNormed ? "100*(1-r)" : label.searchMethod == SearchMethod.CCorrNormed ? "100*r" : "50*(r+1)",
                        profile == LegacyCompatibilityProfile.LegacyCli ? "Ceiling" : "Truncate", "legacy/1"));
                }
                catch (Exception ex) when (ex is FormatException or InvalidDataException or OpenCVException or NotSupportedException)
                {
                    diagnostics.Add(new("error", "invalid-label", source.Path, source.Name, ex.Message));
                    entries.Add(source.Name, new(source.Name, source.Path, source.Digest, source.Content, label.searchMethod, roi, null, null, null, null,
                        compatName, "Custom", compatParameters, "invalid", "unavailable", profile.ToString(), "legacy/1", Error: ex.Message));
                }
            }
            LabelConversionReport report = new(Version, discovery.TotalFiles, entries.Count, entries.Values.Count(entry => entry.Error == null), diagnostics);
            if (strict && report.ErrorCount != 0) return new(null, report, false);
            string signature = JsonSerializer.Serialize(new
            {
                converter = Version,
                runtime = "5.14.3",
                binding = "5.10.0",
                nativeLoading = "short-bundle+managed-images/1",
                profile,
                normalizationId,
                modelDigest = ocrProfile?.Digest ?? modelDigest,
                // 来源路径也持久化在不可变索引中，迁移目录必须得到新的 generation。
                sources = discovery.Selected.Select(source => new { source.Name, source.Path, source.Digest, source.Priority }),
                diagnostics,
            });
            string id = Digest(Encoding.UTF8.GetBytes(signature));
            string root = Path.GetFullPath(cacheRoot);
            Directory.CreateDirectory(root);
            string destination = Path.Combine(root, id);
            string indexJson = JsonSerializer.Serialize(entries, JsonOptions);
            string reportJson = JsonSerializer.Serialize(report, JsonOptions);
            bool hasOcr = entries.Values.Any(entry => entry.Method == SearchMethod.TesserDetect && entry.Error == null);
            if (Directory.Exists(destination))
            {
                if (File.ReadAllText(Path.Combine(destination, "easycon", "manifest.json")) != signature ||
                    images.Any(image => Digest(File.ReadAllBytes(Path.Combine(destination, "image", "legacy", image.Key + ".png"))) != image.Key) ||
                    File.ReadAllText(Path.Combine(destination, "pipeline", "legacy.native.json")) != nativeNodes.ToJsonString(JsonOptions) ||
                    File.ReadAllText(Path.Combine(destination, "pipeline", "legacy.compat.json")) != compatNodes.ToJsonString(JsonOptions) ||
                    File.ReadAllText(Path.Combine(destination, "easycon", "labels.index.json")) != indexJson ||
                    File.ReadAllText(Path.Combine(destination, "easycon", "conversion.report.json")) != reportJson)
                    throw new InvalidDataException("已生成的标签缓存已损坏，请清理该缓存版本后重建");
                if (hasOcr) ocrProfile!.VerifyModels(destination);
                MaaRecognitionSession.ValidateResource(destination, entries);
            }
            if (!Directory.Exists(destination))
            {
                string stage = Path.Combine(root, ".building-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path.Combine(stage, "pipeline"));
                Directory.CreateDirectory(Path.Combine(stage, "image", "legacy"));
                Directory.CreateDirectory(Path.Combine(stage, "easycon"));
                try
                {
                    foreach ((string digest, byte[] png) in images) File.WriteAllBytes(Path.Combine(stage, "image", "legacy", digest + ".png"), png);
                    File.WriteAllText(Path.Combine(stage, "pipeline", "legacy.native.json"), nativeNodes.ToJsonString(JsonOptions));
                    File.WriteAllText(Path.Combine(stage, "pipeline", "legacy.compat.json"), compatNodes.ToJsonString(JsonOptions));
                    File.WriteAllText(Path.Combine(stage, "pipeline", "bootstrap.json"), "{\"EC.Legacy.HostReady\":{\"recognition\":\"DirectHit\",\"action\":\"DoNothing\"}}");
                    File.WriteAllText(Path.Combine(stage, "easycon", "labels.index.json"), indexJson);
                    File.WriteAllText(Path.Combine(stage, "easycon", "conversion.report.json"), reportJson);
                    File.WriteAllText(Path.Combine(stage, "easycon", "manifest.json"), signature);
                    if (ocrProfile != null && hasOcr)
                    {
                        string models = ocrProfile.Materialize();
                        foreach (MaaOcrModel model in ocrProfile.Models)
                        {
                            string target = Path.Combine(stage, "model", "ocr", model.Id);
                            Directory.CreateDirectory(target);
                            foreach (string file in model.Sha256.Keys) File.Copy(Path.Combine(models, "model", "ocr", model.Id, file), Path.Combine(target, file));
                        }
                        ocrProfile.VerifyModels(stage);
                    }
                    MaaRecognitionSession.ValidateResource(stage, entries);
                    Directory.Move(stage, destination);
                }
                finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
            }
            string current = Path.Combine(root, "current-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(current, JsonSerializer.Serialize(new { generation = id, compatibility = profile.ToString() }));
                File.Move(current, Path.Combine(root, "current.json"), true);
            }
            finally { if (File.Exists(current)) File.Delete(current); }
            GenerationLease.Prune(root, id, 8);
            return new(new(id, destination, profile, entries, report), report, true);
        }
    }

    private static JsonObject Node(string type, JsonObject parameters)
    {
        JsonObject result = (JsonObject)parameters.DeepClone();
        result["recognition"] = type;
        result["action"] = "DoNothing";
        result["pre_delay"] = 0;
        result["post_delay"] = 0;
        return result;
    }

    private static void ValidateRoi(ImgLabel label)
    {
        if (label.RangeX < 0 || label.RangeY < 0 || label.RangeWidth <= 0 || label.RangeHeight <= 0 ||
            (long)label.RangeX + label.RangeWidth > FrameNormalization.Width ||
            (long)label.RangeY + label.RangeHeight > FrameNormalization.Height)
            throw new InvalidDataException("ROI 必须位于 1920×1080 规范图像范围内");
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}