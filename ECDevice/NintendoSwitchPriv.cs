using ECDevice.Arduino;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace ECDevice
{
    public partial class NintendoSwitch
    {

        void Signal()
        {
            if (clientCon == null)
                return;
            //PrintTime();
            else
                _ewh.Set();
        }

        bool SendSync(Func<byte, bool> predicate, int timeout = 100, params byte[] bytes)
        {
            EventWaitHandle ewh = new EventWaitHandle(false, EventResetMode.AutoReset);
            ConClient.BytesTransferedHandler h = (port, bytes_) =>
            {
                foreach (var b in bytes_)
                    if (predicate(b))
                    {
                        ewh.Set();
                        break;
                    }
            };
            try
            {
                clientCon.BytesReceived += h;
                clientCon.Write(bytes);
                if (!ewh.WaitOne(timeout))
                    return false;
                return true;
            }
            finally
            {
                clientCon.BytesReceived -= h;
            }
        }

        bool ResetControl()
        {
            EventWaitHandle ewh = new EventWaitHandle(false, EventResetMode.AutoReset);
            ConClient.BytesTransferedHandler h = (port, bytes_) =>
            {
                if (bytes_.Contains(Reply.Hello))
                    ewh.Set();
            };
            try
            {
                clientCon.BytesReceived += h;
                for (int i = 0; i < 3; i++)
                    clientCon.Write(Command.Ready, Command.Hello);
                return ewh.WaitOne(50);
            }
            finally
            {
                clientCon.BytesReceived -= h;
            }
        }

        void PrintKey(string str, Key key = null)
        {
            str = str + " " + key?.Name ?? "";
            Debug.WriteLine(str);
        }

        private void Direction(DirectionKey dkey, bool down, ref DirectionKey flags, Func<byte, byte, Key> getkey)
        {
            var oldflags = flags;
            if (down)
                flags |= dkey;
            else
                flags &= ~dkey;
            GetXYFromDirection(flags, out byte x, out byte y);
            Down(getkey(x, y));
        }

        private void Loop()
        {
            int sleep = 0;
            while (true)
            {
                if (source.Token.IsCancellationRequested)
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
                    var values = _keystrokes.Values.ToArray();
                    foreach (var ks in values)
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
                    var log = $"[Send {DateTime.Now:ss.fff}] { _report.GetKeyStr()}";
                    Log?.Invoke(log);
                    clientCon.Write(_report.GetBytes());
                    _nextSendTime = DateTime.Now.AddMilliseconds(MINIMAL_INTERVAL);
                    _ewh.Reset();
                }
            }
        }
    }
}
