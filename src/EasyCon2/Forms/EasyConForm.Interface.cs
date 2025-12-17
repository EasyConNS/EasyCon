using EasyCon2.Assist;
using System.Net.Http;

namespace EasyCon2.Forms;

partial class EasyConForm
{
    private bool _msgNewLine = true;
    private bool _msgFirstLine = true;
    private void Print(string message, Color? color, bool timestamp = true)
    {
        lock (_messages)
        {
            if (_msgNewLine)
            {
                if (!_msgFirstLine)
                    _messages.Enqueue(new Tuple<RichTextBox, object, Color?>(richTextBoxMessage, Environment.NewLine, null));
                _msgFirstLine = false;
                if (timestamp)
                    _messages.Enqueue(new Tuple<RichTextBox, object, Color?>(richTextBoxMessage, DateTime.Now.ToString("[HH:mm:ss.fff] "), Color.Gray));
            }
            _messages.Enqueue(new Tuple<RichTextBox, object, Color?>(richTextBoxMessage, message, color));
            _msgNewLine = true;
        }
    }

    public void Print(string message, bool newline = true)
    {
        lock (_messages)
        {
            _msgNewLine = _msgNewLine && newline;
            Print(message, null);
        }
    }

    public void Alert(string message)
    {
        Task.Run(() =>
        {
            bool canPush = false;
            try
            {
                if (_config.AlertToken != "")
                {
                    var address = $"https://www.pushplus.plus/send/{_config.AlertToken}?content={message}&title=伊机控消息";
                    using var client = new HttpClient();
                    var result = client.GetAsync(address).Result.Content.ReadAsStringAsync().Result;
                    Print(result);
                    canPush = true;
                }

                if (_config.ChannelToken != "")
                {
                    Notification notification = new(_config.ChannelToken, message);
                    ws?.SendMsg(notification);
                    canPush = true;
                }
            }
            catch (Exception e)
            {
                Print($"推送失败:{e.Message}");
            }
            if (!canPush)
            {
                Print("推送失败: 请配置推送Token");
            }
        });
    }
}
