using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;

namespace PokemonTycoon
{
    public partial class Form1 : Form
    {
        class TabScripting : TabModule, IScriptOutput
        {
            Form1 form;

            Thread _thread = null;
            bool _newMessage = true;
            const string LogPath = @"Log\";
            StreamWriter writer;

            public TabScripting(Form1 form)
            {
                this.form = form;
                Directory.CreateDirectory(LogPath);
                writer = new StreamWriter(new FileStream($"{LogPath}Log {DateTime.Now.ToString("MM_dd HH_mm_ss")}.txt", FileMode.Create));
            }

            public override void Activate()
            { }

            public void Log(object message)
            {
                writer.Write(DateTime.Now.ToString("[HH:mm:ss.fff] "));
                writer.WriteLine(message);
                writer.Flush();
            }

            public void PushMessage(object message = null, Color? color = null)
            {
                form.Invoke((Action)delegate
                {
                    if (message == null)
                    {
                        _newMessage = true;
                        return;
                    }
                    var box = form.richTextBoxScriptingMessage;
                    if (_newMessage)
                    {
                        _newMessage = false;
                        if (box.TextLength > 0)
                            PushMessage(Environment.NewLine);
                        PushMessage(DateTime.Now.ToString("[HH:mm:ss.fff] "), Color.Gray);
                        while (box.TextLength >= 100000)
                        {
                            box.Select(0, box.GetFirstCharIndexFromLine(1));
                            box.ReadOnly = false;
                            box.SelectedText = string.Empty;
                            box.ReadOnly = true;
                        }
                    }
                    box.SelectionStart = box.TextLength;
                    box.SelectionLength = 0;
                    box.SelectionColor = color ?? box.ForeColor;
                    box.AppendText(message.ToString());
                    box.ScrollToCaret();
                });
            }

            public void Summary(string message)
            {
                form.Invoke((Action)delegate
                {
                    form.richTextBoxScriptingSummary.Text = message;
                });
            }

            public void Light(Color color)
            {
                form.Invoke((Action)delegate
                {
                    form.labelTestLight.BackColor = color;
                });
            }

            public void Run(Script script)
            {
                Stop();
                _thread = new Thread(new ThreadStart(() =>
                {
                    try
                    {
                        script.Run(this);
                    }
                    catch (Exception ex)
                    {
                        PushMessage($"{ex.GetType().Name}:", Color.OrangeRed);
                        PushMessage();
                        PushMessage(ex.Message, Color.OrangeRed);
                        PushMessage();
                    }
                }));
                _thread.IsBackground = true;
                _thread.Start();
            }

            public void Stop()
            {
                if (_thread == null)
                    return;
                _thread.Abort();
                _thread = null;
                Light(Color.White);
            }

            public bool IsRunning()
            {
                return _thread != null;
            }

            public void Test()
            {
                if (IsRunning())
                {
                    Stop();
                    return;
                }
                Run(new ScTest());
            }
        }
    }

    interface IScriptOutput
    {
        void Log(object message);
        void PushMessage(object message = null, Color? color = null);
        void Summary(string message);
        void Light(Color color);
    }
}
