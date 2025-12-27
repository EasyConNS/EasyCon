using System.Diagnostics;

namespace EasyCon2.Forms;

internal class RichLogBox : RichTextBox
{
    private readonly Queue<Tuple<object, Color?>> _messages = new();

    private bool _msgNewLine = true;
    private bool _msgFirstLine = true;

    public RichLogBox()
    {
        HandleCreated += (s, arg) =>
        {
            //
        };

        Task.Run(() =>
        {
            while (true)
            {
                if (!IsHandleCreated) continue;
                // Invoke or BeginInvoke?
                BeginInvoke(new Action(() =>
                {
                    var once = false;
                    while (_messages.Count > 0)
                    {
                        Tuple<object, Color?> tuple;
                        lock (_messages)
                        {
                            tuple = _messages.Dequeue();
                        }
                        var message = tuple.Item1;
                        var color = tuple.Item2;
                        if (!once)
                        {
                            SuspendLayout();
                            once = true;
                        }
                        if (message == null)
                        {
                            // cls
                            Text = string.Empty;
                            continue;
                        }
                        while (TextLength >= 1000000)
                        {
                            this.Select(0, GetFirstCharIndexFromLine(10));
                            ReadOnly = false;
                            SelectedText = string.Empty;
                            ReadOnly = true;
                        }
                        SelectionStart = TextLength;
                        SelectionLength = 0;
                        SelectionColor = color ?? ForeColor;
                        AppendText(message.ToString());
                        Debug.WriteLine("-" + message.ToString());
                    }

                    if (once)
                    {
                        ScrollToCaret();
                        ResumeLayout();
                        Invalidate();
                    }
                }
                ));

                Thread.Sleep(25);
            }
        });
    }

    public void Print(string message, bool newline = true, bool timestamp = true)
    {
        lock (_messages)
        {
            _msgNewLine = _msgNewLine && newline;
            Print(message, null, timestamp);
        }
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
    }

    public void ClearLog()
    {
        lock (_messages)
        {
            _messages.Enqueue(new(null, null));
            _msgFirstLine = true;
            _msgNewLine = true;
        }
    }
}
