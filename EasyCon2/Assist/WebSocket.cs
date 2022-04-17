using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using WebSocketSharp;

namespace EasyCon2.Assist
{
    public class Web_Socket
    {
        WebSocket webSocket;
        string webPath = "ws://127.0.0.1:43963";
        public Web_Socket()
        {
            webSocket = new WebSocket(webPath);
            webSocket.Connect();
            webSocket.OnMessage += (sender, e) =>
            {
                Debug.WriteLine(e.Data.ToString());
                //接收到消息并处理
            };
        }

        public void SendMsg(String s)
        {
            webSocket.Send(s);//发送消息的函数
        }

        public void SendMsg(WS_Message ws_Message)
        {
            String json = JsonSerializer.Serialize(ws_Message);
            Debug.WriteLine(json);
            webSocket.Send(json);//发送消息的函数
        }
    }
}
