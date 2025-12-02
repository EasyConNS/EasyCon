namespace EasyDevice;

public partial class NintendoSwitch
{
    const int MINIMAL_INTERVAL = 30;

    DateTime _nextSendTime = DateTime.MinValue;
    private readonly EventWaitHandle _ewh = new(false, EventResetMode.ManualReset);

    void Signal()
    {
        if (this.IsConnected())
            _ewh.Set();
    }

    void Loop(CancellationToken token)
    {
        int sleep = 0;
        while (true)
        {
            if (token.IsCancellationRequested)
                return;
            if (_keystrokes.Count == 0)
                _ewh.WaitOne();
            else
                _ewh.WaitOne(sleep);
            if (DateTime.Now < _nextSendTime)
                Thread.Sleep((int)(_nextSendTime - DateTime.Now).TotalMilliseconds);
            sleep = int.MaxValue;
            lock (this)
            {
                if (_reset)
                {
                    _report.Reset();
                    _reset = false;
                }
                foreach (var ks in _keystrokes.Values.ToArray())
                {
                    if (ks.Time > DateTime.Now)
                    {
                        var n = (int)(ks.Time - DateTime.Now).TotalMilliseconds;
                        if (n > 0 && n < sleep)
                            sleep = n;
                    }
                    else if (ks.Up)
                    {
                        ks.Key.Up(_report);
                        _keystrokes.Remove(ks.KeyCode);
                    }
                    else
                    {
                        ks.Key.Down(_report);
                        if (ks.Duration > 0)
                        {
                            _keystrokes[ks.KeyCode] = new KeyStroke(ks.Key, true, 0, DateTime.Now + TimeSpan.FromMilliseconds(ks.Duration));
                            if (ks.Duration < sleep)
                                sleep = ks.Duration;
                        }
                        else
                            _keystrokes.Remove(ks.KeyCode);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[Send {DateTime.Now:ss.fff}] {_report}");
                Log($"[Send {DateTime.Now:ss.fff}] {_report}");

                WriteReport(_report.GetBytes());
                _nextSendTime = DateTime.Now.AddMilliseconds(MINIMAL_INTERVAL);
                _ewh.Reset();
            }
        }
    }
}
