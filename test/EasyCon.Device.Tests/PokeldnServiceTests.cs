using EasyDevice;
using EasyDevice.Connection;
using EasyDevice.Connection.Pokeldn;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace EasyCon.Device.Tests;

[TestFixture]
public class PokeldnServiceTests
{
    [Test]
    public async Task S3Service_UsesOfficialJsonProtocolThroughRealDeviceLoop()
    {
        using PadService server = new();
        ServiceSwitch device = new(server.Port);
        Assert.That(device.TryConnect(ECDevice.PokeldnServiceEndpoint), Is.EqualTo(NintendoSwitch.ConnectResult.Success));
        try
        {
            Assert.That(device.FirmwareVersion, Is.EqualTo("1.3.0"));
            Assert.That(device.ConsoleConnected, Is.True);
            device.ApplyReport(new SwitchReport { Button = (ushort)SwitchButton.PLUS, LX = 255, RY = 0 });
            Assert.That(SpinWait.SpinUntil(() => server.Reports.Contains("000208FF80800000"), 2000), Is.True);
            Assert.That(await device.ResetAndWaitForWriteAsync(TimeSpan.FromSeconds(2)), Is.True);
            Assert.That(SpinWait.SpinUntil(() => server.Operations.Count(op => op == "stop") >= 2, 2000), Is.True);
        }
        finally { device.Disconnect(); }
        Assert.That(server.Operations.Take(4), Is.EqualTo(new[] { "connect", "status", "stop", "send" }));
        Assert.That(server.Operations.Last(), Is.EqualTo("disconnect"));
        Assert.That(server.Reports.Last(), Is.EqualTo("0000088080808000"));
    }

    [Test]
    public void RefusedCommand_PropagatesServiceError()
    {
        using PadService server = new() { RefuseSend = true };
        using PokeldnServiceTransport transport = new(server.Port);
        transport.Connect(CancellationToken.None);
        IOException? error = Assert.Throws<IOException>(() => transport.SendReport(new byte[8], CancellationToken.None));
        Assert.That(error!.Message, Does.Contain("board refused"));
    }

    [Test]
    public void ServiceWithNoBoard_DoesNotClaimConnected()
    {
        using PadService server = new() { BoardConnected = false };
        using PokeldnServiceTransport transport = new(server.Port);
        Assert.Throws<IOException>(() => transport.Connect(CancellationToken.None));
    }

    [Test]
    public void CanceledBleScan_DoesNotLeaveAConnectionWorkerWaiting()
    {
        using PadService server = new() { HangConnect = true };
        using PokeldnServiceTransport transport = new(server.Port);
        using CancellationTokenSource cancel = new(200);
        Assert.Throws<OperationCanceledException>(() => transport.Connect(cancel.Token));
    }

    [Test]
    public void MissingService_GivesActionableMessage()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int unusedPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using PokeldnServiceTransport transport = new(unusedPort);
        IOException? error = Assert.Throws<IOException>(() => transport.Connect(CancellationToken.None));
        Assert.That(error!.Message, Does.Contain("pokeldn.pad.service"));
    }

    [Test]
    public void DisconnectDuringFragmentedAck_DrainsReplyBeforeNeutralAndDisconnect()
    {
        using PadService server = new() { DelayLiveReportAck = true };
        ServiceSwitch device = new(server.Port);
        Assert.That(device.TryConnect(ECDevice.PokeldnServiceEndpoint), Is.EqualTo(NintendoSwitch.ConnectResult.Success));
        device.Down(ECKeyUtil.Button(SwitchButton.A));
        Assert.That(server.LiveReportAckStarted.Wait(2000), Is.True);
        device.Disconnect();
        Assert.That(server.Reports.Last(), Is.EqualTo("0000088080808000"));
        Assert.That(server.Operations.Last(), Is.EqualTo("disconnect"));
        Assert.That(SpinWait.SpinUntil(() => server.DisconnectAcknowledged, 1000), Is.True);
    }

    private sealed class ServiceSwitch(int port) : NintendoSwitch
    {
        protected override IConnection CreatePokeldnConnection(string connStr)
            => new PokeldnConnection(connStr, new PokeldnServiceTransport(port));
    }

    private sealed class PadService : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cancel = new();
        private readonly Task _server;
        public ConcurrentQueue<string> Operations { get; } = new();
        public ConcurrentQueue<string> Reports { get; } = new();
        public bool RefuseSend { get; init; }
        public bool BoardConnected { get; init; } = true;
        public bool HangConnect { get; init; }
        public bool DelayLiveReportAck { get; init; }
        public ManualResetEventSlim LiveReportAckStarted { get; } = new(false);
        public bool DisconnectAcknowledged { get; private set; }
        public int Port { get; }

        public PadService()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _server = Task.Run(Serve);
        }

        private async Task Serve()
        {
            try
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(_cancel.Token);
                using NetworkStream stream = client.GetStream();
                using StreamReader reader = new(stream, Encoding.UTF8, false, 1024, true);
                while (await reader.ReadLineAsync(_cancel.Token) is string line)
                {
                    using JsonDocument request = JsonDocument.Parse(line);
                    string op = request.RootElement.GetProperty("op").GetString()!;
                    Operations.Enqueue(op);
                    if (op == "connect" && HangConnect)
                        await Task.Delay(Timeout.Infinite, _cancel.Token);
                    string reply = "{\"ok\":true}";
                    if (op == "status")
                        reply = BoardConnected
                            ? "{\"ok\":true,\"connected\":true,\"link\":\"bluetooth\",\"status\":{\"mounted\":true,\"playing\":false,\"version\":\"1.3.0\"}}"
                            : "{\"ok\":true,\"connected\":false}";
                    if (op == "send")
                    {
                        Reports.Enqueue(request.RootElement.GetProperty("report").GetString()!);
                        if (RefuseSend)
                            reply = "{\"ok\":false,\"error\":\"board refused\"}";
                    }
                    // Exercise a fragmented response too; newline, not a TCP packet, delimits it.
                    byte[] bytes = Encoding.UTF8.GetBytes(reply + "\n");
                    await stream.WriteAsync(bytes.AsMemory(0, 2), _cancel.Token);
                    if (DelayLiveReportAck && op == "send" && Reports.Last() == "0400088080808000")
                    {
                        LiveReportAckStarted.Set();
                        await Task.Delay(100, _cancel.Token);
                    }
                    await stream.WriteAsync(bytes.AsMemory(2), _cancel.Token);
                    if (op == "disconnect")
                        DisconnectAcknowledged = true;
                }
            }
            catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { }
        }

        public void Dispose()
        {
            _cancel.Cancel();
            _listener.Stop();
            Assert.That(_server.Wait(2000), Is.True);
            _cancel.Dispose();
        }
    }
}