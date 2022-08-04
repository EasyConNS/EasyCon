﻿using System.Diagnostics;
using System.Text.Json;
using WebSocketSharp;

namespace EasyCon2.Assist
{
    public class Web_Socket
    {
        WebSocket webSocket;
        //string webPath = "ws://127.0.0.1:43963";
        string webPath = "ws://106.52.245.228:43963";
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

        public void Reconnect()
        {
            webSocket = new WebSocket(webPath);
            webSocket.Connect();
            webSocket.OnMessage += (sender, e) =>
            {
                Debug.WriteLine(e.Data.ToString());
                //接收到消息并处理
            };
        }

        public bool CheckConnect()
        {
            return webSocket.IsAlive;
        }

        public void SendMsg(String s)
        {
            webSocket.Send(s);//发送消息的函数
        }

        public void SendMsg(WS_Message ws_Message)
        {
            try
            {
                String json = JsonSerializer.Serialize(ws_Message);
                Debug.WriteLine(json);
                webSocket.Send(json);//发送消息的函数
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                //Reconnect();
            }

        }
    }
}
