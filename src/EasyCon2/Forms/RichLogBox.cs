using System.Collections.Concurrent;
using System.Diagnostics;

namespace EasyCon2.Forms;

internal class RichLogBox : RichTextBox
{
    private readonly ConcurrentQueue<Tuple<object, Color?>> _messages = new();

    private bool _msgNewLine = true;
    private bool _msgFirstLine = true;

    public RichLogBox()
    {
        HandleCreated += (s, arg) =>
        {
            //
        };
        IProgress<Tuple<object, Color?>> progress = new Progress<Tuple<object, Color?>>(tuple =>
        {
            var msg = tuple.Item1;
            var color = tuple.Item2;

            SelectionStart = TextLength;
            SelectionLength = 0;
            SelectionColor = color ?? ForeColor;
            if (msg == null)
            {
                Text = string.Empty;
            }
            else
            {
                AppendText(msg.ToString());
            }

            ScrollToCaret();
            //ResumeLayout();
            //Invalidate();
        });

        Task.Run(() =>
        {
            while (true)
            {
                if (!IsHandleCreated) continue;
                // Invoke or BeginInvoke?
                //BeginInvoke(new Action(() =>
                //{
                //var once = false;
                while (_messages.Count > 0)
                {
                    if(!_messages.TryDequeue(out var tuple))
                    {
                        continue;
                    }
                    var message = tuple.Item1;
                    var color = tuple.Item2;
                    //if (!once)
                    //{
                    //    SuspendLayout();
                    //    once = true;
                    //}
                    if (message == null)
                    {
                        // cls
                        //Text = string.Empty;
                        progress.Report(tuple);
                        continue;
                    }
                    //while (TextLength >= 1000000)
                    //{
                    //    //this.Select(0, GetFirstCharIndexFromLine(10));
                    //    ReadOnly = false;
                    //    SelectedText = string.Empty;
                    //    ReadOnly = true;
                    //}
                    progress.Report(tuple);
                    Debug.WriteLine("-" + message.ToString());
                }

                //if (once)
                //{
                //    ScrollToCaret();
                //    ResumeLayout();
                //    Invalidate();
                //}

                //));

                Thread.Sleep(25);
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
