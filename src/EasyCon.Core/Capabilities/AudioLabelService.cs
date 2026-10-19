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
        return new Session(source, Path.GetFullPath(scriptDirectory), token);
    }

    private sealed class Session(IAudioCaptureSource source, string directory, CancellationToken runToken) : IAudioSession
    {
        // The script thread owns the dictionary; each recognition owns its background consumer.
        private readonly Dictionary<string, AudioRecognition> _loaded = new(StringComparer.Ordinal);
        private bool _disposed;

        public void Load(string name, CancellationToken token)
        {
            CheckHealth(token);
            if (!AudioLabelStore.IsValidName(name))
                throw new ArgumentException("声音标签名称无效，不能包含路径或扩展目录。", nameof(name));
            if (_loaded.ContainsKey(name))
                throw new InvalidOperationException($"声音标签“{name}”已加载；请先 AUDIO_UNLOAD，再 AUDIO_LOAD 开始新一轮。");
            string path = Path.Combine(directory, "audiolabel", name + ".audiolabel", "label.json");
            try
            {
                AudioLabel label = AudioLabelStore.Load(path);
                if (!string.Equals(label.Metadata.Name, name, StringComparison.Ordinal))
                    throw new InvalidDataException("目录名称与标签元数据中的名称不一致。");
                AudioMatchTemplate template = AudioMatchTemplate.Create(label.Clip, token)
                    ?? throw new InvalidDataException("标签没有足够的声音特征。");
                token.ThrowIfCancellationRequested();
                _loaded.Add(name, new AudioRecognition(source, template, runToken));
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or System.Text.Json.JsonException)
            {
                throw new InvalidOperationException($"声音标签“{name}”无法加载：{path}；{ex.Message}", ex);
            }
        }

        public int Score(string labelName, CancellationToken token)
        {
            CheckHealth(token);
            return GetLoaded(labelName).Score;
        }

        public void Unload(string labelName, CancellationToken token)
        {
            CheckHealth(token);
            AudioRecognition recognition = GetLoaded(labelName);
            _loaded.Remove(labelName);
            recognition.Dispose();
        }

        private AudioRecognition GetLoaded(string name)
            => _loaded.TryGetValue(name, out AudioRecognition? recognition) ? recognition
                : throw new InvalidOperationException($"声音标签“{name}”尚未加载或已卸载；请先 AUDIO_LOAD。");

        public void CheckHealth(CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            runToken.ThrowIfCancellationRequested();
            token.ThrowIfCancellationRequested();
            foreach ((string name, AudioRecognition recognition) in _loaded)
            {
                try { recognition.CheckHealth(); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new InvalidOperationException($"声音标签“{name}”后台识别失败：{ex.Message}", ex);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (AudioRecognition recognition in _loaded.Values)
                recognition.Dispose();
            _loaded.Clear();
        }
    }
}