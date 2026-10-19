#nullable enable
using EasyCon.Capture;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;
using OpenCvSharp;
using System.Security.Cryptography;
using System.Text.Json;

namespace EasyCon.Core.Maa;

/// <summary>专用识别 Tasker；显式图片、不绑定手柄，每个完成请求取出托管结果后清理原生历史。</summary>
public sealed class MaaRecognitionSession : IMaaRecognitionSession, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly MaaResource _resource;
    private readonly MaaTasker _tasker;
    private readonly LegacyRecognition _legacy;
    private readonly LegacyRecognition _condition;
    private readonly MaaResourceBundleLease? _bundle;
    private readonly Dictionary<string, Size> _templates = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();
    private volatile bool _disposed;
    private int _queued;
    private int _liveImages;
    private static int _activeSessions;
    public int QueuedRequests => Volatile.Read(ref _queued);
    public int LiveImages => Volatile.Read(ref _liveImages);
    public static int ActiveSessions => Volatile.Read(ref _activeSessions);
    public long CompletedRequests { get; private set; }
    public int RetainedResults { get; private set; }
    public int ValidatedTemplates => _templates.Count;
    public string NativeResourceDirectory { get; }

    public MaaRecognitionSession(string? resourceDirectory = null, IReadOnlyDictionary<string, MaaLabelEntry>? labels = null)
    {
        _ = MaaEnvironment.RuntimeVersion;
        _legacy = new(labels ?? new Dictionary<string, MaaLabelEntry>());
        _condition = new(labels ?? new Dictionary<string, MaaLabelEntry>(), true);
        string directory = resourceDirectory ?? MaaEnvironment.BootstrapResource;
        _bundle = labels == null ? null : new(directory);
        NativeResourceDirectory = _bundle?.Directory ?? directory;
        try { _resource = CreateResource(NativeResourceDirectory, _legacy, _condition); }
        catch { _bundle?.Dispose(); throw; }
        MaaTasker? tasker = null;
        try
        {
            tasker = new() { Resource = _resource, Controller = MaaController.Null, DisposeOptions = DisposeOptions.None };
            if (!tasker.IsInitialized)
                throw new InvalidOperationException("Maa 独立识别 Tasker 未初始化");
            _tasker = tasker;
            LoadAndValidateTemplates(directory, labels);
            Interlocked.Increment(ref _activeSessions);
        }
        catch { tasker?.Dispose(); _resource.Dispose(); _bundle?.Dispose(); throw; }
    }

    internal static void ValidateResource(string directory, IReadOnlyDictionary<string, MaaLabelEntry> labels)
    {
        using MaaRecognitionSession session = new(directory, labels);
    }

    private void LoadAndValidateTemplates(string directory, IReadOnlyDictionary<string, MaaLabelEntry>? labels)
    {
        if (labels == null) return;
        foreach (string digest in labels.Values.Where(entry => entry.Error == null && entry.NativeType == "TemplateMatch")
            .Select(entry => entry.TemplateDigest!).Distinct(StringComparer.Ordinal))
        {
            string name = "legacy/" + digest + ".png";
            string path = Path.Combine(directory, "image", "legacy", digest + ".png");
            try
            {
                byte[] png = File.ReadAllBytes(path);
                if (Convert.ToHexStringLower(SHA256.HashData(png)) != digest)
                    throw new InvalidDataException("模板摘要不符");
                using MaaImageBuffer image = new();
                if (!image.TrySetEncodedData(png) || !_resource.OverrideImage(name, image))
                    throw new InvalidDataException("Maa 模板内存装载失败");
                // 避免原生 imread 的长路径限制；同时以真实 TemplateMatch 验证缓冲区已被资源持有。
                using Mat dimensions = Cv2.ImDecode(png, ImreadModes.Color);
                string parameters = JsonSerializer.Serialize(new
                {
                    template = name,
                    roi = new[] { 0, 0, dimensions.Width, dimensions.Height },
                    method = 1,
                    threshold = 2,
                    green_mask = false,
                });
                MaaTaskJob probe = _tasker.AppendRecognition("TemplateMatch", parameters, image);
                MaaJobStatus status = probe.Wait();
                using RecognitionDetail? result = probe.QueryRecognitionDetail();
                if (status != MaaJobStatus.Succeeded || result == null)
                    throw new InvalidDataException($"原生模板探针失败（{status}）");
                ValidateTemplateDetail(result.Detail);
                using JsonDocument detail = JsonDocument.Parse(result.Detail);
                if (!result.Hit || detail.RootElement.GetProperty("best").ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("原生模板探针没有命中自身图像");
                _templates.Add(name, dimensions.Size());
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or OpenCVException)
            {
                throw new InvalidDataException($"Maa 模板资源不可用: {path}", ex);
            }
            finally
            {
                if (!_tasker.ClearCache()) throw new InvalidOperationException("Maa 模板探针历史清理失败");
            }
        }
    }

    private void ValidateTemplateRequest(string parameters)
    {
        using JsonDocument document = JsonDocument.Parse(parameters);
        if (!document.RootElement.TryGetProperty("template", out JsonElement value))
            throw new InvalidDataException("Maa TemplateMatch 没有指定模板资源");
        string?[] names = value.ValueKind == JsonValueKind.String ? [value.GetString()] :
            value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(item => item.GetString()).ToArray() : [];
        if (names.Length == 0 || names.Any(name => name == null || !_templates.ContainsKey(name)))
            throw new InvalidDataException("Maa TemplateMatch 引用了未通过原生装载验证的模板资源");
        if (document.RootElement.TryGetProperty("roi", out JsonElement roi) && roi.ValueKind == JsonValueKind.Array)
        {
            int[] bounds = roi.EnumerateArray().Select(item => item.GetInt32()).ToArray();
            if (bounds.Length != 4 || names.Any(name => bounds[2] < _templates[name!].Width || bounds[3] < _templates[name!].Height))
                throw new InvalidDataException("Maa 模板 ROI 小于已装载的模板，不能视为正常无候选");
        }
    }

    private static void ValidateTemplateDetail(string detail, bool allowEmpty = false)
    {
        using JsonDocument document = JsonDocument.Parse(detail);
        if (!document.RootElement.TryGetProperty("all", out JsonElement all) || all.ValueKind != JsonValueKind.Array || (!allowEmpty && all.GetArrayLength() == 0))
            throw new InvalidDataException("Maa 原生模板未产生计算结果（资源不可用或计算失败），不能视为正常无候选");
    }

    private static MaaResource CreateResource(string directory, LegacyRecognition legacy, LegacyRecognition condition)
    {
        _ = MaaEnvironment.RuntimeVersion;
        MaaResource resource = new();
        try
        {
            if (!resource.SetOption(ResourceOption.InferenceDevice, InferenceDevice.CPU) ||
                !resource.SetOption(ResourceOption.InferenceExecutionProvider, InferenceExecutionProvider.CPU) ||
                !resource.Register("EasyCon.LegacyTemplateMatch", legacy) || !resource.Register("EasyCon.LegacyScoreCondition", condition))
                throw new InvalidOperationException("Maa 识别资源配置或扩展注册失败");
            if (resource.AppendBundle(directory).Wait() != MaaJobStatus.Succeeded || !resource.IsLoaded)
                throw new InvalidDataException($"Maa 识别资源加载失败: {directory}");
            return resource;
        }
        catch { resource.Dispose(); throw; }
    }

    public async Task<MaaRecognitionResult> RecognizeAsync(MaaRecognitionRequest request, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        token = cancellation.Token;
        if (Interlocked.Increment(ref _queued) > 32)
        {
            Interlocked.Decrement(ref _queued);
            throw new InvalidOperationException("Maa 识别队列已满（32 个等待请求）");
        }
        try { await _gate.WaitAsync(token).ConfigureAwait(false); }
        finally { Interlocked.Decrement(ref _queued); }
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await Task.Run(() => Recognize(request, token), CancellationToken.None).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private MaaRecognitionResult Recognize(MaaRecognitionRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.Type == "TemplateMatch") ValidateTemplateRequest(request.Parameters);
        _legacy.Error = null;
        _legacy.Token = token;
        _condition.Error = null;
        _condition.Token = token;
        try
        {
            using MaaImageBuffer image = new();
            Volatile.Write(ref _liveImages, 1);
            if (!image.TrySetEncodedData(request.Png.Span)) throw new InvalidDataException("Maa 输入图像解码失败");
            MaaTaskJob? cancellationStop = null;
            // 取消调用方（包括 GUI 线程）只发出 Stop；当前工作线程负责排空后再清历史。
            using CancellationTokenRegistration registration = token.Register(() => cancellationStop = _tasker.Stop());
            try
            {
                token.ThrowIfCancellationRequested();
                MaaTaskJob job = _tasker.AppendRecognition(request.Type, request.Parameters, image);
                MaaJobStatus status = job.Wait();
                token.ThrowIfCancellationRequested();
                if (_legacy.Error != null) throw new InvalidOperationException("Maa 兼容识别失败", _legacy.Error);
                if (_condition.Error != null) throw new InvalidOperationException("Maa 旧分数条件识别失败", _condition.Error);
                using RecognitionDetail? result = job.QueryRecognitionDetail();
                if (result == null) throw new InvalidOperationException($"Maa {request.Type} 未提供识别结果（{status}）");
                if (status != MaaJobStatus.Succeeded) throw new InvalidOperationException($"Maa {request.Type} 识别作业失败（{status}）");
                // 负相关等正常候选过滤也可能返回 all=[]；模板已在同一原生资源中逐一自匹配验证。
                if (request.Type == "TemplateMatch") ValidateTemplateDetail(result.Detail, allowEmpty: true);
                RetainedResults = 1;
                return new(request.RequestId, status.ToString(), result.Detail, null, result.Hit);
            }
            finally
            {
                registration.Dispose();
                cancellationStop?.Wait();
            }
        }
        finally
        {
            Volatile.Write(ref _liveImages, 0);
            if (!_tasker.ClearCache()) throw new InvalidOperationException("Maa 识别历史缓存清理失败");
            RetainedResults = 0;
            CompletedRequests++;
        }
    }

    private void RequestStop()
    {
        try { if (!_disposed) _tasker.Stop().Wait(); }
        catch (ObjectDisposedException) { }
    }

    public async Task StopAsync()
    {
        _lifetime.Cancel();
        await Task.Run(RequestStop).ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _lifetime.Cancel();
        RequestStop();
        _gate.Wait();
        try
        {
            if (_disposed) return;
            _disposed = true;
            _tasker.Dispose();
            _resource.Dispose();
            _bundle?.Dispose();
            _lifetime.Dispose();
            Interlocked.Decrement(ref _activeSessions);
        }
        finally { _gate.Release(); }
    }

    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }

    private sealed class LegacyRecognition(IReadOnlyDictionary<string, MaaLabelEntry> labels, bool condition = false) : IMaaCustomRecognition
    {
        public string Name { get; set; } = condition ? "EasyCon.LegacyScoreCondition" : "EasyCon.LegacyTemplateMatch";
        public Exception? Error { get; set; }
        public CancellationToken Token { get; set; }

        public bool Analyze<T>(T context, in AnalyzeArgs args, in AnalyzeResults results) where T : IMaaContext
        {
            try
            {
                Token.ThrowIfCancellationRequested();
                using JsonDocument parameters = JsonDocument.Parse(args.RecognitionParam);
                string name = parameters.RootElement.GetProperty("label_id").GetString() ?? "";
                if (!labels.TryGetValue(name, out MaaLabelEntry? entry) || entry.Error != null)
                    throw new InvalidDataException($"标签 [{name}] 不可用");
                RawMatchResult raw;
                if (entry.Method == SearchMethod.TesserDetect)
                {
                    using RecognitionDetail result = context.RunRecognition(entry.NativeNode!, args.Image)
                        ?? throw new InvalidOperationException("Maa 文字标签没有返回识别结果");
                    using JsonDocument textDetail = JsonDocument.Parse(result.Detail);
                    JsonElement[] lines = textDetail.RootElement.GetProperty("all").EnumerateArray().ToArray();
                    string text = string.Join('\n', lines.Select(line => line.GetProperty("text").GetString()));
                    double confidence = lines.Length == 0 ? 0 : lines.Average(line => line.GetProperty("score").GetDouble());
                    raw = new(LegacyAlgorithmAdapter.TextSimilarity(text, entry.ReadLabel().ImgBase64) * confidence,
                        new Rect(entry.Roi[0], entry.Roi[1], entry.Roi[2], entry.Roi[3]), entry.Method, "maa-ocr/5.14.3");
                }
                else
                {
                    if (!args.Image.TryGetEncodedData(out byte[]? png) || png == null) throw new InvalidDataException("Maa 回调图像编码失败");
                    using Mat image = Cv2.ImDecode(png, ImreadModes.Color);
                    raw = LegacyTemplateMatcher.Match(entry.ReadLabel(), image);
                }
                Token.ThrowIfCancellationRequested();
                string detail = JsonSerializer.Serialize(new
                {
                    raw = raw.Score,
                    method = (int)raw.Method,
                    box = new[] { raw.Box.X, raw.Box.Y, raw.Box.Width, raw.Box.Height },
                    algorithm_version = raw.AlgorithmVersion,
                });
                if (!results.Box.TrySetValues(raw.Box.X, raw.Box.Y, raw.Box.Width, raw.Box.Height) || !results.Detail.TrySetValue(detail))
                    throw new InvalidOperationException("Maa 兼容识别结果写回失败");
                // true 表示计算有效；业务阈值仍由旧 ECS 比较返回分数。
                if (!condition) return true;
                JsonElement config = parameters.RootElement;
                LegacyCompatibilityProfile profile = Enum.Parse<LegacyCompatibilityProfile>(config.GetProperty("compatibility").GetString()!, false);
                return LegacyAlgorithmAdapter.Compare(LegacyAlgorithmAdapter.ToLegacyInteger(raw, profile),
                    config.GetProperty("comparison").GetString()!, config.GetProperty("threshold").GetInt32());
            }
            catch (Exception ex) { Error = ex; return false; }
        }
    }
}