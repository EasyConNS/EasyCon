namespace EasyCon.Core.Assist;

public enum WS_Role
{
    Server,
    Client,
    Other,
}
public enum WS_Event
{
    LogReq,
    LogRes,
    Notifiy,
    CommandReq,
    CommandRes
}

public class Role
{
    public WS_Role role { get; set; }
    public String token { get; set; }

    public Role(WS_Role role,string t)
    {
        this.role = role;
        token = t;
    }
}

public class WS_Message
{
    public Role ws_Role { get; set; }
    public WS_Event ws_Event { get; set; }
    public String data { get; set; }
}

public class LogResponse : WS_Message
{
    public LogResponse(String token,String log)
    {
        ws_Role = new Role(WS_Role.Client,token);
        ws_Event = WS_Event.LogRes;
        data = log;
    }
}

public class Notification : WS_Message
{
    public Notification(String token, String msg)
    {
        ws_Role = new Role(WS_Role.Client, token);
        ws_Event = WS_Event.Notifiy;
        data = msg;
    }
}

public class CommandReq : WS_Message
{
    public CommandReq(String token, String msg)
    {
        ws_Role = new Role(WS_Role.Client,"");
        ws_Event = WS_Event.CommandReq;
        data = msg;
    }
}

//public class Ack : WS_Message
//{
//    Ack(String token, String msg)
//    {
//        ws_Role = new Role(WS_Role.Client, token);
//        ws_Event = WS_Event.Notifiy;
//        data = msg;
//    }
//}
