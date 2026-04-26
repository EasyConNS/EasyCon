using System.Collections.Concurrent;

namespace EasyCon2.Forms;

internal class RichLogBox : RichTextBox
{
    private readonly ConcurrentQueue<Tuple<object, Color?>> _messages = new();
    private readonly AutoResetEvent _wake = new(false);

    private bool _msgNewLine = true;
    private bool _msgFirstLine = true;
    private const int MaxTextLength = 500_000;

    public RichLogBox()
    {
        IProgress<List<Tuple<object, Color?>>> progress = new Progress<List<Tuple<object, Color?>>>(batch =>
        {
            SuspendLayout();
            try
            {
                foreach (var tuple in batch)
                {
                    var msg = tuple.Item1;
                    var color = tuple.Item2;

                    SelectionStart = TextLength;
                    SelectionLength = 0;
                    SelectionColor = color ?? Color.White;

                    if (msg == null)
                    {
                        Text = string.Empty;
                    }
                    else
                    {
                        AppendText(msg.ToString());
                    }
                }

                // 截断：超过上限时删除前半部分
                if (TextLength > MaxTextLength)
                {
                    SelectionStart = 0;
                    SelectionLength = TextLength / 2;
                    SelectedText = string.Empty;
                }

                ScrollToCaret();
            }
            finally
            {
                ResumeLayout();
            }
        });

        Task.Run(() =>
        {
            while (true)
            {
                _wake.WaitOne(100); // 有消息时立即唤醒，空闲时最多等 100ms

                if (!IsHandleCreated) continue;

                var batch = new List<Tuple<object, Color?>>();
                while (_messages.TryDequeue(out var tuple))
                {
                    batch.Add(tuple);
                }

                if (batch.Count > 0)
                {
                    progress.Report(batch);
                }
            }
        });
    }

    public async void Print(string message, bool newline = true, bool timestamp = true)
    {
        _msgNewLine = _msgNewLine && newline;
        Print(message, null, timestamp);
    }

    public void Print(string message, Color? color, bool timestamp = true)
    {
        lock (_messages)
        {
            if (_msgNewLine)
            {
                if (!_msgFirstLine)
                    _messages.Enqueue(new(Environment.NewLine, null));
                _msgFirstLine = false;
                if (timestamp)
                    _messages.Enqueue(new(DateTime.Now.ToString("[HH:mm:ss.fff] "), Color.Gray));
            }
            _messages.Enqueue(new(message, color));
            _msgNewLine = true;
        }
        _wake.Set();
    }

    public void ClearLog()
    {
        lock (_messages)
        {
            _messages.Enqueue(new(null, null));
            _msgFirstLine = true;
            _msgNewLine = true;
        }
        _wake.Set();
    }
}