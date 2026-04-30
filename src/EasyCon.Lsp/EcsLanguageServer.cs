using EasyCon.Lsp.Handlers;
using EmmyLua.LanguageServer.Framework.Protocol.Message.Initialize;
using EmmyLua.LanguageServer.Framework.Server;
using System.Net;
using System.Net.Sockets;

namespace EasyCon.Lsp;

public static class EcsLanguageServer
{
    public static async Task RunAsync(Stream input, Stream output)
    {
        var options = LanguageServerOptions.Default;
        var server = new LanguageServer(input, output, options);

        var docManager = new DocumentManager();

        server.AddHandler(new EcsTextDocumentHandler(docManager, server));
        server.AddHandler(new EcsCompletionHandler(docManager));
        server.AddHandler(new EcsHoverHandler(docManager));
        server.AddHandler(new EcsDefinitionHandler(docManager));
        server.AddHandler(new EcsDocumentSymbolHandler(docManager));

        server.OnInitialize(async (request, serverInfo) =>
        {
            serverInfo.Name = "ecs-lsp";
            serverInfo.Version = "0.1.0";
        });

        await server.Run();
    }

    public static async Task RunTcpAsync(string host, int port)
    {
        var listener = new TcpListener(IPAddress.Parse(host), port);
        listener.Start();
        Console.Error.WriteLine($"ecs-lsp: 等待连接 {host}:{port}");

        while (true)
        {
            var client = await listener.AcceptTcpClientAsync();
            _ = HandleTcpClient(client);
        }
    }

    private static async Task HandleTcpClient(TcpClient client)
    {
        var endpoint = client.Client.RemoteEndPoint;
        Console.Error.WriteLine($"ecs-lsp: 客户端已连接 {endpoint}");
        try
        {
            using var stream = client.GetStream();
            await RunAsync(stream, stream);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ecs-lsp: 连接断开 {endpoint} — {ex.Message}");
        }
    }
}