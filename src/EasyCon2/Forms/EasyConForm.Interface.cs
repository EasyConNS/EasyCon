using EasyCon2.Assist;
using EasyDevice;
using EasyScript;
using System.Net.Http;

namespace EasyCon2.Forms;

partial class EasyConForm
{

    #region 脚本功能接口
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
        bool canPush = true;
        try
        {
            if (_config.AlertToken == "")
            {
                canPush = false;
                //Print("pushplus推送Token为空");
            }
            else
            {
                var address = $"https://www.pushplus.plus/send/{_config.AlertToken}?content={message}&title=伊机控消息";
                using var client = new HttpClient();
                var result = client.GetAsync(address).Result.Content.ReadAsStringAsync().Result;
                Print(result);
                canPush = true;
            }

            if (_config.ChannelToken == "")
            {
                canPush = false;
            }
            else
            {
                Notification notification = new Notification(_config.ChannelToken, message);
                ws.SendMsg(notification);
                canPush = true;
            }
        }
        catch (Exception e)
        {
            Print($"推送失败:{e.Message}");
        }


        if (canPush == false)
        {
            Print("推送Token为空");
        }
    }

    void ICGamePad.ClickButtons(ECKey key, int duration)
    {
        NS.Press(key, duration);
    }

    void ICGamePad.PressButtons(ECKey key)
    {
        NS.Down(key);
    }

    void ICGamePad.ReleaseButtons(ECKey key)
    {
        NS.Up(key);
    }

    void ICGamePad.ChangeAmiibo(uint index)
    {
        //Print($"切换Amiibo序号:{index}");
        NS.ChangeAmiiboIndex((byte)(index & 0x0F));
    }
    #endregion
}
