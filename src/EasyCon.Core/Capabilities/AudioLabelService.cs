using EasyCon.Capture.Audio;

namespace EasyCon.Core.Capabilities;

/// <summary>Desktop adapter borrowing the GUI's game-audio source and resolving script-local labels.</summary>
public sealed class AudioLabelService(IAudioCaptureSource source, string? scriptDirectory) : IAudioService
{
    public IAudioSession BeginRun(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(scriptDirectory))
            throw new InvalidOperationException("请先保存脚本；声音标签从脚本目录的 audiolabel 文件夹加载。");
        return new Session(source, Path.GetFullPath(scriptDirectory));
    }

    private sealed class Session : IAudioSession
    {
        private readonly AudioDetectionSession _detection;
        private readonly string _directory;
        private readonly Dictionary<string, AudioMatchTemplate> _templates = new(StringComparer.Ordinal);

        public Session(IAudioCaptureSource source, string directory)
        {
            _directory = directory;
            _detection = new AudioDetectionSession(source, Load);
        }

        private AudioMatchTemplate Load(string name, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!AudioLabelStore.IsValidName(name))
                throw new ArgumentException("声音标签名称无效，不能包含路径或扩展目录。", nameof(name));
            if (_templates.TryGetValue(name, out AudioMatchTemplate? cached))
                return cached;
            string path = Path.Combine(_directory, "audiolabel", name + ".audiolabel", "label.json");
            try
            {
                AudioLabel label = AudioLabelStore.Load(path);
                if (!string.Equals(label.Metadata.Name, name, StringComparison.Ordinal))
                    throw new InvalidDataException("目录名称与标签元数据中的名称不一致。");
                AudioMatchTemplate template = AudioMatchTemplate.Create(label.Clip, token)
                    ?? throw new InvalidDataException("标签没有足够的声音特征。");
                // Bound feature memory as well as PCM history; reload evicted labels on demand.
                if (_templates.Count >= 32)
                    _templates.Clear();
                _templates.Add(name, template);
                return template;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or System.Text.Json.JsonException)
            {
                throw new InvalidOperationException($"声音标签“{name}”无法加载：{path}；{ex.Message}", ex);
            }
        }

        public double Match(string labelName, int lookbackMs, CancellationToken token)
            => _detection.Match(labelName, lookbackMs, token);

        public bool Wait(string labelName, int timeoutMs, double threshold, CancellationToken token)
            => _detection.Wait(labelName, timeoutMs, threshold, token);

        public double GetWaitScore(CancellationToken token) => _detection.GetWaitScore(token);

        public void Dispose() => _detection.Dispose();
    }
}