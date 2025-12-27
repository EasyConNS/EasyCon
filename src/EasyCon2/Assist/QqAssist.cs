using System.Diagnostics;
using System.Text.Json;
using WebSocketSharp;

namespace EasyCon2.Assist;

public class QqAssist(string token = "")
{
    WebSocket webSocket;
    //string webPath = "ws://127.0.0.1:43963";
    string webPath = "ws://106.52.245.228:43963";
    private string Token { get; set; } = token;

    public void Connect()
    {
        webSocket = new WebSocket(webPath);
        webSocket.Connect();
        webSocket.OnMessage += (sender, e) =>
        {
            Debug.WriteLine(e.Data); //接收到消息并处理
        };
    }

    public bool IsConnect => webSocket.IsAlive;

    public void SendMsg(string s) => webSocket?.Send(s);

    public void SendNotify(string msg)
    {
        SendMsg(new Notification(Token, msg));
    }

    public void SendLogResp(string msg)
    {
        SendMsg(new LogResponse(Token, msg));
    }

    private void SendMsg(WS_Message ws_Message)
    {
        try
        {
            string json = JsonSerializer.Serialize(ws_Message);
            Debug.WriteLine(json);
            webSocket?.Send(json);//发送消息的函数
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }
}

public enum WS_Role
{
    Server,
    Client,
    Other,
}
public enum WS_Event
{
    LogReq,
    LogResp,
    Notifiy,
    CommandReq,
    CommandRes
}

class Role
{
    public WS_Role role { get; set; }
    public string token { get; set; }

    public Role(WS_Role role, string t)
    {
        this.role = role;
        token = t;
    }
}

abstract class WS_Message
{
    public Role ws_Role { get; set; }
    public WS_Event ws_Event { get; set; }
    public string data { get; set; }
}

class LogResponse : WS_Message
{
    public LogResponse(string token, string log)
    {
        ws_Role = new Role(WS_Role.Client, token);
        ws_Event = WS_Event.LogResp;
        data = log;
    }
}

class Notification : WS_Message
{
    public Notification(string token, string msg)
    {
        ws_Role = new Role(WS_Role.Client, token);
        ws_Event = WS_Event.Notifiy;
        data = msg;
    }
}

class CommandReq : WS_Message
{
    public CommandReq(string token, string msg)
    {
        ws_Role = new Role(WS_Role.Client, "");
        ws_Event = WS_Event.CommandReq;
        data = msg;
    }
}

