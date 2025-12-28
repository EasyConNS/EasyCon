using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace EasyCon.Core.Assist;

public sealed class AssistClient
{
    private readonly ClientWebSocket ws;
    private readonly Uri _uri = new("ws://106.52.245.228:43963");

    public AssistClient()
    {
        ws.ConnectAsync(_uri, default);
        //var bytes = new byte[1024];
        //var result = await ws.ReceiveAsync(bytes, default);
        //string res = Encoding.UTF8.GetString(bytes, 0, result.Count);
        //await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closed", default);
    }

    public bool IsConnect() => ws.State == WebSocketState.Open;

    public async void SendMsg(WS_Message ws_Message)
    {
        var json = JsonSerializer.Serialize(ws_Message);
        Debug.WriteLine(json);
        var buf = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(new ArraySegment<byte>(buf), WebSocketMessageType.Text, true, CancellationToken.None);
    }
}
