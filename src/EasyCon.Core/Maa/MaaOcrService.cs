#nullable enable
using EasyCon.Core.Capabilities;
using EasyCon.Core.Logging;
using OpenCvSharp;
using System.Text.Json;

namespace EasyCon.Core.Maa;

/// <summary>显式模型 profile 的 Maa OCR 试点；已有图像不借用采集或输入，旧失败返回契约不变。</summary>
public sealed class MaaOcrService(MaaOcrProfile profile, CancellationToken token = default) : IOcrService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, MaaOcrModel> _initialized = new(StringComparer.OrdinalIgnoreCase);
    private MaaRecognitionSession? _session;
    private bool _disposed;
    private static int _activeModelResources;
    public string Backend => "maa";
    public int LastConfidence { get; private set; }
    public double LastRawConfidence { get; private set; }
    public string ModelGeneration => profile.Digest;
    public string Compatibility => profile.Compatibility;
    public static int ActiveModelResources => Volatile.Read(ref _activeModelResources);
    public MaaOcrService CreateInvocation(CancellationToken cancellationToken) => new(profile, cancellationToken);

    public bool Init(OcrConfig config)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            string language = string.IsNullOrEmpty(config.Language) ? profile.DefaultLanguage : config.Language;
            try
            {
                MaaOcrModel model = profile.Resolve(config);
                if (_session == null)
                {
                    _session = new(profile.Materialize());
                    Interlocked.Increment(ref _activeModelResources);
                }
                _initialized[language] = model;
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _initialized.Remove(language);
                CoreLog.Error($"Maa OCR 初始化失败: {ex.Message}");
                return false;
            }
        }
    }

    public string Recognize(ImageRef image, OcrQuery query) => Recognize(image, query, default);

    public string Recognize(ImageRef image, OcrQuery query, CancellationToken operationToken)
    {
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, operationToken);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellation.Token.ThrowIfCancellationRequested();
            try
            {
                using Mat source = Cv2.ImDecode(Convert.FromBase64String(image.Base64), ImreadModes.Color);
                if (source.Empty()) return "";
                int x = Math.Clamp(query.X, 0, source.Width), y = Math.Clamp(query.Y, 0, source.Height);
                int width = query.Width > 0 ? Math.Clamp(query.Width, 0, source.Width - x) : source.Width - x;
                int height = query.Height > 0 ? Math.Clamp(query.Height, 0, source.Height - y) : source.Height - y;
                if (width == 0 || height == 0) return "OCR ARGS ERR!";
                string language = string.IsNullOrEmpty(query.Language) ? profile.DefaultLanguage : query.Language;
                if (!_initialized.TryGetValue(language, out MaaOcrModel? model))
                {
                    if (!Init(new OcrConfig { Language = language })) return "";
                    model = _initialized[language];
                }
                using Mat roi = new(source, new Rect(x, y, width, height));
                string parameters = JsonSerializer.Serialize(new { model = model.Id, only_rec = model.OnlyRecognition, threshold = 0, order_by = model.OrderBy });
                MaaRecognitionResult result = _session!.RecognizeAsync(new("OCR", parameters, roi.ToBytes(".png"), Guid.NewGuid().ToString("N"), null, profile.Digest), cancellation.Token).GetAwaiter().GetResult();
                using JsonDocument detail = JsonDocument.Parse(result.Detail ?? throw new InvalidDataException("Maa OCR 没有返回结果"));
                JsonElement[] lines = detail.RootElement.GetProperty("all").EnumerateArray().ToArray();
                if (lines.Length == 0)
                {
                    LastRawConfidence = 0;
                    LastConfidence = 0;
                    return "";
                }
                double confidence = lines.Average(line => line.GetProperty("score").GetDouble());
                if (!double.IsFinite(confidence)) throw new InvalidDataException("OCR 置信度无效");
                LastRawConfidence = confidence;
                LastConfidence = (int)(confidence * 100);
                return string.Join('\n', lines.Select(line => line.GetProperty("text").GetString()));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { CoreLog.Error($"Maa OCR 识别失败: {ex.Message}"); return ""; }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_session != null)
            {
                try { _session.Dispose(); }
                finally { Interlocked.Decrement(ref _activeModelResources); }
            }
            _initialized.Clear();
        }
    }
}